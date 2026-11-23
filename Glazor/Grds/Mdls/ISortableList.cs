namespace Glazor.Grds.Mdls;

/// <summary>A list a model sorts in place, such as <c>OnDiskChunkedList</c> behind a paged model.</summary>
public interface ISortableList<T> : IList<T>
{
    /// <summary>Sorts the list in place by <paramref name="comparer"/>.</summary>
    public void Sort(IComparer<T> comparer);
}

/// <summary>
/// A <see cref="List{T}"/> that is an <see cref="ISortableList{T}"/>: what a <c>ReadAll</c> EF model holds its rows in, and what a
/// paged one's <c>CreateItemsListFn</c> returns to keep them in memory instead of spilling them to disk.
/// </summary>
public class SortableList<T> : List<T>, ISortableList<T>
{
    /// <summary>An empty list.</summary>
    public SortableList() : base()
    {
    }

    /// <summary>An empty list with room for <paramref name="capacity"/> items.</summary>
    public SortableList(int capacity) : base(capacity)
    {
    }

    /// <summary>A list of the items of <paramref name="collection"/>.</summary>
    public SortableList(IEnumerable<T> collection) : base(collection)
    {
    }
}
