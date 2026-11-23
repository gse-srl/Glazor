using Glazor.Grds.Filters;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds;

/// <summary>
/// A grid over an <see cref="Mdls.IDataGridModel{TGridItem}"/>: what <see cref="IGrid{TGridItem}"/> has, plus the cell cursor,
/// filters, the layout and the touch scroller.
/// </summary>
public interface IDataGrid<TGridItem> : IGrid<TGridItem>
{
    /// <inheritdoc cref="ADataGrid{TGridItem}.CellCursorEnabled"/>
    public bool CellCursorEnabled { get; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.GotoLastKnown"/>
    public bool GotoLastKnown { get; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.TouchScrollerEnabled"/>
    public bool TouchScrollerEnabled { get; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.HorzResizer"/>
    public IGridHorzResizer? HorzResizer { get; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.Layoutable"/>
    public bool Layoutable { get; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.OnLayoutChanged"/>
    public Action? OnLayoutChanged { get; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.FilterDefFactory"/>
    public FilterDefFactory? FilterDefFactory { get; }

    /// <summary>Whether a filter that is not void is applied to any column.</summary>
    public bool IsFiltered();
}
