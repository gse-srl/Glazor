# Treegliaria and TreeGridModel

Contents:
- [The shape of it](#the-shape-of-it)
- [The row record is the node](#the-row-record-is-the-node)
- [Expansion is a list rebuild](#expansion-is-a-list-rebuild)
- [What the model gives you](#what-the-model-gives-you)
- [A tree in the database: EFFolderModel](#a-tree-in-the-database-effoldermodel)
- [Traps](#traps)

## The shape of it

`Treegliaria<TGridItem>` renders hierarchical rows using the same columns, filters and layout machinery as
`Grigliaria`. Its `Model` parameter is an `ITreeGridModel<TGridItem>`; the implementation you normally use is
`TreeGridModel<TGridItem>`, which derives from `DataGridModel<TGridItem>` and adds the constraint `where TGridItem : ITreeNode`.

The thing to understand before writing any of it: **the model holds a flat list, exactly like a
`DataGridModel`.** There is no tree structure inside it. Indentation and expander glyphs come from each row
answering `GetLevel()` and `IsExpanded()`, and *which rows are in the list at all* is your decision. Collapsing
a node does not hide anything by itself — you rebuild the list without that node's descendants.

Once that clicks, the rest is ordinary, with one consequence to plan for. Sorting and filtering also operate on the
flat list, row by row, so they know nothing of the hierarchy: a sort can move a child under another parent, and a
filter can keep a child and drop its parent. Keeping the tree coherent under a sort or a filter is your model's job,
in whatever way fits your data: sort within siblings, keep the ancestors of every match, or set `Sortable="false"` and
`Filterable="false"` on the tree's columns.

## The row record is the node

Implement `ITreeNode` on the row type itself rather than wrapping it. All five members are required:

```csharp
public record CategoryNode : ITreeNode
{
    public int Id { get; init; }
    public string Name { get; init; } = null!;
    public int? ParentId { get; init; }
    public int Level { get; init; }              // 0 is a root
    public bool Expanded { get; set; }
    public IList<int> ChildrenIds { get; init; } = [];

    int  ITreeNode.GetLevel()    => Level;
    bool ITreeNode.IsExpanded()  => Expanded;
    bool ITreeNode.IsLeaf()      => ChildrenIds.Count == 0;
    bool ITreeNode.IsSubTotal()  => false;
    public bool HasChildren()    => ChildrenIds.Count > 0;
}
```

Worth knowing about three of them:

- **`GetLevel()`** reserves `0` for roots, and drives the indentation. A tree of fixed depth can hard-code it:
  one production node type answers `IsLeaf() => Level == 1` with the comment *"only 2 levels are allowed"*,
  which is a perfectly good answer when the domain says so.
- **`HasChildren()` should return `true` when you do not know yet.** That is how a lazily-loaded tree gets an
  expander to click in the first place — answer `false` and the user can never ask you to load the children.
- **`IsSubTotal()`** marks a leaf that carries aggregates rather than a record. Most trees answer `false`.

Implementing the interface members explicitly (`int ITreeNode.GetLevel()`) keeps them off the record's own
surface, so the rest of your code reads `node.Level` rather than the interface method. Both production trees do
this.

## Expansion is a list rebuild

`TreeGridModel.OnSetExpand` is called when the user clicks an expander. Its job is to record the new state and
rebuild the flat list:

```csharp
public MyPage()
{
    _treeModel = new()
    {
        ItemKeyFn = n => n.Id,
        OnSetExpand = (int rowIndex, bool expanded) =>
        {
            SetExpand(_treeModel!.GetItem(rowIndex), expanded);
            return ValueTask.CompletedTask;
        },
    };
}

private void SetExpand(CategoryNode node, bool expanded)
{
    if (node.Expanded == expanded)
        return;
    if (expanded) _collapsed.Remove(node.Id); else _collapsed.Add(node.Id);
    Rebuild();
}

private void Rebuild() => _treeModel.GetData().Set(Flatten());
```

The app keeps the collapsed set — a `HashSet<int>` of ids — and `Flatten()` walks the real hierarchy emitting
only the rows that should be visible, stamping `Level` and `Expanded` as it goes. That set is the thing worth
holding onto across a reload: rebuild from fresh data and the tree comes back open where the user left it.

Note the model is built in the **constructor**, because `OnSetExpand` is `init`-only and the closure needs
`_treeModel` itself. Assigning it in `OnInitialized` works too; building it in a field initializer does not
compile, because a field initializer cannot reference another instance field (CS0236).

## What the model gives you

Everything `DataGridModel` does — `GetData()`, `ItemKeyFn`, filters and sorting, row by row as above — plus:

```csharp
ITreeNode? current = _treeModel.GetCurrentTreeNode();
ITreeNode  node    = _treeModel.GetTreeNode(rowIdx);
await _treeModel.SetExpandedAsync(rowIdx, expanded);   // what the grid calls; you can too
```

`SetExpandedAsync` just forwards to your `OnSetExpand`, which is useful for an "expand all" button: walk the
rows calling it, or bypass it and rebuild once, which is cheaper.

The markup is `Grigliaria`'s, with the same resizers and column declarations; `StateKey` is the host subclass's own
parameter, as on `XGrigliaria`:

```razor
<XTreegliaria @ref="_treeRef" TGridItem="CategoryNode" Model="_treeModel"
              HeaderHeight="50" RowHeight="40" Layoutable="true"
              HorzResizer="@(GridHorzResizers.DispUnitWidth(50, 150, 12))"
              VertResizer="@(GridVertResizers.RowHeight("height: 60vh;", 4, 20))"
              StateKey="@(nameof(Categories) + "_SK")"
              OnRefreshData="@RefreshAsync">
    <GridColumn Property="@(x => x.Name)" Name="Category" Label="Category" PreferredSize="30" />
</XTreegliaria>
```

To show the node's label in the tree itself, after the indent and expander, name its column with `TreeColumn="Name"`: that
column, fixed in front of the columns that are not, carries the tree and keeps its own sort, filter and size; declare it
before any other fixed column to have it first. To style the label, give
`DefaultTreeCell` child content in a `TreeCellTemplate` (`<DefaultTreeCell Param="context"><b>@name</b></DefaultTreeCell>`),
which takes the value's place. A label that opens its node is an `Anchor` calling `context.Grid.ToggleExpandAsync(row)`.

If you have hoisted layout persistence and a toolbar into a `Grigliaria` subclass, `Treegliaria` takes the
same treatment — see `SKILL.md`. Used directly it works exactly as well; the sample applications happen to
pair their grid wrapper with a tree wrapper.

## A tree in the database: EFFolderModel

When the hierarchy lives in tables, `EFFolderModel<TDbContext, TGridItem>` (in `Glazor.EntityFrameworkCore`) replaces
the flatten-and-rebuild above: the row type is a plain DTO, not an `ITreeNode`, and the model keeps the nodes itself.

```csharp
_model = new EFFolderModel<AppDbContext, EntryDto>
{
    DbContextFactory = DbFactory,
    ItemKeyExpr = e => e.Id,                       // the row's own, wherever it sits
    IsFolderFn = e => e.IsFolder,                  // false: a leaf, no expander, no query
    RootsQueryFn = db => db.Entries.AsNoTracking().Where(e => e.ParentId == null)
        .Select(e => new EntryDto { /* … */ }).OrderBy(e => e.Name),   // the model adds the key after it
    ChildrenQueryFn = (db, parent) => db.Entries.AsNoTracking().Where(e => e.ParentId == parent.Id)
        .Select(e => new EntryDto { /* … */ }).OrderBy(e => e.Name),
    ParentFn = async (db, child, ct) => await db.Entries.AsNoTracking().Where(e => e.Id == child.ParentId)
        .Select(e => new EntryDto { /* … */ }).FirstOrDefaultAsync(ct),   // only GoToAsync needs it; a virtual parent as it is
};
```

- Roots are read in passes of `MinRowsToRead` (512) as the grid pages; a folder's children the first time it opens, then
  kept until `ForceReload`. Pass a `Spinner`, as for any EF model.
- It is tree-aware where `TreeGridModel` is not: each level sorted among siblings (the roots by the database for an
  `EFProperty` column), and filters in memory keeping a folder while a row read below it matches. Leave columns sortable
  and filterable.
- Rows of several kinds (artists, albums, tracks) share one DTO with an id range per kind, and `ChildrenQueryFn` switches
  on the parent's kind.
- `VirtualChildrenFn(parent)` (null: the roots) adds folders no table holds, shown first; `ChildrenQueryFn` gives their
  children too. A filter keeps one only for a match read below it.
- `ChildrenPageSize` pages a folder's children; `MoreRowFn(folder)` makes the "More…" row below a page, whose expander reads
  the next one. A paged level sorts in the database for an `EFProperty` column, and is read whole for any other.
- `ReloadItemAsync`, `ForceReload(true)` and `GoToAsync(item)` keep or move the current row by its path.
- A row may sit under several parents: each place expanded on its own, its children read once, and what finds a row by
  item or key (`SetCurrentItem`) finding its first place.
- It is an `IFolderGridModel`: `ExpandAllAsync(row)` and `CollapseAllAsync(row)` (-1 the whole tree; expand all reads
  every page below, a query per folder), `ZoomInAsync()` on the current folder, or a leaf's, `ZoomOutAsync(levels)` (0
  all the way out), `GetZoomedItem()` and `GetZoomLevel()` to show where the zoom is. `Treegliaria` binds the four
  actions to `Ctrl`+`Shift`+`↓`/`↑`/`→`/`←`; a toolbar button calls them directly. They read nothing themselves: the grid's
  next render reads what they need, under its spinner, as after a filter or a sort. Called twice from code with no render
  between, one that takes a row builds the rows first, outside the spinner. A current row past the rows read, after a page
  down, is read first too, outside the spinner, so that it stays on its record. A bar above the header has
  zoom in, zoom out and zoom out all buttons and, while zoomed, the path to zoom back out from; `ZoomLabel` names its
  folders, and `ShowZoomBar="false"` hides it for a page with its own toolbar.
- No editing, no search on the sorted column.
- It is an `ITreeStateGridModel` too, so `Treegliaria.CreateMemento()` over it returns a `TreeMemento`: the grid's memento
  plus the folders expanded and the zoom, each as the keys of its path. It needs a key of one value (a number, a string, a
  `Guid`, a date); over a composite `ItemKeyExpr` `CanSaveTreeState()` is false, and `CreateMemento` returns the grid's
  plain `Memento`. A host's `TreeGridModelWrapper` around it keeps both: the wrapper forwards the folder and tree-state
  members, and its `IsFolderModel()` and `CanSaveTreeState()` follow the model it wraps. A restore reads the folders again at the
  next build, one query each; one gone is skipped, a zoom on one goes to its nearest ancestor.

## Traps

**`OnSetExpand` is `required`.** A model built without it does not compile (CS9035): set it in the object
initializer, with the closure that records the new state.

**Over `TreeGridModel` the expansion is not part of the memento.** The in-memory model leaves expansion to the host
(`OnSetExpand`), so `Treegliaria`'s memento holds only columns, widths, filters and sort there. To have the expansion
saved, implement `ITreeStateGridModel` on your model: `CanSaveTreeState()` returns true, `GetTreeState()` returns a `TreeState` (`Expanded`, a list of
`NodePath`, and `Zoom`, a `NodePath` or null; a `NodePath` is the keys from a root down to a node), and
`RestoreTreeState(state)` takes it back, its keys `JsonElement`s when read from JSON. Or save your collapsed set
yourself alongside the memento. Over an `ITreeStateGridModel` such as `EFFolderModel` it is a `TreeMemento`, expansion and
zoom included.

**`ITreeNode` members are read every render.** Keep them as field reads rather than computations —
`Level` and `Expanded` stored on the record, not derived by walking parents.
