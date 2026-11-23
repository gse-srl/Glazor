using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace Glazor.Grds;

/// <summary>The constants of <see cref="MultiSelGrid{TGridItem}"/>.</summary>
public static class MultiSelGrid
{
    /// <summary>
    /// The name of the selection column, in front of the host's columns. A column declared with it gives it a label and a size;
    /// it stays fixed, and neither sortable nor filterable.
    /// </summary>
    public const string SelColName = "$sel-col";
}

/// <summary>
/// A <see cref="Grigliaria{TGridItem}"/> with a checkbox column in front, driven by an <see cref="Mdls.ISelectionModel{TGridItem}"/>;
/// with an <see cref="Mdls.ISelectionModelWithSelectAll{TGridItem}"/> its header has a tri-state select-all. The host's model
/// keeps its own column indexes. See the MultiSelGrid section of docs/Technical_Documentation.md.
/// </summary>
[CascadingTypeParameter(nameof(TGridItem))]
public partial class MultiSelGrid<TGridItem> : IGrigliaria<TGridItem>
{
    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;
    private IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    private const string SelColName = MultiSelGrid.SelColName;

    [Inject] private ILogger<MultiSelGrid<TGridItem>> Logger { get; init; } = null!;

    /// <summary>
    /// Must be an IOrderedGridModel or an IDataGridModel: another model throws an ArgumentException at the first render.<br/>
    /// If the model implements both it is assumed to be an IOrderedGridModel, that's because when ordered filters and sorts are not permitted.
    /// </summary>
    [Parameter, EditorRequired]
    public IGridModel<TGridItem> Model { get; set; } = null!;

    /// <inheritdoc/>
    public IGridModel<TGridItem> GetModel() => Model;

    /// <summary>Required. What is selected.</summary>
    [Parameter, EditorRequired]
    public ISelectionModel<TGridItem> SelectionModel { get; set; } = null!;

    /// <summary>Raised when the selection changes. An <see cref="Action"/>: the handler calls <c>StateHasChanged</c> to re-render.</summary>
    [Parameter]
    public Action? OnSelectionChanged { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.OnModelChanged"/>
    [Parameter]
    public Action<GridModelChangeType>? OnModelChanged { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.OnStableItemChanged"/>
    [Parameter]
    public EventCallback<TGridItem?> OnStableItemChanged { get; set; }

    /// <inheritdoc/>
    [Parameter]
    public Func<GridTimeoutException, bool>? OnTimeout { get; set; }

    /// <summary>Forwarded to the inner grid: any delegate makes this component re-render with its parent, as on <see cref="AGrid{TGridItem}.ShouldReRenderTrick"/>.</summary>
    [Parameter]
    public Func<bool>? ShouldReRenderTrick { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.AriaLabel"/>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.Spinner"/>
    [Parameter]
    public ISpinner? Spinner { get; set; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.OnLayoutChanged"/>
    [Parameter]
    public Action? OnLayoutChanged { get; set; }

    /// <summary>Raised when a row was moved, on a model that supports reordering.</summary>
    [Parameter]
    public EventCallback OnRowShift { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowRowNumber"/>
    [Parameter]
    public bool ShowRowNumber { get; set; } = true;

    /// <inheritdoc cref="AGrid{TGridItem}.HeaderHeight"/>
    [Parameter]
    public int? HeaderHeight { get; set; } = 50;

    /// <inheritdoc cref="AGrid{TGridItem}.RowHeight"/>
    [Parameter]
    public int? RowHeight { get; set; } = 40;

    /// <inheritdoc cref="Grigliaria{TGridItem}.ChildContent"/>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.PageSize"/>
    [Parameter]
    public int PageSize { get; set; } = 5;

    /// <inheritdoc cref="AGrid{TGridItem}.GetPageSize"/>
    public int GetPageSize() => _dataGrid?.GetPageSize() ?? PageSize;

    /// <inheritdoc cref="AGrid{TGridItem}.RowsPopupEnabled"/>
    [Parameter]
    public bool RowsPopupEnabled { get; set; }
    /// <inheritdoc cref="AGrid{TGridItem}.OnRowPopup"/>
    [Parameter]
    public EventCallback<RowPopupEvent> OnRowPopup { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.ColumnsPopupEnabled"/>
    [Parameter]
    public bool ColumnsPopupEnabled { get; set; } = true;

    /// <inheritdoc cref="AGrid{TGridItem}.ShowNavigationButtons"/>
    [Parameter]
    public bool ShowNavigationButtons { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowHeader"/>
    [Parameter]
    public bool ShowHeader { get; set; } = true;

    /// <inheritdoc cref="AGrid{TGridItem}.ShowColumnGroups"/>
    [Parameter]
    public bool ShowColumnGroups { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.VertResizer"/>
    [Parameter]
    public IGridVertResizer? VertResizer { get; set; }

    /// <inheritdoc cref="AGrid{TGridItem}.ShowDefaultButtons"/>
    [Parameter]
    public bool ShowDefaultButtons { get; set; } = true;

    /// <inheritdoc cref="AGrid{TGridItem}.HCellTemplate"/>
    [Parameter]
    public RenderFragment<IGrid<TGridItem>.HCellTemplateParam>? HCellTemplate { get; set; }
    /// <inheritdoc cref="AGrid{TGridItem}.GHCellTemplate"/>
    [Parameter]
    public RenderFragment<IGrid<TGridItem>.GHCellTemplateParam>? GHCellTemplate { get; set; }
    /// <inheritdoc cref="AGrid{TGridItem}.EmptyRowCellTemplate"/>
    [Parameter]
    public RenderFragment<IGrid<TGridItem>.EmptyRowTemplateParam>? EmptyRowCellTemplate { get; set; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.CellCursorEnabled"/>
    [Parameter]
    public bool CellCursorEnabled { get; set; } = true;

    /// <inheritdoc cref="ADataGrid{TGridItem}.RowDialogEnabled"/>
    [Parameter]
    public bool RowDialogEnabled { get; set; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.ReloadEnabled"/>
    [Parameter]
    public bool ReloadEnabled { get; set; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.GotoLastKnown"/>
    [Parameter]
    public bool GotoLastKnown { get; set; } = true;

    /// <summary>Two-finger touch scrolling over the grid body; false unless set, as the configuration default does not apply here.</summary>
    [Parameter]
    public bool TouchScrollerEnabled { get; set; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.HorzResizer"/>
    [Parameter]
    public IGridHorzResizer? HorzResizer { get; set; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.Layoutable"/>
    [Parameter]
    public bool Layoutable { get; set; }

    /// <inheritdoc cref="ADataGrid{TGridItem}.FilterDefFactory"/>
    [Parameter]
    public FilterDefFactory? FilterDefFactory { get; set; }

    /// <inheritdoc cref="Grigliaria{TGridItem}.RowHeadingTemplate"/>
    [Parameter]
    public RenderFragment<IGrigliaria<TGridItem>.RowHeadingTemplateParam>? RowHeadingTemplate { get; set; }
    /// <inheritdoc cref="Grigliaria{TGridItem}.CellTemplate"/>
    [Parameter]
    public RenderFragment<IGrigliaria<TGridItem>.CellTemplateParam>? CellTemplate { get; set; }

    /// <summary>A cell, the checkbox column's included, as <c>MultiSelCellTemplate</c> and <c>DefaultMultiSelCell</c> receive it.</summary>
    /// <param name="Grid">The grid drawing the cell.</param>
    /// <param name="Model">The model the inner grid shows: the host's, with the selection column in front.</param>
    /// <param name="CellParam">The cell as the inner grid draws it; its <c>ColIdx</c> indexes <paramref name="Model"/>.</param>
    public record CellTemplateParam(MultiSelGrid<TGridItem> Grid, IDataGridModel<TGridItem> Model, IGrigliaria<TGridItem>.CellTemplateParam CellParam);
    /// <summary>Replaces how every cell is drawn, the checkbox column included; <c>DefaultMultiSelCell</c> draws a cell as the grid would.</summary>
    [Parameter]
    public RenderFragment<CellTemplateParam>? MultiSelCellTemplate { get; set; }

    /// <inheritdoc cref="Grigliaria{TGridItem}.OnDblClick"/>
    [Parameter]
    public EventCallback<DblClickEvent> OnDblClick { get; set; }

    /// <summary>Refuses changes to the selection.</summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    private readonly SelectedProperty<TGridItem> selected;

    private Grigliaria<TGridItem>? _dataGrid;
    private MSelModel<TGridItem>? _wrappedModel;

    /// <summary>A grid with nothing selected yet; the selection is <see cref="SelectionModel"/>'s.</summary>
    public MultiSelGrid()
    {
        selected = new(this);
    }

    /// <summary>
    /// Renders again; it can be called from any thread, as it goes through <c>InvokeAsync</c>.
    /// </summary>
    public async Task RepaintAsync()
    {
        await InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc/>
    public async ValueTask FocusAsync()
    {
        if (_dataGrid != null)
            await _dataGrid.FocusAsync();
    }

    private async Task DblClickAsync(DblClickEvent e)
    {
        if (!ReadOnly)
        {
            ToggleRowSelection(e.RowIdx);
        }
        _dataGrid!.SetCurrentRow(e.RowIdx);
        await OnDblClick.InvokeAsync(new DblClickEvent(e.RowIdx, e.ColIdx > 0 ? e.ColIdx - 1 : null));   // the inner grid counts the selection column
    }

    /// <summary>
    /// Whether the model is an <see cref="IOrderedGridModel{TGridItem}"/>, whose rows the user moves up and down; then the grid
    /// shows the move buttons, and neither filters nor sorts.
    /// </summary>
    public bool IsOrderable()
    {
        return _wrappedModel?._orderedModel != null;
    }

    // Asked at every render: a host's swap test may read both rows, so they must exist
    /// <summary>Whether the current row can move by <paramref name="delta"/> in an ordered model: the target row exists and the model lets the two swap.</summary>
    protected bool IsCurrentRowShiftable(int delta)
    {
        int crow = _wrappedModel.RequireNotNull().GetCurrentRow();
        int target = crow + delta;
        return crow >= 0 && target >= 0 && target < _wrappedModel.GetRowCount() && _wrappedModel.AreRowsSwappable(crow, target);
    }

    private async ValueTask ShiftCurrentRowAsync(int delta)
    {
        IOrderedGridModel<TGridItem> model = _wrappedModel.RequireNotNull()._orderedModel.RequireNotNull();
        int oldIdx = model.GetCurrentRow();
        int idx = oldIdx + delta;
        // Kept, but no real browser gets here: the click would have to leave before the render that disabled the button arrived
        if (oldIdx < 0 || idx < 0 || idx >= model.GetRowCount()
            || model.IsCurrentRowFrozen() || !IsCurrentRowShiftable(delta))
            return;
        model.SwapRows(oldIdx, idx);
        await OnRowShift.InvokeAsync();
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        IGridModel<TGridItem> model = Model.RequireNotNull();
        if (_wrappedModel == null || !Object.ReferenceEquals(_wrappedModel._wrapped, model))
        {
            _wrappedModel = model switch
            {
                IOrderedGridModel<TGridItem> oml => new MSelModel<TGridItem>(this, oml),
                IDataGridModel<TGridItem> dml => new MSelModel<TGridItem>(this, dml),
                _ => throw new ArgumentException($"The model must implement IOrderedGridModel or IDataGridModel: {model.GetType()}", nameof(Model))
            };
        }
    }

    /// <summary>The model the inner grid shows: <see cref="Model"/> with the selection column added.</summary>
    protected IDataGridModel<TGridItem> GetWrappedModel() => _wrappedModel.RequireNotNull();

    private bool ToggleCurrentRowSelection()
    {
        int rowIdx = Model.GetCurrentRow();
        return rowIdx >= 0 && ToggleRowSelection(rowIdx);
    }

    private bool ToggleRowSelection(int rowIdx)
    {
        if (ReadOnly)
            throw new InvalidOperationException("ReadOnly grid");
        if (Model.IsCurrentRowFrozen())
            return false;
        bool s = selected[rowIdx];
        if (!selected.Set(rowIdx, !s))
            return false;
        OnSelectionChanged?.Invoke();
        return true;
    }

    /// <summary>Called by the inner grid when its model changes: renders again and raises <see cref="OnModelChanged"/>.</summary>
    protected virtual void ModelChanged(GridModelChangeType changeType)
    {
        StateHasChanged();
        OnModelChanged?.Invoke(changeType);
    }

    /// <summary>Called by the inner grid when its layout changes: raises <see cref="OnLayoutChanged"/> and renders again.</summary>
    protected virtual void LayoutChanged()
    {
        OnLayoutChanged?.Invoke();
        StateHasChanged();
    }

    /// <inheritdoc/>
    public bool IsFiltered()
    {
        return _dataGrid?.IsFiltered() ?? false;
    }

    /// <inheritdoc cref="ADataGrid{TGridItem}.GetColumnsToShow"/>
    public IEnumerable<string> GetColumnsToShow() => _dataGrid.RequireNotNull().GetColumnsToShow();

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetColumnsToShow"/>
    public void SetColumnsToShow(params IEnumerable<string> columnsToShow) => _dataGrid.RequireNotNull().SetColumnsToShow(columnsToShow);

    /// <inheritdoc cref="ADataGrid{TGridItem}.EnsureColumnIsShown"/>
    public bool EnsureColumnIsShown(string columnName) => _dataGrid.RequireNotNull().EnsureColumnIsShown(columnName);

    /// <inheritdoc cref="AGrid{TGridItem}.GetCurrentCol"/>
    public string? GetCurrentCol() => _dataGrid.RequireNotNull().GetCurrentCol();

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetCurrentCol"/>
    public bool SetCurrentCol(string columnName) => _dataGrid.RequireNotNull().SetCurrentCol(columnName);

    /// <inheritdoc cref="ADataGrid{TGridItem}.GetFilter"/>
    public AFilterDef? GetFilter(string columnName) => _dataGrid.RequireNotNull().GetFilter(columnName);

    /// <inheritdoc cref="ADataGrid{TGridItem}.HasFilter"/>
    public bool HasFilter(string columnName) => _dataGrid.RequireNotNull().HasFilter(columnName);

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetFilter"/>
    public void SetFilter(string columnName, AFilterDef? filterDef) => _dataGrid.RequireNotNull().SetFilter(columnName, filterDef);

    /// <inheritdoc cref="ADataGrid{TGridItem}.GetFilters"/>
    public IReadOnlyDictionary<string, AFilterDef> GetFilters() => _dataGrid.RequireNotNull().GetFilters();

    /// <inheritdoc cref="ADataGrid{TGridItem}.SetFilters"/>
    public void SetFilters(IReadOnlyDictionary<string, AFilterDef> filters) => _dataGrid.RequireNotNull().SetFilters(filters);

    /// <inheritdoc cref="ADataGrid{TGridItem}.ShowFilterDialogAsync"/>
    public Task ShowFilterDialogAsync(string columnName, Action<FilterDefDlg.SubmitEvent>? onFormSubmit) =>
        _dataGrid.RequireNotNull().ShowFilterDialogAsync(columnName, onFormSubmit);

    /// <inheritdoc cref="ADataGrid{TGridItem}.ShowLayoutDialogAsync"/>
    public Task ShowLayoutDialogAsync() => _dataGrid.RequireNotNull().ShowLayoutDialogAsync();

    /// <inheritdoc cref="ADataGrid{TGridItem}.ShowRowDialogAsync"/>
    public Task ShowRowDialogAsync() => _dataGrid.RequireNotNull().ShowRowDialogAsync();

    /// <inheritdoc cref="AGrid{TGridItem}.StabilizeItemAsync"/>
    public async Task StabilizeItemAsync() => await _dataGrid.RequireNotNull().StabilizeItemAsync();

    /// <inheritdoc cref="AGrid{TGridItem}.GetStableItem"/>
    public TGridItem? GetStableItem() => _dataGrid.RequireNotNull().GetStableItem();

    /// <summary>
    /// The inner grid's layout, filters and sort, as <see cref="ADataGrid{TGridItem}.CreateMemento"/> saves them.
    /// </summary>
    public virtual Memento CreateMemento() => _dataGrid.RequireNotNull().CreateMemento();

    /// <summary>
    /// Puts back what <see cref="CreateMemento"/> saved, as <see cref="ADataGrid{TGridItem}.RestoreMemento"/> does.
    /// </summary>
    public virtual void RestoreMemento(Memento? memento) => _dataGrid.RequireNotNull().RestoreMemento(memento);

    /// <summary>
    /// Whether <see cref="SelectionModel"/> is an <see cref="ISelectionModelWithSelectAll{TGridItem}"/>; otherwise the select-all box
    /// in the header is disabled.
    /// </summary>
    public bool IsSelectAllSupported()
    {
        return SelectionModel is ISelectionModelWithSelectAll<TGridItem>;
    }

    // Select-all works on every candidate: under a filter it would reach rows the user cannot see
    private bool IsModelFiltered() => Model is IDataGridModel<TGridItem> dataModel && dataModel.IsFiltered();

    // The header box's binding: the box's Disabled carries the rules a caller from outside would skip
    internal bool? SelectAll
    {
        get
        {
            if (SelectionModel is ISelectionModelWithSelectAll<TGridItem> selAllModel && selAllModel.AllSelected())
                return true;
            return SelectionModel.SelectedCount() == 0 ? false : null;
        }
        set
        {
            if (ReadOnly)
                throw new InvalidOperationException("ReadOnly grid");
            if (SelectionModel is ISelectionModelWithSelectAll<TGridItem> selAllModel)
            {
                int before = selAllModel.SelectedCount();   // select-all only adds and deselect-all only removes
                switch (value)
                {
                    case true:
                        selAllModel.SelectAll();
                        break;
                    case false:
                        selAllModel.DeselectAll();
                        break;
                    case null:
                        return;
                }
                if (selAllModel.SelectedCount() != before)
                    OnSelectionChanged?.Invoke();
            }
            else
            {
                throw new InvalidOperationException($"Not supported: {value}");
            }
        }
    }

    internal sealed record SelectedProperty<MPT>(MultiSelGrid<MPT> Grid)
    {
        public bool this[int idx]
        {
            get
            {
                MPT? item = Grid.Model.GetItem(idx);
                return item != null && Grid.SelectionModel.IsSelected(item);
            }
            set
            {
                if (Set(idx, value))
                    Grid.OnSelectionChanged?.Invoke();
            }
        }

        internal bool Set(int idx, bool value)
        {
            if (Grid.ReadOnly)
                throw new InvalidOperationException("ReadOnly grid");
            if (Grid.Model.IsCurrentRowFrozen())
                return false;
            MPT? item = Grid.Model.GetItem(idx);
            item.RequireNotNull();
            if (!Grid.SelectionModel.IsSelectionEnabled(item))
                return false;   // its box is disabled, and Ctrl+Space or a double-click must not change it either
            Grid.SelectionModel.Select(item, value);
            return true;
        }
    }

    /// <summary>
    /// The model the inner grid shows: the host's model with the selection column in front.
    /// </summary>
    internal sealed class MSelModel<MPT> : IDataGridModel<MPT>, IOrderedGridModel<MPT>, IWrapsHostModel<MPT>
    {
        IGridModel<MPT> IWrapsHostModel<MPT>.HostModel => _wrapped;

        // column for check box
        private IColumn<MPT> _selectColumn;

        private readonly MultiSelGrid<MPT> _grid;

        internal readonly IGridModel<MPT> _wrapped;

        internal readonly IDataGridModel<MPT>? _dataModel;
        internal readonly IOrderedGridModel<MPT>? _orderedModel;

        /// <summary>
        /// Column for the selection check box
        /// </summary>
        private static Column<MPT, bool> NewSelectColumn()
        {
            return new Column<MPT, bool>("")
            {
                Alignment = Alignment.Center,
                PreferredSize = 4,
                Fixed = true,
                Filterable = false,
                Sortable = false,
            };
        }

        internal MSelModel(MultiSelGrid<MPT> grid, IDataGridModel<MPT> wrapped)
        {
            ArgumentNullException.ThrowIfNull(grid);
            ArgumentNullException.ThrowIfNull(wrapped);
            _grid = grid;
            _wrapped = wrapped;
            _dataModel = wrapped;
            _orderedModel = null;
            _selectColumn = NewSelectColumn();
        }

        internal MSelModel(MultiSelGrid<MPT> grid, IOrderedGridModel<MPT> wrapped)
        {
            ArgumentNullException.ThrowIfNull(grid);
            ArgumentNullException.ThrowIfNull(wrapped);
            _grid = grid;
            _wrapped = wrapped;
            _dataModel = null;
            _orderedModel = wrapped;
            _selectColumn = NewSelectColumn();
        }

        public event OnModelChangedEventHandler? OnModelChanged { add => _wrapped.OnModelChanged += value; remove => _wrapped.OnModelChanged -= value; }

        public void ApplyFilters(IEnumerable<KeyValuePair<string, AFilterDef>> filters)
        {
            if (_dataModel == null && !filters.Any())
                return;   // a restore clears what an ordered model never has
            _dataModel.RequireNotNull().ApplyFilters(filters);
        }

        public bool AreRowsSwappable(int row1, int row2)
        {
            return _orderedModel.RequireNotNull().AreRowsSwappable(row1, row2);
        }

        public bool CanDoContainsSearch()
        {
            return _dataModel != null && _dataModel.CanDoContainsSearch();
        }

        public bool CanSearchOnSortedColumn()
        {
            return _dataModel != null && _dataModel.CanSearchOnSortedColumn();
        }

        public async ValueTask<int> ContainsSearchAsync(string columnName, string value, bool forward, int startFrom, CancellationToken ct = default)
        {
            return await _dataModel.RequireNotNull().ContainsSearchAsync(columnName, value, forward, startFrom, ct);
        }

        public void EnrichColumn<T>(AGrid<MPT> grid, EnrichGridColumn<MPT, T> gridColumn)
        {
            if (!grid.CheckCollectingColumns(gridColumn.Name))
                return;
            if (gridColumn.Name == SelColName)
            {
                _selectColumn = _selectColumn.Enrich(gridColumn);
                return;
            }
            _wrapped.EnrichColumn(grid, gridColumn);
        }

        public void SetColumn<T>(AGrid<MPT> grid, GridColumn<MPT, T> gridColumn)
        {
            if (!grid.CheckCollectingColumns(gridColumn.Name))
                return;
            if (gridColumn.Name == SelColName)
            {
                _selectColumn = new Column<MPT>(gridColumn.GetColumn())
                {
                    Fixed = true,
                    Filterable = false,
                    Sortable = false,
                };
                return;
            }
            _wrapped.SetColumn(grid, gridColumn);
        }

        public void UnsetColumn(AGrid<MPT> grid, string columnName)
        {
            if (columnName == SelColName)
                throw new InvalidOperationException($"The selection column {SelColName} belongs to MultiSelGrid and cannot be removed");
            _wrapped.UnsetColumn(grid, columnName);
        }

        public bool RequiresFetch() => _wrapped.RequiresFetch();

        public async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
        {
            return await _wrapped.FetchRowsAsync(rowIndex, count, ct);
        }

        public IColumn<MPT> GetColumn(int columnIndex)
        {
            return columnIndex == 0 ? _selectColumn : _wrapped.GetColumn(columnIndex - 1);
        }

        public IColumnInfo<MPT> GetColumnInfo(int columnIndex)
        {
            // the selection column has no info: it is never filtered or sorted, so asking for it is a caller's error, not a lookup that can fail quietly
            return columnIndex == 0 ? throw new ArgumentOutOfRangeException(nameof(columnIndex)) : _wrapped.GetColumnInfo(columnIndex - 1);
        }

        public int GetColumnCount()
        {
            return _wrapped.GetColumnCount() + 1;
        }

        public int GetColumnsVersion()
        {
            return _wrapped.GetColumnsVersion();
        }

        public int GetColumnIndex(string columnName)
        {
            if (SelColName == columnName)
                return 0;
            int idx = _wrapped.GetColumnIndex(columnName);
            return idx < 0 ? idx : idx + 1;
        }

        public string GetColumnName(int columnIndex)
        {
            return columnIndex == 0 ? SelColName : _wrapped.GetColumnName(columnIndex - 1);
        }

        public MPT? GetCurrentItem()
        {
            return _wrapped.GetCurrentItem();
        }

        public void SetCurrentItem(MPT item)
            => _wrapped.SetCurrentItem(item);

        public int GetCurrentRow()
        {
            return _wrapped.GetCurrentRow();
        }

        public int GetCurrentRowCount()
        {
            return _wrapped.GetCurrentRowCount();
        }

        public int GetDataVersion()
        {
            return _wrapped.GetDataVersion();
        }

        public IReadOnlyList<MPT> GetDataView()
        {
            return _wrapped.GetDataView();
        }

        public IReadOnlyDictionary<string, AFilterDef> GetFilters()
        {
            return _dataModel != null ? _dataModel.GetFilters() : ImmutableDictionary<string, AFilterDef>.Empty;
        }

        public bool IsFiltered()
        {
            return _dataModel != null && _dataModel.IsFiltered();
        }

        public MPT GetItem(int rowIndex)
        {
            return _wrapped.GetItem(rowIndex);
        }

        public Type GetItemType()
        {
            return _wrapped.GetItemType();
        }

        public int GetRowCount()
        {
            return _wrapped.GetRowCount();
        }

        public string? GetSortedColumn()
        {
            return _dataModel?.GetSortedColumn();
        }

        public SortType GetSortType()
        {
            return _dataModel == null ? SortType.None : _dataModel.GetSortType();
        }

        public object? GetValue(MPT item, int columnIndex)
        {
            if (columnIndex == 0)
            {
                return _grid.SelectionModel.IsSelected(item);
            }
            return _wrapped.GetValue(item, columnIndex - 1);
        }

        public object? GetValueAt(int rowIndex, int columnIndex)
        {
            if (columnIndex == 0)
            {
                return _grid.selected[rowIndex];
            }
            return _wrapped.GetValueAt(rowIndex, columnIndex - 1);
        }

        public bool IsCurrentRowFrozen()
        {
            return _wrapped.IsCurrentRowFrozen();
        }

        public bool IsFilterable()
        {
            return _dataModel != null && _dataModel.IsFilterable();
        }

        public bool IsSortable()
        {
            return _dataModel != null && _dataModel.IsSortable();
        }

        public int RowIndexOf(MPT item)
        {
            return _wrapped.RowIndexOf(item);
        }

        public bool SameItem(MPT? item1, MPT? item2)
            => _wrapped.SameItem(item1, item2);

        public async ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default)
        {
            return await _dataModel.RequireNotNull().SearchOnSortedColumnAsync(value, ct);
        }

        public void SetCurrentRow(int rowIndex)
        {
            _wrapped.SetCurrentRow(rowIndex);
        }

        public void SetSortedColumn(string? columnName, bool ascending)
        {
            if (_dataModel == null && columnName == null)
                return;
            _dataModel.RequireNotNull().SetSortedColumn(columnName, ascending);
        }

        public void SwapRows(int row1, int row2)
        {
            _orderedModel.RequireNotNull().SwapRows(row1, row2);
        }

        public void Sync()
        {
            _wrapped.Sync();
        }

        public Func<MPT, object>? GetItemKeyFn()
            => _wrapped.GetItemKeyFn();
    }
}

