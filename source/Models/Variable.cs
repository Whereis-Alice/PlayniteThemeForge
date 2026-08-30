using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Playnite.SDK.Data;

namespace ThemeForge.Models
{
    /// <summary>
    /// Serialized half of a theme variable: just the type tag and the raw string value.
    /// Kept separate from <see cref="Variable"/> so that saved settings stay small and
    /// forward compatible when a theme adds or renames metadata.
    /// </summary>
    public class VariableValue : INotifyPropertyChanged
    {
        private string rawValue;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Type { get; set; }

        public string Value
        {
            get { return rawValue; }
            set
            {
                if (rawValue == value)
                {
                    return;
                }

                rawValue = value;
                OnPropertyChanged();
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    /// <summary>
    /// A single tweakable value declared by a theme in options.yaml / themeforge.yaml.
    /// Metadata properties are marked DontSerialize: they always come from the theme,
    /// never from user settings, so an updated theme can change labels freely.
    /// </summary>
    public class Variable : VariableValue
    {
        public new string Value
        {
            get { return base.Value ?? Default; }
            set { base.Value = value; }
        }

        [DontSerialize]
        public string Title { get; set; }

        [DontSerialize]
        public string LocKey { get; set; }

        [DontSerialize]
        public string Default { get; set; }

        [DontSerialize]
        public string Description { get; set; }

        [DontSerialize]
        public string DescriptionLocKey { get; set; }

        [DontSerialize]
        public string Preview { get; set; }

        [DontSerialize]
        public string Group { get; set; }

        [DontSerialize]
        public string GroupLocKey { get; set; }

        [DontSerialize]
        public SliderRange Slider { get; set; }

        /// <summary>Optional explicit choice list, rendered as a combo box.</summary>
        [DontSerialize]
        public List<VariableChoice> Choices { get; set; }

        [DontSerialize]
        public bool NeedRestart { get; set; }

        /// <summary>Set by the engine when the value is not addressable at runtime.</summary>
        [DontSerialize]
        public bool IsMissing { get; set; }
    }

    public class VariableChoice
    {
        public string Value { get; set; }
        public string Title { get; set; }
        public string LocKey { get; set; }

        public override string ToString()
        {
            return Title ?? Value;
        }
    }
}
