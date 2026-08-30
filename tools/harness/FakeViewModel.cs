using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThemeForge.Models;
using ThemeForge.ViewModels;

namespace Harness
{
    /// <summary>
    /// Hand built stand in for ForgeSettingsViewModel. WPF bindings resolve by name, so a
    /// plain object exposing the same property names is enough to exercise every template.
    /// The item level view models are the real ones, which is the point: their editors are
    /// what we want to inspect.
    /// </summary>
    public class FakeViewModel
    {
        public static FakeViewModel Build()
        {
            var model = new FakeViewModel();
            model.Settings = new ForgeSettings();
            model.Settings.LiveApply = true;
            model.Settings.ShowTopPanelButton = true;
            model.Settings.NotifyMissingExtensions = true;

            var theme = new ThemeDescriptor
            {
                Id = "8b15c46a-90c2-4fe5-9ebb-1ab25ba7fcb1",
                Name = "Helium Nova",
                Author = "Whereis-Alice",
                Version = "1.0.0",
                ThemeApiVersion = "2.9.0",
                Mode = "Desktop",
                RootPath = "C:" + System.IO.Path.DirectorySeparatorChar + "Themes"
            };
            model.AvailableThemes = new ObservableCollection<ThemeDescriptor> { theme };
            model.SelectedTheme = theme;
            model.ThemeSummary = "Helium Nova 1.0.0 - Whereis-Alice - API 2.9.0";

            model.Profiles = new ObservableCollection<ThemeProfile>
            {
                new ThemeProfile { Name = "Default" },
                new ThemeProfile { Name = "Night" }
            };
            model.SelectedProfile = model.Profiles[0];

            model.Presets = BuildPresets();
            model.Groups = BuildGroups();
            model.Resources = BuildResources();

            model.MissingExtensions = new ObservableCollection<string>
            {
                "SuccessStory (SuccessStory)",
                "Bangumi Metadata (BangumiMetadata)"
            };
            model.UnresolvedKeys = new ObservableCollection<string>
            {
                "GridViewLegacySpacing",
                "DetailsViewObsoleteMargin"
            };

            model.ResourceSummary = "3 / 24";
            model.PluginVersion = "1.0.0";
            model.HostVersion = "10.53.0.18801";
            return model;
        }

        private static ObservableCollection<PresetItemViewModel> BuildPresets()
        {
            var root = new Presets();

            var layout = new Preset { Name = "Layout", Description = "Overall spacing of the library views." };
            layout.Presets = new Presets();
            layout.Presets["Comfortable"] = new Preset { Name = "Comfortable", Description = "Default Helium spacing." };
            layout.Presets["Compact"] = new Preset { Name = "Compact", Description = "Tighter grid, more covers per row.", NeedRestart = true };
            layout.Presets["Cinematic"] = new Preset { Name = "Cinematic", Description = "Large hero art with a dimmed backdrop." };
            root["Layout"] = layout;

            var accent = new Preset { Name = "Accent colour" };
            accent.Presets = new Presets();
            accent.Presets["Violet"] = new Preset { Name = "Violet" };
            accent.Presets["Azure"] = new Preset { Name = "Azure" };
            accent.Presets["Emerald"] = new Preset { Name = "Emerald" };
            root["Accent"] = accent;

            root.PostLoad(null);

            var items = new ObservableCollection<PresetItemViewModel>();
            items.Add(new PresetItemViewModel(layout, layout.Presets["Compact"], p => { }));
            items.Add(new PresetItemViewModel(accent, accent.Presets["Violet"], p => { }));
            return items;
        }

        private static ObservableCollection<OptionGroupViewModel> BuildGroups()
        {
            var groups = new ObservableCollection<OptionGroupViewModel>();

            var general = new OptionGroupViewModel("general", "General");
            general.Description = "Behaviour switches exposed by the theme.";
            general.Source.Add(Item("ShowNavigationButtons", "Show navigation buttons", ValueKind.Boolean, "Boolean", "True", "False", null, null));
            general.Source.Add(Item("GridViewCoverZoomOnHover", "Zoom covers on hover", ValueKind.Boolean, "Boolean", "False", "True", null, null));
            general.Source.Add(Item("LibraryTitle", "Library title", ValueKind.Text, "String", "Library", "My games", null, null));
            general.Source.Add(Choice("GridViewSectionOrder", "Grid section order", new string[] { "0", "1", "2" }, new string[] { "Cover first", "Details first", "Actions first" }, "1"));
            groups.Add(general);

            var metrics = new OptionGroupViewModel("metrics", "Metrics");
            metrics.Order = 1;
            metrics.Source.Add(Number("DetailsViewDescriptionWidth", "Description width", 200, 1200, 0.5, "600", "468.9"));
            metrics.Source.Add(Number("GridViewSpacing", "Grid spacing", 0, 40, 1, "8", "14"));
            var integer = Item("SidebarWidth", "Sidebar width", ValueKind.Integer, "Int32", "60", "72", null, null);
            integer.Slider = SliderRange.Create(40, 160, 1);
            metrics.Source.Add(integer);
            var restart = Item("UseAlternateFont", "Use alternate font", ValueKind.Boolean, "Boolean", "False", "True", null, null);
            restart.NeedRestart = true;
            metrics.Source.Add(restart);
            groups.Add(metrics);

            var look = new OptionGroupViewModel("look", "Appearance");
            look.Order = 2;
            look.Source.Add(Item("AccentColor", "Accent colour", ValueKind.Color, "Color", "#FF9C7BE0", "#FF4FC3F7", null, null));
            look.Source.Add(Item("HeroOverlayBrush", "Hero overlay", ValueKind.Brush, "SolidColorBrush", "#99000000", "#CC101018", null, null));
            look.Source.Add(Item("TitleFont", "Title font", ValueKind.FontFamily, "FontFamily", "Segoe UI", "Microsoft YaHei UI", null, null));
            look.Source.Add(Choice("SidebarVisibility", "Sidebar visibility", new string[] { "Visible", "Collapsed" }, new string[] { "Visible", "Hidden" }, "Visible"));
            look.Source.Add(Item("CardPadding", "Card padding", ValueKind.Thickness, "Thickness", "8,8,8,8", "12,10,12,10", null, null));
            var broken = Item("LegacyGlow", "Legacy glow", ValueKind.Color, "Color", "#FF000000", "not-a-colour", null, null);
            broken.IsMissing = true;
            look.Source.Add(broken);
            groups.Add(look);

            foreach (var group in groups)
            {
                group.ApplyFilter(x => true);
            }

            return groups;
        }

        private static ObservableCollection<OptionItemViewModel> BuildResources()
        {
            var list = new ObservableCollection<OptionItemViewModel>();
            list.Add(Resource("TextBrush", "#FFEFEFF2", "#FFEFEFF2"));
            list.Add(Resource("TextBrushDark", "#FF8A8A95", "#FF000000"));
            list.Add(Resource("GlyphBrush", "#FF9C7BE0", "#7E55AEFF"));
            list.Add(Resource("ButtonBackgroundBrush", "#FF33333C", "#00FFFFFF"));
            list.Add(Resource("NegativeRatingBrush", "#FFFF6B6B", "#FFFF6B6B"));
            list.Add(Resource("WindowBackgourndBrush", "#FF1A1A1E", null));
            return list;
        }

        private static OptionItemViewModel Resource(string key, string defaultValue, string stored)
        {
            var item = new OptionItemViewModel(key, x => { });
            item.Title = key;
            item.GroupName = "Playnite";
            item.Kind = ValueKind.Brush;
            item.TypeName = "SolidColorBrush";
            item.IsResource = true;
            item.DefaultValue = defaultValue;
            item.Initialize(stored);
            return item;
        }

        private static OptionItemViewModel Number(string key, string title, double min, double max, double step, string defaultValue, string stored)
        {
            var item = Item(key, title, ValueKind.Number, "Double", defaultValue, stored, null, null);
            item.Slider = SliderRange.Create(min, max, step);
            return item;
        }

        private static OptionItemViewModel Choice(string key, string title, string[] values, string[] labels, string stored)
        {
            var choices = new List<VariableChoice>();
            for (var i = 0; i < values.Length; i++)
            {
                choices.Add(new VariableChoice { Value = values[i], Title = labels[i] });
            }

            return Item(key, title, ValueKind.Choice, "String", values[0], stored, null, choices);
        }

        private static OptionItemViewModel Item(
            string key,
            string title,
            ValueKind kind,
            string typeName,
            string defaultValue,
            string stored,
            SliderRange slider,
            List<VariableChoice> choices)
        {
            var item = new OptionItemViewModel(key, x => { });
            item.Title = title;
            item.Description = "Declared by the theme as " + typeName + ".";
            item.Kind = kind;
            item.TypeName = typeName;
            item.DefaultValue = defaultValue;
            item.Slider = slider;
            item.Choices = choices;
            item.Initialize(stored);
            return item;
        }

        public ForgeSettings Settings { get; set; }
        public System.Windows.ResourceDictionary PreviewResources { get; set; }
        public ObservableCollection<ThemeDescriptor> AvailableThemes { get; set; }
        public ThemeDescriptor SelectedTheme { get; set; }
        public bool IsActiveTheme { get { return true; } }
        public string ThemeSummary { get; set; }
        public string SearchText { get; set; }
        public bool ShowOnlyModified { get; set; }
        public bool ShowAllResources { get; set; }
        public bool PreviewVisible { get { return true; } set { } }
        public ObservableCollection<ThemeProfile> Profiles { get; set; }
        public ThemeProfile SelectedProfile { get; set; }
        public ObservableCollection<PresetItemViewModel> Presets { get; set; }
        public ObservableCollection<OptionGroupViewModel> Groups { get; set; }
        public ObservableCollection<OptionItemViewModel> Resources { get; set; }
        public ObservableCollection<string> MissingExtensions { get; set; }
        public ObservableCollection<string> UnresolvedKeys { get; set; }
        public int ModifiedCount { get { return 7; } }
        public bool HasModifications { get { return true; } }
        public string ResourceSummary { get; set; }
        public bool HasMissingExtensions { get { return true; } }
        public bool HasUnresolvedKeys { get { return true; } }
        public bool HasPresets { get { return true; } }
        public bool HasOptions { get { return true; } }
        public bool LegacyDataAvailable { get { return true; } }
        public string PluginVersion { get; set; }
        public string HostVersion { get; set; }
    }
}
