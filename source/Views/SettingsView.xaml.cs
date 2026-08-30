using System.Windows;
using System.Windows.Controls;
using ThemeForge.ViewModels;

namespace ThemeForge.Views
{
    /// <summary>
    /// Host for the whole editor. The only thing the code behind has to do is wire the
    /// preview resource dictionary: a <see cref="ResourceDictionary"/> cannot be bound in
    /// xaml, it has to be pushed into an element scope from code.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        private ResourceDictionary attached;

        public SettingsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var dictionaries = PreviewHost.Resources.MergedDictionaries;
            if (attached != null)
            {
                dictionaries.Remove(attached);
                attached = null;
            }

            var model = e.NewValue as ForgeSettingsViewModel;
            if (model == null || model.PreviewResources == null)
            {
                return;
            }

            attached = model.PreviewResources;
            dictionaries.Add(attached);
        }
    }
}
