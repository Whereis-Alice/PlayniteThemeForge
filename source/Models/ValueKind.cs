namespace ThemeForge.Models
{
    /// <summary>
    /// The editor widget a value should be rendered with. Derived from the declared yaml
    /// type when present, otherwise inferred from the live resource object.
    /// </summary>
    public enum ValueKind
    {
        Unknown = 0,
        Text,
        Boolean,
        Number,
        Integer,
        Color,
        Brush,
        GradientBrush,
        Visibility,
        Thickness,
        CornerRadius,
        FontFamily,
        Duration,
        TimeSpan,
        HorizontalAlignment,
        VerticalAlignment,
        Choice
    }
}
