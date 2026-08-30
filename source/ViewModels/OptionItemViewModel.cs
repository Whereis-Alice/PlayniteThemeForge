using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using Playnite.SDK;
using ThemeForge.Models;
using ThemeForge.Services;

namespace ThemeForge.ViewModels
{
    /// <summary>
    /// One editable row in the user interface.
    ///
    /// There is deliberately a single view model for every kind of value, with one canonical
    /// string in <see cref="Value"/> and typed views computed on top of it. The alternative -
    /// a class per type, as both original plugins did - meant the same "reset", "modified" and
    /// "validate" logic was reimplemented per type, and they drifted apart.
    /// </summary>
    public class OptionItemViewModel : ObservableObject
    {
        private readonly Action<OptionItemViewModel> onChanged;
        private string rawValue;
        private string error;

        public OptionItemViewModel(string key, Action<OptionItemViewModel> onChanged)
        {
            Key = key;
            this.onChanged = onChanged;
            ResetCommand = new RelayCommand(Reset, () => IsModified);
            CopyKeyCommand = new RelayCommand(CopyKey);
        }

        public string Key { get; private set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string GroupName { get; set; }
        public ValueKind Kind { get; set; }
        public string TypeName { get; set; }
        public Type ValueType { get; set; }
        public SliderRange Slider { get; set; }
        public List<VariableChoice> Choices { get; set; }
        public string DefaultValue { get; set; }

        /// <summary>True when the value only exists in the theme's own resource tree.</summary>
        public bool IsResource { get; set; }

        /// <summary>True when the theme declares the option but nothing in the app answers to the key.</summary>
        public bool IsMissing { get; set; }

        /// <summary>Themes bind some constants with StaticResource, which only re-reads on restart.</summary>
        public bool NeedRestart { get; set; }

        public RelayCommand ResetCommand { get; private set; }
        public RelayCommand CopyKeyCommand { get; private set; }

        public string DisplayTitle
        {
            get { return string.IsNullOrWhiteSpace(Title) ? Key : Title; }
        }

        /// <summary>Tooltip text: description first, then the resource key so it can be looked up.</summary>
        public string ToolTipText
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Description))
                {
                    parts.Add(Description);
                }

                parts.Add(Key + "  (" + (TypeName ?? Kind.ToString()) + ")");

                if (IsMissing)
                {
                    parts.Add(Localization.Get("LOCThemeForgeMissingResource", "This resource is not present in the running theme."));
                }

                if (NeedRestart)
                {
                    parts.Add(Localization.Get("LOCThemeForgeNeedsRestart", "Takes effect after a restart."));
                }

                return string.Join(Environment.NewLine, parts);
            }
        }

        public string Value
        {
            get { return rawValue ?? DefaultValue; }
            set
            {
                if (string.Equals(rawValue, value, StringComparison.Ordinal))
                {
                    return;
                }

                rawValue = value;
                Validate();
                NotifyValueChanged();
            }
        }

        /// <summary>Null when the user has not overridden the option; that is what gets persisted.</summary>
        public string StoredValue
        {
            get { return IsModified ? Value : null; }
        }

        public bool IsModified
        {
            get { return rawValue != null && !string.Equals(rawValue, DefaultValue, StringComparison.Ordinal); }
        }

        public string ValueError
        {
            get { return error; }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(error); }
        }

        public bool HasSlider
        {
            get { return Slider != null; }
        }

        public bool HasChoices
        {
            get { return Choices != null && Choices.Count > 0; }
        }

        /// <summary>Applies the stored value without raising the change callback (initial load).</summary>
        public void Initialize(string storedValue)
        {
            rawValue = storedValue;
            Validate();
        }

        public void Reset()
        {
            if (rawValue == null)
            {
                return;
            }

            rawValue = null;
            Validate();
            NotifyValueChanged();
        }

        private void CopyKey()
        {
            try
            {
                System.Windows.Clipboard.SetText(Key);
            }
            catch (Exception)
            {
                // Clipboard access can fail while another process owns it; nothing to recover.
            }
        }

        private void Validate()
        {
            error = null;
            var text = Value;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Free text and choices are accepted verbatim: a choice is constrained by its list and
            // a string has nothing to parse.
            if (Kind == ValueKind.Text || Kind == ValueKind.Choice)
            {
                return;
            }

            // A theme may declare an option without a usable type name. The editor kind is still
            // known, so fall back to it rather than skipping validation altogether, which used to
            // let a typo like "not-a-colour" pass as valid.
            var target = ValueType ?? ValueConverter.ResolveType(TypeName) ?? ValueConverter.TypeOfKind(Kind);
            if (target == null)
            {
                return;
            }

            if (ValueConverter.ParseAs(target, text) == null)
            {
                error = Localization.Get("LOCThemeForgeInvalidValue", "Value cannot be interpreted as") + " " + (TypeName ?? target.Name);
            }
        }

        private void NotifyValueChanged()
        {
            OnPropertyChanged("Value");
            OnPropertyChanged("StoredValue");
            OnPropertyChanged("IsModified");
            OnPropertyChanged("ValueError");
            OnPropertyChanged("HasError");
            OnPropertyChanged("BoolValue");
            OnPropertyChanged("NumberValue");
            OnPropertyChanged("NumberText");
            OnPropertyChanged("ColorValue");
            OnPropertyChanged("ColorBrush");
            OnPropertyChanged("PreviewBrush");
            OnPropertyChanged("AlphaValue");
            OnPropertyChanged("RedValue");
            OnPropertyChanged("GreenValue");
            OnPropertyChanged("BlueValue");
            OnPropertyChanged("SelectedChoice");
            OnPropertyChanged("FontValue");

            if (onChanged != null)
            {
                onChanged(this);
            }
        }

        public bool BoolValue
        {
            get
            {
                bool parsed;
                return bool.TryParse(Value, out parsed) && parsed;
            }
            set { Value = value ? "True" : "False"; }
        }

        public double NumberValue
        {
            get
            {
                double parsed;
                return double.TryParse(Value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
            }
            set
            {
                var rounded = Kind == ValueKind.Integer ? Math.Round(value) : Math.Round(value, 3);
                Value = rounded.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Text bound to the numeric box; keeps partial input from clearing the value.</summary>
        public string NumberText
        {
            get { return Value; }
            set
            {
                double parsed;
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    NumberValue = parsed;
                }
            }
        }

        public Color ColorValue
        {
            get
            {
                var parsed = ValueConverter.ParseColor(Value);
                return parsed.HasValue ? parsed.Value : Colors.Transparent;
            }
            set { Value = ValueConverter.FormatColor(value); }
        }

        public Brush ColorBrush
        {
            get { return new SolidColorBrush(ColorValue); }
        }

        /// <summary>Swatch for any brush valued option, including gradients.</summary>
        public Brush PreviewBrush
        {
            get
            {
                var parsed = ValueConverter.ParseAs(typeof(Brush), Value) as Brush;
                return parsed != null ? parsed : ColorBrush;
            }
        }

        public int AlphaValue
        {
            get { return ColorValue.A; }
            set { SetChannel(0, value); }
        }

        public int RedValue
        {
            get { return ColorValue.R; }
            set { SetChannel(1, value); }
        }

        public int GreenValue
        {
            get { return ColorValue.G; }
            set { SetChannel(2, value); }
        }

        public int BlueValue
        {
            get { return ColorValue.B; }
            set { SetChannel(3, value); }
        }

        private void SetChannel(int channel, int raw)
        {
            var component = (byte)Math.Max(0, Math.Min(255, raw));
            var current = ColorValue;
            var updated = channel == 0
                ? Color.FromArgb(component, current.R, current.G, current.B)
                : channel == 1
                    ? Color.FromArgb(current.A, component, current.G, current.B)
                    : channel == 2
                        ? Color.FromArgb(current.A, current.R, component, current.B)
                        : Color.FromArgb(current.A, current.R, current.G, component);

            ColorValue = updated;
        }

        public VariableChoice SelectedChoice
        {
            get
            {
                if (!HasChoices)
                {
                    return null;
                }

                var current = Value;
                var match = Choices.FirstOrDefault(c => string.Equals(c.Value, current, StringComparison.OrdinalIgnoreCase));
                return match != null ? match : Choices.FirstOrDefault();
            }
            set
            {
                if (value != null)
                {
                    Value = value.Value;
                }
            }
        }

        public string FontValue
        {
            get { return Value; }
            set { Value = value; }
        }

        /// <summary>Installed font families, offered for FontFamily valued options.</summary>
        public static List<string> SystemFonts
        {
            get
            {
                if (systemFonts == null)
                {
                    systemFonts = Fonts.SystemFontFamilies
                        .Select(f => f.Source)
                        .Where(f => !string.IsNullOrEmpty(f))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }

                return systemFonts;
            }
        }

        private static List<string> systemFonts;

        public override string ToString()
        {
            return Key + " = " + Value;
        }
    }
}
