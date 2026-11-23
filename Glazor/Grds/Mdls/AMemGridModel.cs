namespace Glazor.Grds.Mdls;

/// <summary>
/// Represents an abstract grid model that operates on an in-memory collection of items.
/// </summary>
/// <remarks>This class provides the foundation for grid models that manage their data in memory.  Derived classes
/// must implement the <see cref="GetData"/> method to provide access to the underlying collection.</remarks>
/// <typeparam name="TGridItem">The type of items contained in the grid.</typeparam>
public abstract class AMemGridModel<TGridItem> : AGridModel<TGridItem>, IUpdatableModel<TGridItem> where TGridItem : class
{
    /// <summary>A model with no column and no row.</summary>
    protected AMemGridModel() : base()
    {
    }

    /// <inheritdoc/>
    public override bool RequiresFetch() => false;

    /// <summary>
    /// Any modification made to the returned collection is reflected
    /// into the model as soon as the <see cref="AGridModel{TGridItem}.Sync"/> method is directly called or
    /// indirectly called.
    /// </summary>
    /// <returns>the model back-end collection.</returns>
    public abstract AList<TGridItem> GetData();

    /// <inheritdoc/>
    public sealed override ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
    {
        Sync();
        int rowCount = GetCurrentRowCount();   // the grid's rows, a pending insert included, which the view lacks
        return ValueTask.FromResult(Math.Max(0, Math.Min(count, rowCount - rowIndex)));
    }

    /// <summary>The index in <c>GetData()</c> of the row shown at <paramref name="viewIndex"/>; with <paramref name="insert"/>, where a row inserted there goes.</summary>
    protected abstract int ViewIndexToIndex(bool insert, int viewIndex);

    void IUpdatableModel<TGridItem>.Set(int viewIndex, TGridItem element)
    {
        GetData()[ViewIndexToIndex(false, viewIndex)] = element;
    }

    void IUpdatableModel<TGridItem>.Insert(int viewIndex, TGridItem element)
    {
        GetData().Insert(ViewIndexToIndex(true, viewIndex), element);
    }

    TGridItem IUpdatableModel<TGridItem>.Remove(int viewIndex)
    {
        var data = GetData();
        int index = ViewIndexToIndex(false, viewIndex);
        TGridItem res = data[index];
        data.RemoveAt(index);
        return res;
    }

    void IUpdatableModel<TGridItem>.FireModelChanged(GridModelChangeType changeType)
    {
        FireModelChanged(changeType);
    }
}
