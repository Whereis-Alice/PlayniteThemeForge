using System.Windows;
using System.Windows.Media;

namespace Harness
{
    /// <summary>
    /// Minimal stand in for the brush set every Playnite theme is required to publish.
    /// The plugin views resolve these with DynamicResource, so without them the harness
    /// would render an unstyled black on black window and hide real layout defects.
    /// </summary>
    public static class FakeTheme
    {
        public static ResourceDictionary Build()
        {
            var dictionary = new ResourceDictionary();

            Add(dictionary, "WindowBackgourndBrush", "#FF1A1A1E");
            Add(dictionary, "ControlBackgroundBrush", "#FF232329");
            Add(dictionary, "PopupBackgroundBrush", "#FF26262D");
            Add(dictionary, "TooltipBackgroundBrush", "#FF32323A");
            Add(dictionary, "ExpanderBackgroundBrush", "#FF2A2A31");
            Add(dictionary, "GridItemBackgroundBrush", "#FF2E2E36");
            Add(dictionary, "ButtonBackgroundBrush", "#FF33333C");

            Add(dictionary, "TextBrush", "#FFEFEFF2");
            Add(dictionary, "TextBrushDarker", "#FFB9B9C2");
            Add(dictionary, "TextBrushDark", "#FF8A8A95");

            Add(dictionary, "NormalBrush", "#FF3C3C46");
            Add(dictionary, "NormalBrushDark", "#FF2B2B33");
            Add(dictionary, "NormalBorderBrush", "#FF4A4A56");
            Add(dictionary, "HoverBrush", "#FF4E4E5C");
            Add(dictionary, "PopupBorderBrush", "#FF5A5A68");
            Add(dictionary, "PanelSeparatorBrush", "#FF3A3A44");
            Add(dictionary, "WindowPanelSeparatorBrush", "#FF33333C");

            Add(dictionary, "GlyphBrush", "#FF9C7BE0");
            Add(dictionary, "HighlightGlyphBrush", "#FFC0A6FF");
            Add(dictionary, "CheckBoxCheckMarkBkBrush", "#FF9C7BE0");

            Add(dictionary, "PositiveRatingBrush", "#FF5CD08A");
            Add(dictionary, "NegativeRatingBrush", "#FFFF6B6B");
            Add(dictionary, "MixedRatingBrush", "#FFE8C15A");
            Add(dictionary, "WarningBrush", "#FFE8A33A");

            dictionary.Add("FontSize", 13.0);
            dictionary.Add("FontSizeLarge", 17.0);
            dictionary.Add("FontSizeSmall", 11.0);

            return dictionary;
        }

        private static void Add(ResourceDictionary dictionary, string key, string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            dictionary.Add(key, brush);
        }
    }
}
