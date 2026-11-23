namespace Glazor.Grds.Mdls;

/// <summary>
/// An <see cref="IGridModel{TGridItem}"/> where the row order is relevant and can be changed by the user.
/// </summary>
public interface IOrderedGridModel<TGridItem> : IGridModel<TGridItem>
{
    /// <summary>
    /// Swaps the given rows. Care should be taken to restore the current row.
    /// </summary>
    /// <param name="row1">The index of the first row.</param>
    /// <param name="row2">The index of the second row.</param>
    void SwapRows(int row1, int row2);

    /// <summary>
    /// Determines whether the given rows are swappable.
    /// </summary>
    /// <param name="row1">The index of the first row.</param>
    /// <param name="row2">The index of the second row.</param>
    /// <returns>True if the rows are swappable; otherwise, false.</returns>
    bool AreRowsSwappable(int row1, int row2);
}
