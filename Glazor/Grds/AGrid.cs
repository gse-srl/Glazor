using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Glazor.Grds;

/// <summary>
/// The base of the grids: the model, the page of rows shown and its size, the header, the spinner during a fetch, and the
/// registration of the markup columns. Its protected members are for subclasses and may change between releases. See the
/// Extending Grigliaria section of docs/grigliaria.md.
/// </summary>
public abstract class AGrid<TGridItem> : ComponentBase, IGrid<TGridItem>, IAsyncDisposable
{
    [Inject] private ILogger<AGrid<TGridItem>> Logger { get; init; } = null!;

    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;

    /// <summary>The texts the grid shows: the host's <see cref="IGlazorTexts"/> service, or the built-in one.</summary>
    protected IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    private int _pageIdx = -1;

    /// <summary>Rows rendered per page; a <see cref="VertResizer"/>, when set, computes it instead.</summary>
    [Parameter]
    public int PageSize { get; set; } = 5;

    // What the VertResizer computed at the last render, when there is one. Kept apart from the PageSize parameter,
    // which belongs to the parent and must not be overwritten by the component.
    private int? _resizedPageSize;
    private readonly PageSizeSettler _pageSizeSettler = new();

    /// <summary>
    /// The page size in effect: the one the <see cref="VertResizer"/> computed at the last render when there is one,
    /// otherwise the <see cref="PageSize"/> parameter.
    /// </summary>
    public int GetPageSize() => _resizedPageSize ?? PageSize;

    private int _columnsToShowCount;
    private int _firstExcludedIdx;
    private int _deltaIdx;
    private int _horzDisplayedUnits = -1;
    private IReadOnlyDictionary<string, NoValue>? _shownColumns;

    private string? _columnToShow;

    /// <summary>Enables the row context menu, reported through <see cref="OnRowPopup"/>.</summary>
    [Parameter]
    public bool RowsPopupEnabled { get; set; }

    /// <summary>
    /// The row context menu requested, with the row and where the pointer is; the row is null below the last one.
    /// </summary>
    [Parameter]
    public EventCallback<RowPopupEvent> OnRowPopup { get; set; }

    /// <summary>Shows the current/total row counter in the footer.</summary>
    [Parameter]
    public bool ShowRowNumber { get; set; } = true;

    /// <summary>Shows the first row, previous page, previous row, next row, next page and last row buttons in the footer; rendered only with <see cref="ShowDefaultButtons"/>.</summary>
    [Parameter]
    public bool ShowNavigationButtons { get; set; }

    /// <summary>Renders the column header row.</summary>
    [Parameter]
    public bool ShowHeader { get; set; } = true;

    /// <summary>Renders the group header row above the headers, for the columns that share a <c>GroupId</c>.</summary>
    [Parameter]
    public bool ShowColumnGroups { get; set; }

    /// <summary>The accessible name of the grid's table, <c>aria-label</c>: a page with more than one grid names each.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>The header row's height in pixels.</summary>
    [Parameter]
    public int? HeaderHeight { get; set; } = 50;

    /// <summary>A data row's height in pixels; <see cref="GridVertResizers.RowHeight"/> needs it.</summary>
    [Parameter]
    public int? RowHeight { get; set; } = 40;

    /// <summary>Enables the per-column menu on a right-click of a header: shift the column, sort, filter, clear the filter.</summary>
    [Parameter]
    public bool ColumnsPopupEnabled { get; set; } = true;

    /// <summary>Computes the page size from the height available. See <see cref="GridVertResizers"/>.</summary>
    [Parameter]
    public IGridVertResizer? VertResizer { get; set; }

    /// <summary>Shows the built-in footer controls: the edit, insert and remove buttons with an <c>EditMode</c>, the search button,
    /// the row dialog button with <c>RowDialogEnabled</c>, the reload button with <c>ReloadEnabled</c>, the horizontal scroller and, with <see cref="ShowNavigationButtons"/>,
    /// the navigation buttons.</summary>
    [Parameter]
    public bool ShowDefaultButtons { get; set; } = true;

    /// <summary>Replaces how the header cells are drawn.</summary>
    [Parameter]
    public RenderFragment<IGrid<TGridItem>.HCellTemplateParam>? HCellTemplate { get; set; }

    /// <summary>Replaces how the group header cells are drawn.</summary>
    [Parameter]
    public RenderFragment<IGrid<TGridItem>.GHCellTemplateParam>? GHCellTemplate { get; set; }

    /// <summary>Replaces the heading cell of each filler row below the last row: one <c>td</c>, the grid draws the rest of the row.</summary>
    [Parameter]
    public RenderFragment<IGrid<TGridItem>.EmptyRowTemplateParam>? EmptyRowCellTemplate { get; set; }

    /// <summary>Raised when the model changes. An <see cref="Action"/>, raised from synchronous paths: the handler calls <c>StateHasChanged</c> to re-render.</summary>
    [Parameter]
    public Action<GridModelChangeType>? OnModelChanged { get; set; }

    /// <summary>Raised when the current item has stayed put long enough to count as settled, to drive a detail panel without following every key.</summary>
    [Parameter]
    public EventCallback<TGridItem?> OnStableItemChanged { get; set; }

    /// <summary>
    /// Required for models like EFDataGridModel
    /// </summary>
    [Parameter]
    public ISpinner? Spinner { get; set; }

    /// <inheritdoc/>
    [Parameter]
    public Func<GridTimeoutException, bool>? OnTimeout { get; set; }

    internal SpinnerHelper _spinnerHelper = new();   // replaced by the tests with one on a short budget

    /// <summary>
    /// So the child component is drawn whenever the parent is drawn.
    /// trick https://stackoverflow.com/questions/67447396/blazor-statehaschanged-not-updating-child-components
    /// </summary>
    [Parameter]
    public Func<bool>? ShouldReRenderTrick { get; set; }

    private string? _currentCol;

    private bool _collectingColumns;
    private readonly Dictionary<string, object> _columnOwners = new();   // the markup component that registered each column last
    private readonly HashSet<string> _unregisteredColumns = new();   // until the next render takes them out of the model

    private (double width, double height) _lastSize = (640, 240);

    private readonly System.Timers.Timer _stabilizerTimer;
    private TGridItem? _stableItem;
    private bool _reportNextStableItem;   // the host was last told of a row of a model since replaced, so even null is news
    // The current item as read when the stabilizer timer fires; compared with _stableItem to raise OnStableItemChanged.
    private TGridItem? _tmpItem;

    // What the columns to show were computed from: code columns change the model before the grid renders, not during
    private IGridModel<TGridItem>? _columnsModel;
    private int? _columnsVersion;

    internal readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

    /// <summary>Sets up the timer that reports the current item once the selection settles.</summary>
    protected AGrid()
    {
        _stabilizerTimer = new System.Timers.Timer(100)
        {
            AutoReset = false
        };
        _stabilizerTimer.Elapsed += (_, _) => InvokeAsync(async () =>
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return;   // kept, but no real browser gets here: the timer would have to fire during the render that disposes the grid
            try
            {
                await ApplyStableSelectionTimer();
            }
            catch (Exception ex)
            {
                await DispatchExceptionAsync(ex);
            }
        });
    }

    /// <summary>A subclass owning disposable state overrides it, releases that state, and ends with <c>await base.DisposeAsync()</c>.</summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Disposing: {thisType}", this.GetType());
        _cancellationTokenSource.Cancel();  // No async and no dispose (we let the GC do its job), we just flag the token
        _subscribedModel?.OnModelChanged -= OnModelChangedHandler;
        _subscribedModel = null;
        _stabilizerTimer.Stop();
        _stabilizerTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    private IGridModel<TGridItem>? _subscribedModel;
    /// <inheritdoc/>
    /// <remarks>The grid subscribes to its model here: an override calls the base.</remarks>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        // avoid double subscription to the model's OnModelChanged event
        var model = GetModel();
        if (ReferenceEquals(_subscribedModel, model))
            return;
        // A column added in code registered on the model, which knows no grid: it is validated here, a markup one as it registers
        for (int i = 0; i < (model?.GetColumnCount() ?? 0); i++)
            ValidateColumn(model!.GetColumnName(i), model.GetColumn(i));
        _subscribedModel?.OnModelChanged -= OnModelChangedHandler;
        bool sameRows = ReferenceEquals(Unwrapped(_subscribedModel), Unwrapped(model));   // a new wrapper over the same model
        _subscribedModel = model;
        if (!sameRows)
        {
            _reportNextStableItem |= _stableItem != null;
            _stableItem = default;   // the old model's row, though a row of the new one may have its key
        }
        model?.OnModelChanged += OnModelChangedHandler;
        if (model != null)
            RestartSelectionTimer();   // a model already in sync raises no change at its first render
    }

    private static IGridModel<TGridItem>? Unwrapped(IGridModel<TGridItem>? model)
    {
        while (model is GridModelWrapper<TGridItem> wrapper)
            model = wrapper._gridModel;
        return model;
    }

    private void OnModelChangedHandler(GridModelChangeType changeType)
    {
        ModelChanged(changeType);
    }

    /// <summary>
    /// Called when the model changes, before <see cref="OnModelChanged"/> is raised: a subclass overrides it to act on every
    /// change, as a memento saved when the model settles, and calls the base.
    /// </summary>
    protected virtual void ModelChanged(GridModelChangeType changeType)
    {
        RestartSelectionTimer();
        OnModelChanged?.Invoke(changeType);
    }

    /// <summary>Called when a fetch times out; returns what <see cref="OnTimeout"/> returns, false without one, to let the exception propagate.</summary>
    protected virtual bool Timeout(GridTimeoutException e)
    {
        if (OnTimeout != null)
        {
            return OnTimeout(e);
        }
        return false;
    }

    /// <summary>Whether the grid is collecting its markup columns now; false, logged, when a column registers outside that pass.</summary>
    protected internal bool CheckCollectingColumns(string? columnName)
    {
        if (!_collectingColumns)
        {
            // Cannot throw it, otherwise sometimes:Microsoft.AspNetCore.Components.Rendering.ComponentState.RenderIntoBatch(RenderBatchBuilder batchBuilder, RenderFragment renderFragment, Exception& renderFragmentException)
            if (Logger.IsEnabled(LogLevel.Debug))
                Logger.LogDebug("The grid is not collecting columns now: {columnName}", columnName);
            return false;
        }
        return true;
    }

    /// <summary>Records the component that registered <paramref name="columnName"/>, so the column goes when that component goes.</summary>
    protected internal void ColumnRegistered(string columnName, object owner) => _columnOwners[columnName] = owner;

    /// <summary>
    /// A markup column whose component was removed from the tree goes in the next render, once the markup columns have
    /// registered, unless another component registered the name. A grid torn down renders no more, so a model the host keeps
    /// keeps its columns.
    /// </summary>
    protected internal void ColumnUnregistered(string columnName, object owner)
    {
        if (!_columnOwners.TryGetValue(columnName, out object? registered) || !ReferenceEquals(registered, owner))
            return;
        _columnOwners.Remove(columnName);
        _unregisteredColumns.Add(columnName);
        StateHasChanged();
    }

    // Right before the columns are computed: cells rendered earlier in the batch still read the model they were built for
    private void UnsetUnregisteredColumns()
    {
        IGridModel<TGridItem> model = GetModel();
        if (_unregisteredColumns.Count == 0 || model.IsCurrentRowFrozen())   // a pending edit refuses the change: a later render retries
            return;
        foreach (string columnName in _unregisteredColumns)
        {
            if (!_columnOwners.ContainsKey(columnName))
                model.UnsetColumn(this, columnName);
        }
        _unregisteredColumns.Clear();
    }

    /// <summary>Throws when the grid cannot show <paramref name="column"/>; called for each column of a new model and each column added since.</summary>
    protected internal virtual void ValidateColumn(string columnName, IColumn<TGridItem> column)
    {
    }

    /// <summary>Starts the render pass in which the markup columns register.</summary>
    protected void StartCollectingColumns()
    {
        _collectingColumns = true;
    }

    /// <summary>Ends the pass <see cref="StartCollectingColumns"/> started: removes the columns whose component went, and recomputes the columns to show when the model's columns changed.</summary>
    protected void EndCollectingColumns()
    {
        _collectingColumns = false;
        UnsetUnregisteredColumns();
        IGridModel<TGridItem> model = GetModel();
        int modelVersion = model.GetColumnsVersion();
        if (!ReferenceEquals(_columnsModel, model) || _columnsVersion != modelVersion)
        {
            if (ReferenceEquals(_columnsModel, model))   // a column added in code since, checked as OnParametersSet checks a new model's
                for (int i = 0; i < model.GetColumnCount(); i++)
                    ValidateColumn(model.GetColumnName(i), model.GetColumn(i));
            _columnsModel = model;
            _columnsVersion = modelVersion;
            ComputeColumnsToShow(true);
        }
    }

    /// <summary>
    /// Renders again; it can be called from any thread, as it goes through <c>InvokeAsync</c>.
    /// </summary>
    public async Task RepaintAsync()
    {
        await InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc/>
    public abstract IGridModel<TGridItem> GetModel();

    /// <summary>The index of the first row on the page shown; -1 before the first render.</summary>
    public int GetPageIdx() => _pageIdx;

    /// <summary>The display units the horizontal resizer gave at the last render; -1 without one, when every column is shown.</summary>
    public int GetHorzDisplayedUnits() => _horzDisplayedUnits;

    /// <summary>The horizontal resizer in effect, or null when the grid shows every column at once.</summary>
    public abstract IGridHorzResizer? GetHorzResizer();

    /// <summary>Records the size the browser measured; a null dimension keeps the last one.</summary>
    protected void Resized(double? width, double? height)
    {
        if (width == null && height == null)
            throw new InvalidOperationException("Neither a width nor a height was given");
        _lastSize = (width ?? _lastSize.width, height ?? _lastSize.height);
        _heightMeasured |= height != null;
    }

    /// <summary>The size last recorded by <see cref="Resized"/>.</summary>
    protected (double width, double height) GetLastSize() => _lastSize;

    // The heights the grid drew, its body's and its whole content's, each with the page size it was drawn with
    private bool _heightMeasured;
    private double _bodyHeight;
    private double _contentHeight;
    private int _bodyRows;
    private int _contentRows;
    private int _renderedPageSize;   // of the last render the browser applied: what it measures was drawn with it
    private readonly Queue<int> _pageSizesSent = new();
    private (int Rows, double Body, double Content)? _drawn;
    private (int Rows, double Body, double Content)? _drawnBefore;   // the last one drawn with another page size

    /// <summary>
    /// Records what the grid drew: the height of its body, the rows of the page, or of its whole content, header and footer
    /// included. Either may be null when only the other was measured.
    /// </summary>
    protected void Drawn(double? bodyHeight, double? contentHeight)
    {
        if (bodyHeight != null)
            (_bodyHeight, _bodyRows) = (bodyHeight.Value, _renderedPageSize);
        if (contentHeight != null)
            (_contentHeight, _contentRows) = (contentHeight.Value, _renderedPageSize);
        // The two are reported one at a time, each followed by a render: only heights drawn with one page size go together,
        // and only once the grid's own height is known, as before it the page size was a guess
        if (!_heightMeasured || _bodyRows <= 0 || _bodyRows != _contentRows || _bodyHeight <= 0 || _contentHeight <= _bodyHeight)
            return;
        if (_drawn?.Rows != _bodyRows)
            _drawnBefore = _drawn;
        else if (_drawn?.Body != _bodyHeight)
            _drawnBefore = null;   // the rows changed height: a page drawn before no longer gives a row's height
        _drawn = (_bodyRows, _bodyHeight, _contentHeight);
    }

    /// <summary>
    /// The page size last drawn, the height of the content it drew and the height a row takes, as measured: null until the body
    /// and the content have been measured with one page size. A row's height is exact once two page sizes have been drawn, the
    /// body's growth per row; before, the body's height per row, which counts its own extras in and so is a little more.
    /// </summary>
    public (int Rows, double Content, double RowHeight)? GetDrawnRows()
    {
        if (_drawn is not (int rows, double body, double content))
            return null;
        double rowHeight = body / rows;
        if (_drawnBefore is (int rowsBefore, double bodyBefore, _) && (body - bodyBefore) / (rows - rowsBefore) is > 0 and var growth)
            rowHeight = growth;
        return (rows, content, rowHeight);
    }

    // We have to call SyncBeforeRendering() explicitly in our razor file because the generated BuildRenderTree() doesn't call the base one.

    /// <inheritdoc/>
    protected override bool ShouldRender() => _spinnerHelper.IsIdle();

    // Raised once the browser has applied the render, which it acknowledges before it reports sizes measured on it; a size
    // measured on an earlier render, which may arrive after a later one was sent, so keeps the page size it was drawn with
    /// <inheritdoc/>
    /// <remarks>The grid records the page size the browser drew here: an override calls the base.</remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        if (_pageSizesSent.TryDequeue(out int pageSize))
            _renderedPageSize = pageSize;
    }

    /// <summary>Whether no fetch is in flight; the grid does not render while one is.</summary>
    public bool IsIdle() => _spinnerHelper.IsIdle();

    /// <summary>
    /// Must be explicitly called in the razor file before generating any html content.<br/>
    /// </summary>
    protected virtual void SyncBeforeRendering()
    {
        _resizedPageSize = VertResizer == null ? null :
            _pageSizeSettler.Settle(_lastSize.height, VertResizer.GetPageSize(this, _lastSize.height), Environment.TickCount64);
        if (GetPageSize() < 1)
            throw new InvalidOperationException($"The page size must be at least 1: {GetPageSize()}");
        _pageSizesSent.Enqueue(GetPageSize());
        _horzDisplayedUnits = GetHorzResizer()?.GetDisplayedUnits(this, _lastSize.width) ?? -1;
        if (_horzDisplayedUnits < 0)
            _deltaIdx = 0;   // the header adds it to its column indexes
        IGridModel<TGridItem> model = GetModel();
        if (model.RequiresFetch())
        {
            try
            {
                int version = model.GetDataVersion();
                int startRow = -1;
                var mres = _spinnerHelper.Monitor<(int, int)>(Spinner.RequireNotNull(), (ct) => Task.Run<(int, int)>(async () =>
                    {
                        await model.FetchRowsAsync(0, 0, ct);   // a cold model's first query and restore, under the token
                        int crowIdx = startRow = model.GetCurrentRow();   // the getter may query: under the spinner
                        int rowCount = model.GetRowCount();
                        if (crowIdx < 0 && rowCount > 0)
                        {
                            crowIdx = 0;
                        }
                        int pageIdx = _pageIdx;
                        int pageSize = GetPageSize();
                        pageIdx = Math.Min(pageIdx, crowIdx);
                        if ((pageIdx + pageSize) <= crowIdx)
                            pageIdx = (crowIdx + 1) - pageSize;
                        pageIdx = Math.Max(0, Math.Min(pageIdx, rowCount - pageSize));
                        for (int i = 0; i < 2; i++)
                        {
                            pageIdx = Math.Min(pageIdx, crowIdx);
                            if ((pageIdx + pageSize) <= crowIdx)
                                pageIdx = (crowIdx + 1) - pageSize;
                            pageIdx = Math.Max(0, Math.Min(pageIdx, rowCount - pageSize));
                            int rowsToFetch = Math.Min(rowCount - pageIdx, pageSize);
                            if (await model.FetchRowsAsync(pageIdx, rowsToFetch, ct) < rowsToFetch)
                            {
                                if (i >= 1)
                                    throw new InvalidOperationException("The model fetched fewer rows than it counts, a second time");
                                // We reached the end, so
                                // ... by contract now getRowCount must return the real counter
                                rowCount = model.GetRowCount();
                                // ... and a valid current row 
                                crowIdx = model.GetCurrentRow();
                                if (crowIdx < 0 && rowCount > 0)
                                    crowIdx = 0;
                                // ... we cycle once again to correctly position the _pageIdx
                            }
                            else
                            {
                                break;
                            }
                        }
                        return (crowIdx, pageIdx);
                    }),
                    _cancellationTokenSource.Token
                );
                (int crowIdx, int pageIdx) = mres.Task.Result;
                if (model.GetCurrentRow() == startRow)   // a sort in memory during the fetch moves the current row with its record
                    model.SetCurrentRow(crowIdx);
                else   // and the page follows it: a fetch only adds rows, so it moves down
                    pageIdx = Math.Max(pageIdx, model.GetCurrentRow() + 1 - GetPageSize());
                _pageIdx = pageIdx;
                model.Sync();
                if (version != model.GetDataVersion())
                {
                    ModelChanged(GridModelChangeType.AfterSync);
                }
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
            int crowIdx = model.GetCurrentRow();
            int rowCount = model.GetRowCount();
            if (crowIdx < 0 && rowCount > 0)
            {
                model.SetCurrentRow(0);
                crowIdx = 0;
            }
            _pageIdx = Math.Min(_pageIdx, crowIdx);
            int pageSize = GetPageSize();
            if ((_pageIdx + pageSize) <= crowIdx)
                _pageIdx = (crowIdx + 1) - pageSize;
            _pageIdx = Math.Max(0, Math.Min(_pageIdx, rowCount - pageSize));
            model.Sync();
        }
        //
        SyncShownColumns();
    }

    /// <summary>The model's column at <paramref name="columnIndex"/>, as the grid shows it: with the size its layout sets, if any.</summary>
    public virtual IColumn<TGridItem> GetColumn(int columnIndex) => GetModel().GetColumn(columnIndex);

    /// <returns>the names of the columns the grid shows, in order, the fixed ones first. With a horizontal resizer only some
    /// are in its window: <see cref="GetShownColumns"/>.</returns>
    public abstract IEnumerable<string> GetColumnsToShow();

    /// <param name="columnName">the column has been removed from the model</param>
    protected virtual void ColumnRemovedFromModel(String columnName)
    {
    }

    /// <summary>
    /// Scrolls a grid with a horizontal resizer so that the model's column <paramref name="columnIndex"/> is in its window.
    /// </summary>
    /// <returns>true when the column is already in view; false when the grid scrolls to it at the next render.</returns>
    /// <exception cref="ArgumentException">The column is not one of <see cref="GetColumnsToShow"/>.</exception>
    public bool EnsureColumnIsShown(int columnIndex)
    {
        string colName = GetModel().GetColumnName(columnIndex)
            ?? throw new ArgumentException($"Column does not exist: {columnIndex}");
        if (!GetColumnsToShow().Contains(colName))
            throw new ArgumentException($"Not a showable column: {colName}");
        if (_shownColumns == null || _horzDisplayedUnits < 0)
            return true;
        SyncShownColumns();   // on the current order: the last render's window may predate a reorder
        if (_shownColumns.ContainsKey(colName))
            return true;
        _columnToShow = colName;
        StateHasChanged();
        return false;
    }

    /// <summary>
    /// The names of the columns rendered: with a horizontal resizer the fixed ones and those in its window, without one the
    /// same as <see cref="GetColumnsToShow"/>.
    /// </summary>
    public IEnumerable<string> GetShownColumns()
    {
        if (_horzDisplayedUnits < 0)
            return GetColumnsToShow();
        return _shownColumns!.Keys;
    }

    /// <summary>Recomputes the columns to show: after the columns registered, or when none is set to show.</summary>
    protected abstract void ComputeColumnsToShow(bool afterCollectingColumns);


    /// <summary>
    /// The cell cursor does not rest on a fixed column: on one, it moves to the first column that is not fixed, if any. On a
    /// column no longer shown, or with none yet, it moves there too, or to the first column when every one shown is fixed.
    /// </summary>
    private void KeepCurrentColOnShownColumns(IEnumerable<string> cols)
    {
        IGridModel<TGridItem> model = GetModel();
        string? first = null;
        string? firstNotFixed = null;
        bool currentIsFixed = false;
        bool currentIsShown = false;
        foreach (string colName in cols)
        {
            int colIdx = model.GetColumnIndex(colName);
            if (colIdx < 0)
                continue;
            bool isFixed = GetColumn(colIdx).IsFixed();
            first ??= colName;
            if (colName == _currentCol)
            {
                currentIsFixed = isFixed;
                currentIsShown = true;
            }
            if (!isFixed)
                firstNotFixed ??= colName;
        }
        if (currentIsFixed && firstNotFixed != null)
            _currentCol = firstNotFixed;
        else if (!currentIsShown)
            _currentCol = firstNotFixed ?? first;
    }

    private void SyncShownColumns()
    {
        IEnumerable<string> cols = GetColumnsToShow();
        if (!cols.Any())
        {
            ComputeColumnsToShow(false);
            cols = GetColumnsToShow();
        }
        if (_horzDisplayedUnits < 0)
        {
            _shownColumns = null;
            _columnToShow = null;
            if (IsCellCursorEnabled())
                KeepCurrentColOnShownColumns(cols);
            return;
        }
        bool isCellCursorEnabled = IsCellCursorEnabled();
        string[] colNames = new string[cols.Count()];
        int[] colIdxs = new int[cols.Count()];
        int toBeShownIdx = -1;
        IGridModel<TGridItem> model = GetModel();
        {
            int i = 0;
            foreach (string colName in cols)
            {
                colIdxs[i] = model.GetColumnIndex(colName);
                if (colIdxs[i] < 0)
                {
                    ColumnRemovedFromModel(colName);
                }
                else
                {
                    colNames[i] = colName;
                    if (toBeShownIdx < 0 && _columnToShow != null && _columnToShow.Equals(colName))
                        toBeShownIdx = i;
                    i++;
                }
            }
            if (i < colNames.Length)
            {
                string[] oColNames = colNames;
                colNames = new string[i];
                Array.Copy(oColNames, 0, colNames, 0, i);
                int[] oColIdxs = colIdxs;
                colIdxs = new int[i];
                Array.Copy(oColIdxs, 0, colIdxs, 0, i);
            }
            _columnToShow = null;
        }
        int totPSizes = 0;
        OrderedDictionary<string, NoValue> showed = new(colIdxs.Length);
        // fixed columns, if any
        int notFixedIdx = colIdxs.Length;  // the first not fixed col, "worst" case, all columns are fixed
        for (int i = 0; i < colIdxs.Length; i++)
        {
            IColumn<TGridItem> col = GetColumn(colIdxs[i]);
            if (col.IsFixed())
            {
                totPSizes += col.GetPreferredSize();
                showed.TryAdd(colNames[i], NoValue.Value);
            }
            else
            {
                notFixedIdx = i;
                break;
            }
        }
        if (notFixedIdx >= colIdxs.Length)
        {   // only fixed columns, as the layout dialog can leave, or none, as without a HorzResizer: all shown, nothing scrolls
            _deltaIdx = 0;
            _firstExcludedIdx = -1;
            _columnsToShowCount = colIdxs.Length;
            _shownColumns = showed;
            if (isCellCursorEnabled && colNames.Length > 0 && Array.IndexOf(colNames, _currentCol) < 0)
                _currentCol = colNames[0];
            return;
        }
        if (notFixedIdx + _deltaIdx >= colIdxs.Length)
            _deltaIdx = Math.Max(0, colIdxs.Length - notFixedIdx - 1);
        if (toBeShownIdx >= notFixedIdx)
        {  // there is a column to show and it is not a fixed col
            int dix = toBeShownIdx - notFixedIdx;
            if (dix < _deltaIdx)
            {
                _deltaIdx = dix;
            }
            else
            {
                _deltaIdx = dix;
                int idx = _deltaIdx;
                int tot = totPSizes;
                for (int i = idx; i >= 0; i--)
                {
                    IColumn<TGridItem> col = GetColumn(colIdxs[notFixedIdx + i]);
                    tot += col.GetPreferredSize();
                    if (tot <= _horzDisplayedUnits)
                    {
                        _deltaIdx = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        // not fixed columns
        _firstExcludedIdx = -1;
        _columnsToShowCount = colIdxs.Length;
        for (int i = notFixedIdx + _deltaIdx; i < colIdxs.Length; i++)
        {
            IColumn<TGridItem> col = GetColumn(colIdxs[i]);
            totPSizes += col.GetPreferredSize();
            if (totPSizes > _horzDisplayedUnits)
            {
                _firstExcludedIdx = i;
                break;
            }
        }
        // try to add columns from the left, this is required when the browser resizes
        if (_deltaIdx > 0 && _firstExcludedIdx < 0)
        {
            int idx = _deltaIdx;
            for (int i = idx - 1; i >= 0; i--)
            {
                IColumn<TGridItem> col = GetColumn(colIdxs[notFixedIdx + i]);
                totPSizes += col.GetPreferredSize();
                if (totPSizes <= _horzDisplayedUnits)
                {
                    _deltaIdx = i;
                }
                else
                {
                    break;
                }
            }
        }
        // here we are, add columns 
        int upTo = _firstExcludedIdx >= 0 ? _firstExcludedIdx : colIdxs.Length;
        if (upTo <= notFixedIdx + _deltaIdx)   // not even the window's first column fits beside the fixed ones: shown anyway
        {
            upTo = notFixedIdx + _deltaIdx + 1;
            _firstExcludedIdx = upTo < colIdxs.Length ? upTo : -1;   // the column shown anyway is not excluded
        }
        for (int i = notFixedIdx + _deltaIdx; i < upTo; i++)
        {
            showed.TryAdd(colNames[i], NoValue.Value);
        }
        _shownColumns = showed;
        if (isCellCursorEnabled)
        {
            int idx = Array.IndexOf(colNames, _currentCol);
            // Math.Clamp(idx, notFixedIdx + _deltaIdx, upTo - 1) may throw an exception
            idx = Math.Max(notFixedIdx + _deltaIdx, Math.Min(upTo - 1, idx));
            _currentCol = colNames[idx];
        }
    }

    /// <summary>Whether the grid tracks a current column as well as the current row; a subclass may decide it dynamically.</summary>
    public virtual bool IsCellCursorEnabled() => false;

    /// <summary>The name of the current column; null when the cell cursor is not enabled.</summary>
    public string? GetCurrentCol() => IsCellCursorEnabled() ? _currentCol : null;

    internal void SetCurrentColImpl(string columnName)
    {
        if (!IsCellCursorEnabled())
            throw new InvalidOperationException();
        int idx = GetModel().GetColumnIndex(columnName);
        if (idx < 0)
            throw new ArgumentException($"Column not found: {columnName}");
        EnsureColumnIsShown(idx);
        _currentCol = columnName;
        StateHasChanged();
    }

    /// <summary>The model's current row index; less than 0 when there is none.</summary>
    public int GetCurrentRow() => GetModel().GetCurrentRow();

    internal int GetColumnsToShowCount() => _columnsToShowCount;
    internal int GetFirstExcludedColIdx() => _firstExcludedIdx;
    internal int GetDeltaColIdx() => _deltaIdx;

    /// <summary>
    /// Scrolls the columns horizontally, with a <see cref="ADataGrid{TGridItem}.HorzResizer"/>: the first column shown after
    /// the fixed ones becomes the one at this offset among the columns to show that are not fixed, 0 the first. It is not a
    /// model column index, as <see cref="EnsureColumnIsShown(int)"/> takes.
    /// </summary>
    public virtual void ScrollToCol(int firstScrollingOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstScrollingOffset);
        if (_horzDisplayedUnits >= 0 && firstScrollingOffset != _deltaIdx)
        {
            _deltaIdx = firstScrollingOffset;
            StateHasChanged();
        }
    }

    /// <summary>Whether <see cref="PrevCol"/> can scroll: the grid has a horizontal resizer and columns hidden on the left.</summary>
    public virtual bool CanPrevCol() => _horzDisplayedUnits >= 0 && _deltaIdx > 0;
    /// <summary>Scrolls the columns that are not fixed one column to the left, when <see cref="CanPrevCol"/>.</summary>
    public virtual void PrevCol()
    {
        if (CanPrevCol())
        {
            _deltaIdx--;
            StateHasChanged();
        }
    }

    /// <summary>Whether <see cref="NextCol"/> can scroll: the grid has a horizontal resizer and columns hidden on the right.</summary>
    public virtual bool CanNextCol() => _horzDisplayedUnits >= 0 && _firstExcludedIdx >= 0;
    /// <summary>Scrolls the columns that are not fixed one column to the right, when <see cref="CanNextCol"/>.</summary>
    public virtual void NextCol()
    {
        if (CanNextCol())
        {
            _deltaIdx++;
            StateHasChanged();
        }
    }

    /// <summary>Moves by <paramref name="delta"/> pages, the current row with them; false when the current row is frozen.</summary>
    protected bool MovePage(int delta)
    {
        IGridModel<TGridItem> model = GetModel();
        if (model.IsCurrentRowFrozen())
            return false;
        int pageSize = GetPageSize();
        _pageIdx = Math.Max(0, Math.Min(model.GetRowCount() - 1, _pageIdx + (delta * pageSize)));
        model.SetCurrentRow(Math.Min(model.GetRowCount() - 1, Math.Max(0, model.GetCurrentRow() + (delta * pageSize))));
        StateHasChanged();
        return true;
    }

    /// <summary>Scrolls so the page starts at row <paramref name="pageIdx"/>, keeping the current row in view; false when the current row is frozen.</summary>
    protected bool ScrollTo(int pageIdx)
    {
        IGridModel<TGridItem> model = GetModel();
        if (model.IsCurrentRowFrozen())
            return false;
        int tableSize = model.GetRowCount();
        _pageIdx = Math.Max(0, Math.Min(tableSize - 1, pageIdx));
        if (tableSize > 0)
            model.SetCurrentRow(Math.Min(_pageIdx + GetPageSize() - 1, Math.Max(_pageIdx, model.GetCurrentRow())));
        StateHasChanged();
        return true;
    }

    /// <summary>Scrolls by <paramref name="dx"/> columns, with horizontal paging on, and <paramref name="dy"/> rows, keeping the current row in view; false when the current row is frozen.</summary>
    protected internal bool ScrollBy(int dx, int dy)
    {
        IGridModel<TGridItem> model = GetModel();
        if (model.IsCurrentRowFrozen())
            return false;
        if (_horzDisplayedUnits >= 0)   // horizontal paging on, as PrevCol and NextCol require
            _deltaIdx = Math.Max(0, _deltaIdx + dx);
        int pageIdx = _pageIdx + dy;
        int tableSize = model.GetRowCount();
        _pageIdx = Math.Max(0, Math.Min(pageIdx, tableSize - 1));
        if (tableSize > 0)
        {
            model.SetCurrentRow(Math.Max(_pageIdx,
                Math.Min(model.GetCurrentRow(), _pageIdx + GetPageSize() - 1)));
        }
        StateHasChanged();
        return true;
    }

    /// <summary>Gives the grid the keyboard focus, so that its keys work.</summary>
    public abstract ValueTask FocusAsync();

    private void RestartSelectionTimer()
    {
        _stabilizerTimer.Stop();
        _stabilizerTimer.Interval = _stableItem == null ? 100 : 500;  // less frequent when mooving between selections
        _stabilizerTimer.Start();
        // The current item is read when the timer fires (StabilizeItemAsync), not here: a row just set beyond the loaded
        // window of a paged model is fetched by the render, under the spinner, before the timer runs.
    }

    private async Task ApplyStableSelectionTimer()
    {
        _stabilizerTimer.Stop();
        await StabilizeItemAsync();
    }

    internal async Task StableItemChangedAsync(TGridItem? selected)
    {
        bool same = !_reportNextStableItem && GetModel().SameItem(_stableItem, selected);
        _reportNextStableItem = false;
        _stableItem = selected;   // a reload keeps the key and replaces the instance
        if (!same)
        {
            await OnStableItemChanged.InvokeAsync(selected);
            StateHasChanged();
        }
    }

    /// <summary>
    /// Settles the current item at once, without waiting for the stabilizer: <see cref="OnStableItemChanged"/> is raised if it
    /// differs from the stable item, with null when the current item is no longer among the model's rows.
    /// </summary>
    public async Task StabilizeItemAsync()
    {
        _tmpItem = GetModel().GetCurrentItem();
        // Is the selected element still in the dataview?
        // RowIndexOf() scans the loaded rows only (see AGridModel), so the worst case here is chunk reads
        // from the spill files, never a query. It is also rare: we are stabilizing the current item, which
        // is normally already resident, so the fast path in RowIndexOf catches it.
        if (_tmpItem != null && GetModel().RowIndexOf(_tmpItem) < 0)
        {
            await StableItemChangedAsync(default);
        }
        else
        {
            await StableItemChangedAsync(_tmpItem);
        }
    }

    /// <summary>The item <see cref="OnStableItemChanged"/> last reported, null before the first or when none was current.</summary>
    public TGridItem? GetStableItem() => _stableItem;

    /// <summary>Opens the header menu of the column at <paramref name="columnIndex"/> at the given point.</summary>
    protected internal virtual async Task ShowColumnPopupAsync(int columnIndex, Popup columnPopup, int x, int y)
    {
        await columnPopup.ShowAsync(x, y);
    }

    /// <summary>Called when a header menu closes; does nothing by default.</summary>
    protected internal virtual Task ColumnPopupClosedAsync()
    {
        return Task.CompletedTask;
    }

    private Popup? _columnPopup;   // the header menu opened last

    /// <summary>Whether a column's header menu is open: the grid's own keys and wheel are off meanwhile.</summary>
    protected bool IsColumnPopupOpen() => _columnPopup?.IsVisible == true;

    internal async Task OpenColumnPopupAsync(int columnIndex, Popup columnPopup, int x, int y)
    {
        await ShowColumnPopupAsync(columnIndex, columnPopup, x, y);
        _columnPopup = columnPopup;
        StateHasChanged();   // the grid's keys go off while the menu is open
    }

    internal async Task CloseColumnPopupAsync()
    {
        StateHasChanged();   // and come back when it closes
        await ColumnPopupClosedAsync();
    }
}
