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
        private readonly DictNoCase<ResourceGraph> graphs = new DictNoCase<ResourceGraph>();
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

            // Theme folders may have changed on disk, so the parsed derivation graphs are stale.
            graphs.Clear();

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

        /// <summary>
        /// Add-ons the theme declares as hard requirements and that are not installed. Split out
        /// from the full list so the UI can tell "this will break" apart from "this is optional";
        /// most themes list far more nice-to-haves than actual requirements.
        /// </summary>
        public List<string> MissingRequiredExtensions(ThemeDescriptor theme)
        {
            return themes.MissingExtensions(theme, false);
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

        /// <summary>
        /// Diagnoses the individual overrides saved for a theme against its selected presets.
        ///
        /// The layering (preset &lt; variable &lt; resource override) is deliberate, but an override the
        /// user forgot about - or one imported from a legacy plugin - silently defeats every preset
        /// they pick afterwards, which reads as "the preset does nothing". Three situations are
        /// reported: a preset declares the same key, a preset declares a key the override is derived
        /// from, or the override merely restates the theme's own default.
        /// </summary>
        public List<ShadowFinding> ShadowedPresetKeys(ThemeDescriptor theme, ThemeState state)
        {
            var result = new List<ShadowFinding>();
            if (theme == null || state == null)
            {
                return result;
            }

            var supplied = SuppliedPresetKeys(theme, state);
            var graph = supplied.Count > 0 ? GraphFor(theme) : null;

            // Redundancy is measured against the captured baseline, and that snapshot only
            // describes the theme that is actually loaded. For any other theme it says nothing.
            var active = themes.Active;
            var judgeRedundant = active != null &&
                string.Equals(active.Id, theme.Id, StringComparison.OrdinalIgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in state.Resources)
            {
                Diagnose(result, seen, supplied, graph, judgeRedundant, pair.Key, pair.Value);
            }

            foreach (var pair in state.Variables)
            {
                Diagnose(result, seen, supplied, graph, judgeRedundant, pair.Key, pair.Value);
            }

            result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
            return result;
        }

        /// <summary>Classifies one override; the first matching reason wins.</summary>
        private void Diagnose(List<ShadowFinding> result, HashSet<string> seen, HashSet<string> supplied,
            ResourceGraph graph, bool judgeRedundant, string key, VariableValue entry)
        {
            if (string.IsNullOrEmpty(key) || !seen.Add(key))
            {
                return;
            }

            if (supplied.Contains(key))
            {
                result.Add(new ShadowFinding(key, ShadowReason.Direct, null));
                return;
            }

            if (graph != null)
            {
                var via = FirstSupplied(supplied, graph.Closure(key));
                if (via != null)
                {
                    result.Add(new ShadowFinding(key, ShadowReason.Derived, via));
                    return;
                }
            }

            if (judgeRedundant && IsRedundant(key, entry))
            {
                result.Add(new ShadowFinding(key, ShadowReason.Redundant, null));
            }
        }

        /// <summary>Lowest name among the supplied keys, so the reported cause never flickers.</summary>
        private static string FirstSupplied(HashSet<string> supplied, HashSet<string> closure)
        {
            string best = null;
            foreach (var candidate in closure)
            {
                if (supplied.Contains(candidate) &&
                    (best == null || StringComparer.OrdinalIgnoreCase.Compare(candidate, best) < 0))
                {
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Keys the selected presets contribute. Preset files are inspected as well as their inline
        /// constants, because most colour presets ship their keys in a xaml file.
        /// </summary>
        private static HashSet<string> SuppliedPresetKeys(ThemeDescriptor theme, ThemeState state)
        {
            var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (theme.Options == null || theme.Options.Presets == null)
            {
                return supplied;
            }

            var presets = theme.Options.Presets;
            var constants = presets.GetConstants(state.SelectedPresets);
            if (constants != null)
            {
                foreach (var pair in constants)
                {
                    supplied.Add(pair.Key);
                }
            }

            foreach (var file in presets.GetResourceFiles(state.SelectedPresets))
            {
                var dictionary = ResourceApplier.LoadXamlFile(theme.RootPath, file);
                if (dictionary == null)
                {
                    continue;
                }

                foreach (var key in dictionary.Keys.OfType<string>())
                {
                    supplied.Add(key);
                }
            }

            return supplied;
        }

        /// <summary>
        /// Derivation graph for a theme, cached by folder. Diagnostics are rebuilt after every
        /// value change, and re-parsing a hundred xaml files per slider tick is not acceptable.
        /// </summary>
        private ResourceGraph GraphFor(ThemeDescriptor theme)
        {
            if (theme == null || string.IsNullOrEmpty(theme.RootPath))
            {
                return null;
            }

            var graph = graphs.Get(theme.RootPath);
            if (graph == null)
            {
                graph = new ResourceGraph();
                graph.Build(theme.RootPath);
                graphs[theme.RootPath] = graph;
            }

            return graph;
        }

        /// <summary>True when an override formats to exactly the value the theme already declares.</summary>
        private bool IsRedundant(string key, VariableValue entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Value))
            {
                return false;
            }

            var known = registry.Find(key);
            if (known == null || string.IsNullOrEmpty(known.BaselineValue))
            {
                return false;
            }

            object parsed;
            try
            {
                parsed = string.IsNullOrEmpty(entry.Type)
                    ? ValueConverter.ParseAs(known.ValueType, entry.Value)
                    : ValueConverter.Parse(entry.Type, entry.Value);
            }
            catch (Exception)
            {
                return false;
            }

            if (parsed == null)
            {
                return false;
            }

            return string.Equals(ValueConverter.Format(parsed), known.BaselineValue, StringComparison.OrdinalIgnoreCase);
        }

        public void Shutdown()
        {
            live.Detach();
        }
    }
}
