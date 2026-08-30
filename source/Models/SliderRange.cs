using System;
using System.Globalization;

namespace ThemeForge.Models
{
    /// <summary>
    /// Numeric bounds for a slider editor. Values are kept as strings in yaml so that
    /// a theme can express durations ("0:0:0.5") as well as plain numbers.
    /// </summary>
    public class SliderRange
    {
        private double min;
        private double max = 100;
        private double step = 1;
        private double smallChange;
        private double largeChange;

        public string Min
        {
            get { return ToStr(min); }
            set { min = ToDouble(value, 0); }
        }

        public string Max
        {
            get { return ToStr(max); }
            set { max = ToDouble(value, 100); }
        }

        public string Step
        {
            get { return ToStr(step); }
            set { step = ToDouble(value, 1); }
        }

        public string SmallChange
        {
            get { return ToStr(smallChange != 0 ? smallChange : step); }
            set { smallChange = ToDouble(value, 0); }
        }

        public string LargeChange
        {
            get
            {
                if (largeChange != 0)
                {
                    return ToStr(largeChange);
                }

                if (step <= 0)
                {
                    return ToStr(1);
                }

                var count = (max - min) / step;
                var scale = (int)(count / 10) + (count % 10 > 0 ? 1 : 0);
                return ToStr(step * Math.Max(scale, 1));
            }
            set { largeChange = ToDouble(value, 0); }
        }

        public double MinValue { get { return min; } }
        public double MaxValue { get { return max; } }
        public double StepValue { get { return step <= 0 ? 1 : step; } }

        public static SliderRange Create(double minValue, double maxValue, double stepValue)
        {
            return new SliderRange
            {
                Min = ToStr(minValue),
                Max = ToStr(maxValue),
                Step = ToStr(stepValue)
            };
        }

        private static double ToDouble(string str, double fallback)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return fallback;
            }

            if (str.Contains(":"))
            {
                TimeSpan span;
                return TimeSpan.TryParse(str, CultureInfo.InvariantCulture, out span) ? span.TotalSeconds : fallback;
            }

            double result;
            return double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static string ToStr(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
