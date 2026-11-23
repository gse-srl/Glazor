namespace Glazor.Grds.Mdls;

/// <summary>
/// Internal interface to standardize updates on models hidden data structures.
/// </summary>
internal interface IUpdatableModel<TGridItem> : IGridModel<TGridItem>
{
    internal void Set(int index, TGridItem element);

    internal void Insert(int index, TGridItem element);

    internal TGridItem Remove(int index);

    internal void FireModelChanged(GridModelChangeType changeType);
}
