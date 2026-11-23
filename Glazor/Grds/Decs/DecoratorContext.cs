namespace Glazor.Grds.Decs;

/// <summary>
/// What a cell template, a cell renderer or a <c>CellClassFn</c> gets: the row's item, its value in the column, the row's index
/// in the grid's model and the column.
/// </summary>
/// <param name="Item">The row's item.</param>
/// <param name="Value">The item's value in the column.</param>
/// <param name="RowIdx">The row's index in the grid's model.</param>
/// <param name="Column">The column.</param>
public readonly record struct DecoratorContext<TGridItem, VT>(TGridItem Item, VT? Value, int RowIdx, IColumn<TGridItem> Column);
