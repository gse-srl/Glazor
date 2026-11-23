namespace Glazor.Grds;

/// <summary>
/// Represents a vertical resizer for a grid.
/// </summary>
public interface IGridVertResizer
{
    /// <summary>
    /// The CSS the resizer measures to size the grid.
    /// </summary>
    /// <returns>We expect something like "height: 60vh"</returns>
    string? GetHeightRefStyles();

    /// <summary>
    /// Gets the number of rows to show in the grid.
    /// </summary>
    /// <returns>The number of rows to show.</returns>
    int GetPageSize<TGridItem>(AGrid<TGridItem> grid, double referenceHeight);
}
