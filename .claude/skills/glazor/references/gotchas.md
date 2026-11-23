# Glazor traps

Each of these has cost real debugging time. Grouped by what you would be doing when you hit one.

## Reading data out of a model

**`GetDataView().ToList()` throws on a paged EF model.** So do `.ToArray()` and `new List<>(dataView)`. The
view is a `ReadOnlyCollection<T>` wrapping an `OnDiskChunkedList<T>`, and LINQ's buffering operators take the
`ICollection<T>` fast path straight to `CopyTo`, which throws `InvalidOperationException`.

This is deliberate. A real `CopyTo` has to materialize every row into the caller's array, which is exactly the
memory blow-up paging exists to prevent, so it fails loudly instead. The indexer, `Count` and streaming LINQ
all work — the trap is specifically the buffering operators, which is why it sits unnoticed until someone
writes one.

```csharp
var view = _model.GetDataView();
var row  = view[idx];                                   // fine
foreach (var r in view) { }                             // fine
var some = view.Where(r => r.Ok).Take(10).ToList();     // fine: Where streams first
var all  = view.ToList();                               // throws on a paged model
```

In-memory models are unaffected — and `GetData().AsEnumerable()` is the idiom for walking one.

**Rows are spilled to the OS temp directory by default.** `ReadAll` defaults to `false`, so the backing store
for every paged EF grid is an `OnDiskChunkedList` — entity rows MessagePack-serialized, unencrypted, under
`Path.GetTempPath()`. For some deployments that is a data-residency question rather than a performance one.
`SpillDirectory` on the model chooses where the chunks are written; `CreateItemsListFn` replaces the list altogether. Orphaned
files outlive a process that stops, normally or not, while a paged model is alive.

## Wiring a grid

**A model assigned fresh loses the filters, the sort and the current row.** They live on the model; the grid keeps
the column sizes, and shows the default columns again unless a memento was restored. Build it once and call
`ForceReload` when parameters change:

```csharp
if (_lastId == Id && _model != null) return;
_lastId = Id;
_model ??= new() { /* … */ };
_model.ForceReload(restoreCurrentRow: false);
```

**Without an item key the current row jumps.** `ItemKeyFn` (in memory) or `ItemKeyExpr` (EF) is how the model
re-finds the selected row after a reload, re-sort or filter change.

**Without a `VertResizer` the grid falls back to `PageSize`,** which defaults to 5. It pages rather than
virtualizes, so it has to be told how much vertical space it has.
`GridVertResizers.RowHeight("height: 30vh;", min, max)` first guesses: it divides a measured CSS height by the grid's
`RowHeight` (default 40) after subtracting `HeaderHeight` and drops one row. Once drawn, it measures what the browser drew
— the rows, the header, the footer, the scrollbar — and settles on the largest page that fits the height, so nothing
below the grid is overlapped. Either way it clamps, and a `RowHeight` of `null` or 0 makes it throw.

**The style is any CSS that gives the box a height,** since the resizer measures the box, not the style. To have a
grid fill the window under the page's text, put it in a flex column of definite height (the page `height: 100vh`, its
`main` `flex: 1; min-height: 0; overflow: auto; scrollbar-gutter: stable`) and pass `"flex: 1 1 0; min-height: 12rem;"`; the minimum keeps a short
window usable, and raise the maximum page size, or a tall screen stops at it. With a panel under the grid, keep a fixed
`vh` instead, so the panel stays in view.

**A title above a grid that fills the window can make it flip between two page sizes.** One more row brings the scroll
container's scrollbar, the page narrows, the title wraps and the grid loses that row; without it the scrollbar goes. The grid
notices the size going back to the one it had twice in a row and keeps the smaller one; `scrollbar-gutter: stable` on the scroll container
keeps the width, and the row.

**Omit `HorzResizer` and horizontal paging is off** — every visible column renders. Often what you want; only a
trap if you expected `Ctrl`+`←`/`→` to do something.

**Once the markup passes a template as a child element, columns need an explicit `<ChildContent>`.** Razor takes
no loose content beside a named template such as `<TitleTemplate>`; a component with several `RenderFragment`
parameters still takes loose columns when none of them is passed that way.

**Columns go in the grid's own markup.** Write each `GridColumn` and `EnrichGridColumn` directly in the grid's child
content, or under an `@if` or a `@foreach`. A column inside a component of your own, or under another component such as
a `CascadingValue`, is not supported: it can render after the grid has collected its columns, and is then left out of
the model, silently.

**An enrichment outlives its `EnrichGridColumn`.** One under an `@if` that turns false leaves its templates on the column,
and a template it leaves out keeps the one the column has. To switch templates, give every branch its own
`EnrichGridColumn`, the plain one included (grigliaria.md, "Enriching a column declared in code").

**A grid inside a grid's cell is not supported.** The keys and clicks that reach the inner grid go on to the outer one,
which can act on them: while the inner row is being edited, `↓` in its editor moves the outer row. Show related rows in a
second grid beside the first, or in a dialog.

**A layout of only `Fixed` columns shows just those.** With a `HorzResizer` nothing is left to scroll, so the horizontal
controls stay disabled. The layout dialog gets there when every other column is moved out.

**While the layout dialog is open, only the dialog changes the grid.** It applies each change as the user makes it and
its Cancel puts the old layout back, so leave `Layoutable`, the columns (an `@if` or `@foreach` over them) and the model
alone until it closes.

**`Ctrl`+`←`/`→` are the grid's only with a `HorzResizer`.** Then they scroll its columns. Without one the grid leaves
them to what is around it: a `KeyAction` of yours on an ancestor, or the row dialog, which moves its row with them.

**Exactly one of `Property` / `EFProperty` per column.** Both, or neither, throws at parameter-set time.

**A sortable column needs values that can be compared.** `Sortable` defaults to true, and a column whose type implements
neither `IComparable<T>` nor `IComparable`, nullable or not, and is not `object`, throws `InvalidOperationException` when it
registers, unless the model overrides `GetComparerForColumn` for it. That covers records, classes and collections, and a custom type an EF value
converter stores, even though the database could sort it: the search on the sorted column compares in memory. Set
`Sortable="false"` on such a column.

## Notifications not re-rendering

**`OnModelChanged`, `OnLayoutChanged` and `OnSelectionChanged` are `Action`, not `EventCallback`.** Blazor
re-renders your component automatically when an `EventCallback` handler completes; an `Action` handler gets
nothing. Call `StateHasChanged()` yourself.

They are shaped that way on purpose: they fire from a render, a property setter or the model's own event, where there is nothing to
await. The ones raised from real user events — `OnDblClick`, `OnClose`, `OnKey`, `OnStableItemChanged` — are
`EventCallback`.

**A grid that does not follow its parent's `StateHasChanged` takes `ShouldReRenderTrick`.** Set it to any delegate,
`ShouldReRenderTrick="@(() => true)"`: Blazor re-renders a child whose parameter is not of a known-immutable type on every
parent render. The grid never calls it, so do not look for a reader.

**`OnStableItemChanged` is what drives a detail panel,** not the raw current-row change. It fires once the
selection settles, so holding an arrow key does not thrash the panel.

## Trees

**`TreeGridModel.OnSetExpand` is `required`.** A model built without it does not compile (CS9035), so set it in
the object initializer. See `trees.md` for what it has to do.

**Tree expansion is in the memento only over an `ITreeStateGridModel`.** Over `TreeGridModel`, `Treegliaria`'s
`CreateMemento`/`RestoreMemento` save what `Grigliaria` saves, columns, widths, filters and sort, and not the open/closed
state: save your collapsed set yourself if you want it to persist. Over an `ITreeStateGridModel` such as `EFFolderModel`
the memento is a `TreeMemento`, with the folders expanded and the zoom. See `trees.md`.

## Naming and spelling

**A mistyped grid parameter fails at render, not silently.** No grid captures unmatched attributes, so
`CellCursorEnable="false"` makes Blazor throw `InvalidOperationException` naming the missing property. The cell
cursor is on by default; the grid method that reads the setting is `IsCellCursorEnabled()`.

**`GridModelChangeType` is in `Glazor.Grds.Mdls`,** not `Glazor.Grds`. Since `Glazor.Grds` is the one usually
imported globally, a code-behind overriding `ModelChanged` needs an explicit `using Glazor.Grds.Mdls;`.

**`Name` defaults to the first `Label`.** A later label changes only the header, and the column keeps its name. Set
`Name` explicitly whenever the label can differ from one session to another — localization, a dynamic header — or a
saved layout stops matching and silently reverts. Set it too under `@foreach` without `@key`: Blazor reuses the
components by position, and one named after its label keeps the name of the column that was there before. `Name` may be
omitted, but not passed empty: a null or empty `Name` throws at the first render.

## The behaviours: KeyAction and MouseAction

**Your own keyboard and mouse handlers on the same element are safe.** Both components attach with
`addEventListener`, once per element, and teardown removes exactly the functions they added — so a handler
the application has bound to the element `BindAncestorWithTag` resolves to is neither replaced on mount nor
removed on unmount.

**Several of these can share one ancestor, and one listener serves them all.** That is deliberate: it is what
the stroke list parked on the element is for. Per-registration settings such as
`MouseAction.ExclusiveClick` travel with the stroke rather than with the shared listener, so setting it on
one `MouseAction` does not reach a sibling bound to the same element.

**A key pressed in an open modal dialog is the dialog's.** A `KeyAction` on an element around the dialog, a page panel
holding a grid for instance, does not see keys pressed in the grid's dialogs or in a `DialogBox` shown with
`ShowModalAsync`; bind the dialog's own keys inside it, or on it with `BindAncestorWithTag="dialog"`.

**`Key="ANY_CHARS"` and the grid's type-ahead skip characters typed with AltGr** (`@`, `#`, `[` on an Italian keyboard):
Chrome on Windows reports AltGr as `Ctrl` + `Alt`, and a key with either held is not text. Once the grid's search popup is
open, they type there as usual.

**`CancelBubble` is what lets a wheel handler `preventDefault`.** It defaults to `true` and is what cancels the
browser's own scroll; turn it off and the page scrolls underneath your handler.

## Configuration

**`TouchScrollerEnabled`'s default is not `false`.** When the parameter is not set on the component, the grid
reads `Grigliaria:TouchScrollerEnabled` from `IConfiguration`, falling back to `false` only if the key is absent:

```json
{ "Grigliaria": { "TouchScrollerEnabled": true } }
```

This is the only setting Glazor reads from configuration. The value is read
once and cached, so a configuration reload does not take: change it and restart. Setting the parameter
explicitly always wins, which is how you opt one grid out of an application-wide `true`.

## Subclassing a grid

**A subclass's markup replaces the base's, it does not wrap it.** The Razor compiler generates a
`BuildRenderTree` that overrides rather than calls the base, so the base output has to be re-emitted:

```razor
@RenderBase()

@code {
    private RenderFragment RenderBase() => builder => base.BuildRenderTree(builder);
}
```

**Add `[CascadingTypeParameter(nameof(TGridItem))]`** or consumers must restate `TGridItem` on every
`GridColumn`.

**Guard layout saving on "has rendered".** Before the first render the grid has not discovered its columns, so
a memento taken then is empty — and saving it overwrites good state. Compare against the last saved memento
too: `AfterSync` fires often, and `Memento` is a record, so `Equals` suffices to suppress the churn.

## Filters

**Adding state to a filter definition without extending its `Serialized` record makes that state invisible** —
to equality and to the persisted payload. No error; two definitions differing only in your field compare equal.
See `filters.md`.

**A definition needing DI must implement `IWithInjectedParams<SELF>`.** Restoring from a memento constructs it
via `Activator.CreateInstance` with no injection, so anything from DI arrives null. See `filters.md`.

**A scoped definition needs `IsStillApplicable()`.** If its candidate values depend on a company, tenant or
selected parent, a filter restored in a different context is applied with stale keys and the grid shows
nothing for no visible reason. See `filters.md`.

**A collection-valued column wants `Filterable="false"`** unless you supply a `FilterDef`. The factory falls
through to a string contains/starts-with filter, which means nothing over a collection and, on an EF model,
gets composed into the query. Numeric types that `InputNumber` cannot edit (`byte`, `uint`, `Half`, …) do not fall through: they throw, on registration for a markup `<GridColumn>` and when the filter is first asked for on a column added with `AddColumn`, so those columns need `Filterable="false"` too.

## Layout widths

**Auto-width columns take a share of the width by count, not by content.** The grid counts how many visible
columns have a negative `PreferredSize` (a `Column<>` record added through `model.AddColumn`, or a negative size given to `SetPreferredSize`; a `<GridColumn>` always gets a type default), gives that fraction of the width to them collectively, and lets the sized
columns divide the rest in proportion to their sizes:

```
sizes 33, 67                        -> 33%  67%            (nothing reserved)
sizes 33, 67 + one auto column      -> 22%  44%  auto      (1 of 3 unsized, so 33% reserved)
sizes 8, 5, 6 + three auto columns  -> 21%  13%  15%  auto…(half reserved)
```

So adding one unsized column visibly narrows every sized one. Give every column a `PreferredSize` and the
reserve is zero, leaving each its plain share.
