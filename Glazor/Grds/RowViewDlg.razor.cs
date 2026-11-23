using Glazor.Grds.Decs;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds;

/// <summary>
/// The grid's current row as a list: one line per column, with its label and its value drawn as the grid draws it. The
/// columns of the layout come first, then a separator and the hidden ones. Opened by the grid's <c>ShowRowDialogAsync</c>.
/// </summary>
public partial class RowViewDlg<TGridItem>
{
    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;
    private IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    private ADataGrid<TGridItem>? _grid;
    private DataGridModel<Field>? _fields;
    private TGridItem? _item;
    private int _rowIdx = -1;

    private readonly string _titleId = "t" + Guid.NewGuid().ToString("N");   // aria-labelledby, unique per dialog on the page

    private DialogBox jsDialog = null!;
    private Grigliaria<Field>? _fieldsGrid;

    /// <inheritdoc/>
    protected override bool ShouldRender() => jsDialog != null && jsDialog.IsVisible;

    /// <summary>Opens the dialog on the current row of <paramref name="grid"/>.</summary>
    public async Task ShowModalAsync(ADataGrid<TGridItem> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _grid = grid;
        _fields = CreateModel(grid);
        ReadCurrentRow();
        await jsDialog.ShowModalAsync();
        StateHasChanged();   // the title follows the row
        await _fieldsGrid!.RepaintAsync();
        await _fieldsGrid.FocusAsync();
    }

    /// <summary>Closes the dialog, as its Close button, Esc and Enter do; the grid's current row stays where the dialog moved it.</summary>
    public async Task CloseAsync() => await jsDialog.CloseAsync();

    private void Closed()
    {
        _grid = null;
        _fields = null;
        _item = default;
        _rowIdx = -1;
    }

    // Called by the grid once it has synced: a row that Previous or Next reached has been fetched, under the grid's spinner
    internal void GridSynced()
    {
        if (_grid == null || !jsDialog.IsVisible || !_grid.IsIdle())
            return;
        ReadCurrentRow();
        StateHasChanged();
    }

    private void ReadCurrentRow()
    {
        IGridModel<TGridItem> model = _grid.RequireNotNull().GetModel();
        _rowIdx = model.GetCurrentRow();
        _item = _rowIdx < 0 || _rowIdx >= model.GetCurrentRowCount() ? default : model.GetItem(_rowIdx);
    }

    private bool CanMove(int delta)
    {
        if (_grid == null)
            return false;
        IGridModel<TGridItem> model = _grid.GetModel();
        int row = model.GetCurrentRow() + delta;
        return row >= 0 && row < model.GetRowCount() && !model.IsCurrentRowFrozen();
    }

    private async Task MoveAsync(int delta)
    {
        if (!CanMove(delta))
            return;
        _grid!.SetCurrentRow(_grid.GetCurrentRow() + delta);   // the grid renders, then calls GridSynced
        if (!CanMove(delta) && _fieldsGrid != null)
            await _fieldsGrid.FocusAsync();   // the button used turns disabled: the focus would fall to the page, and the dialog's keys with it
    }

    private string RowCounter()
    {
        if (_grid == null || _rowIdx < 0)
            return "";
        int rowCount = _grid.GetModel().GetRowCount();
        return (_rowIdx + 1) + "/" + (rowCount == int.MaxValue ? "..." : rowCount);
    }

    // A line of the list: a column of the grid, or the separator when Column is null
    internal sealed record Field(string Label, int ColIdx, IColumn<TGridItem>? Column);

    private DataGridModel<Field> CreateModel(ADataGrid<TGridItem> grid)
    {
        IGridModel<TGridItem> gridModel = grid.GetModel();
        List<Field> fields = [];
        HashSet<string> shown = [];
        foreach (string colName in grid.GetColumnsToShow())
        {
            shown.Add(colName);
            if (FieldOf(grid, gridModel.GetColumnIndex(colName), colName) is { } field)
                fields.Add(field);
        }
        bool separator = false;
        for (int i = 0; i < gridModel.GetColumnCount(); i++)
        {
            string colName = gridModel.GetColumnName(i);
            if (shown.Contains(colName) || FieldOf(grid, i, colName) is not { } field)
                continue;
            if (!separator)
                fields.Add(new Field(Texts.Get(TextKeys.HiddenColumns), -1, null));
            separator = true;
            fields.Add(field);
        }
        //
        DataGridModel<Field> model = new(fields);
        model.AddColumn(nameof(Field.Label), new Column<Field, string>(Texts.Get(TextKeys.Column))
        {
            PreferredSize = 10,
            Fixed = true,   // the cell cursor goes to the values
            Alignment = Alignment.Center,
            Sortable = false,
            Filterable = false,
            CellClassFn = ctx => ctx.Item.Column == null ? "separator" : "label",
        }, f => f.Label);
        model.AddColumn("Value", new Column<Field, object?>(Texts.Get(TextKeys.Value))
        {
            PreferredSize = 20,
            Alignment = Alignment.Left,
            Sortable = false,
            Filterable = false,
            CellClassFn = ctx => ctx.Item.Column == null ? "separator" :
                Cell(ctx) is { } cell ? cell.Column.GetCellClassFn()?.Invoke(cell) : null,
            Renderer = ctx => Cell(ctx) is { } cell ? DefaultCell<TGridItem>.Value(cell) : _ => { },
        }, f => f.Column != null && _item is { } item ? gridModel.GetValue(item, f.ColIdx) : null);
        return model;
    }

    // The sizeless columns stay out, as in the layout dialog, with those that say so
    private static Field? FieldOf(ADataGrid<TGridItem> grid, int colIdx, string colName)
    {
        IColumn<TGridItem> col = grid.GetColumn(colIdx);
        if (col.GetPreferredSize() == 0 || !col.IsListedInRowDialog())
            return null;
        string label = col.GetLabel();
        return new Field(string.IsNullOrEmpty(label) ? $"@({colName})" : label, colIdx, col);
    }

    // The cell of the grid a line stands for, as the column's renderer and class function take it
    private DecoratorContext<TGridItem, object>? Cell(DecoratorContext<Field, object?> ctx) =>
        ctx.Item.Column is { } col && _item is { } item ? new(item, ctx.Value, _rowIdx, col) : null;
}
