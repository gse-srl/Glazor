namespace Glazor.Grds.Mdls;

/// <summary>A model whose current row can be edited, or a new row inserted: either freezes the current row until it is posted or cancelled.</summary>
public interface IEditableGridModel<TGridItem> : IGridModel<TGridItem>
{

    /// <summary>
    /// Whether an edit of the current row has started and was neither posted nor cancelled.
    /// </summary>
    bool IsEditPending();

    /// <summary>
    /// Whether an inserted row is pending, neither posted nor cancelled.
    /// </summary>
    bool IsInsertPending();

    /// <summary>Whether the current row can be edited: the grid offers no edit otherwise, and <see cref="EditAsync"/> throws.</summary>
    bool IsCurrentRowEditable();

    /// <summary>
    /// Switch to the edit state, the editing form should be bound to the returned item.
    /// </summary>
    /// <returns>The model's own row, which the form changes in place.</returns>
    /// <exception cref="InvalidOperationException">An edit or insert is pending, there is no current row, or it is not editable.</exception>
    public ValueTask<TGridItem> EditAsync();

    /// <summary>
    /// Switch to the insert state, the editing form should be bound to the returned item.
    /// While it is pending, the row being inserted is the current row; it is not stored yet, so <c>GetDataView()</c> leaves
    /// it out.
    /// </summary>
    /// <param name="newItemProto">The row to insert.</param>
    /// <param name="afterCurrentRow">Whether the row goes after the current row rather than before it; with no current row it
    /// goes at the top.</param>
    /// <returns><paramref name="newItemProto"/>, for the form to bind to.</returns>
    public ValueTask<TGridItem> InsertAsync(TGridItem newItemProto, bool afterCurrentRow);

    /// <summary>
    /// Cancel any pending edit or insert operation, if there are none, nothing happens.
    /// </summary>
    /// <param name="ct">Cancels the read of the row whose edit is cancelled; the edit ends all the same.</param>
    public ValueTask CancelAsync(CancellationToken ct = default);

    /// <summary>
    /// Confirm a pending edit or insert operation.
    /// </summary>
    /// <param name="ct">Cancels the read that follows the post; the edit or insert ends all the same.</param>
    public ValueTask PostAsync(CancellationToken ct = default);
}
