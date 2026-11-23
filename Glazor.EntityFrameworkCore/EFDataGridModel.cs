using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Grds.Props;
using Glazor.Misc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Linq.Expressions;

namespace Glazor.EntityFrameworkCore;

/// <summary>
/// Represents a data grid model backed by Entity Framework, providing support for paging, sorting, filtering, and
/// asynchronous data operations for a collection of items of type TGridItem.
/// </summary>
/// <remarks>EFDataGridModel enables efficient data virtualization and manipulation for large datasets by
/// integrating with Entity Framework queries. It supports both paged and full data loading, in-memory and database-side
/// sorting/filtering, and provides mechanisms for updating, reloading, and tracking changes to the grid data. The model
/// is designed for use in UI data grids that require dynamic, responsive data access and manipulation. Thread safety is
/// not guaranteed; access from multiple threads should be synchronized externally if needed.</remarks>
/// <typeparam name="TDbContext">The EF Core context type the model queries through <c>DbContextFactory</c>.</typeparam>
/// <typeparam name="TGridItem">The type of the items managed by the data grid model. Must be a reference type.</typeparam>
public class EFDataGridModel<TDbContext, TGridItem> : AGridModel<TGridItem>, IDataGridModel<TGridItem>, IReloadableGridModel<TGridItem>,
        IAsyncGridModel<TGridItem>, IUpdatableModel<TGridItem>, IDisposable
    where TDbContext : DbContext where TGridItem : class
{
    private static readonly ILogger<EFDataGridModel<TDbContext, TGridItem>> logger = LoggerSupport.Logger<EFDataGridModel<TDbContext, TGridItem>>();

    private ISortableList<TGridItem>? _pocos;
    private bool _allDataRead;
    private int _notInMemFilteredPocosCount;
    private IReadOnlyList<Func<TGridItem, object?>>? _baseQueryOrder;   // the getters of the row members the base query orders by
    private bool _baseQueryOrderRead;

    private int _currentRow;

    /// <summary>
    /// Default is false, when false paging is activated.
    /// </summary>
    public bool ReadAll { get; init; } = false;

    /// <summary>
    /// Default true that means that we try to restore the current row via its PK even if it requires extra data to be queried.
    /// </summary>
    public bool AlwaysRestoreCurrentPK { get; init; } = true;

    /// <summary>
    /// Where the paged model spills the chunks of rows it evicts from memory, as plaintext MessagePack files.
    /// Default null, meaning the OS temp directory. This makes sense only if ReadAll is false.
    /// </summary>
    public string? SpillDirectory { get; init; }

    private int _minRowsToRead = 1024;
    /// <summary>
    /// default 1024 and never less than 10, this makes sense only if ReadAll is false
    /// </summary>
    public int MinRowsToRead
    {
        get => _minRowsToRead;
        init
        {
            _minRowsToRead = Math.Max(10, value);
        }
    }

    private RestorePoint? _memento;
    private int _version;

    private string? _sortedColumn;
    private bool _sortedAsc = true;
    private bool _unsortedInMemory;   // a row set at its index, or a read that failed: the type-ahead sorts the rows read again before it searches them

    private readonly FiltersDefs _filtersDefs;

    /// <summary>
    /// Gives the context of each query and each row reload, disposed when it ends; <see cref="RefineItemAsync"/> gets the same one.
    /// </summary>
    public required IDbContextFactory<TDbContext> DbContextFactory { get; init; }

    /// <summary>
    /// The optional parameter is set when the model needs to refresh a single entity to let you,
    /// for example, detach the entity from the DbContext, if needed, something like dbContext.Entry(myEntity).State = EntityState.Detached.
    /// </summary>
    public required Func<TDbContext, TGridItem?, IQueryable<TGridItem>> BaseQueryFn { get; init; }

    /// <summary>
    /// The key of a row, as an expression so it can take part in queries: a column, or several as an anonymous type, a record, a
    /// class whose constructor takes its properties, or a tuple (<c>a => new { a.Policy, a.Number }</c>). Each member must be comparable, and the key as a whole must compare by value:
    /// a composite key of a class that does not throws <see cref="InvalidOperationException"/> on first use.
    /// </summary>
    public required Expression<Func<TGridItem, object>> ItemKeyExpr { get; init; }

    /// <summary>
    /// Creates the list that holds the rows read, given a capacity hint. Without it a paged model holds them in an
    /// <see cref="OnDiskChunkedList{T}"/>, which spills to <see cref="SpillDirectory"/>, and a <see cref="ReadAll"/> one in memory.
    /// </summary>
    public Func<int, ValueTask<ISortableList<TGridItem>>>? CreateItemsListFn { get; init; }

    private ItemKeyHelper<TGridItem>? _itemKey;
    private ItemKeyHelper<TGridItem> ItemKey => _itemKey ??= new(ItemKeyExpr);

    /// <summary>A model with no filter; the host sets <see cref="DbContextFactory"/>, <see cref="BaseQueryFn"/> and the key before the first read.</summary>
    public EFDataGridModel()
    {
        _filtersDefs = new();
    }

    private bool _disposed;
    private int _reloads;   // bumped by a reload, a Dispose and each read: a read in flight that sees it change was overtaken

    /// <summary>Releases the rows read, with the chunks spilled to disk; a subclass that holds state of its own overrides it and calls the base.</summary>
    public virtual void Dispose()
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Disposing: {thisType}", this.GetType());
        _disposed = true;
        if (_pocos is IDisposable disposable)
            disposable.Dispose();
        _pocos = null;
        ++_reloads;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public sealed override Func<TGridItem, object> GetItemKeyFn() => ItemKey.KeyFn;

    void IUpdatableModel<TGridItem>.Set(int index, TGridItem element)
    {
        this.Set(index, element);
    }

    void IUpdatableModel<TGridItem>.Insert(int index, TGridItem element)
    {
        this.Insert(index, element);
    }

    TGridItem IUpdatableModel<TGridItem>.Remove(int index)
    {
        return this.Remove(index);
    }

    private protected void Set(int index, TGridItem element)
    {
        _pocos![index] = element;
        ++_version;
        _unsortedInMemory = true;
    }

    private protected void Insert(int index, TGridItem element)
    {
        _pocos!.Insert(index, element);
        ++_version;
        _currentRow = index;
    }

    private protected TGridItem Remove(int index)
    {
        TGridItem res = _pocos![index];
        _pocos!.RemoveAt(index);
        ++_version;
        if (index < _currentRow || (index == _currentRow && index == _pocos.Count && _allDataRead))   // else the next row, still to read
            --_currentRow;
        return res;
    }

    /// <summary>Whether the model reads every row at its first query: <see cref="ReadAll"/>.</summary>
    public bool IsReadingAllData() => ReadAll;

    /// <summary>Whether every row of the query has been read, so <c>GetRowCount</c> is the real count.</summary>
    public bool AllDataRead() => _allDataRead && _pocos != null;

    /// <summary>
    /// Calling this method ensures that all rows are read from the ef storage.
    /// </summary>
    public async ValueTask ReadAllDataAsync(CancellationToken ct = default)
    {
        if (AllDataRead())
            return;
        await QueryDataAsync(int.MaxValue, ct);
    }

    /// <summary>Whether the rows read are held in memory only, not in an <see cref="OnDiskChunkedList{T}"/> that spills them to disk.</summary>
    public bool IsDataStoredInMem() => _pocos is not OnDiskChunkedList<TGridItem>;

    /// <inheritdoc/>
    public override bool RequiresFetch() => GetCurrentRowCountNoSync() < GetRowCountNoSync() || !IsDataStoredInMem();

    /// <summary>Whether the row must be fetched before it is read: it has not been read yet, or its chunk is on disk.</summary>
    public bool MustBeFetched(int rowIndex)
    {
        if (!RequiresFetch())
            return false;
        if (_pocos != null)
        {
            if (_allDataRead)
            {
                if (rowIndex >= _pocos.Count)
                    throw new ArgumentOutOfRangeException(nameof(rowIndex), $"rowIndex: {rowIndex} >= {_pocos.Count}");
                return _pocos is OnDiskChunkedList<TGridItem> cl && !cl.IsInMemory(rowIndex);
            }
            if (rowIndex != int.MaxValue && rowIndex < _pocos.Count)
            {
                return _pocos is OnDiskChunkedList<TGridItem> cl && !cl.IsInMemory(rowIndex);
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public override ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default) => FetchPocosAsync(rowIndex, count, ct);

    // By index into the rows read: a subclass that shows rows of its own maps its indexes in FetchRowsAsync, before calling here
    private int FetchPocos(int rowIndex, int count)
    {
        return AsyncHelper.RunSync(() => FetchPocosAsync(rowIndex, count, CancellationToken.None).AsTask());
    }

    private async ValueTask<int> FetchPocosAsync(int rowIndex, int count, CancellationToken ct)
    {
        if (rowIndex < 0 || count < 0 || rowIndex + count < 0)
            throw new ArgumentException($"Invalid parameters, rowIndex: {rowIndex} count: {count}");
        SyncImpl(ct);
        if (_pocos != null)
        {
            if (_allDataRead)
            {
                if (rowIndex < _pocos.Count && _pocos is OnDiskChunkedList<TGridItem> oncl)
                    oncl.EnsureIsInMemory(rowIndex);
                return Math.Max(0, Math.Min(count, _pocos.Count - rowIndex));
            }
            // already fetched
            // to respect the "contract" we always try to fetch the next row
            if ((rowIndex + count != int.MaxValue) && (rowIndex + count + 1) <= _pocos.Count)
            {
                if (rowIndex < _pocos.Count && _pocos is OnDiskChunkedList<TGridItem> oncl)
                {
                    oncl.EnsureIsInMemory(rowIndex);
                    if (rowIndex + 1 < _pocos.Count)
                        oncl.EnsureIsInMemory(rowIndex + 1);
                }
                return count;
            }
        }
        int toBeFetched = count;
        if (_pocos != null && rowIndex >= _pocos.Count)
            toBeFetched = rowIndex - _pocos.Count + count;
        await QueryDataAsync(toBeFetched == int.MaxValue ? toBeFetched : toBeFetched + 1, ct);
        if (rowIndex < _pocos.RequireNotNull().Count && _pocos is OnDiskChunkedList<TGridItem> cl)
            cl.EnsureIsInMemory(rowIndex);
        if ((rowIndex + count) > 0 && (rowIndex + count) <= _pocos.Count)
        {
            return count;
        }
        return Math.Max(0, _pocos.RequireNotNull().Count - rowIndex);
    }

    /// <returns>A snapshot list reflecting any sort order or filter applied, it is 
    ///   unmodifiable but can change if the backend collection changes.<br/>
    ///   Since the returned list may be backed by an OnDiskChunkedList do not use .ToList(), .ToArray(), etc on the result, you
    ///   won't be able to load all data in memory.
    /// </returns>
    public sealed override IReadOnlyList<TGridItem> GetDataView()
    {
        SyncImpl();
        if (_pocos == null)
            return ImmutableList<TGridItem>.Empty;
        return _pocos.AsReadOnly();
    }

    /// <inheritdoc/>
    public override void Sync()
    {
        SyncImpl();
    }

    /// <inheritdoc/>
    public override int GetDataVersion()
    {
        return _version;
    }

    /// <summary>
    /// Here you can change the IQueryable just before its execution and after Where filters were applied, for example to avoid the fetching of some
    /// heavy fields that have to be queryable, ie. included in a where condition.
    /// It is also called once after each <see cref="ForceReload"/>, on the base query with no filters, to read the order a reloaded
    /// row is compared with; that query is not run.
    /// </summary>
    protected virtual IQueryable<TGridItem> RefineQueryBeforeExec(TDbContext dbContext, bool singleItemReload, IQueryable<TGridItem> query)
    {
        return query;
    }

    /// <summary>The point in a read at which <see cref="RefineItemAsync"/> is called, as its documentation details.</summary>
    protected enum RefineItemPhase
    {
        /// <summary>Every row the query returned, before the filters applied in memory.</summary>
        BeforeFiltering,
        /// <summary>The rows those filters kept, before they are added to the list.</summary>
        AfterFiltering,
        /// <summary>One row re-read on its own.</summary>
        SingleItemReload,
    }

    /// <summary>
    /// Redefine it if you want, for example, to remove some data after loading so that you can keep the
    /// rows cheaper to hold: on a paged model what an item still carries at
    /// <see cref="RefineItemPhase.AfterFiltering"/> is what gets stored in the backing list, and therefore
    /// what is MessagePack-serialized when a chunk is swapped to disk. A row re-read on its own is stored as
    /// <see cref="RefineItemPhase.SingleItemReload"/> returns it.
    /// </summary>
    /// <param name="dbContext">The DbContext to use for any additional queries.</param>
    /// <param name="item">The item just read, or the replacement just re-read for a single row.</param>
    /// <param name="phase">
    /// Which point in the read is calling, since the cost and the purpose differ:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="RefineItemPhase.BeforeFiltering"/> — every row the query returned, before the in-memory
    /// filters look at it. Supply here whatever those filters need to read. Runs for rows that are about
    /// to be discarded too, so keep it cheap.
    /// </description></item>
    /// <item><description>
    /// <see cref="RefineItemPhase.AfterFiltering"/> — only the rows that survived, immediately before they
    /// are added to the list. The place for anything expensive, and for trimming what will be held.
    /// </description></item>
    /// <item><description>
    /// <see cref="RefineItemPhase.SingleItemReload"/> — one row being re-read on its own. It has passed the filters the database applies, or, on the editable model, is
    /// re-read without them to stay shown when they no longer admit it; those applied in memory may read what this returns,
    /// so supply what they need here too. What it returns is stored as it is: trim here what
    /// <see cref="RefineItemPhase.AfterFiltering"/> trims, but for what those filters read.
    /// </description></item>
    /// </list>
    /// </param>
    /// <param name="ct">Cancels the read this item belongs to; observe it in anything that awaits.</param>
    /// <returns>The item to use in place of <paramref name="item"/>.</returns>
    protected virtual async ValueTask<TGridItem> RefineItemAsync(TDbContext dbContext, TGridItem item, RefineItemPhase phase, CancellationToken ct)
    {
        return item;
    }

    // The getters call this with no token and must keep doing so: a getter loading data on a cold model is
    // the contract EFCoreTests pins. FetchRowsAsync and the two searches pass theirs through, so the first
    // page's walk can be cancelled from the same token as every later page.
    /// <summary>Reads the first page when nothing is read yet, or after a reload reads back to the rows read before it and finds the current row again.</summary>
    protected void SyncImpl(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);   // a query now would fill a spill list nobody disposes
        if (_memento == null)
        {
            if (_pocos == null)
                QueryData(MinRowsToRead, ct);
            return;
        }
        RestorePoint memento = _memento.Value;
        _memento = null;
        if (memento.Size < 0)
        {
            if (_pocos == null)
                QueryData(MinRowsToRead, ct);
            return;
        }
        // all data read means every row is here, though fewer than the memento recorded
        if (_pocos == null || (!_allDataRead && _pocos.Count < memento.Size))
        {
            QueryData(memento.Size, ct);
        }
        int currentRow = -1;
        // try to restore the pk as current row
        if (memento.CurrentPK != null)
        {
            Func<TGridItem, object> keyFn = GetItemKeyFn();
            bool found = false;
            int idx = 0;
            foreach (TGridItem item in _pocos!)
            {
                object otherPK = keyFn.Invoke(item);
                if (Object.Equals(memento.CurrentPK, otherPK))
                {
                    currentRow = idx;
                    found = true;
                    break;
                }
                idx++;
            }
            if (!found && AlwaysRestoreCurrentPK && !_allDataRead && KeyIsInQuery(memento.CurrentPK, ct))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Querying more data to reposition the PK: {pk}", memento.CurrentPK);
                bool sortsInMemory = SortsInMemory();
                while (!found && !_allDataRead)
                {
                    QueryData(MinRowsToRead, ct);
                    // an in-memory sort re-sorts the whole list on every fetch: the new rows can be anywhere
                    for (idx = sortsInMemory ? 0 : idx; !found && idx < _pocos.Count; idx++)
                    {
                        TGridItem item = _pocos[idx];
                        object otherPK = keyFn.Invoke(item);
                        if (Object.Equals(memento.CurrentPK, otherPK))
                        {
                            currentRow = idx;
                            found = true;
                        }
                    }
                }
            }
        }
        if (currentRow < 0)
            currentRow = memento.CurrentRow;
        if (currentRow < 0)
            return;
        currentRow = Math.Min(currentRow, _pocos!.Count - 1);
        _currentRow = currentRow;
    }

    /// <summary>
    /// What a read asks of the database, as <see cref="CompleteQueryable"/> receives it. An override passes it on to the base one
    /// as it is: the model finds the end of the data and the next offset from <c>RowIndex</c> and <c>RequestedCount</c>, and
    /// applies the same filters and sort in memory and to single-row reloads. Change the filters through
    /// <see cref="FilterQueryable"/>.
    /// </summary>
    protected sealed record ReadRequest
    {
        /// <summary>Every row, with no <c>Skip</c> or <c>Take</c>.</summary>
        public required bool ReadAll { get; init; }

        /// <summary>The first row of the page, its <c>Skip</c>; not used with <see cref="ReadAll"/>.</summary>
        public required int RowIndex { get; init; }

        /// <summary>How many rows the page asks for, its <c>Take</c>; not used with <see cref="ReadAll"/>.</summary>
        public required int RequestedCount { get; init; }

        /// <summary>The filters by column name; <see cref="FilterQueryable"/> adds those the database applies.</summary>
        public required IReadOnlyDictionary<string, AFilterDef> FilterDefs { get; init; }

        /// <summary>The sorted column, null for none; the database sorts it when it is an <c>EFProperty</c> column.</summary>
        public string? SortedColumn { get; init; }

        /// <summary>Whether <see cref="SortedColumn"/> is sorted ascending.</summary>
        public bool SortedAsc { get; init; } = true;
    }

    /// <summary>
    /// Builds the query a read runs from the base query: the key appended to its order so that paging by offset is stable, the
    /// filters <see cref="FilterQueryable"/> adds, the sort pushed down, <see cref="RefineQueryBeforeExec"/>, then the page's
    /// <c>Skip</c> and <c>Take</c> unless <see cref="ReadRequest.ReadAll"/>. An override calls the base one.
    /// </summary>
    protected virtual IQueryable<TGridItem> CompleteQueryable(TDbContext dbContext, IQueryable<TGridItem> query, ReadRequest request)
    {
        (bool readAll, int rowIndex, int requestedCount) = (request.ReadAll, request.RowIndex, request.RequestedCount);
        string? sortedColumn = request.SortedColumn;
        IColumnInfo<TGridItem>? sortInfo = sortedColumn == null ? null : GetColumnInfo(GetColumnIndex(sortedColumn));
        if (!readAll && sortInfo?.QueryPushDownEnabled != true)
        {
            // Paging by offset needs a total order; done before the filters, whose Where would hide the host's OrderBy from ThenBy
            if (typeof(IOrderedQueryable<TGridItem>).IsAssignableFrom(query.Expression.Type))
                query = ItemKey.ThenBy((IOrderedQueryable<TGridItem>)query, true);
            else if (!ItemKeyHelper<TGridItem>.HasOrdering(query.Expression))
                query = ItemKey.OrderBy(query);
            else if (ItemKey.WithTheKeyAfterTheLastOrdering(query) is { } completed)
                query = completed;
            // ordered before a projection from another type: left as it is
        }
        query = FilterQueryable(dbContext, query, request.FilterDefs);
        if (sortInfo?.QueryPushDownEnabled == true)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Sorting data on db: {colName}", sortedColumn);
            query = request.SortedAsc ? ItemKey.ThenBy(query.OrderBy(sortInfo.PropertyExpression!), true)
                : ItemKey.ThenBy(query.OrderByDescending(sortInfo.PropertyExpression!), false);
        }
        query = RefineQueryBeforeExec(dbContext, false, query);
        if (!readAll)
        {
            int count = Math.Min(requestedCount, int.MaxValue - rowIndex);
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Requested: {requestedCount} really fetching: {count}", requestedCount, count);
            query = query.Skip(rowIndex).Take(count);
        }
        return query;
    }

    /// <summary>
    /// Adds the filters the database applies. The paged query and a single-row reload both go through it, so an override that
    /// changes which rows a filter admits holds for both.
    /// </summary>
    protected virtual IQueryable<TGridItem> FilterQueryable(TDbContext dbContext, IQueryable<TGridItem> query,
        IReadOnlyDictionary<string, AFilterDef> filterDefs)
    {
        if (PushedDownFilter(filterDefs) is { } combined)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Adding where condition: {combined}", combined);   // not the query: a logger that enumerates its arguments would run it
            query = query.Where(combined);
        }
        return query;
    }

    // The filters the database applies, null if none; the others are applied in memory
    private Expression<Func<TGridItem, bool>>? PushedDownFilter(IReadOnlyDictionary<string, AFilterDef> filterDefs)
    {
        List<Expression<Func<TGridItem, bool>>> efPreds = new(filterDefs.Count);
        foreach (var en in filterDefs)
        {
            string colName = en.Key;
            AFilterDef fdef = en.Value;
            if (!fdef.CanCreatePredicate())
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Skipping column (due to filter-definition): {colName}", colName);
                continue;
            }
            int colIdx = GetColumnIndex(colName);
            if (colIdx < 0)
                throw new ArgumentException($"Column {colName} not found");
            var cinfo = GetColumnInfo(colIdx);
            if (!cinfo.QueryPushDownEnabled)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Skipping column: {colName}", colName);
                continue;
            }
            var predicate = fdef.CreatePredicate<TGridItem>(cinfo.PropertyExpression.RequireNotNull());
            efPreds.Add(predicate);
        }
        return efPreds.Count > 0 ? ExprUtils.CombineExpressions<TGridItem, bool>(efPreds, (e1, e2) => Expression.AndAlso(e1, e2)) : null;
    }

    /// <summary>
    /// Whether the key can be in the filtered query at all: false spares the walk to the end of the table that a key the filter
    /// excludes would cause. The database answers for the filters it applies; a key it admits may still be excluded by one
    /// applied in memory, and then the walk runs.
    /// </summary>
    private bool KeyIsInQuery(object pk, CancellationToken ct) => AsyncHelper.RunSync(() => KeyIsInQueryAsync(pk, ct));

    private async Task<bool> KeyIsInQueryAsync(object pk, CancellationToken ct)
    {
        await using TDbContext dbContext = await DbContextFactory.CreateDbContextAsync(ct);
        IQueryable<TGridItem> query = CompleteQueryable(dbContext, BaseQueryFn.Invoke(dbContext, null),
            new ReadRequest { ReadAll = true, RowIndex = 0, RequestedCount = 0, FilterDefs = _filtersDefs.Definitions });
        bool res = await query.Where(CreateKeyPredicate(ItemKeyExpr, pk)).AnyAsync(ct);
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Executed KeyIsInQueryAsync for key: {pk} result: {res}", pk, res);
        return res;
    }

    /// <summary>Blocks on <see cref="QueryDataAsync"/>.</summary>
    protected void QueryData(int requestedCount, CancellationToken ct = default)
    {
        AsyncHelper.RunSync(() => QueryDataAsync(requestedCount, ct).AsTask());
    }

    /// <summary>Reads at least <paramref name="requestedCount"/> rows, never fewer than <see cref="MinRowsToRead"/>: the first ones on a new query, else those after the rows read.</summary>
    protected async ValueTask QueryDataAsync(int requestedCount, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (AllDataRead())
            return;
        ++_version;
        int count = Math.Max(requestedCount, MinRowsToRead);
        if (_pocos == null || ReadAll)
        {
            _filtersDefs.Sync();   // on a fresh query only: the pages appended later are counted under the same filters
            _notInMemFilteredPocosCount = 0;
            _pocos?.Clear();   // a ReadAll query reads every row again
        }
        object? currentKey = CurrentKeyUnderASortInMemory();
        _unsortedInMemory = true;   // until the sort at the end: a read that fails keeps the rows it added, unsorted
        int rowIndex = _notInMemFilteredPocosCount;
        int fetchCount = 1;
        int added = 0;
        int reloads = ++_reloads;   // a read appends from the offset it started at: one that overtakes it ends it
        await using TDbContext dbContext = await DbContextFactory.CreateDbContextAsync(ct);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfOvertaken();
            IQueryable<TGridItem> query = CompleteQueryable(dbContext, BaseQueryFn.Invoke(dbContext, null), new ReadRequest
            {
                ReadAll = ReadAll, RowIndex = rowIndex, RequestedCount = count, FilterDefs = _filtersDefs.Definitions,
                SortedColumn = _sortedColumn, SortedAsc = _sortedAsc,
            });
            // The size is a capacity hint only
            if (_pocos == null)
            {
                ISortableList<TGridItem> pocos = (await CreateItemsListAsync(Math.Min(count, MinRowsToRead))).RequireNotNull();
                if (reloads != _reloads)
                    (pocos as IDisposable)?.Dispose();
                ThrowIfOvertaken();
                _pocos = pocos;
            }
            InMemoryFilter<TGridItem> memFilter = new(this, true, _filtersDefs.Definitions);
            int read = 0;
            int filtered = 0;
            foreach (var item in await query.ToListAsync(ct))  // No .AsAsyncEnumerable() otherwise we may get an exception in RefineItemAsync()
            {
                ct.ThrowIfCancellationRequested();
                ++read;
                var nitem1 = await RefineItemAsync(dbContext, item, RefineItemPhase.BeforeFiltering, ct);
                ThrowIfOvertaken();
                if (memFilter.Includes(nitem1) || ShownDespiteTheFilter(nitem1))
                {
                    ++filtered;
                    var nitem2 = await RefineItemAsync(dbContext, nitem1, RefineItemPhase.AfterFiltering, ct);
                    ThrowIfOvertaken();
                    _pocos.Add(nitem2);
                }
                _notInMemFilteredPocosCount++;
            }
            ThrowIfOvertaken();
            _allDataRead = ReadAll || read < count;
            if (_allDataRead && _currentRow >= _pocos.Count)
                _currentRow = _pocos.Count - 1;
            rowIndex += read;
            added += filtered;
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Fetched: {read} filtered: {filtered} total: {total}", read, filtered, _pocos.Count);
            // Exit on the rows added over all the passes, not on this pass alone: an in-memory filter that drops a few rows
            // per window would otherwise never let a single pass reach requestedCount and the loop would read on and on.
            if (_allDataRead || added >= requestedCount)
            {
                break;
            }
            fetchCount++;
            if (fetchCount >= 100 && fetchCount >= requestedCount)
                logger.LogError("Too many fetches, filter data on the db side, {fetchCount}", fetchCount);
            else if (fetchCount >= 50)  // heuristic
                logger.LogWarning("Too many fetches, filter data on the db side, {fetchCount}", fetchCount);
            else if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Fetching again {fetchCount}", fetchCount);
        }
        SortInMemoryFollowing(currentKey);

        // A ForceReload, a Dispose or another read during an await: the list, the offset and the current row now belong to another read
        void ThrowIfOvertaken()
        {
            if (reloads != _reloads)
                throw new OperationCanceledException("The model was reloaded or read on while this read was in flight");
        }
    }

    // An in-memory sort moves the rows read so far: the current row, if one of them, follows its key
    private object? CurrentKeyUnderASortInMemory() =>
        SortsInMemory() && _currentRow >= 0 && _currentRow < (_pocos?.Count ?? 0) ? ItemKey.KeyFn(_pocos![_currentRow]) : null;

    private void SortInMemoryFollowing(object? currentKey)
    {
        ISortableList<TGridItem> pocos = _pocos!;
        SortInMemory(pocos);
        if (currentKey != null && Equals(ItemKey.KeyFn(pocos[_currentRow]), currentKey))
            return;   // it did not move
        for (int i = 0; currentKey != null && i < pocos.Count; i++)
        {
            if (Equals(ItemKey.KeyFn(pocos[i]), currentKey))
            {
                _currentRow = i;
                break;
            }
        }
    }

    private bool SortsInMemory() =>
        _sortedColumn != null && !GetColumnInfo(GetColumnIndex(_sortedColumn)).QueryPushDownEnabled;

    /// <summary>
    /// Sorts the specified list of items in memory according to the current sort column and direction, if query
    /// pushdown is not enabled for the column.
    /// </summary>
    /// <remarks>Sorting is performed only if a sort column is specified and query pushdown is disabled for
    /// that column. The sort order and column are determined by the current grid state. The operation modifies the
    /// input list directly.</remarks>
    /// <param name="pocos">The list of items to be sorted in place. Must not be null.</param>
    protected void SortInMemory(ISortableList<TGridItem> pocos)
    {
        _unsortedInMemory = false;
        if (_sortedColumn != null)
        {
            int colIdx = GetColumnIndex(_sortedColumn.RequireNotNull());
            IColumnInfo<TGridItem> cinfo = GetColumnInfo(colIdx);
            if (!cinfo.QueryPushDownEnabled)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Sorting data in memory: {colName}", _sortedColumn);
                var colComparer = new ColumnComparer<TGridItem>(this, GetComparerForColumn(colIdx), _sortedAsc, colIdx);
                IComparer<TGridItem> comparer =
                    Comparers.Combo(colComparer, Comparer<TGridItem>.Create((gi1, gi2) =>
                    {
                        int res = ItemKey.Compare(gi1, gi2);
                        return _sortedAsc ? res : -Math.Sign(res);   // -int.MinValue is int.MinValue
                    }));
                pocos.Sort(comparer);
            }
        }
    }

    /// <summary>The list the rows read are kept in: <see cref="CreateItemsListFn"/>'s, else a <see cref="SortableList{T}"/> under <see cref="ReadAll"/> and an <see cref="OnDiskChunkedList{T}"/> otherwise.</summary>
    protected async ValueTask<ISortableList<TGridItem>> CreateItemsListAsync(int size)
    {
        if (CreateItemsListFn != null)
            return await CreateItemsListFn.Invoke(size);
        ISortableList<TGridItem> res;
        if (ReadAll)
        {
            res = new SortableList<TGridItem>(size);
        }
        else
        {
            int chunksInMemory = Math.Max(OnDiskChunkedList.MinChunksInMemory, 32);   // the budget data-models.md states
            int chunkSize = Math.Max(OnDiskChunkedList.MinChunkSize, MinRowsToRead);
            logger.LogDebug("Using OnDiskChunkedList chunksInMemory: {chunksInMemory} ; chunkSize: {chunkSize}", chunksInMemory, chunkSize);
            res = new OnDiskChunkedList<TGridItem>(chunksInMemory, chunkSize, SpillDirectory);
        }
        return res;
    }

    /// <summary>
    /// Used for in memory local sorting.
    /// </summary>
    protected virtual IComparer<object>? GetComparerForColumn(int columnIdx)
    {
        return PocoColumnComparer.GetComparerForColumn(GetColumn(columnIdx));
    }

    // Also for a column sorted in the database: the search on the sorted column compares in memory
    /// <inheritdoc/>
    protected override void ValidateStoredColumn(int columnIndex)
    {
        if (GetColumn(columnIndex).IsSortable())
            PocoColumnComparer.CheckCanSort(GetColumnName(columnIndex), GetColumn(columnIndex).GetValueType(), GetComparerForColumn(columnIndex));
    }

    /// <inheritdoc/>
    public override void SetCurrentRow(int rowIndex)
    {
        SyncImpl();
        if (rowIndex == _currentRow)
            return;
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        int rowCount = GetRowCount();
        if (rowIndex < -1 || rowIndex >= rowCount)
            throw new ArgumentOutOfRangeException(nameof(rowIndex), $"Row index {rowIndex} out of range: {rowCount}");
        _currentRow = rowIndex;
        FireModelChanged(GridModelChangeType.Other);
    }

    /// <inheritdoc/>
    public override int GetCurrentRow()
    {
        SyncImpl();
        return _currentRow;
    }

    /// <inheritdoc/>
    public override TGridItem? GetCurrentItem()
    {
        int idx = GetCurrentRow();
        if (idx < 0)
            return default;
        if (idx >= GetCurrentRowCount() && idx < GetRowCount())
        {
            // Past the loaded rows: a paged model whose total is still unknown accepts any current row. Fetching up to it
            // either loads it or finds the end, and finding the end clamps the current row, so re-read it.
            FetchPocos(idx, 1);
            idx = GetCurrentRow();
            if (idx < 0 || idx >= GetCurrentRowCount())
                return default;
        }
        return GetItem(idx);
    }

    /// <inheritdoc/>
    public override TGridItem GetItem(int rowIndex)
    {
        SyncImpl();
        if (MustBeFetched(rowIndex))
        {
            if (FetchPocos(rowIndex, 1) < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rowIndex), $"rowIndex: {rowIndex} size: {_pocos?.Count}");
            }
        }
        return _pocos![rowIndex];
    }

    /// <inheritdoc/>
    public override sealed int GetCurrentRowCount()
    {
        SyncImpl();
        return GetCurrentRowCountNoSync();
    }

    /// <summary>The rows read so far, without the sync <see cref="GetCurrentRowCount"/> runs first; a subclass counts here the rows it adds.</summary>
    protected virtual int GetCurrentRowCountNoSync()
    {
        return _pocos?.Count ?? 0;
    }

    /// <inheritdoc/>
    public override sealed int GetRowCount()
    {
        SyncImpl();
        return GetRowCountNoSync();
    }

    /// <summary>The row count once every row is read, <see cref="int.MaxValue"/> before, without the sync <see cref="GetRowCount"/> runs first.</summary>
    protected virtual int GetRowCountNoSync()
    {
        return _pocos == null || !_allDataRead ? int.MaxValue : _pocos.Count;
    }

    /// <inheritdoc/>
    public bool IsFilterable()
    {
        return true;
    }

    /// <inheritdoc/>
    public void ApplyFilters(IEnumerable<KeyValuePair<string, AFilterDef>> filters)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        var checkedFilters = CheckFilterColumns(filters);
        _memento ??= CreateRestorePoint();   // under the current filters
        _filtersDefs.Clear();
        foreach (var entry in checkedFilters)
        {
            _filtersDefs.Add(entry.Key, entry.Value.Clone());
        }
        ForceReload(true);
    }

    /// <summary>
    /// Like <see cref="DataGridModel{TGridItem}.RemoveColumn"/>: a sort or a filter on the removed column would otherwise
    /// be composed into the next query and fail there.
    /// </summary>
    public override IColumn<TGridItem>? RemoveColumn(string columnName)
    {
        bool changed = columnName == _sortedColumn || _filtersDefs.Definitions.ContainsKey(columnName);
        if (changed && IsCurrentRowFrozen())
            // before anything changes: ForceReload would refuse it anyway
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (changed)
            _memento ??= CreateRestorePoint();   // while the column can still be queried
        IColumn<TGridItem>? res = base.RemoveColumn(columnName);
        if (res == null)
            return null;
        if (columnName == _sortedColumn)
            _sortedColumn = null;
        _filtersDefs.Remove(columnName);
        if (changed)
            ForceReload(true);
        return res;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, AFilterDef> GetFilters()
    {
        return _filtersDefs.Definitions;
    }

    /// <inheritdoc/>
    public bool IsFiltered()
    {
        return _filtersDefs.IsFiltered();
    }

    /// <inheritdoc/>
    public bool IsSortable()
    {
        return true;
    }

    /// <inheritdoc/>
    public string? GetSortedColumn()
    {
        return _sortedColumn;
    }

    /// <inheritdoc/>
    public SortType GetSortType()
    {
        return _sortedColumn == null ? SortType.None : _sortedAsc ? SortType.Asc : SortType.Desc;
    }

    /// <inheritdoc/>
    public void SetSortedColumn(string? columnName, bool ascending)
    {
        if (_sortedColumn == columnName && (columnName == null || _sortedAsc == ascending))   // no sort has no direction
            return;
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (columnName != null && GetColumnIndex(columnName) < 0)
            throw new ArgumentException($"Column not found: {columnName}");
        if (columnName != null)   // a column the user cannot sort may still be sorted in code
            PocoColumnComparer.CheckCanSort(columnName, GetColumn(GetColumnIndex(columnName)).GetValueType(), GetComparerForColumn(GetColumnIndex(columnName)));
        _memento ??= CreateRestorePoint();   // under the current order: it may have to fetch the current row
        _sortedColumn = columnName;
        _sortedAsc = ascending;
        if (!ReadAll && SortsInMemory())
            logger.LogWarning("Sorting {colName} in memory: only the rows read so far are sorted, and they are sorted again as more are read. Give the column an EFProperty expression, or set ReadAll", columnName);
        ForceReload(true);
    }

    /// <inheritdoc/>
    public async ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (_sortedColumn == null)
            throw new InvalidOperationException($"No sorted column");
        SyncImpl(ct);
        if (_unsortedInMemory && SortsInMemory())
        {
            SortInMemoryFollowing(CurrentKeyUnderASortInMemory());
            ++_version;
        }
        int colIdx = GetColumnIndex(_sortedColumn);
        var comparator = GetComparerForColumn(colIdx);
        double multiplier = 1.0;
        while (true)
        {
            var data = GetDataView();
            int res = PocoColumnComparer.BinarySearch(data, this, colIdx, value, _sortedAsc, comparator);
            if (res >= 0 || ~res < data.Count || _allDataRead)   // ~res is the insertion point: before a loaded row means final
                return res;
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Fetching rows because of incomplete search {size}. ", data.Count);
            await FetchPocosAsync(data.Count, (int)(MinRowsToRead * multiplier), ct);
            if (multiplier < 4)
                multiplier += 0.5;  // If still not found we progressively load more data
        }
    }

    /// <inheritdoc/>
    public bool CanSearchOnSortedColumn()
    {
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask<int> ContainsSearchAsync(string columnName, string value, bool forward, int startFrom, CancellationToken ct = default)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        int colIdx = GetColumnIndex(columnName);
        if (colIdx < 0)
            throw new ArgumentException($"Column not found: {columnName}");
        SyncImpl(ct);
        if (!forward && !_allDataRead && startFrom >= GetDataView().Count)   // a backward search from a row not read reads up to its start
            await FetchPocosAsync(GetDataView().Count, Math.Min(startFrom, int.MaxValue - 1) + 1 - GetDataView().Count, ct);
        double multiplier = 1.0;
        while (true)
        {
            var data = GetDataView();
            int res = PocoColumnComparer.ContainsSearch(data, this, colIdx, value, forward, startFrom);
            // Rows are loaded from the beginning, so everything before startFrom is already here: a backward miss is final.
            if (res >= 0 || _allDataRead || !forward)
                return res;
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Fetching rows because of incomplete search {size}. ", data.Count);
            if (!SortsInMemory())
                startFrom = Math.Max(startFrom, data.Count);   // else the rows a fetch adds can land before those searched: scan again from the start
            await FetchPocosAsync(data.Count, (int)(MinRowsToRead * multiplier), ct);
            if (multiplier < 4)
                multiplier += 0.5;  // If still not found we progressively load more data
        }
    }

    /// <inheritdoc/>
    public bool CanDoContainsSearch()
    {
        return true;
    }

    /// <inheritdoc/>
    public virtual void ForceReload(bool restoreCurrentRow)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (restoreCurrentRow)
        {
            if (_memento == null)
                _memento = CreateRestorePoint();
        }
        else
            _memento = null;
        if (_pocos is IDisposable disposable)
            disposable.Dispose();
        _pocos = null;
        ++_reloads;   // after CreateRestorePoint, whose own fetch must not be cut off
        _allDataRead = false;
        _currentRow = -1;
        _baseQueryOrderRead = false;
    }

    /// <inheritdoc/>
    public virtual ValueTask<bool> ReloadItemAsync(int rowIndex, CancellationToken ct = default) =>
        ReloadItemAsync(rowIndex, GetItemKeyFn().Invoke(GetItem(rowIndex)), ct);

    /// <summary>
    /// Reloads the row stored under <paramref name="pk"/> into <paramref name="rowIndex"/>.
    /// <see cref="ReloadItemAsync(int, CancellationToken)"/> passes the row's own key; a cancelled edit of key members passes
    /// the key the row had when the edit started. Override this one to hook every reload of a single row.<br/>
    /// A row the filter the database applies no longer admits leaves the list, as a deleted one does. A row whose value in the
    /// order the database pages by changed is put in its place by a new query, <see cref="ForceReload"/> restoring the current
    /// row; if a filter applied in memory now excludes it, it leaves the list first, and the current row goes to the next one.
    /// </summary>
    /// <exception cref="OperationCanceledException">A sort, a filter or a reload while the row was read again: the list it was
    /// read for is gone, and the new one reads the row anew.</exception>
    protected internal virtual async ValueTask<bool> ReloadItemAsync(int rowIndex, object pk, CancellationToken ct = default)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        TGridItem item = GetItem(rowIndex);
        int reloads = ReloadsSoFar();
        object?[]? orderBefore = await PagingOrderValuesAsync(item, ct);
        TGridItem? nitem = await QueryItemAsync(item, pk, throughTheFilter: true, ct);
        ThrowIfReloadOvertaken(reloads);
        if (nitem == null)
        {
            Remove(rowIndex);
            LeftTheRowsRead();
            return false;
        }
        if (MovedInTheOrder(orderBefore, await PagingOrderValuesAsync(nitem, ct)))
        {
            ThrowIfReloadOvertaken(reloads);
            // The new query drops a row a filter applied in memory excludes, and the restore would read on looking for it
            bool dropped = !PassesTheMemoryFilters(nitem);
            if (dropped)
            {
                bool lastReadCurrent = rowIndex == _currentRow && rowIndex == _pocos!.Count - 1 && !_allDataRead;
                Remove(rowIndex);
                if (lastReadCurrent)   // the next row is not read yet, and a key restore would land on the one before
                    _memento = new RestorePoint(rowIndex + 1, rowIndex, null);
            }
            ForceReload(true);
            return !dropped;
        }
        ThrowIfReloadOvertaken(reloads);
        Set(rowIndex, nitem);
        return true;
    }

    // A ForceReload, a Dispose or a read while a single-row reload awaited: its row index belongs to another list
    private protected int ReloadsSoFar() => _reloads;

    private protected void ThrowIfReloadOvertaken(int reloads)
    {
        if (reloads != _reloads)
            throw new OperationCanceledException("The model was reloaded or read on while this row was read again");
    }

    /// <summary>
    /// The row stored under <paramref name="pk"/>, through the filters the database applies or without them.
    /// </summary>
    /// <returns>Null when the query returns no row.</returns>
    private protected async ValueTask<TGridItem?> QueryItemAsync(TGridItem item, object pk, bool throughTheFilter,
        CancellationToken ct = default)
    {
        await using TDbContext dbContext = await DbContextFactory.CreateDbContextAsync(ct);
        IQueryable<TGridItem> query = BaseQueryFn.Invoke(dbContext, item);
        query = query.Where(CreateKeyPredicate(ItemKeyExpr, pk));
        if (throughTheFilter)
            query = FilterQueryable(dbContext, query, _filtersDefs.Definitions);
        query = RefineQueryBeforeExec(dbContext, true, query);
        TGridItem? nitem = await query.SingleOrDefaultAsync(ct);
        if (nitem == null)
            return default;
        return await RefineItemAsync(dbContext, nitem, RefineItemPhase.SingleItemReload, ct);
    }

    private protected bool FiltersInTheDatabase() => PushedDownFilter(_filtersDefs.Definitions) != null;

    private protected bool PassesTheMemoryFilters(TGridItem item) => new InMemoryFilter<TGridItem>(this, true, _filtersDefs.Definitions).Includes(item);

    // A row read, deleted or taken out of the filter the database applies: the rows after it moved up one, and so must the paging offset
    private protected void LeftTheRowsRead() => _notInMemFilteredPocosCount--;

    // Asked for each row read that a filter applied in memory excludes
    private protected virtual bool ShownDespiteTheFilter(TGridItem item) => false;

    /// <summary>
    /// The values the database pages by: the pushed-down sort, or else the base query's own order, then the key.
    /// </summary>
    /// <returns>null when the base query's order reads more than the row's own members, so that any change may move the row.</returns>
    private protected async ValueTask<object?[]?> PagingOrderValuesAsync(TGridItem item, CancellationToken ct = default)
    {
        List<object?> values = new(3);
        IColumnInfo<TGridItem>? sortInfo = _sortedColumn == null ? null : GetColumnInfo(GetColumnIndex(_sortedColumn));
        if (sortInfo?.QueryPushDownEnabled == true)
            values.Add(sortInfo.Property.GetterFunc(item));
        else
        {
            if (await BaseQueryOrderAsync(ct) is not { } baseOrder)
                return null;
            foreach (Func<TGridItem, object?> getter in baseOrder)
                values.Add(getter(item));
        }
        values.Add(GetItemKeyFn().Invoke(item));
        return values.ToArray();
    }

    private protected static bool MovedInTheOrder(object?[]? before, object?[]? after) =>
        before == null || after == null || !before.SequenceEqual(after);

    // Read once until the next ForceReload, which is where a new base query takes effect; the context only builds the expression
    private async ValueTask<IReadOnlyList<Func<TGridItem, object?>>?> BaseQueryOrderAsync(CancellationToken ct)
    {
        if (!_baseQueryOrderRead)
        {
            await using TDbContext dbContext = await DbContextFactory.CreateDbContextAsync(ct);
            _baseQueryOrder = QueryOrder.RowMembers(RefineQueryBeforeExec(dbContext, false, BaseQueryFn.Invoke(dbContext, null)).Expression, typeof(TGridItem))
                ?.Select(QueryOrder.Getter<TGridItem>).ToList();
            _baseQueryOrderRead = true;
        }
        return _baseQueryOrder;
    }

    private Expression<Func<TGridItem, bool>> CreateKeyPredicate(Expression<Func<TGridItem, object>> prop, object value)
    {
        Expression<Func<TGridItem, bool>> res = ItemKeyHelper<TGridItem>.Predicate(prop, value);
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Expression<Func<{TGridItem}, bool>> = {expr}", typeof(TGridItem), res);
        return res;
    }

    void IUpdatableModel<TGridItem>.FireModelChanged(GridModelChangeType changeType)
    {
        FireModelChanged(changeType);
    }

    /// <summary>Records how many rows are read and the current row with its key, reading up to the current row first when it lies beyond them.</summary>
    protected internal RestorePoint CreateRestorePoint()
    {
        if (_pocos != null && _currentRow >= _pocos.Count && !_allDataRead)
            FetchPocos(_currentRow, 1);   // the current row may lie past the loaded window; its key is what finds it again
        int size = _pocos == null ? -1 : _pocos.Count;
        object? currentPK = _currentRow >= 0 && _currentRow < size ? GetItemKeyFn().Invoke(_pocos![_currentRow]) : null;
        return new RestorePoint(size, _currentRow, currentPK);
    }

    /// <summary>What a reload reads back to and restores.</summary>
    /// <param name="Size">The rows read; -1 when none were.</param>
    /// <param name="CurrentRow">The index of the current row.</param>
    /// <param name="CurrentPK">The key of the current row; null when there was none.</param>
    protected internal readonly record struct RestorePoint(int Size, int CurrentRow, object? CurrentPK)
    {
    }
}
