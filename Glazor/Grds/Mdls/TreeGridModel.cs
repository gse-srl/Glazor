namespace Glazor.Grds.Mdls;

/// <summary>
/// The in-memory model for <see cref="Treegliaria{TGridItem}"/>: rows in pre-order, each an <see cref="ITreeNode"/> that knows its
/// level and state. Expanding or collapsing a row calls <see cref="OnSetExpand"/>, where the host changes the rows. Filter and sort
/// are <see cref="DataGridModel{TGridItem}"/>'s, row by row: what they mean for a tree is the host's model to decide.
/// </summary>
public class TreeGridModel<TGridItem> : DataGridModel<TGridItem>, ITreeGridModel<TGridItem> where TGridItem : class, ITreeNode
{

    /// <summary>An empty model; fill it through <c>GetData()</c>.</summary>
    public TreeGridModel() : this([])
    {
    }

    /// <summary>A model over a copy of <paramref name="data"/>, the visible nodes in pre-order.</summary>
    public TreeGridModel(IEnumerable<TGridItem> data) : base(data)
    {
    }

    /// <summary>
    /// Called with the row index and the state asked when a row is expanded or collapsed: the host records the state and sets
    /// the rows again, without the descendants of the collapsed nodes.
    /// </summary>
    public required Func<int, bool, ValueTask> OnSetExpand { get; init; }

    /// <inheritdoc/>
    public ITreeNode? GetCurrentTreeNode()
    {
        return GetCurrentItem();
    }

    /// <inheritdoc/>
    public ITreeNode GetTreeNode(int rowIndex)
    {
        return GetItem(rowIndex);
    }

    /// <inheritdoc/>
    public async ValueTask SetExpandedAsync(int rowIndex, bool expanded)
    {
        await OnSetExpand.Invoke(rowIndex, expanded);
    }
}
