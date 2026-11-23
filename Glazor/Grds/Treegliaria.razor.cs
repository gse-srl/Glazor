using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Grds.Props;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace Glazor.Grds;

/// <summary>The constants of <see cref="Treegliaria{TGridItem}"/>.</summary>
public static class Treegliaria
{
    /// <summary>
    /// The name of the expansion column the tree has without a <c>TreeColumn</c>. A column declared with it gives it a value and
    /// a size; it stays fixed, and neither sortable nor filterable.
    /// </summary>
    public const string ExpColName = "$exp-col";
}

/// <summary>
/// A <see cref="Treegliaria{TGridItem}"/>'s memento over an <see cref="ITreeStateGridModel{TGridItem}"/>: the grid's layout, filters
/// and sort, and its <see cref="TreeState"/>, the nodes expanded and the one zoomed on. As JSON it carries
/// <c>"$type": "tree"</c>, so it reads back as itself through a <see cref="Memento"/> too.
/// </summary>
/// <param name="Layout">The columns shown and the sizes the user set; null for a grid that is not layoutable.</param>
/// <param name="FiltersDefs">The filters applied, by column name.</param>
/// <param name="SorterColumns">The column the grid is sorted by, if any; empty when it is not sorted.</param>
/// <param name="Tree">The nodes expanded and the one zoomed on.</param>
public record TreeMemento(GridLayout? Layout, IReadOnlyDictionary<string, AFilterDef> FiltersDefs, IReadOnlyList<SortedColumn> SorterColumns,
    TreeState Tree) : Memento(Layout, FiltersDefs, SorterColumns);

/// <summary>
/// A <see cref="Grigliaria{TGridItem}"/> over hierarchical rows: an <see cref="Mdls.ITreeGridModel{TGridItem}"/> gives the rows in
/// pre-order, and a column in front expands and collapses them. The host's model keeps its own column indexes. See the
/// Treegliaria section of docs/Technical_Documentation.md.
/// </summary>
[CascadingTypeParameter(nameof(TGridItem))]
public partial class Treegliaria<TGridItem> : IGrigliaria<TGridItem>
{
    private const string ExpColName = Treegliaria.ExpColName;

    /// <summary>Required. The rows of the tree, normally a <see cref="TreeGridModel{TGridItem}"/>.</summary>
    [Parameter, EditorRequired]
    public ITreeGridModel<TGridItem> Model { get; set; } = null!;

    /// <inheritdoc/>
    public IGridModel<TGridItem> GetModel() => Model;

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

    /// <summary>A cell, the expansion column's included, as <c>TreeCellTemplate</c> and <c>DefaultTreeCell</c> receive it.</summary>
    /// <param name="Grid">The grid drawing the cell.</param>
    /// <param name="Model">The model the inner grid shows: the host's, with the expansion column in front unless a <c>TreeColumn</c> draws the tree.</param>
    /// <param name="CellParam">The cell as the inner grid draws it; its <c>ColIdx</c> indexes <paramref name="Model"/>.</param>
    public record CellTemplateParam(Treegliaria<TGridItem> Grid, ITreeGridModel<TGridItem> Model, IGrigliaria<TGridItem>.CellTemplateParam CellParam);
    /// <summary>Replaces how every cell is drawn, the expansion column included; <c>DefaultTreeCell</c> draws a cell as the tree would.</summary>
    [Parameter]
    public RenderFragment<CellTemplateParam>? TreeCellTemplate { get; set; }

    /// <inheritdoc cref="Grigliaria{TGridItem}.OnDblClick"/>
    [Parameter]
    public EventCallback<DblClickEvent> OnDblClick { get; set; }

    /// <summary>
    /// A column of the model that draws the tree: the indent and the expander go in front of its value, in place of a column of
    /// their own, and the column, shown fixed in front of the columns that are not, keeps its own sort, filter, size and label; a
    /// fixed column declared before it stays in front of it. Null, the tree has its own expansion column,
    /// <see cref="Treegliaria.ExpColName"/>.
    /// </summary>
    [Parameter]
    public string? TreeColumn { get; set; }

    /// <summary>
    /// Over a folder model, see <see cref="IFolderGridModel{TGridItem}.IsFolderModel"/>, a bar above the header with buttons to zoom in on the current folder
    /// and out of it, and the path to the folder zoomed on; true unless set. Off, the keys still zoom.
    /// </summary>
    [Parameter]
    public bool ShowZoomBar { get; set; } = true;

    /// <summary>
    /// The text of a folder in the path of the zoom bar, see <see cref="ShowZoomBar"/>; by default the value of
    /// <see cref="TreeColumn"/>, or of the expansion column given one, or else that of the first column shown.
    /// </summary>
    [Parameter]
    public Func<TGridItem, string>? ZoomLabel { get; set; }

    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;
    private IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    private Grigliaria<TGridItem>? _dataGrid;
    private WrappedModel<TGridItem>? _wrappedModel;

    /// <summary>A tree grid; its rows come from <see cref="Model"/>.</summary>
    public Treegliaria()
    {
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

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        ITreeGridModel<TGridItem> model = Model.RequireNotNull();
        if (_wrappedModel == null || !Object.ReferenceEquals(_wrappedModel._dataGridModel, model) || _wrappedModel.TreeColumn != TreeColumn)
        {
            _wrappedModel = new WrappedModel<TGridItem>(this, model, TreeColumn);
        }
    }

    /// <summary>The model the inner grid shows: <see cref="Model"/> with the expander column added.</summary>
    protected TreeGridModelWrapper<TGridItem> GetWrappedModel() => _wrappedModel.RequireNotNull();

    // The column whose cells carry the indent and the expander
    internal string TreeColumnName => TreeColumn ?? ExpColName;

    private void CheckTreeColumn()
    {
        if (TreeColumn != null && Model.GetColumnIndex(TreeColumn) < 0)
            throw new InvalidOperationException($"TreeColumn names {TreeColumn}, which is no column of the model");
    }

    // The markup columns have registered by now; a model with no row draws no cell to check it
    /// <inheritdoc/>
    protected override void OnAfterRender(bool firstRender) => CheckTreeColumn();

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

    private Task DblClickAsync(DblClickEvent e) =>
        OnDblClick.InvokeAsync(new DblClickEvent(e.RowIdx, TreeColumn != null ? e.ColIdx : e.ColIdx > 0 ? e.ColIdx - 1 : null));   // the inner grid counts the expand column, if any

    /// <summary>
    /// Makes the row current and expands or collapses it, as a click on its expander does: for a label of a
    /// <see cref="TreeCellTemplate"/> that opens its node. Nothing happens while the current row is frozen, as during an edit.
    /// </summary>
    public async Task ToggleExpandAsync(int rowIdx)
    {
        if (Model.IsCurrentRowFrozen())
            return;
        Model.SetCurrentRow(rowIdx);
        await ToggleCurrentExpandAsync();
    }

    private async ValueTask ToggleCurrentExpandAsync()
    {
        ITreeNode? node = Model.GetCurrentTreeNode();
        if (node == null || !node.HasChildren())
            return;
        await Model.SetExpandedAsync(Model.GetCurrentRow(), !node.IsExpanded());
    }

    private string ZoomLabelOf(TGridItem item)
    {
        if (ZoomLabel != null)
            return ZoomLabel(item);
        if (TreeColumn == null && GetWrappedModel().GetValue(item, 0) is { } own)   // an expansion column given a value
            return own.ToString() ?? "";
        string? name = TreeColumn ?? _dataGrid?.GetColumnsToShow().FirstOrDefault(n => n != ExpColName);
        return name == null ? "" : Model.GetValue(item, Model.GetColumnIndex(name))?.ToString() ?? "";
    }

    private async ValueTask OnCurrentAsync(Func<int, ValueTask> action)
    {
        int row = Model.GetCurrentRow();
        if (row >= 0)   // -1 would mean the whole tree
            await action(row);
    }

    /// <inheritdoc cref="AGrid{TGridItem}.StabilizeItemAsync"/>
    public async Task StabilizeItemAsync() => await _dataGrid.RequireNotNull().StabilizeItemAsync();

    /// <inheritdoc cref="AGrid{TGridItem}.GetStableItem"/>
    public TGridItem? GetStableItem() => _dataGrid.RequireNotNull().GetStableItem();

    /// <summary>
    /// The grid's layout, filters and sort, as <see cref="Grigliaria{TGridItem}"/> saves them. Over an
    /// <see cref="ITreeStateGridModel{TGridItem}"/> it is a <see cref="TreeMemento"/>, with the nodes expanded and the one zoomed
    /// on too. When the model cannot save its tree state, as <see cref="ITreeStateGridModel{TGridItem}.CanSaveTreeState"/> says,
    /// it is the grid's memento alone.
    /// </summary>
    public virtual Memento CreateMemento()
    {
        Memento memento = _dataGrid.RequireNotNull().CreateMemento();
        if (!CanSaveTreeState(out ITreeStateGridModel<TGridItem>? stateful))
            return memento;
        return new TreeMemento(memento.Layout, memento.FiltersDefs, memento.SorterColumns, stateful.GetTreeState());
    }

    /// <summary>
    /// Puts back what <see cref="CreateMemento"/> saved; a <see cref="TreeMemento"/> over an
    /// <see cref="ITreeStateGridModel{TGridItem}"/> that can save its tree state puts back the nodes expanded and the zoom too. A
    /// plain memento, or null, leaves them as they are.
    /// </summary>
    public virtual void RestoreMemento(Memento? memento)
    {
        Grigliaria<TGridItem> inner = _dataGrid.RequireNotNull();
        if (Model.IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");   // before the tree state changes
        if (memento is TreeMemento { Tree: { } state } && CanSaveTreeState(out ITreeStateGridModel<TGridItem>? stateful))   // a store may lack the tree
            stateful.RestoreTreeState(state);   // first: the grid's restore renders
        inner.RestoreMemento(memento);
    }

    private bool CanSaveTreeState([NotNullWhen(true)] out ITreeStateGridModel<TGridItem>? stateful)
    {
        stateful = Model as ITreeStateGridModel<TGridItem>;
        return stateful != null && stateful.CanSaveTreeState();
    }

    /// <summary>
    /// The model the inner grid shows: the host's model with the expansion column in front, or with its tree column fixed.
    /// </summary>
    internal sealed class WrappedModel<MPT> : TreeGridModelWrapper<MPT>, IWrapsHostModel<MPT>
    {
        IGridModel<MPT> IWrapsHostModel<MPT>.HostModel => _gridModel;

        // column for the expand controls
        private ColumnInfo<MPT> _expandColumnInfo;

        private readonly Treegliaria<MPT> _grid;

        // the expansion column takes index 0 unless a column of the host's draws the tree
        private readonly int _offset;
        private (IColumn<MPT> Column, IColumn<MPT> Fixed)? _treeColumn;

        internal string? TreeColumn { get; }

        /// <summary>
        /// Column for the expand controls
        /// </summary>
        private static Column<MPT, bool> NewExpandColumn()
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

        internal WrappedModel(Treegliaria<MPT> grid, ITreeGridModel<MPT> wrapped, string? treeColumn) : base(wrapped)
        {
            ArgumentNullException.ThrowIfNull(grid);
            ArgumentNullException.ThrowIfNull(wrapped);
            _grid = grid;
            TreeColumn = treeColumn;
            _offset = treeColumn == null ? 1 : 0;
            _expandColumnInfo = new ColumnInfo<MPT>(ExpColName, NewExpandColumn(), Getter<MPT, object>.Default, null);
        }

        private void CheckExpandColumn(string columnName)
        {
            if (TreeColumn != null)
                throw new InvalidOperationException($"The column {TreeColumn} draws this tree, so it has no {columnName} column");
        }

        public override void EnrichColumn<T>(AGrid<MPT> grid, EnrichGridColumn<MPT, T> gridColumn)
        {
            if (!grid.CheckCollectingColumns(gridColumn.Name))
                return;
            if (gridColumn.Name == ExpColName)
            {
                CheckExpandColumn(gridColumn.Name);
                _expandColumnInfo = new ColumnInfo<MPT>(ExpColName, _expandColumnInfo.Column.Enrich(gridColumn), _expandColumnInfo.Property,
                    _expandColumnInfo.PropertyExpression);
                return;
            }
            _gridModel.EnrichColumn(grid, gridColumn);
        }

        public override void SetColumn<T>(AGrid<MPT> grid, GridColumn<MPT, T> gridColumn)
        {
            if (!grid.CheckCollectingColumns(gridColumn.Name))
                return;
            if (gridColumn.Name == ExpColName)
            {
                CheckExpandColumn(gridColumn.Name);
                _expandColumnInfo = ColumnInfo<MPT>.Of(ExpColName, 
                    new Column<MPT>(gridColumn.GetColumn())
                    {
                        Fixed = true,
                        Filterable = false,
                        Sortable = false,
                    }, 
                    gridColumn);
                return;
            }
            _gridModel.SetColumn(grid, gridColumn);
        }

        public override void UnsetColumn(AGrid<MPT> grid, string columnName)
        {
            if (columnName == ExpColName && TreeColumn == null)
                throw new InvalidOperationException($"The expand column {ExpColName} belongs to Treegliaria and cannot be removed");
            _gridModel.UnsetColumn(grid, columnName);
        }

        public override IColumn<MPT> GetColumn(int columnIndex)
        {
            if (columnIndex < _offset)
                return _expandColumnInfo.Column;
            IColumn<MPT> column = _gridModel.GetColumn(columnIndex - _offset);
            if (TreeColumn == null || _gridModel.GetColumnName(columnIndex - _offset) != TreeColumn)
                return column;
            if (_treeColumn?.Column != column)   // one wrapper per column the host registers
                _treeColumn = (column, new FixedColumn(column));
            return _treeColumn.Value.Fixed;
        }

        public override IColumnInfo<MPT> GetColumnInfo(int columnIndex)
        {
            return columnIndex < _offset ? _expandColumnInfo : _gridModel.GetColumnInfo(columnIndex - _offset);
        }

        public override int GetColumnCount()
        {
            return _gridModel.GetColumnCount() + _offset;
        }

        public override int GetColumnsVersion()
        {
            return _gridModel.GetColumnsVersion();
        }

        public override int GetColumnIndex(string columnName)
        {
            if (ExpColName == columnName)
                return _offset - 1;
            int idx = _gridModel.GetColumnIndex(columnName);
            return idx < 0 ? idx : idx + _offset;
        }

        public override string GetColumnName(int columnIndex)
        {
            return columnIndex < _offset ? ExpColName : _gridModel.GetColumnName(columnIndex - _offset);
        }

        public override object? GetValue(MPT item, int columnIndex)
        {
            if (columnIndex < _offset)
            {
                var prop = _expandColumnInfo.Property;
                return prop?.GetterFunc.Invoke(item);
            }
            return _gridModel.GetValue(item, columnIndex - _offset);
        }

        public override object? GetValueAt(int rowIndex, int columnIndex)
        {
            if (columnIndex < _offset)
            {
                return GetValue(GetItem(rowIndex), columnIndex);
            }
            return _gridModel.GetValueAt(rowIndex, columnIndex - _offset);
        }

        // The tree column stays first, and in the grid, whatever the layout
        private sealed class FixedColumn(IColumn<MPT> wrapped) : ColumnWrapper<MPT>(wrapped)
        {
            public override bool IsFixed() => true;

            public override bool? IsShownInRowDialog() => base.IsShownInRowDialog() ?? !string.IsNullOrEmpty(GetLabel());   // listed in the row dialog, though fixed

            protected override ColumnWrapper<MPT> NewWrapper(IColumn<MPT> wrapped) => new FixedColumn(wrapped);
        }
    }
}
