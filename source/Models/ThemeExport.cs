using System.Collections.Generic;

namespace ThemeForge.Models
{
    /// <summary>
    /// Portable snapshot of one theme's configuration, written as yaml.
    ///
    /// Neither original plugin could share a look between machines: ThemeOptions kept
    /// everything in a single json blob keyed by theme guid, and ThemeModifier stored
    /// absolute colour values with no theme identity at all. Exporting one theme at a time,
    /// with the theme name recorded for a sanity check, makes a configuration shareable.
    /// </summary>
    public class ThemeExport
    {
        public int FormatVersion { get; set; } = 1;
        public string ThemeId { get; set; }
        public string ThemeName { get; set; }
        public string ThemeVersion { get; set; }
        public List<string> SelectedPresets { get; set; } = new List<string>();
        public VariablesValues Variables { get; set; } = new VariablesValues();
        public VariablesValues Resources { get; set; } = new VariablesValues();

        public static ThemeExport FromState(ThemeDescriptor theme, ThemeState state)
        {
            var export = new ThemeExport();
            if (theme != null)
            {
                export.ThemeId = theme.Id;
                export.ThemeName = theme.Name;
                export.ThemeVersion = theme.Version;
            }

            if (state != null)
            {
                export.SelectedPresets = state.SelectedPresets == null
                    ? new List<string>()
                    : new List<string>(state.SelectedPresets);
                export.Variables = ThemeState.CloneValues(state.Variables);
                export.Resources = ThemeState.CloneValues(state.Resources);
            }

            return export;
        }

        /// <summary>Writes the snapshot into a state, replacing whatever was there.</summary>
        public void ApplyTo(ThemeState state)
        {
            if (state == null)
            {
                return;
            }

            state.SelectedPresets = SelectedPresets == null ? new List<string>() : new List<string>(SelectedPresets);
            state.Variables = ThemeState.CloneValues(Variables);
            state.Resources = ThemeState.CloneValues(Resources);
        }
    }
}
