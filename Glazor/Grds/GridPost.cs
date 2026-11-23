namespace Glazor.Grds;

/// <summary>
/// The row a grid editing in place posts, for its <c>OnPost</c> to save. The handler calls the model's <c>PostAsync</c> once the
/// row is saved; until then the row stays in edit, with the errors <see cref="AddError"/> puts on it.
/// </summary>
public sealed class GridPost<TGridItem>
{
    private readonly Action<string, string> _addError;

    internal GridPost(TGridItem item, bool inserting, Action<string, string> addError)
    {
        Item = item;
        Inserting = inserting;
        _addError = addError;
    }

    /// <summary>The row, as its editors left it.</summary>
    public TGridItem Item { get; }

    /// <summary>True for a new row, false for one edited.</summary>
    public bool Inserting { get; }

    /// <summary>
    /// Shows the message on the editor of the property, or on the row heading when no editor shows it; the next change to that
    /// property, or the next post, clears it.
    /// </summary>
    public void AddError(string propertyName, string message) => _addError(propertyName, message);
}
