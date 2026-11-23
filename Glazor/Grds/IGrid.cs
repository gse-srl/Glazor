using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds;

/// <summary>
/// Common properties for all grids, we use an interface because blazor works better with object composition.
/// </summary>
public interface IGrid<TGridItem>
{
    /// <summary>The model the grid shows, its <c>Model</c> parameter.</summary>
    public IGridModel<TGridItem> GetModel();

    /// <inheritdoc cref="AGrid{TGridItem}.PageSize"/>
    public int PageSize { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.GetPageSize"/>
    public int GetPageSize();

    /// <inheritdoc cref="AGrid{TGridItem}.RowsPopupEnabled"/>
    public bool RowsPopupEnabled { get; }

    /// <summary>
    /// The row context menu requested, with the row and where the pointer is; the row is null below the last one.
    /// </summary>
    public EventCallback<RowPopupEvent> OnRowPopup { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.ColumnsPopupEnabled"/>
    public bool ColumnsPopupEnabled { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowRowNumber"/>
    public bool ShowRowNumber { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowNavigationButtons"/>
    public bool ShowNavigationButtons { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowHeader"/>
    public bool ShowHeader { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowColumnGroups"/>
    public bool ShowColumnGroups { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.HeaderHeight"/>
    public int? HeaderHeight { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.RowHeight"/>
    public int? RowHeight { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.VertResizer"/>
    public IGridVertResizer? VertResizer { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowDefaultButtons"/>
    public bool ShowDefaultButtons { get; }

    /// <summary>A column's header cell, as <see cref="HCellTemplate"/> and <c>DefaultHCell</c> receive it.</summary>
    /// <param name="Grid">The grid drawing the header.</param>
    /// <param name="Model">The grid's model.</param>
    /// <param name="ColIdx">The column's index in the model.</param>
    /// <param name="Column">The column.</param>
    /// <param name="ColSpan">The cell's <c>colspan</c>.</param>
    /// <param name="RowSpan">The cell's <c>rowspan</c>: 2 for a column in no group while the column groups are shown, else 1.</param>
    /// <param name="ToShow">Where the column stands among those shown, so the header can tell which way it can move.</param>
    public record HCellTemplateParam(AGrid<TGridItem> Grid, IGridModel<TGridItem> Model, int ColIdx, IColumn<TGridItem> Column, int ColSpan, int RowSpan,
        ColumnPlace ToShow);
    /// <inheritdoc cref="AGrid{TGridItem}.HCellTemplate"/>
    public RenderFragment<HCellTemplateParam>? HCellTemplate { get; }

    /// <summary>A column group's header cell, above its columns, as <see cref="GHCellTemplate"/> and <c>DefaultGHCell</c> receive it.</summary>
    /// <param name="Grid">The grid drawing the header.</param>
    /// <param name="Model">The grid's model.</param>
    /// <param name="GroupId">The group, as the columns' <c>GroupId</c> names it.</param>
    /// <param name="ColSpan">The cell's <c>colspan</c>: the group's columns shown side by side.</param>
    /// <param name="RowSpan">The cell's <c>rowspan</c>.</param>
    /// <param name="ColIdxs">The model indexes of the group's columns under the cell.</param>
    public record GHCellTemplateParam(AGrid<TGridItem> Grid, IGridModel<TGridItem> Model, string GroupId, int ColSpan, int RowSpan, params IReadOnlyList<int> ColIdxs);
    /// <inheritdoc cref="AGrid{TGridItem}.GHCellTemplate"/>
    public RenderFragment<GHCellTemplateParam>? GHCellTemplate { get; }

    /// <summary>
    /// The heading cell of a filler row, one that fills the page below the last row or stands for an empty model, as
    /// <see cref="EmptyRowCellTemplate"/> and <c>DefaultEmptyRow</c> receive it.
    /// </summary>
    /// <param name="Grid">The grid drawing the row.</param>
    /// <param name="Model">The grid's model.</param>
    /// <param name="RowIdx">The place the filler row takes, past the last row of the model.</param>
    public record EmptyRowTemplateParam(AGrid<TGridItem> Grid, IGridModel<TGridItem> Model, int RowIdx);
    /// <inheritdoc cref="AGrid{TGridItem}.EmptyRowCellTemplate"/>
    public RenderFragment<EmptyRowTemplateParam>? EmptyRowCellTemplate { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.OnModelChanged"/>
    public Action<GridModelChangeType>? OnModelChanged { get; }

    /// <summary>
    /// Called when a data fetch runs past the spinner's time budget, before the exception is allowed to
    /// escape into the render path. Only a model that fetches asynchronously (such as EFDataGridModel) can reach this — an
    /// in-memory one never does.
    /// </summary>
    /// <remarks>
    /// Carries <see cref="GridTimeoutException.WorkStopped"/>, which is what makes the decision
    /// answerable: <c>false</c> means the fetch that timed out is still running, and still writing to the
    /// model. <see cref="GridTimeoutException.Wait"/> gives it longer and updates that flag.
    /// </remarks>
    /// <returns>
    /// <c>false</c>, the default, to let the exception propagate: that tears the circuit down and leaves
    /// Blazor's reload prompt, which is the safe outcome because nothing reads the model afterwards.
    /// <c>true</c> to say it is handled, in which case this render continues immediately — so returning
    /// <c>true</c> while <see cref="GridTimeoutException.WorkStopped"/> is <c>false</c> paints a model
    /// that a background thread is concurrently writing. Wait for it, or let it propagate.
    /// </returns>
    public Func<GridTimeoutException, bool>? OnTimeout { get; }

    /// <summary>
    /// Stabilizes the current item immediately, as if it hasn't been changed for a while, 
    /// triggering the <see cref="OnStableItemChanged"/> callback if the stable item has changed.
    /// </summary>
    public Task StabilizeItemAsync();

    /// <summary>
    /// Gets the current stable item, ie. the item that is currently selected and that isn't changed for a while. 
    /// </summary>
    public TGridItem? GetStableItem();

    /// <summary>
    /// Gets or sets the callback that is invoked when the current item in the grid stabilizes, ie. it isn't changed for a while.
    /// Very useful to trigger actions that require a stable item, like showing details of the current item in a side panel, without 
    /// triggering them on every single change of the current item.
    /// </summary>
    public EventCallback<TGridItem?> OnStableItemChanged { get; }

    /// <inheritdoc cref="AGrid{TGridItem}.Spinner"/>
    public ISpinner? Spinner { get; }
}

/// <summary>
/// Where a header cell's column stands in <c>GetColumnsToShow()</c>, for a column that is not fixed: <see cref="Idx"/> is its
/// position, <see cref="FirstNotFixedIdx"/> that of the first column not fixed, so a header can tell which way it can move.
/// </summary>
/// <param name="FirstNotFixedIdx">The position of the first column not fixed.</param>
/// <param name="Idx">The column's position.</param>
public record ColumnPlace(int FirstNotFixedIdx, int Idx);

/// <summary>A row's context menu requested, as <c>OnRowPopup</c> reports it.</summary>
/// <param name="RowIdx">The row's index in the model; null below the last row.</param>
/// <param name="ClientX">The pointer's horizontal position in the viewport, as <c>Popup.ShowAsync</c> takes it.</param>
/// <param name="ClientY">The pointer's vertical position in the viewport.</param>
public record RowPopupEvent(int? RowIdx, int ClientX, int ClientY);
