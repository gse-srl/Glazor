using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds;

/// <summary>
/// What <see cref="Grigliaria{TGridItem}"/>, <see cref="MultiSelGrid{TGridItem}"/> and <see cref="Treegliaria{TGridItem}"/> share, so
/// host code can take any of them: the columns shown and the current column, the filters, the filter, layout and row dialogs,
/// focus and repaint, the row-heading and cell templates, and <see cref="OnDblClick"/>. A grid of your own implements it by
/// deriving from one of the three, which may gain members in a later release.
/// </summary>
public interface IGrigliaria<TGridItem> : IDataGrid<TGridItem>
{
    /// <inheritdoc cref="AGrid{TGridItem}.GetColumnsToShow"/>
    public IEnumerable<string> GetColumnsToShow();

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetColumnsToShow"/>
    public void SetColumnsToShow(params IEnumerable<string> columnsToShow);

    /// <inheritdoc cref="ADataGrid{TGridItem}.EnsureColumnIsShown"/>
    public bool EnsureColumnIsShown(string columnName);

    /// <inheritdoc cref="AGrid{TGridItem}.GetCurrentCol"/>
    public string? GetCurrentCol();

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetCurrentCol"/>
    public bool SetCurrentCol(string columnName);

    /// <inheritdoc cref="ADataGrid{TGridItem}.GetFilter"/>
    public AFilterDef? GetFilter(string columnName);

    /// <inheritdoc cref="ADataGrid{TGridItem}.HasFilter"/>
    public bool HasFilter(string columnName);

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetFilter"/>
    public void SetFilter(string columnName, AFilterDef? filterDef);

    /// <inheritdoc cref="ADataGrid{TGridItem}.GetFilters"/>
    public IReadOnlyDictionary<string, AFilterDef> GetFilters();

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetFilters"/>
    public void SetFilters(IReadOnlyDictionary<string, AFilterDef> filters);

    /// <inheritdoc cref="ADataGrid{TGridItem}.ShowFilterDialogAsync"/>
    public Task ShowFilterDialogAsync(string columnName, Action<FilterDefDlg.SubmitEvent>? onFormSubmit);

    /// <inheritdoc cref="ADataGrid{TGridItem}.ShowLayoutDialogAsync"/>
    public Task ShowLayoutDialogAsync();

    /// <inheritdoc cref="ADataGrid{TGridItem}.ShowRowDialogAsync"/>
    public Task ShowRowDialogAsync();

    /// <inheritdoc cref="AGrid{TGridItem}.FocusAsync"/>
    public ValueTask FocusAsync();

    /// <inheritdoc cref="AGrid{TGridItem}.RepaintAsync"/>
    public Task RepaintAsync();

    /// <summary>A row's heading cell, as <see cref="RowHeadingTemplate"/> and <c>DefaultRowHeading</c> receive it.</summary>
    /// <param name="Grid">The grid drawing the row.</param>
    /// <param name="Model">The grid's model.</param>
    /// <param name="RowIdx">The row's index in the model.</param>
    public record RowHeadingTemplateParam(Grigliaria<TGridItem> Grid, IDataGridModel<TGridItem> Model, int RowIdx);
    /// <summary>
    /// Each row has td that shows the current state of the row itself, ie. if it is the current one, if it is in edit, ..., use this to 
    /// replace how it is drawn.
    /// </summary>
    public RenderFragment<RowHeadingTemplateParam>? RowHeadingTemplate { get; }

    /// <summary>A data cell, as <see cref="CellTemplate"/> and <c>DefaultCell</c> receive it.</summary>
    /// <param name="Grid">The grid drawing the cell.</param>
    /// <param name="Model">The grid's model.</param>
    /// <param name="RowIdx">The row's index in the model.</param>
    /// <param name="ColIdx">The column's index in the model.</param>
    /// <param name="Column">The column.</param>
    public record CellTemplateParam(Grigliaria<TGridItem> Grid, IDataGridModel<TGridItem> Model, int RowIdx, int ColIdx, IColumn<TGridItem> Column);
    /// <summary>
    /// Use this to replace how a cell with data is drawn.
    /// </summary>
    public RenderFragment<CellTemplateParam>? CellTemplate { get; }

    /// <summary>
    /// A row was double-clicked; not raised when the grid edits in place, where a double-click edits the row.
    /// ColIdx is set only if IsCellCursorEnabled().
    /// </summary>
    public EventCallback<DblClickEvent> OnDblClick { get; }
}

/// <summary>The row double-clicked, and its column when the grid tracks a current cell.</summary>
/// <param name="RowIdx">The row's index in the model.</param>
/// <param name="ColIdx">The column's index in the model; null without a cell cursor, or on a double-click of the row heading.</param>
public record DblClickEvent(int RowIdx, int? ColIdx);
