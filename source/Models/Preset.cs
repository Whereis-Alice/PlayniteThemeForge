using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Data;

namespace ThemeForge.Models
{
    /// <summary>
    /// A named bundle of resource files and constant overrides. Presets can nest, which
    /// lets a theme express "Layout &gt; Compact &gt; With sidebar" style trees.
    /// </summary>
    public class Preset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LocKey { get; set; }
        public string Description { get; set; }
        public string DescriptionLocKey { get; set; }
        public string Preview { get; set; }
        public Presets Presets { get; set; }
        public List<string> Files { get; set; }
        public VariablesValues Constants { get; set; }

        [DontSerialize]
        public bool NeedRestart { get; set; }

        /// <summary>
        /// True for the "Default" entry Theme Forge injects when a group has no explicit
        /// default. Synthetic entries are never written to the saved selection.
        /// </summary>
        [DontSerialize]
        public bool IsSynthetic { get; set; }

        [DontSerialize]
        public bool IsGroup { get { return Presets != null && Presets.Count > 0; } }

        /// <summary>
        /// Selectable children including a synthetic "Default" entry when the theme did
        /// not declare one, so the user can always get back to the untouched look.
        /// </summary>
        private List<Preset> optionsList;

        [DontSerialize]
        public List<Preset> OptionsList
        {
            get
            {
                // Cached: a combo box compares SelectedItem by reference, so handing out a fresh
                // list of fresh objects on every read would make the selection unbindable.
                if (optionsList == null)
                {
                    optionsList = Presets == null ? new List<Preset>() : Presets.Values.ToList();
                    if (!optionsList.Any(p => p.Id != null && p.Id.ToLowerInvariant().EndsWith("default")))
                    {
                        optionsList.Insert(0, new Preset
                        {
                            Id = (Id == null ? string.Empty : Id + ".") + "default",
                            Name = "Default",
                            LocKey = "LOCThemeForgeDefault",
                            IsSynthetic = true
                        });
                    }
                }

                return optionsList;
            }
        }

        [DontSerialize]
        private Preset DefaultOption
        {
            get { return OptionsList.FirstOrDefault(p => p.Id != null && p.Id.ToLowerInvariant().EndsWith("default")); }
        }

        private Preset selected;

        [DontSerialize]
        public Preset Selected
        {
            get { return selected ?? DefaultOption; }
            set { selected = value; }
        }

        public override string ToString()
        {
            return Name ?? Id;
        }
    }
}
