using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Data;

namespace ThemeForge.Models
{
    /// <summary>
    /// One saved profile for a theme. Profiles let the user keep several looks around
    /// ("dark compact", "big covers") and switch between them without re-tuning every
    /// value, which neither of the two original plugins supported.
    /// </summary>
    public class ThemeProfile
    {
        public string Name { get; set; }
        public List<string> SelectedPresets { get; set; } = new List<string>();
        public VariablesValues Variables { get; set; } = new VariablesValues();
        public VariablesValues Resources { get; set; } = new VariablesValues();

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Everything Theme Forge remembers about a single theme.
    ///
    /// <see cref="Variables"/> holds values for options the theme declared, while
    /// <see cref="Resources"/> holds free-form overrides for any resource key found in the
    /// running application. Keeping them apart means a theme update that drops an option
    /// cannot silently resurrect it as an untyped override.
    /// </summary>
    public class ThemeState
    {
        public List<string> SelectedPresets { get; set; } = new List<string>();
        public VariablesValues Variables { get; set; } = new VariablesValues();
        public VariablesValues Resources { get; set; } = new VariablesValues();
        public string ActiveProfile { get; set; }
        public List<ThemeProfile> Profiles { get; set; } = new List<ThemeProfile>();

        [DontSerialize]
        public bool IsEmpty
        {
            get
            {
                return (SelectedPresets == null || SelectedPresets.Count == 0)
                    && (Variables == null || Variables.Count == 0)
                    && (Resources == null || Resources.Count == 0);
            }
        }

        public ThemeProfile CaptureProfile(string name)
        {
            return new ThemeProfile
            {
                Name = name,
                SelectedPresets = SelectedPresets == null ? new List<string>() : SelectedPresets.ToList(),
                Variables = CloneValues(Variables),
                Resources = CloneValues(Resources)
            };
        }

        public void RestoreProfile(ThemeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            SelectedPresets = profile.SelectedPresets == null ? new List<string>() : profile.SelectedPresets.ToList();
            Variables = CloneValues(profile.Variables);
            Resources = CloneValues(profile.Resources);
            ActiveProfile = profile.Name;
        }

        public static VariablesValues CloneValues(VariablesValues source)
        {
            var clone = new VariablesValues();
            if (source == null)
            {
                return clone;
            }

            foreach (var pair in source)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                clone[pair.Key] = new VariableValue { Type = pair.Value.Type, Value = pair.Value.Value };
            }

            return clone;
        }
    }

    /// <summary>Persisted plugin settings. Written to ExtensionsData as json by Playnite.</summary>
    public class ForgeSettings : ObservableObject
    {
        private bool liveApply = true;
        private bool showTopPanelButton = true;
        private bool menuInExtensions = true;
        private bool notifyMissingExtensions = true;
        private bool showAllResources;
        private bool showOnlyModified;

        /// <summary>Per theme id state. Survives switching themes back and forth.</summary>
        public DictNoCase<ThemeState> Themes { get; set; } = new DictNoCase<ThemeState>();

        /// <summary>Push edits into the running application as they are made.</summary>
        public bool LiveApply
        {
            get { return liveApply; }
            set { SetValue(ref liveApply, value); }
        }

        public bool ShowTopPanelButton
        {
            get { return showTopPanelButton; }
            set { SetValue(ref showTopPanelButton, value); }
        }

        public bool MenuInExtensions
        {
            get { return menuInExtensions; }
            set { SetValue(ref menuInExtensions, value); }
        }

        public bool NotifyMissingExtensions
        {
            get { return notifyMissingExtensions; }
            set { SetValue(ref notifyMissingExtensions, value); }
        }

        /// <summary>Show every editable resource, not just the ones the theme declared.</summary>
        public bool ShowAllResources
        {
            get { return showAllResources; }
            set { SetValue(ref showAllResources, value); }
        }

        public bool ShowOnlyModified
        {
            get { return showOnlyModified; }
            set { SetValue(ref showOnlyModified, value); }
        }

        /// <summary>Bumped by migrations so an import from a legacy plugin runs once.</summary>
        public int MigrationVersion { get; set; }

        /// <summary>
        /// Copies every persisted field from another instance.
        ///
        /// The settings dialog edits a clone so that Cancel can discard the changes, but the
        /// plugin hands out one long lived instance to the engine. Copying field by field keeps
        /// that reference valid instead of swapping it and leaving stale bindings behind.
        /// </summary>
        public void CopyFrom(ForgeSettings other)
        {
            if (other == null)
            {
                return;
            }

            Themes = other.Themes ?? new DictNoCase<ThemeState>();
            MigrationVersion = other.MigrationVersion;
            LiveApply = other.LiveApply;
            ShowTopPanelButton = other.ShowTopPanelButton;
            MenuInExtensions = other.MenuInExtensions;
            NotifyMissingExtensions = other.NotifyMissingExtensions;
            ShowAllResources = other.ShowAllResources;
            ShowOnlyModified = other.ShowOnlyModified;
        }

        public ThemeState State(string themeId)
        {
            if (string.IsNullOrEmpty(themeId))
            {
                return new ThemeState();
            }

            if (Themes == null)
            {
                Themes = new DictNoCase<ThemeState>();
            }

            ThemeState state;
            if (!Themes.TryGetValue(themeId, out state) || state == null)
            {
                state = new ThemeState();
                Themes[themeId] = state;
            }

            return state;
        }
    }
}
