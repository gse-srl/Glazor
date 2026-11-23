using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Glazor.Grds;

public partial class Grigliaria<TGridItem>
{
    [Inject] private IServiceProvider EditServices { get; init; } = null!;

    /// <summary>
    /// Whether the grid edits rows in place, over an <see cref="IEditableGridModel{TGridItem}"/>: editors in the row being edited,
    /// Cancel and Post on its heading. <see cref="EditMode.Disabled"/>, the default, leaves editing to the page.
    /// </summary>
    [Parameter]
    public EditMode EditMode { get; set; }

    /// <summary>The row Ctrl+Ins inserts, made from the current one, null in an empty grid; without it the grid inserts nothing.</summary>
    [Parameter]
    public Func<TGridItem?, TGridItem>? NewItem { get; set; }

    /// <summary>
    /// Saves the row Enter or Post confirms, once its editors pass validation: the handler stores it and calls the model's
    /// <c>PostAsync</c>. Without it the grid calls <c>PostAsync</c> itself, which keeps the change only when the model's reload reads
    /// the rows the editors changed.
    /// </summary>
    [Parameter]
    public EventCallback<GridPost<TGridItem>> OnPost { get; set; }

    /// <summary>
    /// Removes the current row on Ctrl+Del: the handler asks for a confirmation if it likes, deletes the row, and drops it from
    /// the model, with <c>GetData().RemoveAt</c> on one in memory or <c>ReloadItemAsync</c> on an EF one. Without it the grid
    /// removes nothing.
    /// </summary>
    [Parameter]
    public EventCallback<TGridItem> OnRemove { get; set; }

    private EditContext? _editContext;
    private object? _editItem;
    private ValidationMessageStore? _editErrors;
    private IDisposable? _editValidation;
    private bool _focusEditor;
    private string? _focusColumn;   // the column whose cell takes the focus at the next render
    private string? _lastEditFocus;   // the column of the edited row's cell last focused
    private bool _posting;   // while OnPost saves, the row stays pending: a second Post would save it again, a Cancel drop it
    private bool _removing;   // while OnRemove deletes, the row stays current: a second Remove would hand it over again

    internal IEditableGridModel<TGridItem>? EditableModel =>
        EditMode == EditMode.Disabled ? null : GetModel() as IEditableGridModel<TGridItem>;

    internal bool IsEditingInPlace => EditableModel is { } m && (m.IsEditPending() || m.IsInsertPending());

    // The EditContext of the row being edited, made again for each edit
    internal EditContext? GetEditContext()
    {
        if (!IsEditingInPlace)
        {
            DropEditContext();
            return null;
        }
        TGridItem item = GetModel().GetCurrentItem().RequireNotNull();
        if (!ReferenceEquals(item, _editItem))
        {
            DropEditContext();
            _editItem = item;
            _editContext = new EditContext(item!);
            _editValidation = _editContext.EnableDataAnnotationsValidation(EditServices);
            _editErrors = new ValidationMessageStore(_editContext);
            _editContext.OnFieldChanged += (_, e) => _editErrors?.Clear(e.FieldIdentifier);
            _editContext.OnValidationStateChanged += (_, _) => StateHasChanged();   // the row heading shows the row's errors
        }
        return _editContext;
    }

    // The EditContext a cell edits under, null when it shows its value
    internal EditContext? CellEditContext(int rowIdx, IColumn<TGridItem> column) =>
        rowIdx == GetModel().GetCurrentRow() && (column.GetEditProperty() != null || column.GetEditRenderer() != null)
            ? GetEditContext() : null;

    // The errors no editor of the row shows: whole-row ones, and those of properties with no editor
    internal IEnumerable<string> RowErrors()
    {
        if (GetEditContext() is not { } ctx)
            return [];
        HashSet<string> shown = [];
        foreach (string name in GetColumnsToShow())
        {
            IColumn<TGridItem> column = GetModel().GetColumn(GetModel().GetColumnIndex(name));
            if (column.GetEditRenderer() == null && column.GetEditProperty() is { } p)
                shown.Add(p.Name);
        }
        return ctx.GetValidationMessages().Except(shown.SelectMany(n => ctx.GetValidationMessages(new FieldIdentifier(_editItem!, n))));
    }

    private void DropEditContext()
    {
        _lastEditFocus = null;
        _editValidation?.Dispose();
        _editValidation = null;
        _editContext = null;
        _editErrors = null;
        _editItem = null;
    }

    // True once after an edit starts, or a move off an edge of the column window: for the cell of the column aimed at, an
    // editor or a value, or else for the first editor
    internal bool ClaimEditorFocus(string columnName, bool editor)
    {
        if (!_focusEditor || (_focusColumn == null ? !editor : _focusColumn != columnName))
            return false;
        _focusEditor = false;
        return true;
    }

    private void FocusEditorWhenShown()
    {
        _focusEditor = true;
        _focusColumn = GetCurrentCol() is { } name && GetShownColumns().Contains(name) ? name : null;
    }

    // A new row starts from its first field: the first editable column, the column window scrolled back to it
    private void FocusFirstEditorWhenShown()
    {
        string? first = GetColumnsToShow().FirstOrDefault(n => GetModel().GetColumn(GetModel().GetColumnIndex(n)) is var c
            && (c.GetEditProperty() != null || c.GetEditRenderer() != null));
        if (first == null)
        {
            FocusEditorWhenShown();
            return;
        }
        _focusEditor = true;
        _focusColumn = first;
        if (IsCellCursorEnabled() && !IsFixedColumn(first))
            SetCurrentColImpl(first);
        else
            EnsureColumnIsShown(GetModel().GetColumnIndex(first));
    }

    /// <summary>Edits the current row in place, as F2 does; does nothing when the model cannot edit it now.</summary>
    protected internal async Task StartEditAsync()
    {
        if (EditableModel is not { } m || m.IsEditPending() || m.IsInsertPending() || GetModel().GetCurrentRow() < 0
                || !m.IsCurrentRowEditable())
            return;
        DropEditContext();
        await m.EditAsync();
        FocusEditorWhenShown();
        StateHasChanged();
    }

    /// <summary>Inserts the row <c>NewItem</c> makes and edits it, as Ctrl+Insert does; does nothing without it or during an edit.</summary>
    protected internal async Task InsertAsync()
    {
        if (EditableModel is not { } m || NewItem == null || m.IsEditPending() || m.IsInsertPending())
            return;
        DropEditContext();
        await m.InsertAsync(NewItem(GetModel().GetCurrentItem()), afterCurrentRow: false);
        FocusFirstEditorWhenShown();
        StateHasChanged();
    }

    /// <summary>
    /// Posts the row being edited, as Enter does: it validates the row, then hands it to <c>OnPost</c>, or posts it on the model
    /// without one; a row with errors stays edited with its messages.
    /// </summary>
    protected internal async Task PostAsync()
    {
        if (_posting || EditableModel is not { } m || GetEditContext() is not { } ctx)
            return;
        _editErrors!.Clear();
        if (ctx.Validate())
        {
            _posting = true;
            try
            {
                if (OnPost.HasDelegate)
                    await OnPost.InvokeAsync(new GridPost<TGridItem>((TGridItem)_editItem!, m.IsInsertPending(), AddEditError));
                else
                    await m.PostAsync();
            }
            finally
            {
                _posting = false;
            }
        }
        if (IsEditingInPlace)
            ctx.NotifyValidationStateChanged();
        else
            await FocusAsync();
        StateHasChanged();
    }

    /// <summary>Cancels the edit or the insert in place, as Esc does.</summary>
    protected internal async Task CancelAsync()
    {
        if (_posting || EditableModel is not { } m || !IsEditingInPlace)
            return;
        await m.CancelAsync();
        DropEditContext();
        await FocusAsync();
        StateHasChanged();
    }

    /// <summary>Hands the current row to <c>OnRemove</c>, as Ctrl+Delete does; does nothing without it or during an edit.</summary>
    protected internal async Task RemoveAsync()
    {
        if (_removing || EditableModel is not { } m || !OnRemove.HasDelegate || m.IsEditPending() || m.IsInsertPending()
                || GetModel().GetCurrentItem() is not { } item)
            return;
        _removing = true;
        try
        {
            await OnRemove.InvokeAsync(item);
        }
        finally
        {
            _removing = false;
        }
        StateHasChanged();
    }

    private void AddEditError(string propertyName, string message)
    {
        if (_editErrors != null && _editItem != null)
            _editErrors.Add(new FieldIdentifier(_editItem, propertyName), message);
    }

    internal bool IsEditedRow(int rowIdx) => IsEditingInPlace && rowIdx == GetModel().GetCurrentRow();

    private bool IsFixedColumn(string columnName) => GetModel().GetColumn(GetModel().GetColumnIndex(columnName)).IsFixed();

    // Whether the end of the edited row's cell of the column carries a stop that moves the column window: the stop before the
    // window, in the last fixed column, when columns are hidden before it; the stop after it, in the last column shown, when
    // columns are hidden after it. A stop sits at the end of a cell, since a focusable cell comes before its own children.
    internal bool HasEditEdge(string columnName, int step)
    {
        if (!IsEditingInPlace)
            return false;
        List<string> shown = GetShownColumns().ToList();
        return step < 0
            ? CanPrevCol() && shown.LastOrDefault(IsFixedColumn) == columnName
            : CanNextCol() && shown.LastOrDefault() == columnName;
    }

    // The stop before the window when no column is fixed: in the row's cell before the columns
    internal bool HasLeadingEditEdge() => IsEditingInPlace && CanPrevCol() && !GetShownColumns().Any(IsFixedColumn);

    // Tab or Shift+Tab reached an edge. From the column on the edge it shows the column beyond, whose cell takes the focus; from
    // elsewhere, a fixed column or the footer, it passes the focus on to the column on the edge
    internal void EditEdgeFocused(int step)
    {
        List<string> names = GetColumnsToShow().ToList();
        HashSet<string> shown = [.. GetShownColumns()];
        int edge = step > 0 ? names.FindLastIndex(shown.Contains) : names.FindIndex(n => shown.Contains(n) && !IsFixedColumn(n));
        if (edge < 0)
            return;
        int at = names[edge] == _lastEditFocus ? edge + step : edge;
        if (at < 0 || at >= names.Count || IsFixedColumn(names[at]))
            return;
        string target = names[at];
        _focusEditor = true;
        _focusColumn = target;
        if (IsCellCursorEnabled())
            SetCurrentColImpl(target);
        else
            EnsureColumnIsShown(GetModel().GetColumnIndex(target));
        StateHasChanged();
    }

    // The cell cursor follows the focus through the edited row, off the fixed columns
    internal void EditCellFocused(string columnName)
    {
        _lastEditFocus = columnName;
        if (IsCellCursorEnabled() && GetCurrentCol() != columnName && !IsFixedColumn(columnName))
            SetCurrentColImpl(columnName);
    }

    // OnClick and OnMove edit the row a move reaches
    internal bool IsFastEdit => EditMode >= EditMode.OnClick && EditableModel != null;

    // Posts the row edited, then moves and edits the row reached; a post refused, or one that moves the row, stays where it is
    private async Task MoveAndEditAsync(int? targetRow, Func<bool> move)
    {
        if (IsEditingInPlace)
        {
            int row = GetModel().GetCurrentRow();
            if (targetRow == row)
                return;
            int rowCount = GetModel().GetRowCount();   // a post that drops or adds a row shifts the indexes after it
            await PostAsync();
            if (IsEditingInPlace || GetModel().GetCurrentRow() != row || GetModel().GetRowCount() != rowCount)
                return;
            move();   // at the first or last row the same row is edited again, as PageUp and PageDown do
        }
        else
            move();
        await StartEditAsync();
    }

    internal async Task RowClickedAsync(int rowIdx, int? colIdx)
    {
        if (IsFastEdit)
            await MoveAndEditAsync(rowIdx, () => colIdx is int c ? SetCurrentCell(rowIdx, c) : SetCurrentRow(rowIdx));
        else if (colIdx is int c)
            SetCurrentCell(rowIdx, c);
        else
            SetCurrentRow(rowIdx);
    }

    // ↑, ↓, PageUp and PageDown, which OnMove makes edit the row they reach
    private async Task RowKeyAsync(Func<bool> move)
    {
        if (EditMode == EditMode.OnMove && EditableModel != null)
            await MoveAndEditAsync(null, move);
        else
            move();
    }

    // A double-click edits the row when the grid edits in place, else it is the host's
    internal async Task DblClickAsync(int rowIdx, int? colIdx)
    {
        if (EditableModel != null)
            await StartEditAsync();
        else
            await OnDblClick.InvokeAsync(new DblClickEvent(rowIdx, colIdx));
    }
}
