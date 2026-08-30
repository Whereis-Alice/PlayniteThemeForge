using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Playnite.SDK;
using Playnite.SDK.Data;
using ThemeForge.Models;

namespace ThemeForge.Services
{
    /// <summary>
    /// One-shot import of settings written by the two plugins Theme Forge replaces, so the user
    /// keeps the look they already tuned.
    ///
    /// ThemeOptions stores presets and typed values per theme id. ThemeModifier stores a flat set
    /// of Playnite base brushes (shared by every theme) plus per-theme constant lists where the
    /// value sits in an untyped "Element" field and enums are persisted as their numeric value.
    /// </summary>
    public static class LegacyMigration
    {
        /// <summary>Bump when a new import step is added; already migrated installs skip the old ones.</summary>
        public const int CurrentVersion = 1;

        public const string ThemeOptionsExtensionId = "904cbf3b-573f-48f8-9642-0a09d05c64ef";
        public const string ThemeModifierExtensionId = "ec2f4013-17e6-428a-b8a9-5e34a3b80009";

        private static readonly ILogger logger = LogManager.GetLogger();

        public class MigrationReport
        {
            public int ThemeOptionsValues { get; set; }
            public int ThemeOptionsPresets { get; set; }
            public int ThemeModifierBrushes { get; set; }
            public int ThemeModifierConstants { get; set; }
            public int SkippedGradients { get; set; }
            public List<string> Notes { get; set; } = new List<string>();

            public int Total
            {
                get { return ThemeOptionsValues + ThemeOptionsPresets + ThemeModifierBrushes + ThemeModifierConstants; }
            }
        }

        public static bool HasLegacyData(string extensionsDataPath)
        {
            return File.Exists(ConfigPath(extensionsDataPath, ThemeOptionsExtensionId))
                || File.Exists(ConfigPath(extensionsDataPath, ThemeModifierExtensionId));
        }

        private static string ConfigPath(string extensionsDataPath, string extensionId)
        {
            return Path.Combine(extensionsDataPath ?? string.Empty, extensionId, "config.json");
        }

        /// <summary>
        /// Imports both legacy configurations into <paramref name="settings"/>. Existing Theme Forge
        /// values always win, so running the import twice cannot clobber later edits.
        /// </summary>
        public static MigrationReport Run(ForgeSettings settings, string extensionsDataPath, string activeThemeId)
        {
            var report = new MigrationReport();
            if (settings == null)
            {
                return report;
            }

            ImportThemeOptions(settings, ConfigPath(extensionsDataPath, ThemeOptionsExtensionId), report);
            ImportThemeModifier(settings, ConfigPath(extensionsDataPath, ThemeModifierExtensionId), activeThemeId, report);

            settings.MigrationVersion = CurrentVersion;
            return report;
        }

        private class ThemeOptionsConfig
        {
            public Dictionary<string, List<string>> SelectedPresets { get; set; }
            public Dictionary<string, VariablesValues> UserSettings { get; set; }
        }

        public static void ImportThemeOptions(ForgeSettings settings, string configPath, MigrationReport report)
        {
            if (!File.Exists(configPath))
            {
                return;
            }

            ThemeOptionsConfig config;
            try
            {
                config = Serialization.FromJsonFile<ThemeOptionsConfig>(configPath);
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: cannot read ThemeOptions settings " + configPath);
                report.Notes.Add("ThemeOptions: " + e.Message);
                return;
            }

            if (config == null)
            {
                return;
            }

            if (config.SelectedPresets != null)
            {
                foreach (var pair in config.SelectedPresets)
                {
                    if (pair.Value == null || pair.Value.Count == 0)
                    {
                        continue;
                    }

                    var state = settings.State(pair.Key);
                    foreach (var preset in pair.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(preset) && !state.SelectedPresets.Contains(preset))
                        {
                            state.SelectedPresets.Add(preset);
                            report.ThemeOptionsPresets++;
                        }
                    }
                }
            }

            if (config.UserSettings != null)
            {
                foreach (var pair in config.UserSettings)
                {
                    if (pair.Value == null)
                    {
                        continue;
                    }

                    var state = settings.State(pair.Key);
                    foreach (var value in pair.Value)
                    {
                        if (value.Value == null || string.IsNullOrEmpty(value.Value.Value))
                        {
                            continue;
                        }

                        if (state.Variables.ContainsKey(value.Key))
                        {
                            continue;
                        }

                        state.Variables.Set(value.Key, value.Value.Type, value.Value.Value);
                        report.ThemeOptionsValues++;
                    }
                }
            }
        }

        private class ModifierConstant
        {
            public string Name { get; set; }
            public string TypeResource { get; set; }
            public object Element { get; set; }
            public double Opacity { get; set; }
        }

        private class ModifierTheme
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<ModifierConstant> Constants { get; set; }
        }

        private class ModifierConfig
        {
            public List<ModifierTheme> ThemesConstants { get; set; }
        }

        public static void ImportThemeModifier(ForgeSettings settings, string configPath, string activeThemeId, MigrationReport report)
        {
            if (!File.Exists(configPath))
            {
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(configPath);
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme Forge: cannot read ThemeModifier settings " + configPath);
                report.Notes.Add("ThemeModifier: " + e.Message);
                return;
            }

            ImportModifierBrushes(settings, json, activeThemeId, report);
            ImportModifierConstants(settings, json, report);
        }

        /// <summary>
        /// The "&lt;Key&gt;_Edit" entries are Playnite's own base brushes and were applied globally by
        /// ThemeModifier, regardless of the active theme. Theme Forge scopes every override to a
        /// theme, so they land on the theme that was active when the import runs.
        /// </summary>
        private static void ImportModifierBrushes(ForgeSettings settings, string json, string activeThemeId, MigrationReport report)
        {
            if (string.IsNullOrEmpty(activeThemeId))
            {
                return;
            }

            Dictionary<string, object> flat;
            if (!Serialization.TryFromJson(json, out flat) || flat == null)
            {
                return;
            }

            var state = settings.State(activeThemeId);

            foreach (var pair in flat)
            {
                if (pair.Key == null)
                {
                    continue;
                }

                if (pair.Key.EndsWith("_EditGradient", StringComparison.Ordinal))
                {
                    // Gradient definitions cannot be expressed as a single colour; they are left
                    // for the user to redo in the gradient editor.
                    continue;
                }

                if (!pair.Key.EndsWith("_Edit", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = pair.Value as string;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var key = pair.Key.Substring(0, pair.Key.Length - "_Edit".Length);
                if (state.Resources.ContainsKey(key) || state.Variables.ContainsKey(key))
                {
                    continue;
                }

                var formatted = ConvertLegacyBrush(text);
                if (formatted == null)
                {
                    continue;
                }

                state.Resources.Set(key, "SolidColorBrush", formatted);
                report.ThemeModifierBrushes++;
            }
        }

        /// <summary>
        /// Turns "#FF55AEFF-0.49" (colour plus separate opacity) into a single "#7F55AEFF".
        /// Folding the opacity into the alpha channel means one value to edit instead of two that
        /// silently multiply each other.
        /// </summary>
        public static string ConvertLegacyBrush(string legacy)
        {
            var text = legacy.Trim();
            var opacity = 1.0;

            var separator = text.LastIndexOf('-');
            if (separator > 0)
            {
                double parsed;
                if (double.TryParse(text.Substring(separator + 1), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    opacity = parsed;
                    text = text.Substring(0, separator);
                }
            }

            var color = ValueConverter.ParseColor(text);
            if (!color.HasValue)
            {
                return null;
            }

            return ValueConverter.FormatColor(ValueConverter.ApplyOpacity(color.Value, opacity));
        }

        private static void ImportModifierConstants(ForgeSettings settings, string json, MigrationReport report)
        {
            ModifierConfig config;
            if (!Serialization.TryFromJson(json, out config) || config == null || config.ThemesConstants == null)
            {
                return;
            }

            foreach (var theme in config.ThemesConstants)
            {
                if (theme == null || string.IsNullOrEmpty(theme.Id) || theme.Constants == null)
                {
                    continue;
                }

                var state = settings.State(theme.Id);

                foreach (var constant in theme.Constants)
                {
                    if (constant == null || string.IsNullOrWhiteSpace(constant.Name))
                    {
                        continue;
                    }

                    if (state.Variables.ContainsKey(constant.Name) || state.Resources.ContainsKey(constant.Name))
                    {
                        continue;
                    }

                    var type = ValueConverter.ResolveType(constant.TypeResource);
                    var formatted = FormatLegacyElement(constant.Element, type, constant.Opacity);
                    if (formatted == null)
                    {
                        continue;
                    }

                    var typeName = type == null ? constant.TypeResource : type.Name;
                    state.Variables.Set(constant.Name, typeName, formatted);
                    report.ThemeModifierConstants++;
                }
            }
        }

        /// <summary>
        /// Renders an untyped json value using the declared resource type. Enum valued constants
        /// were stored as plain numbers, which is why an imported Visibility used to read as "2";
        /// they are converted back to their name here.
        /// </summary>
        public static string FormatLegacyElement(object element, Type type, double opacity)
        {
            if (element == null)
            {
                return null;
            }

            if (type != null && type.IsEnum && !(element is string))
            {
                try
                {
                    var numeric = Convert.ToInt64(element, CultureInfo.InvariantCulture);
                    var name = Enum.GetName(type, Enum.ToObject(type, numeric));
                    if (name != null)
                    {
                        return name;
                    }
                }
                catch (Exception)
                {
                    // Falls through to the generic formatting below.
                }
            }

            if (element is bool)
            {
                return (bool)element ? "True" : "False";
            }

            if (element is double || element is float || element is decimal || element is long || element is int)
            {
                var text = Convert.ToDouble(element, CultureInfo.InvariantCulture).ToString("0.#####", CultureInfo.InvariantCulture);
                return text;
            }

            var raw = Convert.ToString(element, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (raw.StartsWith("#", StringComparison.Ordinal) && opacity > 0 && opacity < 1)
            {
                var color = ValueConverter.ParseColor(raw);
                if (color.HasValue)
                {
                    return ValueConverter.FormatColor(ValueConverter.ApplyOpacity(color.Value, opacity));
                }
            }

            return raw;
        }
    }
}
