using System;
using System.Collections.Generic;
using System.IO;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace ThemeForge.Models
{
    /// <summary>
    /// Contents of a theme's options file. Theme Forge reads the original ThemeOptions
    /// "options.yaml" layout as-is, plus its own "themeforge.yaml" which adds groups and
    /// resource declarations. Both deserialize into this type.
    /// </summary>
    public class ThemeOptionsSchema
    {
        public Presets Presets { get; set; }
        public Variables Variables { get; set; }

        /// <summary>Optional display groups so long option lists stay navigable.</summary>
        public List<OptionGroup> Groups { get; set; }

        private static ILogger Logger { get { return LogManager.GetLogger(); } }

        public static ThemeOptionsSchema FromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var schema = Serialization.FromYamlFile<ThemeOptionsSchema>(filePath);
                if (schema != null && schema.Presets != null)
                {
                    schema.Presets.PostLoad(Path.GetDirectoryName(filePath));
                }

                return schema;
            }
            catch (System.Exception e)
            {
                Logger.Error(e, "Theme Forge: failed to read option schema " + filePath);
                return null;
            }
        }

        /// <summary>Merges another schema into this one; existing entries win.</summary>
        public void MergeFrom(ThemeOptionsSchema other)
        {
            if (other == null)
            {
                return;
            }

            if (other.Presets != null)
            {
                if (Presets == null)
                {
                    Presets = new Presets();
                }

                foreach (var pair in other.Presets)
                {
                    if (!Presets.ContainsKey(pair.Key))
                    {
                        Presets[pair.Key] = pair.Value;
                    }
                }
            }

            if (other.Variables != null)
            {
                if (Variables == null)
                {
                    Variables = new Variables();
                }

                foreach (var pair in other.Variables)
                {
                    if (!Variables.ContainsKey(pair.Key))
                    {
                        Variables[pair.Key] = pair.Value;
                    }
                }
            }

            if (other.Groups != null)
            {
                if (Groups == null)
                {
                    Groups = new List<OptionGroup>();
                }

                // Groups are matched by id or title further up the stack, so appending a second
                // declaration of the same group would leave a silent shadow: the first entry wins
                // and the later icon/order/description are dropped without any hint as to why.
                // Skipping duplicates here keeps "existing wins" true for groups as well.
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var existing in Groups)
                {
                    if (existing == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(existing.Id))
                    {
                        known.Add(existing.Id.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(existing.Title))
                    {
                        known.Add(existing.Title.Trim());
                    }
                }

                foreach (var group in other.Groups)
                {
                    if (group == null)
                    {
                        continue;
                    }

                    var id = string.IsNullOrWhiteSpace(group.Id) ? null : group.Id.Trim();
                    var title = string.IsNullOrWhiteSpace(group.Title) ? null : group.Title.Trim();
                    if ((id != null && known.Contains(id)) || (title != null && known.Contains(title)))
                    {
                        continue;
                    }

                    if (id != null)
                    {
                        known.Add(id);
                    }

                    if (title != null)
                    {
                        known.Add(title);
                    }

                    Groups.Add(group);
                }
            }
        }
    }

    public class OptionGroup
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string LocKey { get; set; }
        public string Description { get; set; }
        public string DescriptionLocKey { get; set; }
        public string Icon { get; set; }
        public int Order { get; set; }
    }
}
