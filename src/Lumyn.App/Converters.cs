using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Lumyn.App;

/// <summary>Small value converters used by the views (referenced via <c>x:Static</c>).</summary>
public static class Converters
{
    /// <summary>
    /// true → a 2★ star grid column (the YouTube-Music-style audio queue pane);
    /// false → zero width so the cover/hero fills the whole audio area.
    /// </summary>
    public static readonly FuncValueConverter<bool, GridLength> QueueColumnWidth =
        new(hasQueue => hasQueue ? new GridLength(2, GridUnitType.Star) : new GridLength(0));
}
