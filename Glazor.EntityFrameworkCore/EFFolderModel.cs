using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Text.Json;

namespace Glazor.EntityFrameworkCore;

/// <summary>
/// A tree for <see cref="Treegliaria{TGridItem}"/> over an EF Core store, where folders hold folders and leaves as in a file
/// system. The roots come from <see cref="RootsQueryFn"/>, read in passes of <see cref="MinRowsToRead"/> as the grid pages; a
/// folder's children come from <see cref="ChildrenQueryFn"/>, read the first time the folder is expanded, a page of
/// <see cref="ChildrenPageSize"/> at a time, and kept until <see cref="ForceReload"/>. Beside the rows read, the host may put
/// virtual folders of its own, <see cref="VirtualChildrenFn"/>. A row may sit under several parents: each place it has is
/// expanded on its own, and its children are read once for all.
/// </summary>
/// <remarks>
/// Filters apply in memory, to the rows read: a folder stays shown, collapsed or not, while a row read below it matches.
/// Siblings are sorted in memory; a level read a page at a time, the roots or a folder's paged children, is sorted by the
/// database when the sorted column has an <c>EFProperty</c>, and read whole to be sorted in memory otherwise. After every change
/// the current row is put back by its path, the levels on it read as far as they must, or on its nearest ancestor still shown.
/// The search on the sorted column is not supported, since a tree's rows are not in one order.
/// </remarks>
/// <typeparam name="TDbContext">The EF Core context type the model queries through <see cref="DbContextFactory"/>.</typeparam>
/// <typeparam name="TGridItem">The type of the rows, folders and leaves alike.</typeparam>
public class EFFolderModel<TDbContext, TGridItem> : AGridModel<TGridItem>, IFolderGridModel<TGridItem>, ITreeStateGridModel<TGridItem>, IReloadableGridModel<TGridItem>,
        IAsyncGridModel<TGridItem>, IDisposable
    where TDbContext : DbContext where TGridItem : class
{
    private static readonly ILogger<EFFolderModel<TDbContext, TGridItem>> logger = LoggerSupport.Logger<EFFolderModel<TDbContext, TGridItem>>();

    /// <summary>Gives the context the queries of each read share, disposed when the read ends.</summary>
    public required IDbContextFactory<TDbContext> DbContextFactory { get; init; }

    /// <summary>
    /// The roots. Order it for the order they are shown in when no column is sorted; the model adds the key after the last
    /// <c>OrderBy</c> or <c>ThenBy</c>, as <see cref="EFDataGridModel{TDbContext, TGridItem}"/> does, so that the passes page over a
    /// total order.
    /// </summary>
    public required Func<TDbContext, IQueryable<TGridItem>> RootsQueryFn { get; init; }

    /// <summary>
    /// The children of a folder, a virtual one included. Order it for the order they are shown in when no column is sorted; the
    /// model adds the key after the last <c>OrderBy</c> or <c>ThenBy</c>, so that the pages read follow a total order.
    /// </summary>
    public required Func<TDbContext, TGridItem, IQueryable<TGridItem>> ChildrenQueryFn { get; init; }

    /// <summary>
    /// The parent of a row, null for a root: a row read with the context given, or a virtual folder of the host's own. Only
    /// <see cref="GoToAsync"/> needs it.
    /// </summary>
    public Func<TDbContext, TGridItem, CancellationToken, ValueTask<TGridItem?>>? ParentFn { get; init; }

    /// <summary>Whether a row may have children: a leaf shows no expander, and its children are never asked for.</summary>
    public required Func<TGridItem, bool> IsFolderFn { get; init; }

    /// <summary>
    /// The key of a row, unique in the whole tree, as an expression so that it can take part in queries: a column, or several as
    /// an anonymous type, a record, a class whose constructor takes its properties, or a tuple, as
    /// <see cref="EFDataGridModel{TDbContext, TGridItem}.ItemKeyExpr"/>.
    /// </summary>
    public required Expression<Func<TGridItem, object>> ItemKeyExpr { get; init; }

    /// <summary>
    /// Rows of the host's own below a parent, null for the roots, shown before the rows read there: folders that group others,
    /// an "Unfiled" one say, whose children come from <see cref="ChildrenQueryFn"/> as any folder's do. Their keys are in the
    /// same key space as the rows read. A filter keeps one only while a row read below it matches, since it has no values of its
    /// own to match. Asked once per parent, until <see cref="ForceReload"/>.
    /// </summary>
    public Func<TGridItem?, IEnumerable<TGridItem>>? VirtualChildrenFn { get; init; }

    private int _minRowsToRead = 512;
    /// <summary>The roots read in one pass: 512 by default, and never fewer than 10.</summary>
    public int MinRowsToRead
    {
        get => _minRowsToRead;
        init => _minRowsToRead = Math.Max(10, value);
    }

    private int _childrenPageSize = int.MaxValue;
    /// <summary>
    /// The children of a folder read at a time, all of them by default. Below a page a folder shows the row
    /// <see cref="MoreRowFn"/> makes, which reads the next page when expanded.
    /// </summary>
    public int ChildrenPageSize
    {
        get => _childrenPageSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _childrenPageSize = value;
        }
    }

    /// <summary>
    /// The row shown below the children of a folder not all read, a "More…" one say, with a key of its own; required with a
    /// <see cref="ChildrenPageSize"/>. It gets the folder.
    /// </summary>
    public Func<TGridItem, TGridItem>? MoreRowFn { get; init; }

    private ItemKeyHelper<TGridItem>? _itemKey;
    private ItemKeyHelper<TGridItem> ItemKey => _itemKey ??= new(ItemKeyExpr);
    private bool? _canSaveTreeState;

    private Level? _roots;
    private readonly Dictionary<object, Level> _children = new();
    private readonly HashSet<object> _virtual = new();
    private readonly HashSet<PathKey> _expanded = new();

    private List<Node>? _rows;   // null until the next build
    private List<TGridItem> _items = new();
    private int _rootsShown;   // a pass appends the rows of the roots it reads
    private InMemoryFilter<TGridItem>? _filter;
    private int _currentRow = -1;
    private IReadOnlyList<object>? _pathToRestore;
    private PathKey?[]? _buildPath;   // while building, its places from the root, so that the levels on it are read down to it
    private (PathKey Folder, HashSet<PathKey> Page)? _moreRead;   // while building, the page a "More" row read: its first row shown goes current
    // What an operation asks to read, done by the next build; each may give the path the current row goes to instead
    private readonly List<Func<Reader, CancellationToken, ValueTask<IReadOnlyList<object>?>>> _pendingReads = [];
    private PathKey? _zoom;   // the folder shown as the root, with nothing above or beside it
    private PathKey[]? _zoomPath;   // while building, its places from the root
    private TGridItem[] _zoomItems = [];   // as the last build found them, from the root
    private int _version;

    private string? _sortedColumn;
    private bool _sortedAsc = true;
    private readonly FiltersDefs _filtersDefs = new();

    private bool _disposed;

    /// <summary>Releases the rows read; a subclass that holds state of its own overrides it and calls the base.</summary>
    public virtual void Dispose()
    {
        _disposed = true;
        _rows = null;
        _roots = null;
        _children.Clear();
        _pendingReads.Clear();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public sealed override Func<TGridItem, object> GetItemKeyFn() => ItemKey.KeyFn;

    private object Key(TGridItem item) => ItemKey.KeyFn(item);

    // ---- the rows

    private void SyncImpl()
    {
        if (_rows == null)
            AsyncHelper.RunSync(() => BuildAsync(CancellationToken.None).AsTask());
    }

    /// <inheritdoc/>
    public override void Sync() => SyncImpl();

    /// <inheritdoc/>
    public override bool RequiresFetch() => _rows == null || !RootsDone;

    // Zoomed, the roots are no longer paged: only the one on the way to the folder zoomed on is shown
    private bool RootsDone => _zoom != null || _roots!.AllRead;

    /// <inheritdoc/>
    public override async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
    {
        if (rowIndex < 0 || count < 0)
            throw new ArgumentException($"Invalid parameters, rowIndex: {rowIndex} count: {count}");
        if (_rows == null)
            await BuildAsync(ct);
        long wanted = (long)rowIndex + count + 1;   // one more than asked: the grid learns whether a next page exists
        if (!RootsDone && _rows!.Count < wanted)
        {
            await using Reader reader = new(DbContextFactory);
            while (!RootsDone && _rows.Count < wanted)
                await AppendRootsAsync(reader, ct);
        }
        if (RootsDone && _currentRow >= _rows!.Count)
            _currentRow = _rows.Count - 1;   // set past the roots read, before the end was found
        return (int)Math.Max(0, Math.Min(count, (long)_rows!.Count - rowIndex));
    }

    /// <inheritdoc/>
    public override int GetDataVersion() => _version;

    /// <inheritdoc/>
    public override int GetRowCount()
    {
        SyncImpl();
        return RootsDone ? _rows!.Count : int.MaxValue;
    }

    /// <inheritdoc/>
    public override int GetCurrentRowCount()
    {
        SyncImpl();
        return _rows!.Count;
    }

    /// <inheritdoc/>
    public override TGridItem GetItem(int rowIndex)
    {
        SyncImpl();
        return _rows![rowIndex].Item;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<TGridItem> GetDataView()
    {
        SyncImpl();
        return _items.AsReadOnly();
    }

    /// <inheritdoc/>
    public override int GetCurrentRow()
    {
        SyncImpl();
        return _currentRow;
    }

    /// <inheritdoc/>
    public override void SetCurrentRow(int rowIndex)
    {
        SyncImpl();
        if (rowIndex == _currentRow)
            return;
        int rowCount = GetRowCount();   // int.MaxValue while the roots are read: the fetch reads up to the row, or finds the end
        if (rowIndex < -1 || rowIndex >= rowCount)
            throw new ArgumentOutOfRangeException(nameof(rowIndex), $"Row index {rowIndex} out of range: {rowCount}");
        _currentRow = rowIndex;
        FireModelChanged(GridModelChangeType.Other);
    }

    /// <inheritdoc/>
    public ITreeNode GetTreeNode(int rowIndex)
    {
        SyncImpl();
        return _rows![rowIndex];
    }

    /// <inheritdoc/>
    public ITreeNode? GetCurrentTreeNode() => CurrentNode();

    /// <inheritdoc/>
    public override TGridItem? GetCurrentItem() => CurrentNode() is { } node ? node.Item : default;

    private Node? CurrentNode()
    {
        SyncImpl();
        if (_currentRow >= _rows!.Count)
            AsyncHelper.RunSync(() => FetchRowsAsync(_currentRow, 1, CancellationToken.None).AsTask());
        return _currentRow < 0 ? null : _rows![_currentRow];
    }

    /// <summary>
    /// Expands or collapses a folder. It reads nothing itself: the next fetch, the grid's render under its spinner, or the first
    /// getter called reads the children of a folder expanded for the first time. Expanding the row below a page of children
    /// reads the next page there, and makes its first row shown current, or leaves that row current when a filter shows none.
    /// A current row past the rows read is read first, so that it stays on its record.
    /// </summary>
    public ValueTask SetExpandedAsync(int rowIndex, bool expanded)
    {
        ReadUpToCurrentRow();
        SyncImpl();
        Node node = _rows![rowIndex];
        if (node.IsMore)
        {
            if (expanded)
                ReadMore(node.Parent!);
        }
        else if (!node.IsLeaf() && (expanded ? _expanded.Add(node.Path) : _expanded.Remove(node.Path)))
        {
            Invalidate();
            FireModelChanged(GridModelChangeType.Other);
        }
        return ValueTask.CompletedTask;
    }

    private void ReadMore(Node folder)
    {
        TGridItem item = folder.Item;
        List<object> path = PathOf(folder);
        _pendingReads.Add(async (reader, ct) =>
        {
            if (!_children.TryGetValue(Key(item), out Level? level) || level.AllRead)
                return null;   // read again since, or read to the end
            int before = level.Items.Count;
            await ReadAsync(reader, level, item, ChildrenPageSize, ct);
            PathKey folderPath = PathKey.Of(path, path.Count)!;
            _moreRead = (folderPath, [.. level.Items.Skip(before).Select(child => new PathKey(folderPath, Key(child)))]);
            return path;   // the folder, when neither the page nor the "More" row is shown
        });
        Invalidate();
        FireModelChanged(GridModelChangeType.Other);
    }

    // Called before a change: a current row past the rows read is read first, as EFDataGridModel's reload does, so its path
    // finds the same record once the rows are built again. It blocks, outside the grid's fetch
    private void ReadUpToCurrentRow()
    {
        if (_rows != null && _currentRow >= _rows.Count)
            CurrentNode();
    }

    // The rows are built again at the next sync, and the current row put back by its path
    private void Invalidate()
    {
        if (_pathToRestore == null && _rows != null && _currentRow >= 0 && _currentRow < _rows.Count)
            _pathToRestore = PathOf(_rows[_currentRow] is { IsMore: true } more ? more.Parent! : _rows[_currentRow]);   // a "More" key is never read: the build would read the folder to its end
        _rows = null;
    }

    private List<object> PathOf(Node node)
    {
        List<object> path = [];
        for (Node? n = node; n != null; n = n.Parent)
            path.Insert(0, Key(n.Item));
        return path;
    }

    private (string Column, bool Ascending)? DatabaseOrder()
    {
        if (_sortedColumn == null)
            return null;
        int colIdx = GetColumnIndex(_sortedColumn);
        return colIdx >= 0 && GetColumnInfo(colIdx).QueryPushDownEnabled ? (_sortedColumn, _sortedAsc) : null;
    }

    // The one rule for how a level is shown, sorted here or in the order the database read it: LevelAsync keeps, reads and
    // the rows are shown by it, and ReloadItemAsync reads again a level it leaves in the database's order
    private bool InDatabaseOrder(Level level) => _sortedColumn == null || level.Order != null && Equals(level.Order, DatabaseOrder());

    private async ValueTask BuildAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ChildrenPageSize != int.MaxValue && MoreRowFn == null)
            throw new InvalidOperationException($"{nameof(MoreRowFn)} is required with a {nameof(ChildrenPageSize)}");
        _filtersDefs.Sync();
        IReadOnlyList<object>? path = _pathToRestore;
        _pathToRestore = null;   // before the queries: a build that fails does not carry the path, or the reads, into the next one
        List<Func<Reader, CancellationToken, ValueTask<IReadOnlyList<object>?>>> reads = [.. _pendingReads];
        _pendingReads.Clear();
        try
        {
            await using Reader reader = new(DbContextFactory);
            foreach (var read in reads)
                path = await read(reader, ct) ?? path;
            _buildPath = path == null ? null : Enumerable.Range(1, path.Count).Select(n => PathKey.Of(path, n)).ToArray();
            List<Node> rows;
            while (true)
            {
                _zoomPath = _zoom?.Places();
                if (_zoomPath != null)
                    await LevelAsync(reader, null, _zoomPath[0].Key, ct);
                await LevelAsync(reader, null, path?[0], ct);
                _filter = _filtersDefs.IsFiltered() ? new(this, false, _filtersDefs.Definitions) : null;
                rows = [];
                _rootsShown = 0;
                await ShowRootsAsync(reader, rows, ct);
                if (rows.Count > 0 || _zoom == null)
                    break;
                _zoom = _zoom.Parent;   // the folder zoomed on is gone: on its parent instead
            }
            int current = -1;
            if (_moreRead is (PathKey folder, HashSet<PathKey> page))   // a filter may hide the first rows of the page, or all of them
            {
                current = rows.FindIndex(n => !n.IsMore && page.Contains(n.Path));
                if (current < 0)
                    current = rows.FindIndex(n => n.IsMore && n.Parent!.Path.Equals(folder));
            }
            for (int i = (_buildPath?.Length ?? 0) - 1; i >= 0 && current < 0; i--)
                current = rows.FindIndex(n => !n.IsMore && n.Path.Equals(_buildPath![i]));
            _rows = rows;
            _items = rows.Select(n => n.Item).ToList();
            _zoomItems = _zoom == null ? [] : rows[0].Ancestry().Select(n => n.Item).ToArray();   // the folder zoomed on is first
            _currentRow = current >= 0 ? current : Math.Min(_currentRow, rows.Count - 1);
            ++_version;   // no event: a build may run on the grid's fetch thread, and the grid tells its own after the fetch
        }
        finally
        {
            _buildPath = null;
            _moreRead = null;
        }
    }

    private async ValueTask AppendRootsAsync(Reader reader, CancellationToken ct)
    {
        List<Node> rows = _rows!;
        int from = rows.Count;
        await ReadAsync(reader, _roots!, null, MinRowsToRead, ct);
        ThrowIfRebuilt();
        await ShowRootsAsync(reader, rows, ct);
        ThrowIfRebuilt();
        _items.AddRange(rows.Skip(from).Select(n => n.Item));
        ++_version;

        // A sort, a filter or a reload during an await drops the rows this read was adding to
        void ThrowIfRebuilt()
        {
            if (rows != _rows)
                throw new OperationCanceledException("The rows were rebuilt while this read was in flight");
        }
    }

    // The level of a parent's children, null for the roots. One read in another database order is read again, unless all of it
    // is here to be sorted in memory; it is read whole for a sort in memory, and as far as the row wanted, the path's next one
    private async ValueTask<Level> LevelAsync(Reader reader, TGridItem? parent, object? wanted, CancellationToken ct)
    {
        object? key = parent == null ? null : Key(parent);
        Level? level = parent == null ? _roots : _children.GetValueOrDefault(key!);
        (string Column, bool Ascending)? order = DatabaseOrder();
        if (level != null && (level.Reordered && InDatabaseOrder(level) || !Equals(level.Order, order) && !(level.AllRead && !InDatabaseOrder(level))))
            level = null;
        if (level == null)
        {
            List<TGridItem> virtualRows = VirtualChildrenFn?.Invoke(parent)?.ToList() ?? [];
            foreach (TGridItem row in virtualRows)
                _virtual.Add(Key(row));
            level = new(virtualRows, order);
            if (parent == null)
                _roots = level;
            else
                _children[key!] = level;
        }
        int page = parent == null ? MinRowsToRead : ChildrenPageSize;
        if (!InDatabaseOrder(level))
        {
            if (!level.AllRead)
                await ReadAsync(reader, level, parent, int.MaxValue, ct);
        }
        else if (level.Items.Count == 0 && !level.AllRead)
        {
            await ReadAsync(reader, level, parent, page, ct);
        }
        if (wanted != null && !_virtual.Contains(wanted))
        {
            while (!level.AllRead && !level.Items.Any(i => Equals(Key(i), wanted)))
                await ReadAsync(reader, level, parent, page, ct);
        }
        return level;
    }

    private async ValueTask ReadAsync(Reader reader, Level level, TGridItem? parent, int count, CancellationToken ct)
    {
        TDbContext db = await reader.DbAsync(ct);
        int offset = level.Offset;
        IQueryable<TGridItem> query = Ordered(parent == null ? RootsQueryFn(db) : ChildrenQueryFn(db, parent), level.Order)
            .Skip(offset);
        if (count != int.MaxValue)
            query = query.Take(count + 1);   // one more, dropped below: whether there is a next page is known now
        List<TGridItem> read = await query.ToListAsync(ct);
        if (level.Offset != offset)   // another read appended to the level while this one waited
            throw new OperationCanceledException("The level was read on while this read was in flight");
        level.AllRead = read.Count <= count;
        if (!level.AllRead)
            read.RemoveAt(count);
        level.Items.AddRange(read);
        level.Offset += read.Count;
    }

    private IQueryable<TGridItem> Ordered(IQueryable<TGridItem> query, (string Column, bool Ascending)? order)
    {
        if (order is (string column, bool ascending))
        {
            Expression<Func<TGridItem, object?>> expr = GetColumnInfo(GetColumnIndex(column)).PropertyExpression!;
            return ascending ? ItemKey.ThenBy(query.OrderBy(expr), true) : ItemKey.ThenBy(query.OrderByDescending(expr), false);
        }
        return typeof(IOrderedQueryable<TGridItem>).IsAssignableFrom(query.Expression.Type)
            ? ItemKey.ThenBy((IOrderedQueryable<TGridItem>)query, true)
            : ItemKey.WithTheKeyAfterTheLastOrdering(query)   // an AsNoTracking or a Where may follow the OrderBy
                ?? (ItemKeyHelper<TGridItem>.HasOrdering(query.Expression) ? query : ItemKey.OrderBy(query));   // ordered before a projection from another type: left as it is
    }

    // The roots read and not shown yet, after the virtual ones; sorted in memory they are all read, so they are shown at once
    private async ValueTask ShowRootsAsync(Reader reader, List<Node> rows, CancellationToken ct)
    {
        Level roots = _roots!;
        IEnumerable<TGridItem> shown = _rootsShown > 0 ? roots.Items.Skip(_rootsShown)
            : roots.Virtual.Concat(InDatabaseOrder(roots) ? roots.Items : Sorted(roots.Items));
        if (_zoomPath != null)   // zoomed, only the root on the way down
            shown = shown.Where(r => Equals(Key(r), _zoomPath[0].Key)).Take(1);
        foreach (TGridItem root in shown.ToList())
            await VisitAsync(reader, root, null, rows, [], ct);
        _rootsShown = roots.Items.Count;
    }

    // Adds the row of the item and those of its children shown, if the filter admits the item or a row below it. The rows are
    // null below a collapsed folder, visited only to know whether the filter admits something read there
    private async ValueTask<bool> VisitAsync(Reader reader, TGridItem item, Node? parent, List<Node>? rows, HashSet<object> onPath,
        CancellationToken ct)
    {
        object key = Key(item);
        if (!onPath.Add(key))
        {
            logger.LogWarning("Row {key} is its own ancestor: skipped", key);
            return false;
        }
        try
        {
            PathKey path = new(parent?.Path, key);
            int depth = path.Depth;
            int shift = _zoomPath == null ? 0 : _zoomPath.Length - 1;   // the folder zoomed on is at level 0
            if (depth < shift)   // above the folder zoomed on: no row, only the way down to it
            {
                if (!path.Equals(_zoomPath![depth]))
                    return false;
                object next = _zoomPath[depth + 1].Key;
                Level way = await LevelAsync(reader, item, next, ct);
                Node above = new(item, path, parent, true, true, way, false, shift);
                TGridItem? child = way.Virtual.Concat(way.Items).FirstOrDefault(c => Equals(Key(c), next));
                return child != null && await VisitAsync(reader, child, above, rows, onPath, ct);
            }
            bool zoomed = _zoomPath != null && depth == shift;
            bool folder = IsFolderFn(item);
            bool shown = rows != null && folder && _expanded.Contains(path);
            object? wanted = _buildPath != null && depth + 1 < _buildPath.Length && path.Equals(_buildPath[depth]) ? _buildPath[depth + 1]!.Key : null;
            Level? level = shown ? await LevelAsync(reader, item, wanted, ct) : folder ? _children.GetValueOrDefault(key) : null;
            Node node = new(item, path, parent, folder, shown, level, false, shift);
            bool admitted = zoomed || _filter == null || (!_virtual.Contains(key) && _filter.Includes(item));
            int at = rows?.Count ?? 0;
            rows?.Add(node);
            bool below = false;
            if (level != null && (shown || !admitted))
            {
                foreach (TGridItem child in level.Virtual.Concat(InDatabaseOrder(level) ? level.Items : Sorted(level.Items)).ToList())
                    below |= await VisitAsync(reader, child, node, shown ? rows : null, onPath, ct);
                if (!level.AllRead)   // a folder with more to read stays, collapsed too, to read it
                {
                    if (shown)
                    {
                        TGridItem more = MoreRowFn?.Invoke(item)
                            ?? throw new InvalidOperationException($"{nameof(MoreRowFn)} is required with a {nameof(ChildrenPageSize)}");
                        rows!.Add(new Node(more, new(path, Key(more)), node, true, false, null, true, shift));
                    }
                    below = true;
                }
            }
            if (admitted || below)
                return true;
            rows?.RemoveRange(at, rows.Count - at);
            return false;
        }
        finally
        {
            onPath.Remove(key);
        }
    }

    private IEnumerable<TGridItem> Sorted(List<TGridItem> items)
    {
        if (_sortedColumn == null)
            return items;
        int colIdx = GetColumnIndex(_sortedColumn);
        return items.Order(new ColumnComparer<TGridItem>(this, GetComparerForColumn(colIdx), _sortedAsc, colIdx));   // stable
    }

    // ---- reload and go to

    /// <summary>
    /// Reads every row again at the next sync, the children of the folders expanded and the virtual rows included; the
    /// folders stay expanded.
    /// </summary>
    /// <param name="restoreCurrentRow">If true the current row is put back, or its nearest ancestor still shown.</param>
    public void ForceReload(bool restoreCurrentRow)
    {
        if (restoreCurrentRow)
        {
            ReadUpToCurrentRow();
            Invalidate();
        }
        else
        {
            _pathToRestore = null;
            _rows = null;
            _currentRow = -1;
        }
        _roots = null;
        _children.Clear();
        _virtual.Clear();
    }

    /// <summary>
    /// Reads the row again, through the query of its parent, and its children when they are shown next. A virtual row keeps its
    /// place, and its children are read again.
    /// </summary>
    /// <returns>false if the row left the tree, deleted or moved under another parent: the current row then goes to its nearest
    /// ancestor still shown.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled, or a reload dropped the row's level while the row
    /// was read again; the next build reads the level anew.</exception>
    public async ValueTask<bool> ReloadItemAsync(int rowIndex, CancellationToken ct = default)
    {
        ReadUpToCurrentRow();
        SyncImpl();
        Node node = _rows![rowIndex];
        if (node.IsMore)
            return true;
        object key = Key(node.Item);
        _children.Remove(key);
        bool found = true;
        if (!_virtual.Contains(key))
        {
            TGridItem? fresh;
            await using (Reader reader = new(DbContextFactory))
            {
                TDbContext db = await reader.DbAsync(ct);
                IQueryable<TGridItem> query = node.Parent == null ? RootsQueryFn(db) : ChildrenQueryFn(db, node.Parent.Item);
                fresh = await query.Where(ItemKeyHelper<TGridItem>.Predicate(ItemKeyExpr, key)).FirstOrDefaultAsync(ct);
            }
            Level? level = node.Parent == null ? _roots : _children.GetValueOrDefault(Key(node.Parent.Item));
            int at = level?.Items.FindIndex(s => Equals(Key(s), key)) ?? -1;
            if (level == null || at < 0)   // a reload dropped the row's level while the query waited: the next build reads it anew
                throw new OperationCanceledException("The row's level was dropped while this row was read again");
            if (fresh == null)
            {
                level.Items.RemoveAt(at);
                level.Offset--;
                found = false;
                // its other places come from other queries, which only a new read answers for
                foreach (object other in _children.Where(e => e.Value != level && e.Value.Items.Any(s => Equals(Key(s), key))).Select(e => e.Key).ToList())
                    _children.Remove(other);
                if (_roots != level && _roots!.Items.Any(s => Equals(Key(s), key)))
                    _roots = null;
            }
            else
            {
                foreach (Level read in _children.Values.Prepend(_roots!))   // wherever else it sits too
                {
                    for (int i = 0; i < read.Items.Count; i++)
                    {
                        if (Equals(Key(read.Items[i]), key))
                        {
                            read.Items[i] = fresh;
                            read.Reordered = true;
                        }
                    }
                }
                // its other places may have moved too, and a page read by offset past them would skip or repeat a row
                foreach (object other in _children.Where(e => e.Value != level && (!e.Value.AllRead || InDatabaseOrder(e.Value))
                        && e.Value.Items.Any(s => Equals(Key(s), key))).Select(e => e.Key).ToList())
                    _children.Remove(other);
                if (_roots != level && (!_roots!.AllRead || InDatabaseOrder(_roots)) && _roots.Items.Any(s => Equals(Key(s), key)))
                    _roots = null;
                if (!level.AllRead || InDatabaseOrder(level))   // the base query's order too: read again, down to it
                {
                    if (node.Parent == null)
                        _roots = null;
                    else
                        _children.Remove(Key(node.Parent.Item));
                }
            }
        }
        if (!found && _zoom != null && _zoom.IsAtOrBelow(node.Path))
            _zoom = node.Path.Parent;   // a zoom through the row gone would read its level to the end too
        if (!found && _currentRow >= 0 && _currentRow < _rows.Count && _rows[_currentRow].Ancestry().Contains(node))
        {
            // a path through the row gone would make the build read its level to the end looking for it
            _pathToRestore = node.Parent == null ? null : PathOf(node.Parent);
            _rows = null;
        }
        else
            Invalidate();
        FireModelChanged(GridModelChangeType.Other);
        return found;
    }

    /// <summary>
    /// Makes the row current, expanding its ancestors, which <see cref="ParentFn"/> finds, and zooming out as far as it takes to
    /// show them; this reads only them. Their children
    /// are read again by the next fetch, the grid's render under its spinner, or the first getter called, as many pages as it
    /// takes to reach the row. A row not shown then, filtered out or no longer below its ancestors, leaves the current row on
    /// its nearest ancestor shown.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown without a <see cref="ParentFn"/>, or when a row is its own ancestor.</exception>
    public async ValueTask GoToAsync(TGridItem item, CancellationToken ct = default)
    {
        if (ParentFn == null)
            throw new InvalidOperationException($"{nameof(ParentFn)} is required to go to a row");
        List<object> path = [Key(item)];
        await using (Reader reader = new(DbContextFactory))
        {
            TDbContext db = await reader.DbAsync(ct);
            for (TGridItem? parent = item; (parent = await ParentFn(db, parent, ct)) != null;)
            {
                object parentKey = Key(parent);
                if (path.Contains(parentKey))
                    throw new InvalidOperationException($"Row {parentKey} is its own ancestor");
                path.Insert(0, parentKey);
            }
        }
        for (int n = 1; n < path.Count; n++)
        {
            _expanded.Add(PathKey.Of(path, n)!);
            _children.Remove(path[n - 1]);
        }
        if (_roots != null && !_virtual.Contains(path[0]) && !_roots.Items.Any(i => Equals(Key(i), path[0])))
            _roots = null;   // a root added or moved there since the roots were read
        PathKey target = PathKey.Of(path, path.Count)!;
        while (_zoom != null && !target.IsAtOrBelow(_zoom))
            _zoom = _zoom.Parent;   // the row shows only below the folder zoomed on
        _rows = null;
        _pathToRestore = path;
        FireModelChanged(GridModelChangeType.Other);
    }

    // ---- expand all and zoom

    /// <summary>
    /// Expands the folder at the row and every folder below it. The next build reads all their children, every page: one query
    /// per folder at least, so mind it over a large tree. -1 expands the whole tree, every root read.
    /// </summary>
    public ValueTask ExpandAllAsync(int rowIndex)
    {
        ReadUpToCurrentRow();
        if (rowIndex < 0)
        {
            _pendingReads.Add(async (reader, ct) =>
            {
                Level roots = await LevelAsync(reader, null, null, ct);
                while (!roots.AllRead)
                    await ReadAsync(reader, roots, null, MinRowsToRead, ct);
                foreach (TGridItem root in roots.Virtual.Concat(roots.Items).ToList())
                    await ExpandBelowAsync(reader, root, new(null, Key(root)), [], ct);
                return null;
            });
        }
        else
        {
            SyncImpl();
            Node node = _rows![rowIndex];
            if (node.IsMore)
                node = node.Parent!;
            HashSet<object> onPath = [];
            for (Node? n = node.Parent; n != null; n = n.Parent)
                onPath.Add(Key(n.Item));
            (TGridItem item, PathKey path) = (node.Item, node.Path);
            _pendingReads.Add(async (reader, ct) =>
            {
                await ExpandBelowAsync(reader, item, path, onPath, ct);
                return null;
            });
        }
        Invalidate();
        FireModelChanged(GridModelChangeType.Other);
        return ValueTask.CompletedTask;
    }

    private async ValueTask ExpandBelowAsync(Reader reader, TGridItem item, PathKey path, HashSet<object> onPath, CancellationToken ct)
    {
        object key = Key(item);
        if (!IsFolderFn(item) || !onPath.Add(key))
            return;
        try
        {
            _expanded.Add(path);
            Level level = await LevelAsync(reader, item, null, ct);
            while (!level.AllRead)
                await ReadAsync(reader, level, item, ChildrenPageSize, ct);
            foreach (TGridItem child in level.Virtual.Concat(level.Items).ToList())
                await ExpandBelowAsync(reader, child, new(path, Key(child)), onPath, ct);
        }
        finally
        {
            onPath.Remove(key);
        }
    }

    /// <summary>
    /// Collapses the folder at the row and every folder below it; -1 collapses the whole tree. The folder zoomed on stays
    /// expanded: only the folders below it collapse.
    /// </summary>
    public ValueTask CollapseAllAsync(int rowIndex)
    {
        ReadUpToCurrentRow();
        if (rowIndex < 0)
        {
            _expanded.Clear();
        }
        else
        {
            SyncImpl();
            Node node = _rows![rowIndex];
            PathKey top = node.IsMore ? node.Parent!.Path : node.Path;
            _expanded.RemoveWhere(p => p.IsAtOrBelow(top));
        }
        if (_zoom != null)
            _expanded.Add(_zoom);   // zoomed on, the folder shows expanded
        Invalidate();
        FireModelChanged(GridModelChangeType.Other);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Shows the current folder as the only root, expanded, at level 0, or the folder of the current leaf or "More" row: the rows
    /// above and beside it go, and the roots no longer page. Should the folder be gone at a later build, the zoom moves to its
    /// parent.
    /// </summary>
    public ValueTask ZoomInAsync()
    {
        SyncImpl();
        if (ZoomTarget() is { } node)
        {
            _expanded.Add(node.Path);
            _zoom = node.Path;
            Invalidate();
            FireModelChanged(GridModelChangeType.Other);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Climbs that many levels back up; zero or less goes all the way out. The current row stays.</summary>
    public ValueTask ZoomOutAsync(int levels)
    {
        ReadUpToCurrentRow();
        if (_zoom != null)
        {
            for (int i = 0; _zoom != null && (levels <= 0 || i < levels); i++)
                _zoom = _zoom.Parent;
            Invalidate();
            FireModelChanged(GridModelChangeType.Other);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public int GetZoomLevel()
    {
        SyncImpl();   // a build moves the zoom off a folder gone
        return _zoom == null ? 0 : _zoom.Depth + 1;
    }

    /// <inheritdoc/>
    public TGridItem? GetZoomedItem()
    {
        SyncImpl();
        return _zoom == null ? null : _rows![0].Item;   // always admitted, and first
    }

    /// <inheritdoc/>
    public IReadOnlyList<TGridItem> GetZoomPath() => _zoomItems;

    /// <inheritdoc/>
    public bool CanZoomIn() => _rows != null && _currentRow < _rows.Count && ZoomTarget() != null;   // reads nothing: the zoom bar draws before the grid's fetch

    /// <inheritdoc/>
    public bool IsFolderModel() => true;

    // ---- tree state, for a memento

    /// <inheritdoc/>
    /// <remarks>The key must be a single value, a number, a string, a <see cref="Guid"/> or a date: false for a composite <see cref="ItemKeyExpr"/>.</remarks>
    public bool CanSaveTreeState() => _canSaveTreeState ??= IsSingleValue(Nullable.GetUnderlyingType(ItemKey.KeyType) ?? ItemKey.KeyType);

    private static bool IsSingleValue(Type keyType) => keyType.IsPrimitive || keyType.IsEnum || keyType == typeof(string)
        || keyType == typeof(decimal) || keyType == typeof(Guid) || keyType == typeof(DateTime) || keyType == typeof(DateTimeOffset)
        || keyType == typeof(DateOnly) || keyType == typeof(TimeOnly);

    /// <inheritdoc/>
    /// <remarks>A folder found gone at the last build is left out, with a warning.</remarks>
    public TreeState GetTreeState()
    {
        if (!CanSaveTreeState())
            throw new NotSupportedException($"A memento cannot store a key of type {ItemKey.KeyType}: {nameof(ItemKeyExpr)} must give a single value");
        static NodePath NodePathOf(PathKey path) => new(path.Places().Select(p => p.Key).ToArray());
        List<NodePath> expanded = [];
        foreach (PathKey path in _expanded)
        {
            if (IsGone(path))
                logger.LogWarning("Expanded folder {path} is gone: left out of the memento", path);
            else
                expanded.Add(NodePathOf(path));
        }
        return new(expanded, _zoom == null ? null : NodePathOf(_zoom));
    }

    // Gone when a level on its path was read whole and has no row with its key; a level not read, or not whole, may still have it
    private bool IsGone(PathKey path)
    {
        Level? level = _roots;
        foreach (PathKey place in path.Places())
        {
            if (level == null)
                return false;
            if (!level.Virtual.Concat(level.Items).Any(i => Equals(Key(i), place.Key)))
                return level.AllRead;
            level = _children.GetValueOrDefault(place.Key);
        }
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The folders are read again at the next build, one query per folder expanded; a zoom on a folder gone goes to its nearest
    /// ancestor still there.
    /// </remarks>
    public void RestoreTreeState(TreeState state)
    {
        Type keyType = ItemKey.KeyType;
        object KeyOf(object key) => key is JsonElement json ? json.Deserialize(keyType).RequireNotNull() : key;
        PathKey? PathOf(NodePath? node)
        {
            if (node == null)
                return null;
            try
            {
                return PathKey.Of(node.Keys.Select(KeyOf).ToArray(), node.Keys.Count);
            }
            catch (JsonException)   // saved under another key type: that folder is no longer there
            {
                logger.LogWarning("Saved folder {path} does not fit a key of type {type}: skipped", string.Join("/", node.Keys), keyType);
                return null;
            }
        }
        ReadUpToCurrentRow();
        _expanded.Clear();
        foreach (NodePath node in state.Expanded)
        {
            if (PathOf(node) is { } path)
                _expanded.Add(path);
        }
        _zoom = PathOf(state.Zoom);
        if (_zoom != null)
            _expanded.Add(_zoom);   // zoomed on, the folder shows expanded
        Invalidate();
    }

    private Node? ZoomTarget()
    {
        if (_rows == null || CurrentNode() is not { } node)
            return null;
        Node? target = node.IsMore || node.IsLeaf() ? node.Parent : node;   // a leaf zooms on its folder, a root leaf on nothing
        return target == null || target.Path.Equals(_zoom) ? null : target;
    }

    // ---- filters and sort

    /// <inheritdoc/>
    public override IColumn<TGridItem>? RemoveColumn(string columnName)
    {
        if (columnName == _sortedColumn || _filtersDefs.Definitions.ContainsKey(columnName))
            ReadUpToCurrentRow();   // the rows are built again only then
        IColumn<TGridItem>? res = base.RemoveColumn(columnName);
        if (res == null)
            return null;
        if (columnName == _sortedColumn)
        {
            _sortedColumn = null;
            Invalidate();
        }
        if (_filtersDefs.Remove(columnName))
            Invalidate();
        else if (_filter != null)
            _filter = new(this, false, _filtersDefs.Definitions);   // it holds column indexes
        return res;
    }

    /// <summary>The comparer for the values of column <paramref name="columnIdx"/>, as <see cref="PocoColumnComparer.GetComparerForColumn"/> gives it.</summary>
    protected virtual IComparer<object>? GetComparerForColumn(int columnIdx)
    {
        return PocoColumnComparer.GetComparerForColumn(GetColumn(columnIdx));
    }

    /// <inheritdoc/>
    protected override void ValidateStoredColumn(int columnIndex)
    {
        if (GetColumn(columnIndex).IsSortable())
            PocoColumnComparer.CheckCanSort(GetColumnName(columnIndex), GetColumn(columnIndex).GetValueType(), GetComparerForColumn(columnIndex));
    }

    /// <inheritdoc/>
    public bool IsFilterable() => true;

    /// <inheritdoc/>
    public void ApplyFilters(IEnumerable<KeyValuePair<string, AFilterDef>> filters)
    {
        ReadUpToCurrentRow();
        var checkedFilters = CheckFilterColumns(filters);
        _filtersDefs.Clear();
        foreach (var entry in checkedFilters)
            _filtersDefs.Add(entry.Key, entry.Value.Clone());
        Invalidate();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, AFilterDef> GetFilters() => _filtersDefs.Definitions;

    /// <inheritdoc/>
    public bool IsFiltered() => _filtersDefs.IsFiltered();

    /// <inheritdoc/>
    public bool IsSortable() => true;

    /// <inheritdoc/>
    public string? GetSortedColumn() => _sortedColumn;

    /// <inheritdoc/>
    public SortType GetSortType() => _sortedColumn == null ? SortType.None : (_sortedAsc ? SortType.Asc : SortType.Desc);

    /// <inheritdoc/>
    public void SetSortedColumn(string? columnName, bool ascending)
    {
        if (_sortedColumn == columnName && (columnName == null || _sortedAsc == ascending))   // no sort has no direction
            return;
        if (columnName != null && GetColumnIndex(columnName) < 0)
            throw new ArgumentException($"Column not found: {columnName}");
        if (columnName != null)   // a column the user cannot sort may still be sorted in code
            PocoColumnComparer.CheckCanSort(columnName, GetColumn(GetColumnIndex(columnName)).GetValueType(), GetComparerForColumn(GetColumnIndex(columnName)));
        ReadUpToCurrentRow();
        _sortedColumn = columnName;
        _sortedAsc = ascending;
        Invalidate();
    }

    /// <exception cref="InvalidOperationException">Always: a tree's rows are not in one order to search.</exception>
    public ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default)
        => throw new InvalidOperationException("A tree's rows are not in one order to search");

    /// <inheritdoc/>
    public bool CanSearchOnSortedColumn() => false;

    /// <inheritdoc/>
    public bool CanDoContainsSearch() => true;

    /// <inheritdoc/>
    /// <remarks>Over the rows shown: a forward search reads the next roots when it reaches the last row.</remarks>
    public async ValueTask<int> ContainsSearchAsync(string columnName, string value, bool forward, int startFrom, CancellationToken ct = default)
    {
        int colIdx = GetColumnIndex(columnName);
        if (colIdx < 0)
            throw new ArgumentException($"Column not found: {columnName}");
        if (_rows == null)
            await BuildAsync(ct);
        if (!forward && startFrom >= _items.Count)   // a backward search from a row not read reads up to its start
            while (_items.Count <= startFrom && !RootsDone)
            {
                await using Reader reader = new(DbContextFactory);
                await AppendRootsAsync(reader, ct);
            }
        while (true)
        {
            int res = PocoColumnComparer.ContainsSearch(_items, this, colIdx, value, forward, startFrom);
            if (res >= 0 || !forward || RootsDone)
                return res;
            startFrom = Math.Max(startFrom, _items.Count);
            await using Reader reader = new(DbContextFactory);
            await AppendRootsAsync(reader, ct);
        }
    }

    // ---- types

    // One context for the queries of a build, opened by the first
    private sealed class Reader(IDbContextFactory<TDbContext> factory) : IAsyncDisposable
    {
        private TDbContext? _db;

        public async ValueTask<TDbContext> DbAsync(CancellationToken ct) => _db ??= await factory.CreateDbContextAsync(ct);

        public ValueTask DisposeAsync() => _db?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    // The roots, or a folder's children: the virtual rows, then the rows read a page at a time in the database order given
    private sealed class Level(List<TGridItem> virtualRows, (string Column, bool Ascending)? order)
    {
        public List<TGridItem> Virtual { get; } = virtualRows;

        public List<TGridItem> Items { get; } = [];

        public int Offset { get; set; }   // where the next page starts in the database: the rows read, less those deleted since

        public bool AllRead { get; set; }

        public bool Reordered { get; set; }   // a row was read again in place: Items is no longer in the order it was read in

        public (string Column, bool Ascending)? Order { get; } = order;
    }

    // A row's place: its key under its parent's place, as a row under two parents has two places, each expanded on its own
    private sealed class PathKey(PathKey? parent, object key) : IEquatable<PathKey>
    {
        private readonly int _hash = HashCode.Combine(parent?._hash, key);

        public PathKey? Parent => parent;

        public object Key => key;

        public int Depth { get; } = parent == null ? 0 : parent.Depth + 1;

        public bool IsAtOrBelow(PathKey top)
        {
            PathKey? p = this;
            while (p != null && p.Depth > top.Depth)
                p = p.Parent;
            return top.Equals(p);
        }

        // Its places and those of its ancestors, from the root
        public PathKey[] Places()
        {
            PathKey[] places = new PathKey[Depth + 1];
            for (PathKey? p = this; p != null; p = p.Parent)
                places[p.Depth] = p;
            return places;
        }

        // The place of the first count keys of a path from the root
        public static PathKey? Of(IReadOnlyList<object> keys, int count)
        {
            PathKey? path = null;
            for (int i = 0; i < count; i++)
                path = new(path, keys[i]);
            return path;
        }

        public bool Equals(PathKey? other) => other != null && (ReferenceEquals(this, other)
            || (_hash == other._hash && Depth == other.Depth && Equals(key, other.Key) && Equals(parent, other.Parent)));

        public override bool Equals(object? obj) => Equals(obj as PathKey);

        public override int GetHashCode() => _hash;

        public override string ToString() => string.Join("/", Places().Select(p => p.Key));
    }

    private sealed class Node(TGridItem item, PathKey path, Node? parent, bool folder, bool expanded, Level? level, bool more, int shift)
        : ITreeNode
    {
        public TGridItem Item => item;

        public PathKey Path => path;

        public Node? Parent => parent;

        // From the root down to this node
        public IEnumerable<Node> Ancestry() => parent == null ? [this] : parent.Ancestry().Append(this);

        public bool IsMore => more;   // below a page of children: expanding it reads the next page

        public bool IsExpanded() => expanded;

        public int GetLevel() => path.Depth - shift;

        public bool IsLeaf() => !folder;

        public bool IsSubTotal() => false;

        // not read yet, or more to read: it may have some
        public bool HasChildren() => folder && (level == null || !level.AllRead || level.Virtual.Count + level.Items.Count > 0);
    }
}
