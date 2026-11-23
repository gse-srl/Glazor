# Glazor models

Contents:
- [In-memory: `DataGridModel<T>` and the `AList` surface](#in-memory-datagridmodelt-and-the-alist-surface)
- [`EditableDataGridModel<T>` and the edit lifecycle](#editabledatagridmodelt-and-the-edit-lifecycle)
- [`EFDataGridModel<TDbContext, T>` configuration](#efdatagridmodeltdbcontext-t-configuration)
- [Shaping what an EF model reads](#shaping-what-an-ef-model-reads)
- [Reloading](#reloading)
- [Selection models](#selection-models)
- [Wrappers](#wrappers)
- [Sync, and why getters can block](#sync-and-why-getters-can-block)

Namespaces: `Glazor.Grds.Mdls` and `Glazor.EntityFrameworkCore`; the latter ships in the package of the same name, which the host must reference for the EF models. Hierarchical models are in `trees.md`.

## In-memory: `DataGridModel<T>` and the `AList` surface

The default, and by a wide margin the most common. The item type must be a reference type (`where T : class`); the same holds for the value type of the set filters, `SetDef<VT>`, `DynaSetDef` and `DynaValuesInSetDef`: over a column that holds a value type, use a `DistinctDef` such as `EFDistinctDef`; over a collection of value-type items, ids for instance, use `EFValuesInSetDef`, or derive from `ADynaValuesInSetDef`.

```csharp
private readonly DataGridModel<Role> _model = new() { ItemKeyFn = r => r.Id };

// later
_model.GetData().Set(roles);
```

`GetData()` returns an `AList<T>` — a mutable list that marks the model as changed, so the next render re-applies
filters and sorting. It does not render anything by itself: after changes made outside a UI event, call `StateHasChanged()`
when the grid has to refresh at once.

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
foreach (var r in data.AsEnumerable()) { }
```

Or seed through the constructor: `new DataGridModel<Product>(products)`.

`GridModel<T>` adds ordered-list semantics, most usefully row reordering via
`AreRowsSwappableFn = (model, from, to) => true`. `MultiSelGrid` asks it at every render, only about the current row and
an existing neighbour, so the function can read both rows by index.

Positioning helpers, which you reach for when opening a dialog on a particular row:

```csharp
_model.SetCurrentItem(item);
int idx = _model.RowIndexOf(item);
if (idx >= 0) _model.SetCurrentRow(idx);
_model.ApplyFilters([]);   // clear all filters
_model.SetSortedColumn("descr", ascending: true);
```

`RowIndexOf` scans, with a fast path for the current row. That is inherent — membership on an unindexed list
is O(N) — and fine at in-memory sizes. On a paged EF model it covers only the rows loaded so far: a row not yet
fetched yields -1. The scan reads no further, though like any getter it runs the sync the model has
pending, the first query included.

## `EditableDataGridModel<T>` and the edit lifecycle

Constructed with the callbacks it needs to re-read data, because after a post it has to see what the server
actually stored:

```csharp
_model = new EditableDataGridModel<Mapping>(
    reloadAllFn: async () => await LoadAllAsync())
{
    ItemKeyFn = m => m.Id,
};
```

```csharp
Mapping draft = await _model.EditAsync();                          // begin editing the current row
Mapping draft = await _model.InsertAsync(proto, afterCurrentRow: true);
await _model.PostAsync();                                          // commit; triggers reload
await _model.CancelAsync();                                        // discard

bool editing   = _model.IsEditPending();
bool inserting = _model.IsInsertPending();
bool canEdit   = _model.IsCurrentRowEditable();
bool frozen    = _model.IsCurrentRowFrozen();
```

Between `EditAsync`/`InsertAsync` and `PostAsync`/`CancelAsync` the row is frozen: moving to another row, searching,
filtering, sorting, `RemoveColumn` on a sorted or filtered column, `ForceReload` and `ReloadItemAsync` throw
`InvalidOperationException`. Check `IsCurrentRowFrozen()` before calling them, and disable toolbar buttons on it. While
an insert is pending, the row being inserted is
the current row, before or after the old one; `CancelAsync` gives the old one back. It is not stored until `PostAsync`:
`GetDataView()` leaves it out, one row shorter than `GetCurrentRowCount()`, so read rows by position with `GetItem()`.
`PostAsync` finds the new row by its key: give the item the key it was stored under, a generated identity included, before
calling it. The sequence is `InsertAsync(proto)`, then your `SaveChanges`, then `proto.Id = entity.Id`, then `PostAsync`,
so the key member of the row type needs a setter.

`PostAsync` and `CancelAsync` take an optional `CancellationToken`, which cancels the read that follows; the edit or the
insert ends all the same. An in-memory model has no token to give the reload functions: a token already cancelled
stops the model before it calls them.

A save that fails in your own code, before `PostAsync`, leaves the edit or insert pending. An error the user can correct, a
validation one, goes on its field with the form still open. A `DbUpdateException` goes to the application's error
dialog, one of your own, as the demo's `GuiHelper.Error(ex, msg)` is: it logs the exception under a new error id and shows
the id. The edit stays pending, since the row in the database did not change and the user can correct
the input and save again; only a `DbUpdateConcurrencyException`, whose row is stale, ends it with `CancelAsync`. Once `PostAsync` or `CancelAsync` is called the edit ends even when its reload throws, so
there is nothing pending to retry with. The default row heading shows a pen while an edit is pending, an asterisk while
an insert is.

**`EditAsync` hands back the live model reference, not a copy.** There is no snapshot anywhere, so
`CancelAsync` cannot restore anything itself — it calls your reload function and trusts it to return pristine
data. If `reloadAllFn` hands back the same mutated objects, cancel silently keeps the edits.
Re-read from the store, or snapshot yourself. The model finds the row again by the key it had when the edit started,
so a cancelled change to a key member brings back the same row; after such a change an in-memory model calls
`reloadAllFn` rather than your single-row reload, which reads the row the item names.

Even when editing happens in a **dialog** — so nothing is edited inline and a plain `DataGridModel` would do —
prefer `EditableDataGridModel`: everything is then ready if inline editing comes later. The pattern is: `SetCurrentItem` → `EditAsync` → show
the dialog → on submit write to the database and `PostAsync`, on dismiss `CancelAsync`. The freeze is what
stops the user navigating the grid out from under an open dialog.

The same works with a **panel under a paged grid**, on `EditableEFDataGridModel`, and the panel edits the row the model
hands out, as the model expects:

- The form binds to the row `EditAsync` or `InsertAsync` returns, the one in the grid's list. The projection carries
  what the save writes, the foreign keys beside the names shown, and the row type is a class with setters: a `record`
  compares by value, so its equality would follow the edits.
- The save writes that row back as read: it builds the entity from the row and calls `Update`, or `Add` for an insert,
  without reading the entity again. A concurrency token in the row, a version column, is then compared with the value
  the edit started from; read again at save time it would hold the current one, and another user's change would be
  overwritten without an error. `Update` writes every column, so a column the row does not carry would be cleared:
  project them all, or `Attach` the entity and mark only the edited properties modified. Without a token, a row
  another user deleted still fails, as an update that touches no row.
- `DbUpdateConcurrencyException` goes to `CancelAsync`, which re-reads the row: it shows what the other user saved,
  with the new version to edit from, or leaves the grid when they deleted it. The user's own Cancel does the same, and
  the form's changes go with the edited object, which the model replaces.
- `OnStableItemChanged` points the panel at the current row while nothing is pending, so it follows the user's
  navigation; with an edit pending the panel keeps the row being edited.
- `bool Editing => _model.IsEditPending() || _model.IsInsertPending();` drives the panel: it disables the form when
  false, and the grid's own edit buttons and the panel's New button when true, since the row cannot move.
- `OnDblClick` sets the current row and calls `EditAsync`; a `KeyAction Key="Escape"` bound to the panel's `div`,
  `Enabled="@Editing"`, cancels; a flag set on `EditAsync` and read in `OnAfterRenderAsync` moves the focus to the first
  field.

```csharp
private async Task SaveAsync()
{
    try
    {
        await using AppDbContext db = await DbFactory.CreateDbContextAsync();
        Order order = _row.ToEntity();   // the row as read, its Version included
        if (_model.IsInsertPending())
            db.Orders.Add(order);
        else
            db.Orders.Update(order);
        await db.SaveChangesAsync();
        _row.Id = order.Id;   // a new row's generated key, by which PostAsync finds it again
        await _model.PostAsync();
    }
    catch (DbUpdateConcurrencyException e)
    {
        await CancelAsync();   // re-reads the row: the other user's values and version, or none if it was deleted
        await Gui.Error(e, "Another user changed or deleted the order: it is shown as the database has it now.");
    }
    catch (DbUpdateException e)
    {
        await Gui.Error(e, "The database refused the change, which was not saved.");   // still pending: correct and save again
    }
}
```

### Editing in the grid itself

Instead of a panel, the grid can edit the row in place: `EditMode="EditMode.Manual"` on a `Grigliaria` over an editable
model, or `OnClick` / `OnMove` to edit the row a click, or also the arrows, reach, posting the one left first. F2 or a
double-click edits the current row, Ctrl+Ins inserts the row `NewItem` makes, Enter or the row heading's check posts,
Esc or its cross cancels. A column edits when its `EFProperty` or `EditProperty` is a plain property with a setter
(`EditProperty` alone also reads the value), unless `Editable="false"`; `EditTemplate` draws its own editor. The row is
validated with DataAnnotations, each message in its editor's upper left corner. The save is the same code as above, in
`OnPost`, which gets the row as `post.Item` and `post.Inserting`, and `post.AddError(property, message)` for an error
the database reports on a field. Ctrl+Del hands the current row to `OnRemove`, which deletes it and drops it from the
model (`GetData().RemoveAt` in memory, `ReloadItemAsync` on an EF model). See the "Editing in place" section of
docs/grigliaria.md.

## `EFDataGridModel<TDbContext, T>` configuration

| Member | Type | Default | Notes |
| --- | --- | --- | --- |
| `DbContextFactory` | `IDbContextFactory<TDbContext>` | — | **Required.** A factory, not a context: the model creates a short-lived context per query |
| `BaseQueryFn` | `Func<TDbContext, TGridItem?, IQueryable<TGridItem>>` | — | **Required.** Filters and sorting compose onto whatever you return. The second argument is non-null only when refreshing a single entity, which is where you would detach it |
| `ItemKeyExpr` | `Expression<Func<TGridItem, object>>` | — | **Required.** An expression, so the key can take part in queries. A plain key must not be null; members of a composite key may be |
| `ReadAll` | `bool` | `false` | `false` reads incrementally; `true` materializes everything up front |
| `MinRowsToRead` | `int` | `1024` | Rows per fetch, clamped to ≥10. Larger means fewer round trips, more memory. Only meaningful when `ReadAll` is `false` |
| `SpillDirectory` | `string?` | `null` (OS temp) | Where the paged model writes the chunks it evicts from memory. Created if missing. Only meaningful when `ReadAll` is `false` |
| `AlwaysRestoreCurrentPK` | `bool` | `true` | Re-find the current row by key after a reload, querying more rows if it must; a row the database's filters exclude is not looked for, one only an in-memory filter excludes is looked for to the end |
| `CreateItemsListFn` | `Func<int, ValueTask<ISortableList<TGridItem>>>?` | `null` | Supply the backing list — the way to opt out of on-disk spilling, with a `new SortableList<TGridItem>(size)` |

## Shaping what an EF model reads

`EFDataGridModel` has four `protected virtual` hooks you would normally reach for, and knowing the order
they run in is most of understanding them. For a page of rows:

```
dbContext = DbContextFactory.CreateDbContextAsync()                 one short-lived context for the whole fetch
CompleteQueryable(dbContext, query, request)                        compose filters + sort, then:
    FilterQueryable(dbContext, query, filterDefs)                       the filters the database applies
    RefineQueryBeforeExec(dbContext, singleItemReload: false, query)    your last touch, before paging
    query.Skip(rowIndex).Take(count)                                    paging, unless readAll
rows = await query.ToListAsync()                                    the page is read in full, first
    foreach (row in rows):                                          ← so the context is free to be queried
        RefineItemAsync(dbContext, item, BeforeFiltering, ct)
        InMemoryFilter.Includes(item)                               ── rejected rows stop here
        RefineItemAsync(dbContext, item, AfterFiltering, ct)            ── survivors only
        → added to the backing list
```

and for a single row being re-read by `ReloadItemAsync`, which goes through the overridable
`ReloadItemAsync(rowIndex, pk, ct)` on every reload, a cancelled edit of key members included:

```
dbContext = DbContextFactory.CreateDbContextAsync()
query = FilterQueryable(dbContext, BaseQueryFn(dbContext, item).Where(key), filterDefs)
RefineQueryBeforeExec(dbContext, singleItemReload: true, query)
(an internal dependency read)                                    ── if it comes back null the row is removed
RefineItemAsync(dbContext, item, SingleItemReload, ct)
```

**`FilterQueryable`** is where to change how a filter is translated, or which rows it admits: the paged query and the
single-row reload both go through it, so they agree on which rows pass and the paging offset stays right. Overriding
`CompleteQueryable` for that instead leaves the reload deciding with the definitions' own predicates.

**`RefineQueryBeforeExec`** is the query-side hook: the `Where` clauses and the `OrderBy` are already applied and the
`Skip`/`Take` is not yet, so a projection here still pages correctly. This is where
you drop a heavy column from the projection that had to stay queryable in order to be filtered on — project
into a DTO that carries `HasBlob = !string.IsNullOrEmpty(x.Blob)` and leaves `Blob` null, and the column
stays filterable while the bytes never leave the database. The `singleItemReload` flag tells you which of the
two paths is asking, and the `DbContext` is the one the query is about to run on.

Between `RefineQueryBeforeExec` and `RefineItemAsync` on the reload path the model does its own dependency read, and if
that comes back empty the row is dropped and `ReloadItemAsync` returns `false` — which is how a caller learns the row is
gone. That step is internal rather than an extension point: to shape a reloaded row, `RefineItemAsync` is called
immediately afterwards with `SingleItemReload`, and it receives the `DbContext`, which is everything such
work usually needs.

**`RefineItemAsync`** is the item-side hook, and the phase is the part worth reading twice:

| phase | runs for | good for |
| --- | --- | --- |
| `BeforeFiltering` | **every** row the query returned | supplying whatever the in-memory filters need to read |
| `AfterFiltering` | only the rows that survived | anything expensive, and trimming what will be held |
| `SingleItemReload` | one row re-read on its own, through the filters the database applies, or without them on the editable model when they no longer admit it | the same shaping, on the reload path; the in-memory filters may then read the row |

Two consequences of that split. `BeforeFiltering` runs for rows that are about to be discarded, so it should
stay cheap — it exists so an in-memory filter can see a value the query did not project. And what an item still
carries at `AfterFiltering` is what goes into the backing list, which on a paged model is the
`OnDiskChunkedList` — so trimming there genuinely reduces what gets MessagePack-serialized when a chunk spills
to disk. That is the case the hook was written for. A row re-read on its own skips `AfterFiltering` and is stored as
`SingleItemReload` returns it, so trim there too, but for what the in-memory filters read.

**The `DbContext` it hands you is safe to query.** That is why the page is materialized with `ToListAsync`
before the loop rather than streamed: a second query on a context whose reader is still open throws
*"A second operation was started on this context…"*, which would make the parameter useless. The paged query already
has `Skip`/`Take` applied, so the list holds the rows one fetch asks for, at least `MinRowsToRead`, and all of them at
once before they reach the backing list: a fetch that asks for many, End with `GotoLastKnown` false or the re-read after
a sort, a filter change or a reload deep in a table, holds them all while it runs. Under an in-memory filter every pass
asks for the whole count again, which keeps the queries few and can read and keep up to one pass more than needed. Under
`ReadAll` the list is the whole result, read in full anyway.

## Reloading

`EFDataGridModel` implements `Glazor.Grds.Mdls.IReloadableGridModel<T>`, which is two members:

```csharp
_model.ForceReload(restoreCurrentRow: true);              // re-query on next sync
bool stillThere = await _model.ReloadItemAsync(rowIdx);   // re-read one row; false if it is gone
```

`ReloadItemAsync` takes an optional `CancellationToken`, which cancels its query.

`ReloadEnabled="true"` on the grid gives the user a footer button that calls `ForceReload(true)`.

Pass `restoreCurrentRow: false` when the reason for reloading means the old current row is meaningless — a
different parent record selected, say.

A reloaded row that no longer belongs among the rows read would shift the paging offset, so `ReloadItemAsync` handles it.
One out of the filter the database applies is dropped by `EFDataGridModel`. `EditableEFDataGridModel` keeps it shown where
it was, pinned until the next query, and pins a posted new row the filter excludes where it was inserted. One whose value
in the order the database pages by changed (the pushed-down sort, the base query's own `OrderBy`, or the key) makes the
model call `ForceReload(true)`, and the current row follows it to its new place; on `EFDataGridModel`, one a filter
applied in memory now excludes leaves the list instead, since the new query would drop it, and the current row goes to the
next one. An order the model cannot read on the row
itself, through a navigation, after a projection or through a cast to a derived type, makes every reload re-query.
The move is told by comparing with the row as it was: the editable model records it at `EditAsync`, but without
`EditAsync` the list's item is compared as it is, so reload before changing that item in place.

Rows another user inserts or deletes ahead of the rows read are not seen: they shift the offset, and the next window shows
as many rows twice or never. Over a table others write while the user pages, call `ForceReload` before reading on, or set
`ReadAll`.

`ForceReload` does **not** fire the model-changed notification, so if you drive the model directly rather than
through a grid, re-render yourself.

## Selection models

Used by `MultiSelGrid`, and independently wherever multi-selection is needed. Each comes in a plain and a
keyed arity.

| Type | Behaviour |
| --- | --- |
| `SingleSelectionModel<T>`, `SingleSelectionModel<T, KT>` | one item at a time |
| `SetSelectionModel<T>`, `SetSelectionModel<T, KT>` | many items, by the item or by its key |
| `SetSelectionModelWithSelectAll<T>`, `SetSelectionModelWithSelectAll<T, KT>` | …plus a tri-state select-all |

```csharp
SetSelectionModelWithSelectAll<Role, int> sel = new(allRoles, r => r.Id);
foreach (var r in user.Roles)
    sel.Select(r, true);

IEnumerable<int> ids = sel.Selected();
int n               = sel.SelectedCount();
bool all            = sel.AllSelected();
sel.Clear();
```

**Passing your own set, say whether the model shares it.** Built without a set, as above, the model starts from an
empty one of its own. A constructor taking a set requires `defensiveCopy`: with `false` it shares it, and you change the
selection only through the model: `set.Clear()` or `set.Add(...)` behind its back is not seen, and `AllSelected()`,
cached on the model's own changes, keeps reporting the previous state. A read-only set (immutable, frozen, a
`ReadOnlySet`) is copied anyway, since the model could not select into it. With `true` it works on its own copy, keeping
the comparer of a `HashSet`, a `SortedSet`, an immutable or a frozen set; your set is left alone and the selection is read
with `Selected()`.

The keyed constructor is `(items, keyFn)`, or `(items, keyFn, selectedKeys, defensiveCopy)` with a set of yours, which
has an overload taking a fifth `Func<T, bool> selectionEnabledFn` to lock some rows. A locked row keeps its state,
selected or not: `Select` refuses to select it, `SelectAll` and `DeselectAll` change only the candidates, and
`MultiSelGrid` disables its box and ignores `Ctrl`+`Space` and a double-click on it. A selected key that is not a candidate
keeps its state too. Each constructor takes the candidates as items or as keys. When the candidates are passed as items the
snapshot is taken after that filter, so locked rows are excluded from "all selected" too; when they are passed as
keys the snapshot is what you pass, filtered by nothing, so leave the locked rows' keys out or select-all selects them.

The constructor snapshots its candidate set. **Rebuild the model when that set changes** rather than mutating
the collection you passed in: initialise the field with `Enumerable.Empty<T>()` and replace the whole model once the
real rows arrive.

## Wrappers

`GridModelWrapper<T>`, `DataGridModelWrapper<T>` and `TreeGridModelWrapper<T>` are pass-through decorators with
protected constructors — derive from one to intercept part of a model's behaviour without reimplementing it: adding a
computed column, filtering rows out, adapting a model to a different shape. `Treegliaria` uses this internally to add
its expansion column, or to fix the `TreeColumn` that replaces it; `MultiSelGrid`'s wrapping grid model implements the
interfaces directly. `OnModelChanged` reports the wrapped model's changes; a wrapper whose own rows change raises the
change with `FireModelChanged`, as an `AGridModel<T>` does.

## Sync, and why getters can block

Models are lazy. Filters, sorting and pending reloads are applied on the next **sync**, which the grid triggers
as part of rendering — you rarely call `Sync()` yourself. `GetDataVersion()` increments every time a sync
re-applies filters, sorting or a reload, whether or not the resulting view differs: treat a change in it as "the view
may have changed", not as proof that it did.

A model is not thread-safe: use it only on the circuit's dispatcher, as the grid does. From a timer, a background task or
any callback that arrives on another thread, reach it through the component's `InvokeAsync`, then call `RepaintAsync()` or
`StateHasChanged()`.

Laziness is deliberate: a grid that is never rendered never queries. The consequence for an EF-backed model is
that the first getter to fire during render is what triggers the query, on the renderer's dispatcher where
nothing can be awaited — so it blocks. That is what the spinner covers, and why `Spinner` is effectively
required there.

The cost is per *new data window*, on a user action, not per render. Measured over 5,000 rows with
`MinRowsToRead` 100 and `PageSize` 20: first paint one query; an idle re-render none; paging inside rows
already read none; paging beyond them one query per `MinRowsToRead`; walking to row 600 and back, none. So
concurrency scales with users *actively paging*, not users connected — and it is one more reason most grids are
better off in memory when the data fits.
