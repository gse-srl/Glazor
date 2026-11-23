using Glazor.Grds.Mdls;

namespace Glazor.Grds;

/// <summary>
/// A grid whose layout the user changes through <see cref="LayoutDefDlg{TGridItem}"/>.
/// </summary>
public interface ILayoutable<TGridItem>
{
    /// <inheritdoc cref="ADataGrid{TGridItem}.OnLayoutChanged"/>
    Action? OnLayoutChanged { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.GetColumn"/>
    IColumn<TGridItem> GetColumn(int columnIndex);

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetColumnsToShow"/>
    void SetColumnsToShow(params IEnumerable<string> columnsToShow);

    /// <inheritdoc cref="ADataGrid{TGridItem}.GetColumnsToShow"/>
    IEnumerable<string> GetColumnsToShow();

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetPreferredSize"/>
    void SetPreferredSize(string columnName, int? size);

    /// <inheritdoc cref="IGrid{TGridItem}.GetModel"/>
    IGridModel<TGridItem> GetModel();

    /// <inheritdoc cref="ADataGrid{TGridItem}.EnsureColumnIsShown"/>
    bool EnsureColumnIsShown(string columnName);
    /// <summary>Whether the grid has a cell cursor, as its <c>CellCursorEnabled</c> parameter says.</summary>
    bool IsCellCursorEnabled();
    /// <inheritdoc cref="ADataGrid{TGridItem}.SetCurrentCol"/>
    bool SetCurrentCol(string columnName);
}
