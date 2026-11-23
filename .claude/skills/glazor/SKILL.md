---
name: glazor
description: How to build data grids and grid-backed pages in a Blazor Server app with the Glazor library — Grigliaria, Treegliaria, MultiSelGrid, GridColumn, the in-memory and EF Core models, per-column filters, layout persistence, hierarchical rows via ITreeNode, and the dialog/popup/spinner components. Use this skill whenever a task involves a Glazor type (Grigliaria, GridColumn, DataGridModel, EFDataGridModel, TreeGridModel, AFilterDef, MultiSelGrid, DialogBox, Clamper, KeyAction), a `Glazor.*` namespace, or the `_content/Glazor/` asset path — and also whenever someone is adding, changing, or debugging a paged, filterable or sortable table in a Blazor project that references Glazor, even if they never name the library. Reach for it before writing grid markup by hand: Glazor grids page rather than virtualize, and the four things that makes necessary — a vertical resizer, an item key, a spinner, and a model built once — are not guessable from the API surface.
---

# Using Glazor

Glazor is a Razor Class Library for .NET 10 Blazor Server. Its centre is **`Grigliaria`** — a data grid that
renders **one page of rows at a time** rather than virtualizing a long scroll region. That single choice
explains most of the API: the grid must be told how much vertical space it has, it must be able to identify a
row across a re-sort, and when it is backed by EF Core it fetches during render, so it needs a spinner.

This file covers the shape of a grid page. Four reference files go deeper:

| File | When to read it |
| --- | --- |
| `references/models.md` | choosing and configuring a model; the editing lifecycle; an edit dialog or panel |
| `references/trees.md` | hierarchical rows — `Treegliaria`, `ITreeNode`, expansion |
| `references/filters.md` | writing your own filter definition; the two traps that corrupt persisted state |
| `references/gotchas.md` | failure modes that cost real time, each with the reason |

## Before you write any grid markup

Every one of these is a silent failure if missing.

| What | Where | Why |
| --- | --- | --- |
| `<link href="_content/Glazor/glazor.css" rel="stylesheet" />` | `App.razor` `<head>` | Declares the `--glazor-*` colours and icons every stylesheet reads, and styles the popups, dialogs, clamper and spinner; without it the grid has no colours and no icons and the dialogs render as bare elements |
| `<link href="{YourApp}.styles.css" rel="stylesheet" />` | `App.razor` `<head>` | Carries the grid's own scoped styles; without it the grid renders unstyled and nothing reports an error |
| `LoggerSupport.SetLoggerFactory(app.Services.GetRequiredService<ILoggerFactory>())` | `Program.cs`, after `builder.Build()` | Glazor's non-component types cannot inject `ILogger`; skip this and they log to `NullLogger` and you debug blind |
| `<HeadOutlet @rendermode="InteractiveServer" />`, `<Routes @rendermode="InteractiveServer" />` | `App.razor` | Sorting, filtering and keyboard navigation need interactivity, from the root: with a `@rendermode` only on the page the layout renders statically, and the spinner it cascades arrives null in the page |

Namespaces: `Glazor.Grds` has the components (the two scrollers included), `Glazor.Grds.Decs` the cells a template reuses
(`DefaultCell`, `DefaultTreeCell`, `DefaultMultiSelCell`), `Glazor.Misc` the dialogs and behaviours,
`Glazor.Tabs` has `Tabs` and `Tab`. Put those four in `_Imports.razor` or import them per file; either works. A component whose
namespace is not imported only draws a warning (RZ10012) and renders as a raw element, `<tabs>` for instance.
`Glazor.Grds.Mdls` is the one worth remembering: the models, the selection models **and**
`GridModelChangeType` live there, and it is *not* `Glazor.Grds`. A missing `using Glazor.Grds.Mdls;` is the
usual first compile error.

## Layout persistence, and where to put it

`Grigliaria` is complete on its own. Put it on a page with a model and columns, as in *Declaring columns*
below, and you have a working grid — nothing here requires a subclass.

What is worth deciding early is where **layout persistence** goes: saving a user's hidden and reordered
columns, filters and sort order so they survive navigating away and back. The grid supplies the pieces and
leaves the policy to you.

```csharp
Memento CreateMemento();              // public: layout + filters + sort, as a serializable record
void    RestoreMemento(Memento? m);   // null: back to the layout, filters and sort the grid started with
```

Two notifications tell you when to save, and each is available either as a **parameter** or as a protected
virtual — so you can wire this up with or without inheriting:

| | as a parameter | as an override |
| --- | --- | --- |
| the model changed | `OnModelChanged` — `Action<GridModelChangeType>?` | `ModelChanged(GridModelChangeType)` |
| columns, order or widths changed | `OnLayoutChanged` — `Action?` | `LayoutChanged()` |

**With one or two grids, do it on the page.** Take an `@ref` to the grid, pass the two `Action` parameters, and
call `CreateMemento`/`RestoreMemento` from your own handlers. No subclass, nothing to build.

**With many grids, hoisting it into a subclass saves repeating yourself** — and gives every grid the same
toolbar while you are there: a title, a reset button, a refresh button, an export button. The code below is the
sample applications', reduced to essentials.

One caveat on that evidence, because it is easy to over-read. Both sample applications have the same wrapper, and
between them 70 grid pages use it — but they also share an author and a common set of house services, so that is
one lineage rather than two teams independently arriving at the same answer. Read it as a pattern that has held up across 70 pages, not as what the library expects. A
new project is free to use `Grigliaria` directly, or to wrap it by composition rather than inheritance.

```razor
@* XGrigliaria.razor *@
@using Glazor.Grds
@typeparam TGridItem
@inherits Grigliaria<TGridItem>

@if (ShowTop)
{
    <div style="display:flex;justify-content:space-between;align-items:center">
        <h1>@(Title ?? "")@TitleTemplate</h1>
        <div>
            @if (IsFiltered())
            {
                <span style="color:orangered;font-weight:bold">FILTER ACTIVE</span>
            }
            <button type="button" @onclick="ResetGridStateAsync"
                    disabled="@(Model.IsCurrentRowFrozen())">Reset</button>
            @if (OnRefreshData.HasDelegate)
            {
                <button type="button" @onclick="RefreshDataAsync"
                        disabled="@(Model.IsCurrentRowFrozen())">Refresh</button>
            }
            @TopTemplate
        </div>
    </div>
}

@RenderBase()

@code {
    // A subclass's generated BuildRenderTree replaces the base one instead of calling it,
    // so the base markup has to be re-emitted by hand. https://github.com/dotnet/razor/issues/10499
    private RenderFragment RenderBase() => builder => base.BuildRenderTree(builder);
}
```

Two details in that markup earn their place. `IsFiltered()` tells the user *why* they are looking at three rows
out of nine thousand — without it, a filter left on from a previous visit looks like missing data. And
`Model.IsCurrentRowFrozen()` disables the toolbar while an edit or insert is pending, which is the model saying
that moving, filtering, sorting or reloading would throw right now.

The code-behind adds the parameters and the persistence:

```csharp
using Glazor.Grds;
using Glazor.Grds.Mdls;   // GridModelChangeType
using Microsoft.AspNetCore.Components;

namespace MyApp.Components;   // the namespace of XGrigliaria.razor: your project's, then its folders

[CascadingTypeParameter(nameof(TGridItem))]
public partial class XGrigliaria<TGridItem> : Grigliaria<TGridItem>
{
    [Parameter] public string? Title { get; set; }
    [Parameter] public bool ShowTop { get; set; } = true;
    [Parameter] public RenderFragment? TitleTemplate { get; set; }
    [Parameter] public RenderFragment? TopTemplate { get; set; }
    [Parameter] public EventCallback OnRefreshData { get; set; }

    /// <summary>When set, layout + filters + sort are saved under this key.</summary>
    [Parameter] public string? StateKey { get; set; }

    [Inject] private ILayoutStore Store { get; init; } = null!;   // yours: a circuit-scoped cache, session state, a table

    private bool _rendered;
    private Memento? _lastSaved;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            if (StateKey != null && Store.Get<Memento>(StateKey) is { } saved)
            {
                _lastSaved = saved;
                RestoreMemento(saved);
            }
            _rendered = true;      // nothing before this point is worth saving
        }
    }

    protected override void ModelChanged(GridModelChangeType changeType)
    {
        base.ModelChanged(changeType);
        if (changeType == GridModelChangeType.AfterSync)
            SaveGridState();
    }

    protected override void LayoutChanged()
    {
        base.LayoutChanged();
        SaveGridState();
    }

    private void SaveGridState()
    {
        if (StateKey == null || !_rendered)
            return;
        var current = CreateMemento();
        if (!current.Equals(_lastSaved))   // Memento is a record: value equality
        {
            _lastSaved = current;
            Store.Set(StateKey, current);
        }
    }

    protected virtual Task ResetGridStateAsync()
    {
        _lastSaved = null;
        RestoreMemento(null);              // null: back to the layout, filters and sort the grid started with
        if (StateKey != null) Store.Remove(StateKey);
        return Task.CompletedTask;
    }

    protected virtual async Task RefreshDataAsync() => await OnRefreshData.InvokeAsync();
}

// Registered by the host; any store that keeps a value per key will do.
public interface ILayoutStore
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    void Remove(string key);
}
```

`[CascadingTypeParameter]` is what lets a page write `<GridColumn Property="@(x => x.Name)">` without restating
`TGridItem` on every column.

The `_rendered` guard and the compare-before-save are both load-bearing. Before the first render the grid has
not discovered its columns, so a memento taken then is empty and would overwrite good saved state. And
`AfterSync` fires often, so without the equality check you write the same snapshot repeatedly. A memento holds
only the registered columns: one under an `@if` that is off when the state is saved comes back with its defaults in a
later session.

`Memento` round-trips through JSON (`System.Text.Json`; `AFilterDef` carries its own converter), so a circuit-scoped cache, session state or a
user-preferences table all work as the store. A circuit-scoped cache with a `StateKey` of `nameof(ThePage) + "_SK"`
is a good default.

If you do take this route, `Treegliaria` gets the same treatment — the sample applications pair their grid wrapper
with a tree wrapper, identical but for the base class: `Treegliaria` does not derive from
`ADataGrid`, but its `CreateMemento`/`RestoreMemento` take the same `Memento`. Over an `ITreeStateGridModel` it saves in
`ModelChanged` whatever the change type, since expanding, collapsing and zooming raise `Other`, not `AfterSync`; the
compare before saving keeps the writes to the real changes.

## Choosing a model

The grid never owns its rows; a model supplies them. This is the main design decision per page, and the answer
is usually the boring one. In the two sample applications the split is roughly **70 in-memory models to 2
EF-backed ones** — most grids show data the page has already fetched. Weigh that as two data points from one
team rather than a law, but the direction is worth knowing.

| Model | Rows live in | Filter & sort | Use when |
| --- | --- | --- | --- |
| `DataGridModel<T>` | a list you own | in memory | the default; the page already has the rows |
| `EditableDataGridModel<T>` | a list you own | in memory | …plus an edit/insert lifecycle |
| `TreeGridModel<T>` | a flat list of `ITreeNode` rows | in memory | hierarchical rows — `references/trees.md` |
| `EFDataGridModel<TDbContext, T>` | the database | **translated to SQL** | the table is too large to load, or filtering must hit an index |

Every model needs to know how to identify a row, so it can keep the current row selected across a reload,
re-sort or filter change: `ItemKeyFn = x => x.Id` in memory, `ItemKeyExpr = x => x.Id` for EF (an expression,
so it can take part in the query).

### The common case

```csharp
private readonly DataGridModel<Role> _model = new() { ItemKeyFn = r => r.Id };

private async Task RefreshAsync()
{
    var roles = await LoadRolesAsync();
    _model.GetData().Set(roles);      // the model re-syncs on the next render
    StateHasChanged();                // which is up to you, unless a UI event is already causing one
}
```

`GetData()` returns an `AList<T>` that marks the model as changed; the UI refresh is yours to trigger. `Set` **wraps** the list you hand it
rather than copying, so it becomes the backing store; a read-only one, as an array, is copied. See `references/models.md` for the rest of that surface
and the editing lifecycle.

### When it is EF-backed

```csharp
protected override void OnParametersSet()
{
    if (_lastUserId == UserId && _model != null)
        return;
    _lastUserId = UserId;

    _model ??= new()
    {
        DbContextFactory = DbContextFactory,
        ItemKeyExpr      = x => x.LoginId,
        BaseQueryFn      = (db, _) => db.AccessLogins
            .AsNoTracking()
            .Where(x => x.UserId == _lastUserId)
            .Select(x => new AccessDTO { /* … */ })
            .OrderByDescending(x => x.LoginDateTime),
        ReadAll = false,        // page instead of loading everything
    };
    _model.ForceReload(false);  // the user changed, so the old current row is meaningless
}
```

The `??=` plus `ForceReload` is the point. **Assigning a fresh model silently discards the user's filters, sort
order and current row**, and the grid shows its default columns again unless a memento was restored — everything the
wrapper above works to preserve. Note the closure reads
`_lastUserId` rather than the parameter, so the query stays correct without rebuilding the model.

Two things the query itself should do: **project to a DTO**, a `record` with `init` setters, under
`AsNoTracking()` — on a grid that edits, a class with setters instead, since the form edits the row the model hands out
and the generated key of an inserted row is written into it before `PostAsync` (`references/models.md`) — and prefer `EFProperty` columns so filtering and sorting become SQL over the whole table. In memory, a filter
tests each row as it is read, at the cost of more queries, and a sort covers only the rows fetched so far, re-sorted as
each window arrives; setting such a sort on a paged model logs a warning.

Ordering it is optional: a paged model orders its query by `ItemKeyExpr`, after your own `OrderBy` when there is
one, so order it only for the order you want the rows in. The key follows your last `OrderBy` or `ThenBy` even when
`Where`, `AsNoTracking`, `Include` or a `Select` to the same type come after it. A base query ordered before a
projection from another type, and on a grid over a base type a query over a derived type with calls after its order, are
used as they are, and their order must then be total by itself. EF drops an order that `Distinct`, `Concat` or another
set operation follows, so such a query is ordered by the key alone: order after the set operation for your own order.

**Give an EF-backed grid a `Spinner`; it needs one.** Fetches happen *during rendering*, so the grid blocks briefly
while a query runs, and the spinner is what the user sees; without one the first render throws
`ArgumentNullException`. The spinner is provided by the application: allocate `<Spinner @ref="_gui.Spinner" />`
once, in the main layout of an app interactive from the root (see the table at the top), cascade the holder (`<CascadingValue Value="_gui" IsFixed="true">@Body</CascadingValue>`),
and pass `Spinner="@Gui.Spinner"` from a `[CascadingParameter] GuiHelper Gui`. Never render the spinner in the same
page as the grid, and never cascade the `@ref` field itself: a reference is set only after its component has
rendered, so the grid's first render would get null. Full example: docs/Technical_Documentation.md, Spinner. Turn prerendering off on `HeadOutlet` and `Routes`
(`new InteractiveServerRenderMode(prerender: false)`) in an app with a paged grid: the first render fetches and shows the
spinner, which needs the circuit, and under prerendering it runs once more on the static pass, where the spinner's interop
fails and is logged and the query runs twice. Put `<AppLoading />` in `App.razor`, before `Routes`: a page opened by a
link or a reload stays blank until its first render ends, queries included, and it shows the wheel meanwhile.

## Declaring columns

```razor
<div class="grigliaria">
    <XGrigliaria TGridItem="AccessDTO" @ref="_gridRef" Model="_model"
                 HeaderHeight="70" RowHeight="40"
                 ShowHeader="true" ShowColumnGroups="true" ShowNavigationButtons="false"
                 Layoutable="true"
                 HorzResizer="@(GridHorzResizers.DispUnitWidth(50, 150, 12))"
                 VertResizer="@(GridVertResizers.RowHeight("height: 30vh;", 2, 20))"
                 StateKey="@(nameof(AccessLogs) + "_SK")"
                 Spinner="@Gui.Spinner"
                 OnRefreshData="@(() => _model.ForceReload(true))">
        <ChildContent>
            <GridColumn EFProperty="@(x => x.NavId)"    Name="NavId"    Label="Navigation id" GroupId="Navigation" ShownByDefault="false" />
            <GridColumn EFProperty="@(x => x.NavDate)"  Name="NavDate"  Label="Timestamp"     GroupId="Navigation" PreferredSize="12" />
            <GridColumn EFProperty="@(x => x.Location)" Name="Location" Label="Location"      GroupId="Navigation" />
            <GridColumn EFProperty="@(x => x.Status)"   Name="Status"   Label="Status"
                        CellClassFn="@(ctx => ctx.Value == "ERROR" ? "err" : "ok")" />
        </ChildContent>
    </XGrigliaria>
</div>
```

`<ChildContent>` is explicit here, as it must be once the markup also passes another template as a child element, such as
`<TitleTemplate>`: Razor then takes no loose content beside it, so the columns go in `<ChildContent>` too.

Columns go in the grid's own markup, directly or under `@if` and `@foreach`. A `GridColumn` inside a component of your
own, or under another component such as a `CascadingValue`, is not supported and can be missing from the model.

Exactly one of `EFProperty` (an expression, pushed to SQL) or `Property` (a delegate, always in memory) is
required — both or neither throws at parameter-set time.

- **`Name` defaults to the first `Label`.** A later label changes only the header. Set `Name` explicitly if the
  label can differ from one session to another — a localized or dynamic header otherwise stops matching a saved
  layout — and on columns written with `@foreach` without `@key`, where a reused component keeps the old name. Omit
  it or give it a value: a null or empty `Name` throws at the first render.
- **`PreferredSize`** is in display units, defaulting by type: `string` 20, `DateTime` and `DateTimeOffset` 12, numeric
  8, `enum` 10, `bool` 6, otherwise 10, for a `<GridColumn>` and a `Column<>` added through `model.AddColumn` alike. Sized
  columns divide the width in proportion to their sizes; a `Column<>` with a negative `PreferredSize`, or a column given
  a negative size by `SetPreferredSize`, is left unsized, and each such column reserves a share by count first. See
  `references/gotchas.md` § Layout widths.
- **`Sortable`** needs values that can be compared: a type that implements `IComparable<T>` or `IComparable`, nullable or
  not, or `object`, unless the model supplies a comparer through `GetComparerForColumn`. A sortable column of a type that
  implements neither, a record, a class or a collection, throws `InvalidOperationException` at the first render, on an EF
  model too: set `Sortable="false"` on it.
- **`GroupId`** groups adjacent columns under a shared header, which shows the `GroupId` itself (a `GHCellTemplate` for
  other text), and needs `ShowColumnGroups="true"`.
- **`ShownByDefault="false"`** keeps noisy identity columns available in the layout dialog but out of the way.
- **`RowDialogEnabled="true"`** on the grid adds a footer button that opens the current row as a list, one line per column
  with its label and its value drawn as in the grid, the hidden columns after a separator; two buttons and
  `Ctrl`+`←`/`→` step through the rows. The button is disabled while every column it lists is in view. A column is listed unless it
  is `Fixed` or has no label; `ShownInRowDialog` on the column decides either way. See `docs/grigliaria.md` § Row dialog.
- **`ReloadEnabled="true"`** on the grid adds a footer button that reloads an EF model with `ForceReload(true)`, keeping the
  filters, the sort and the current row; it is disabled during an edit.
- **A layout of only `Fixed` columns** renders them, and nothing scrolls, with or without a `HorzResizer`.

`CellClassFn` puts a class on the `<td>`. Pair it with scoped CSS through a wrapper `<div>` of your own:

```css
/* MyPage.razor.css */
.grigliaria ::deep tbody td.err { background-color: lightpink; }
```

### Cells with markup

`DisplayTemplate`'s context is `(Item, Value, RowIdx, Column)`. A column without one clamps its text by itself, to
`LinesToClamp` lines (default 1), with the rest in a popup on hover. A `DisplayTemplate` replaces that, so always wrap
the text it shows in `Clamper`:

```razor
<GridColumn Property="@(x => x.Roles)" Name="Roles" Label="Roles" PreferredSize="40"
            Sortable="false" Filterable="false">
    <DisplayTemplate>
        <Clamper LinesToClamp="1">
            @if (context.Value != null)
            {
                @foreach (var role in context.Value.OrderBy(r => r))
                {
                    <span>&nbsp;□@role</span>
                }
            }
        </Clamper>
    </DisplayTemplate>
</GridColumn>
```

`Sortable="false" Filterable="false"` matters on a collection-valued column. A list cannot be compared, so a
sortable one throws at the first render; and no filter has a meaning the factory can supply for an
`IReadOnlyList<string>`: left on, it hands the column a string contains filter and — on an EF model — composes that
into the query. A collection column that *should* be filterable needs
`Property` plus an explicit `FilterDef`.

A text field in a template gets the typed characters only: the grid keeps its navigation keys (`↑`/`↓`,
`PageUp`/`PageDown`, `Home`/`End`, `←`/`→` with a cell cursor), so they do not move the caret. Edit rows with `EditMode`.

## Filtering

Each column gets an editor chosen by type, which is usually all you need:

```csharp
bool anyActive = grid.IsFiltered();
grid.SetFilter("Price", new NumberDef<decimal> { From = 100 });
grid.SetFilter("Price", null);                       // clear one
IReadOnlyDictionary<string, AFilterDef> all = grid.GetFilters();
```

Writing your own — a multi-select whose candidate values come from the database — is the one genuinely
intricate corner of the library, and it has **two** traps that silently corrupt persisted state. Read
`references/filters.md` first. In brief: a definition holding anything from DI must implement
`IWithInjectedParams<SELF>`, because deserialization constructs it without DI; and if its candidate values are
scoped to something — a company, a tenant, a selected parent — it must record that scope and answer
`IsStillApplicable()`, or a filter restored from a different context is applied with stale keys.

## MultiSelGrid

Adds a checkbox column driven by a selection model, with a tri-state select-all. `Ctrl`+`Space` toggles the
current row. Select-all covers every candidate of the selection model, so the header box is disabled while the
grid is filtered. A row the selection model locks with `selectionEnabledFn` keeps its state, selected or not: select-all,
`Ctrl`+`Space` and a double-click leave it as it is.

```razor
<MultiSelGrid @ref="_gridRef" Model="_model" SelectionModel="_selectionModel" ShowRowNumber="true"
              OnSelectionChanged="() => StateHasChanged()">
    <GridColumn Property="@(x => x.Name)" Label="Role" Name="descr" />
</MultiSelGrid>
```

```csharp
private readonly DataGridModel<Role> _model = new() { ItemKeyFn = r => r.Id };
private SetSelectionModelWithSelectAll<Role, int> _selectionModel =
    new(Enumerable.Empty<Role>(), r => r.Id);

public async Task ShowAsync(Page page)
{
    var roles = await LoadRolesAsync();
    _model.GetData().Set(roles);
    _selectionModel = new(roles, r => r.Id);   // rebuild: it snapshots its source
    foreach (var r in page.Roles)
        _selectionModel.Select(r, true);
}
```

**`OnSelectionChanged` is an `Action`, not an `EventCallback`** — Blazor will not re-render for you, so call
`StateHasChanged()` yourself. The same goes for `OnModelChanged` and `OnLayoutChanged`: they fire from
synchronous paths where there is nothing to await, which is why they are shaped that way.

Rebuild the selection model when the candidate set changes rather than mutating it — the constructor snapshots
its source.

## The rest of the library

`DialogBox` (a thin wrapper over the native `<dialog>`), `Popup` (anchored, transient, only one open at a
time), `Spinner`, `Tabs`, `TriStateCheckbox`, `Clamper`, `PopupValidationMessage`, and the render-nothing
behaviours `KeyAction`, `MouseAction`, `ResizeObserver`, and in `Glazor.Grds` the `Scroller` and `TouchScroller`.

The pairing that comes up most — one app has 46 of them — is a `DialogBox` holding a `MultiSelGrid` for editing
a many-to-many relationship, with
`<KeyAction Key="Escape" OnKey="..." BindAncestorWithTag="div" Enabled="@_editing" />` for keyboard dismissal.
Override `ShouldRender() => _dialog.IsVisible` on such a dialog so a closed one costs nothing.

Show or hide a `Tab` with its `Hidden` parameter, never an `@if`: a `Tab` registers when it is created, so one created after
the first render is placed last, whatever its place in the markup.

These attach their listeners with `addEventListener`, so a handler the application already has on the same
element keeps working — see `references/gotchas.md` § The behaviours for what that does and does not
guarantee.

Every colour and icon is a `--glazor-*` custom property declared in `glazor.css`; `glazor-dark.css`, linked after it,
is the dark theme, `glazor-aurora.css` (light, indigo and teal) and `glazor-paper.css` (light, warm) are two more linked the same way, and an app themes a part of the page by setting the properties on an ancestor. The icons are SVG masks
in the stylesheet, there are no images to replace. The property list is in `docs/Technical_Documentation.md` § Theming.

The components' own texts — button titles, dialog labels, the column menu — are localized: English and Italian ship
in the library and follow `CultureInfo.CurrentUICulture`, so an app that sets the culture from the request with
`UseRequestLocalization` needs nothing else. For other wording or another language the app registers an `IGlazorTexts`
service, keyed by the `TextKeys` constants; the components ask it for every key, so its last arm hands the rest to
`GlazorTexts.Default`. Column headers and enum values are the app's own text and are not touched.

## Traps worth reading before you debug

`references/gotchas.md` collects the failure modes that cost real time. The three that bite most often:

- **`GetDataView().ToList()` throws** on a paged EF model — and so do `.ToArray()` and `new List<>(dataView)`.
  Enumerate or index it instead. This is deliberate: materializing every row is the memory blow-up paging
  exists to prevent.
- **A paged EF grid spills rows to the OS temp directory** by default, MessagePack-serialized. That is a
  data-residency question in some deployments; `SpillDirectory` chooses where the chunks go, `CreateItemsListFn` replaces the list altogether.
