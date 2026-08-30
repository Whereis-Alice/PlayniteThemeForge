using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThemeForge.Models
{
    /// <summary>
    /// Tree of presets. Ids are dot delimited paths ("Layout.Compact") so a selection can
    /// be persisted as a flat string list and still resolve back into the tree.
    /// </summary>
    public class Presets : DictNoCase<Preset>
    {
        /// <summary>Resolves a dot delimited path, invoking <paramref name="visitor"/> on every node.</summary>
        public Preset GetItem(string path, Func<Preset, bool> visitor = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var level = this;
            Preset current = null;

            foreach (var segment in path.Split('.'))
            {
                current = level == null ? null : level.Get(segment);
                level = current == null ? null : current.Presets;

                if (current == null)
                {
                    break;
                }

                if (visitor != null && !visitor(current))
                {
                    break;
                }
            }

            return current;
        }

        /// <summary>
        /// Resource files contributed by the given selection, in declaration order.
        /// A file listed by several presets is moved to the position of the last one that
        /// mentions it, matching the "later preset wins" override expectation.
        /// </summary>
        public List<string> GetResourceFiles(IEnumerable<string> selection)
        {
            var paths = new List<string>();
            if (selection == null)
            {
                return paths;
            }

            foreach (var entry in selection)
            {
                GetItem((entry ?? string.Empty).Trim(), preset =>
                {
                    if (preset.Files != null)
                    {
                        paths.RemoveAll(f => preset.Files.Contains(f));
                        paths.AddRange(preset.Files);
                    }

                    return true;
                });
            }

            return paths;
        }

        /// <summary>Constant overrides contributed by the given selection.</summary>
        public VariablesValues GetConstants(IEnumerable<string> selection)
        {
            var values = new VariablesValues();
            if (selection == null)
            {
                return values;
            }

            foreach (var entry in selection)
            {
                GetItem((entry ?? string.Empty).Trim(), preset =>
                {
                    if (preset.Constants != null)
                    {
                        foreach (var constant in preset.Constants)
                        {
                            values[constant.Key] = constant.Value;
                        }
                    }

                    return true;
                });
            }

            return values;
        }

        /// <summary>Flattens the tree into (path, preset) pairs.</summary>
        public List<KeyValuePair<string, Preset>> Enumerate()
        {
            var result = new List<KeyValuePair<string, Preset>>();
            Walk(this, result);
            return result;
        }

        private static void Walk(Presets level, List<KeyValuePair<string, Preset>> sink)
        {
            if (level == null)
            {
                return;
            }

            foreach (var pair in level)
            {
                sink.Add(pair);
                Walk(pair.Value.Presets, sink);
            }
        }

        /// <summary>
        /// Assigns ids, resolves preview image paths and normalises nested nodes.
        /// Called once after deserialization.
        /// </summary>
        public void PostLoad(string themePath)
        {
            Assign(null, this, themePath);
        }

        private static void Assign(string parentId, Presets level, string themePath)
        {
            if (level == null)
            {
                return;
            }

            foreach (var key in level.Keys.ToList())
            {
                var preset = level[key];
                if (preset == null)
                {
                    level.Remove(key);
                    continue;
                }

                if (string.IsNullOrEmpty(preset.Id))
                {
                    preset.Id = parentId != null ? parentId + "." + key : key;
                }

                if (!string.IsNullOrEmpty(preset.Preview))
                {
                    var imagePath = Path.Combine(themePath ?? string.Empty, preset.Preview);
                    preset.Preview = File.Exists(imagePath) ? imagePath : null;
                }

                Assign(preset.Id, preset.Presets, themePath);
            }
        }
    }
}
