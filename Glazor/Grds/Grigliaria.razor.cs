using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Glazor.Grds;

/// <summary>
/// Glazor's data grid: one page of rows at a time over an <see cref="Mdls.IDataGridModel{TGridItem}"/>, with per-column
/// filters, sort, a layout the user arranges and a <see cref="Memento"/> to persist them. Its columns are
/// <see cref="GridColumn{TGridItem, VT}"/> components in its markup, or columns the model declares in code. See docs/grigliaria.md.
/// </summary>
[CascadingTypeParameter(nameof(TGridItem))]
public partial class Grigliaria<TGridItem> : ADataGrid<TGridItem>, IGrigliaria<TGridItem>
{
    [Inject] private ILogger<Grigliaria<TGridItem>> Logger { get; init; } = null!;

    /// <summary>The row source. See docs/data-models.md.</summary>
    [Parameter, EditorRequired]
    public IDataGridModel<TGridItem> Model { get; set; } = null!;

    /// <inheritdoc/>
    public override IDataGridModel<TGridItem> GetModel() => Model;

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        Model.RequireNotNull();   // named, as the wrapper grids do: AGrid tolerates a null model
        base.OnParametersSet();
    }

    /// <summary>
    /// Each row has td that shows the current state of the row itself, ie. if it is the current one, if it is in edit, ..., use this to
    /// replace how it is drawn.
    /// </summary>
    [Parameter]
    public RenderFragment<IGrigliaria<TGridItem>.RowHeadingTemplateParam>? RowHeadingTemplate { get; set; }

    /// <summary>
    /// Use this to replace how a cell with data is drawn.
    /// </summary>
    [Parameter]
    public RenderFragment<IGrigliaria<TGridItem>.CellTemplateParam>? CellTemplate { get; set; }

    /// <summary>
    /// A row was double-clicked; not raised when the grid edits in place, where a double-click edits the row.
    /// ColIdx is set only if IsCellCursorEnabled().
    /// </summary>
    [Parameter]
    public EventCallback<DblClickEvent> OnDblClick { get; set; }

    /// <summary>The <c>GridColumn</c> and <c>EnrichGridColumn</c> declarations, directly or under <c>@if</c> and <c>@foreach</c>, not inside components of the host's own.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>The outer element, whose height the vertical resizer measures.</summary>
    protected ElementReference vertHeightRef;
    /// <summary>The focusable element that takes the grid's keys and touch scrolling.</summary>
    protected ElementReference mainDivRef;
    /// <summary>The table body, whose drawn size sets the page size and the columns that fit.</summary>
    protected ElementReference bodyRef;

    private string? _searchText;
    private ElementReference _searchPopupAnchor;
    private Popup? _searchPopup;
    private InputTextOnInput? _searchInput;

    /// <inheritdoc/>
    public override async ValueTask FocusAsync()
    {
        await mainDivRef.FocusAsync();
    }

    private bool CanSearchOnCurrentSortedColumn()
    {
        if (Model.CanSearchOnSortedColumn() && Model.GetRowCount() > 0 &&
            Model.IsSortable() && !Model.IsCurrentRowFrozen())
        {
            var sortedColumn = Model.GetSortedColumn();
            if (sortedColumn != null)
            {
                int colIdx = Model.GetColumnIndex(sortedColumn);
                return typeof(string).IsAssignableFrom(Model.GetColumn(colIdx).GetValueType());
            }
        }
        return false;
    }

    private async ValueTask SearchOnCurrentSortedColumnAsync(KeyAction.KeyEvent evt)
    {
        if (Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("SearchOnSortedColumn: {evt}", evt);
        if (evt.Chars != null && _searchPopup.RequireNotNull().IsVisible)
        {
            SearchText = _searchText + evt.Chars;   // typed while the popup opened: they follow the characters that opened it
            return;
        }
        _searchText = evt.Chars;
        if (_searchText == null)
            return;
        await _searchPopup.RequireNotNull().ShowAsync();
        SearchOnCurrentSortedColumnImpl(_searchText);
    }

    private async ValueTask SearchOnCurrentSortedColumnAsync()
    {
        var current = Model.GetCurrentItem();
        if (current != null)
        {
            string sortedColumn = Model.GetSortedColumn().RequireNotNull();
            _searchText = Model.GetValue(current, Model.GetColumnIndex(sortedColumn))?.ToString() ?? "";
        }
        else
            _searchText = "";
        await _searchPopup.RequireNotNull().ShowAsync();
        _lastSearchedText = _searchText;   // the current row already starts with it
    }

    private ValueTask SearchPopupClosedAsync()
    {
        // Required NOP so the KeyAction enabled state is refreshed
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Typing in the search box is debounced: on a paged model every search may fetch rows, so a burst of
    /// keystrokes must cost one search, not one per character.
    /// </summary>
    private readonly System.Timers.Timer _searchTimer = new(300) { AutoReset = false };
    private string? _lastSearchedText;

    /// <summary>Sets up the timer that runs the incremental search once typing pauses.</summary>
    public Grigliaria()
    {
        _searchTimer.Elapsed += (_, _) => InvokeAsync(async () =>
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return;   // see the stabilizer timer in AGrid: the callback may be queued when the grid is disposed
            try
            {
                string txt = _searchText ?? "";
                if (txt != _lastSearchedText)
                    SearchOnCurrentSortedColumnImpl(txt);
                StateHasChanged();
            }
            catch (Exception ex)
            {
                await DispatchExceptionAsync(ex);   // as from a key: a timeout OnTimeout declined, or a failed query, ends the circuit
            }
        });
    }

    private string? SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        _searchTimer.Stop();
        _searchTimer.Dispose();
        DropEditContext();
        await base.DisposeAsync();
    }

    // Descending, the rows that start with the text come before the insertion point; an exact match is any of its equals
    private int FirstStartingWith(int idx, string txt)
    {
        int colIdx = Model.GetColumnIndex(Model.GetSortedColumn().RequireNotNull());
        while (idx > 0 && Model.GetValue(Model.GetItem(idx - 1), colIdx) is string prev && prev.StartsWith(txt, StringComparison.CurrentCultureIgnoreCase))
            idx--;
        return idx;
    }

    private void SearchOnCurrentSortedColumnImpl(string txt)
    {
        if (!CanSearchOnCurrentSortedColumn())
            return;   // a key or the timer may come after an edit started, or the sort changed, before the render that disables them
        _lastSearchedText = txt;
        if (Model.RequiresFetch())
        {
            try
            {
                var mres = _spinnerHelper.Monitor<int>(Spinner.RequireNotNull(), (ct) => Task.Run<int>(async () =>
                    {
                        return await Model.SearchOnSortedColumnAsync(txt, ct);
                    }),
                    _cancellationTokenSource.Token
                );
                int idx = mres.Task.Result;
                if (idx < 0)
                    idx = (-idx) - 1;
                idx = FirstStartingWith(idx, txt);
                int currentRow = Math.Min(idx, Model.GetRowCount() - 1);
                Model.SetCurrentRow(currentRow);
                if (mres.EndMode == SpinnerHelper.EndMode.OUT_OF_TIME)
                {
                    StateHasChanged();
                }
            }
            catch (GridTimeoutException e)
            {
                if (!Timeout(e))
                    throw;
            }
        }
        else
        {
            int idx = AsyncHelper.RunSync(() => Model.SearchOnSortedColumnAsync(txt).AsTask());
            if (idx < 0)
                idx = (-idx) - 1;
            idx = FirstStartingWith(idx, txt);
            int currentRow = Math.Min(idx, Model.GetRowCount() - 1);
            Model.SetCurrentRow(currentRow);
        }
    }
}
