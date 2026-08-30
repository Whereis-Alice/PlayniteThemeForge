using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace ThemeForge.Models
{
    /// <summary>
    /// Reader for the original ThemeModifier "thememodifier.yaml" declaration format so
    /// that existing themes (Helium, Harmony, Mythic, ...) keep working without changes.
    ///
    /// The format is a flat list where a bare string starts a new section and a single
    /// entry mapping declares an editable constant:
    ///
    ///   Constants:
    ///     - "Details View"
    ///     - DetailsViewDescriptionWidth(150,2400): 'Width of description'
    ///     - DetailsViewAllowUseOfLogos (needs Extra Metadata Loader): 'Allow use of logos'
    ///
    /// The parenthesised suffix is a numeric range when it looks like "min,max" and a
    /// free form remark otherwise. The original implementation only understood the range
    /// form, silently folding remarks into the resource key and breaking those entries.
    /// </summary>
    public class LegacyConstantsSchema
    {
        public List<object> Constants { get; set; }

        private static ILogger Logger { get { return LogManager.GetLogger(); } }

        public static LegacyConstantsSchema FromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                return Serialization.FromYamlFile<LegacyConstantsSchema>(filePath);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Theme Forge: failed to read legacy constants file " + filePath);
                return null;
            }
        }

        /// <summary>Converts the legacy list into declared variables plus display groups.</summary>
        public ThemeOptionsSchema ToSchema()
        {
            var schema = new ThemeOptionsSchema
            {
                Variables = new Variables(),
                Groups = new List<OptionGroup>()
            };

            if (Constants == null)
            {
                return schema;
            }

            var currentGroup = "General";
            var order = 0;

            foreach (var entry in Constants)
            {
                if (entry == null)
                {
                    continue;
                }

                var asText = entry as string;
                if (asText != null)
                {
                    currentGroup = asText.Trim();
                    schema.Groups.Add(new OptionGroup { Id = currentGroup, Title = currentGroup, Order = order++ });
                    continue;
                }

                var asMap = entry as IDictionary;
                if (asMap == null)
                {
                    continue;
                }

                foreach (DictionaryEntry pair in asMap)
                {
                    var rawKey = pair.Key == null ? null : pair.Key.ToString();
                    if (string.IsNullOrWhiteSpace(rawKey))
                    {
                        continue;
                    }

                    var label = pair.Value == null ? null : pair.Value.ToString();
                    var variable = ParseDeclaration(rawKey, label);
                    if (variable == null)
                    {
                        continue;
                    }

                    var declaration = variable.Value;
                    declaration.Value.Group = currentGroup;
                    schema.Variables[declaration.Key] = declaration.Value;
                }
            }

            return schema;
        }

        /// <summary>
        /// Splits "Key(1,10)" / "Key (remark)" / "Key" into a resource key plus editor hints.
        /// </summary>
        public static KeyValuePair<string, Variable>? ParseDeclaration(string rawKey, string label)
        {
            var key = rawKey.Trim();
            string suffix = null;

            var open = key.IndexOf('(');
            if (open > 0 && key.EndsWith(")", StringComparison.Ordinal))
            {
                suffix = key.Substring(open + 1, key.Length - open - 2).Trim();
                key = key.Substring(0, open).Trim();
            }

            if (key.Length == 0)
            {
                return null;
            }

            var variable = new Variable
            {
                Title = string.IsNullOrWhiteSpace(label) ? key : label.Trim()
            };

            if (suffix != null)
            {
                double min, max;
                var parts = suffix.Split(',');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out min) &&
                    double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out max))
                {
                    var span = Math.Abs(max - min);
                    var step = span <= 20 ? 1 : (span <= 200 ? 5 : 10);
                    variable.Slider = SliderRange.Create(min, max, step);
                }
                else
                {
                    // Not a range: keep it as the hint text instead of corrupting the key.
                    variable.Description = suffix;
                }
            }

            return new KeyValuePair<string, Variable>(key, variable);
        }
    }
}
