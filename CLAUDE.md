# Glazor — notes for AI-assisted work

Read this before proposing changes: several things here look like bugs and are not. Where the docs or a test state a rule,
the entry points there for the detail.

## Deliberate design decisions — do not "fix" these

**`OnDiskChunkedList.CopyTo` throws on purpose.** A real implementation must materialize every row
into the caller's array, which is exactly the memory blow-up the class exists to prevent. A loud
failure is preferred. Consequence: `GetDataView().ToList()` / `.ToArray()` throw
`InvalidOperationException` on a paged EF model — enumerate or index it instead. Documented on
`IGridModel.GetDataView()`.

**`FilterDefFactory` resolves persisted type names via `Type.GetType`.** `AFilterDef` is an open
hierarchy: a library user must be able to add their own definition and have it persist and restore
with **no registration step**. A discriminator registry was tried and rejected because it trades that
away; auto-registration via assembly scanning is worse (trim/AOT-hostile, blind to unloaded
assemblies, cannot discover closed generics). The `AFilterDef` assignability check before
`Activator.CreateInstance` is the guard. Persisted names are stripped of assembly version/culture/PKT
so a rebuild does not invalidate stored layouts.

**`EFDataGridModel.SyncImpl()` queries lazily, and that is a tested contract.** `EFCoreTests` asserts
`GetRowCount() == 3` on a freshly built model with no fetch call — host code using the model without a
grid relies on a getter loading data. Do not make `SyncImpl` non-blocking; if the render path is ever
reworked, prefetch *ahead of* the render and leave `SyncImpl` alone. Prerequisite: a non-fetching
resident-row count on `IGridModel` (`GetCurrentRowCountNoSync()` exists but is `protected`).

**`SpinnerHelper.Monitor` blocking the circuit is known, and it costs less than it looks.** The wait is
bounded (~70 s, then `TimeoutException`), and killing the circuit is the chosen outcome over degrading.
The spinner is closed before the throw specifically because a dying circuit does not tear down the DOM,
and a `showModal()` dialog under `closedby="none"` would otherwise cover Blazor's own Reload link.

Before concluding that the blocking wait freezes the UI, read the numbers `PagedGridCostTests` measures: the render
thread pays **once per new data window, on a user action the spinner already covers**, never on an idle re-render or on
rows already read, and paging past what was read costs one query per `MinRowsToRead`, not one per page. Exposure scales
with users *actively paging*, not users connected. The residual concern is only that a genuinely slow query holds the
circuit for its duration.

**`AGridModel.RowIndexOf` scanning is expected.** Membership on an unindexed list is O(N), 
for paged models it acts on loaded data only. A
current-row fast path exists for the common case. The selection stabilizer is debounced, so it runs
once per settled selection, not per keystroke.

**`ADataGrid._defaultTouchScrollerEnabled` static cache is fine.** `IConfiguration` is app-scoped, so
which grid reads `Grigliaria:TouchScrollerEnabled` first is irrelevant.

**`TempFile` using `Path.GetTempFileName()` is fine.** The 65,535-files cap a reviewer will
remember is a .NET Framework / Windows limitation: per the API docs it does not exist on *any*
OS from .NET 8 onward, and this targets net10.0. Do not propose `Guid`-based names or
`FileOptions.DeleteOnClose` on that basis.

## Decisions taken in review — do not propose the opposite

These were settled with the maintainer during the release reviews; nothing in the code says so.

**`EFDataGridModel` resets after a failed reload.** `SyncImpl` clears the memento before the query that
consumes it. If that query throws or is cancelled, the model comes back without its current row and its
memento; filters and sort survive because they are plain fields, and rows a query appended before failing stay, the
next sync continuing from the offset they reached; a `ReadAll` query has no offset, so it drops them and reads every row
again. This is wanted, so that no state of unknown validity is
carried into the next sync. Do not re-arm the memento, keep the current row, or "resume where the failure
stopped".

**A post or a cancel whose reload fails still ends the edit.** `EditSupport` clears the pending edit or insert before it
reloads, so when the reload throws (the database is gone, `RefineItemAsync` throws) the exception reaches the caller, nothing
is left pending, the row shows what was in memory, and an inserted row stays in the list. The next edit starts as usual.
This is wanted, as for the reset of `EFDataGridModel` above: no state of unknown validity is kept to retry with.
`EditableModelTests` pins it. Do not propose keeping the edit pending after a failure, or retrying the reload.

**`DataGridModel` stays in sync after a failed sync.** `SyncImpl` marks itself in sync, bumps the version and clears the item
to restore before it filters and sorts, so a getter or a filter that throws leaves the partial view in place until the next
change invalidates it. This is wanted: a sync retried on every later render would have side effects on rendering. Do not
propose a catch that marks the model out of sync again.

**No catches for `JSDisconnectedException` / `TaskCanceledException` in render paths.** When the circuit is
gone nothing renders anyway; a catch in `OnAfterRenderAsync` only changes what is logged. Disconnects are
handled in `DisposeAsync`, which every component does. Do not file such catches as robustness findings and do
not add them while fixing something else. The one exception is in logging only: `Popup` and `DialogBox` keep a disconnect
out of the error their `EnsureModuleIsLoaded` logs, with a filter on its catch, and still rethrow it.

**Stale persisted layouts are tolerated in one place.** `ADataGrid.RestoreMemento` prunes column names that no longer
exist from the layout, and sets aside their filters and sort with a warning, because a memento outlives the code that
produced it; they are applied only if such a column registers later, as markup columns do after an early restore.
`SetLayout` and `SetColumnsToShow` are programmer input: an unknown column there throws.
In the same way a restore into a grid that is not layoutable ignores the memento's layout and keeps its filters and
sort, while `SetLayout` with a layout there throws. `GridLayout.columnsToShow` is never legitimately null.

**While the layout dialog is open, only the dialog changes the grid.** The dialog applies each change as the user makes it,
and its Cancel, or Esc, puts back the layout it started from, with what a restore set aside for columns not registered.
Filters applied from the dialog's filter link take effect at once and stay after Cancel: Cancel puts back the layout
only. Do not propose restoring the filters on Cancel. So until it closes the host must not change the grid: not
`Layoutable`, not the columns (an `@if` or a `@foreach` over them), not the model. Keeping that rule is the host's job. Do
not report what happens when a host breaks it, such as Cancel after `Layoutable` turned off, or a column leaving while the
dialog is open.

**`SetLayout(null)` puts back the default layout.** On a layoutable grid it replaces the layout with an empty one, so every
size set so far goes and each column takes its own `PreferredSize`, and it shows the fixed columns, then those
`ShownByDefault` with a size. It is a layout change: a restored
memento keeps it, so a later column change does not bring the restored layout back, `OnLayoutChanged` is raised and the
grid re-renders. Filters, sort and the current row are left alone. A memento without a layout restores the same way, and
so does each column change after it until the user arranges the columns. On a grid that is not layoutable it does nothing,
does not throw and does not make the grid layoutable. Do not propose reading null as "keep the current layout".

**`SetLayout` keeps the sizes of columns not registered.** Only `columnsToShow` is checked; a size for a column that left
under an `@if` comes back with it, and `SetLayout(GetLayout())` never throws for one. Do not propose a check or a prune.

**A grid with no column to show renders empty.** With no column fixed, shown by default or named by a layout, or with every
column gone, the grid renders its frame with no data cells, with or without a `HorzResizer`: no
exception, the model still loads and the current row still moves, and the columns set later show. Do not propose failing
fast on it.

**A column size below 1 is accepted.** `SetPreferredSize` takes a negative size without complaint, and the column renders at
`auto` width. A size of 0 is the sizeless column grigliaria.md documents under `PreferredSize`: out of the default set and
the layout dialog, with no width of its own. Do not propose an argument check that refuses a size below 1.

**The row dialog follows grigliaria.md § Row dialog.** It is off until the grid sets `RowDialogEnabled`. A column is listed
unless it is fixed or has no label, and its `ShownInRowDialog` decides either way. With the feature on, the footer button is
always there, disabled while every column it lists is in view, with no current row or during an edit. Do not propose turning the
dialog on by default, listing fixed or unlabelled columns by default, or hiding the button when it cannot be used.

**A grid takes `Ctrl`+`←`/`→` only with a `HorzResizer`,** when they scroll its columns. Without one the grid leaves them
to what is around it, as a general rule: the row dialog, whose list is such a grid, moves its row with them. Do not propose
taking the two keys on every grid again, or a switch that turns them off for the dialog's list alone.

**A markup column leaves the model when its component goes,** with its filter and its sort, at the next render
(grigliaria.md § Columns); a grid torn down, and a model instance swapped out earlier, keep their columns. Do not propose
keeping a removed column's filter or sort, or removing it from every model the grid has held.

**A restore follows grigliaria.md § Layout persistence.** `CreateMemento` saves the registered columns only: saving the
set-aside filters, sort or layout place would keep stale names in the store for good. `PrepareRestoredFilter` drops a
restored filter the user could not clear (a column not filterable, a reset included) and one that takes injected
parameters its column's definition no longer injects. Do not propose saving what was set aside, keeping either kind of
filter (on a reset too), or applying a restored injected filter as it is.

**A column with no `Name` is named after its first `Label`,** and keeps that name when the label changes; one passed must
have a value, and under `@foreach` the docs ask for a `Name` or a `@key` (grigliaria.md § Columns). Do not propose renaming
a column when its label changes, telling a reused component from a relabelled one, or reading a null `Name` as omitted.

**Columns go in the grid's own markup, as in QuickGrid,** directly or under `@if` and `@foreach`; one inside a component
of the host's own, or under a `CascadingValue`, is not supported (grigliaria.md § Anatomy). Do not propose supporting
column components of the host's own, dropping `IsFixed` from the grid's cascade, or retrying a column the grid refused.

**A grid inside another grid's cell is not supported** (grigliaria.md § Per-cell markup). Do not report what nested grids
do, or propose changes to `KeyAction` or the grids for them.

**`FilterDefFactory` throws for numeric types `InputNumber` cannot bind** (`byte`, `uint`, `Half`, …). A
filterable column of such a type is a configuration error; the consumer sets `Filterable="false"`. Fail-fast at
registration is the intended behaviour, not a gap.

**A column's `FilterDef` stays typed,** `AFilterDef<VT>?`, so the compiler checks it against the column's type; a built-in
definition is typed by the nullable type, as grigliaria.md states under `FilterDef`. Do not propose retyping `FilterDef` as
the untyped `AFilterDef?`.

**A sortable column needs values that can be compared,** or the model throws when it registers and the consumer sets
`Sortable="false"` (data-models.md, grigliaria.md under `Sortable`). One rule for every column, pushed down or not: the
search on the sorted column compares in memory. Do not propose turning sorting off quietly, exempting pushed-down columns,
or comparing values by their text. A type that implements `IComparable<TBase>` for a base type only is accepted:
`IComparable<in T>` is contravariant, so `Comparer<T>.Default` uses it.

**`IGridModel<T>` is not meant to be implemented from outside.** Its `SetColumn` / `EnrichColumn` need
internal members on purpose: a custom model derives from `AGridModel<T>`, which owns column registration, or
from one of the wrappers. Do not propose making the interface implementable.

**The wrapper models forward the members built on others.** `GridModelWrapper.GetValueAt` and `GetCurrentItem`, and
`TreeGridModelWrapper.GetCurrentTreeNode`, go to the wrapped model rather than to the wrapper's own `GetItem`, `GetValue`
and `GetCurrentRow`: the wrapped model answers them best, the EF models reading up to a current row beyond the rows read.
A subclass that changes those primitives overrides the members built on them too, as the class doc says and Treegliaria's
wrapper does. Do not propose composing them in the wrapper.

**`IGrigliaria` has no memento methods.** `Grigliaria`, `MultiSelGrid` and `Treegliaria` all have `CreateMemento` and
`RestoreMemento`, but the interface declares only what has the same shape on the three: the columns, the current column, the
filters, the three dialogs, focus, repaint and the templates. An implementation chooses its own form of memento, and whether
to provide one at all. Do not propose adding them to the interface. A grid of the host's own implements it by deriving from
one of the three, so a member added to it in a later release breaks no host.

**The wrapper grids forward no layout methods.** `MultiSelGrid` and `Treegliaria` forward the inner grid's methods that take
column names (`GetColumnsToShow`, `SetColumnsToShow`, `EnsureColumnIsShown`, `GetCurrentCol`, `SetCurrentCol`, `GetFilter`,
`HasFilter`, `SetFilter`, `GetFilters`, `SetFilters`), but not `GetLayout` or `SetLayout`, and `GridLayout` has no public constructor that takes columns: as for the
memento, a grid chooses its own form of layout. Do not propose forwarding them, or a public `GridLayout` constructor.

**The grids' protected members stay protected.** `AGrid`, `ADataGrid` and `Grigliaria` expose their mechanics to
subclasses (`MoveRow`, `ScrollBy`, `StartCollectingColumns`, `ColumnRegistered`, the `ElementReference` fields,
`filterDefDlg`, `layoutDefDlg`, …), and `Grigliaria` is subclassed in practice. A subclass that uses them accepts that they
may change between releases, as with many other libraries. Do not report them as accidental exports or propose narrowing
them to `private protected` or `internal`.

**`EFDataGridModel` keeps its other members' visibility.** `IsReadingAllData`, `IsDataStoredInMem` and `MustBeFetched` stay
public although nothing outside the class calls them, and `CreateRestorePoint` with its `RestorePoint` record stays `protected
internal`. Only `Set`, `Insert` and `Remove` are `private protected`, since a host's subclass calling them would skip the
paging-offset bookkeeping, and `CreateKeyPredicate` is private. Do not propose narrowing the others.

**The `Glazor.Misc` helpers are public API.** `RequireNotNull`, `AsyncHelper`, `Utils`, `NoValue`,
`VersionedList`, `MessagePackConf`, the date formatters, `Comparers` and `EqComparers` are used by projects
that depend on Glazor. A few members stay internal, the `IsNumber` extension methods among them; `Utils.IsNumber` is
public. Do not propose hiding the helpers. `OnDiskChunkedList`, `MessagePackConf` and the
date formatters ship in `Glazor.EntityFrameworkCore`, still in `Glazor.Misc`, so that the `Glazor` package does not depend
on MessagePack.

**`ExprUtils` is public API.** Its null-safe expression helpers serve a host that writes its own filter predicates, though
docs/ does not mention it. The types listed under "Other public types" in Technical_Documentation.md, `EnumModel` and
`ColumnEqualityComparer` among them, are public on purpose too; `Getter` and `ExprGetter` are internal, and `IGetter` is
what the public surface hands out. Do not report these as exported by accident; check that table before calling any public
type accidental.

**The namespaces keep their names.** `Glazor.Grds`, `Glazor.Grds.Mdls`, `Glazor.Grds.Decs`, `Glazor.Grds.Props`,
`Glazor.Grds.Filters`, `Glazor.Misc` and `Glazor.Tabs` stay as they are. A saved memento names each filter's type in full
(`"Glazor.Grds.Filters.BoolDef, Glazor"`), so a rename stops every stored layout with a filter from restoring, and every
project that depends on Glazor has to change its usings. Do not propose renaming, spelling out or merging them.

**`MultiSelGrid` treats a model that is both ordered and filterable as ordered.** An ordered model is one the
user arranges by hand; filtering or sorting it would reorder rows under them, so the capabilities are
exclusive and ordered wins.

**`MultiSelGrid`'s select-all box is disabled while the grid is filtered.** Select-all works on every candidate the
selection model holds, so under a filter it would select or clear rows the user cannot see. Selecting only the rows in
view was set aside: on a paged EF model it must read every filtered row, and the box's state is unknown until it has. Do
not propose enabling the box under a filter, or a select-all over the rows in view.

**Comments.** Short, and never restating what the code or a name already says. Never a reference to a review
report or a finding id: the code has to stand on its own for readers who never saw them. When a fix needs a
rationale, state the mechanism in one line, not the history.

**Commit only when asked.** Commit or push only when the current message asks for it. An earlier request in the
session that said "commit" does not carry over to the next one, and "do it", "fix it" or "do all" asks for the change, not
for a commit. Leave the changes in the working tree for the maintainer to review, and offer no commit as a next step.

**Fixes stay minimal.** Make the smallest change that makes the finding go away, in the style the surrounding code
already has. Add no catch, undo, cache, option or hook that no caller can observe: a configuration error that fails
fast needs no cleanup, and the registration checks throw without undoing anything. When a fix does need extra
structure, give the reason in one line. Do not propose making a path transactional.

**A failure nothing can repair is not a finding.** When the environment fails under the library, a disk full or broken
while a chunk spills, an I/O error, the database gone, the operation throws and the host gets the exception. What the
library holds afterwards is not repaired: a reload or a new circuit starts clean. Do not report the state such a failure
leaves, a paging offset one off for instance, and do not propose a catch, a counter, an undo or a transactional path
against it. A finding needs a cause the library or the host can remove.

**`OnDiskChunkedList.Sort` may hold twice the residency budget while it runs,** as data-models.md documents. Bounding the
peak costs writes wherever spills already happen (66 to 98 chunk writes for 49 chunks under a budget of 32), and the
transient peak is preferred. Do not propose an object-counted budget for the merge again.

**Reading spilled chunks back is not a finding.** A row already read that `OnDiskChunkedList` has written to disk comes
back with a chunk read, which is the list doing its job. `Grigliaria`'s type-ahead steps back one row at a time to the
first row that starts with the text, so a short prefix on a descending sort, a large block of equal values or a cleared
search box can read many chunks; it never queries the database. Do not report chunk reads as a cost, and do not propose
a galloping step back, a residency check or other ways to avoid them.

**An evicted chunk is written again even when it was only read.** `EnsureIsOnDisk` writes on every eviction, so each chunk
read back costs a write too. The list hands out its rows, not copies: `EditAsync` returns the row the host's form writes to,
and a host may change a row in place, so a chunk the list saw only read can hold changes. A dirty flag would drop them at the
eviction, during an edit with a fetch included; one that counts every row handed out marks every loaded chunk. Do not
propose a dirty flag, or other ways to skip the write.

**A large fetch on a paged EF model holds all its rows at once.** `QueryDataAsync` reads each pass with one `ToListAsync`
before the rows reach the chunked list, so the `DbContext` given to `RefineItemAsync` stays free to query; a fetch that
asks for many rows, End with `GotoLastKnown` false or the re-read after a sort, a filter change or a reload deep in a
table, holds them all while it runs. The peak is transient and accepted, as for the merge in `OnDiskChunkedList.Sort`.
Under an in-memory filter every pass asks for the whole count again, so a re-read can read and keep up to one pass more
than it needs; that is accepted too, since it keeps the queries few, and each is a deep `OFFSET` for the database. Sizing
the later passes by the rows still missing was tried and reverted: under a filter that keeps few rows the passes shrank,
and the queries tripled. Do not propose capping a pass at the resident budget, streaming the query, or sizing a pass by
the rows still missing.

**Changes to `Clamper.razor.js` must be proven in a real browser, not only by the Node probe.** Two fixes that
passed `ClamperModes.mjs` were reverted after the maintainer's browser check: a last-line hot zone with its own
highlight (1.0.12), and an overflow test chosen by mode with an unconditional mouseout reset (1.0.13). The probe's
fake elements report whatever metrics it is given; real `scrollHeight`/`clientHeight` under `-webkit-line-clamp`
and inline content do not follow them. Propose clamper changes for a browser check rather than as finished fixes.
The clamper as it stands has since been checked in a real browser and needs no fix: the vertical half of `isClamped()`
applying to a single-line cell, the 20 px hot zone at the right edge rather than where a line-clamp ellipsis falls, and
mouseout resetting only while the cell is still clamped are not problems in practice. Do not propose changes to them.

**A reloaded row that leaves the filter, or moves in the paging order, follows data-models.md § `EFDataGridModel`.** On
`EditableEFDataGridModel` only, a row out of the filter stays shown, pinned until the next query, and the database
decides, never the in-memory `Includes`, whose collation would put the paging offset one off; the in-memory models
re-filter the whole list. A row that moved forces `ForceReload(true)`, and the current row reads ahead to follow it. Do
not propose pinning on the in-memory models, deciding the filter in memory on the EF models, keeping a moved row in place,
or an existence query over the rows read, which was applied and reverted.

**A reloaded row keeps its index under a sort in memory.** On the EF models a post or a reload that does not move the row
in the paging order puts it back where it was, so the rows read can be out of order, under a sort icon that says otherwise,
until the next fetch, or for good under `ReadAll`. Only `SearchOnSortedColumnAsync` sorts them again first, the current
row following its record, since its binary search needs the order. Do not propose sorting again after every post.

**The current-row restore asks the database only.** When a reload did not bring the current row back, `KeyIsInQuery`
asks whether the filters the database applies still admit its key, and a row they exclude is not looked for. A row only a
filter applied in memory excludes is looked for to the end of the table: testing it in memory would read the row and run
the host's `RefineItemAsync` once more on every restore. Do not propose testing the filters applied in memory there.

**Opening any popup closes the one already open, the grid's search popup included.** `Popup.razor.js` keeps one
module-level "open" popup for the whole page, so a clamper opening on hover closes `Grigliaria`'s search popup while
the user types. That is intended: the search popup is only a way to position things over the grid, and losing it is
fine on any occasion. Do not propose exempting hover popups or letting a popup opt out of `closeOthers`.

**A hover popup may take the focus.** `Popup` opens with `dialog.show()`, which moves the focus into the dialog, so a
clamper popup opened while the user types in a field takes the focus from it. That needs the mouse over an ellipsis while
typing, which is not a real case. Do not propose giving the focus back, or opening hover popups without moving it.

**Enter in the grid's search box is left to the browser.** The search popup takes Esc, closing itself before a host's own
Escape action sees the key; Enter it does not handle, so inside a host form it submits the form, as in any text field. Do
not propose taking Enter in the search popup.

**The type-ahead does not collect AltGr characters.** Chrome on Windows reports AltGr as Ctrl+Alt, and `_isTextKey` takes no
key with Ctrl or Alt held, so a character typed with AltGr (`@`, `#`, `[` on an Italian keyboard) before the search box
opens is dropped; once the box is open it types there as usual. That is accepted. Do not propose taking AltGr in the
type-ahead.

**Enter on a focused button or link runs the Enter action around it.** A click leaves the focus on the control clicked, so
`KeyAction` takes Enter there as anywhere in its container: after a sort click or a footer button, Enter runs the host's or
the dialog's action, not the control again; reached with Tab, a control such as the layout dialog's `>>` does the same,
which is accepted. A button that keeps its own Enter carries a `KeyAction` bound to itself, as the dialogs' Cancel and Clear
do. Leaving Enter to every focused button in `KeyAction.razor.js` was applied and reverted, and leaving it to one under
`:focus-visible` does not work, since Chrome sets that at the keydown. Do not propose either again.

**Filtering and sorting a tree is the tree model's job.** `TreeGridModel` inherits `DataGridModel`'s row-by-row sort and
filter, so over a flat pre-order list a sort can move children under other parents and a filter can leave a child
without its parent. That is left to the `ITreeGridModel` implementation, which decides what a filtered or sorted tree
means for its data. `EFFolderModel` is one, for a tree in the database: it sorts each level among siblings and keeps a
folder while a row read below it matches. An in-memory base implementation to start from is planned for a future release.
Do not propose disabling sort or filter on tree columns, or tree-aware sorting in `TreeGridModel`, before then.

**Enums marked `[Flags]` are not supported by `EnumDef`.** Membership is exact: a combined value such as `A | B` is
included only if that exact value is selected, so deselecting any member drops the rows that combine it. That is the
supported behaviour, not a gap. Do not propose flag-aware membership, in memory or in the pushed-down predicate.

**A restored `SetDef` offers the candidates it was saved with;** candidates that change are what `DynaSetDef` is for, as
grigliaria.md states. Do not propose refreshing a restored `SetDef`'s candidates from the code.

**A start-full set is not equal to itself once populated.** An `ADynaSetDef` built with `startFull` and never populated
differs, by `Equals`, from the same one after its dialog read the candidates with all of them still selected, though both
filter the same rows: unpopulated it cannot know its candidates, and populated it needs the explicit set to select values
added later. The cost is one extra memento save. Do not propose equality by meaning between the two.

**A filter definition's subclass overrides `Clone`:** each built-in `Clone` builds its own type, as `AFilterDef.Clone` and
grigliaria.md state. Do not propose cloning the runtime type (`MemberwiseClone`) in the built-ins.

**`DefaultDef` on an EF model follows the database:** in memory it ignores case, pushed down case follows the collation,
and a column that is not a string is matched on the database's own cast of its value, as grigliaria.md states under
Filtering. Do not propose making the two paths agree by forcing a case rule, projecting such columns to strings, or
refusing `DefaultDef` on them.

**`DefaultDef` is for the columns that have no filter of their own.** The factory gives enums `EnumDef`, numbers
`NumberDef`, dates and times `DateDef` and `bool` `BoolDef`; `DefaultDef` gets strings and the types with no definition
(`Guid`, `TimeSpan`, `char`, `object`, custom types). A `DefaultDef` that a host forces on a type with its own
definition, such as an enum, is a wrong usage of Glazor: it need not work, and its failures are not findings.

**The cell cursor is not meant to rest on a fixed column.** `SyncShownColumns` moves it off one on every render: with a
`HorzResizer` into the scrolling window, without one to the first column that is not fixed. A click or ArrowLeft into
a fixed column is undone on the next render. When every column shown is fixed it rests on one of them, as there is
nowhere else. That is intended. Do not propose letting the cursor stay on a fixed column.

**A current tab that turns disabled stays current.** `Disabled` refuses selecting a tab, by a click, the keyboard or
`SetCurrentTabAsync`; it does not move the selection off the tab already current, whose panel stays shown. Do not propose
moving off a current tab when it turns disabled.

**A tab is shown or hidden with `Hidden`, not an `@if`.** A `Tab` registers when it is created and is appended, so one
created after the first render, under an `@if` that turns true, is placed last whatever its place in the markup. The docs
say to use `Hidden`. Do not propose keeping the markup order in code.

**A race is a finding only if it happens in a real browser.** An interleaving that a test or probe forces, such as a
JS reply held back so that a second event overtakes it, is not a finding when a real browser reaches it only inside a
narrow timing window. The filter dialog's double OK, which throws only when the second click lands between one and two
round trips after the first, was fixed and reverted on that basis. Report such a race only when it has been reproduced
in a real browser under realistic conditions.

**The spinner is the application's, allocated once in the main layout,** and a grid over a paged model throws on its first
render without one; the app is interactive from the root (getting-started.md, Technical_Documentation.md § Spinner). Do
not propose tolerating a missing spinner, and do not write samples that render the spinner in the grid's page, cascade the
`@ref` field itself (still null when that render reads it), or put a `@rendermode` on the page.

**A selection model's set is changed only through the model,** with `defensiveCopy` saying whether it is shared, and a
read-only set is copied even with `false` (data-models.md § Selection models). The select-all models cache `AllSelected()`
on the model's version. Do not propose refusing a read-only set, dropping the cache, or detecting changes the host makes
to a shared set.

**A locked row keeps its state.** The rows a `selectionEnabledFn` refuses stay as they are, selected or not: the constructors
keep the locked rows of the set they are given, `SelectAll` and `DeselectAll` change only the candidates, MultiSelGrid
shows a locked row's box disabled, and `Select(item, true)` on one does nothing. `Select(item, false)` still deselects
one, from host code. Do not propose dropping locked rows from the selection, or reading a refused select as a deselect.

**Log messages are not tested.** A fix whose effect is a log line, such as the warning for an in-memory sort on a paged
model, gets no test asserting that the line is written. Counting the library's own log lines to measure work, as
`PagedGridCostTests` and `OnDiskChunkedListSortTests` do, is a different thing and stays. Do not add tests that capture a
log message to check it.

**A one-line argument check gets no test.** A fix that only refuses bad input at the call, such as
`ArgumentOutOfRangeException.ThrowIfNegative(firstScrollingOffset)` in `ScrollToCol`, is covered by building and running the suite. A test
is for a fix that changes behaviour: a crash during rendering, a leak, a wrong result, an event raised twice. Do not add
tests that only assert a guard throws.

**A sample's arguments need no explaining.** A doc or skill sample that passes a bare argument, such as the `true` that is
`alwaysInMemory` in `base(true, r => r.Id, startFull)`, is read against the constructor or method it calls. Do not report
an argument the text leaves unexplained, or propose naming it; a sample that does not compile or behaves wrongly is still a
finding.

**The test fixtures keep their insurance domain.** `Glazor.Tests/Models` models policies, claims and claim parties, with
the table names, keys, enums and the self reference the EF tests need; the values are placeholders, and the maintainer
has reviewed the schema: it holds nothing sensitive. Do not report it as a release finding, or propose renaming it to a
neutral domain.

**A paged model's rows are flat DTOs without cycles, and a cycle is refused, not detected:** `DepthGuardResolver` makes a
graph deeper than `MessagePackConf.MaxGraphDepth` (64) throw from the fetch that spills it, instead of overflowing the
stack (data-models.md). Do not propose reference tracking, a graph walk before the spill, or a per-type exemption from the
limit.

**A nested type stays nested while it uses the container's type parameter.** `HCellTemplateParam`, `GHCellTemplateParam`,
`EmptyRowTemplateParam` live in `IGrid<TGridItem>`, `RowHeadingTemplateParam` and `CellTemplateParam` in
`IGrigliaria<TGridItem>`, `Treegliaria<TGridItem>.CellTemplateParam` in its grid: each is typed by `TGridItem`, so the
container qualifies it and a host writes `IGrid<Product>.HCellTemplateParam`. Only a type that uses no type parameter
belongs at namespace level, as `Memento`, `GridLayout`, `LayoutAttr` and `SortedColumn` do, so that a persistence layer needs
no row type. Do not propose hoisting the template parameter records, or renaming the tree grid's `CellTemplateParam`.

**`OnModelChanged`, `OnLayoutChanged` and `OnSelectionChanged` are `Action`, not `EventCallback`:** they fire from
synchronous paths where nothing can be awaited, as the docs state with the reason. Do not report the mix as an
inconsistency, or propose turning the three into `EventCallback`.

**`ShouldReRenderTrick` is never read by the grid, and that is the point:** any delegate there makes Blazor re-render the
grid on every parent render (grigliaria.md § Parameters), so a grep for readers finds none. Do not report it as dead API,
or propose deleting it or consulting it in `ShouldRender`.
