using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Playnite.SDK;
using ThemeForge.Models;

namespace ThemeForge.Services
{
    /// <summary>
    /// Converts between the string form stored in yaml / json settings and live WPF
    /// objects.
    ///
    /// The original plugins hand-rolled a switch per supported type, which meant every
    /// new resource type needed code. Here the well known types are handled explicitly
    /// (for predictable formatting) and everything else falls back to the type's own
    /// <see cref="TypeConverter"/>, so gradients, font weights, grid lengths and enums all
    /// work without extra cases.
    /// </summary>
    public static class ValueConverter
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static readonly Dictionary<string, Type> knownTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "String", typeof(string) },
            { "Boolean", typeof(bool) },
            { "Bool", typeof(bool) },
            { "Int32", typeof(int) },
            { "Int", typeof(int) },
            { "Integer", typeof(int) },
            { "Double", typeof(double) },
            { "Single", typeof(double) },
            { "Color", typeof(Color) },
            { "SolidColorBrush", typeof(SolidColorBrush) },
            { "Brush", typeof(Brush) },
            { "LinearGradientBrush", typeof(LinearGradientBrush) },
            { "RadialGradientBrush", typeof(RadialGradientBrush) },
            { "Thickness", typeof(Thickness) },
            { "CornerRadius", typeof(CornerRadius) },
            { "Duration", typeof(Duration) },
            { "TimeSpan", typeof(TimeSpan) },
            { "Visibility", typeof(Visibility) },
            { "FontFamily", typeof(FontFamily) },
            { "FontWeight", typeof(FontWeight) },
            { "FontStyle", typeof(FontStyle) },
            { "GridLength", typeof(GridLength) },
            { "HorizontalAlignment", typeof(HorizontalAlignment) },
            { "VerticalAlignment", typeof(VerticalAlignment) },
            { "TextAlignment", typeof(TextAlignment) },
            { "TextWrapping", typeof(TextWrapping) },
            { "Stretch", typeof(Stretch) },
            { "Orientation", typeof(System.Windows.Controls.Orientation) },
            { "Dock", typeof(System.Windows.Controls.Dock) },
            { "ScrollBarVisibility", typeof(System.Windows.Controls.ScrollBarVisibility) }
        };

        public static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            Type type;
            return knownTypes.TryGetValue(typeName.Trim(), out type) ? type : null;
        }

        /// <summary>Canonical type name to persist for a live value.</summary>
        public static string TypeNameOf(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is SolidColorBrush)
            {
                return "SolidColorBrush";
            }

            return value.GetType().Name;
        }

        public static ValueKind KindOf(Type type)
        {
            if (type == null)
            {
                return ValueKind.Unknown;
            }

            if (type == typeof(string))
            {
                return ValueKind.Text;
            }

            if (type == typeof(bool))
            {
                return ValueKind.Boolean;
            }

            if (type == typeof(int) || type == typeof(short) || type == typeof(long))
            {
                return ValueKind.Integer;
            }

            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            {
                return ValueKind.Number;
            }

            if (type == typeof(Color))
            {
                return ValueKind.Color;
            }

            if (type == typeof(SolidColorBrush))
            {
                return ValueKind.Brush;
            }

            if (typeof(GradientBrush).IsAssignableFrom(type))
            {
                return ValueKind.GradientBrush;
            }

            if (typeof(Brush).IsAssignableFrom(type))
            {
                return ValueKind.Brush;
            }

            if (type == typeof(Visibility))
            {
                return ValueKind.Visibility;
            }

            if (type == typeof(Thickness))
            {
                return ValueKind.Thickness;
            }

            if (type == typeof(CornerRadius))
            {
                return ValueKind.CornerRadius;
            }

            if (type == typeof(FontFamily))
            {
                return ValueKind.FontFamily;
            }

            if (type == typeof(Duration))
            {
                return ValueKind.Duration;
            }

            if (type == typeof(TimeSpan))
            {
                return ValueKind.TimeSpan;
            }

            if (type == typeof(HorizontalAlignment))
            {
                return ValueKind.HorizontalAlignment;
            }

            if (type == typeof(VerticalAlignment))
            {
                return ValueKind.VerticalAlignment;
            }

            if (type.IsEnum)
            {
                return ValueKind.Choice;
            }

            return ValueKind.Unknown;
        }

        /// <summary>
        /// Representative CLR type for a value kind. Used as a validation fallback when a theme
        /// declares an option without a type name: the kind is still known from the yaml editor
        /// hint, so bad input can be reported instead of silently accepted.
        /// </summary>
        public static Type TypeOfKind(ValueKind kind)
        {
            switch (kind)
            {
                case ValueKind.Boolean:
                    return typeof(bool);
                case ValueKind.Integer:
                    return typeof(int);
                case ValueKind.Number:
                    return typeof(double);
                case ValueKind.Color:
                    return typeof(Color);
                case ValueKind.Brush:
                    return typeof(SolidColorBrush);
                case ValueKind.GradientBrush:
                    return typeof(LinearGradientBrush);
                case ValueKind.Visibility:
                    return typeof(Visibility);
                case ValueKind.Thickness:
                    return typeof(Thickness);
                case ValueKind.CornerRadius:
                    return typeof(CornerRadius);
                case ValueKind.FontFamily:
                    return typeof(FontFamily);
                case ValueKind.Duration:
                    return typeof(Duration);
                case ValueKind.TimeSpan:
                    return typeof(TimeSpan);
                case ValueKind.HorizontalAlignment:
                    return typeof(HorizontalAlignment);
                case ValueKind.VerticalAlignment:
                    return typeof(VerticalAlignment);
                default:
                    return null;
            }
        }

        public static ValueKind KindOf(string typeName)
        {
            return KindOf(ResolveType(typeName));
        }

        public static object Parse(string typeName, string raw)
        {
            return ParseAs(ResolveType(typeName), raw);
        }

        /// <summary>
        /// Returns null when the text cannot be turned into the requested type. Callers
        /// treat null as "leave the theme value alone" rather than writing a broken
        /// resource, which is how a typo in one field used to break a whole dictionary.
        /// </summary>
        public static object ParseAs(Type type, string raw)
        {
            if (raw == null)
            {
                return null;
            }

            if (type == typeof(string))
            {
                return raw;
            }

            var text = raw.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            try
            {
                if (text[0] == '<')
                {
                    return XamlReader.Parse(EnsureNamespaces(text));
                }

                if (type == null)
                {
                    return raw;
                }

                if (type == typeof(bool))
                {
                    bool parsed;
                    return bool.TryParse(text, out parsed) ? (object)parsed : null;
                }

                if (type == typeof(int))
                {
                    double numeric;
                    if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                    {
                        return (int)Math.Round(numeric);
                    }

                    return null;
                }

                if (type == typeof(double))
                {
                    double parsed;
                    return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? (object)parsed : null;
                }

                if (type == typeof(Color))
                {
                    var color = ParseColor(text);
                    return color.HasValue ? (object)color.Value : null;
                }

                if (type == typeof(SolidColorBrush))
                {
                    var color = ParseColor(text);
                    return color.HasValue ? new SolidColorBrush(color.Value) : null;
                }

                if (typeof(Brush).IsAssignableFrom(type))
                {
                    var brush = new BrushConverter().ConvertFromInvariantString(text) as Brush;
                    if (brush != null && brush.CanFreeze)
                    {
                        brush.Freeze();
                    }

                    return brush;
                }

                if (type.IsEnum)
                {
                    return Enum.Parse(type, text, true);
                }

                var converter = TypeDescriptor.GetConverter(type);
                if (converter != null && converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFromInvariantString(text);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Theme Forge: cannot convert '" + text + "' to " + (type == null ? "?" : type.Name) + ": " + e.Message);
            }

            return null;
        }

        /// <summary>Text form of a live resource value, round-trippable through <see cref="ParseAs"/>.</summary>
        public static string Format(object value)
        {
            if (value == null)
            {
                return null;
            }

            var text = value as string;
            if (text != null)
            {
                return text;
            }

            if (value is bool)
            {
                return (bool)value ? "True" : "False";
            }

            if (value is double || value is float || value is decimal)
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.#####", CultureInfo.InvariantCulture);
            }

            if (value is int || value is short || value is long)
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            }

            if (value is Color)
            {
                return FormatColor((Color)value);
            }

            var solid = value as SolidColorBrush;
            if (solid != null)
            {
                return FormatColor(ApplyOpacity(solid.Color, solid.Opacity));
            }

            var brush = value as Brush;
            if (brush != null)
            {
                return SaveXaml(brush);
            }

            var font = value as FontFamily;
            if (font != null)
            {
                return font.Source;
            }

            try
            {
                var converter = TypeDescriptor.GetConverter(value.GetType());
                if (converter != null && converter.CanConvertTo(typeof(string)))
                {
                    return converter.ConvertToInvariantString(value);
                }
            }
            catch (Exception)
            {
                // Fall through to ToString below.
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static Color? ParseColor(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                var converted = ColorConverter.ConvertFromString(text.Trim());
                if (converted is Color)
                {
                    return (Color)converted;
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        public static string FormatColor(Color color)
        {
            if (color.A == 255)
            {
                return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            }

            return "#" + color.A.ToString("X2") + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        /// <summary>
        /// Folds a brush level opacity into the alpha channel. Themes and the legacy
        /// ThemeModifier settings both express translucency this way, and a single alpha
        /// value round-trips through the resource dictionary reliably.
        /// </summary>
        public static Color ApplyOpacity(Color color, double opacity)
        {
            if (double.IsNaN(opacity) || opacity >= 1)
            {
                return color;
            }

            if (opacity < 0)
            {
                opacity = 0;
            }

            return Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
        }

        private static string SaveXaml(object value)
        {
            try
            {
                return XamlWriter.Save(value);
            }
            catch (Exception e)
            {
                logger.Warn("Theme Forge: cannot serialize " + value.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Adds the default xaml namespaces to a hand written snippet if missing.</summary>
        private static string EnsureNamespaces(string xaml)
        {
            if (xaml.IndexOf("xmlns=", StringComparison.Ordinal) >= 0)
            {
                return xaml;
            }

            var end = xaml.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '/', '>' }, 1);
            if (end < 0)
            {
                return xaml;
            }

            const string namespaces = " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
                                      " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
                                      " xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"";

            return xaml.Substring(0, end) + namespaces + xaml.Substring(end);
        }
    }
}
