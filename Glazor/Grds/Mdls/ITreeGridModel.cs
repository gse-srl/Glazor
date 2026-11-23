namespace Glazor.Grds.Mdls;

/// <summary>
/// Specialized <see cref="IDataGridModel{TGridItem}"/> to be used with <see cref="Treegliaria{TGridItem}"/>.
/// Adds support for nested nodes, see <see cref="ITreeNode"/>.
/// </summary>
public interface ITreeGridModel<TGridItem> : IDataGridModel<TGridItem>
{
    /// <summary>
    /// Gets the tree node at the given row index.
    /// </summary>
    /// <param name="rowIndex">The index of the row.</param>
    /// <returns>The tree node at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A row out of range, as <see cref="IGridModel{TGridItem}.GetValueAt(int, int)"/>
    /// says.</exception>
    ITreeNode GetTreeNode(int rowIndex);

    /// <summary>
    /// Gets the current tree node, or null if none.
    /// </summary>
    /// <returns>The current tree node.</returns>
    ITreeNode? GetCurrentTreeNode();

    /// <summary>
    /// Expands or collapses a node at the specified row index.
    /// </summary>
    /// <param name="rowIndex">The index of the row.</param>
    /// <param name="expanded">True to expand the node; false to collapse it.</param>
    ValueTask SetExpandedAsync(int rowIndex, bool expanded);
}
