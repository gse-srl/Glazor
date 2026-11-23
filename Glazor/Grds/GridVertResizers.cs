namespace Glazor.Grds;

/// <summary>
/// The ways a grid can choose its page size, the rows it shows, for its <c>VertResizer</c> parameter.
/// </summary>
public static class GridVertResizers
{
    /// <summary>
    /// Always the same number of rows, whatever the grid's height.
    /// </summary>
    /// <param name="pageSize">Rows per page; at least 1.</param>
    public static IGridVertResizer Fixed(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return new FixedImpl(pageSize);
    }
    private record FixedImpl(int PageSize) : IGridVertResizer
    {
        public string? GetHeightRefStyles() => null;

        public int GetPageSize<TGridItem>(AGrid<TGridItem> grid, double referenceHeight)
        {
            return PageSize;
        }
    }

    /// <summary>
    /// Rows growing with the grid's height, from <paramref name="minPageSize"/> at <paramref name="referencedMinGridHeight"/> pixels
    /// to <paramref name="maxPageSize"/> at <paramref name="referencedMaxGridHeight"/>, clamped outside that range.
    /// </summary>
    /// <param name="heightRefStyles">We expect something like "height: 60vh"</param>
    /// <param name="minPageSize">At least 1, and less than <paramref name="maxPageSize"/>.</param>
    /// <param name="maxPageSize">The rows at <paramref name="referencedMaxGridHeight"/> and taller.</param>
    /// <param name="referencedMinGridHeight">Less than <paramref name="referencedMaxGridHeight"/>.</param>
    /// <param name="referencedMaxGridHeight">The height, in pixels, from which <paramref name="maxPageSize"/> rows are shown.</param>
    public static IGridVertResizer Proportional(string heightRefStyles,
                            int minPageSize,
                            int maxPageSize,
                            int referencedMinGridHeight,
                            int referencedMaxGridHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heightRefStyles);
        ArgumentOutOfRangeException.ThrowIfLessThan(minPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPageSize, minPageSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(referencedMaxGridHeight, referencedMinGridHeight);
        return new ProportionalImpl(heightRefStyles, minPageSize, maxPageSize, referencedMinGridHeight, referencedMaxGridHeight);
    }
    private record ProportionalImpl(string HeightRefStyles,
                            int MinPageSize,
                            int MaxPageSize,
                            int ReferencedMinGridHeight,
                            int ReferencedMaxGridHeight) : IGridVertResizer
    {
        private readonly double rp = (double)(ReferencedMaxGridHeight - ReferencedMinGridHeight) /
            (MaxPageSize - MinPageSize);

        public string? GetHeightRefStyles() => HeightRefStyles;

        public int GetPageSize<TGridItem>(AGrid<TGridItem> grid, double referenceHeight)
        {
            referenceHeight -= grid.HeaderHeight ?? 0;
            int pgSize = MinPageSize + (int)((referenceHeight - ReferencedMinGridHeight) / rp);
            int pageSize = Math.Clamp(pgSize, MinPageSize, MaxPageSize);
            return pageSize;
        }
    }

    /// <summary>
    /// As many rows of the grid's <c>RowHeight</c>, which must be set, as fit the grid's height, clamped to the range.
    /// Once the grid is drawn, as many rows as fit by the heights the browser drew.
    /// </summary>
    /// <param name="heightRefStyles">We expect something like "height: 60vh"</param>
    /// <param name="minPageSize">At least 1, and not more than <paramref name="maxPageSize"/>.</param>
    /// <param name="maxPageSize">The most rows shown, however tall the grid.</param>
    public static IGridVertResizer RowHeight(string heightRefStyles,
                            int minPageSize,
                            int maxPageSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heightRefStyles);
        ArgumentOutOfRangeException.ThrowIfLessThan(minPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPageSize, minPageSize);
        return new RowHeightImpl(heightRefStyles, minPageSize, maxPageSize);
    }
    private record RowHeightImpl(string HeightRefStyles,
                            int MinPageSize,
                            int MaxPageSize) : IGridVertResizer
    {
        public string? GetHeightRefStyles() => HeightRefStyles;

        public int GetPageSize<TGridItem>(AGrid<TGridItem> grid, double referenceHeight)
        {
            int rowHeight = grid.RowHeight ?? 0;
            if (rowHeight <= 0)
                throw new InvalidOperationException($"Invalid RowHeight {rowHeight}");
            int pgSize;
            // Once drawn, by the room the last page left, or took beyond the grid's height, in rows as really drawn
            if (grid.GetDrawnRows() is (int drawnRows, double drawnContent, double drawnRowHeight))
                pgSize = drawnRows + (int)Math.Floor((referenceHeight - drawnContent) / drawnRowHeight);
            else
                pgSize = (int)((referenceHeight - (grid.HeaderHeight ?? 0)) / rowHeight) - 1;  // -1 is an "heuristic" just to consider some more used space
            int pageSize = Math.Clamp(pgSize, MinPageSize, MaxPageSize);
            return pageSize;
        }
    }
}
