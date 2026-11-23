namespace Glazor.Grds;

/// <summary>
/// Represents a horizontal resizer for a grid.
/// </summary>
public interface IGridHorzResizer
{
    /// <summary>
    /// Gets the number of displayed units. A value of -1 means unlimited.
    /// Display units are also returned by <see cref="IColumn.GetPreferredSize"/>.
    /// </summary>
    /// <returns>The number of displayed units.</returns>
    int GetDisplayedUnits<TGridItem>(AGrid<TGridItem> grid, double gridWidth);
}
