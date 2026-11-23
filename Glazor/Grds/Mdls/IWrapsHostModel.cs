namespace Glazor.Grds.Mdls;

// The model a wrapper grid hands its inner grid, around the host's
internal interface IWrapsHostModel<TGridItem>
{
    IGridModel<TGridItem> HostModel { get; }
}
