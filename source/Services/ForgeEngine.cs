using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using ThemeForge.Models;

namespace ThemeForge.Services
{
    /// <summary>
    /// The heart of the plugin: owns the theme list, the resource snapshot and the live
    /// override dictionary, and knows how to turn saved settings into applied resources.
    ///
    /// Layering rules are defined in exactly one place here (<see cref="BuildValues"/>) so the
    /// editor, the preview surface and the startup path can never disagree about what the
    /// effective value of a key is.
    /// </summary>
    public class ForgeEngine
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private readonly ThemeRepository themes;
        private readonly ResourceRegistry registry = new ResourceRegistry();
        private readonly ResourceApplier live = new ResourceApplier();
        private ForgeSettings settings;

        public ForgeEngine(IPlayniteAPI api, ForgeSettings settings)
        {
            this.api = api;
            this.settings = settings;
            themes = new ThemeRepository(api);
        }

        public IPlayniteAPI Api
        {
            get { return api; }
        }

        /// <summary>
        /// Live settings instance. The settings view model edits a clone and copies the result
        /// back into this very object, so the reference stays stable for the plugin's lifetime.
        /// </summary>
        public ForgeSettings Settings
        {
            get { return settings; }
            set { settings = value ?? new ForgeSettings(); }
        }

        public ThemeRepository Themes
        {
            get { return themes; }
        }

        public ResourceRegistry Registry
        {
            get { return registry; }
        }

        public ResourceApplier Live
        {
            get { return live; }
        }

        public ThemeDescriptor ActiveTheme
        {
            get { return themes.Active; }
        }

        /// <summary>
        /// Discovers themes, snapshots the untouched resource tree, then attaches the override
        /// dictionary and applies whatever the user saved for the active theme.
        ///
        /// Order matters: the snapshot has to happen while our own dictionary is not attached,
        /// otherwise a previously saved override would be recorded as the theme's baseline and
        /// "reset" would restore the override instead of the theme default.
        /// </summary>
        public void Initialize()
        {
            themes.Refresh();
            registry.Capture(live.Root);
            live.Attach();
            ApplyActive();
        }

        /// <summary>Re-reads theme folders and the resource tree, keeping overrides applied.</summary>
        public void Reload()
        {
            themes.Refresh();

            // Detaching first keeps our own overrides out of the captured baseline.
            var wasAttached = live.IsAttached;
            live.Detach();
            registry.Capture(live.Root);

            if (wasAttached)
            {
                live.Attach();
            }

            ApplyActive();
        }

        /// <summary>
        /// Effective overrides for a theme, lowest priority first:
        ///   1. constants contributed by the selected presets
        ///   2. values for options the theme declared
        ///   3. free-form resource overrides
        /// A later layer replaces an earlier one for the same key.
        /// </summary>
        public VariablesValues BuildValues(ThemeDescriptor theme, ThemeState state)
        {
            var result = new VariablesValues();
            if (state == null)
            {
                return result;
            }

            if (theme != null && theme.Options != null && theme.Options.Presets != null)
            {
                Merge(result, theme.Options.Presets.GetConstants(state.SelectedPresets));
            }

            Merge(result, state.Variables);
            Merge(result, state.Resources);

            return result;
        }

        private static void Merge(VariablesValues target, VariablesValues source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var pair in source)
            {
                if (pair.Value == null || string.IsNullOrEmpty(pair.Value.Value))
                {
                    continue;
                }

                target[pair.Key] = new VariableValue { Type = pair.Value.Type, Value = pair.Value.Value };
            }
        }

        /// <summary>
        /// Best known CLR type for a key: what the theme declared, then what the running
        /// application actually holds. Without the declared type a theme could not introduce an
        /// option for a resource it only defines in a preset file that is not currently merged.
        /// </summary>
        public Type TypeOf(ThemeDescriptor theme, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (theme != null && theme.Options != null && theme.Options.Variables != null)
            {
                var declared = theme.Options.Variables.Get(key);
                if (declared != null)
                {
                    var resolved = ValueConverter.ResolveType(declared.Type);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
            }

            return registry.TypeOf(key, null);
        }

        /// <summary>
        /// Value a key falls back to when the user has not overridden it: the constant supplied
        /// by the selected preset, then the theme's declared default, then the value captured
        /// from the untouched theme.
        /// </summary>
        public string BaselineOf(ThemeDescriptor theme, string key, VariablesValues presetConstants)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (presetConstants != null)
            {
                var fromPreset = presetConstants.Get(key);
                if (fromPreset != null && !string.IsNullOrEmpty(fromPreset.Value))
                {
                    return fromPreset.Value;
                }
            }

            if (theme != null && theme.Options != null && theme.Options.Variables != null)
            {
                var declared = theme.Options.Variables.Get(key);
                if (declared != null && !string.IsNullOrEmpty(declared.Default))
                {
                    return declared.Default;
                }
            }

            var entry = registry.Find(key);
            if (entry != null)
            {
                return entry.BaselineValue;
            }

            return ValueConverter.Format(ResourceRegistry.Live(key));
        }

        /// <summary>Applies the saved state of the active theme to the running application.</summary>
        public void ApplyActive()
        {
            var theme = themes.Active;
            if (theme == null)
            {
                logger.Warn("Theme Forge: active theme not found, nothing applied.");
                live.Clear();
                return;
            }

            Apply(live, theme, settings.State(theme.Id));
        }

        /// <summary>
        /// Pushes a theme state into the given applier. Used both for the live application and
        /// for the preview surface, which guarantees the preview cannot drift from reality.
        /// </summary>
        public void Apply(ResourceApplier applier, ThemeDescriptor theme, ThemeState state)
        {
            if (applier == null || theme == null || state == null)
            {
                return;
            }

            var files = theme.Options != null && theme.Options.Presets != null
                ? theme.Options.Presets.GetResourceFiles(state.SelectedPresets)
                : new List<string>();

            applier.SetFiles(theme.RootPath, files);

            var values = BuildValues(theme, state);
            var changed = applier.ApplyValues(values, key => TypeOf(theme, key));

            if (changed > 0)
            {
                logger.Debug("Theme Forge: applied " + changed + " changed resource(s) of " + values.Count + " override(s).");
            }
        }

        /// <summary>Add-ons a theme wants that are not installed, honouring the notify setting.</summary>
        public List<string> MissingExtensions(ThemeDescriptor theme)
        {
            return themes.MissingExtensions(theme, true);
        }

        /// <summary>Keys the theme declared that nothing in the running application answers to.</summary>
        public List<string> UnresolvedKeys(ThemeDescriptor theme)
        {
            var missing = new List<string>();
            if (theme == null || theme.Options == null || theme.Options.Variables == null)
            {
                return missing;
            }

            foreach (var pair in theme.Options.Variables)
            {
                if (registry.Find(pair.Key) == null && ResourceRegistry.Live(pair.Key) == null)
                {
                    missing.Add(pair.Key);
                }
            }

            return missing;
        }

        public void Shutdown()
        {
            live.Detach();
        }
    }
}
