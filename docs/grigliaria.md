# Grigliaria

`Grigliaria<TGridItem>` is Glazor's data grid. It renders one **page** of rows at a time rather than
virtualizing a long scroll region, which is what lets it sit on top of a table with millions of rows without
holding them in memory — and what makes the *vertical resizer* a first-class concept rather than an
afterthought.

- **Namespace:** `Glazor.Grds`
- **Inheritance:** `Grigliaria<T>` → `ADataGrid<T>` → `AGrid<T>` → `ComponentBase`
- **Requires:** an app interactive from the root, with the render mode on `HeadOutlet` and `Routes`, and an
  [`IDataGridModel<T>`](data-models.md)

## Contents

- [Anatomy](#anatomy)
- [Parameters](#parameters)
  - [Data & rendering](#data--rendering)
  - [Sizing](#sizing)
  - [Layout & filtering](#layout--filtering)
  - [Interaction](#interaction)
  - [Notifications](#notifications)
  - [Templates](#templates)
- [Columns](#columns)
- [Resizers](#resizers)
- [Filtering](#filtering)
- [Layout persistence](#layout-persistence)
- [Row dialog](#row-dialog)
- [Editing in place](#editing-in-place)
- [Keyboard & mouse](#keyboard--mouse)
- [Accessibility](#accessibility)
- [Templates and cell styling](#templates-and-cell-styling)
- [Extending Grigliaria](#extending-grigliaria)
- [API reference](#api-reference)

## Anatomy

```razor
<Grigliaria TGridItem="Product" Model="_model">   @* the component  *@
    <GridColumn EFProperty="@(x => x.Code)"       @* the columns    *@
                Name="Code" Label="Code" />
</Grigliaria>
```

Columns are declared as child content. They register themselves with the model on first render, so the set of
columns is discovered from the markup — you don't configure it twice.

Write each `GridColumn` and `EnrichGridColumn` in the grid's own markup, directly or under an `@if` or a `@foreach`. A
column inside a component of your own, or under another component such as a `CascadingValue`, is not supported: it can
render after the grid has collected its columns, and is then missing from the model.

The grid renders, top to bottom: an optional column-group header row, an optional header row, the page of data
rows, and a footer holding the edit buttons, the search button, the row dialog button, the horizontal scroller, the
navigation buttons and the row counter. The layout button is the header's top-left cell, and each column's filter button is in its own header.

## Parameters

### Data & rendering

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `Model` | `IDataGridModel<TGridItem>` | — | **Required.** The row source. See [Data models](data-models.md). |
| `ChildContent` | `RenderFragment?` | `null` | Where the `GridColumn` and `EnrichGridColumn` declarations go, directly or under `@if` and `@foreach`, not inside components of your own. |
| `AriaLabel` | `string?` | `null` | The accessible name of the grid's table (`aria-label`); a page with more than one grid names each. |
| `PageSize` | `int` | `5` | Rows rendered per page. Normally driven by `VertResizer` instead of set directly. |
| `ShowHeader` | `bool` | `true` | Render the column header row. |
| `ShowColumnGroups` | `bool` | `false` | Render the group header row above the headers. See [`GroupId`](#columns). |
| `ShowRowNumber` | `bool` | `true` | Show the `current/total` counter in the footer. |
| `ShowNavigationButtons` | `bool` | `false` | Show the six navigation buttons in the footer: first row, previous page, previous row, next row, next page, last row. Rendered only with `ShowDefaultButtons`. |
| `ShowDefaultButtons` | `bool` | `true` | Show the built-in footer controls: with an `EditMode` the edit, insert and remove buttons, the search button, the horizontal scroller, with `RowDialogEnabled` the row dialog button, with `ReloadEnabled` the reload button and, with `ShowNavigationButtons`, the navigation buttons. |
| `Spinner` | `ISpinner?` | `null` | Shown while the model fetches. **Effectively required for `EFDataGridModel`.** Provided by the application, allocated once in the main layout: see [Architecture › Spinner](Technical_Documentation.md#spinner). |
| `ShouldReRenderTrick` | `Func<bool>?` | `null` | Escape hatch to force a re-render when a parent's `StateHasChanged` would not otherwise reach the grid. Any delegate does: Blazor re-renders a child whose parameter is not of a known-immutable type on every parent render, so the grid follows the parent. The delegate is never called. Forwarded by `Treegliaria` and `MultiSelGrid`. |

### Sizing

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `HeaderHeight` | `int?` | `50` | Header row height in pixels. |
| `RowHeight` | `int?` | `40` | Data row height in pixels. Required by `GridVertResizers.RowHeight`. |
| `VertResizer` | `IGridVertResizer?` | `null` | Computes `PageSize` from available height. See [Resizers](#resizers). |
| `HorzResizer` | `IGridHorzResizer?` | `null` | Computes how many display units fit horizontally. See [Resizers](#resizers). |

### Layout & filtering

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `Layoutable` | `bool` | `false` | Enables the layout dialog: show, hide and reorder columns. The dialog lists each column's preferred size but does not change it. Turning it on creates the layout state that `CreateMemento` captures. Without it, a restored memento's layout is ignored, and stays ignored if the grid becomes layoutable later, and `SetLayout` with a layout throws. While the dialog is open only it changes the grid: leave `Layoutable`, the columns and the model alone until it closes. A column it moves, with its arrows or from the hidden list, is scrolled into the grid's window and, with the cell cursor, becomes the current column. Its Cancel, and Esc, put back the layout it started from, with what a restore set aside for columns not registered, and leave the current column where the moves took it. |
| `ColumnsPopupEnabled` | `bool` | `true` | Right-click a header for the per-column menu: shift left or right (non-fixed columns), sort ascending or descending, filter, clear filter. |
| `FilterDefFactory` | `FilterDefFactory?` | `null` | Overrides which filter editor is chosen per column type, for this grid. Defaults to `FilterDefFactory.Default`, the one the persisted mementos are also read with; a subclass meant for every grid is set there once, at startup. |

### Interaction

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `CellCursorEnabled` | `bool` | `true` | Track a *current cell* as well as a current row, enabling `←`/`→` navigation and the `ColIdx` in `OnDblClick`. |
| `GotoLastKnown` | `bool` | `true` | `End` jumps to the last row already fetched rather than forcing a full read to the end. |
| `TouchScrollerEnabled` | `bool` | config, else `false` | Two-finger touch scrolling over the grid body. Unset on the component, the default comes from configuration — see below. |
| `RowsPopupEnabled` | `bool` | `false` | Enable the row context menu, reported through `OnRowPopup`. |
| `RowDialogEnabled` | `bool` | `false` | Adds a footer button that opens the current row as a list of its columns, those out of view included. See [Row dialog](#row-dialog). |
| `ReloadEnabled` | `bool` | `false` | Adds a footer button that reloads the data with `ForceReload(true)`, keeping the filters, the sort and the current row, when the model is an `IReloadableGridModel`, as the EF models are; disabled during an edit. `MultiSelGrid` and `Treegliaria` take it too. |
| `EditMode` | `EditMode` | `Disabled` | `Manual`, `OnClick` or `OnMove` edits rows in place over an `IEditableGridModel`. See [Editing in place](#editing-in-place). |
| `NewItem` | `Func<TGridItem?, TGridItem>?` | `null` | The row Ctrl+Ins inserts, made from the current one; without it the grid inserts nothing. |

#### Configuring the touch scroller application-wide

`TouchScrollerEnabled` is the one parameter in the library with an application-level default, and the
only setting Glazor reads from `IConfiguration` at all. When the parameter is **not** set on the
component, the grid takes its value from `appsettings.json`:

```json
{
  "Grigliaria": {
    "TouchScrollerEnabled": true
  }
}
```

So the `false` in the table is the fallback when the key is absent, not a fixed default. Setting the
parameter explicitly always wins, which is how you opt one grid out of an application-wide `true`:

```razor
<Grigliaria TGridItem="Product" Model="_model" TouchScrollerEnabled="false" ... />
```

> [!NOTE]
> The configured value is read the first time a grid needs it and then cached, so it is not picked up
> by a configuration reload. Change it and restart.

`Treegliaria` and `MultiSelGrid` do not read the setting: each always passes its own `TouchScrollerEnabled`, `false`
unless set on it, to the grid inside, so the touch scroller is turned on per component there.

### Notifications

| Parameter | Type | Description |
| --- | --- | --- |
| `OnDblClick` | `EventCallback<DblClickEvent>` | Row double-clicked; not raised when the grid edits in place, where a double-click edits the row. `ColIdx` is non-null only when `CellCursorEnabled`, and not on the row heading. On `MultiSelGrid` and `Treegliaria` it counts your model's columns, and is null on the selection or expansion column; a tree drawn in a `TreeColumn` has no expansion column, so a double-click there gives that column's index. |
| `OnRowPopup` | `EventCallback<RowPopupEvent>` | Row context menu requested. `RowIdx` is `null` when the click was below the last row; `ClientX` and `ClientY` are where the pointer is, to open a `Popup` there with `ShowAsync(x, y)`. |
| `OnPost` | `EventCallback<GridPost<TGridItem>>` | A row edited in place passed validation and is posted: save it and call the model's `PostAsync`. |
| `OnRemove` | `EventCallback<TGridItem>` | Ctrl+Del on the current row, with an `EditMode`: confirm if you like, delete it, and drop it from the model. |
| `OnStableItemChanged` | `EventCallback<TGridItem?>` | The current item has stayed put long enough to be considered settled — use this to drive a detail panel without thrashing it during keyboard navigation. |
| `OnModelChanged` | `Action<GridModelChangeType>?` | Something in the model changed (`AfterSync` or `Other`). `GridModelChangeType` is in `Glazor.Grds.Mdls`, not `Glazor.Grds`. |
| `OnLayoutChanged` | `Action?` | Visible columns, their order, or a column width changed. |
| `OnTimeout` | `Func<GridTimeoutException, bool>?` | A fetch on a paged model ran past its time budget (about 70 s). Return `true` to handle it and keep the grid up, `false` or leave it unset to let the exception propagate. `WorkStopped` says whether the query has actually stopped; `Wait(timeout)` gives it more time. |

> [!IMPORTANT]
> `OnModelChanged` and `OnLayoutChanged` are **`Action`, not `EventCallback`**. That is deliberate: they fire
> from synchronous paths (a render, a property setter, the model's own event) where awaiting is not possible. The consequence is that
> Blazor will **not** re-render your component automatically when they run — call `StateHasChanged()` yourself
> if the handler changes something you display.

### Templates

| Parameter | Type | Context | Description |
| --- | --- | --- | --- |
| `CellTemplate` | `RenderFragment<IGrigliaria<TGridItem>.CellTemplateParam>?` | `(Grid, Model, RowIdx, ColIdx, Column)` | Replace how *every* data cell is drawn. For one column, prefer `GridColumn.DisplayTemplate`. |
| `RowHeadingTemplate` | `RenderFragment<IGrigliaria<TGridItem>.RowHeadingTemplateParam>?` | `(Grid, Model, RowIdx)` | Replace the leading cell that shows row state (current, editing, …). |
| `HCellTemplate` | `RenderFragment<IGrid<TGridItem>.HCellTemplateParam>?` | `(Grid, Model, ColIdx, Column, ColSpan, RowSpan, ToShow)` | Replace header cells. |
| `GHCellTemplate` | `RenderFragment<IGrid<TGridItem>.GHCellTemplateParam>?` | `(Grid, Model, GroupId, ColSpan, RowSpan, ColIdxs)` | Replace group header cells. |
| `EmptyRowCellTemplate` | `RenderFragment<IGrid<TGridItem>.EmptyRowTemplateParam>?` | `(Grid, Model, RowIdx)` | Replace the heading cell of each filler row drawn below the last row: one `<td>`, the grid draws the rest of the row. |

On `MultiSelGrid` and `Treegliaria` the contexts carry the inner grid's own model and column indexes, which count the
selection or expansion column, unlike `OnDblClick`; under a `TreeColumn` the tree has no expansion column, and they
match your model's. Tell a column by its name, `context.Model.GetColumnName(context.ColIdx)`, not by an index taken from
your model.

## Columns

`GridColumn<TGridItem, VT>` declares one column. `VT` is inferred from the property expression.

```razor
<GridColumn EFProperty="@(x => x.Price)" Name="Price" Label="Price"
            PreferredSize="10" Alignment="Alignment.Right" GroupId="Money" />
```

A `GridColumn` inside an `@if` comes and goes with it: when the `@if` turns false, the column leaves the model, with its
filter and its sort, at the next render, or the first one after a pending edit ends. The same holds for columns written with
`@foreach`, provided each has a `Name` or a `@key`: a column that leaves the list leaves the grid. Without either, Blazor
reuses the components by position, and a column named after its label keeps the name of the one that was there before.

### Choosing the value accessor

One of these is required — supplying both, or neither, throws at parameter-set time. `EditProperty`, below, can stand in for
`Property`.

| | Type | Behaviour |
| --- | --- | --- |
| `EFProperty` | `Expression<Func<TGridItem, VT>>` | An expression tree. With `EFDataGridModel`, sorting and filtering on this column are **translated to SQL**. |
| `Property` | `Func<TGridItem, VT>` | A compiled delegate. Always evaluated **in memory**, never pushed to the database. It must keep reading the same values: a sort or a filter already applied is not redone when it changes, so to show other values use another `Name`. |

Use `EFProperty` for anything mapped to a column. Use `Property` for computed or navigated values — a
projection over a collection, a value assembled from several fields:

```razor
@* Pushed down to SQL *@
<GridColumn EFProperty="@(x => x.LoginDateTime)" Name="LoginDateTime" Label="Logged at" />

@* Computed in memory *@
<GridColumn Property="@(x => x.Category.Description)" Name="Description" Label="Description" />
```

### Reference

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `Label` | `string` | — | **Required.** Header text. |
| `Name` | `string` | the first `Label` | Stable identity used by the layout, filters, sort and mementos. Without it the column is named after its first `Label`, and a later label changes only the header; passed, it must have a value, as a null or empty `Name` throws at the first render. **Set it explicitly if the label can differ from one session to another** (localization, dynamic headers) — otherwise a saved memento stops matching: `RestoreMemento` drops the unknown column from the layout, and sets its filter and its sort aside with a warning, to apply them only if a column of that name registers later, while `SetLayout` and `SetColumnsToShow` treat an unknown name as an error. Set it too on columns written with `@foreach` without `@key`. Names must be unique within a grid: two columns with one name, or with one label and no `Name`, collapse into one. |
| `PreferredSize` | `int` | by type | Width in display units. Defaults from `VT`: `string` 20, `DateTime` and `DateTimeOffset` 12, numeric 8, `enum` 10, `bool` 6, otherwise 10. 0 keeps the column out of the default set and out of the layout dialog; a layout or `SetColumnsToShow` naming it still shows it, with no width of its own, and the dialog leaves it where it is. |
| `Sortable` | `bool` | `true` | Allow sorting by this column. Its values must be comparable: a type that implements `IComparable<T>` or `IComparable`, nullable or not, or `object`, unless the model supplies a comparer through `GetComparerForColumn`. A sortable column of another type, a record, a class or a collection, throws `InvalidOperationException` at the first render, even when the database sorts it: set `Sortable="false"` on it. |
| `Filterable` | `bool` | `true` | Allow filtering on this column. |
| `FilterDef` | `AFilterDef<VT>?` | `null` | Supply a specific filter editor instead of letting the factory choose. Without `VT`, the column's type follows `Property` and `FilterDef` together: `NumberDef<int>` on an `int` property makes the column `int?`, `BoolDef` on a `bool` makes it `bool?`, and `EnumDef` makes it an `Enum` column. A column whose type is given, by `VT` or by a `Column<T, int>` in code, takes only a definition of that type: a built-in one only when the type is nullable, and `EnumDef` only on `Enum`; for the others, return it from an override of `FilterDefFactory.CreateDefinition`. See [Filtering](#filtering). |
| `Fixed` | `bool` | `false` | Pin to the left; excluded from horizontal scrolling and reordering. |
| `ShownByDefault` | `bool` | `true` | Whether the column is visible before the user customizes the layout. The grid starts with the fixed columns, then those shown by default with a size, in their order; with none, it shows no column until `SetColumnsToShow` or a restore names some. `SetColumnsToShow` shows another set from code. |
| `ShownInRowDialog` | `bool?` | `null` | Whether the [row dialog](#row-dialog) lists the column. Unset, it does unless the column is `Fixed` or has no label; `true` or `false` decides either way. A column of size 0 is never listed. |
| `Alignment` | `Alignment` | `Default` | `Default`, `Left`, `Center`, `Right`. `Default` aligns numbers right, dates, enums and booleans center, the rest left. |
| `GroupId` | `string?` | `null` | Groups adjacent columns under a shared header, which shows this text; a `GHCellTemplate` draws something else. Needs `ShowColumnGroups="true"` on the grid. |
| `LinesToClamp` | `int` | `1` | Clamp cell text to N lines with an ellipsis. `0` or less disables clamping. |
| `HeaderLinesToClamp` | `int` | `1` | Same, for the header cell. |
| `DisplayTemplate` | `RenderFragment<DecoratorContext<TGridItem, VT>>?` | `null` | Custom cell rendering. Context is `(Item, Value, RowIdx, Column)`. |
| `HeaderDisplayTemplate` | `RenderFragment<HDecoratorContext<TGridItem>>?` | `null` | Custom header rendering. Context is `(Column)`. |
| `CellClassFn` | `Func<DecoratorContext<TGridItem, VT>, string?>?` | `null` | Return CSS classes for the cell's `<td>`. See [cell styling](#templates-and-cell-styling). |
| `EditProperty` | `Expression<Func<TGridItem, VT>>?` | `null` | The property an editor writes when the grid edits rows in place, a plain one with a setter. Alone it is also where the value is read; with `Property` it only names what to edit; with `EFProperty` it throws, as an `EFProperty` on a plain property is edited without it. See [Editing in place](#editing-in-place). |
| `Editable` | `bool` | `true` | False keeps the column read-only when the grid edits rows in place. |
| `EditTemplate` | `RenderFragment<DecoratorContext<TGridItem, VT>>?` | `null` | The column's editor in the row being edited, under the row's `EditContext`: it binds what it likes, `context.Item` included. |

### Enriching a column declared in code

When the model already defines a column (through `AddColumn`/`AddEFColumn`), `EnrichGridColumn` attaches
templates to it from markup without redeclaring it:

```razor
<EnrichGridColumn TGridItem="Product" VT="decimal" Name="Price">
    <DisplayTemplate>@context.Value.ToString("C")</DisplayTemplate>
</EnrichGridColumn>
```

`Name` must match the model column's name, and the column has to exist by the time the component registers:
place an `EnrichGridColumn` after the `GridColumn` it enriches, or use it only for columns the model defines
itself. Enriching a name the model does not know throws `ArgumentException("Column doesn't exist")`.
`VT` types the templates' `context` only: the column keeps its own value type, which picks its filter and its sort.
`MultiSelGrid`'s selection column keeps its checkboxes: the grid enriches it after your content, so an enrichment
of it does not show.

An enrichment stays on the column until another one replaces it: an `EnrichGridColumn` that leaves the render tree, under
an `@if` that turns false, does not take its templates away. To switch templates, give every branch its own
`EnrichGridColumn`, the plain one included, since a template left out keeps the one the column has:

```razor
@if (_highlight)
{
    <EnrichGridColumn TGridItem="Product" VT="string" Name="Name">
        <DisplayTemplate><b>@context.Value</b></DisplayTemplate>
    </EnrichGridColumn>
}
else
{
    <EnrichGridColumn TGridItem="Product" VT="string" Name="Name">
        <DisplayTemplate>@context.Value</DisplayTemplate>
    </EnrichGridColumn>
}
```

### Fixed columns

When every column shown is fixed, as the layout dialog allows by moving all the others out, the grid shows the fixed
columns and nothing scrolls horizontally, with a `HorzResizer` or without one.

## Resizers

`Grigliaria` pages rather than virtualizes, so it has to decide *how many rows* and *how many columns* fit.
That's what resizers do: they observe a reference element and convert its measured size into a page size or a
display-unit count.

### Vertical — how many rows

```csharp
GridVertResizers.Fixed(pageSize)
GridVertResizers.RowHeight(HeightRefStyles, MinPageSize, MaxPageSize)
GridVertResizers.Proportional(HeightRefStyles, MinPageSize, MaxPageSize,
                              ReferencedMinGridHeight, ReferencedMaxGridHeight)
```

`RowHeight` is the usual choice. Before the grid is drawn it estimates: it subtracts `HeaderHeight` from the measured
height, divides by the grid's `RowHeight` (which must be set) and drops one row for the space the chrome takes. Once
drawn, it measures what the browser drew, the rows, the header, the footer and the scrollbar, and settles on the largest
page that fits the height, so nothing below the grid is overlapped. Either way it clamps to `[min, max]`.
The factories check their arguments: sizes start at 1, `Proportional` needs `Min < Max` on both pairs, and a blank
`HeightRefStyles` is refused with an `ArgumentException`.

```razor
<Grigliaria RowHeight="40"
            VertResizer="@(GridVertResizers.RowHeight("height: 70vh;", 4, 20))" ... />
```

The first argument is a **CSS height applied to an internal reference element** — `"height: 70vh;"`,
`"height: 100%;"`, whatever expresses the space you want the grid to claim. Because it's real CSS, the grid
re-pages when the viewport changes.

A page can have two stable layouts around the grid, each giving it the height for the other's page size: with one more row
a scrollbar appears, the page narrows, a title above the grid wraps and takes a row's height away; with that row gone the
scrollbar goes and the height comes back. When the page size goes back to the one it had twice in a row, each time within two
seconds, the grid keeps the smaller one while the height alternates between those two values, logs a warning once, and follows the next
resize that gives another height. `scrollbar-gutter: stable` on the scroll container keeps such a page at one width, and the grid from losing that row.

A resizer of your own implements `IGridVertResizer`: `GetPageSize` gets the grid and the measured height and returns the
rows to show. The grid's `RowHeight`, `HeaderHeight` and `GetDrawnRows()` are what `RowHeight` reads; `GetDrawnRows()` is
null until the grid has been drawn and measured, then the rows drawn, the height of their content and a row's height.

### Horizontal — how many columns

```csharp
GridHorzResizers.Fixed(dispUnits)
GridHorzResizers.DispUnitWidth(MinDispUnits, MaxDispUnits, DispUnitWidth)
GridHorzResizers.Proportional(MinDispUnits, MaxDispUnits,
                              ReferencedMinGridWidth, ReferencedMaxGridWidth)
```

`DispUnitWidth(50, 150, 12)` reads as: each display unit is 12 px wide, and the grid will use between 50 and
150 units depending on how much width it actually has. Columns are then admitted left to right until the
budget runs out, which is what drives horizontal paging and the `Ctrl+←`/`Ctrl+→` shortcuts.
The factories check their arguments: units start at 1 and `Proportional` needs `Min < Max` on both pairs.

Omit `HorzResizer` entirely and horizontal paging is disabled — every visible column is rendered, and the grid leaves
`Ctrl+←`/`Ctrl+→` to what is around it. A resizer of your own
implements `IGridHorzResizer`: `GetDisplayedUnits` gets the grid and its measured width and returns the display units to
fill, or -1 for every column.

## Filtering

Each column gets a filter editor appropriate to its type, chosen by `FilterDefFactory`. The user opens it from
the filter button in the column's header or from the column's header menu.

Built-in definitions in `Glazor.Grds.Filters`:

| Filter | For |
| --- | --- |
| `DefaultDef` | The columns without a filter of their own (strings, `Guid`, `TimeSpan`, `object`, custom types), matched on their text — contains or starts-with, by its `Contains` flag. The column keeps its own type; on an EF model the text is the database's own cast. In memory the match ignores case; pushed down it is the database's own, so case follows its collation |
| `NumberDef`, `DateDef` | Ranges over numbers and dates. A `DateTimeOffset` bound is typed as a local date and time and takes the server's offset, not the browser's. The editor picks a date and time to the minute, so a date's "To" covers the whole minute it names, 10:30 keeping 10:30:45, or the whole day when a subclass picks dates only; a "To" set in code with seconds ends where it is |
| `BoolDef` | Tri-state: true / false / all, with an include-nulls option |
| `EnumDef` | Multi-select over the enum's members. An enum marked `[Flags]` is not supported: a combined value such as `A \| B` is kept only when that exact value is selected. It persists the names of the members selected, so in a restored one a member added to the enum since, or renamed, is not selected and its rows are filtered out, also when the filter saved only left out the empty values |
| `SetDef`, `DynaSetDef` | Multi-select over a fixed or lazily-loaded value set. `SetDef` persists the values themselves, so its value type must compare by value (a string, a record, or a type overriding `Equals`); over entities use a `DynaSetDef` with a key. It persists its candidates too, so a restored one offers the values it was saved with: when they change over time, use a `DynaSetDef`, which reads them again each time its dialog opens |
| `EFDistinctDef` | Multi-select over the distinct values the column has in a query, read each time its dialog opens. Ready to use, with nothing to derive, and the column may hold a value type too. In `Glazor.EntityFrameworkCore`: see [Offering the values a column has](#offering-the-values-a-column-has) |
| `EFValuesInSetDef` | Multi-select over a collection column, with dialog rows read from a query and shown in columns of your own. Ready to use, with nothing to derive, and the items may be a value type, ids for instance. In `Glazor.EntityFrameworkCore`: see [A dialog with rows of its own](#a-dialog-with-rows-of-its-own) |
| `AValuesInSetDef` subclasses | Columns whose value is itself a collection — matches on intersection |

**The "Include null" box** is shown only for a column whose value can be null: a `Nullable<T>`, or a reference type
registered with an expression (`EFProperty`, `AddEFColumn`) whose member chain the compiler's nullable annotations do
not mark non-null all the way (`string?`, or a path through a nullable navigation; a plain `string` property hides the
box). A `Func` accessor (`Property`, `AddColumn`), a computed expression or a build without nullable annotations keeps
the box. A restored filter with `IncludeNull` on a column that hides it is harmless: there is no null to include.

**Every value selected** in a set whose dialog reads its values means every value, those listed and any added since: with
"Include null" the filter is void, without it any value but null passes. With one value left out the filter is the values
selected, and a value added later does not pass. A set over a collection does the same with "Include null or empty".

### Supplying your own filter

Set `FilterDef` on the column. This is how you filter a collection-valued column, or provide the candidate
values from a database:

```razor
<GridColumn Property="@(x => x.Roles.AsQueryable())" Name="Roles" Label="Roles"
            Sortable="false" FilterDef="_rolesFilter">
    <DisplayTemplate>
        <Clamper LinesToClamp="1">
            @if (context.Value != null)
            {
                @foreach (var role in context.Value.OrderBy(r => r.Name))
                {
                    <span>&nbsp;□@role.Name</span>
                }
            }
        </Clamper>
    </DisplayTemplate>
</GridColumn>
```

```csharp
// A DynaValuesInSetDef subclass that loads its candidate values from EF, written below
private RolesFilterDef _rolesFilter = null!;

protected override void OnInitialized() =>
    _rolesFilter = new(startFull: true) { DbContextFactoryFn = () => DbContextFactory };
```

`Property` with `.AsQueryable()`, not `EFProperty`: the match runs in memory against the collection, while the candidates
come from the definition's own query.

`RolesFilterDef` derives from `DynaValuesInSetDef<SELF, VT, KT>`: the definition's own type, the type of the collection's
items and their key. Its dialog lists the items themselves; for rows of another type see
[A dialog with rows of its own](#a-dialog-with-rows-of-its-own). It supplies the grid the user picks values in,
`CreateAllValuesModel`, with the columns it adds to the model; the candidates,
`RetrieveAllValuesAsync`, called each time the dialog opens; `Clone`; `Read`, for a persisted memento; and a
parameterless constructor, which may be non-public, for the deserializer. The grid clones a definition before it edits
or applies it, so every subclass overrides `Clone` to return its own type, a subclass of a built-in definition such as
`DateDef` too: the built-in `Clone` returns the built-in type.

```csharp
public class RolesFilterDef : DynaValuesInSetDef<RolesFilterDef, Role, int>, IWithInjectedParams<RolesFilterDef>
{
    // A Func, not the factory: the definition may be built before the page's injected properties are set
    public Func<IDbContextFactory<AppDbContext>> DbContextFactoryFn { get; init; } = null!;

    protected RolesFilterDef() : this(false) { }   // the deserializer's prototype
    public RolesFilterDef(bool startFull) : base(true, r => r.Id, startFull) { }

    public override RolesFilterDef Clone() => new(StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,
        IncludeNullOrEmpty = IncludeNullOrEmpty,
        AllKeys = AllKeys == null ? null : new(AllKeys),
        Selected = new(Selected),
    };

    // Columns go on the model directly, not as markup
    protected override DataGridModel<Role> CreateAllValuesModel()
    {
        DataGridModel<Role> model = new() { ItemKeyFn = r => r.Id };
        model.AddColumn("id", new Column<Role, int>("Id") { Alignment = Alignment.Center, PreferredSize = 5 }, r => r.Id);
        model.AddColumn("name", new Column<Role, string?>("Role") { PreferredSize = 20 }, r => r.Name);
        return model;
    }

    protected override async ValueTask<IList<Role>> RetrieveAllValuesAsync(CancellationToken ct)
    {
        using AppDbContext db = await DbContextFactoryFn().CreateDbContextAsync(ct);
        return await db.Roles.AsNoTracking().OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync(ct);
    }

    public override RolesFilterDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        return new(ser.StartFull)   // true only when it was saved full and never populated
        {
            IncludeNullOrEmpty = ser.IncludeNullOrEmpty,
            AllKeys = ser.AllKeys?.ToHashSet(),
            Selected = ser.Selected.ToHashSet(),
        };
    }

    public RolesFilterDef InjectParamsInto(RolesFilterDef toBeInjected) => new(toBeInjected.StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,                // the column's own
        IncludeNullOrEmpty = toBeInjected.IncludeNullOrEmpty,   // the restored one's
        AllKeys = toBeInjected.AllKeys,
        Selected = toBeInjected.Selected,
    };
}
```

**A definition that holds something from the page implements `IWithInjectedParams<SELF>`.** A restored memento builds its
definitions with the parameterless constructor, so `DbContextFactoryFn` comes back null and the dialog would throw when it
opens. `RestoreMemento` gives each restored definition that implements the interface to the column's own definition, and
applies the one its `InjectParamsInto` returns: the plumbing comes from `this`, the user's choices from `toBeInjected`.
Reversed, the filter comes back with the right factory and the wrong selection. A restored definition that the column's own
does not take is dropped with a warning.

### Offering the values a column has

`EFDistinctDef<TDbContext, TGridItem, VT>`, in `Glazor.EntityFrameworkCore`, is a multi-select over the distinct values a
column has in a query. There is nothing to derive: say where the rows are and which value to read.

```razor
<GridColumn EFProperty="@(o => o.Status)" Name="Status" Label="Status" FilterDef="_statusFilter" />
```

```csharp
private readonly EFDistinctDef<AppDbContext, Order, string> _statusFilter;

public Orders() => _statusFilter = new(startFull: true)
{
    DbContextFactoryFn = () => DbContextFactory,
    QueryFn = db => db.Orders.Where(o => o.CustomerId == _customerId),
    ColumnExpr = o => o.Status,
};
```

- **`QueryFn`** gives the rows whose values are offered. Give it the grid's base query, and a user is offered the values
  the grid can show and no other: the values of the whole table would list what a grid narrowed to their rows never shows.
- **`ColumnExpr`** is the value read from a row. `VT` is the column's type, the nullable one for a nullable column
  (`int?`, `string?`), and need not be a reference type: a number, a `Guid` or a date do too. Nulls are left out of the list: the
  "Include null" box stands for them.
- **`ColumnsFn`**, optional, adds the dialog's columns to its model, and may sort it, as on `EFValuesInSetDef` below. Each
  row is a `DistinctRowValue<VT>`, and a column shows what it derives from its `Value`, a label for a code for instance.
  The selection holds the values whatever the columns show, so a column of codes keeps filtering on its codes:

```csharp
ColumnsFn = (model, texts) =>
{
    model.AddColumn("status", new Column<DistinctRowValue<string>, string>("Status"), r => _labels[r.Value]);
    model.SetSortedColumn("status", true);
},
```

The values are read each time the dialog opens, and listed one per row: without `ColumnsFn` in one column, "Value",
sorted when their type can be compared. The
selection is persisted with the memento's options, so `VT` is a type System.Text.Json writes; what the page supplies is
not persisted, and a restored definition takes it from the column's own, as `IWithInjectedParams` describes above. With an
allow-list of persisted types, list the closed type: `typeof(EFDistinctDef<AppDbContext, Order, string>)`.

For values that do not come from EF, derive from `DistinctDef<SELF, VT>` and implement `RetrieveValuesAsync`, `Clone` and
`Read`. Its dialog lists the values in one column; override `CreateAllValuesModel` to change it. A dialog that shows more
than the value has rows of its own: see the next section.

### A dialog with rows of its own

A set definition reads its candidates when its dialog opens and lists them in a small grid, a checkbox on each row. The
rows of `DynaSetDef` are the column's values, those of `DynaValuesInSetDef` the collection's items. For a dialog that shows
something else, a name beside a code or fields of another table, derive from the general base, which has a row type of its
own, `RT`, a class:

| The column holds | Base | Editor to derive from | Its box |
| --- | --- | --- | --- |
| a value, `VT` | `ADynaSetDef<SELF, VT, RT, KT>` | `ADynaSetDefRenderer<SELF, VT, RT, KT>` | "Include null", `IncludeNull` |
| a collection, `IQueryable<VT>` | `ADynaValuesInSetDef<SELF, VT, RT, KT>` | `ADynaValuesInSetDefRenderer<SELF, VT, RT, KT>` | "Include null or empty", `IncludeNullOrEmpty` |

`DynaSetDef`, `DistinctDef` and `DynaValuesInSetDef` are these bases with `RT` fixed. The selection, the memento and the
predicate hold keys, `KT`, only: a value or an item gives its key through the expression passed to the base constructor,
and a row through `RowToKey`, so the two must agree. `VT` may be a value type, a collection of ids for instance. A subclass
implements:

- **`CreateAllValuesModel()`**, the dialog's grid: a `DataGridModel<RT>` with one column per field to show, added with
  `AddColumn`, since there is no markup to declare them in. It may sort it, `SetSortedColumn`, and label its columns with
  `Texts`, the dialog's `IGlazorTexts`, for labels that follow the culture.
- **`RetrieveAllValuesAsync(ct)`**, the rows to offer, read when the dialog opens; `ct` goes on to the query.
- **`RowToKey(RT)`**, a row's key.
- **`Renderer()`**, the editor. The bases name none: a one-line class deriving from the editor in the table, typed by the
  definition, is enough.
- **`Clone`**, **`Read`** and a parameterless constructor, as every definition, and `IWithInjectedParams` when it holds
  something from the page.

A column of country codes whose dialog lists each country with its name and continent:

```razor
<GridColumn EFProperty="@(c => c.CountryCode)" Name="Country" Label="Country" FilterDef="_countryFilter" />
```

```csharp
_countryFilter = new(startFull: true) { DbContextFactoryFn = () => DbContextFactory };
```

```csharp
public sealed record Country(string Code, string Name, string Continent);   // the dialog's rows

public class CountryFilterDef : ADynaSetDef<CountryFilterDef, string, Country, string>, IWithInjectedParams<CountryFilterDef>
{
    public Func<IDbContextFactory<AppDbContext>> DbContextFactoryFn { get; init; } = null!;

    protected CountryFilterDef() : this(false) { }   // the deserializer's prototype
    public CountryFilterDef(bool startFull) : base(code => code, startFull) { }

    protected override string RowToKey(Country row) => row.Code;   // the key the column's value gives

    protected override DataGridModel<Country> CreateAllValuesModel()
    {
        DataGridModel<Country> model = new() { ItemKeyFn = c => c.Code };
        model.AddColumn("code", new Column<Country, string>("Code") { PreferredSize = 6 }, c => c.Code);
        model.AddColumn("name", new Column<Country, string>("Country") { PreferredSize = 20 }, c => c.Name);
        model.AddColumn("continent", new Column<Country, string>("Continent") { PreferredSize = 12 }, c => c.Continent);
        model.SetSortedColumn("name", true);
        return model;
    }

    protected override async ValueTask<IList<Country>> RetrieveAllValuesAsync(CancellationToken ct)
    {
        using AppDbContext db = await DbContextFactoryFn().CreateDbContextAsync(ct);
        return await db.Countries.AsNoTracking().Select(c => new Country(c.Code, c.Name, c.Continent)).ToListAsync(ct);
    }

    public override CountryFilterDef Clone() => new(StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,
        IncludeNull = IncludeNull,
        AllKeys = AllKeys == null ? null : new(AllKeys),
        Selected = new(Selected),
    };

    public override CountryFilterDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        return new(ser.StartFull)
        {
            IncludeNull = ser.IncludeNull,
            AllKeys = ser.AllKeys?.ToHashSet(),
            Selected = ser.Selected.ToHashSet(),
        };
    }

    public CountryFilterDef InjectParamsInto(CountryFilterDef toBeInjected) => new(toBeInjected.StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,   // the column's own
        IncludeNull = toBeInjected.IncludeNull,    // the restored one's
        AllKeys = toBeInjected.AllKeys,
        Selected = toBeInjected.Selected,
    };

    private static readonly FilterDefRendererType<CountryFilterDefRenderer> rendererType = new();

    public override FilterDefRendererType Renderer() => rendererType;
}

// The bases name no editor: a one-line one, typed by the definition
public class CountryFilterDefRenderer : ADynaSetDefRenderer<CountryFilterDef, string, Country, string> { }
```

Over a collection the definition is the same, deriving from `ADynaValuesInSetDef`, with `IncludeNullOrEmpty` in place of
`IncludeNull` and its editor deriving from `ADynaValuesInSetDefRenderer`; the column is an `IQueryable<VT>`, as in
[Supplying your own filter](#supplying-your-own-filter).

When the rows come from EF, `EFValuesInSetDef<TDbContext, VT, RT, KT>`, in `Glazor.EntityFrameworkCore`, is that definition
for a collection, ready to use: there is nothing to derive, `Clone`, `Read`, `IWithInjectedParams` and the editor included.
The constructor takes the item's key expression, `startFull` and, optionally, `alwaysInMemory`. A column of actor ids, a
film's cast, whose dialog lists the actors with their number of films:

```razor
<GridColumn EFProperty="@(r => r.Cast.AsQueryable())" Name="Cast" Label="Cast" Sortable="false" FilterDef="_castFilter" />
```

```csharp
public sealed record ActorRow(int Id, string Name, int Films);

private EFValuesInSetDef<AppDbContext, int, ActorRow, int> _castFilter = null!;

protected override void OnInitialized() => _castFilter = new(id => id, startFull: true)
{
    DbContextFactoryFn = () => DbContextFactory,
    QueryFn = db => db.Actors.Select(a => new ActorRow(a.ActorId, a.FirstName + " " + a.LastName, a.Films.Count)),
    RowKeyFn = a => a.Id,
    ColumnsFn = (model, texts) =>
    {
        model.AddColumn("name", new Column<ActorRow, string>("Actor") { PreferredSize = 20 }, a => a.Name);
        model.AddColumn("films", new Column<ActorRow, int>("Films") { PreferredSize = 6 }, a => a.Films);
        model.SetSortedColumn("name", true);
    },
};
```

- **`QueryFn`** gives the rows to choose from, read without tracking each time the dialog opens.
- **`RowKeyFn`** is a row's key, which must equal the key the constructor's expression gives an item.
- **`ColumnsFn`**, optional, adds the dialog's columns to its model, and may sort it; `texts` is the dialog's `IGlazorTexts`,
  for labels that follow the culture. The model finds a row by `RowKeyFn`. Without it the dialog lists each row's
  `ToString()` in one column, "Value", sorted, as `EFDistinctDef` lists its values.

What the page supplies, the key expression included, is not persisted: a restored definition takes it from the column's own,
as `IWithInjectedParams` describes above. With an allow-list of persisted types, list the closed type. The demo's rentals
page has such a column.

### Reading and setting filters in code

```csharp
bool anyActive = grid.IsFiltered();

grid.SetFilter("Price", new NumberDef<decimal> { From = 100 });
AFilterDef? current = grid.GetFilter("Price");
grid.SetFilter("Price", null);                       // clear one

IReadOnlyDictionary<string, AFilterDef> all = grid.GetFilters();
grid.SetFilters(all);                                // restore in bulk
```

The grid keeps a copy of what `SetFilter` and `SetFilters` get, so a definition the page goes on changing does not change
the filter. `GetFilters` returns a copy of the set, which later changes leave as it is, so it can be kept and restored. The
definitions `GetFilter` and `GetFilters` return are the grid's own: to change one, change its `Clone()` and pass that to
`SetFilter`.

With `EFDataGridModel` and `EFProperty` columns, these become `WHERE` clauses. With `Property` columns — or
any model backed by a list — they are applied in memory.

## Layout persistence

With `Layoutable="true"` the user can hide and reorder columns, and host code can set widths with `SetPreferredSize`. `CreateMemento()` captures that
layout **together with** the active filters and sort order; `RestoreMemento()` puts it back.

```csharp
public record Memento(
    GridLayout? Layout,
    IReadOnlyDictionary<string, AFilterDef> FiltersDefs,
    IReadOnlyList<SortedColumn> SorterColumns);
```

`Memento` is a serializable record, so you can round-trip it through JSON (`System.Text.Json`), session state or a
user-preferences table. `Treegliaria` over an `ITreeStateGridModel` returns a `TreeMemento`, derived from it, with the nodes expanded
and the zoom, `{"$type":"tree","Tree":{"Expanded":[[1],[1,7]],"Zoom":null},"Layout":…}`: as JSON it starts with
`"$type": "tree"`, so a store that holds every grid's state as a `Memento` reads it back
as a `TreeMemento`, and a memento with no `"$type"` reads as a plain one. The `"$type"` is written when the memento is
serialized as a `Memento`, its declared type, or as an `object`: `JsonSerializer.Serialize(memento, memento.GetType())`
writes none, and the tree state reads back lost. A store that reorders the keys, PostgreSQL's
`jsonb` for one, moves `"$type"` from the front: read it back with `AllowOutOfOrderMetadataProperties = true` in the
`JsonSerializerOptions`. The pattern below saves it whenever the layout or the model settles, and restores it
on first render:

> [!NOTE]
> Persistence relies on the reflection-based `System.Text.Json` serializer and on runtime type loading: `GridLayout` is read
> and written through non-public members, and a filter definition is restored by `Type.GetType` on its persisted type name
> and `Activator.CreateInstance`. A host that turns reflection off (`JsonSerializerIsReflectionEnabledByDefault` false), serializes
> through a source-generated `JsonSerializerContext`, trims or publishes ahead-of-time cannot persist mementos. Glazor as a
> whole is not trimmable and has not been tested under AOT.
>
> Leave `DictionaryKeyPolicy` unset in the options a memento goes through: filters and column sizes are keyed by column name,
> and System.Text.Json renames a key when it writes, not when it reads, so the restore would set them aside as columns the
> grid does not have. A filter definition kept outside a memento is persisted typed as `AFilterDef`, as `Memento` holds it:
> the converter on `AFilterDef` is not inherited, so a property typed `NumberDef<int>` is written without its type name, in a
> shape the factory does not read, and one typed `EnumDef` or `SetDef<T>` cannot be written at all.

> [!IMPORTANT]
> A memento trusts its store. Each filter definition is persisted with its type name and restored by `Type.GetType`, guarded by
> an `AFilterDef` assignability check, so the deserializer builds whatever closed definition the store names, generic argument
> included: `SetDef<T>` takes any class, and its constructor and setters run. A cookie, `localStorage` or a query string can be
> written by the user: sign what goes there (`ProtectedLocalStorage`, `IDataProtector`). If users or other tenants can write the
> store, derive from `FilterDefFactory` and override `GetDefType` and `GetEnumType` with an allow-list of the closed types you
> persist, matched on the name as a string, as `FilterDefFactory.TypeName` writes it. Do not resolve the name and then compare
> the `Type`: `Type.GetType` loads the assemblies the name chooses before the comparison runs. An `EnumDef` persists its column's
> own type, so a nullable enum column is listed as `Status?` (an `EnumDef` resolves its enum through the factory reading it too).
> Set the factory as `FilterDefFactory.Default` at startup:
>
> ```csharp
> public sealed class AppFilterDefFactory : FilterDefFactory
> {
>     private static readonly Dictionary<string, Type> defs = new[]
>         { typeof(NumberDef<decimal>), typeof(DateDef<DateTime>), typeof(BoolDef), typeof(DefaultDef), typeof(EnumDef), typeof(SetDef<string>) }
>         .ToDictionary(TypeName);
>     private static readonly Dictionary<string, Type> enums = new[] { typeof(Status), typeof(Status?) }.ToDictionary(TypeName);
>
>     protected override Type GetDefType(string defType) =>
>         defs.TryGetValue(defType, out var type) ? type : throw new JsonException($"Filter type not allowed: {defType}");
>     protected override Type GetEnumType(string enumType) =>
>         enums.TryGetValue(enumType, out var type) ? type : throw new JsonException($"Enum type not allowed: {enumType}");
> }
>
> // Program.cs
> FilterDefFactory.Default = new AppFilterDefFactory();
> ```
>
> A persisted member an enum no longer has is dropped with a warning; the rest of the memento restores. A filter or enum type
> that no longer resolves, or a malformed store, fails the whole deserialization with a `JsonException` (the cause is its
> inner exception), so a host catches that one type and falls back to the default state. The `AFilterDef`
> JSON converter forwards to that default, so the restore, the filter dialogs and every grid without a `FilterDefFactory`
> parameter use the same factory. A factory given only as a grid parameter does not reach the deserializer.

```csharp
using Glazor.Grds;
using Glazor.Grds.Mdls;   // GridModelChangeType lives here
using Microsoft.AspNetCore.Components;

[CascadingTypeParameter(nameof(TGridItem))]
public partial class MyGrid<TGridItem> : Grigliaria<TGridItem>
{
    [Parameter] public string? StateKey { get; set; }
    [Inject] private ILayoutStore Cache { get; set; } = null!;   // yours: a circuit-scoped cache, session state, a table

    private bool _rendered;
    private Memento? _lastSaved;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            if (StateKey != null && Cache.Get<Memento>(StateKey) is { } saved)
            {
                _lastSaved = saved;
                RestoreMemento(saved);
            }
            _rendered = true;      // don't save what we haven't drawn yet
        }
    }

    protected override void ModelChanged(GridModelChangeType changeType)
    {
        base.ModelChanged(changeType);
        if (changeType == GridModelChangeType.AfterSync)
            SaveState();
    }

    protected override void LayoutChanged()
    {
        base.LayoutChanged();
        SaveState();
    }

    private void SaveState()
    {
        if (StateKey == null || !_rendered)
            return;
        var current = CreateMemento();
        if (!current.Equals(_lastSaved))       // Memento is a record: value equality
        {
            _lastSaved = current;
            Cache.Set(StateKey, current);
        }
    }
}

// Registered by the host; any store that keeps a value per key will do.
public interface ILayoutStore
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
}
```

Details that matter:

- **Guard on "has rendered".** Before the first render the grid hasn't discovered its columns, so a memento
  taken then is empty and would overwrite a good saved state.
- **Compare before saving.** `Memento` is a record with value equality, so `Equals` is enough to suppress the
  churn of identical saves — `AfterSync` fires often.
- **Over a tree, save on every change.** A `Treegliaria` subclass over an `ITreeStateGridModel` saves in `ModelChanged`
  whatever the change type: expanding, collapsing and zooming raise `GridModelChangeType.Other`, not `AfterSync`. `Other`
  also comes when the current row moves, and the compare before saving keeps the writes to the real changes.
- **A change to the columns keeps what the user arranged.** When the column set changes (a column added or removed in
  code, a markup column shown by an `@if`), the columns shown stay, in their order: a removed column goes, and an added
  one that the defaults would show is appended. After a restore, a column the restored layout names
  takes its place there instead; the filters and the sort stay as the user left them. A new `Model` brings its own
  filters, sort and current row, what a restore set aside for the old one goes, and it shows the default columns or,
  after a restore, the restored layout as the user has arranged it since; the column sizes stay, as they are the grid's. When a column change or a new model changes the columns
  shown, `OnLayoutChanged` is raised, so a host that saves on it saves what the grid now shows.
- **A restore can come before the columns.** A restore made before the markup columns register, from a subclass's
  `OnParametersSet` for instance, applies their filters and sort once they do, or at the first render after an edit that
  is pending then, where the user has set none since. A reset afterwards goes back to how the grid started, not to the
  restored state.
- **A memento holds what the grid has.** `CreateMemento` saves the registered columns only. A column that is not
  registered when the state is saved, a markup column whose `@if` is off, takes what the restore set aside for it if it
  comes back in the same session, and its defaults if it comes back in a later one.
- **What no longer applies is dropped.** A restored filter on a column that is no longer filterable, or whose type no
  longer matches the filter, a restored filter that takes injected parameters of a class its column's definition does not
  take, a filter the host set in code included, and a restored sort on a column whose values can no longer be compared are
  dropped with a warning. A filter the host sets in code on a column the user cannot filter is dropped too, by a reset as
  well, since the user could not clear it: the host applies it again. A sort set in code on a column the user cannot sort
  comes back when the column's declared type can be compared, `IComparable<T>`, `IComparable` or `object`; the restore does
  not see a comparer the model supplies.

`RestoreMemento(null)` goes back to how the grid started: the columns the defaults show, for the columns it has now, and the
sizes, filters and sort it started with. After a new `Model`, the filters and sort are those the new model came with. Sizes
set since, in code or by a restored memento, go. A starting filter or sort on a column that has left since, a markup column whose `@if` turned off for instance, is set aside as a restored one is,
and applies when the column comes back. That is how a "reset grid" button is implemented.

## Row dialog

A grid often has more columns than it shows: some are out of the layout, others are scrolled out of its window.
`RowDialogEnabled="true"` adds a button to the footer that opens the row dialog on the current row: a list with one line
per column, its label and its value.

```razor
<Grigliaria TGridItem="Product" Model="_model" Layoutable="true" RowDialogEnabled="true"
            HorzResizer="@(GridHorzResizers.DispUnitWidth(50, 150, 12))">
    <GridColumn Property="@(x => x.Code)"  Name="Code"  Label="Code" Fixed="true" ShownInRowDialog="true" />
    <GridColumn Property="@(x => x.Name)"  Name="Name"  Label="Name" />
    <GridColumn Property="@(x => x.Notes)" Name="Notes" Label="Notes" ShownInRowDialog="false" />
</Grigliaria>
```

**The lines.** The columns of the layout come first, in its order, then a "Hidden columns" line and the columns the
layout leaves out. A column is listed unless it is fixed or has no label, so an action column, usually both, stays out;
`ShownInRowDialog` on the column decides either way, on a `GridColumn` and on a `Column<>` declared in code alike. A
column of size 0 is never listed. The lines are those of the columns the grid has when the dialog opens: leave the
columns and the model alone until it closes. A line is as tall as a row of the grid, its `RowHeight`, or 40 px when the
grid has none.

**The values** are drawn as the grid draws them: by the column's `DisplayTemplate`, with the grid's `RowIdx` in its
context, and its `CellClassFn`, or else by type, a checkbox for a `bool`, the description of an enum, clamped text for the
rest. A grid-level `CellTemplate` is not used, since it draws a cell of the grid. The dialog is read-only.

**Moving.** The dialog's two buttons, and `Ctrl` + `←` / `→`, move the grid's current row to the previous and to the next
one, and the dialog follows; on a paged model the grid fetches the row as it does for `↓`, under its spinner. The list
keeps its place, so a column can be compared row after row. `Esc`, `Enter` or Close closes the dialog and leaves the current row
where the moves took it.

**The button** is disabled while every column the dialog lists is in view, when there is no current row, and while a row is being
edited. `Alt` + `Enter` on the grid, Windows' Properties key, does what the button does, when the button does; otherwise the
key is left to the browser. `ShowRowDialogAsync()` opens the dialog from code, also when every column is in view; it throws
`InvalidOperationException` without `RowDialogEnabled`, with no current row, or during an edit.

`MultiSelGrid` and `Treegliaria` take `RowDialogEnabled` and have `ShowRowDialogAsync()` too. Their own selection or
expansion column is fixed, so it stays out unless a column declared with its name sets `ShownInRowDialog="true"`. A tree's
`TreeColumn` is listed, with its value alone: the indent and the expander are the tree's cell, not the column's.

## Editing in place

Over an `IEditableGridModel`, `EditMode="EditMode.Manual"` lets the grid edit rows itself: F2 or a double-click turns the
current row into editors, Ctrl+Ins inserts a row made by `NewItem`, Enter or the green check on the row heading posts it,
and Esc or the cross cancels it. `EditMode.Disabled`, the default, leaves editing to the page, as the Tracks demo does with
a form beside the grid.

```razor
<Grigliaria TGridItem="Part" Model="_model" EditMode="EditMode.Manual" OnPost="SaveAsync"
            NewItem="@(current => new Part { Kind = current?.Kind ?? PartKind.Bolt })">
    <GridColumn Property="@(x => x.Id)" Name="Id" Label="Id" />
    <GridColumn EditProperty="@(x => x.Name)" Name="Name" Label="Name" />
    <GridColumn EditProperty="@(x => x.Qty)" Name="Quantity" Label="Quantity" />
</Grigliaria>
```

**Which columns edit.** A column whose `EFProperty` or `EditProperty` is a plain property with a public setter,
`x => x.Qty`, not an `init` one, gets an editor; any other column shows its value. `Editable="false"` keeps a column
read-only, a key say, and an `EditTemplate` draws the column's editor itself. The editor follows the property's type, as
the filter does, with Blazor's own inputs: `InputText` for `string`, `InputNumber` for the types it binds, `InputCheckbox` for
`bool`, `InputDate` for the date and time types, and `InputSelect` for an enum, with an empty entry when it is nullable.
A `DateTime` or a `DateTimeOffset` edits as date and time; a column that holds dates only in one takes a date input from its `EditTemplate`.
A property of another type is shown, not edited, unless an `EditTemplate` binds it. While a row is
edited, Tab and Shift+Tab walk all its cells, the read-only ones too. Under a `HorzResizer` the editors are those of the
columns in view, and Tab off the last column shown, or Shift+Tab off the first one that scrolls, moves the window by a
column and focuses the cell that came into view; the footer's column scrollbar still moves it, and `Ctrl` + `←` /
`→` stay with the editor. The cell cursor follows the focus. A `CellTemplate` of the page's own that does not draw
`DefaultCell` gives up the stops on the window's edges. The inputs send their value on change, as in a form; Enter, and
the row keys under `EditMode.OnMove`, fire it on the field first, as a browser does before a submit, so they never post a
value the server has not seen; the cell of the column the cell cursor is on takes the focus when the edit
starts, its editor or its value: F2 starts there, and a click or a double-click on the cell clicked. The editors take
the theme's colours through `--glazor-editor-bg`, `--glazor-editor-border` and `--glazor-editor-focus`, and are aligned
as their values are: numbers right, dates, enums and booleans centered.

**Validation.** The row is validated with its DataAnnotations under an `EditContext` the grid holds for the edit. A row
with errors is not posted: each message shows as a `PopupValidationMessage` in the upper left corner of its editor, by the
property's name, whatever the column's `Name`. A whole-row error, or one on a property no editor shows, shows on the row
heading beside Cancel and Post.

**Saving.** The page saves the row in `OnPost`, which gets a `GridPost`: the `Item`, whether it is `Inserting`, and
`AddError` to put a message on an editor. The handler stores the row and calls the model's `PostAsync`, which ends the edit
and reads the row back; if it calls nothing the row stays in edit. On an optimistic-lock conflict it calls `CancelAsync`
instead, which reads the row back as the database has it, and tells the user, as the Tracks demo does:

```csharp
private async Task SaveAsync(GridPost<Part> post)
{
    try
    {
        await using AppDbContext db = await DbFactory.CreateDbContextAsync();
        if (post.Inserting) db.Parts.Add(ToEntity(post.Item)); else db.Parts.Update(ToEntity(post.Item));
        await db.SaveChangesAsync();
        await _model.PostAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        await _model.CancelAsync();
        _message = "Another user changed the part: it is shown as the database has it now.";   // shown by the page
    }
    catch (DbUpdateException e) when (IsDuplicateName(e))
    {
        post.AddError(nameof(Part.Name), "That name is taken.");
    }
}
```

Without `OnPost` the grid calls `PostAsync` itself, which keeps the change only when the model's reload reads the rows the
editors changed.

**Fast edit.** `EditMode.OnClick` edits the row a click reaches, and `EditMode.OnMove` the row the keys reach as well, `↑`,
`↓`, `PageUp` and `PageDown`, as Java's editable grid did. A move away from a row being edited posts it first: a row with
errors stays where it is with its messages, and so does a row the post moved in the list, on its new place, no longer
edited; otherwise the row reached is edited when the model lets it. Under `OnMove` those keys post and move while a row is
edited, so a list or a date editor does not get them: `Alt` + `↓` still opens a list.

**Buttons.** With `ShowDefaultButtons`, the footer has Edit, then Insert when `NewItem` is set and Remove when `OnRemove`
is, each disabled while a row is edited or when it cannot act. A subclass's own buttons call the protected `StartEditAsync`,
`InsertAsync`, `PostAsync`, `CancelAsync` and `RemoveAsync`, which act as the keys do: `PostAsync` validates the row and goes
through `OnPost`, which the model's own `PostAsync` skips.

The demo's In-place edit grid page edits the Chinook tracks this way, with lists for the album, genre and media in
`EditTemplate`s.

**Removing.** Ctrl+Del hands the current row to `OnRemove`, never while a row is edited. Deleting is the page's, as saving
is: the handler asks for a confirmation if it likes, deletes the row, and drops it from the model, with
`GetData().RemoveAt(model.GetCurrentRow())` on a model in memory or `ReloadItemAsync`, which finds the row gone, on an EF
one. Without `OnRemove` the grid removes nothing.

`Treegliaria` and `MultiSelGrid` do not pass `EditMode` to the grid inside.

## Keyboard & mouse

Available when the grid has focus. Call `await grid.FocusAsync()` to give it focus programmatically.

| Input | Action |
| --- | --- |
| `↑` / `↓` | Previous / next row |
| `PageUp` / `PageDown` | Previous / next page |
| `Home` | First row |
| `End` | Last row (or last *fetched* row when `GotoLastKnown`) |
| `←` / `→` | Previous / next cell — requires `CellCursorEnabled`; without it the grid leaves the keys to the page |
| `Ctrl` + `←` / `→` | Move the horizontal window by one column, with a `HorzResizer`; without one the grid leaves the keys alone |
| `Alt` + `Enter` | Open the row dialog — with `RowDialogEnabled`, when its footer button is enabled |
| `Ctrl` + `←` / `→` in the row dialog | Previous / next row of the grid |
| `Enter` in the row dialog | Close it; on its Previous or Next button, move as the button does |
| any printable character | Start an incremental search on the sorted column |
| `Esc` in the search box, the column menu, a hover popup, or the filter, layout or row dialog | Close it, and go no further; the layout dialog puts back the layout it started from |
| Mouse wheel | Scroll rows |
| `Ctrl` + wheel | Previous / next cell — requires `CellCursorEnabled`, like `←` / `→`; without it the page zooms as usual |
| Two-finger drag | Scroll, when `TouchScrollerEnabled` |
| Right-click header | Column menu, when `ColumnsPopupEnabled` |
| `F2`, double-click | Edit the current row — with an `EditMode` |
| `Ctrl` + `Insert` | Insert a row made by `NewItem` — with an `EditMode` |
| `Ctrl` + `Delete` | Remove the current row through `OnRemove` — with an `EditMode` |
| `Enter`, `Esc` | Post or cancel the row edited in place |
| `↑` / `↓`, `PageUp` / `PageDown` with `EditMode.OnMove` | Post the row edited, move, and edit the row reached |

While a row is being edited, the arrows, `Ctrl` + `←` / `→`, `Home`, `End` and the page keys are left to the editor that has
focus, and the wheel to the page. A post or a cancel made from a component that
does not hold the grid does not re-render it: call the grid's `RepaintAsync`, or its keys stay off until it renders.

**Incremental search** only engages when the current sort column holds strings and the model reports
`CanSearchOnSortedColumn()`. Typing opens a small search popup and moves the current row as you type to the first row
that starts with the text, in either sort direction, or to the closest one when none does — on an EF-backed model it
binary-searches the rows already read, fetching further windows until the match is among them. The footer's search
button opens the popup on the current row's own value and leaves the current row where it is. Esc closes the popup and
goes no further, so a host's own Escape around the grid, a dialog's for instance, does not run; Enter is left to the
browser, so inside a host form it submits the form, as in any text field. A character typed with AltGr (`@`, `#`, `[` on
an Italian keyboard) does not open the search, since Chrome on Windows reports AltGr as `Ctrl` + `Alt`; once the popup is
open it types there as usual.

## Accessibility

The grid is a plain `<table>`, as QuickGrid is, with the attributes assistive technology reads:

- Every column header cell has `scope="col"` and a group header `scope="colgroup"`; a sortable one has `aria-sort`
  (`ascending`, `descending` or `none`), and the sort arrow is hidden from assistive technology since the attribute says
  the same.
- The table has `aria-rowcount` (the rows plus the header rows, or `-1` while a paged model has not reached the end) and
  every data row `aria-rowindex`, so a screen reader knows where the page sits in the whole table; the rows that only fill
  the page are hidden from it; the current row has `aria-current="true"`.
- The table takes its accessible name from `AriaLabel`; the footer and the layout dialog's own tables are presentation. Every
  icon-only control is named: the footer buttons, MultiSelGrid's move buttons and the layout and row dialogs' by a `title`
  and an `aria-label`, the filter, layout and column-scroll icons and a tree's expand control (with `aria-expanded`) by a
  `title`; a disabled one stays a link, `aria-disabled`. The icons are spans drawn by CSS masks, hidden from assistive
  technology under the control that carries the name, but for the editing mark, an image role with a label; the current
  row's marker is hidden too, the row carrying `aria-current`. The selection boxes, the search box
  and a bool cell have a label; the filter, layout and row dialogs are named by their title through `aria-labelledby`. The custom
  scrollbars are hidden, the keys scroll.
- The keys bind to a container with `tabindex="0"`: the grid is a Tab stop, so the keyboard reaches it even with no header
  link or footer button, and takes focus on a click or through `FocusAsync()` too; it shows an outline when the focus reached
  it from the keyboard. The footer buttons are in the tab order.

A host template that replaces a default header cell, row heading or cell supplies these attributes itself: the default
components are the reference.

## Templates and cell styling

### Per-cell markup

`DisplayTemplate`'s context is a `DecoratorContext<TGridItem, VT>` record — `(Item, Value, RowIdx, Column)`:

```razor
<GridColumn Property="@(x => x.Id)" Label="" Name="Edit"
            Fixed="true" Sortable="false" Filterable="false"
            PreferredSize="6" Alignment="Alignment.Center">
    <DisplayTemplate>
        <button type="button" @onclick="() => StartEditAsync(context.Item)">Edit</button>
    </DisplayTemplate>
</GridColumn>
```

Note the pattern: an action column still needs a value accessor (any stable field will do), and it opts out of
sorting and filtering.

A text field in a template takes what is typed into it, but the grid keeps its navigation keys: `↑`/`↓`, `PageUp`/`PageDown`,
`Home`/`End`, and `←`/`→` with a cell cursor. Those keys move the grid, not the caret. To edit a row in the grid, use
`EditMode`, whose editors keep the caret keys.

A template may hold any component but another grid: a grid inside a grid's cell is not supported. The keys and clicks
that reach the inner grid go on to the outer one, which can act on them: while the inner row is being edited, `↓` in its
editor moves the outer grid's current row.

### Styling cells from data

`CellClassFn` puts a class on the `<td>`, which pairs well with Blazor's scoped CSS. Wrap the grid in a class
of your own and reach through with `::deep`:

```razor
<div class="my-grid">
    <Grigliaria TGridItem="Request" Model="_model">
        <GridColumn EFProperty="@(x => x.Status)" Name="Status" Label="Status"
                    CellClassFn="@(ctx => ctx.Value == "ERROR" ? "err" : "ok")" />
    </Grigliaria>
</div>
```

```css
/* MyPage.razor.css */
.my-grid ::deep tbody td.err { background-color: lightpink; }
.my-grid ::deep tbody td.ok  { background-color: lightgreen; }
```

### Clamping long values

`Clamper` truncates to a line count and reveals the rest in a popup on hover. `GridColumn.LinesToClamp` does
this for plain text; use `Clamper` explicitly inside a `DisplayTemplate` when the cell contains markup:

```razor
<DisplayTemplate>
    <Clamper LinesToClamp="1">
        @foreach (var tag in context.Value ?? []) { <span>&nbsp;□@tag</span> }
    </Clamper>
</DisplayTemplate>
```

## Extending Grigliaria

Subclassing is the intended way to add a toolbar, persistence or export to every grid in an application.
`Grigliaria` is a Razor component, so a subclass that wants to add markup around the base output has to
re-emit it explicitly:

```razor
@* MyGrid.razor *@
@using Glazor.Grds
@typeparam TGridItem
@inherits Grigliaria<TGridItem>

@if (ShowTop)
{
    <div class="grid-toolbar">
        <h1>@Title</h1>
        @if (IsFiltered())
        {
            <span class="filter-active">FILTER ACTIVE</span>
        }
        <button @onclick="ResetState" disabled="@(Model.IsCurrentRowFrozen())">Reset</button>
        @TopTemplate
    </div>
}

@RenderBase()

@code {
    // https://github.com/dotnet/razor/issues/10499
    private RenderFragment RenderBase() => builder => base.BuildRenderTree(builder);
}
```

```csharp
// MyGrid.razor.cs
using Glazor.Grds;
using Microsoft.AspNetCore.Components;

namespace MyApp.Components;   // the namespace of MyGrid.razor: your project's, then its folders

[CascadingTypeParameter(nameof(TGridItem))]
public partial class MyGrid<TGridItem> : Grigliaria<TGridItem>
{
    [Parameter] public string? Title { get; set; }
    [Parameter] public bool ShowTop { get; set; } = true;
    [Parameter] public RenderFragment? TopTemplate { get; set; }

    private void ResetState() => RestoreMemento(null);   // back to the layout, filters and sort the grid started with
}
```

The `RenderBase()` indirection is a workaround for a Razor compiler limitation: an `@inherits` component's
generated `BuildRenderTree` replaces the base implementation rather than calling it, so the base markup has to
be invoked by hand.

`[CascadingTypeParameter]` lets consumers write `<GridColumn Property="...">` inside your grid without
restating `TGridItem` on every column.

Useful overrides:

| Member | When it runs |
| --- | --- |
| `ModelChanged(GridModelChangeType)` | The model changed. Call `base` first. |
| `LayoutChanged()` | Visible columns, order or widths changed. Call `base` first. |
| `IsCellCursorEnabled()` | Decide the cell cursor dynamically. |
| `CreateMemento()` / `RestoreMemento(Memento?)` | Extend the persisted snapshot with your own state. |
| `DisposeAsync()` | The grid is being disposed; release your own timers, modules or subscriptions here, then end with `await base.DisposeAsync()`. |
| `SetParametersAsync`, `OnParametersSet()`, `OnAfterRender(bool)`, `ShouldRender()` | The grid does its own work in them, subscribing to its model and measuring the page among it: an override calls `base`. |

## API reference

Selected public members, beyond the parameters above.

### Current position

```csharp
int  GetCurrentRow();
bool SetCurrentRow(int rowIdx);
bool SetCurrentCell(int rowIdx, int colIdx);
bool SetCurrentCol(string columnName);    // on the current row
string? GetCurrentCol();                  // null unless CellCursorEnabled
ValueTask FocusAsync();
Task StabilizeItemAsync();                // force OnStableItemChanged now
TGridItem? GetStableItem();               // the row OnStableItemChanged last reported, as the model holds it now
```

To jump to a row, set the current row: the grid scrolls to show it at its next render. `SetCurrentRow` on `Grigliaria` renders
at once; on `MultiSelGrid` and `Treegliaria`, set it on the model and call `RepaintAsync()`. No public method scrolls the
rows without moving the current row.

### Columns and layout

```csharp
IEnumerable<string> GetColumnsToShow();
IEnumerable<string> GetShownColumns();   // actually rendered, after horizontal paging
void SetColumnsToShow(params IEnumerable<string> columnNames);
void SetPreferredSize(string columnName, int? size);   // InvalidOperationException if not Layoutable; ArgumentException for an unknown column; a null size clears the one set
bool EnsureColumnIsShown(string columnName);   // also by model column index; scrolls only when the column is out of view
IColumn<TGridItem> GetColumn(int columnIndex);

GridLayout? GetLayout();                 // a copy, to save and give back to SetLayout later; null if not Layoutable
void        SetLayout(GridLayout? layout);   // null: the default layout, the columns ShownByDefault with a size gives, no sizes; a layout throws if not Layoutable
Task        ShowLayoutDialogAsync();         // the dialog the header's layout button opens, also with ShowHeader false; InvalidOperationException if not Layoutable or during an edit
Task        ShowRowDialogAsync();            // the row dialog the footer's button opens, also when every column is in view; InvalidOperationException without RowDialogEnabled, with no current row or during an edit

// GridLayout, read-only: what a saved or current layout holds
IReadOnlyList<string> ColumnsToShow { get; }                    // the columns shown, in order
IReadOnlyDictionary<string, LayoutAttr> Attributes { get; }     // the sizes the user set; a column without one takes its PreferredSize
```

### Filters

```csharp
bool IsFiltered();
bool HasFilter(string columnName);
AFilterDef? GetFilter(string columnName);
void SetFilter(string columnName, AFilterDef? filterDef);     // ArgumentException for an unknown or unfilterable column
IReadOnlyDictionary<string, AFilterDef> GetFilters();
void SetFilters(IReadOnlyDictionary<string, AFilterDef> filters);   // the same checks on every entry; a void definition is left out
FilterDefFactory GetFilterDefFactory();
Task ShowFilterDialogAsync(string columnName, Action<FilterDefDlg.SubmitEvent>? onFormSubmit);   // the checks of SetFilter, and InvalidOperationException during an edit
```

### State

```csharp
Memento CreateMemento();
void    RestoreMemento(Memento? memento);   // null resets to defaults
```

### Horizontal paging

```csharp
bool CanPrevCol();  void PrevCol();
bool CanNextCol();  void NextCol();
void ScrollToCol(int firstScrollingOffset);   // the offset among the columns to show that are not fixed, not a model index
int  GetHorzDisplayedUnits();
```

### Misc

```csharp
Task RepaintAsync();
bool IsIdle();                              // no fetch in flight
int  GetPageSize();                         // rows per page in effect: what the VertResizer computed, else the PageSize parameter; on IGrid, so on every grid
int  GetPageIdx();
```

## Complete example

An audit-log page: EF-backed, incrementally read, with column groups, a custom cell template and a layout the
user can arrange. This is the shape most real pages end up in; a toolbar and layout persistence come with a
subclass, see [Extending Grigliaria](#extending-grigliaria).

```razor
@* AccessLogs.razor *@
@page "/admin/access-logs/{UserId:int}"

<PageTitle>Access logs</PageTitle>

<div class="access-logs">
    <Grigliaria TGridItem="AccessDTO" @ref="_gridRef" Model="_model"
                HeaderHeight="70" RowHeight="40"
                ShowHeader="true" ShowColumnGroups="true" ShowNavigationButtons="false"
                Layoutable="true"
                HorzResizer="@(GridHorzResizers.DispUnitWidth(50, 150, 12))"
                VertResizer="@(GridVertResizers.RowHeight("height: 30vh;", 2, 20))"
                Spinner="@Gui.Spinner"
                OnDblClick="@ShowDetailAsync">

        <GridColumn EFProperty="@(x => x.NavId)"       Name="NavId"       Label="Navigation id" GroupId="Navigation" ShownByDefault="false" />
        <GridColumn EFProperty="@(x => x.NavDateTime)" Name="NavDateTime" Label="Timestamp"     GroupId="Navigation" PreferredSize="12" />
        <GridColumn EFProperty="@(x => x.Location)"    Name="Location"    Label="Location"      GroupId="Navigation" />

        <GridColumn EFProperty="@(x => x.LoginDateTime)"   Name="LoginDateTime"   Label="Logged at" GroupId="Login" PreferredSize="12" />
        <GridColumn EFProperty="@(x => x.RemoteIpAddress)" Name="RemoteIpAddress" Label="Remote IP" GroupId="Login" PreferredSize="12"
                    CellClassFn="@(_ => "ip")" />

        <GridColumn EFProperty="@(x => x.Roles)" Name="Roles" Label="Roles" GroupId="Login"
                    PreferredSize="40" Sortable="false" Filterable="false">
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
    </Grigliaria>
</div>
```

```csharp
// AccessLogs.razor.cs
using Glazor.Grds;
using Glazor.EntityFrameworkCore;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Components.Pages;   // the namespace of AccessLogs.razor: your project's, then its folders

public partial class AccessLogs : IDisposable
{
    [Inject] private IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = null!;

    [Parameter] public int? UserId { get; set; }   // from the route

    [CascadingParameter] public GuiHelper Gui { get; set; } = null!;   // a class of yours, cascaded by the main layout with its Spinner

    private Grigliaria<AccessDTO> _gridRef = null!;
    private EFDataGridModel<AppDbContext, AccessDTO> _model = null!;

    private int? _lastUserId;

    protected override void OnParametersSet()
    {
        if (_lastUserId == UserId && _model != null)
            return;
        _lastUserId = UserId;

        // Build the model once. On later parameter changes force a reload instead of
        // replacing it, so the user's filters, sorting and layout survive.
        _model ??= new()
        {
            DbContextFactory = DbContextFactory,
            ItemKeyExpr      = x => x.Id,
            ReadAll          = false,
            BaseQueryFn      = (db, _) => db.AccessLogins
                .AsNoTracking()
                .Where(x => x.UserId == _lastUserId)
                .Select(x => new AccessDTO
                {
                    Id              = x.Id,
                    LoginDateTime   = x.DateTime,
                    RemoteIpAddress = x.RemoteIpAddress,
                    Roles           = x.Roles,
                    NavId           = x.Location!.Id,
                    NavDateTime     = x.Location.DateTime,
                    Location        = x.Location.Location,
                })
                .OrderByDescending(x => x.LoginDateTime),
        };
        _model.ForceReload(false);   // the user changed: re-query; the old current row is meaningless, the filters and sort stay
    }

    private Task ShowDetailAsync(DblClickEvent e) =>
        DetailAsync(_model.GetItem(e.RowIdx));

    private Task DetailAsync(AccessDTO access) => Task.CompletedTask;   // yours: show the access in a panel or a dialog

    // The grid never disposes its model; the page that created it does.
    public void Dispose() => _model?.Dispose();

    public record AccessDTO
    {
        public long Id { get; init; }
        public DateTime LoginDateTime { get; init; }
        public string RemoteIpAddress { get; init; } = null!;
        public IReadOnlyList<string>? Roles { get; init; } = [];
        public long? NavId { get; init; }
        public DateTime? NavDateTime { get; init; }
        public string? Location { get; init; }
    }
}
```

```css
/* AccessLogs.razor.css */
.access-logs ::deep tbody td.ip { font-family: monospace; }
```

Points worth copying:

- **The DTO is a `record` with `init` setters**, projected in the query. Nothing is tracked, and the shape the
  grid sees is exactly the shape the database returns.
- **`_model ??=` plus `ForceReload`** — building the model once and reloading on parameter change is what
  preserves the user's filters and sort; pass `true` to look for the current row again, `false` when the changed parameter
  makes it meaningless, as a different user does. Assigning a fresh model would silently reset them, and show the default
  columns again unless a memento was restored.
- **The page disposes the model.** The grid never does. `EFDataGridModel` keeps its spilled row chunks on disk
  until `Dispose`, so the page implements `IDisposable` and forwards to it.
- **`OrderByDescending` in the base query** sets the order the rows first appear in; the model adds the key after
  it, which is what paging needs.
- **A wrapper `<div>` with its own class** gives scoped CSS something to hook `CellClassFn` output onto.
- **`ShownByDefault="false"`** on the noisy identity columns: available in the layout dialog, out of the way
  until asked for.
- **`Sortable="false" Filterable="false"`** on the collection-valued `Roles` column. A list cannot be compared,
  so a sortable one throws at the first render. And no filter has a meaning the factory can supply for an
  `IReadOnlyList<string>`: it would fall through to `DefaultDef` (string contains / starts-with) and, on an EF
  model, compose that into the query. A collection column that
  *should* be filterable wants `Property` plus an explicit `FilterDef` — see
  [Supplying your own filter](#supplying-your-own-filter).

### Factoring out the boilerplate

Once several pages look like the above, a toolbar and persistence are worth adding once, in a subclass —
see [Extending Grigliaria](#extending-grigliaria). A grid subclass that adds a title, a reset button, an
export button and memento persistence keyed by a `StateKey` parameter turns each page back into just markup
plus a model.

## Related components

- **[`Treegliaria<T>`](Technical_Documentation.md#treegliaria)** — hierarchical rows, same columns and filters.
- **[`MultiSelGrid<T>`](Technical_Documentation.md#multiselgrid)** — adds a selection column and select-all.

Both wrap a `Grigliaria` internally and forward most of the parameters documented here.
