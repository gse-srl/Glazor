# Data models

A Glazor grid never owns its rows. It asks a **model** for them, and the model decides where they come from,
how filtering and sorting happen, and how much is held in memory. Picking the right one is the main design
decision when adding a grid.

- **Namespaces:** `Glazor.Grds.Mdls`, and `Glazor.EntityFrameworkCore` for the EF Core models, which ship in the package
  of the same name: the `Glazor` package alone does not depend on EF Core.

## Choosing a model

| Model | Data lives in | Filter & sort | Use when |
| --- | --- | --- | --- |
| `GridModel<T>` | A list you own | None: the order is the user's | An ordered list the user reorders by hand, in a `MultiSelGrid` |
| `EditableGridModel<T>` | A list you own | None: the order is the user's | …plus insert / edit / cancel on the ordered list |
| `DataGridModel<T>` | A list you own | In memory | The usual in-memory grid |
| `EditableDataGridModel<T>` | A list you own | In memory | …plus insert / edit / cancel |
| `EFDataGridModel<TDbContext, T>` | The database | **Translated to SQL** | Tables too large to load, or filtering that should be indexed |
| `EditableEFDataGridModel<…>` | The database | Translated to SQL | …plus insert / edit / cancel |
| `TreeGridModel<T>` | A hierarchy (`T : ITreeNode`) | In memory | With `Treegliaria` |
| `EFFolderModel<TDbContext, T>` | The database, a tree of folders and leaves | In memory, over the rows read | With `Treegliaria`, children read when a folder opens |

All of them implement `IGridModel<T>`. The ones `Grigliaria` and `Treegliaria` take implement `IDataGridModel<T>`;
`GridModel` and `EditableGridModel` implement `IOrderedGridModel<T>` instead, which only `MultiSelGrid` accepts. The interface is what
the grid consumes, not something to implement yourself — a custom model derives from `AGridModel<T>` or from
one of the [wrappers](#wrappers).

## Common concepts

### Item identity

Every model needs to know how to identify a row, so it can keep the current row selected across a reload, a
re-sort, or a filter change.

```csharp
new DataGridModel<Product> { ItemKeyFn = p => p.Id }                 // in-memory: a delegate
new EFDataGridModel<AppDb, Product> { ItemKeyExpr = p => p.Id, /* DbContextFactory, BaseQueryFn */ }   // EF: an expression, so it can be queried
```

### Sync

Models are lazy. Filters, sorting and pending reloads are applied on the next **sync**, which the grid triggers
as part of rendering — you rarely call `Sync()` yourself. `GetDataVersion()` increments every time a sync
re-applies filters, sorting or a reload, whether or not the resulting view differs from the previous one; treat a
change in it as "the view may have changed", not as proof that it did.

A model is not thread-safe: use it only on the circuit's dispatcher, as the grid does. From a timer, a background task or
any callback that arrives on another thread, reach it through the component's `InvokeAsync`, then call `RepaintAsync()` or
`StateHasChanged()`.

### Frozen rows

While an edit or insert is pending, `IsCurrentRowFrozen()` returns `true`, and moving to another row, searching, filtering,
sorting, `RemoveColumn` on a sorted or filtered column, `SwapRows`, `ForceReload` and `ReloadItemAsync` throw
`InvalidOperationException`. Check it before calling them, as a page that reloads an `EditableEFDataGridModel` when a
parameter changes must, and disable your toolbar buttons on it:

```razor
<button @onclick="RefreshAsync" disabled="@(Model.IsCurrentRowFrozen())">Refresh</button>
```

The list `GetData()` returns is not checked: leave it alone while an edit or insert is pending.

### Finding a row by text

`ContainsSearchAsync(column, text, forward, startFrom)` returns the next row, or the previous one, whose value in the column
contains the text, ignoring case, and a negative number when there is none. The row it starts from is included, and a value
that is not a string is matched on its text. No grid calls it: it is for a page's own "Find" box. `DataGridModel`,
`EFDataGridModel` and `EFFolderModel` support it, as `CanDoContainsSearch()` says. A paged model reads on as far as the
search needs, querying the database as it goes.

```razor
<input @bind="_find" aria-label="Find" />
<button @onclick="FindNextAsync" disabled="@(!_model.CanDoContainsSearch() || _model.IsCurrentRowFrozen())">Find next</button>

@code {
    private string _find = "";

    private async Task FindNextAsync()
    {
        int row = await _model.ContainsSearchAsync("Name", _find, forward: true, startFrom: _model.GetCurrentRow() + 1);
        if (row >= 0)
            _model.SetCurrentRow(row);   // the grid shows it
    }
}
```

## In-memory models

### `DataGridModel<T>`

The default for data you already have. The item must be a reference type (`where T : class`), as for every in-memory model: the current item is tracked through a nullable field, which a struct cannot leave empty. The same constraint applies to the value type of the set filters, `SetDef<VT>`, `DynaSetDef` and `DynaValuesInSetDef`: over a column that holds a value type, use a `DistinctDef` such as `EFDistinctDef`, whose dialog rows wrap the values; over a collection of value-type items, ids for instance, use `EFValuesInSetDef`, or derive from `ADynaValuesInSetDef`, whose rows are a class of their own.

```csharp
private readonly DataGridModel<Role> _model = new()
{
    ItemKeyFn = r => r.Id,
};

// later — replace the contents
_model.GetData().Set(roles);
```

`GetData()` returns an `AList<T>` — a mutable list that marks the model as changed, so the next render re-applies
filters and sorting. It does not render anything by itself: after changes made outside a UI event, call `StateHasChanged()`
when the grid has to refresh at once.

A column whose getter reads state that changes, a rate or a setting, shows the new values at once, but the sort and the
filters use them only from the next change to the data, the filters or the sort. A column that registers again with a new
getter, as one whose lambda captures a local does at every render, applies nothing again: under `@foreach` that would sort
the whole list at every render. After changing such state, `_model.ApplyFilters(_model.GetFilters())` applies the filters
and the sort again.

```csharp
var data = _model.GetData();
data.Set(items);                 // wrap this list, not a copy; a read-only one, as an array, is copied
data.SetAndGoto(items, current); // the same, and make `current` the current row
data.Add(item);                  // append; it becomes the current row, as with Insert
data.AddRange(items);            // append; the current row stays
data.Insert(0, item);
data.Remove(item);
data.RemoveAt(0);
data.Clear();
```

Or seed it through the constructor:

```csharp
var model = new DataGridModel<Product>(products);
```

Sorting compares a column's values through `Comparer<T>.Default`: the column's declared type must implement
`IComparable<T>` or `IComparable`, nullable or not, or be `object`, unless a subclass supplies a comparer through
`GetComparerForColumn`. A sortable column of another type, a record, a class or a collection that implements neither,
makes the model throw `InvalidOperationException` when it registers, a configuration error: set `Sortable` to false on it.
The EF models follow the same rule.

### `GridModel<T>`

Adds ordered-list semantics — most usefully row reordering:

```csharp
new GridModel<Step>
{
    ItemKeyFn         = s => s.Id,
    AreRowsSwappableFn = (model, from, to) => true,   // let the up and down buttons move any row
}
```

`MultiSelGrid` asks `AreRowsSwappableFn` at every render whether the current row can move up or down, and only when there
is a current row and a row on that side, so the function can read both rows by index.

### `EditableDataGridModel<T>`

Adds the edit lifecycle. It's constructed with the callbacks it needs to reload data, because after a post it
has to re-read what the server actually stored. `ItemKeyFn` is required: the key finds the row again after a reload,
and `EditAsync` or `InsertAsync` throws `InvalidOperationException` without one.

```csharp
_model = new EditableDataGridModel<Mapping>(
    reloadAllFn: async () => await LoadAllAsync())
{
    ItemKeyFn = m => m.Id,
};

// with per-row reload as well
_model = new EditableDataGridModel<Mapping>(
    reloadAllFn: async () => await LoadAllAsync(),
    reloadItemFn: async m => await LoadOneAsync(m.Id));   // null when the row is gone
```

The lifecycle:

```csharp
Mapping draft = await _model.EditAsync();                   // begin editing current row
Mapping draft = await _model.InsertAsync(proto, afterCurrentRow: true);
await _model.PostAsync();                                   // commit; triggers reload
await _model.CancelAsync();                                 // discard

bool editing   = _model.IsEditPending();
bool inserting = _model.IsInsertPending();
bool canEdit   = _model.IsCurrentRowEditable();
```

Between `EditAsync`/`InsertAsync` and `PostAsync`/`CancelAsync` the row is frozen. `EditAsync` hands back the
item to bind your form to. While an insert is pending, the row being inserted is the current row, whether it went
before or after the one that was current; `CancelAsync` gives that one back. It is not stored until `PostAsync`, so
`GetDataView()` leaves it out and is one row shorter than `GetCurrentRowCount()`: read rows by position with
`GetItem()`. `PostAsync` finds the new row by its key, so give the item the key it was stored under, a generated
identity included, before calling it. The item `EditAsync` returns is the
model's own, so the form changes it in place; `CancelAsync` finds the row again by the key it had when the edit
started, so a cancelled change to a key member brings back the same row. An EF model reloads that row; an in-memory
one reloads everything after such a change, since your single-row reload reads the row the item names.

`PostAsync` and `CancelAsync` take an optional `CancellationToken`, which cancels the read that follows; the edit or the
insert ends all the same. An in-memory model has no token to give your reload functions: a token already cancelled
stops the model before it calls them.

> [!TIP]
> If editing happens in a dialog rather than inline, a plain `DataGridModel` is enough — but starting from
> `EditableDataGridModel` costs nothing and leaves inline editing available later.

### `TreeGridModel<T>`

The model `Treegliaria` takes. It is a `DataGridModel` whose rows are the visible nodes in pre-order: a flat list, with no
tree inside it. Each row answers the five members of `ITreeNode`, and the grid draws the indentation and the expander from
them:

```csharp
public sealed class Node : ITreeNode
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int Level { get; init; }                 // 0 is a root
    public bool Expanded { get; set; }
    public List<Node> Children { get; init; } = [];

    int ITreeNode.GetLevel() => Level;
    bool ITreeNode.IsExpanded() => Expanded;
    bool ITreeNode.IsLeaf() => Children.Count == 0;
    bool ITreeNode.IsSubTotal() => false;          // true for a leaf that carries totals
    bool ITreeNode.HasChildren() => Children.Count > 0;   // true while the children are not loaded yet
}
```

Which rows are in the list is the host's decision. Expanding or collapsing a row calls `OnSetExpand`, which is `required`:
it records the new state and sets the list again without the collapsed nodes' descendants. Build the model in the
page's constructor or in `OnInitialized`, since the closure needs the model itself; a field initializer cannot refer to
another field (CS0236):

```csharp
private readonly List<Node> _roots = LoadRoots();
private TreeGridModel<Node> _tree = null!;

protected override void OnInitialized()
{
    _tree = new() { ItemKeyFn = n => n.Id, OnSetExpand = SetExpandAsync };
    Rebuild();
}

private ValueTask SetExpandAsync(int rowIndex, bool expanded)
{
    Node node = _tree.GetItem(rowIndex);
    if (node.Expanded != expanded)
    {
        node.Expanded = expanded;
        Rebuild();
    }
    return ValueTask.CompletedTask;
}

private void Rebuild() => _tree.GetData().Set(Flatten(_roots).ToList());

private static IEnumerable<Node> Flatten(IEnumerable<Node> nodes)
{
    foreach (Node node in nodes)
    {
        yield return node;
        if (node.Expanded)
            foreach (Node child in Flatten(node.Children))
                yield return child;
    }
}
```

`GetTreeNode(rowIndex)` and `GetCurrentTreeNode()` give a row's node, and `SetExpandedAsync(rowIndex, expanded)` calls
`OnSetExpand` as the expander does, for an "expand all" button. To keep the open nodes across a reload, keep the expanded
ids in a set and stamp `Expanded` from it while flattening the fresh data. The expansion state is not part of the grid's
`Memento`, unless the model implements `ITreeStateGridModel`: then `Treegliaria` saves it in a `TreeMemento`, see
[Architecture › Treegliaria](Technical_Documentation.md#treegliaria).

Sorting and filtering are `DataGridModel`'s, row by row over the flat list, so a sort can move a child under another parent
and a filter can keep a child without its parent. What a sorted or filtered tree means is left to the model: until it
decides, set `Sortable="false"` and `Filterable="false"` on the tree's columns, as the demo does. Over a database,
[`EFFolderModel`](#effoldermodeltdbcontext-tgriditem) sorts each level and filters keeping the parents.

## `EFDataGridModel<TDbContext, TGridItem>`

Backs the grid with an EF Core query. Filters and sorting on `EFProperty` columns are composed into the query
and executed by the database; rows arrive incrementally.

```csharp
using Glazor.EntityFrameworkCore;

private EFDataGridModel<AppDbContext, AccessDTO> _model = null!;
private int? _lastUserId;

protected override void OnParametersSet()
{
    if (_lastUserId == UserId && _model != null)
        return;
    _lastUserId = UserId;
    _model ??= new()
    {
        DbContextFactory = DbContextFactory,
        ItemKeyExpr      = x => x.Id,
        BaseQueryFn      = (db, _) => db.AccessLogins
            .AsNoTracking()
            .Where(x => x.UserId == _lastUserId)
            .Select(x => new AccessDTO { Id = x.Id, /* … */ })
            .OrderByDescending(x => x.LoginDateTime),
        ReadAll = false,      // page instead of loading everything
    };
    _model.ForceReload(false); // the user changed: re-query; the old current row is meaningless, the filters and sort stay
}
```

### Configuration

| Member | Type | Default | Description |
| --- | --- | --- | --- |
| `DbContextFactory` | `IDbContextFactory<TDbContext>` | — | **Required.** A factory, not a context: the model creates a short-lived context per query. |
| `BaseQueryFn` | `Func<TDbContext, TGridItem?, IQueryable<TGridItem>>` | — | **Required.** Builds the base query. Filters and sorting are composed onto whatever you return. |
| `ItemKeyExpr` | `Expression<Func<TGridItem, object>>` | — | **Required.** The key, as an expression so it can take part in queries. A composite key is an anonymous type, a record, a class whose constructor takes its properties, or a tuple, `x => new { x.Policy, x.Number }` or `x => ValueTuple.Create(x.Policy, x.Number)` (an expression takes no tuple literal, so not `(x.Policy, x.Number)`): each member comparable, the whole compared by value. A class that does not compare by value is refused with `InvalidOperationException` on first use. A plain key must not be null; members of a composite key may be. |
| `ReadAll` | `bool` | `false` | `false` reads incrementally; `true` reads every row in one query. Filters and sorting work as with `false`: a change runs the query again. |
| `MinRowsToRead` | `int` | `1024` | Rows per fetch; clamped to a minimum of 10. Larger means fewer round trips and more memory. Only meaningful when `ReadAll` is `false`. |
| `SpillDirectory` | `string?` | `null` (OS temp) | Where evicted row chunks are written when paging. See [Paging stores rows on disk](#paging-stores-rows-on-disk). |
| `AlwaysRestoreCurrentPK` | `bool` | `true` | Re-find the current row by key after a reload, reading on past the rows read if it must. One query first asks the database whether its filters still admit the row, so a row they exclude is not looked for; a row only a filter applied in memory excludes is looked for to the end of the table. |
| `CreateItemsListFn` | `Func<int, ValueTask<ISortableList<TGridItem>>>?` | `null` | Supply your own backing list. By default a paged model gets an `OnDiskChunkedList` and a `ReadAll` one a `SortableList`, a plain in-memory list: `size => ValueTask.FromResult<ISortableList<T>>(new SortableList<T>(size))` keeps a paged model's rows in memory too. |

### Extension points

Six `protected virtual` members let a subclass shape what is queried and what is kept; the first four are listed in the
order they run:

| Member | Runs | Use it to |
| --- | --- | --- |
| `CompleteQueryable(dbContext, query, request)` | Once per fetch, before the query executes; also once when a restore asks whether the filters still admit the current row's key, with `ReadAll` true, `RequestedCount` 0 and no sort, a query that must stay unpaged. The `ReadRequest` carries `ReadAll`, `RowIndex`, `RequestedCount`, `FilterDefs`, `SortedColumn` and `SortedAsc`. Composes the pushed-down filters, through `FilterQueryable`, the sort and the `Skip`/`Take`. | Change how sorting or paging is translated. Call `base` unless you replace the whole pipeline, with the request as it is: the model takes the end of the data and the next offset from `RowIndex` and `RequestedCount`, and applies the same filters and sort in memory and to single-row reloads. To change which rows a filter admits, override `FilterQueryable`. |
| `FilterQueryable(dbContext, query, filterDefs)` | Inside `CompleteQueryable`, before the sort; also in a single-row `ReloadItemAsync`, which decides with it whether the row still passes the filters. | Change how filters are translated, or which rows a filter admits: the reload then agrees with the paged query, and the paging offset stays right. Call `base` for the filters you leave to their definitions. |
| `RefineQueryBeforeExec(dbContext, singleItemReload, query)` | Inside `CompleteQueryable`, after filters and sort, before paging; also before a single-row `ReloadItemAsync`; and once after each `ForceReload`, on the base query, unfiltered and never run, to read its order. | Add `Include`s, a projection, or drop heavy columns that must stay queryable for a `WHERE` but need not be fetched. |
| `RefineItemAsync(dbContext, item, phase, ct)` | Per row: `BeforeFiltering` for every row the query returned, `AfterFiltering` for the rows that survived the in-memory filters, `SingleItemReload` on a re-read. | Load what in-memory filters need (`BeforeFiltering`, keep it cheap), or trim what will be held and spilled (`AfterFiltering`, and `SingleItemReload`, whose row is stored as it returns it, but for what the in-memory filters read). |
| `ReloadItemAsync(rowIndex, pk, ct)` | On every re-read of a single row: `ReloadItemAsync(rowIndex)` passes the row's own key, a cancelled edit of key members the key the row had when the edit started. The token cancels the query: the one given to `ReloadItemAsync(rowIndex, ct)`, `PostAsync(ct)` or `CancelAsync(ct)`. | Hook every single-row reload, which the public `ReloadItemAsync(rowIndex)` does not see after such a cancel. Call `base` to reload. |
| `GetComparerForColumn(columnIdx)` | When a column is sorted or searched in memory, and when a sortable column registers. | Supply a comparer for the column's values; the default is `Comparer<T>.Default` for the column's type, which requires `IComparable<T>` or `IComparable`. A sortable column whose values are neither is refused without one, even when the database sorts it, since the search on the sorted column compares in memory. |

### Paging stores rows on disk

With `ReadAll = false` (the default) the rows already read are kept in an `OnDiskChunkedList`: chunks of
`MinRowsToRead` rows (never fewer than 32 per chunk), of which 32 chunks stay in memory and the rest are written to disk as MessagePack files and read
back on demand. While the list is sorted, which an in-memory sort on a paged model does after every fetch, the resident chunks and the sorted output can be in memory together, so the budget may be doubled for the duration of the sort. Four things follow from that.

**The files are plaintext.** Whatever the query returns is serialized as is, unencrypted, into
`SpillDirectory` — by default the OS temp directory. They are deleted when the model is disposed or reloaded,
but outlive a process that ends while the model is alive. If the rows carry personal or otherwise sensitive data, point
`SpillDirectory` at a location with the right protection (an encrypted volume, a tmpfs, a per-tenant folder),
or trim the rows before they are stored: `RefineItemAsync(…, RefineItemPhase.AfterFiltering, …)` runs on each
row immediately before it is added, and `SingleItemReload` on a row read again after a post or a cancel, which is stored
as it returns it, so trim there too, but for what the in-memory filters read; a DTO projection in `BaseQueryFn` keeps unneeded columns out of the
result altogether. The files get the permissions the OS gives temporary files: on Windows the per-user temp
directory, on Linux owner-only, as `mkstemp` creates them. In a `SpillDirectory` the files are owner-only on Linux and macOS
too, and so is the directory when Glazor creates it; a directory that already exists keeps the permissions the host gave it,
and on Windows its ACL decides. A model that was not disposed releases its files when the
garbage collector finalizes them, so a forgotten `Dispose` delays the deletion rather than skipping it while the process runs. .NET runs no finalizer
when the process ends, so a process that stops, normally or not, with paged models still alive leaves their files behind. A dedicated `SpillDirectory` makes those easy to deal with: nothing else writes there, so the
host can empty it at startup, when no other process can be using it. A spill file is open only while a chunk is written or
read back, so an older process still running, under IIS overlapped recycling or a rolling deploy, or a second instance
sharing the folder, loses the chunks deleted under it, and its next read of one throws. Where that can happen, delete
only the files older than the process.

**`TGridItem` must be MessagePack-serializable.** The serializer is contractless and reads private members,
so a plain entity or record needs no attributes. It restores only what it can set: a setter, public or private, `init`, a
field of the row's own class, or a constructor parameter of the same name. A private field declared in a base class, or a
get-only auto-property no constructor parameter sets, comes back as its default, the key too: give such a member a
setter, even a private one. Every property that is not serializable, or that you do not
want on disk, needs `[IgnoreMember]`. Each row is written as `TGridItem`: a row of a derived type comes back from disk as
`TGridItem`, without its own members, and an abstract `TGridItem` cannot be written at all. A paged model over a type
hierarchy projects its rows to one concrete type. In the same way a member declared `object` keeps only what MessagePack's own
types hold: read back from disk, a `decimal` or a `Guid` in it comes back as a `string`, an enum as its number, a `DateTime` as
UTC and a class as a dictionary, so the same row shows differently once spilled, and a sort or a search in memory on such a
column throws when it compares a row read back with one still in memory. Give the members of a paged model's rows a concrete
type.

**The object graph must not contain cycles.** MessagePack does not detect reference cycles, so Glazor's serializer
options refuse a graph nested deeper than `MessagePackConf.MaxGraphDepth` (64) with a `MessagePackSerializationException`:
the fetch that spills the chunk fails, instead of the serializer overflowing the stack and terminating the process.
EF Core creates such cycles through relationship fix-up whenever a query `Include`s a collection whose elements navigate
back to their parent. Because the first chunk is written only when more than 32 chunks exist, this surfaces at roughly
row 33,000 with the defaults, not on the first page. Project to a DTO, or `[IgnoreMember]` the back-reference, or set
`ReadAll = true`.

**A row read back from disk is a new object.** A spilled chunk comes back as new instances, so a row the host keeps across
an eviction is a copy the list no longer holds: a change made to it afterwards does not reach the list, and the list's
`IndexOf`, `Contains` and `Remove`, which compare with `EqualityComparer<T>.Default`, no longer find it unless `TGridItem`
defines its own equality, as a record does. Find a row through the model, whose `RowIndexOf` compares keys when `ItemKeyFn`
is set. The list's enumerator reads by index and does not detect a change made while it runs.

### Requirements and gotchas

**Server-only.** `EFDataGridModel` runs on Blazor Server (interactive server render mode) and nowhere else.
It queries lazily from synchronous getters and during rendering, waiting on the query with a blocking wait
that the spinner covers; the single-threaded WebAssembly runtime does not allow blocking waits, so the model
cannot work there. For a WebAssembly page use `DataGridModel` over data you have already loaded.

**Give the grid a `Spinner`; a paged model needs one.** Fetches happen during rendering, and the grid blocks
briefly waiting for one; without a spinner the first render throws `ArgumentNullException`. The spinner is what
the user sees during those waits. It is provided by the application: allocated once in the main layout of an app
interactive from the root, and cascaded to the pages through an object that holds it, as
[Architecture › Spinner](Technical_Documentation.md#spinner) shows. A `<Spinner @ref>` in the same page as the
grid does not work, because the reference is still null when the grid first renders.

```razor
<Grigliaria TGridItem="AccessDTO" Model="_model" Spinner="@Gui.Spinner" ... />
```

**The model completes the order paging needs.** Reading by offset needs a total order, or pages can overlap or
skip rows, so a paged model orders its query by `ItemKeyExpr`: after your own `OrderBy` when the base query has
one, on its own when it has none, and after the column sort when that is pushed down. Order the base query only
for the order you want the rows in. The key goes right after your last `OrderBy` or `ThenBy` even when other calls
follow it, such as `Where`, `AsNoTracking`, `Include` or a `Select` to the same type. Two shapes are left as they are,
and their order must then be total by itself: a base query ordered before a projection from another type, and, on a grid
over a base type, a query over a derived type whose order other calls follow. EF drops an order that `Distinct`, `Concat`
or another set operation follows, so such a base query is ordered by the key alone: to see the rows in your order, order
after the set operation.

**Other users' writes move the offset.** The next window starts at the count of rows already read, so rows another user
inserts or deletes ahead of them shift it: as many rows are shown twice or never, and nothing detects it. A row shown twice
is in the list twice, and a search by key finds only one of the two. Only the model's own reloads keep the count right;
`ForceReload` reads again from the start. A page over a table others write while the user pages calls `ForceReload` before
reading on, or sets `ReadAll`, which reads every row in one query. Keyset paging, on the [roadmap](../ROADMAP.md), would
remove it.

**Prefer `EFProperty` on columns.** A `Property` column is filtered and sorted in memory. On an incrementally-read
model a filter still tests every row, as it is read: the model queries again until enough rows pass, so a filter that
admits few rows costs many queries. A sort covers only the rows read so far, sorted again as each window arrives, so
rows already on screen can move while the user pages; setting such a sort logs a warning. `EFProperty` pushes the
work into SQL, over the whole table.

**Filter values go as parameters.** A pushed-down filter sends its values as query parameters, so a value typed anew
reuses the query's plan. A set filter's selection is a collection, which EF 10 on SQL Server sends as one parameter per
value, and as one JSON parameter once the query would pass SQL Server's 2,100-parameter limit: leave
`UseParameterizedCollectionMode` at its default, since forcing a mode removes that switch.

**`AsNoTracking()` for DTO projections.** Nothing to track, and it avoids the change tracker growing across
fetches.

**Reload on parameter change instead of recreating.** Constructing a new model discards the user's filters,
sort and current row, and the grid shows its default columns again unless a memento was restored; the column sizes stay
with the grid. `ForceReload` re-queries and keeps them; pass `true` to look for the current row again, `false` when the changed
parameter makes it meaningless, as a different user does:

```csharp
if (_lastUserId == UserId && _model != null)
    return;
_lastUserId = UserId;
_model ??= new() { /* … */ };
_model.ForceReload(false);
```

**You own the model's lifetime.** The grid never disposes its model. `EFDataGridModel` is `IDisposable` and
holds its spilled chunks until disposed, so the component that creates it should implement `IDisposable` and
dispose it when it goes away:

```csharp
public void Dispose() => _model?.Dispose();
```

**The type-ahead assumes the database sorts text as .NET does.** On a sort pushed down to the database, the grid's
incremental search binary-searches the rows in the database's order with the column's comparer, `Comparer<string>.Default`
by default, which follows the current culture and mostly ignores hyphens and apostrophes. SQL Server's Windows collations
(`Latin1_General_…`) sort the same way. A `varchar` column under a SQL collation, such as `SQL_Latin1_General_CP1_CI_AS`,
sorts them as ordinary characters, so on names like `O'Brien` the search can land a few rows off. Override
`GetComparerForColumn` with a comparer that matches the collation.

**MessagePack's source generator comes into the host's build.** MessagePack depends on `MessagePackAnalyzer` with all its
assets, so a project that references `Glazor.EntityFrameworkCore` compiles with its source generator and analyzers; they act
only on the host's own MessagePack types. An `ExcludeAssets` on a reference of the host's own does not remove them; a target
in the host's project file does:

```xml
<Target Name="DropMessagePackAnalyzer" AfterTargets="ResolveLockFileAnalyzers">
  <ItemGroup>
    <Analyzer Remove="@(Analyzer)" Condition="'%(Analyzer.NuGetPackageId)' == 'MessagePackAnalyzer'" />
  </ItemGroup>
</Target>
```

### Reloading

`EFDataGridModel` implements `Glazor.Grds.Mdls.IReloadableGridModel<T>`, which is two members — a
synchronous *mark dirty* and an awaitable single-row re-read:

```csharp
_model.ForceReload(restoreCurrentRow: true);             // re-query on next sync
bool stillThere = await _model.ReloadItemAsync(rowIdx);  // re-read one row; false if it's gone
```

`ReloadItemAsync` takes an optional `CancellationToken`, which cancels its query.

For the user, `ReloadEnabled="true"` on the grid adds a footer button that calls `ForceReload(true)`.

Paging reads by offset, so a reloaded row that no longer belongs among the rows read would make the next window skip or
repeat one. A single-row reload takes care of it:

- **Out of the filter the database applies.** `EFDataGridModel` drops the row as it drops a deleted one, the current row
  going to the next one, read when it was not, and `ReloadItemAsync` returns `false`. `EditableEFDataGridModel` keeps it shown where it was, pinned until the next query: a
  filter or sort change, or `ForceReload`. A second query, run only when the first finds nothing, tells such a row from a
  deleted one. A posted new row the filter excludes is pinned where it was inserted. Under a sort the database applies, a
  pinned row can sit out of the sorted order, and until the next query the grid's type-ahead, a binary search, can land on
  a wrong row near it.
- **Out of a filter applied in memory,** on a `Property` column. The database still counts the row among those read, so it
  stays shown, and the editable model pins it. On the plain model, a row that has also moved in the order leaves
  the list, since the new query would drop it, and the current row goes to the next one.
- **Moved in the order the database pages by.** When the row's value in the pushed-down sort, in the base query's own
  `OrderBy`, or its key changed, the model calls `ForceReload(true)`. Only a new query knows the row's new place, and
  the current row follows it there, reading ahead as far as that. The base query's order is read from its expression,
  again after each `ForceReload`, so a host may switch it; one that reads more than the row's own members, through a
  navigation, after a projection or a join, or through a cast to a derived type, cannot be compared, and then every
  reload re-queries. The editable model compares with the values the row had when `EditAsync` started, since the form
  edits the list's own item. Without `EditAsync`, `ReloadItemAsync` compares with the list's item as it is: reload before
  changing that item in place, or edit through `EditableEFDataGridModel.EditAsync`.

## `EFFolderModel<TDbContext, TGridItem>`

A tree for `Treegliaria` over EF Core, where folders hold folders and leaves as in a file system. The roots are read in
passes of `MinRowsToRead` (512) as the grid pages, and a folder's children the first time it is expanded; they are kept,
collapsed or not, until `ForceReload`. A row's key is its own wherever it sits: a row may sit under several parents.

```csharp
_model = new EFFolderModel<AppDbContext, EntryDto>
{
    DbContextFactory = DbFactory,
    ItemKeyExpr = e => e.Id,
    IsFolderFn = e => e.IsFolder,
    RootsQueryFn = db => db.Entries.AsNoTracking().Where(e => e.ParentId == null)
        .Select(e => new EntryDto { /* … */ }).OrderBy(e => e.Name),           // the model adds the key after the OrderBy
    ChildrenQueryFn = (db, parent) => db.Entries.AsNoTracking().Where(e => e.ParentId == parent.Id)
        .Select(e => new EntryDto { /* … */ }).OrderBy(e => e.Name),
    ParentFn = async (db, child, ct) => await db.Entries.AsNoTracking().Where(e => e.Id == child.ParentId)
        .Select(e => new EntryDto { /* … */ }).FirstOrDefaultAsync(ct),         // for GoToAsync only
};
```

The key goes after your own order as on `EFDataGridModel` (see [Requirements and gotchas](#requirements-and-gotchas)): a
query ordered before a projection from another type is left as it is, and its order must then be total by itself for a level
read a page at a time.

The rows may be of different kinds, artists, albums and tracks say, projected to one DTO: give each kind's ids a range of its
own so that the key stays unique, and let `ChildrenQueryFn` switch on the parent's kind. The demo's EF tree page does so.

- **Loading.** The grid reads under its spinner, as for `EFDataGridModel`, and a getter called first reads on its own.
  The tree operations, `SetExpandedAsync`, `ExpandAllAsync`, `CollapseAllAsync`, `ZoomInAsync`, `ZoomOutAsync` and expanding
  a "More…" row, read nothing themselves: they change what is expanded or zoomed, raise `OnModelChanged` and are done at once,
  and the grid's next render reads what they need, under the spinner, as `ApplyFilters` and `SetSortedColumn` leave a read to
  it. Those that take a row, and `ZoomInAsync`, first build the rows when none are built, as after another operation called
  from host code with no render between: that build runs outside the spinner. A current row past the rows read, as a page
  down leaves it while the roots are read a page at a time, is read first, outside the spinner too, by those operations and
  by `ApplyFilters`, `SetSortedColumn`, `RemoveColumn`, `ForceReload` and `RestoreTreeState`, so that it stays on its
  record, as on `EFDataGridModel`. `GoToAsync` too reads only the ancestors,
  through `ParentFn`. `IsFolderFn` false makes a row a leaf, with no expander and no query. A folder not read yet shows an expander; once read and empty it shows none.
- **Filters** apply in memory to the rows read, every column alike. A folder stays shown, collapsed or not, while a row read
  below it matches; a folder never expanded has nothing read below it, so only its own values count.
- **Virtual folders.** `VirtualChildrenFn(parent)`, null for the roots, returns rows of your own shown first among that
  parent's children: folders that group others, an "Unfiled" one say, which no table holds. Their keys share the key space,
  and their children come from `ChildrenQueryFn` as any folder's do, which switches on them; `ParentFn` returns one as it is,
  for `GoToAsync` to pass through it. With no values of their own, a filter keeps one only while
  a row read below it matches.
- **Paged children.** `ChildrenPageSize` reads a folder's children that many at a time, all of them by default. Below a page
  the folder shows the row `MoreRowFn(folder)` makes, "More…" say, with a key of its own; expanding it reads the next page
  and makes its first row shown current, or leaves "More…" current when a filter shows none of the page. A page asks for one row more, so the "More…" row never shows below a folder read whole,
  and a filter keeps a folder while it has more to read.
- **Sort.** Each level is sorted in memory, siblings among themselves. A level read a page at a time, the roots or a folder's
  paged children, is sorted by the database when the sorted column is an `EFProperty`, so it stays paged; another column
  reads all of it, to sort it in memory.
- **The current row** is put back by its path after a change, an expansion, a filter, a sort or a reload, the levels on it
  read as many pages as it takes, or on its nearest ancestor still shown.
- **`ReloadItemAsync(rowIndex)`** reads the row again through its parent's query and its children when next shown;
  `false` means the row is gone from there, deleted or moved under another parent. **`GoToAsync(item)`** walks
  `ParentFn` up to the root, expands the ancestors, zooms out as far as it takes to show them and makes the row current: the next render reads their children again,
  down to the row, and a row not shown then, filtered out say, leaves the current row on its nearest ancestor shown.
  A row moved under a folder whose children were already read shows there only after `GoToAsync(item)` or `ForceReload`:
  the reload knows only the row's old place, and that folder keeps the children it read, collapsed and expanded again too.
- **Several parents.** A row that `ChildrenQueryFn` returns under two folders has a place under each, its path from the
  root, as in a tree of links: each place is expanded on its own and the current row stays on its own place, while the
  row's children are read once for both, and a reload updates it in both: each level that holds it is read again, as its
  own is, when it is in the database's order or read a page at a time. `GoToAsync` goes to the place `ParentFn` leads to.
  What finds a row by its item or key finds its first place, `RowIndexOf` and `SetCurrentItem`; and `OnStableItemChanged` does
  not fire between two places of the same row.
- **Expand all and zoom.** The model is an `IFolderGridModel`, its `IsFolderModel()` true. `ExpandAllAsync(rowIndex)` expands the folder and every
  folder below it, the next render reading every page of their children: a query per folder, so mind it over a large tree; -1 expands the
  whole tree, every root read. `CollapseAllAsync(rowIndex)` closes them, -1 all, but for the folder zoomed on, which stays expanded. `ZoomInAsync()` shows the current folder
  as the only root, expanded, at level 0, or on a leaf the folder it is in, unless that one is zoomed on already;
  `ZoomOutAsync(levels)` climbs back, all the way with 0. `GetZoomedItem()` is the folder zoomed on and `GetZoomLevel()`
  its depth, 1 for a root; null and 0 when not zoomed, for a page to show where the zoom is; `GetZoomPath()` gives the
  folders from the root down to it, as the last build found them, and `CanZoomIn()` whether `ZoomInAsync()` can act:
  false on a root leaf, or when the folder it would show is shown already. A zoom on a folder gone moves to its parent at
  the next build. `Treegliaria` binds `Ctrl`+`Shift`+`↓`/`↑` to expanding and collapsing all below the current row,
  `Ctrl`+`Shift`+`→` to zooming in and `Ctrl`+`Shift`+`←` to zooming out one level, and shows a bar above the header
  with buttons for them and, while zoomed, the path to zoom back out from; `ShowZoomBar` false hides it.
- **Not supported:** the search on the sorted column (the rows are not in one order) and editing. It is an
  `ITreeStateGridModel`: `Treegliaria`'s memento saves the folders expanded and the zoom, see
  [Architecture › Treegliaria](Technical_Documentation.md#treegliaria).

## Selection models

Used by `MultiSelGrid`, and independently wherever multi-selection is needed.

| Type | Behaviour |
| --- | --- |
| `SingleSelectionModel<TGridItem>`, `SingleSelectionModel<TGridItem, KT>` | One item at a time; the two-parameter form tracks it by key |
| `SetSelectionModel<TGridItem>`, `SetSelectionModel<TGridItem, KT>` | Many items, by the items' own equality (a record by value) or by key |
| `SetSelectionModelWithSelectAll<TGridItem>`, `SetSelectionModelWithSelectAll<TGridItem, KT>` | …plus a tri-state select-all |

```csharp
private SetSelectionModelWithSelectAll<Role, int> _selection = null!;

protected override void OnInitialized()
{
    _selection = new(allRoles, r => r.Id);
    foreach (var r in user.Roles)
        _selection.Select(r, true);
}
```

Built without a set, as here, a model starts from an empty one of its own. When you pass your own set, the required
`defensiveCopy` says whether the model shares it or works on its own copy:

- **`false`, shared:** read the set as you like, but change the selection only through the model (`Select`, `Clear`,
  `SelectAll`, `DeselectAll`). The model does not see a change made to the set directly, and `AllSelected()`, with it
  `MultiSelGrid`'s select-all box, can keep showing the previous state. A read-only set (immutable, frozen, a
  `ReadOnlySet`) is copied anyway, since the model could not select into it: read the selection with `Selected()`.
- **`true`, copied:** your set is left alone, and the selection is read with `Selected()`. The copy keeps the comparer
  of a `HashSet`, a `SortedSet`, an immutable or a frozen set; any other set is copied with the default equality. That
  comparer decides what is selected, and whether all is.

A `selectionEnabledFn`, the last argument of the constructors that take one, locks the rows it refuses: a locked row
keeps its state, selected or not. `Select` refuses to select it, and `MultiSelGrid` shows its box disabled and leaves it
alone on `Ctrl`+`Space` and on a double-click. `SelectAll` and `DeselectAll` change only the candidates, so a locked row
and a selected key that is not a candidate keep their state, and all is selected once every candidate is. Candidates
passed as items leave the locked rows out by themselves; passed as keys, they are taken as given, so leave out the keys
of the locked rows.

```razor
<MultiSelGrid @ref="_gridRef" Model="_rolesModel" SelectionModel="_selection"
              ReadOnly="@(!EditEnabled)"
              OnSelectionChanged="() => StateHasChanged()"
              VertResizer="@(GridVertResizers.RowHeight("height: 100%;", 2, 8))">
    <GridColumn Property="@(x => x.Name)" Name="Role" Label="Role" />
</MultiSelGrid>
```

> [!NOTE]
> `OnSelectionChanged` is an `Action`, not an `EventCallback` — it will not re-render your component for you.
> Call `StateHasChanged()` from the handler, as above.

## Wrappers

Custom models derive from `AGridModel<T>`, which owns column registration, or from one of the wrappers below.
`IGridModel<T>` itself is the contract the grid consumes and is not meant to be implemented directly: its column
registration members rely on internals of `GridColumn`, so a direct implementation cannot be completed outside
the library.

`GridModelWrapper<T>`, `DataGridModelWrapper<T>` and `TreeGridModelWrapper<T>` are pass-through decorators with
protected constructors: they are not instantiated directly. Derive from one to intercept part of a model's behaviour without reimplementing it — adding a computed column,
filtering rows out, or adapting a model to a different shape. `Treegliaria` uses this internally to add its expansion
column; `MultiSelGrid`'s wrapping grid model implements the interfaces directly. `OnModelChanged` reports the wrapped model's
changes; a wrapper whose own rows change, one that filters them on a switch of the page say, raises the change with
`FireModelChanged`, as a model derived from `AGridModel<T>` does.

## A paged model of your own

A source other than EF Core, a web service or Dapper, is paged by a model derived from `AGridModel<T>` that implements
`IDataGridModel<T>` and `IAsyncGridModel<T>`. The grid drives it through a few members:

- **`RequiresFetch()`** returns true, so the grid calls `FetchRowsAsync` before it renders a page.
- **`FetchRowsAsync(rowIndex, count, ct)`** makes the rows from `rowIndex` to `rowIndex + count` readable and returns how
  many of them exist. It returns fewer only at the end of the data, and from then on `GetRowCount()` must be exact: the
  grid fetches the page again once, and throws if the second fetch comes back short too. It runs off the circuit's
  dispatcher while the render waits for it, so it must not reach a component (`InvokeAsync`), and it stops on `ct`,
  which the spinner's budget cancels.
- **`GetRowCount()`** returns `int.MaxValue` while the count is not known, and the exact count once a fetch has found the end.
  `GetCurrentRowCount()` and `GetDataView()` hold the rows read so far.
- **`GetItem(rowIndex)`** throws `ArgumentOutOfRangeException` for a row not read yet, as the built-in models do; the grid
  takes an `IndexOutOfRangeException` too. With
  `IAsyncGridModel<T>` the grid's cell shows `...` for it instead of failing the render.
- **`GetDataVersion()`** changes when a fetch adds rows, and **`SetCurrentRow`** raises the change with
  `FireModelChanged(GridModelChangeType.Other)`, as the built-in models do.

The grid needs a `Spinner` with such a model, as with the EF ones. A model that cannot sort, filter or search says so, and
the grid hides those controls:

```csharp
public class OrdersModel(IOrdersApi api) : AGridModel<Order>, IDataGridModel<Order>, IAsyncGridModel<Order>
{
    private const int ReadSize = 100;
    private readonly List<Order> _rows = [];   // the rows read, from the first on
    private bool _end;
    private int _version, _current = -1;

    public override bool RequiresFetch() => true;

    public override async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
    {
        while (!_end && _rows.Count < rowIndex + count)
        {
            IReadOnlyList<Order> read = await api.GetOrdersAsync(_rows.Count, ReadSize, ct);
            _rows.AddRange(read);
            _end = read.Count < ReadSize;
            ++_version;
        }
        return Math.Clamp(_rows.Count - rowIndex, 0, count);
    }

    public override int GetRowCount() => _end ? _rows.Count : int.MaxValue;   // not known until a fetch finds the end
    public override int GetCurrentRowCount() => _rows.Count;
    public override IReadOnlyList<Order> GetDataView() => _rows.AsReadOnly();
    public override Order GetItem(int rowIndex) => _rows[rowIndex];   // past the rows read it throws, and the cell shows "..."
    public override int GetDataVersion() => _version;
    public override Func<Order, object>? GetItemKeyFn() => o => o.Id;

    public override int GetCurrentRow() => _current;
    public override void SetCurrentRow(int rowIndex)
    {
        if (rowIndex == _current)
            return;
        ArgumentOutOfRangeException.ThrowIfLessThan(rowIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rowIndex, GetRowCount());
        _current = rowIndex;
        FireModelChanged(GridModelChangeType.Other);
    }

    // No sort, filter or search
    public bool IsSortable() => false;
    public void SetSortedColumn(string? columnName, bool ascending)
    {
        if (columnName != null)
            throw new NotSupportedException();
    }
    public string? GetSortedColumn() => null;
    public SortType GetSortType() => SortType.None;
    public bool CanSearchOnSortedColumn() => false;
    public ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default) => throw new NotSupportedException();
    public bool IsFilterable() => false;
    public void ApplyFilters(IEnumerable<KeyValuePair<string, AFilterDef>> filters)
    {
        if (filters.Any())
            throw new NotSupportedException();
    }
    public IReadOnlyDictionary<string, AFilterDef> GetFilters() => new Dictionary<string, AFilterDef>();
    public bool IsFiltered() => false;
}
```

A model that sorts follows the sortable-column rule as the built-in ones do: it overrides `ValidateStoredColumn` and calls
`PocoColumnComparer.CheckCanSort` for each sortable column, so a column whose values cannot be compared fails when it
registers rather than at the first sort.

Columns are added in the constructor with `AddColumn`, or in markup with `GridColumn` and `Property`. The editing and
in-memory filter support the EF models use is not open yet; the roadmap lists it. `CanDoContainsSearch` and
`ContainsSearchAsync` have default implementations that say the model does not support the search, so the sample leaves
them out.
