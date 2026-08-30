using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace ThemeForge.Models
{
    public class ThemeLink
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    /// An installed theme plus every option schema it exposes.
    ///
    /// Three declaration formats are merged, in descending priority:
    ///   1. themeforge.yaml    - native format (presets, typed variables, groups)
    ///   2. options.yaml       - ThemeOptions format
    ///   3. thememodifier.yaml - ThemeModifier format
    /// A theme therefore keeps working unchanged, and a theme that wants the richer
    /// editor only has to add themeforge.yaml.
    /// </summary>
    public class ThemeDescriptor
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string ThemeApiVersion { get; set; }
        public List<ThemeLink> Links { get; set; }

        [DontSerialize]
        public string RootPath { get; set; }

        [DontSerialize]
        public string Mode { get; set; }

        [DontSerialize]
        public ThemeOptionsSchema Options { get; set; }

        [DontSerialize]
        public ThemeExtensionRequirements Extensions { get; set; }

        [DontSerialize]
        public bool HasNativeSchema { get; set; }

        [DontSerialize]
        public bool HasThemeOptionsSchema { get; set; }

        [DontSerialize]
        public bool HasLegacySchema { get; set; }

        [DontSerialize]
        public bool HasPresets
        {
            get { return Options != null && Options.Presets != null && Options.Presets.Count > 0; }
        }

        [DontSerialize]
        public bool HasVariables
        {
            get { return Options != null && Options.Variables != null && Options.Variables.Count > 0; }
        }

        [DontSerialize]
        public bool IsConfigurable
        {
            get { return HasPresets || HasVariables; }
        }

        [DontSerialize]
        public string DisplayName
        {
            get
            {
                var label = Name;
                if (!string.IsNullOrEmpty(Version))
                {
                    label += " " + Version;
                }

                return label;
            }
        }

        private static readonly ILogger logger = LogManager.GetLogger();

        public static ThemeDescriptor FromDirectory(string directory, string mode)
        {
            var manifest = Path.Combine(directory, "theme.yaml");
            if (!File.Exists(manifest))
            {
                return null;
            }

            ThemeDescriptor theme;
            try
            {
                theme = Serialization.FromYamlFile<ThemeDescriptor>(manifest);
            }
            catch (System.Exception e)
            {
                logger.Error(e, "Theme Forge: failed to read " + manifest);
                return null;
            }

            if (theme == null || string.IsNullOrEmpty(theme.Id))
            {
                return null;
            }

            theme.RootPath = directory;
            theme.Mode = mode;
            theme.Options = new ThemeOptionsSchema();

            var native = ThemeOptionsSchema.FromFile(Path.Combine(directory, "themeforge.yaml"));
            if (native != null)
            {
                theme.HasNativeSchema = true;
                theme.Options.MergeFrom(native);
            }

            var themeOptions = ThemeOptionsSchema.FromFile(Path.Combine(directory, "options.yaml"));
            if (themeOptions != null)
            {
                theme.HasThemeOptionsSchema = true;
                theme.Options.MergeFrom(themeOptions);
            }

            var legacy = LegacyConstantsSchema.FromFile(Path.Combine(directory, "thememodifier.yaml"));
            if (legacy != null)
            {
                theme.HasLegacySchema = true;
                theme.Options.MergeFrom(legacy.ToSchema());
            }

            theme.Extensions = ThemeExtensionRequirements.FromFile(Path.Combine(directory, "extensions.yaml"));
            return theme;
        }

        /// <summary>
        /// Resolves LocKey references on variables, groups and presets. The theme's own
        /// dictionary is consulted first so a theme can ship translations for its options
        /// without touching Playnite's string tables.
        /// </summary>
        public void Localize(string language)
        {
            if (Options == null)
            {
                return;
            }

            var dictionary = ThemeForge.Localization.LoadDictionary(RootPath, language);
            if (dictionary == null && !string.IsNullOrEmpty(language) && language != "en_US")
            {
                dictionary = ThemeForge.Localization.LoadDictionary(RootPath, "en_US");
            }

            if (Options.Variables != null)
            {
                foreach (var pair in Options.Variables)
                {
                    var variable = pair.Value;
                    variable.Title = Resolve(dictionary, variable.LocKey, variable.Title ?? pair.Key);
                    variable.Description = Resolve(dictionary, variable.DescriptionLocKey, variable.Description);
                    variable.Group = Resolve(dictionary, variable.GroupLocKey, variable.Group);

                    if (variable.Choices != null)
                    {
                        foreach (var choice in variable.Choices)
                        {
                            choice.Title = Resolve(dictionary, choice.LocKey, choice.Title ?? choice.Value);
                        }
                    }
                }
            }

            if (Options.Groups != null)
            {
                foreach (var group in Options.Groups)
                {
                    group.Title = Resolve(dictionary, group.LocKey, group.Title ?? group.Id);
                    group.Description = Resolve(dictionary, group.DescriptionLocKey, group.Description);
                }
            }

            if (Options.Presets != null)
            {
                foreach (var pair in Options.Presets.Enumerate())
                {
                    pair.Value.Name = Resolve(dictionary, pair.Value.LocKey, pair.Value.Name ?? pair.Key);
                    pair.Value.Description = Resolve(dictionary, pair.Value.DescriptionLocKey, pair.Value.Description);
                }
            }
        }

        private static string Resolve(ResourceDictionary dictionary, string locKey, string fallback)
        {
            if (string.IsNullOrEmpty(locKey))
            {
                return fallback;
            }

            if (dictionary != null && dictionary.Contains(locKey))
            {
                var text = dictionary[locKey] as string;
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            return ThemeForge.Localization.Get(locKey, fallback);
        }
    }
}
