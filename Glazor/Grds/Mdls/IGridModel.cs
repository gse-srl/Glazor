using System.ComponentModel;

namespace Glazor.Grds.Mdls;

/// <summary>What changed in a model, as <c>OnModelChanged</c> reports it.</summary>
public enum GridModelChangeType
{
    /// <summary>The model synced: its rows were read, filtered or sorted again.</summary>
    AfterSync = 1,
    /// <summary>Anything else, the current row for instance.</summary>
    Other = 2,
}

/// <summary>The handler of <see cref="IGridModel{TGridItem}.OnModelChanged"/>, told what changed.</summary>
public delegate void OnModelChangedEventHandler(GridModelChangeType e);

/// <summary>
/// The contract a grid consumes its rows through. Not intended for direct implementation: the column
/// registration members rely on internals of <see cref="GridColumn{TGridItem, VT}"/>. To write a custom
/// model derive from <see cref="AGridModel{TGridItem}"/>, or wrap an existing one through
/// <see cref="GridModelWrapper{TGridItem}"/> and its subclasses.
/// </summary>
public interface IGridModel<TGridItem>
{
    /// <summary>
    /// Notified when something in the model has changed, even just the current row.
    /// </summary>
    public event OnModelChangedEventHandler? OnModelChanged;

    /// <summary>The number of columns the model has, shown or not.</summary>
    int GetColumnCount();

    /// <summary>
    /// The version of the model's columns.
    /// </summary>
    /// <returns>A different value whenever a column is added or removed</returns>
    int GetColumnsVersion();

    /// <summary>
    /// Gets the index of the specified column name.
    /// </summary>
    /// <param name="columnName">The name of the column.</param>
    /// <returns>Less than 0 if not found.</returns>
    int GetColumnIndex(string columnName);

    /// <summary>
    /// Gets the column at the specified index.
    /// </summary>
    /// <param name="columnIndex">The index of the column.</param>
    /// <returns>Always not null; throws an exception if the column doesn't exist.</returns>
    IColumn<TGridItem> GetColumn(int columnIndex);

    /// <summary>The column at <paramref name="columnIndex"/> with its accessor and, when it has one, its push-down expression; throws when it does not exist.</summary>
    IColumnInfo<TGridItem> GetColumnInfo(int columnIndex);

    /// <summary>The name of the column at <paramref name="columnIndex"/>; throws when it does not exist.</summary>
    string GetColumnName(int columnIndex);

    /// <summary>
    /// Gets the currently loaded rows. Generally equal to <see cref="GetRowCount"/>, but can be less if the model supports paging.
    /// </summary>
    int GetCurrentRowCount();

    /// <summary>
    /// Gets the total rows count. If not known yet, returns <see cref="int.MaxValue"/>.
    /// </summary>
    int GetRowCount();

    /// <summary>
    /// Synchronizes the model, ensuring internal state consistency before display.
    /// </summary>
    void Sync();

    /// <summary>
    /// Gets the value at the specified row and column coordinates.
    /// </summary>
    /// <param name="rowIndex">The row index.</param>
    /// <param name="columnIndex">The column index.</param>
    /// <returns>The value at the specified coordinates.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A row out of range, on an in-memory model as on a paging one, whether the
    /// paging model knows the table end or not.</exception>
    object? GetValueAt(int rowIndex, int columnIndex);

    /// <summary>
    /// It may require fetch even if all the data has been loaded because, for example, some pages have been moved to an external storage.
    /// It must not trigger a model Sync.
    /// </summary>
    bool RequiresFetch();

    /// <summary>
    /// Ensures the requested rows exist in memory, ie. they are easily accessible because the "model user" is about to look at them.
    /// </summary>
    /// <param name="rowIndex">The starting row index.</param>
    /// <param name="count">The number of rows to fetch.</param>
    /// <param name="ct">Cancels the fetch, first page included; a paging model stops at its next check and
    /// throws <see cref="OperationCanceledException"/>. An in-memory model has nothing to cancel.</param>
    /// <returns>The real number of rows found.</returns>
    ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default);

    /// <summary>
    /// Gets the current row index. A value less than 0 means none.
    /// </summary>
    int GetCurrentRow();

    /// <summary>
    /// Sets the current row index.
    /// </summary>
    /// <param name="rowIndex">The row index to set as current.</param>
    void SetCurrentRow(int rowIndex);

    /// <summary>
    /// Indicates whether editing is happening on the current row, freezing it.
    /// </summary>
    /// <returns>True if the current row is frozen; otherwise, false.</returns>
    bool IsCurrentRowFrozen();

    /// <summary>
    /// Called by <see cref="EnrichGridColumn{TGridItem, VT}"/> while the grid collects its columns, to replace the
    /// renderers of a column already registered under <c>gridColumn.Name</c>. Implemented by
    /// <see cref="AGridModel{TGridItem}"/> and forwarded by the wrappers; not for host code to call.
    /// </summary>
    /// <typeparam name="T">The column value type.</typeparam>
    /// <param name="grid">The grid collecting columns.</param>
    /// <param name="gridColumn">The component carrying the templates.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void EnrichColumn<T>(AGrid<TGridItem> grid, EnrichGridColumn<TGridItem, T> gridColumn);

    /// <summary>
    /// Called by <see cref="GridColumn{TGridItem, VT}"/> while the grid collects its columns, to register or replace
    /// the column named <c>gridColumn.Name</c>. Implemented by <see cref="AGridModel{TGridItem}"/> and forwarded by
    /// the wrappers; not for host code to call.
    /// </summary>
    /// <typeparam name="T">The column value type.</typeparam>
    /// <param name="grid">The grid collecting columns.</param>
    /// <param name="gridColumn">The component declaring the column.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SetColumn<T>(AGrid<TGridItem> grid, GridColumn<TGridItem, T> gridColumn);

    /// <summary>
    /// Called by the grid for a <see cref="GridColumn{TGridItem, VT}"/> removed from the markup, to remove the column it
    /// registered. Implemented by <see cref="AGridModel{TGridItem}"/> and forwarded by the wrappers; not for host code to call.
    /// </summary>
    /// <param name="grid">The grid the column leaves.</param>
    /// <param name="columnName">The name the component registered.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void UnsetColumn(AGrid<TGridItem> grid, string columnName);

    /// <param name="rowIndex">The row's index.</param>
    /// <returns>The item object at the given index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A row out of range, as <see cref="GetValueAt"/> says.</exception>
    public TGridItem GetItem(int rowIndex);

    /// <summary>
    /// The row that is the same instance, or, when GetItemKeyFn() gives a key, the row with the same key; without a key, the row
    /// that is the same instance or Equals.<br/>
    /// On a paging model only the rows already fetched are searched; a row not yet loaded yields -1.
    /// </summary>
    /// <returns>The row index of the given item, less than 0 if not found.</returns>
    public int RowIndexOf(TGridItem item);

    /// <summary>
    /// Whether the two are the same instance, or, when GetItemKeyFn() gives a key, have the same key; without a key, whether they
    /// are the same instance or Equals.
    /// </summary>
    /// <returns>true even if they are both null</returns>
    public bool SameItem(TGridItem? item1, TGridItem? item2);

    /// <returns>The current item, null if none.</returns>
    public TGridItem? GetCurrentItem();

    /// <summary>
    /// Makes current the row that is the same item as <paramref name="item"/>, as <see cref="SameItem"/> compares them; a paged
    /// model looks among the rows already read only.
    /// </summary>
    /// <exception cref="ArgumentException">No such row.</exception>
    public void SetCurrentItem(TGridItem item);

    /// <summary>The type of the rows, <typeparamref name="TGridItem"/>.</summary>
    public Type GetItemType();

    /// <summary>
    /// If the item has a sort of primary key, ie. something that uniquely identifies it, return it here.
    /// </summary>
    /// <returns>The function that gives a row's key, or null when the rows have no key.</returns>
    public Func<TGridItem, object>? GetItemKeyFn();

    /// <param name="item">The item to extract value from.</param>
    /// <param name="columnIndex">The columnIndex which must be mapped to a property of the item
    ///     or to a computed value.</param>
    /// <returns>The column's value for <paramref name="item"/>.</returns>
    public object? GetValue(TGridItem item, int columnIndex);

    /// <returns>A read-only snapshot list reflecting any sort order or filter applied, it is 
    /// unmodifiable but can change if "back-end" collection or data changes.<br/>
    /// In case of paging, it contains only items already fetched, and its size must
    /// be equal to GetCurrentRowCount().<br/>
    /// It holds the stored rows: while an editable model has an insert pending it lacks the row being inserted, so its size is
    /// one less than GetCurrentRowCount() and GetCurrentRow() is not an index into it. Read rows by position with GetItem().<br/>
    /// NOTE Moreover when paging with a model like EFDataGridModel do not use .ToList(), .ToArray(), etc on the result, 
    /// the system will not let load all data in memory by throwing an exception.</returns>
    public IReadOnlyList<TGridItem> GetDataView();

    /// <returns>The value must change any time one or more CRUD operations have been completed
    /// on the content model and after the model has been synchronized (see <see cref="Sync"/>).<br/>
    /// This can be used by wrapping models to update their state when the wrapped model
    /// changes.</returns>
    public int GetDataVersion();
}
