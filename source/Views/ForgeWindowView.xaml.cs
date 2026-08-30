using System.Windows;
using System.Windows.Controls;
using ThemeForge.ViewModels;

namespace ThemeForge.Views
{
    /// <summary>
    /// Standalone shell around <see cref="SettingsView"/>. Playnite drives the
    /// begin/end/cancel edit cycle itself for the add-on settings page, so when the editor
    /// is opened from the top panel this shell has to do it explicitly.
    /// </summary>
    public partial class ForgeWindowView : UserControl
    {
        private readonly Window window;
        private readonly ForgeSettingsViewModel model;
        private bool settled;

        public ForgeWindowView(Window owner, ForgeSettingsViewModel viewModel)
        {
            InitializeComponent();
            window = owner;
            model = viewModel;
            Editor.DataContext = viewModel;

            // Closing through the title bar has to behave like cancel, otherwise the
            // live overrides would stay applied while the stored settings say otherwise.
            window.Closed += (s, e) => Settle(false);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            Settle(true);
            window.Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Settle(false);
            window.Close();
        }

        private void Settle(bool keep)
        {
            if (settled)
            {
                return;
            }

            settled = true;
            if (keep)
            {
                model.EndEdit();
            }
            else
            {
                model.CancelEdit();
            }
        }
    }
}
