# Architecture & components

Reference for the parts of Glazor outside `Grigliaria`. For the grid itself see
**[Grigliaria](grigliaria.md)**; for row sources see **[Data models](data-models.md)**.

## Contents

- [Namespaces](#namespaces)
- [Grid family](#grid-family)
  - [Treegliaria](#treegliaria)
  - [MultiSelGrid](#multiselgrid)
- [Dialogs and overlays](#dialogs-and-overlays)
  - [DialogBox](#dialogbox)
  - [Popup](#popup)
  - [Spinner](#spinner)
- [Input and layout](#input-and-layout)
  - [Tabs](#tabs)
  - [TriStateCheckbox](#tristatecheckbox)
  - [Clamper](#clamper)
  - [PopupValidationMessage](#popupvalidationmessage)
- [Behaviours](#behaviours)
  - [KeyAction](#keyaction)
  - [MouseAction](#mouseaction)
  - [ResizeObserver](#resizeobserver)
  - [Scroller and TouchScroller](#scroller-and-touchscroller)
- [Localization](#localization)
- [Theming](#theming)
- [Conventions](#conventions)

## Namespaces

| Namespace | Contents |
| --- | --- |
| `Glazor.Grds` | Grid components — `Grigliaria`, `Treegliaria`, `MultiSelGrid`, `GridColumn`, resizers, layout, `Scroller` and `TouchScroller` |
| `Glazor.Grds.Mdls` | Models, selection models, list types |
| `Glazor.EntityFrameworkCore` | `EFDataGridModel` and friends, in the package of the same name |
| `Glazor.Grds.Filters` | Filter definitions and the filter dialog |
| `Glazor.Grds.Decs` | Cell/header decorators and template contexts |
| `Glazor.Grds.Props` | Expression helpers used by the push-down machinery |
| `Glazor.Misc` | Dialogs, overlays, input helpers, behaviours, `LoggerSupport` |
| `Glazor.Tabs` | `Tabs` and `Tab` |

The grid components form a small hierarchy: `AGrid<T>` holds the rendering and navigation core,
`ADataGrid<T>` adds filtering, layout and mementos, and `Grigliaria<T>` is the concrete grid.
`Treegliaria` and `MultiSelGrid` are *not* subclasses — they wrap a `Grigliaria` and forward parameters to it. Of its methods
they forward those that take column names: `SetColumnsToShow`, `EnsureColumnIsShown`, `SetCurrentCol`, `GetFilter`, `HasFilter`,
`SetFilter`, `GetFilters`, `SetFilters` and `ShowFilterDialogAsync`, beside `GetColumnsToShow`, `GetCurrentCol`,
`ShowLayoutDialogAsync` and `ShowRowDialogAsync`.

## Grid family

### Treegliaria

`Treegliaria<TGridItem>` renders hierarchical rows using the same columns, filters and layout machinery as
`Grigliaria`. It takes an `ITreeGridModel<TGridItem>` instead of a flat model, normally a `TreeGridModel<TGridItem>`: see
[Data models › TreeGridModel](data-models.md#treegridmodelt) for building one. Over a database,
[`EFFolderModel`](data-models.md#effoldermodeltdbcontext-tgriditem) reads a folder's children as it opens.

```razor
<Treegliaria TGridItem="Node" Model="_treeModel" RowHeight="40">
    <GridColumn Property="@(x => x.Name)" Name="Name" Label="Name" />
</Treegliaria>
```

Every `Grigliaria` parameter but the editing ones (`EditMode`, `NewItem`, `OnPost`, `OnRemove`) is forwarded unchanged, the templates, `Layoutable`, `HorzResizer`, `AriaLabel`,
`FilterDefFactory`, `CellCursorEnabled` and `OnTimeout` included, and `OnDblClick` with `ColIdx` counted in your model: see the
[Grigliaria parameter reference](grigliaria.md#parameters). `CellTemplate` draws the data cells only, and the expansion column stays the tree's own. Its own parameter is `TreeCellTemplate`:
when set, it replaces the default cell, `DefaultTreeCell`, for every cell, the expansion column included;
`<DefaultTreeCell Param="context"/>` in it draws a cell as the tree would, through `CellTemplate` for the data cells.

The tree is usually drawn in the column of the node's label: `TreeColumn` names a column of your model, and the indent and
expander go in front of its value, with no expansion column of their own. The column is shown fixed, in front of the
columns that are not, and keeps its own sort, filter, size and label; a fixed column declared before it stays in front of it. To draw the label yourself, give `DefaultTreeCell` child content: in the tree column
it takes the place of the value. A label that opens or closes its node, as in a file manager, is a link that calls
`ToggleExpandAsync(rowIdx)`, disabled while `Model.IsCurrentRowFrozen()`; the demo's EF tree does it for its folders. A
click on it also makes the row current, and a double-click toggles twice.

```razor
<Treegliaria TGridItem="Node" Model="_treeModel" TreeColumn="Name">
    <TreeCellTemplate>
        <DefaultTreeCell Param="context"><b>@context.Model.GetItem(context.CellParam.RowIdx).Name</b></DefaultTreeCell>
    </TreeCellTemplate>
    <ChildContent>
        <GridColumn Property="@(x => x.Name)" Name="Name" Label="Name" PreferredSize="30" />
    </ChildContent>
</Treegliaria>
```

Without `TreeColumn` the tree has an expansion column of its own, `Treegliaria.ExpColName`, fixed and neither sortable
nor filterable. A column declared with that name gives it a value and a size; with `TreeColumn` set there is no such
column, and declaring one throws, as does a `TreeColumn` the model has no column for.

`Ctrl`+`Space` expands or collapses the current node, as a click on its expander does. Over an `IFolderGridModel` whose
`IsFolderModel()` is true, such as `EFFolderModel` or a `TreeGridModelWrapper` around it, `Ctrl`+`Shift`+`↓` and `Ctrl`+`Shift`+`↑` expand and collapse everything below the current row,
`Ctrl`+`Shift`+`→` zooms in on it, or on a leaf's folder, and `Ctrl`+`Shift`+`←` zooms out one level. A bar above
the header has buttons for the same, zoom in, zoom out one level and zoom out all the way, each disabled when it cannot
act, and while zoomed the path from the root down to the folder zoomed on, each folder but the last a link that zooms
back out to it. `ShowZoomBar` false hides the bar, for a page with its own toolbar; the keys still zoom. A folder's name
in the path is what the `ZoomLabel` parameter returns, or else the value of the `TreeColumn`, or of the expansion column
given one, or else that of the first column shown.

`CreateMemento` saves the layout, filters and sort as `Grigliaria` does. Over a model that implements `ITreeStateGridModel`
and whose `CanSaveTreeState()` is true it returns a `TreeMemento`, which adds its `Tree`, the `TreeState` the model's `GetTreeState` gives: `Expanded`, the nodes
expanded, and `Zoom`, the node zoomed on or null, each a `NodePath`, the keys of the nodes from a root down to it, written in
JSON as a bare array, `[1, 7]`. `RestoreMemento` gives it back through `RestoreTreeState`. A plain `Memento`, or null,
leaves them as they are. The interface is optional: over a model without it, `TreeGridModel` for one, whose host keeps the
expansion itself (`OnSetExpand`), the memento holds only the grid's part, and a host's own tree model implements it to have
its expansion saved too. A `TreeGridModelWrapper` implements it and `IFolderGridModel` too, forwarding to the model it
wraps, and its `CanSaveTreeState()` and `IsFolderModel()` say whether that model has them. `EFFolderModel` implements it: its
key must be one value, a number, a string, a `Guid` or a date, and over a composite `ItemKeyExpr` `CanSaveTreeState()` is false,
so `CreateMemento` returns the grid's plain `Memento`; the restore reads the folders at the next build, a query per
folder expanded, skips a folder gone since, and moves a zoom on one to its nearest ancestor still there.

### MultiSelGrid

`MultiSelGrid<TGridItem>` adds a checkbox column driven by an
[`ISelectionModel<T>`](data-models.md#selection-models), with a tri-state select-all in the header. Select-all works on every
candidate the selection model holds, so the header box is disabled while the grid is filtered. A row the selection model
locks shows its box disabled and keeps its state: select-all, `Ctrl`+`Space` and a double-click leave it as it is.
`CreateMemento` and `RestoreMemento` save and restore its layout, filters and sort, as on `Grigliaria`; the selection is the
selection model's, and is not part of the memento.

```razor
<MultiSelGrid @ref="_gridRef" Model="_model" SelectionModel="_selection"
              ReadOnly="@(!EditEnabled)"
              OnSelectionChanged="() => StateHasChanged()"
              VertResizer="@(GridVertResizers.RowHeight("height: 100%;", 2, 8))">
    <GridColumn Property="@(x => x.Name)" Name="Role" Label="Role" />
</MultiSelGrid>
```

| Parameter | Type | Description |
| --- | --- | --- |
| `Model` | `IGridModel<TGridItem>` | **Required.** Row source. |
| `SelectionModel` | `ISelectionModel<TGridItem>` | **Required.** What is selected. |
| `ReadOnly` | `bool` | Disable selection changes. |
| `OnSelectionChanged` | `Action?` | Selection changed. **Not** an `EventCallback` — call `StateHasChanged()` yourself. |
| `OnRowShift` | `EventCallback` | A row was moved, when the model supports reordering. |

Every `Grigliaria` parameter but the editing ones (`EditMode`, `NewItem`, `OnPost`, `OnRemove`) is forwarded as well, `Layoutable`, the templates and the callbacks included:
see the [Grigliaria parameter reference](grigliaria.md#parameters). `CellTemplate` draws the data cells only, and the checkbox
column stays the grid's own. Its own parameter is `MultiSelCellTemplate`:
when set, it replaces the default cell, `DefaultMultiSelCell`, for every cell, the checkbox column included;
`<DefaultMultiSelCell Param="context"/>` in it draws a cell as the grid would, through `CellTemplate` for the data cells.

`Ctrl`+`Space` toggles the current row's selection.

## Dialogs and overlays

### DialogBox

A thin wrapper over the native HTML `<dialog>` element.

```razor
<DialogBox @ref="_dlg" Class="my-dialog" ClosedBy="ClosedBy.CloseRequest">
    <h3>Confirm</h3>
    <button @onclick="() => _dlg.CloseAsync()">Close</button>
</DialogBox>

<button @onclick="() => _dlg.ShowModalAsync()">Open</button>

@code {
    private DialogBox _dlg = null!;
}
```

| Member | Description |
| --- | --- |
| `ShowAsync()` | Show non-modal. Returns `false` if it was already open. |
| `ShowModalAsync()` | Show modal, with the backdrop (`--glazor-backdrop`) and the browser's focus handling. Returns `false` if it was already open. |
| `CloseAsync()` | Close. Returns `false` if it wasn't open. |
| `IsVisible` | Current state. |
| `RepaintAsync()` | Force a re-render of the content. |
| `ClosedBy` | `ClosedBy.Any`, `CloseRequest`, `None` — maps to the `closedby` attribute. Default `CloseRequest`. |
| `OnClose` | `EventCallback`, raised however the dialog closed, including `Esc`. |
| any other attribute | Goes on the `<dialog>`: `aria-labelledby` pointing at the title, `aria-describedby`, `data-*`. A `class` or `style` attribute sets the `Class` or `Style` parameter. |

`OnClose` fires for every close path, so it's the right place to restore state — not just the code path
behind your own Cancel button.

### Popup

Like `DialogBox`, but intended for anchored, transient content — a column menu, a filter editor, a hover card.
It uses CSS anchor positioning, so the caller supplies the anchor through `Style`.

```razor
<span @ref="_anchorRef" style="@($"anchor-name: --{_anchorRef.Id}")" class="info">Info</span>

<Popup @ref="_popup" Class="info-popup"
       Style="@($"position: fixed; position-anchor: --{_anchorRef.Id}; position-area: bottom span-right;")">
    <p>Details…</p>
</Popup>
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `Open` | `bool` | `false` | Declarative visibility: `true` shows, `false` closes. `ShowAsync()`/`CloseAsync()` do the same imperatively. A close the popup makes itself, Esc or a click outside, leaves the parent's flag `true`, and setting `true` again is no change: reset the flag in `OnClose`, `<Popup Open="_open" OnClose="() => _open = false">`. |
| `Class`, `Style` | `string` | `""` | Applied to the `<dialog>`. |
| `ClosedBy` | `ClosedBy` | `Any` | `Any` (Esc, `close()`, or click outside), `CloseRequest` (Esc or `close()`), `None` (`close()` only). |
| `OnClose` | `EventCallback` | — | Raised on any close. |
| any other attribute | | | Goes on the `<dialog>`: `aria-label`, `role`, `data-*`. A `class` or `style` attribute sets the `Class` or `Style` parameter. |

Only one `Popup` is open at a time — showing one closes the previous.

`ShowAsync(x, y)` opens it at a point instead of an anchor, such as a mouse event's `ClientX` and `ClientY`, in CSS pixels
from the viewport's top left; where it would cross the right or bottom edge it opens to the left of, or above, the point. The
point is the viewport's only when the popup's `Style` has `position: fixed`, as the grids' column menu does: without it, on a
scrolled page or inside a positioned element, the popup opens offset by the scroll or that element's origin. One popup may
open both ways: at a point the `position-area` of its anchor is set aside, and the next `ShowAsync()` puts back the corner and
the area its `Style` gave it.

### Spinner

An `ISpinner` implementation over `DialogBox`, used by the grids to cover longer fetches. Besides `ISpinner`'s `Show()` and
`Hide()`, which the grids call, it has `ShowModalAsync()` and `CloseAsync()` for a host that wants to cover a long operation of
its own; both return `false` when there is nothing to do, before the first render included.

The spinner is provided by the application, not by Glazor: allocate one, once, in the main layout, and hand it to
the pages. The app must be interactive from the root, with the render mode on `HeadOutlet` and `Routes` in
`App.razor`. With a `@rendermode` on each page instead, the layout renders statically: its spinner is never
interactive, and what it cascades does not reach the page, whose `Gui` arrives null.

Cascade an object that holds it rather than the `@ref` field itself. A component reference is set only
after the component that declares it has rendered, so anything read during that render is still null: a cascaded
`@ref` field, or a `<Spinner @ref>` and a `Spinner="@..."` in the same page. A paged grid then throws on its first
render.

```razor
@* App.razor *@
<HeadOutlet @rendermode="InteractiveServer" />
...
<Routes @rendermode="InteractiveServer" />
```

With a paged grid, turn prerendering off on both: `@rendermode="new InteractiveServerRenderMode(prerender: false)"`.
The grid fetches its first rows during its first render and shows the spinner while it does, which needs the circuit;
under prerendering that render runs once more on the static pass, where the spinner's interop fails and is logged, and
the query runs twice. The in-memory models do not fetch, so a page with only those may keep prerendering.

```razor
@* MainLayout.razor *@
<Spinner @ref="_gui.Spinner" />
<CascadingValue Value="_gui" IsFixed="true">
    @Body
</CascadingValue>

@code {
    private readonly GuiHelper _gui = new();
}
```

```csharp
public sealed class GuiHelper
{
    public Spinner? Spinner { get; set; }
}
```

```razor
@* any page *@
<Grigliaria TGridItem="Row" Model="_model" Spinner="@Gui.Spinner" ... />

@code {
    [CascadingParameter] public GuiHelper Gui { get; set; } = null!;
}
```

The layout can be the holder itself: have it implement an interface exposing `ISpinner Spinner => _spinner`, cascade
`this`, and take that interface as the pages' cascading parameter.

`ISpinner` is deliberately small so you can substitute your own overlay:

```csharp
public interface ISpinner
{
    bool Show();   // true if it became visible now
    void Hide();
}
```

Both may be called from a non-Blazor thread, so an implementation should marshal through `InvokeAsync`.

## Input and layout

### Tabs

```razor
<Tabs LabelsPosition="LabelsPosition.Top" OnTabChanged="OnTabChanged">
    <Tab Name="general" Label="General">
        <p>…</p>
    </Tab>
    <Tab Name="advanced" Label="Advanced" Tooltip="Rarely needed">
        <p>…</p>
    </Tab>
    <Tab Name="debug" Label="Debug" Hidden="@(!_isAdmin)">
        <p>…</p>
    </Tab>
</Tabs>
```

**`Tabs`**

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `LabelsPosition` | `LabelsPosition` | `Top` | `Top`, `Bottom`, `Left`, `LeftVert`, `Right`, `RightVert`. |
| `CurrentTabFrozen` | `bool` | `false` | Block tab changes — useful while a form is dirty. Non-current tabs render as `aria-disabled` while set. |
| `IdPrefix` | `string` | a new `Guid` | Prefix for the generated `id` attributes that tie each tab to its panel. Set it when you need stable, predictable ids — for tests or external labelling. |
| `OnTabChanged` | `EventCallback<TabChangedEvent>` | — | Carries `(TabName, PreviousTabName)`. |

**`Tab`**

| Parameter | Type | Description |
| --- | --- | --- |
| `Name` | `string` | **Required.** Stable identity, used by `SetCurrentTabAsync` and to build the tab/panel ids. Unique within its `Tabs`: a second tab with the same name throws `InvalidOperationException` when it renders. |
| `Label` | `string` | **Required.** Display text. |
| `Hidden` | `bool` | Omit the tab entirely — it is also skipped by keyboard navigation. A current tab that turns hidden gives way to the first tab neither hidden nor disabled, without raising `OnTabChanged`. Show or hide a tab with `Hidden` rather than an `@if`: a `Tab` created after the first render is placed last, whatever its place in the markup. |
| `Disabled` | `bool` | Render the tab but refuse selection. It stays focusable and is marked `aria-disabled`, so it can still be discovered by keyboard. A current tab that turns disabled stays current. |
| `Tooltip` | `string?` | Label `title`. |

Select programmatically with `await tabs.SetCurrentTabAsync("advanced")`, which returns `false` if the tab is
hidden, disabled, already current, or the tab set is frozen. `tabs.CurrentTabName` is the name of the tab shown, or `null`
when there is none; read it to follow a hidden tab giving way, which `OnTabChanged` does not report.

#### Keyboard and accessibility

Tab labels are `<button>` elements, so they are in the tab order and activate on `Enter` or `Space`.
Navigation follows the ARIA tabs pattern with a roving `tabindex` — the current tab is the only one reachable
by `Tab`, and the arrow keys move focus within the tablist:

| Key | Action |
| --- | --- |
| `→` / `↓` | Focus the next tab, wrapping at the end |
| `←` / `↑` | Focus the previous tab, wrapping at the start |
| `Home` / `End` | Focus the first / last tab |
| `Enter` / `Space` | Activate the focused tab |

Focus movement skips `Hidden` tabs and wraps around; it deliberately does *not* skip `Disabled` ones, since
they are marked with `aria-disabled` rather than the `disabled` attribute and so remain discoverable.

Arrow keys move focus without activating — the manual-activation variant of the pattern — so a keyboard user
can look along the tabs before committing.

The emitted markup carries the full relationship: each tab has `role="tab"`, `aria-selected` and `aria-disabled`, the
current one `aria-controls` pointing at its panel, the only panel in the DOM, and the panel has `role="tabpanel"` with
`aria-labelledby` pointing back at its tab. Ids are derived from `IdPrefix` and the tab `Name`, both with every character
outside ASCII letters, digits and `-` written as its hexadecimal character code between underscores (a space is `_20_`), so a name with spaces or punctuation still gives an id the IDREF
attributes can point at, and two names that differ only by such a character keep distinct ids.

### TriStateCheckbox

A checkbox with a real indeterminate state, since `null` is a meaningful value for a filter or a select-all.

```razor
<TriStateCheckbox @bind-Value="_state" Title="Include archived" Disabled="@_locked" />
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `Value` | `bool?` | `null` | `true` / `false` / `null` (indeterminate). Supports `@bind-Value`. `ValueChanged` is raised only when the user toggles the box, not when the parent writes `Value`, like Blazor's own inputs. |
| `ToggleFn` | `Func<bool?, bool?>` | `false → true → null → false` | Override the click cycle. |
| `Disabled` | `bool` | `false` | Also suppresses `ValueChanged`, so a two-way binding won't write back while disabled. |
| `Class`, `Title` | `string?` | `null` | Passed to the `<input>`. |
| any other attribute | | | Goes on the `<input>`: `id`, `name`, `aria-label`, `data-*`; the attributes the checkbox sets itself win. |

### Clamper

Truncates content to a line count and reveals the full value in a popup on hover. Esc closes the popup and goes no
further, so a dialog's or a host's Escape action around it does not run.

```razor
<Clamper LinesToClamp="1">
    @foreach (var tag in tags) { <span>&nbsp;□@tag</span> }
</Clamper>
```

| Parameter | Type | Description |
| --- | --- | --- |
| `LinesToClamp` | `int` | **Required.** Lines to show before clamping. |
| `Enabled` | `bool` | Set `false` to keep the content clamped but turn off the popup on hover and click. To show it unclamped, set `LinesToClamp` to 0. |

`GridColumn.LinesToClamp` applies this to plain-text cells automatically; use `Clamper` directly when a cell
renders markup.

### PopupValidationMessage

A drop-in for `ValidationMessage<T>` that shows the error as an icon plus popup instead of inline text — handy
in dense forms and grid cells.

```razor
<PopupValidationMessage For="@(() => model.Email)" ShowWhenMouseOver="true" />
```

Requires an `EditContext` from an enclosing `EditForm`, and shows its message behind an error icon. The icon is a
focusable button: Tab reaches it and Space or Enter opens the message, Esc closes it, so the text is available without a
mouse; the popup itself is a `<dialog>`, read by assistive technology while it is open. `Class` and `Style` are added to
the icon's own; any other attribute goes on the message inside the popup, as on a `ValidationMessage`.

## Behaviours

These render nothing. They attach a browser behaviour to an ancestor element and raise a callback — which is
how `Grigliaria` implements its own keyboard and wheel handling.

### KeyAction

```razor
<KeyAction Key="Escape" OnKey="@(async e => await CancelAsync())"
           BindAncestorWithTag="div" Enabled="@_editing" />

<KeyAction CtrlKey="true" Key=" " OnKey="@(e => ToggleSelection())"
           BindAncestorWithTag="div" />
```

| Parameter | Type | Description |
| --- | --- | --- |
| `Key` | `string` | A DOM key name (`"Escape"`, `"ArrowUp"`, `" "`), or `KeyAction.AnyChars` for any printable character. |
| `ShiftKey`, `CtrlKey`, `AltKey` | `bool` | Required modifiers; the key matches only with exactly these held. Alt combinations no `KeyAction` asks for are left to the browser (Alt+← is Back), and Meta ones always are. |
| `BindAncestorWithTag` | `string?` | Attach to the nearest ancestor with this tag name; without it the handler goes on the placeholder's parent element. |
| `Enabled` | `bool` | Gate the handler without unmounting it. |
| `CommitFocused` | `bool` | Before the action, fire `change` on the field the key was pressed in, as the browser does before a form's submit, so an input bound on change (`InputText`, `InputNumber`, …) has its value on the server first. The grid's Enter and row keys set it while a row is edited in place. |
| `OnKey` | `EventCallback<KeyEvent>` | Carries `(Chars, TargetName, TargetId)` — the typed characters plus the `name` and `id` of the element the key landed on. |

`AnyChars` is what drives the grid's incremental search. `TargetId` is how one handler bound to a container
can tell *which* child was focused — `Tabs` uses it to resolve the focused tab before moving focus on.

An `Enter` action bound to a container takes Enter on its buttons and links too, since a click leaves the focus on them. A
button that keeps its own Enter carries a `KeyAction` of its own, with no `BindAncestorWithTag`, as the dialogs' Cancel
does: it runs first and the key goes no further.

One `KeyAction` per key and modifiers on an element: a second one replaces the first, with a warning in the browser
console, and removing either removes the key. The keys a grid binds on its container are the grid's: a host's `KeyAction`
for one of them there takes it from the grid, and one on an element around the grid does not see it while the grid's
is enabled.

A key pressed in an open modal dialog, a `DialogBox` shown with `ShowModalAsync` or the grid's filter, layout and row
dialogs, is the dialog's, as in a desktop application: a `KeyAction` on an element around the dialog does not see it, one
inside the dialog or bound on it with `BindAncestorWithTag="dialog"` does. An `@onkeydown`, or a listener of your own,
around the dialog still sees it.

### MouseAction

```razor
<MouseAction Type="MouseAction.MouseType.Wheel" BindAncestorWithTag="div"
             OnMouse="@(e => MoveRow(e.WheelDelta > 0 ? 1 : -1))" />
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `Type` | `MouseType` | required | `Click`, `DblClick`, `Wheel`, `ContextMenu` (right-click on most browsers). |
| `ShiftKey`, `CtrlKey` | `bool` | `false` | Required modifiers. |
| `CancelBubble` | `bool` | `true` | Stop propagation and cancel the default action: a checkbox or a link inside the element does not act on the click, the wheel does not scroll the page. |
| `ExclusiveClick` | `bool` | `false` | Make `Click` and `DblClick` mutually exclusive: a click is held for 500 ms, the usual double-click interval, and dropped if a double-click follows, so a double-click no longer also fires the click handler. Adds that lag to every click. |
| `BindAncestorWithTag` | `string?` | `null` | As `KeyAction`. |
| `Enabled` | `bool` | `true` | Gate the handler without unmounting it, as `KeyAction`. |
| `OnMouse` | `EventCallback<MouseEvent>` | — | Carries `(TargetName, TargetId, ClientX, ClientY, WheelDelta)`. It does **not** report modifier state — `ShiftKey`/`CtrlKey` are a filter, so the handler only fires for the combination it declared and already knows which one it is. |

### ResizeObserver

Wraps the browser's `ResizeObserver`.

```razor
<div @ref="_boxRef">…</div>
<ResizeObserver ObservedRef="@_boxRef" OnResize="e => OnResized(e.Width, e.Height)" />
```

`Enabled` (default `true`) pauses the observation without unmounting the component; `OnResize` carries a `ResizeObserver.ResizeEvent` with `Width` and `Height`.

`@ref` is set after the render, so the component gets `_boxRef` one render late: it starts observing on the render after
the element appears, and an element re-created, by a `@key` change for instance, from the render after that. A host
that renders only once never passes it the element: render once more, with a `StateHasChanged()` in `OnAfterRender`
when `firstRender`, for instance.

Use one `ResizeObserver` per element. A second one on the same element takes the first one's place, so the first stops
receiving `OnResize`, and disposing either stops both. Two components that need the size of the same element share one
observer, or observe two different elements.

This is how the [resizers](grigliaria.md#resizers) learn the grid's available space.

### Scroller and TouchScroller

`Scroller` renders a custom scrollbar; `TouchScroller` adds multi-finger touch scrolling over an existing
element. Both are wired internally by `Grigliaria`; `TouchScrollerEnabled="true"` on the grid is the normal way
to reach the latter. Only the moves of a gesture are cancelled, never its start, so a tap still clicks: with
`Fingers="One"` the rows keep their touch selection. `Enabled`, default `true`, turns the scrolling off without unmounting
it.

```razor
<TouchScroller ScrollableRef="@mainDivRef" PageSize="@GetPageSize()"
               Fingers="TouchScroller.FingerCount.Two"
               OnScrollBy="@(t => ScrollBy(t.Dx, t.Dy))" />
```

## Localization

The texts the components show — button titles, dialog labels, the column menu entries — come from
`Resources/Texts.resx`, in English, and `Texts.it.resx`, in Italian, read for `CultureInfo.CurrentUICulture` by
`Glazor.Misc.GlazorTexts`. A culture with no resources gets English. Nothing has to be registered: a component takes
the host's `IGlazorTexts` service when one is registered, and the built-in one otherwise. A filter renderer derived from
`AFilterDefRenderer`, or a grid derived from `AGrid`, reads the same texts through its protected `Texts`.

The culture is the host's to set. Blazor Server gives each circuit the culture of the request that opened it when
the app uses request localization:

```csharp
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "it")
    .AddSupportedUICultures("en", "it"));
```

For other wording, or another language, register an `IGlazorTexts`. The keys are the `TextKeys` constants, one per
string. The components ask the registered service for every key, so its last arm hands the keys it does not cover to
`GlazorTexts.Default`:

```csharp
builder.Services.AddSingleton<IGlazorTexts, MyTexts>();

sealed class MyTexts : IGlazorTexts
{
    public string Get(string key) => key switch
    {
        TextKeys.Filter => "Filtre",
        TextKeys.FilterActive => "Filtre actif",
        _ => GlazorTexts.Default.Get(key)
    };
}
```

What is not localized: exception messages and log lines, which are for developers; column headers and enum values,
which are the host's own text. Dates and numbers follow the culture through `InputDate`, `InputNumber` and .NET
formatting, as before.

## Theming

Every colour the components use, and every icon they draw, is a `--glazor-*` custom property declared on `:root` in
`glazor.css` with its light default. The stylesheets use nothing else: the grid, the dialogs, the popups, the clamper, the
validation message, the spinner and the tabs all read the properties, and the icons are SVG masks filled with the
element's colour, so a theme reaches them too. There are no images.

A dark theme ships in the package. Link it after `glazor.css` to turn every Glazor component on the page dark:

```html
<link href="_content/Glazor/glazor.css" rel="stylesheet" />
<link href="_content/Glazor/glazor-dark.css" rel="stylesheet" />
```

Two more light ones ship beside it, linked the same way in its place. `glazor-aurora.css` has a deep indigo header lit by
a teal glow, soft violet for the current row, teal for the links and the main buttons and amber for the accents;
`glazor-paper.css` has warm neutrals, ink blue for the current row and the main buttons and small capitals in the header.
Each sets the properties below and adds a rule or two on the components' own `glazor` classes; the page's background
stays yours (`#f6f7fb` suits Aurora, `#f7f3ea` Paper).

To follow the user's preference, or to theme a part of the page, copy its block under a media query or an ancestor's
selector in your own stylesheet; a property set on an ancestor wins over the `:root` default for everything below it:

```css
@media (prefers-color-scheme: dark) {
    :root { --glazor-header-bg: #2b2f36; /* ... */ }
}
.dark-panel { --glazor-row-odd-bg: #1e2126; --glazor-row-even-bg: #23272d; /* ... */ }
```

The properties, with their light defaults in `glazor.css` and their dark values in `glazor-dark.css`:

| Group | Properties |
| --- | --- |
| Text | `--glazor-color-scheme` (`light` or `dark`: what the native checkboxes, radios, date pickers and the popups' scrollbars render as), `--glazor-fg` (the cells', popups' and dialogs' text; `currentColor`, so it inherits by default), `--glazor-link` (the sort headers, the expanders and the menu entries; `LinkText`, the browser's link colour, by default) |
| Header | `--glazor-header-bg`, `--glazor-header-fixed-bg`, `--glazor-header-fg`, `--glazor-header-border`, `--glazor-rule` |
| Rows and cells | `--glazor-row-odd-bg`, `--glazor-row-even-bg`, `--glazor-row-current-bg`, `--glazor-row-edited-bg`, `--glazor-cell-current-bg`, `--glazor-no-data-fg`, `--glazor-focus-outline`, `--glazor-tree-indent` |
| Scrollbars and buttons | `--glazor-scroll-track-bg`, `--glazor-scroll-handle-bg`, `--glazor-scroll-button-bg`, `--glazor-button-bg`, `--glazor-button-fg`, `--glazor-primary-bg`, `--glazor-primary-fg` (the dialogs' OK button) |
| Popups and dialogs | `--glazor-popup-bg`, `--glazor-popup-border`, `--glazor-popup-shadow`, `--glazor-popup-hover-bg`, `--glazor-overlay-shadow`, `--glazor-backdrop` (the modal dialogs' backdrop) |
| Spinner, clamper, validation | `--glazor-spinner`, `--glazor-spinner-backdrop`, `--glazor-clamper-mark`, `--glazor-clamper-popup-bg`, `--glazor-clamper-popup-border`, `--glazor-message-bg`, `--glazor-message-border`, `--glazor-message-icon`, `--glazor-message-icon-bg` |
| Editing in place | `--glazor-editor-bg`, `--glazor-editor-border`, `--glazor-editor-focus`: by default the popup background, the header border and the primary colour, so a theme that sets those dresses the editors too, on `:root` or on an ancestor |
| Icons | `--glazor-icon-accent` (the active filter, the filtered layout, the sort arrow), and one `--glazor-icon-*` per shape, an SVG `url()` a theme may replace |
| Tabs | `--glazor-tab-border`, `--glazor-tab-secondary-border`, `--glazor-tab-bg`, `--glazor-tab-label-inactive-bg`, `--glazor-tab-label-disabled-bg`, `--glazor-tab-label-fg`, `--glazor-tab-label-inactive-fg`, `--glazor-tab-label-disabled-fg`; `Tabs` keeps its own `--tab-*` properties, which read these, for a host that set them |

## Conventions

A few patterns recur across the library and are worth knowing before reading the source.

**`Action` for synchronous notifications, `EventCallback` for asynchronous ones.** Callbacks that fire from
a render, a property setter or the model's own event — `OnModelChanged`, `OnLayoutChanged`, `OnSelectionChanged` — are `Action`,
because there is nowhere to await them. Ones raised from real user events — `OnDblClick`, `OnClose`,
`OnKey`, `OnStableItemChanged` — are `EventCallback`.

The practical consequence: an `EventCallback` re-renders your component automatically when the handler
completes; an `Action` does not. Call `StateHasChanged()` yourself in `Action` handlers.

**`RequireNotNull()` where a null is a programming error.** `Glazor.Misc.Extensions` adds
`obj.RequireNotNull()` to every reference and nullable value: it returns the value, or throws
`ArgumentNullException` naming the expression that produced it (via `CallerArgumentExpression`). Unlike the
`!` operator it enforces the condition at runtime. The library uses it internally and it is meant for host code
too; that is why it is public. `Enum.GetDescription()`, which reads a `[Description]` attribute, is offered on the
same terms.

**`Comparers` and `EqComparers` for composite comparisons.** `Glazor.Misc.Comparers.Combo` chains two or more
`IComparer<T>`s into one, the first non-zero result winning. `EqComparers` builds `IEqualityComparer<T>`s over
members (`Property`, `Combo`) and over collections whose element order must not matter (`ForSet`,
`ForDictionary`) or must (`ForEnumerable`, `ForList`). The library uses them for `GridLayout`, `Memento` and the
filter definitions' `Serialized` records; a custom `AFilterDef` that adds a set-valued member combines
`EqComparers.ForSet` into its record's equality the same way. Like `RequireNotNull`, they are public for host
code too.

**`LoggerSupport` for non-component types.** Models and helpers can't inject `ILogger`, so they resolve one
through `LoggerSupport.Logger<T>()`. Call `LoggerSupport.SetLoggerFactory(...)` once at startup —
see [Getting started](getting-started.md#3-enable-logging-inside-the-library).

**`IAsyncDisposable` with JS modules.** Components owning a JS module dispose it in `DisposeAsync`, tolerating
`JSDisconnectedException` and `TaskCanceledException` — both are normal when a circuit goes away. Behaviours
that attach handlers to an element they don't own also call a JS `cleanup` first, to detach them before the
interop reference dies.


## Other public types

Smaller pieces a consuming project may reach for directly.

| Type | Namespace | What it is |
| --- | --- | --- |
| `IdleTracker` | `Glazor.Misc` | Renders its `ChildContent` and raises `OnUserIdle` after `TimeoutMilliseconds` (default 60,000) without mouse, keyboard, scroll or touch activity on the window, then `OnUserActive` on the next one. |
| `Anchor` | `Glazor.Misc` | An `<a href="#">` that raises `OnClick` without navigating; `Disabled` and `Title` parameters, and any other attribute (`aria-label`, `aria-expanded`, `data-*`) goes on the `<a>`. Disabled it keeps its link role and name, `aria-disabled`. |
| `InputTextOnInput` | `Glazor.Misc` | An `InputText` that updates its bound value on every keystroke (`oninput`) instead of on blur; `FocusOnFirstRender` focuses it once. |
| `EnumModel` | `Glazor.Grds.Mdls` | A `DataGridModel<Enum>` over the members of an enum, for pick lists; its constructor takes an optional `IGlazorTexts` for the column label, the built-in texts without. |
| `OnDiskChunkedList<T>` | `Glazor.Misc` | The list behind a paged `EFDataGridModel`, in the `Glazor.EntityFrameworkCore` package: rows in chunks, the least recently used chunks spilled to disk as MessagePack files. See [Data models](data-models.md#paging-stores-rows-on-disk). |
| `ISortableList<T>`, `SortableList<T>` | `Glazor.Grds.Mdls` | The list an EF model holds its rows in, which it sorts in place: `OnDiskChunkedList<T>` when paged, `SortableList<T>`, a `List<T>`, under `ReadAll`. A paged model's `CreateItemsListFn` returns a `SortableList<T>` to keep its rows in memory. |
| `VersionedList<T>` | `Glazor.Misc` | An `IList<T>` that increments a `Version` on every insert and remove, and on indexer writes too when built with `trackUpdates: true` (or with a comparer), so a consumer can detect modification cheaply. |
| `Defer` | `Glazor.Misc` | Renders its `ChildContent` in a later pass than its siblings, so a parent sees the columns the child components registered before it builds; the grids use it around their table. |
| `SpinnerWheel` | `Glazor.Misc` | The animated wheel the `Spinner` shows, a `role="status"` element a host can place on its own. |
| `AppLoading` | `Glazor.Misc` | The wheel a page shows, placed in `App.razor` outside `Routes`, until the first render brings the layout: `glazor.css` hides it once the layout's `Spinner` is in the page, so it needs one. See [Getting started](getting-started.md). |
| `Scroller` | `Glazor.Grds` | The custom scrollbar of the grids: `Orientation` (`OrientationType.Vertical` or `Horizontal`), `RowCount`, `PageSize`, `PageIdx`, `OnScrollTo`; `SizeTrick` takes the measured size so the bar re-renders when it changes; `Enabled`, default `true`, turns the bar off without unmounting it. |
| `AsyncHelper` | `Glazor.Misc` | `RunSync` runs a `Task` to completion from synchronous code, as the paged models do during a render. It blocks the caller: called on the Blazor dispatcher, the task must never wait on that dispatcher (`InvokeAsync`, a component's methods or callbacks), or both wait for ever. |
| `Utils` | `Glazor.Misc` | `IsNumber` for a value or a type, `IdentityIndexOf`, `ByteArraysEqual`. |
| `NoValue` | `Glazor.Misc` | An empty struct for a dictionary used as an ordered set. |
| `MessagePackConf` | `Glazor.Misc` | In the `Glazor.EntityFrameworkCore` package, the serializer options the spilled chunks are written with (`Options`: contractless, a graph-depth guard of `MaxGraphDepth`, and the `XDateTimeFormatter`, `XDateTimeArrayFormatter` and `XDateTimeResolver` that keep a `DateTime`'s `Kind`). |
| `DefaultFilterDefConverter` | `Glazor.Grds.Filters` | The JSON converter on `AFilterDef`, forwarding to `FilterDefFactory.Default`. |
| `PocoColumnComparer`, `ColumnComparer<T>` | `Glazor.Grds.Mdls` | The in-memory comparison of rows by a column: `GetComparerByType` and `GetComparerForColumn` give the comparer the sortable-column rule describes, `CheckCanSort` applies the rule, `BinarySearch` and `ContainsSearch` run the sorted-column searches over a list. |
| `ColumnEqualityComparer` | `Glazor.Grds` | Equality of two `IColumn`s by what they declare (label, size, clamping, flags), used by the models to tell a re-registered column that changed from one that did not. |
| `ColumnWrapper<TGridItem>` | `Glazor.Grds` | A column that forwards every member to the one it wraps: derive from it and override what changes, a size or the fixed flag, and implement `NewWrapper` so an enrichment keeps the wrapper. |
| `ColumnPlace` | `Glazor.Grds` | The `ToShow` of `HCellTemplateParam`, for a column that is not fixed: `Idx`, its place in `GetColumnsToShow()`, and `FirstNotFixedIdx`, so a header can tell which way it can move. |
| `IColumnInfo<TGridItem>` | `Glazor.Grds.Mdls` | A column as the model registered it, from `GetColumnInfo`: its `Name`, the `Column`, the `Property` getter, the `PropertyExpression` of an `EFProperty` or `AddEFColumn` column, and `QueryPushDownEnabled`, whether an EF model can filter and sort it in the database. |
| `AMemGridModel<TGridItem>` | `Glazor.Grds.Mdls` | The base of `GridModel` and `DataGridModel`, the models over a list in memory that `GetData()` returns. |
| `IEditableDataGridModel<TGridItem>` | `Glazor.Grds.Mdls` | `IEditableGridModel` and `IDataGridModel` together, what `EditableDataGridModel` and `EditableEFDataGridModel` implement: a `Grigliaria` with an `EditMode` edits rows in place on a model that implements `IEditableGridModel`. |
| `ISelectionModelWithSelectAll<TGridItem>` | `Glazor.Grds.Mdls` | A selection model with `AllSelected`, `SelectAll` and `DeselectAll`: `MultiSelGrid` enables its select-all box only over one, as the `SetSelectionModelWithSelectAll` models are. |
| `ADataGrid<TGridItem>.PredefinedFilter` | `Glazor.Grds` | What `GetPredefinedFilterDef(columnName)` returns: the column and the definition its filter starts from, its own `FilterDef` or else the one the grid's `FilterDefFactory` creates. |
| `BoolDef.BoolValue` | `Glazor.Grds.Filters` | The `Value` of a `BoolDef`: `All`, the default, `False` or `True`, as in `new BoolDef { Value = BoolDef.BoolValue.True }`. |
| `ARangeDef`, `AValueDef`, `ASetDef` | `Glazor.Grds.Filters` | The bases of the built-in definitions, a range (`NumberDef`, `DateDef`), a single value (`BoolDef`, `DefaultDef`) and a set (`EnumDef`, `DynaSetDef`), each with its `Includes` and its pushed-down predicate; a definition of your own derives from one of them, or from `AFilterDef<SELF, VT>`, and persists through a record derived from `AFilterDefSer`. |
| `ADynaSetDef<SELF, VT, RT, KT>`, `DistinctDef<SELF, VT>`, `DistinctRowValue<VT>` | `Glazor.Grds.Filters` | `ADynaSetDef` is the set whose values are read when its dialog opens, with the type of the dialog's rows, `RT`, apart from the column's, `VT`: `DynaSetDef` is the one whose rows are the column's values. `DistinctDef` is the one whose rows wrap them, each in a `DistinctRowValue<VT>`, so the column may hold a value type; a subclass supplies the values, `RetrieveValuesAsync`, as `EFDistinctDef`, in `Glazor.EntityFrameworkCore`, does from a query. See [Offering the values a column has](grigliaria.md#offering-the-values-a-column-has). |
| `ADynaValuesInSetDef<SELF, VT, RT, KT>`, `ADynaValuesInSetDefRenderer<SELF, VT, RT, KT>`, `DynaValuesInSetDefRenderer<SELF, VT, KT>` | `Glazor.Grds.Filters` | `ADynaValuesInSetDef` is the set over a collection column, `IQueryable<VT>`, whose values are read when its dialog opens, with the type of the dialog's rows, `RT`, apart from the items': `RowToKey` gives a row's key, which `ValueToKeyExpr` gives an item, and `VT` may be a value type. `DynaValuesInSetDef` is the one whose rows are the items, and `EFValuesInSetDef`, in `Glazor.EntityFrameworkCore`, the one whose rows a query gives, ready to use. A direct subclass names its own editor, a class deriving from `ADynaValuesInSetDefRenderer`, as `DynaValuesInSetDefRenderer` and `EFValuesInSetDefRenderer` do. See [A dialog with rows of its own](grigliaria.md#a-dialog-with-rows-of-its-own). |
| `FilterDefRendererType<T>`, `AFilterDefRenderer<TDef>` | `Glazor.Grds.Filters` | A definition's editor: `Renderer()` returns `new FilterDefRendererType<MyEditor>()`, and the filter dialog renders `MyEditor`, an `AFilterDefRenderer<TDef>`, with the definition as `FilterDef`, `NullableValue` for the "Include null" box and `DialogClosedToken`, a token cancelled when the dialog closes, for what the editor loads. It calls `ShownAsync` once the dialog is shown, and `Validate` before OK, which a `false` refuses. The built-in editors, `BoolDefRenderer`, `NumberDefRenderer`, `DateDefRenderer`, `DefaultDefRenderer`, `EnumDefRenderer`, `DynaSetDefRenderer`, `DistinctDefRenderer` and `DynaValuesInSetDefRenderer`, are the examples; `ADynaSetDefRenderer` is the base of the two before the last, for the editor of an `ADynaSetDef` of your own. |
| `FilterDefDlg`, `LayoutDefDlg<TGridItem>`, `RowViewDlg<TGridItem>` | `Glazor.Grds.Filters`, `Glazor.Grds` | The grid's filter, layout and row dialogs, which a grid subclass reaches through its protected `filterDefDlg`, `layoutDefDlg` and `rowViewDlg`, the last one only with `RowDialogEnabled`; a host opens them with `ShowFilterDialogAsync`, `ShowLayoutDialogAsync` and `ShowRowDialogAsync`. |
| `DefaultCell`, `DefaultHCell`, `DefaultGHCell`, `DefaultRowHeading`, `DefaultEmptyRow`, `DefaultCellEditor`, `EditEdge` | `Glazor.Grds.Decs` | The components the grid draws its cells with, each taking as `Param` what the matching template receives: a template draws `<DefaultCell Param="context"/>` to fall back to the grid's own cell, and they are the reference for the attributes a template supplies. `DefaultCell` also takes `Class` and `Style`, added to the cell's own, `ChildContent`, drawn in place of the value, and `MouseClickCancelBubble`, which stops a click on the cell from reaching the elements around it and cancels its default action. `DefaultCellEditor` is the editor of a cell in the row being edited, and `EditEdge` the Tab stop on each edge of the column window. |
| `IDataGrid<TGridItem>` | `Glazor.Grds` | `IGrid` with what a grid over an `IDataGridModel` adds: the cell cursor, `GotoLastKnown`, the touch scroller, the horizontal resizer, `Layoutable` and `OnLayoutChanged`, the `FilterDefFactory` and `IsFiltered`. `ADataGrid` implements it, and `IGrigliaria` extends it. |
| `IGrigliaria<TGridItem>` | `Glazor.Grds` | What `Grigliaria`, `MultiSelGrid` and `Treegliaria` share, so host code can take any of them: the columns shown and the current column, the filters (`GetFilter`, `HasFilter`, `SetFilter`, `GetFilters`, `SetFilters`), `ShowFilterDialogAsync`, `ShowLayoutDialogAsync` and `ShowRowDialogAsync`, `FocusAsync`, `RepaintAsync`, the row-heading and cell templates and `OnDblClick`. It has no memento methods: each grid has its own. A grid of your own implements it by deriving from one of the three, as the interface may gain members in a later release. |
| `ILayoutable<TGridItem>` | `Glazor.Grds` | What the layout dialog needs from a grid: its columns, those shown and their sizes, and the current column. |
| `IGetter<PT, TProp>`, `IExprGetter<PT, TProp>` | `Glazor.Grds.Props` | A column's getter, as `IColumnInfo.Property` and `GetProperty` hand it out: `GetterFunc` reads the value; `IExprGetter` adds `GetterExpr`, the expression an EF model filters and sorts in the database. |
| `OnModelChangedEventHandler` | `Glazor.Grds.Mdls` | The handler of a model's `OnModelChanged` event, told by a `GridModelChangeType` what changed. |
| `EqComparers.PropsComparers<P>` | `Glazor.Misc` | What `EqComparers.Combo` hands its initializer: `Add` one property after another, with its own `IEqualityComparer` or the default. |
| `TouchScroller.ScrollByEvent` | `Glazor.Grds` | What `TouchScroller.OnScrollBy` reports: `Dx` columns to scroll by, one at most either way, and `Dy` rows, at most three pages either way. |
