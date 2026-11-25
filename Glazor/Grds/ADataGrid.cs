using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glazor.Grds;

/// <summary>
/// The base of the grids over an <see cref="Mdls.IDataGridModel{TGridItem}"/>: what <see cref="AGrid{TGridItem}"/> has, plus the
/// cell cursor, filters and their dialog, the layout and its dialog, and the <see cref="Memento"/> that saves and restores them.
/// </summary>
public abstract class ADataGrid<TGridItem> : AGrid<TGridItem>, IDataGrid<TGridItem>, ILayoutable<TGridItem>, IFilterable<TGridItem>
{
    [Inject] private ILogger<ADataGrid<TGridItem>> Logger { get; init; } = null!;

    [Inject] private IConfiguration Config { get; init; } = null!;

    /// <summary>Tracks a current cell as well as a current row, which enables the left and right keys and the column index of <c>OnDblClick</c>.</summary>
    [Parameter]
    public bool CellCursorEnabled { get; set; } = true;

    /// <summary>
    /// Enables the row dialog, the current row as a list of its columns: a footer button opens it, disabled while every column
    /// it lists is in view, and so does <see cref="ShowRowDialogAsync"/>. A column stays out of it with <c>ShownInRowDialog</c>.
    /// </summary>
    [Parameter]
    public bool RowDialogEnabled { get; set; }

    /// <summary>
    /// Adds a footer button that reloads the data, keeping the filters, the sort and the current row, when the model is an
    /// <see cref="IReloadableGridModel{TGridItem}"/>; disabled while a pending edit or insert holds the current row.
    /// </summary>
    [Parameter]
    public bool ReloadEnabled { get; set; }

    /// <summary>
    /// With true, End goes to the last row already read; with false it reads on to the end of the data first.
    /// </summary>
    [Parameter]
    public bool GotoLastKnown { get; set; } = true;

    /// <summary>Two-finger touch scrolling over the grid body. Not set, it takes <c>Grigliaria:TouchScrollerEnabled</c> from the configuration, else false.</summary>
    [Parameter]
    public bool TouchScrollerEnabled { get; set; }

    /// <summary>Raised when the columns shown, their order or a width change. An <see cref="Action"/>: the handler calls <c>StateHasChanged</c> to re-render.</summary>
    [Parameter]
    public Action? OnLayoutChanged { get; set; }

    /// <summary>Computes how many display units fit across the grid. See <see cref="GridHorzResizers"/>.</summary>
    [Parameter]
    public IGridHorzResizer? HorzResizer { get; set; }

#pragma warning disable BL0007 // Component parameters should be auto properties
    /// <summary>Enables the layout dialog, to show, hide and reorder the columns, and the layout that <see cref="CreateMemento"/> saves.</summary>
    [Parameter]
    public bool Layoutable
    {
        get => _layout != null;
        set
        {
            if ((_layout != null) == value)
                return;
            _layout = value ? new GridLayout() : null;
        }
    }
#pragma warning restore BL0007 // Component parameters should be auto properties

    /// <inheritdoc/>
    public override IGridHorzResizer? GetHorzResizer() => HorzResizer;

    /// <summary>The filter dialog the grid renders.</summary>
    protected FilterDefDlg filterDefDlg = null!;

    /// <summary>The layout dialog the grid renders.</summary>
    protected LayoutDefDlg<TGridItem> layoutDefDlg = null!;

    /// <summary>The row dialog, rendered only with <see cref="RowDialogEnabled"/>.</summary>
    protected RowViewDlg<TGridItem>? rowViewDlg;   // only with RowDialogEnabled

    /// <summary>Chooses the filter editor for each column type in this grid; <see cref="Filters.FilterDefFactory.Default"/> without it.</summary>
    [Parameter]
    public FilterDefFactory? FilterDefFactory { get; set; }

    private GridLayout? _layout;

    private Memento? _orgMemento;
    private IDataGridModel<TGridItem>? _orgModel;   // unwrapped: the model whose filters and sort _orgMemento holds
    private Memento? _lastMemento;
    private bool _restoringLayout;
    private Dictionary<string, AFilterDef>? _pendingFilters;   // restored for columns not registered yet
    private SortedColumn? _pendingSort;
    private IDataGridModel<TGridItem>? _pendingModel;   // the model they wait on, unwrapped: the first one rendered after the restore
    private HashSet<string>? _knownColumns;   // the model's columns when the columns to show were last computed
    private IDataGridModel<TGridItem>? _knownColumnsModel;

    private bool _shouldRender = true;

    private IEnumerable<string> _columnsToShow = [];

    /// <inheritdoc/>
    protected override bool ShouldRender()
    {
        return _shouldRender && base.ShouldRender();
    }

    /// <inheritdoc/>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        // Check if the parent element explicitly provided this parameter
        if (!parameters.TryGetValue<bool>(nameof(TouchScrollerEnabled), out _))
        {
            if (!TouchScrollerDefault.Enabled.HasValue)
            {
                // If not provided, fetch the fallback default from appsettings.json
                TouchScrollerDefault.Enabled = Config.GetValue<bool>("Grigliaria:TouchScrollerEnabled", false);
                Logger.LogInformation("Using fallback value from appsettings.json \"Grigliaria:TouchScrollerEnabled\": {TouchScrollerEnabled}",
                    TouchScrollerDefault.Enabled.Value);
            }
            TouchScrollerEnabled = TouchScrollerDefault.Enabled.Value;
        }
        return base.SetParametersAsync(parameters);
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        // A new model brings the filters and sort a reset goes back to: taken here, before a subclass's restore at the swap
        IDataGridModel<TGridItem> model = UnwrappedModel();
        if (_orgMemento != null && !ReferenceEquals(_orgModel, model))
        {
            Memento start = CreateMemento();
            _orgMemento = _orgMemento with { FiltersDefs = start.FiltersDefs, SorterColumns = start.SorterColumns };
        }
        _orgModel = model;
    }

    // A new wrapper over the same model, as a tree grid builds when its TreeColumn changes, is the same model
    private IDataGridModel<TGridItem> UnwrappedModel()
    {
        IDataGridModel<TGridItem> model = GetModel();
        while (model is DataGridModelWrapper<TGridItem> wrapper)
            model = wrapper._dataGridModel;
        return model;
    }

    /// <inheritdoc/>
    protected override void SyncBeforeRendering()
    {
        ApplyPendingRestore();   // at every render: a column may have registered during an edit, which refused it
        base.SyncBeforeRendering();
        if (_orgMemento == null)
            _orgMemento = CreateMemento();
    }

    /// <summary>
    /// Moves the cell cursor to row <paramref name="rowIdx"/> and the model's column <paramref name="colIdx"/>.
    /// </summary>
    /// <returns>false when the current row is frozen by a pending edit, and nothing moved.</returns>
    /// <exception cref="InvalidOperationException">The cell cursor is not enabled.</exception>
    /// <exception cref="ArgumentException">The column is not one of <see cref="GetColumnsToShow"/>.</exception>
    public virtual bool SetCurrentCell(int rowIdx, int colIdx)
    {
        IGridModel<TGridItem> model = GetModel();
        if (model.IsCurrentRowFrozen())
            return false;
        // Checked before the row moves, with or without a HorzResizer
        string colName = model.GetColumnName(colIdx);
        // Anticipate checks done in SetCurrentColImpl
        if (!IsCellCursorEnabled())
            throw new InvalidOperationException("The cell cursor is not enabled");
        if (!GetColumnsToShow().Contains(colName))
            throw new ArgumentException($"Not a showable column: {colName}", nameof(colIdx));
        SetCurrentRow(rowIdx);
        SetCurrentColImpl(colName);
        StateHasChanged();
        return true;
    }

    /// <summary>
    /// Moves the cell cursor to the column named <paramref name="columnName"/>, on the current row.
    /// </summary>
    /// <returns>false when the current row is frozen by a pending edit, and nothing moved.</returns>
    /// <exception cref="InvalidOperationException">The cell cursor is not enabled.</exception>
    /// <exception cref="ArgumentException">The column is not one of <see cref="GetColumnsToShow"/>.</exception>
    public virtual bool SetCurrentCol(string columnName)
    {
        if (GetModel().IsCurrentRowFrozen())
            return false;
        if (!IsCellCursorEnabled())
            throw new InvalidOperationException("The cell cursor is not enabled");
        if (!GetColumnsToShow().Contains(columnName))
            throw new ArgumentException($"Not a showable column: {columnName}", nameof(columnName));
        SetCurrentColImpl(columnName);
        return true;
    }

    /// <summary>
    /// Makes <paramref name="rowIdx"/> the model's current row and renders the grid.
    /// </summary>
    /// <returns>false when the row was already current, or the current row is frozen by a pending edit.</returns>
    public virtual bool SetCurrentRow(int rowIdx)
    {
        IGridModel<TGridItem> model = GetModel();
        if (model.IsCurrentRowFrozen())
            return false;
        if (model.GetCurrentRow() == rowIdx)
            return false;
        model.SetCurrentRow(rowIdx);
        StateHasChanged();
        return true;
    }

    /// <inheritdoc/>
    public override bool IsCellCursorEnabled() => CellCursorEnabled;

    /// <summary>Makes the first row current; false as <see cref="SetCurrentRow"/> returns it.</summary>
    protected bool MoveToFirst()
    {
        IGridModel<TGridItem> model = GetModel();
        return SetCurrentRow(Math.Min(model.GetRowCount() - 1, 0));
    }

    /// <summary>Makes the last row current: the last row read with <paramref name="gotoLastKnown"/>, else the last row of all, reading up to it first.</summary>
    /// <returns>false when the current row is frozen, the read timed out and <see cref="AGrid{TGridItem}.OnTimeout"/> handled it, or as <see cref="SetCurrentRow"/> returns it.</returns>
    protected async ValueTask<bool> MoveToLastAsync(bool gotoLastKnown)
    {
        IGridModel<TGridItem> model = GetModel();
        if (gotoLastKnown)
        {
            return SetCurrentRow(model.GetCurrentRowCount() - 1);
        }
        else
        {
            if (model.IsCurrentRowFrozen())
                return false;
            if (model.GetCurrentRowCount() < model.GetRowCount())
            {
                if (model.RequiresFetch())
                {
                    try
                    {
                        _ = _spinnerHelper.Monitor<int>(Spinner.RequireNotNull(), (ct) => Task.Run<int>(async () =>
                            {
                                return await model.FetchRowsAsync(model.GetRowCount() - 1, 1, ct);
                            }),
                            _cancellationTokenSource.Token
                        );
                    }
                    catch (GridTimeoutException e)
                    {
                        if (!Timeout(e))
                            throw;
                        return false;   // the end was not reached: a row there would be fetched again by the render
                    }
                }
                else
                    await model.FetchRowsAsync(model.GetRowCount() - 1, 1);
            }
            return SetCurrentRow(model.GetRowCount() - 1);
        }
    }

    /// <summary>Moves the current row by <paramref name="delta"/>, within the rows; false as <see cref="SetCurrentRow"/> returns it.</summary>
    protected bool MoveRow(int delta)
    {
        IGridModel<TGridItem> model = GetModel();
        return SetCurrentRow(Math.Min(model.GetRowCount() - 1, Math.Max(0, model.GetCurrentRow() + delta)));
    }

    /// <summary>Moves the cell cursor by <paramref name="delta"/> columns shown; false without a cell cursor, with the current row frozen or with no column shown.</summary>
    protected bool MoveCol(int delta)
    {
        if (!IsCellCursorEnabled())
            return false;
        IGridModel<TGridItem> model = GetModel();
        if (model.IsCurrentRowFrozen())
            return false;
        var cols = new List<string>(GetColumnsToShow());
        if (cols.Count == 0)
            return false;
        string? col = GetCurrentCol();
        if (col == null)
        {
            col = cols[0];
        }
        else
        {
            int idx = cols.IndexOf(col);
            if (idx < 0)
                col = cols[0];
            else if (delta < 0)
                col = cols[Math.Max(0, idx + delta)];
            else
                col = cols[Math.Min(cols.Count - 1, idx + delta)];
        }
        SetCurrentColImpl(col);
        StateHasChanged();
        return true;
    }

    /// <summary>
    /// Replaces the layout, a change <see cref="OnLayoutChanged"/> reports. <c>null</c> puts back the default one: the fixed
    /// columns, then those shown by default, each at its own preferred size. On a grid that is not layoutable, <c>null</c> does
    /// nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">A layout is given to a grid that is not layoutable.</exception>
    /// <exception cref="ArgumentException">The layout names a column the model does not have.</exception>
    public void SetLayout(GridLayout? layout)
    {
        if (_layout == layout)
            return;
        if (_layout == null)
            throw new InvalidOperationException("The grid is not layoutable: set Layoutable to give it a layout");
        if (layout != null)
            CheckColumnsToShow(layout.columnsToShow);   // before anything changes: a direct setter treats an unknown name as an error
        _layout = new GridLayout();
        if (layout != null)
        {
            _layout!.CopyFrom(layout);
            SetColumnsToShow(_layout.columnsToShow);
        }
        else
        {   // null is the default layout: the columns ShownByDefault gives, no sizes
            SetColumnsToShowImpl([], checkNames: false);
            ColumnsToShowChanged();
        }
    }
    /// <returns>a copy of the current layout, it can be saved and restored later.<br/>null if none.</returns>
    public GridLayout? GetLayout()
    {
        if (_layout == null)
            return null;
        _layout.columnsToShow = GetColumnsToShow();
        return _layout.Clone();
    }

    /// <summary>
    /// Shows these columns in this order, after the fixed ones, which are always shown. An empty list shows the columns shown
    /// by default. A layout change, as <see cref="SetLayout"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A name the model does not have.</exception>
    public void SetColumnsToShow(params IEnumerable<string> columnsToShow)
    {
        SetColumnsToShowImpl(columnsToShow, checkNames: true);
        ColumnsToShowChanged();
    }

    private void ColumnsToShowChanged()
    {
        KeepRestoredLayoutCurrent();
        LayoutChanged();
        if (!_restoringLayout)   // a restore runs inside a render, or in RestoreMemento, which renders when it ends
            StateHasChanged();
    }

    /// <inheritdoc/>
    protected override void ComputeColumnsToShow(bool afterCollectingColumns)
    {
        var model = GetModel();
        List<string> arranged = GetColumnsToShow().ToList();
        SetColumnsToShowImpl([], checkNames: false);   // the columns shown by default
        if (afterCollectingColumns)
        {
            // The original memento takes the defaults for the new column set, and keeps the sizes the grid started with
            if (_orgMemento != null)
            {
                GridLayout? orgLayout = _layout == null ? null : _orgMemento.Layout?.Clone() ?? new();
                if (orgLayout != null)
                    orgLayout.columnsToShow = GetColumnsToShow();
                _orgMemento = _orgMemento with { Layout = orgLayout };
            }
            // A change to the same model's columns keeps what is shown: the removed columns go, and the added ones the
            // defaults show are appended
            List<string> added = new();
            bool known = _knownColumns != null;
            bool newModel = known && !ReferenceEquals(_knownColumnsModel, UnwrappedModel());
            if (_knownColumns != null && !newModel)
            {
                added = GetColumnsToShow().Where(c => !_knownColumns.Contains(c)).ToList();
                SetColumnsToShowImpl(arranged.Where(c => model.GetColumnIndex(c) >= 0).Concat(added), checkNames: true);
            }
            _knownColumns = Enumerable.Range(0, model.GetColumnCount()).Select(model.GetColumnName).ToHashSet();
            _knownColumnsModel = UnwrappedModel();
            if (_lastMemento != null)
            {
                // Added columns take their place in the restored layout, or after it; the filters and the sort stay the user's
                GridLayout? layout = _lastMemento.Layout?.Clone();
                if (layout != null)
                    layout.columnsToShow = layout.columnsToShow.Concat(added.Where(c => !layout.columnsToShow.Contains(c))).ToList();
                RestoreLayout(layout);
                if (added.Count > 0)
                    KeepRestoredLayoutCurrent();
            }
            else if (known && !arranged.SequenceEqual(GetColumnsToShow()))
                LayoutChanged();   // a column change or a new model's defaults changed what is shown; a restore raises it through SetLayout
        }
    }

    /// <summary>
    /// Only <see cref="RestoreMemento"/> tolerates a stale column name, and it prunes before calling: everywhere else an unknown
    /// name is an error, otherwise it is thrown from inside the render tree or, with a HorzResizer, dropped silently.
    /// </summary>
    private void CheckColumnsToShow(IEnumerable<string> columnsToShow)
    {
        var model = GetModel();
        foreach (string cname in columnsToShow)
        {
            if (model.GetColumnIndex(cname) < 0)
                throw new ArgumentException($"Unknown column: {cname}", nameof(columnsToShow));
        }
    }

    private void SetColumnsToShowImpl(IEnumerable<string> columnsToShow, bool checkNames)
    {
        var model = GetModel();
        if (checkNames)
            CheckColumnsToShow(columnsToShow);
        OrderedDictionary<string, NoValue> cols = new(model.GetColumnCount());
        {
            for (int i = 0; i < model.GetColumnCount(); i++)
            {
                IColumn<TGridItem> col = model.GetColumn(i);
                if (col.IsFixed())
                    cols.TryAdd(model.GetColumnName(i), NoValue.Value);
            }
        }
        if (!columnsToShow.Any())
        {
            for (int i = 0; i < model.GetColumnCount(); i++)
            {
                IColumn<TGridItem> col = model.GetColumn(i);
                if (col.GetPreferredSize() != 0 && col.IsShownByDefault())
                    cols.TryAdd(model.GetColumnName(i), NoValue.Value);
            }
        }
        else
        {
            foreach (string cname in columnsToShow)
            {
                cols.TryAdd(cname, NoValue.Value);
            }
        }
        _columnsToShow = cols.Keys;
    }

    /// <summary>Swaps the column shown at <paramref name="toShowIdx"/> with the one <paramref name="delta"/> places away, and keeps it in view.</summary>
    protected internal Task ShiftColumnToShowAsync(int toShowIdx, int delta)
    {
        var colsToShow = GetColumnsToShow().ToList();
        if (toShowIdx < 0 || toShowIdx >= colsToShow.Count)
            throw new ArgumentOutOfRangeException(nameof(toShowIdx));
        if (delta == 0)
            return Task.CompletedTask;
        int newIdx = toShowIdx + delta;
        if (newIdx < 0 || newIdx >= colsToShow.Count)
            throw new ArgumentOutOfRangeException(nameof(delta));
        // swap
        (colsToShow[toShowIdx], colsToShow[newIdx]) = (colsToShow[newIdx], colsToShow[toShowIdx]);
        SetColumnsToShow(colsToShow);
        EnsureColumnIsShown(colsToShow[newIdx]);
        return Task.CompletedTask;
    }

    IEnumerable<string> ILayoutable<TGridItem>.GetColumnsToShow() => GetColumnsToShow();
    /// <inheritdoc/>
    public override IEnumerable<string> GetColumnsToShow() => _columnsToShow;

    /// <inheritdoc/>
    public override abstract IDataGridModel<TGridItem> GetModel();

    /// <summary>
    /// Overrides the preferred size specified for the column
    /// </summary>
    public void SetPreferredSize(string columnName, int? size)
    {
        if (_layout == null)
            throw new InvalidOperationException("The grid is not layoutable: set Layoutable to size its columns");
        if (GetModel().GetColumnIndex(columnName) < 0)
            throw new ArgumentException($"Unknown column: {columnName}", nameof(columnName));
        if (size == null)
            _layout.attributes.Remove(columnName);
        else
            _layout.attributes[columnName] = new((int)size);
        KeepRestoredLayoutCurrent();
        LayoutChanged();
        StateHasChanged();
    }

    // What the user arranges after a restore is what a later change of the columns applies again
    private void KeepRestoredLayoutCurrent()
    {
        if (_lastMemento != null && !_restoringLayout)
            _lastMemento = UpdateMementoLayout(_lastMemento);
    }

    private void RestoreLayout(GridLayout? layout)
    {
        if (_layout == null)
            return;   // a grid that is not layoutable ignores a memento's layout, and still restores its filters and sort
        var model = GetModel();
        _restoringLayout = true;
        try
        {
            GridLayout? pruned = layout?.Clone();   // the names shown are pruned, the sizes kept, as SetLayout keeps them
            if (pruned != null)
                pruned.columnsToShow = pruned.columnsToShow.Where(cn => model.GetColumnIndex(cn) >= 0).ToList();
            SetLayout(pruned);
        }
        finally
        {
            _restoringLayout = false;
        }
    }

    /// <summary>
    /// Called when the columns shown, their order or a width change, and raises <see cref="OnLayoutChanged"/>: a subclass
    /// overrides it to act on every layout change, as a memento saved, and calls the base.
    /// </summary>
    protected virtual void LayoutChanged()
    {
        OnLayoutChanged?.Invoke();
    }

    private class XColumnWrapper(IColumn<TGridItem> wrapped, int? psize) : ColumnWrapper<TGridItem>(wrapped)
    {
        public override int GetPreferredSize()
        {
            return psize != null ? (int)psize : base.GetPreferredSize();
        }
        protected override XColumnWrapper NewWrapper(IColumn<TGridItem> wrapped)
        {
            return new XColumnWrapper(wrapped, psize);
        }
    }

    /// <returns>The model's column, in a <see cref="ColumnWrapper{TGridItem}"/> when the layout sets its size.</returns>
    public override IColumn<TGridItem> GetColumn(int columnIndex)
    {
        var model = GetModel();
        IColumn<TGridItem> fmd = model.GetColumn(columnIndex);
        if (_layout == null)
            return fmd;
        string columnName = model.GetColumnName(columnIndex);
        if (columnName == null)
            throw new ArgumentException($"No name for column: {columnIndex}");
        if (!_layout.attributes.TryGetValue(columnName, out var attr))
            return fmd;
        IColumn<TGridItem> fake = new XColumnWrapper(fmd, attr.PreferredSize);
        return fake;
    }

    /// <summary>
    /// Scrolls a grid with a <see cref="HorzResizer"/> so that the column is in its window.
    /// </summary>
    /// <returns>true when the column is already in view; false when the grid scrolls to it at the next render.</returns>
    /// <exception cref="ArgumentException">The model has no such column, or it is not one of <see cref="GetColumnsToShow"/>.</exception>
    public bool EnsureColumnIsShown(string columnName)
    {
        var model = GetModel();
        int idx = model.GetColumnIndex(columnName);
        if (idx < 0)
            throw new ArgumentException("Column not found: " + columnName);
        return EnsureColumnIsShown(idx);
    }

    /// <summary>
    /// Whether the definition can filter the column: a DefaultDef always can (it is untyped), any other one by its value type.
    /// </summary>
    private static bool FilterMatchesColumn(IColumn<TGridItem> col, AFilterDef filterDef)
    {
        if (filterDef is DefaultDef)
            return true;
        Type clazz0 = col.GetValueType();
        Type iclazz0 = Nullable.GetUnderlyingType(clazz0) ?? clazz0;
        Type clazz1 = filterDef.GetValueType();
        Type iclazz1 = Nullable.GetUnderlyingType(clazz1) ?? clazz1;
        if ((iclazz0.IsEnum && iclazz1 == typeof(Enum)) || (iclazz0 == typeof(Enum) && iclazz1.IsEnum))
            return true;
        return iclazz1.IsAssignableFrom(iclazz0);   // the definition takes every value the column holds
    }

    private void CheckFilter(IColumn<TGridItem> col, AFilterDef filterDef)
    {
        if (!FilterMatchesColumn(col, filterDef))
            throw new ArgumentException("Invalid filter type, expected: " +
                col.GetValueType() + " found: " + filterDef.GetValueType());
    }

    /// <summary>
    /// Filters the column with <paramref name="filterDef"/>; <c>null</c> or a void definition removes its filter. The other
    /// columns' filters stay. A <see cref="DefaultDef"/> fits any column, any other definition must take the column's values.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model is not filterable.</exception>
    /// <exception cref="ArgumentException">The model has no such column, the column is not filterable, or the definition does
    /// not take its value type.</exception>
    public void SetFilter(string columnName, AFilterDef? filterDef)
    {
        var model = GetModel();
        if (!model.IsFilterable())
            throw new InvalidOperationException("The model is not filterable");
        int colIdx = model.GetColumnIndex(columnName);
        if (colIdx < 0)
            throw new ArgumentException("Column not found: " + columnName, nameof(columnName));
        IColumn<TGridItem> col = model.GetColumn(colIdx);
        if (filterDef != null && !col.IsFilterable())
            throw new ArgumentException("Not filterable: " + columnName, nameof(columnName));
        var definedFilters =
            new Dictionary<string, AFilterDef>(GetFilters());
        if (filterDef == null || filterDef.IsVoid())
        {
            definedFilters.Remove(columnName);
        }
        else if (filterDef is DefaultDef)
        {
            // go on, this is an "untyped" filter
            definedFilters[columnName] = filterDef;
        }
        else
        {
            CheckFilter(col, filterDef);
            definedFilters[columnName] = filterDef;
        }
        model.ApplyFilters(definedFilters);
        StateHasChanged();
    }

    /// <returns>the filter applied to the column, or null.</returns>
    public AFilterDef? GetFilter(string columnName)
    {
        IReadOnlyDictionary<string, AFilterDef> res = GetModel().GetFilters();
        return res == null ? null : (res.TryGetValue(columnName, out var fdef) ? (AFilterDef)fdef : null);
    }

    /// <returns>whether a filter is applied to the column.</returns>
    public bool HasFilter(string columnName)
    {
        IReadOnlyDictionary<string, AFilterDef> res = GetModel().GetFilters();
        return res != null && res.ContainsKey(columnName);
    }

    /// <returns>a copy of the filters applied, by column name, that later changes leave as it is; empty when there are none.</returns>
    public IReadOnlyDictionary<string, AFilterDef> GetFilters()
    {
        IReadOnlyDictionary<string, AFilterDef>? res = GetModel().GetFilters();
        return res == null ? ImmutableDictionary<string, AFilterDef>.Empty : res.ToImmutableDictionary();
    }

    /// <summary>
    /// Replaces every filter. Programmer input, as <see cref="SetFilter"/>: an unknown column throws, and so does a definition
    /// for a column that is not filterable; a null or void definition is left out. A persisted set that may name columns no
    /// longer there goes through <see cref="RestoreMemento"/>.
    /// </summary>
    public void SetFilters(IReadOnlyDictionary<string, AFilterDef> filters)
    {
        var model = GetModel();
        if (!model.IsFilterable())
            throw new InvalidOperationException("The model is not filterable");
        var definedFilters =
             new Dictionary<String, AFilterDef>();
        if (filters != null)
        {
            foreach (var entry in filters)
            {
                string columnName = entry.Key;
                int colIdx = model.GetColumnIndex(columnName);
                if (colIdx < 0)
                    throw new ArgumentException("Column not found: " + columnName, nameof(filters));
                IColumn<TGridItem> col = model.GetColumn(colIdx);
                AFilterDef? filterDef = entry.Value;
                if (filterDef != null && !col.IsFilterable())
                    throw new ArgumentException("Not filterable: " + columnName, nameof(filters));
                if (filterDef == null || filterDef.IsVoid())
                    continue;
                if (filterDef is DefaultDef)
                {
                    // go on, this is an "untyped" filter
                }
                else
                {
                    CheckFilter(col, filterDef);
                }
                definedFilters[columnName] = filterDef.Clone();
            }
        }
        model.ApplyFilters(definedFilters);
        StateHasChanged();
    }

    /// <summary>Whether a filter that is not void is applied to any column.</summary>
    public bool IsFiltered()
    {
        var model = GetModel();
        return model.IsFilterable() && GetFilters().Count > 0 && model.IsFiltered();
    }

    /// <summary>A column and the filter definition it starts from.</summary>
    /// <param name="Column">The column, from the model.</param>
    /// <param name="FilterDef">The column's own <c>FilterDef</c>, or else the one the grid's filter factory creates for it.</param>
    public record PredefinedFilter(IColumn<TGridItem> Column, AFilterDef FilterDef);

    /// <summary>
    /// Retrieves the column and its associated predefined filter definition for the specified column name.
    /// </summary>
    /// <param name="columnName">The name of the column for which to obtain the filter definition.</param>
    /// <returns>The column and its filter definition.</returns>
    /// <exception cref="ArgumentException">Thrown when the specified column name does not exist.</exception>
    public PredefinedFilter GetPredefinedFilterDef(string columnName)
    {
        var model = GetModel();
        int colIdx = model.GetColumnIndex(columnName);
        IColumn<TGridItem> col = ((colIdx >= 0) ? model.GetColumn(colIdx) : null)
            ?? throw new ArgumentException("Not found: " + columnName);
        AFilterDef? def = col.GetFilterDef();
        if (def == null)
        {
            FilterDefFactory factory = GetFilterDefFactory();
            def = factory.CreateDefinition<TGridItem>(model, columnName, col);
        }
        return new(col, def);
    }

    /// <summary>Opens the filter dialog of a column, as the header's filter icon does.</summary>
    /// <exception cref="InvalidOperationException">The model is not filterable, or a pending edit or insert holds the current row.</exception>
    /// <exception cref="ArgumentException">The model has no such column, or the column is not filterable.</exception>
    public async Task ShowFilterDialogAsync(string columnName, Action<FilterDefDlg.SubmitEvent>? onFormSubmit)
    {
        if (GetModel().IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");   // the dialog's OK could not apply the filter
        if (!GetModel().IsFilterable())
            throw new InvalidOperationException("The model is not filterable");
        int colIdx = GetModel().GetColumnIndex(columnName);
        if (colIdx >= 0 && !GetModel().GetColumn(colIdx).IsFilterable())   // before the factory, which throws for a type it cannot filter
            throw new ArgumentException("Not filterable: " + columnName, nameof(columnName));
        (var col, var preDef) = GetPredefinedFilterDef(columnName);
        AFilterDef? def = GetFilter(columnName);
        if (def == null)
        {
            def = preDef.Clone();
        }
        else
        {
            def = def.Clone();
        }
        var model = GetModel();
        bool nullableValue = model.GetColumnInfo(model.GetColumnIndex(columnName)).ValueCanBeNull();
        await filterDefDlg.ShowModalAsync(columnName, def, col.GetLabel(), onFormSubmit, nullableValue);
    }

    /// <returns>The <c>FilterDefFactory</c> parameter, or <see cref="Filters.FilterDefFactory.Default"/> without one.</returns>
    public FilterDefFactory GetFilterDefFactory()
    {
        return FilterDefFactory ?? Filters.FilterDefFactory.Default;
    }

    /// <inheritdoc/>
    protected internal override void ValidateColumn(string columnName, IColumn<TGridItem> column)
    {
        if (column.IsFilterable() && column.GetFilterDef() == null)   // as the filter dialog: the column's own definition comes first
        {
            FilterDefFactory deff = GetFilterDefFactory();
            // Just to verify ...
            deff.CreateDefinition(GetModel(), columnName, column);
        }
    }

    /// <summary>Opens the layout dialog, as the header's layout button does.</summary>
    /// <exception cref="InvalidOperationException">The grid is not layoutable, or a pending edit or insert holds the current row.</exception>
    public Task ShowLayoutDialogAsync()
    {
        if (GetLayout() == null)
            throw new InvalidOperationException("The grid is not layoutable");
        if (GetModel().IsCurrentRowFrozen())
            // as the header's button: the dialog's changes and filters are refused during an edit
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        return OnLayoutAsync();
    }

    /// <summary>
    /// Opens the row dialog, as the footer's button does: the current row as a list of its columns, label and value, those
    /// of the layout first and then the hidden ones.
    /// </summary>
    /// <exception cref="InvalidOperationException">The row dialog is not enabled, there is no current row, or a pending edit
    /// or insert holds it.</exception>
    public Task ShowRowDialogAsync()
    {
        if (!RowDialogEnabled)
            throw new InvalidOperationException("The row dialog is not enabled");
        IGridModel<TGridItem> model = GetModel();
        if (model.GetCurrentRow() < 0)
            throw new InvalidOperationException("No current row");
        if (model.IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");   // the dialog's Previous and Next move the current row
        return rowViewDlg.RequireNotNull().ShowModalAsync(this);
    }

    /// <summary>Whether a column the row dialog lists is not on screen: out of the layout, or scrolled out of the grid's window.</summary>
    protected bool HasColumnsOutOfView()
    {
        IGridModel<TGridItem> model = GetModel();
        HashSet<string> inView = [.. GetShownColumns()];
        for (int i = 0; i < model.GetColumnCount(); i++)
        {
            IColumn<TGridItem> col = GetColumn(i);
            if (col.GetPreferredSize() != 0 && col.IsListedInRowDialog() && !inView.Contains(model.GetColumnName(i)))
                return true;
        }
        return false;
    }

    /// <summary>The model the reload button reloads: the grid's own, or the host's a wrapper grid hands on; null when it cannot reload.</summary>
    protected IReloadableGridModel<TGridItem>? GetReloadableModel()
    {
        IGridModel<TGridItem> model = GetModel();
        return (model is IWrapsHostModel<TGridItem> wrapper ? wrapper.HostModel : model) as IReloadableGridModel<TGridItem>;
    }

    /// <summary>Opens the layout dialog, for the header's layout button and <see cref="ShowLayoutDialogAsync"/>; its Cancel puts back the layout the grid had.</summary>
    protected virtual async Task OnLayoutAsync()
    {
        // not null because layoutable
        GridLayout old = GetLayout().RequireNotNull();
        Memento? restored = _lastMemento;   // the dialog's changes update it as the user makes them
        await layoutDefDlg.ShowModalAsync(this, Texts.Get(TextKeys.Layout),
            async () =>
            {
                SetLayout(old);
                _lastMemento = restored;
                StateHasChanged();
            });
    }

    /// <summary>
    /// The restored memento with the grid's current layout and its own filters and sort, taken on every layout change after a
    /// restore, so that a later change of the columns applies the layout as the user has arranged it since.
    /// </summary>
    protected virtual Memento UpdateMementoLayout(Memento memento)
    {
        var newMemento = new Memento(GetLayout(),
            memento.FiltersDefs,
            memento.SorterColumns);
        return newMemento;
    }

    /// <summary>
    /// A Memento can be used to store in a blazor circuit how the grid is configured at the moment to be restored later.<br/>
    /// You can also store the Memento in a db to preserve how a user has configured its grid.
    /// </summary>
    public virtual Memento CreateMemento()
    {
        var model = GetModel();
        return new Memento(GetLayout()?.Clone(cn => model.GetColumnIndex(cn) >= 0),
            GetFilters().ToImmutableDictionary(),
            model.GetSortedColumn() == null ? [] : [new(model.GetSortedColumn()!, model.GetSortType() == SortType.Asc)]);
    }

    /// <summary>
    /// Puts back the layout, filters and sort <see cref="CreateMemento"/> saved; <c>null</c> puts back those the grid started
    /// with, the filters and sort being those the model it has now came with. A memento may outlive the code that wrote it:
    /// the names of columns the model no longer has are dropped from the layout, and their filters and sort are set aside,
    /// applied if such a column registers later on the same model. A filter on a column no longer filterable, or that the
    /// column can no longer take, is dropped. A grid that is not layoutable ignores the layout.
    /// </summary>
    /// <exception cref="InvalidOperationException">A pending edit or insert holds the current row.</exception>
    public virtual void RestoreMemento(Memento? memento)
    {
        if (GetModel().IsCurrentRowFrozen())
            // before the layout changes: the filters would refuse it after
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        _orgMemento ??= CreateMemento();   // an early restore must not become what a reset goes back to
        _shouldRender = false;
        try
        {
            // Kept without the layout it ignores, or a later column change applies it once the grid is layoutable
            _lastMemento = _layout == null && memento?.Layout != null ? memento with { Layout = null } : memento;
            _pendingFilters = null;
            _pendingSort = null;
            _pendingModel = null;
            var model = GetModel();
            memento ??= _orgMemento.RequireNotNull();   // the starting filters too can name a column that has left since
            var persistedFilters = memento.FiltersDefs ?? ImmutableDictionary<string, AFilterDef>.Empty;   // a store written by hand may leave them out
            OrderedDictionary<string, AFilterDef> filtersDefs = new(persistedFilters.Count);
            foreach (var en in persistedFilters)
            {
                if (en.Value == null || en.Value.IsVoid())
                    continue;   // a store written by hand may hold a column with no filter, or one that filters nothing
                int colIdx = model.GetColumnIndex(en.Key);
                if (colIdx < 0)
                {
                    Logger.LogWarning("Persisted filter for unknown column: {Column}, applied if it is registered later", en.Key);
                    (_pendingFilters ??= new())[en.Key] = en.Value;
                    continue;
                }
                if (PrepareRestoredFilter(model, colIdx, en.Key, en.Value) is { } filterDef)
                    filtersDefs[en.Key] = filterDef;
            }
            RestoreLayout(memento.Layout);
            model.ApplyFilters(filtersDefs);
            var sorted = memento.SorterColumns is { Count: > 0 } sorter ? sorter[0] : null;
            int sortedIdx = sorted == null ? -1 : model.GetColumnIndex(sorted.Name);
            if (sortedIdx >= 0 && RestoredSortApplies(model, sortedIdx, sorted!.Name))
            {
                model.SetSortedColumn(sorted.Name, sorted.Asc);
            }
            else
            {
                if (sorted != null && sortedIdx < 0)
                {
                    Logger.LogWarning("Persisted sort for unknown column: {Column}, applied if it is registered later", sorted.Name);
                    _pendingSort = sorted;
                }
                model.SetSortedColumn(null, true);
            }
        }
        finally
        {
            _shouldRender = true;
        }
        StateHasChanged();
    }

    // The same tests SetFilters runs: since the memento was saved the column may have become unfilterable, or its value type
    // may have changed, and a definition applied blind hides rows with no icon to clear or throws in the render
    private AFilterDef? PrepareRestoredFilter(IDataGridModel<TGridItem> model, int colIdx, string columnName, AFilterDef filterDef)
    {
        IColumn<TGridItem> col = model.GetColumn(colIdx);
        if (!model.IsFilterable() || !col.IsFilterable())
        {
            Logger.LogWarning("Ignoring persisted filter for column no longer filterable: {Column}", columnName);
            return null;
        }
        if (!FilterMatchesColumn(col, filterDef)
            || (filterDef is DefaultDef && GetPredefinedFilterDef(columnName).FilterDef is not DefaultDef))   // a text filter, stale once the column has a definition of its own
        {
            Logger.LogWarning("Ignoring persisted filter of type {Filter} for column {Column} of type {Type}", filterDef.GetValueType(), columnName, col.GetValueType());
            return null;
        }
        if (filterDef is IWithInjectedParams)
        {
            (_, var pdef) = GetPredefinedFilterDef(columnName);
            if ((pdef as IWithInjectedParams)?.InjectParamsInto(filterDef) is { } injected)
                return injected;
            Logger.LogWarning("Ignoring persisted filter of type {Filter} for column {Column}, whose filter is now a {Current}", filterDef.GetType(), columnName, pdef.GetType());
            return null;
        }
        return filterDef;
    }

    // The column's values may have become ones that cannot be compared; a sort set in code on a column the user cannot sort applies
    private bool RestoredSortApplies(IDataGridModel<TGridItem> model, int colIdx, string columnName)
    {
        IColumn<TGridItem> col = model.GetColumn(colIdx);
        if (!model.IsSortable())
        {
            Logger.LogWarning("Ignoring persisted sort for a model that cannot sort: {Column}", columnName);
            return false;
        }
        if (col.IsSortable() || PocoColumnComparer.CanCompare(col.GetValueType()))
            return true;
        Logger.LogWarning("Ignoring persisted sort for column whose values cannot be compared: {Column}", columnName);
        return false;
    }

    // Once a restored filter's or sort's column registers: added only where the user has set none since, and not while an
    // edit or insert holds the current row
    private void ApplyPendingRestore()
    {
        var model = GetModel();
        if (_pendingFilters == null && _pendingSort == null)
            return;
        // Bound at a render, not at the restore: one made at a swap, before the grid gets the new model, waits on the new one
        IDataGridModel<TGridItem> unwrapped = UnwrappedModel();
        _pendingModel ??= unwrapped;
        if (!ReferenceEquals(_pendingModel, unwrapped))
        {
            // A new model brings its own filters and sort
            _pendingFilters = null;
            _pendingSort = null;
            return;
        }
        if (model.IsCurrentRowFrozen())
            return;
        if (_pendingFilters != null)
        {
            OrderedDictionary<string, AFilterDef> filters = new(GetFilters());
            bool added = false;
            foreach (var (columnName, filterDef) in _pendingFilters.ToArray())
            {
                int colIdx = model.GetColumnIndex(columnName);
                if (colIdx < 0)
                    continue;
                _pendingFilters.Remove(columnName);
                if (!filters.ContainsKey(columnName) && PrepareRestoredFilter(model, colIdx, columnName, filterDef) is { } prepared)
                {
                    filters[columnName] = prepared;
                    added = true;
                }
            }
            if (added)
                model.ApplyFilters(filters);
            if (_pendingFilters.Count == 0)
                _pendingFilters = null;
        }
        if (_pendingSort != null && model.GetColumnIndex(_pendingSort.Name) is int sortIdx && sortIdx >= 0)
        {
            if (model.GetSortedColumn() == null && RestoredSortApplies(model, sortIdx, _pendingSort.Name))
                model.SetSortedColumn(_pendingSort.Name, _pendingSort.Asc);
            _pendingSort = null;
        }
    }
}

// Outside ADataGrid<TGridItem>: a static field there exists once per row type, each reading and logging the default again.
internal static class TouchScrollerDefault
{
    internal static bool? Enabled;
}

// PreferredSize defaults to the type's own, the value options that skip defaults leave out of the JSON
/// <summary>
/// What a layout keeps for one column: the size the user gave it, in display units.
/// </summary>
/// <param name="PreferredSize">The size the user gave the column, in display units.</param>
public record LayoutAttr(int PreferredSize = 0);

/// <summary>
/// The columns shown, in order, and the sizes set by the user; the part of a <see cref="Memento"/> a grid that is not
/// layoutable ignores. Serializable as JSON.
/// </summary>
public class GridLayout : IEquatable<GridLayout>
{
    /// <summary>The equality <see cref="Equals(GridLayout?)"/> uses: the columns shown in order, and their attributes.</summary>
    protected static readonly IEqualityComparer<GridLayout> comparer = EqComparers.Combo<GridLayout>(l => l
         .Add(l => l?.columnsToShow, EqComparers.ForEnumerable<string>())
         .Add(l => l?.attributes, EqComparers.ForDictionary<string, LayoutAttr>()));


    [JsonInclude]
    internal IEnumerable<string> columnsToShow = [];
    [JsonInclude]
    internal readonly Dictionary<string, LayoutAttr> attributes = new();

    /// <summary>
    /// The names of the columns shown, in order.
    /// </summary>
    [JsonIgnore]   // persisted through the field, under its own name
    public IReadOnlyList<string> ColumnsToShow => columnsToShow as IReadOnlyList<string> ?? [.. columnsToShow];

    /// <summary>
    /// The sizes the user set, by column name; a column without one takes its own <c>PreferredSize</c>.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, LayoutAttr> Attributes => attributes.AsReadOnly();

    /// <summary>An empty layout: no column shown and no size set.</summary>
    public GridLayout()
    {
    }

    [JsonConstructor]
    internal GridLayout(IEnumerable<string> columnsToShow, Dictionary<string, LayoutAttr> attributes)
    {
        // Only the deserializer calls it: a null here is a malformed store
        if (columnsToShow == null || attributes == null)
            throw new JsonException("A layout needs both columnsToShow and attributes");
        this.columnsToShow = columnsToShow;
        this.attributes = attributes;
        foreach (string column in attributes.Where(en => en.Value == null).Select(en => en.Key).ToList())
            attributes.Remove(column);   // a column with no size keeps its own
    }

    internal void CopyFrom(GridLayout other, Func<string, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(other, nameof(other));
        columnsToShow = predicate != null ? other.columnsToShow.Where(predicate).ToList().AsReadOnly() 
            : other.columnsToShow;  // no need to clone, it is a read-only collection
        attributes.Clear();
        foreach (var entry in (predicate != null ? other.attributes.Where(en => predicate(en.Key)) : other.attributes))
            attributes.Add(entry.Key, entry.Value);
    }

    /// <summary>A copy of the layout, keeping only the columns <paramref name="predicate"/> admits, all of them when it is null.</summary>
    public GridLayout Clone(Func<string, bool>? predicate = null)
    {
        GridLayout res = new();
        res.CopyFrom(this, predicate);
        return res;
    }

    /// <inheritdoc/>
    public bool Equals(GridLayout? other)
    {
        if (other == null)
            return false;
        if (Object.ReferenceEquals(this, other))
            return true;
        return comparer.Equals(this, other);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is GridLayout layout && Equals(layout);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return comparer.GetHashCode(this);
    }
}

// Asc defaults to the type's own, the value options that skip defaults leave out of the JSON
/// <summary>The column a grid is sorted by, as a <see cref="Memento"/> saves it.</summary>
/// <param name="Name">The column's name.</param>
/// <param name="Asc">True for an ascending sort.</param>
public record SortedColumn(string Name, bool Asc = false);

/// <summary>
/// A grid's layout, filters and sort, as <see cref="ADataGrid{TGridItem}.CreateMemento"/> saves them and
/// <see cref="ADataGrid{TGridItem}.RestoreMemento"/> puts them back; serializable as JSON, and independent of the row type,
/// so one store can hold the mementos of every grid.
/// </summary>
/// <param name="Layout">The columns shown and the sizes the user set; null for a grid that is not layoutable.</param>
/// <param name="FiltersDefs">The filters applied, by column name.</param>
/// <param name="SorterColumns">The column the grid is sorted by, if any; empty when it is not sorted.</param>
// Always written: strict options require them, and options that skip nulls or defaults would leave them out.
// Only the derived type carries a "$type": a plain memento is written as before, and one without "$type" reads as a Memento.
// A host's own derived memento is written as its nearest known ancestor, as it was before the derived type existed
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(TreeMemento), "tree")]
public record Memento([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] GridLayout? Layout, IReadOnlyDictionary<string, AFilterDef> FiltersDefs, IReadOnlyList<SortedColumn> SorterColumns)
     : IEquatable<Memento>
{
    /// <summary>The equality <see cref="Equals(Memento?)"/> uses: the layout, the filters and the sorted columns.</summary>
    protected static readonly IEqualityComparer<Memento> comparer = EqComparers.Combo<Memento>(l => l
         .Add(l => l?.Layout)
         .Add(l => l?.FiltersDefs, EqComparers.ForDictionary<string, AFilterDef>())
         .Add(l => l?.SorterColumns, EqComparers.ForList<SortedColumn>()));

    /// <summary>
    /// Memento is a record, so a derived one is free to carry extra state, and the Equals the compiler
    /// generates for it starts by comparing EqualityContract. This one has to do the same or equality
    /// stops being symmetric: the base would accept a derived instance holding the same three members
    /// while the derived rejected the base.
    /// </summary>
    public virtual bool Equals(Memento? other)
    {
        if (other == null)
            return false;
        if (Object.ReferenceEquals(this, other))
            return true;
        if (other.EqualityContract != this.EqualityContract)
            return false;
        return comparer.Equals(this, other);
    }

    //public override bool Equals(object? obj) Not needed, generated by the compiler ...

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(EqualityContract, comparer.GetHashCode(this));
    }
}
