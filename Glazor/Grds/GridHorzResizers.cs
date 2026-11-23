namespace Glazor.Grds;

/// <summary>
/// The ways a grid can choose how many display units of columns it shows at once, for its <c>HorzResizer</c> parameter. Columns
/// beyond them are reached by scrolling horizontally.
/// </summary>
public static class GridHorzResizers
{
    /// <summary>
    /// Always the same number of display units, whatever the grid's width.
    /// </summary>
    /// <param name="dispUnits">The display units shown, the sum of the shown columns' preferred sizes; at least 1.</param>
    public static IGridHorzResizer Fixed(int dispUnits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dispUnits, 1);
        return new FixedImpl(dispUnits);
    }
    private record FixedImpl(int DispUnits) : IGridHorzResizer
    {
        public int GetDisplayedUnits<TGridItem>(AGrid<TGridItem> grid, double gridWidth)
        {
            return DispUnits;
        }
    }

    /// <summary>
    /// Display units growing with the grid's width, from <paramref name="minDispUnits"/> at <paramref name="referencedMinGridWidth"/>
    /// pixels to <paramref name="maxDispUnits"/> at <paramref name="referencedMaxGridWidth"/>, clamped outside that range.
    /// </summary>
    /// <param name="minDispUnits">At least 1, and less than <paramref name="maxDispUnits"/>.</param>
    /// <param name="maxDispUnits">The display units at <paramref name="referencedMaxGridWidth"/> and wider.</param>
    /// <param name="referencedMinGridWidth">Less than <paramref name="referencedMaxGridWidth"/>.</param>
    /// <param name="referencedMaxGridWidth">The width, in pixels, from which <paramref name="maxDispUnits"/> are shown.</param>
    public static IGridHorzResizer Proportional(int minDispUnits,
                              int maxDispUnits,
                              int referencedMinGridWidth,
                              int referencedMaxGridWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minDispUnits, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDispUnits, minDispUnits);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(referencedMaxGridWidth, referencedMinGridWidth);
        return new ProportionalImpl(minDispUnits, maxDispUnits, referencedMinGridWidth, referencedMaxGridWidth);
    }
    private record ProportionalImpl(int MinDispUnits,
                              int MaxDispUnits,
                              int ReferencedMinGridWidth,
                              int ReferencedMaxGridWidth) : IGridHorzResizer
    {
        private readonly double rp = (double)(ReferencedMaxGridWidth - ReferencedMinGridWidth) / 
            (MaxDispUnits - MinDispUnits);
        public int GetDisplayedUnits<TGridItem>(AGrid<TGridItem> grid, double gridWidth)
        {
            int du = MinDispUnits + 
                (int)((gridWidth - ReferencedMinGridWidth) / rp);
            int dispUnits = Math.Clamp(du, MinDispUnits, MaxDispUnits);
            return dispUnits;
        }
    }


    /// <summary>
    /// As many display units as fit <paramref name="dispUnitWidth"/> pixels each into the grid's width, clamped to the range.
    /// </summary>
    /// <param name="minDispUnits">At least 1, and not more than <paramref name="maxDispUnits"/>.</param>
    /// <param name="maxDispUnits">The most display units shown, however wide the grid.</param>
    /// <param name="dispUnitWidth">Pixels per display unit; at least 1.</param>
    public static IGridHorzResizer DispUnitWidth(int minDispUnits,
                              int maxDispUnits,
                              int dispUnitWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minDispUnits, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDispUnits, minDispUnits);
        ArgumentOutOfRangeException.ThrowIfLessThan(dispUnitWidth, 1);
        return new DispUnitWidthImpl(minDispUnits, maxDispUnits, dispUnitWidth);
    }
    private record DispUnitWidthImpl(int MinDispUnits,
                              int MaxDispUnits,
                              int DispUnitWidth) : IGridHorzResizer
    {
        public int GetDisplayedUnits<TGridItem>(AGrid<TGridItem> grid, double gridWidth)
        {
            int du = (int)(gridWidth / DispUnitWidth);
            int dispUnits = Math.Clamp(du, MinDispUnits, MaxDispUnits);
            return dispUnits;
        }
    }
}
