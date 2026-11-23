namespace Glazor.Grds.Mdls;

/// <summary>
/// Marks a paged model whose rows may not all be read while the grid renders: a cell whose <c>GetItem</c> throws
/// <see cref="ArgumentOutOfRangeException"/> or <see cref="IndexOutOfRangeException"/> shows "..." instead of failing the render.
/// </summary>
public interface IAsyncGridModel<TGridItem> : IGridModel<TGridItem>
{
}
