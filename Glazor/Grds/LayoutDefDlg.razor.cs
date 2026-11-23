using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds;

/// <summary>
/// This dialog is a little bit peculiar in the sense that things are applied immediately, so the user can see each change,
/// and it's up to the caller to restore things on Cancel.
/// </summary>
public partial class LayoutDefDlg<TGridItem>
{
    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;
    private IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    private OrderedDictionary<string, NoValue>? _showableCols;
    private OrderedDictionary<string, NoValue>? _showedCols;
    // The shown columns of size 0, each with the listed column before it: out of both lists, written back in their place
    private Dictionary<string, string?>? _sizelessShown;

    private IGridModel<TGridItem>? _model;
    private ILayoutable<TGridItem>? _layoutable;
    private IFilterable<TGridItem>? _filterable;

    private string? title;
    private readonly string _titleId = "t" + Guid.NewGuid().ToString("N");   // aria-labelledby, unique per dialog on the page

    private ElementReference okButton;
    private DialogBox jsDialog = null!;

    private AMemGridModel<ColumnInfo>? _leftModel;
    private SetSelectionModel<ColumnInfo>? _leftSelectionModel;

    private AMemGridModel<ColumnInfo>? _rightModel;
    private SetSelectionModel<ColumnInfo>? _rightSelectionModel;

    private MultiSelGrid<ColumnInfo>? leftGridRef;
    private MultiSelGrid<ColumnInfo>? rightGridRef;

    private Func<Task>? _restoreOnClose;

    /// <inheritdoc/>
    protected override bool ShouldRender() => jsDialog != null && jsDialog.IsVisible;

    /// <summary>
    /// Opens the dialog over <paramref name="layoutable"/>. Each change applies to the grid as the user makes it.
    /// </summary>
    /// <param name="layoutable">The grid whose layout the dialog changes.</param>
    /// <param name="title">The dialog's title; null for none.</param>
    /// <param name="restoreOnClose">Called on Cancel or Esc: puts the grid back as it was when the dialog opened, since each change
    /// has already been applied.</param>
    public async Task ShowModalAsync(ILayoutable<TGridItem> layoutable, string? title, Func<Task> restoreOnClose)
    {
        ArgumentNullException.ThrowIfNull(layoutable);
        _model = layoutable.GetModel();
        _layoutable = layoutable;
        _filterable = layoutable is IFilterable<TGridItem> fff ? fff : null;
        this.title = title;
        IEnumerable<string> showedCols = layoutable.GetColumnsToShow();
        _showedCols = new(showedCols.Count());
        _sizelessShown = new();
        string? lastListed = null;
        foreach (string colName in showedCols)
        {
            int colIdx = _model.GetColumnIndex(colName);
            IColumn? col = colIdx < 0 ? null : _model.GetColumn(colIdx);
            if (col == null)
                continue;
            if (col.GetPreferredSize() != 0)
            {
                _showedCols.Add(colName, NoValue.Value);
                lastListed = colName;
            }
            else
                _sizelessShown[colName] = lastListed;
        }
        _showableCols = new(_model.GetColumnCount());
        for (int i = 0; i < _model.GetColumnCount(); i++)
        {
            string colName = _model.GetColumnName(i);
            IColumn col = _model.GetColumn(i);
            if (col.GetPreferredSize() != 0 && !_showedCols.ContainsKey(colName))
            {
                _showableCols.Add(colName, NoValue.Value);
            }
        }
        //
        _leftModel = CreateModel(true);
        _leftSelectionModel = new SetSelectionModel<ColumnInfo>(new HashSet<ColumnInfo>(ColumnInfo.comparer), defensiveCopy: false);
        //
        _rightModel = CreateModel(false);
        _rightSelectionModel = new SetSelectionModel<ColumnInfo>(new HashSet<ColumnInfo>(ColumnInfo.comparer), defensiveCopy: false,
            ci => !ci.Fixed);
        //
        _restoreOnClose = restoreOnClose;
        await jsDialog.ShowModalAsync();
        StateHasChanged();   // the dialog's own attributes (aria-labelledby) follow the title, not only its content
        await leftGridRef!.RepaintAsync();
        await rightGridRef!.RepaintAsync();
        await rightGridRef.FocusAsync();
    }

    /// <summary>Closes the dialog and puts back the layout it started from.</summary>
    public async Task CancelAsync()
    {
        Func<Task>? restoreOnClose = _restoreOnClose;
        _restoreOnClose = null;   // the close event that follows must not restore again
        await jsDialog.CloseAsync();
        if (restoreOnClose != null)
            await restoreOnClose.Invoke();
    }

    private async Task OnCloseAsync()
    {
        // This is called whenever the dialog is closed even for a submit, we must restore here to properly handle the esc key
        if (_restoreOnClose != null)
            await _restoreOnClose.Invoke();
        _model = null;
        _layoutable = null;
        _filterable = null;
        _showedCols = null;
        _sizelessShown = null;
        _showableCols = null;
        _leftModel = null;
        _leftSelectionModel = null;
        _rightModel = null;
        _rightSelectionModel = null;
        _restoreOnClose = null;
        title = null;
    }

    /// <summary>Keeps the layout the dialog applied and closes it, as its OK button does; Cancel puts back the layout it started from.</summary>
    public async Task SubmitAsync()
    {
        await okButton.FocusAsync();  // So we move data into the model
        _restoreOnClose = null;  // prevent layout restoring
        await jsDialog.CloseAsync();
    }

    /// <summary>Moves the columns selected, or all with <paramref name="moveAll"/>, from the hidden list to the shown one, or back.</summary>
    protected async Task MoveAsync(bool leftToRight, bool moveAll)
    {
        IList<ColumnInfo> sourceView = leftToRight ? new List<ColumnInfo>(_leftModel.RequireNotNull().GetDataView()) :
            new List<ColumnInfo>(_rightModel.RequireNotNull().GetDataView());
        IList<ColumnInfo> source = leftToRight ? _leftModel!.GetData() :
            _rightModel!.GetData();
        SetSelectionModel<ColumnInfo> sourceSel = leftToRight ? _leftSelectionModel.RequireNotNull() :
            _rightSelectionModel.RequireNotNull();
        //
        IList<ColumnInfo> dest = !leftToRight ? _leftModel!.GetData() :
            _rightModel!.GetData();
        //
        int destIdx = Math.Max(0, !leftToRight ? _leftModel!.GetRowCount() : _rightModel!.GetCurrentRow());
        for (; destIdx < dest.Count && dest[destIdx].Fixed; destIdx++)
        {
        }
        // 
        OrderedDictionary<string, NoValue> selected;
        if (moveAll)
        {
            selected = new(leftToRight ? _showableCols! : _showedCols!);
        }
        else
        {
            selected = new();
            foreach (var sel in sourceSel.Selected())
            {
                selected.Add(sel.ColumnName, NoValue.Value);
            }
        }
        // 
        for (int i = 0, added = 0; i < sourceView.Count; i++)
        {
            ColumnInfo ci = sourceView[i];
            if (ci.Fixed)
                continue;
            string cname = ci.ColumnName;
            if (!selected.Remove(cname))
                continue;
            if (!source.Remove(ci))
                throw new InvalidOperationException($"Cannot remove: {ci.ColumnName}");
            dest.Insert(destIdx + (added++), ci);
        }
        _leftSelectionModel!.Clear();
        _rightSelectionModel!.Clear();
        _showableCols!.Clear();
        _showedCols!.Clear();
        foreach (var ci in _leftModel!.GetData())
        {
            _showableCols.Add(ci.ColumnName, NoValue.Value);
        }
        foreach (var ci in _rightModel!.GetData())
        {
            _showedCols.Add(ci.ColumnName, NoValue.Value);
        }
        _layoutable!.SetColumnsToShow(ColumnsToShow());
        if (leftToRight)
            ShowCurrentColumn();
        StateHasChanged();
    }

    // The column the right list's current row names, the one just moved, is scrolled into the grid's window, and becomes the
    // grid's current column when it has a cell cursor, which does not rest on a fixed one
    private void ShowCurrentColumn()
    {
        if (_rightModel!.GetCurrentItem() is not { } current)
            return;
        if (_layoutable!.IsCellCursorEnabled() && !current.Fixed)
            _layoutable.SetCurrentCol(current.ColumnName);
        else
            _layoutable.EnsureColumnIsShown(current.ColumnName);
    }

    /// <summary>
    /// The listed columns with the size-0 ones put back after the column each followed, or last when that column was hidden.
    /// </summary>
    private IEnumerable<string> ColumnsToShow()
    {
        Dictionary<string, string?> sizeless = _sizelessShown.RequireNotNull();
        foreach (string col in sizeless.Where(kv => kv.Value == null).Select(kv => kv.Key))
            yield return col;
        foreach (string col in _showedCols.RequireNotNull().Keys)
        {
            yield return col;
            foreach (string after in sizeless.Where(kv => kv.Value == col).Select(kv => kv.Key))
                yield return after;
        }
        foreach (string col in sizeless.Where(kv => kv.Value != null && !_showedCols.ContainsKey(kv.Value)).Select(kv => kv.Key))
            yield return col;
    }

    private bool IsMoveEnabled(bool leftToRight, bool moveAll)
    {
        IGridModel<ColumnInfo> source = leftToRight ? _leftModel.RequireNotNull() : _rightModel.RequireNotNull();
        SetSelectionModel<ColumnInfo> sel = leftToRight ? _leftSelectionModel.RequireNotNull() : _rightSelectionModel.RequireNotNull();
        return !source.IsCurrentRowFrozen() &&
            (moveAll && source.GetRowCount() > 0 ||
             !moveAll && sel.SelectedCount() > 0)
            && (leftToRight || _sizelessShown!.Count > 0   // an empty list would show the default columns
                || (moveAll ? _rightModel!.GetData().Any(ci => ci.Fixed) : sel.SelectedCount() < source.GetRowCount()));
    }

    private async Task OnRowShiftAsync()
    {
        _showedCols.RequireNotNull().Clear();
        foreach (var ci in _rightModel!.GetData())
        {
            _showedCols!.Add(ci.ColumnName, NoValue.Value);
        }
        _layoutable!.SetColumnsToShow(ColumnsToShow());
        ShowCurrentColumn();
        StateHasChanged();
    }

    internal sealed record ColumnInfo(string ColumnName, string ColumnLabel, bool Fixed)
    {
        public static readonly IEqualityComparer<ColumnInfo> comparer = EqComparers.Property<ColumnInfo, string>(ci => ci?.ColumnName);

        public int PreferredSize { get; set; }

        public ColumnInfo(string colName, IColumn<TGridItem> col) : this(colName, col.GetLabel(), col.IsFixed())
        {
            PreferredSize = col.GetPreferredSize();
        }
    }

    private AMemGridModel<ColumnInfo> CreateModel(bool left)
    {
        IList<ColumnInfo> colInfos;
        if (left)
        {
            colInfos = new List<ColumnInfo>(_showableCols.RequireNotNull().Count);
            foreach (string colName in _showableCols!.Keys)
            {
                int colIdx = _model.RequireNotNull().GetColumnIndex(colName);
                if (colIdx < 0)
                    throw new InvalidOperationException($"Column not found: {colName}");
                IColumn<TGridItem> col = _layoutable.RequireNotNull().GetColumn(colIdx);
                colInfos.Add(new ColumnInfo(colName, col));
            }
        }
        else
        {
            colInfos = new List<ColumnInfo>(_showedCols.RequireNotNull().Count);
            foreach (string colName in _showedCols.RequireNotNull().Keys)
            {
                int colIdx = _model.RequireNotNull().GetColumnIndex(colName);
                if (colIdx < 0)
                    throw new InvalidOperationException($"Column not found: {colName}");
                IColumn<TGridItem> col = _layoutable.RequireNotNull().GetColumn(colIdx);
                colInfos.Add(new ColumnInfo(colName, col));
            }
        }
        AMemGridModel<ColumnInfo> model = left ?
            new DataGridModel<ColumnInfo>(colInfos) :
            new GridModel<ColumnInfo>(colInfos)
            {
                AreRowsSwappableFn = (model, row1, row2) =>
                {
                    ColumnInfo col1 = model.GetItem(row1);
                    ColumnInfo col2 = model.GetItem(row2);
                    return !col1.Fixed && !col2.Fixed;
                }
            };
        model.AddColumn(nameof(ColumnInfo.ColumnLabel), new Column<ColumnInfo, string>(Texts.Get(TextKeys.Label))
        {
            PreferredSize = 24,
            Sortable = left,
            Filterable = left,
        }, ci => String.IsNullOrEmpty(ci.ColumnLabel) ? $"@({ci.ColumnName})" : ci.ColumnLabel);
        model.AddColumn(nameof(ColumnInfo.PreferredSize), new Column<ColumnInfo, int>(Texts.Get(TextKeys.PreferredSize))
        {
            PreferredSize = 7,
            Sortable = left,
            Filterable = left,
        }, ci => ci.PreferredSize);
        return model;
    }
}
