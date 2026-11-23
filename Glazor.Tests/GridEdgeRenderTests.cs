using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The parts of the public surface no other test rendered: the host templates and column groups, the stabilizer, the
/// ordered MultiSelGrid, the tree grid's memento and expansion, the timeout reaching the host, the column component's
/// argument checks and the Anchor.
/// </summary>
public class GridEdgeRenderTests
{
    private sealed record Product(string Code, string Name, decimal Price);

    private static Product[] Rows(int count) =>
        Enumerable.Range(0, count).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray();

    private static DataGridModel<Product> ModelOf(int rowCount, string? group = null)
    {
        DataGridModel<Product> model = new(Rows(rowCount));
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, GroupId = group }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10, GroupId = group }, p => p.Price);
        return model;
    }

    private static IRenderedComponent<Grigliaria<Product>> RenderGrid(
        BunitContext ctx, IDataGridModel<Product> model, Action<ComponentParameterCollectionBuilder<Grigliaria<Product>>>? extra = null)
        => ctx.Render<Grigliaria<Product>>(ps =>
        {
            ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner());
            extra?.Invoke(ps);
        });

    private static RenderFragment<T> Cell<T>(string tag, string cls, Func<T, string> text) => p => builder =>
    {
        builder.OpenElement(0, tag);
        builder.AddAttribute(1, "class", cls);
        builder.AddContent(2, text(p));
        builder.CloseElement();
    };

    // ─────────────────────────── templates ───────────────────────────

    /// <summary>
    /// A host's header, group header and row heading templates replace the grid's own cells; the group header spans the
    /// columns of its group and gets their indexes.
    /// </summary>
    [Test]
    public void HeaderGroupAndRowHeadingTemplatesAreHonouredTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(2, "Details"), ps => ps
            .Add(g => g.ShowColumnGroups, true)
            .Add(g => g.HCellTemplate, Cell<IGrid<Product>.HCellTemplateParam>("th", "host-h", p => "H:" + p.Column.GetLabel()))
            .Add(g => g.GHCellTemplate, Cell<IGrid<Product>.GHCellTemplateParam>("th", "host-gh", p => $"G:{p.GroupId}:{p.ColSpan}:{p.ColIdxs.Count}"))
            .Add(g => g.RowHeadingTemplate, Cell<IGrigliaria<Product>.RowHeadingTemplateParam>("td", "host-rh", p => "R:" + p.RowIdx)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("th.host-gh").Select(th => th.TextContent), Is.EqualTo(new[] { "G:Details:2:2" }), "one group over two columns");
            Assert.That(cut.FindAll("th.host-h").Select(th => th.TextContent), Is.EquivalentTo(new[] { "H:Code", "H:Name", "H:Price" }));
            Assert.That(cut.FindAll("td.host-rh").Select(td => td.TextContent), Is.EqualTo(new[] { "R:0", "R:1" }));
            Assert.That(cut.FindAll("th.host-h, th.host-gh").Count, Is.EqualTo(4), "no default header cell beside them");
        });
    }

    /// <summary>
    /// Without rows the page is filled with empty rows, each through the host's template with its index.
    /// </summary>
    [Test]
    public void EmptyRowTemplateFillsThePageTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(0), ps => ps.Add(g => g.PageSize, 3)
            .Add(g => g.EmptyRowCellTemplate, Cell<IGrid<Product>.EmptyRowTemplateParam>("td", "host-empty", p => "E:" + p.RowIdx)));
        Assert.That(cut.FindAll("td.host-empty").Select(td => td.TextContent), Is.EqualTo(new[] { "E:0", "E:1", "E:2" }));
    }

    // ─────────────────────────── stabilizer ───────────────────────────

    /// <summary>
    /// The stable item follows the current row once the selection settles: it is raised when it changes, with the item, and not
    /// again while it stays.
    /// </summary>
    [Test]
    public async Task StableItemFollowsTheSettledSelectionTest()
    {
        using BunitContext ctx = NewContext();
        List<Product?> stable = [];
        var cut = RenderGrid(ctx, ModelOf(3), ps => ps.Add(g => g.OnStableItemChanged, (Product? p) => stable.Add(p)));
        Assert.That(cut.Instance.GetStableItem(), Is.Null, "nothing settled yet");
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetStableItem()?.Code, Is.EqualTo("C00"), "the first row is current after the first render");
            Assert.That(stable.Select(p => p?.Code), Is.EqualTo(new[] { "C00" }), "raised once");
        });
        await cut.InvokeAsync(() => cut.Instance.SetCurrentRow(2));
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetStableItem()?.Code, Is.EqualTo("C02"));
            Assert.That(stable.Select(p => p?.Code), Is.EqualTo(new[] { "C00", "C02" }));
        });
    }

    /// <summary>
    /// The stable item is the instance the model holds now: a row reloaded with the same key replaces it without raising the
    /// change again, and a new model, whose current row may have the same key as the old one's, raises it with its own row.
    /// </summary>
    [Test]
    public async Task StableItemFollowsAReloadedRowAndANewModelTest()
    {
        using BunitContext ctx = NewContext();
        static DataGridModel<Product> Keyed()
        {
            DataGridModel<Product> model = new(Rows(3)) { ItemKeyFn = p => p.Code };
            model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
            return model;
        }
        DataGridModel<Product> first = Keyed();
        List<Product?> stable = [];
        var cut = RenderGrid(ctx, first, ps => ps.Add(g => g.OnStableItemChanged, (Product? p) => stable.Add(p)));
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());

        Product reloaded = new("C00", "Reloaded", 9m);
        await cut.InvokeAsync(() => first.GetData()[0] = reloaded);
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetStableItem(), Is.SameAs(reloaded), "the instance the model holds now");
            Assert.That(stable, Has.Count.EqualTo(1), "the same row: not raised again");
        });

        DataGridModel<Product> second = Keyed();
        cut.Render(ps => ps.Add(g => g.Model, second));
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetStableItem(), Is.SameAs(second.GetCurrentItem()), "the new model's row");
            Assert.That(stable, Has.Count.EqualTo(2), "raised for the new model");
        });
    }

    /// <summary>
    /// A new model with no current row, an empty result, raises the stable item with null: the host was last told of the old
    /// model's row. Another empty model after it raises nothing more.
    /// </summary>
    [Test]
    public async Task StableItemIsNullAfterANewModelWithNoRowTest()
    {
        using BunitContext ctx = NewContext();
        List<Product?> stable = [];
        var cut = RenderGrid(ctx, ModelOf(3), ps => ps.Add(g => g.OnStableItemChanged, (Product? p) => stable.Add(p)));
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        cut.Render(ps => ps.Add(g => g.Model, ModelOf(0)));
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        Assert.That(stable.Select(p => p?.Code), Is.EqualTo(new[] { "C00", null }));
        cut.Render(ps => ps.Add(g => g.Model, ModelOf(0)));
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        Assert.That(stable, Has.Count.EqualTo(2), "null again is no change");
    }

    /// <summary>
    /// A model the host already put on a row raises the stable item once the grid shows it, as one the grid puts on its first
    /// row does: a model in sync raises no change at the first render.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void StableItemIsRaisedForAModelAlreadyOnARowTest(bool preselected)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        if (preselected)
            model.SetCurrentRow(1);
        List<Product?> stable = [];
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.OnStableItemChanged, (Product? p) => stable.Add(p)));
        cut.WaitForState(() => stable.Count > 0, TimeSpan.FromSeconds(2));
        Assert.That(stable.Select(p => p?.Code), Is.EqualTo(new[] { preselected ? "C01" : "C00" }));
    }

    /// <summary>
    /// An exception from the host's handler reaches the renderer, and so an ErrorBoundary, as the other host callbacks do.
    /// </summary>
    [Test]
    public void StableItemHandlerExceptionReachesTheErrorBoundaryTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<ErrorBoundary>(ps => ps
            .Add(e => e.ChildContent, b =>
            {
                b.OpenComponent<Grigliaria<Product>>(0);
                b.AddAttribute(1, nameof(Grigliaria<Product>.Model), ModelOf(3));
                b.AddAttribute(2, nameof(Grigliaria<Product>.Spinner), new NoSpinner());
                b.AddAttribute(3, nameof(Grigliaria<Product>.OnStableItemChanged),
                    EventCallback.Factory.Create<Product?>(this, (Product? p) => throw new InvalidOperationException("host failed")));
                b.CloseComponent();
            })
            .Add(e => e.ErrorContent, ex => b => b.AddMarkupContent(0, "<p class='host-error'>" + ex.Message + "</p>")));
        cut.WaitForState(() => cut.FindAll("p.host-error").Count > 0, TimeSpan.FromSeconds(2));
        Assert.That(cut.Find("p.host-error").TextContent, Is.EqualTo("host failed"));
    }

    // ─────────────────────────── ordered MultiSelGrid ───────────────────────────

    private sealed record Step(string Name);

    private static GridModel<Step> Steps(params string[] names)
    {
        GridModel<Step> model = new() { ItemKeyFn = s => s.Name };
        model.GetData().Set(names.Select(n => new Step(n)).ToList());
        model.AddColumn("Name", new Column<Step, string>("Name"), s => s.Name);
        return model;
    }

    /// <summary>
    /// The move buttons swap the current row with its neighbour in the ordered model and raise <c>OnRowShift</c>; the current
    /// row follows its item.
    /// </summary>
    [Test]
    public async Task MoveButtonsSwapTheRowsAndRaiseOnRowShiftTest()
    {
        using BunitContext ctx = NewContext();
        GridModel<Step> model = Steps("a", "b", "c");
        int shifts = 0;
        var cut = ctx.Render<MultiSelGrid<Step>>(ps => ps.Add(g => g.Model, model)
            .Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Step>(model.GetData().ToList()))
            .Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.OnRowShift, () => shifts++));
        var grid = cut.FindComponent<Grigliaria<Step>>();
        await cut.InvokeAsync(() => grid.Instance.SetCurrentRow(0));
        cut.Render();
        Assert.That(cut.Find("button.btnMoveRowUp").HasAttribute("disabled"), Is.True, "the first row cannot move up");
        await cut.Find("button.btnMoveRowDown").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetData().Select(s => s.Name), Is.EqualTo(new[] { "b", "a", "c" }));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(1), "the current row follows its item");
            Assert.That(shifts, Is.EqualTo(1));
        });
        await cut.Find("button.btnMoveRowUp").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetData().Select(s => s.Name), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(shifts, Is.EqualTo(2));
        });
    }

    /// <summary>A model that can filter and sort, and is ordered too.</summary>
    private sealed class OrderedDataModel : DataGridModel<Step>, IOrderedGridModel<Step>
    {
        public readonly List<(int, int)> Swaps = [];

        public void SwapRows(int row1, int row2) => Swaps.Add((row1, row2));

        public bool AreRowsSwappable(int row1, int row2) => true;
    }

    /// <summary>
    /// A model both ordered and filterable is treated as ordered: the user arranges its rows by hand, so the grid shows the move
    /// buttons and offers neither sorting nor filtering, which would reorder the rows under them.
    /// </summary>
    [Test]
    public async Task AModelOrderedAndFilterableIsTreatedAsOrderedTest()
    {
        using BunitContext ctx = NewContext();
        OrderedDataModel model = new() { ItemKeyFn = s => s.Name };
        model.GetData().Set([new("a"), new("b"), new("c")]);
        model.AddColumn("Name", new Column<Step, string>("Name"), s => s.Name);
        Assert.That(model.IsSortable() && model.IsFilterable(), Is.True, "the model could sort and filter on its own");
        int shifts = 0;
        var cut = ctx.Render<MultiSelGrid<Step>>(ps => ps.Add(g => g.Model, model)
            .Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Step>(model.GetData().ToList()))
            .Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.OnRowShift, () => shifts++));
        var grid = cut.FindComponent<Grigliaria<Step>>();
        await cut.InvokeAsync(() => grid.Instance.SetCurrentRow(0));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.IsOrderable(), Is.True);
            Assert.That(cut.FindAll("th[aria-sort]"), Is.Empty, "no column offers a sort");
            Assert.That(cut.FindAll("button.btnMoveRowDown"), Has.Count.EqualTo(1), "the move buttons are shown");
        });
        await cut.Find("button.btnMoveRowDown").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.Swaps, Is.EqualTo(new[] { (0, 1) }), "the move goes to the ordered model");
            Assert.That(shifts, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// <see cref="GridModel{TGridItem}.SwapRows"/> refuses rows outside the list and rows the host's test does not allow.
    /// </summary>
    [Test]
    public void SwapRowsRefusesBadRowsTest()
    {
        GridModel<Step> model = new() { ItemKeyFn = s => s.Name, AreRowsSwappableFn = (m, r1, r2) => m.GetItem(r1).Name != "c" && m.GetItem(r2).Name != "c" };
        model.GetData().Set([new("a"), new("b"), new("c")]);
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => model.SwapRows(-1, 0));
            Assert.Throws<ArgumentException>(() => model.SwapRows(0, 3));
            Assert.Throws<ArgumentException>(() => model.SwapRows(1, 2), "the host's test says no");
            Assert.DoesNotThrow(() => model.SwapRows(1, 1), "a row with itself is nothing to do");
            model.SwapRows(0, 1);
            Assert.That(model.GetData().Select(s => s.Name), Is.EqualTo(new[] { "b", "a", "c" }));
        });
    }

    /// <summary>
    /// A read-only MultiSelGrid shows the selection and refuses to change it, from the select-all box as from a row.
    /// </summary>
    [Test]
    public void ReadOnlyGridRefusesSelectionChangesTest()
    {
        using BunitContext ctx = NewContext();
        GridModel<Step> model = Steps("a", "b");
        SetSelectionModelWithSelectAll<Step> selection = new(model.GetData().ToList());
        selection.Select(model.GetItem(0), true);
        var cut = ctx.Render<MultiSelGrid<Step>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.ReadOnly, true));
        var grid = cut.FindComponent<Grigliaria<Step>>();
        cut.InvokeAsync(() => grid.Instance.SetCurrentRow(1));
        var stroke = cut.FindComponents<KeyAction>().Single(k => k.Instance.CtrlKey && k.Instance.Key == " ");
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.SelectAll, Is.Null, "one of two selected");
            Assert.ThrowsAsync<InvalidOperationException>(() => cut.InvokeAsync(() => cut.Instance.SelectAll = true));
            Assert.That(stroke.Instance.Enabled, Is.False, "Ctrl+Space is off");
            Assert.That(selection.SelectedCount(), Is.EqualTo(1), "unchanged");
            Assert.That(cut.Find("button.btnMoveRowDown").HasAttribute("disabled"), Is.True, "no reordering either");
        });
    }

    // ─────────────────────────── Treegliaria ───────────────────────────

    private sealed record Node(string Name, int Level = 0, bool Children = false) : ITreeNode
    {
        public bool IsExpanded() => false;
        public int GetLevel() => Level;
        public bool IsLeaf() => !Children;
        public bool IsSubTotal() => false;
        public bool HasChildren() => Children;
    }

    private static TreeGridModel<Node> Tree(Func<int, bool, ValueTask>? onSetExpand, params Node[] nodes)
    {
        TreeGridModel<Node> tree = new(nodes) { OnSetExpand = onSetExpand! };
        tree.AddColumn("Name", new Column<Node, string>("Name") { PreferredSize = 20 }, n => n.Name);
        tree.AddColumn("Level", new Column<Node, int>("Level") { PreferredSize = 10 }, n => n.Level);
        return tree;
    }

    /// <summary>A <c>TreeColumn</c> that names no column of the model fails at the first render, also with no row to draw.</summary>
    [Test]
    public void AnUnknownTreeColumnFailsOnAnEmptyModelTooTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> empty = Tree(null);
        Assert.Throws<InvalidOperationException>(() => ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, empty)
            .Add(g => g.TreeColumn, "Missing")));
    }

    /// <summary>The wrapper grids read and move the cell cursor's column as Grigliaria does, and the cell drawn current follows it.</summary>
    [Test]
    public async Task WrapperGridsGetAndSetTheCurrentColumnTest()
    {
        using BunitContext ctx = NewContext();
        var multi = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, ModelOf(2)).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()));
        var tree = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, Tree(null, new("root", 0, true), new("child", 1))));
        bool multiMoved = false, treeMoved = false;
        await multi.InvokeAsync(() => multiMoved = multi.Instance.SetCurrentCol("Price"));
        await tree.InvokeAsync(() => treeMoved = tree.Instance.SetCurrentCol("Level"));
        Assert.Multiple(() =>
        {
            Assert.That((multiMoved, multi.Instance.GetCurrentCol()), Is.EqualTo((true, "Price")), "MultiSelGrid");
            Assert.That(multi.FindAll("td.data.current"), Has.Count.EqualTo(1), "MultiSelGrid's current cell");
            Assert.That((treeMoved, tree.Instance.GetCurrentCol()), Is.EqualTo((true, "Level")), "Treegliaria");
            Assert.That(tree.FindAll("td.data.current"), Has.Count.EqualTo(1), "Treegliaria's current cell");
        });
    }

    /// <summary>Host code that takes an IGrigliaria sets and clears the filters of any of the three grids.</summary>
    [Test]
    public async Task FiltersThroughIGrigliariaOnEveryGridTest()
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(2));
        var multi = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, ModelOf(2)).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()));
        var tree = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, Tree(null, new("root", 0, true), new("child", 1))));

        static (bool Set, int Left) FilterAndClear<T>(IGrigliaria<T> g)
        {
            g.SetFilter("Name", new DefaultDef { Value = "x" });
            bool set = g.HasFilter("Name") && g.IsFiltered();
            g.SetFilters(new Dictionary<string, AFilterDef>());
            return (set, g.GetFilters().Count);
        }
        (bool, int) onGrid = default, onMulti = default, onTree = default;
        await grid.InvokeAsync(() => onGrid = FilterAndClear(grid.Instance));
        await multi.InvokeAsync(() => onMulti = FilterAndClear(multi.Instance));
        await tree.InvokeAsync(() => onTree = FilterAndClear(tree.Instance));
        Assert.Multiple(() =>
        {
            Assert.That(onGrid, Is.EqualTo((true, 0)), "Grigliaria");
            Assert.That(onMulti, Is.EqualTo((true, 0)), "MultiSelGrid");
            Assert.That(onTree, Is.EqualTo((true, 0)), "Treegliaria");
        });
    }

    /// <summary>The wrapper grids give the page size in effect, the VertResizer's, through IGrid as Grigliaria does.</summary>
    [Test]
    public void WrapperGridsGiveThePageSizeInEffectTest()
    {
        using BunitContext ctx = NewContext();
        var multi = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, ModelOf(20)).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()).Add(g => g.VertResizer, GridVertResizers.Fixed(7)));
        var tree = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, Tree(null, new("root", 0, true), new("child", 1)))
            .Add(g => g.VertResizer, GridVertResizers.Fixed(7)));
        IGrid<Product> multiGrid = multi.Instance;
        IGrid<Node> treeGrid = tree.Instance;
        Assert.Multiple(() =>
        {
            Assert.That((multiGrid.PageSize, multiGrid.GetPageSize()), Is.EqualTo((5, 7)), "MultiSelGrid");
            Assert.That((treeGrid.PageSize, treeGrid.GetPageSize()), Is.EqualTo((5, 7)), "Treegliaria");
        });
    }

    /// <summary>
    /// Under the cell cursor a click on a tree row's indent, or on a leaf's spacer, makes the row current as one on its label
    /// does; the expander's cell is left to the expander, so a folder's row gets no handler of its own.
    /// </summary>
    [Test]
    public async Task ATreeRowsIndentTakesTheClickTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = Tree(null, new("root", 0, true), new("child", 1));
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        List<MouseAction> OwnClicks(int rowIdx)
        {
            var cell = cut.FindComponents<Glazor.Grds.Decs.DefaultTreeCell<Node>>()
                .Single(c => c.Instance.Param.CellParam.RowIdx == rowIdx && c.FindAll("td.indent").Count > 0);
            HashSet<MouseAction> label = cell.FindComponents<Glazor.Grds.Decs.DefaultCell<Node>>()
                .SelectMany(d => d.FindComponents<MouseAction>()).Select(m => m.Instance).ToHashSet();
            return cell.FindComponents<MouseAction>().Select(m => m.Instance)
                .Where(m => m.Type == MouseAction.MouseType.Click && !label.Contains(m)).ToList();
        }

        Assert.That(OwnClicks(0), Is.Empty, "the folder's row: its expander");
        List<MouseAction> leaf = OwnClicks(1);
        Assert.That(leaf, Has.Count.EqualTo(2), "the leaf's indent and spacer");
        foreach (MouseAction click in leaf)
        {
            await cut.InvokeAsync(() => tree.SetCurrentRow(0));
            await cut.InvokeAsync(() => click.ProcessAsync(null, null, 0, 0, 0));
            Assert.That(tree.GetCurrentRow(), Is.EqualTo(1));
        }
    }

    /// <summary>
    /// A new <c>TreeColumn</c> wraps the same model again: the columns the user arranged stay as they were, and the row
    /// already reported as stable is not reported again.
    /// </summary>
    [Test]
    public async Task ANewTreeColumnKeepsTheColumnsTheUserArrangedTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = Tree(null, new("root", 0, true), new("child", 1));
        tree.AddColumn("Other", new Column<Node, string>("Other") { PreferredSize = 10 }, n => n.Name + "!");
        List<Node?> stable = [];
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree).Add(g => g.Layoutable, true)
            .Add(g => g.OnStableItemChanged, (Node? n) => stable.Add(n)));
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Other", "Name"));   // Level hidden
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());

        cut.Render(ps => ps.Add(g => g.TreeColumn, "Other"));
        await cut.InvokeAsync(() => cut.Instance.StabilizeItemAsync());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Other", "Name" }));
            Assert.That(stable, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// The tree grid's memento is the inner grid's: sort and filters set on one tree come back on another through it, and a
    /// grid restored from it saves the same memento.
    /// </summary>
    [Test]
    public async Task TreegliariaMementoRoundTripsTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = Tree(null, new("root", 0, true), new("child", 1));
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        await cut.InvokeAsync(() => tree.SetSortedColumn("Level", false));
        cut.Render();
        Memento memento = cut.Instance.CreateMemento();
        Assert.That(memento.SorterColumns.Select(s => (s.Name, s.Asc)), Is.EqualTo(new[] { ("Level", false) }));

        TreeGridModel<Node> other = Tree(null, new("root", 0, true), new("child", 1));
        var restored = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, other));
        Assert.That(other.GetSortedColumn(), Is.Null);
        await restored.InvokeAsync(() => restored.Instance.RestoreMemento(memento));
        restored.Render();
        Assert.Multiple(() =>
        {
            Assert.That(other.GetSortedColumn(), Is.EqualTo("Level"));
            Assert.That(restored.Instance.CreateMemento(), Is.EqualTo(memento));
        });
    }

    /// <summary>A tree memento restored over a model with no tree state puts back the grid's part and ignores the tree.</summary>
    [Test]
    public async Task ATreeMementoOverAModelWithoutTreeStateRestoresTheGridTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = Tree(null, new("root", 0, true), new("child", 1));
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        TreeMemento memento = new(null, new Dictionary<string, AFilterDef>(), [new SortedColumn("Level", false)],
            new TreeState([new NodePath(["root"])], null));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(memento));
        Assert.That(tree.GetSortedColumn(), Is.EqualTo("Level"));
    }

    /// <summary>A host's own tree model that keeps its tree state.</summary>
    private sealed class StatefulTree : TreeGridModel<Node>, ITreeStateGridModel<Node>
    {
        public TreeState State = new([], null);

        public StatefulTree() : base([new("root", 0, true), new("child", 1)])
        {
            AddColumn("Name", new Column<Node, string>("Name") { PreferredSize = 20 }, n => n.Name);
        }

        public bool CanSaveTreeState() => true;
        public TreeState GetTreeState() => State;
        public void RestoreTreeState(TreeState state) => State = state;
    }

    private sealed class HostTreeWrapper(ITreeGridModel<Node> wrapped) : TreeGridModelWrapper<Node>(wrapped);

    /// <summary>
    /// A host's wrapper around a tree model with neither folders nor tree state has none either: no zoom bar, a plain memento.
    /// </summary>
    [Test]
    public void AWrapperOverAPlainTreeHasNoFoldersNorTreeStateTest()
    {
        using BunitContext ctx = NewContext();
        HostTreeWrapper wrapper = new(Tree(null, new("root", 0, true), new("child", 1)));
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, wrapper));
        Assert.Multiple(() =>
        {
            Assert.That((wrapper.IsFolderModel(), wrapper.CanSaveTreeState()), Is.EqualTo((false, false)));
            Assert.That(cut.FindAll("nav.zoombar"), Is.Empty);
            Assert.That(cut.Instance.CreateMemento(), Is.TypeOf<Memento>());
        });
    }

    /// <summary>
    /// Over a host's own <see cref="ITreeStateGridModel{TGridItem}"/> the tree grid saves the model's tree state in a tree memento,
    /// and gives it back to another such model through JSON.
    /// </summary>
    [Test]
    public async Task AHostTreeStateModelIsSavedThroughTheTreeGridTest()
    {
        using BunitContext ctx = NewContext();
        StatefulTree tree = new() { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.State = new([new NodePath(["root"])], new NodePath(["root"]));
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        Memento memento = cut.Instance.CreateMemento();
        Memento read = System.Text.Json.JsonSerializer.Deserialize<Memento>(System.Text.Json.JsonSerializer.Serialize(memento))!;

        StatefulTree other = new() { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        var restored = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, other));
        await restored.InvokeAsync(() => restored.Instance.RestoreMemento(read));
        Assert.Multiple(() =>
        {
            Assert.That(memento, Is.TypeOf<TreeMemento>());
            Assert.That(other.State, Is.EqualTo(tree.State));
        });
    }

    /// <summary>
    /// Ctrl+Space, or a click on the expand control, asks the model to expand the current node when it has children.
    /// </summary>
    [Test]
    public async Task ExpandingANodeWithChildrenAsksTheModelTest()
    {
        using BunitContext ctx = NewContext();
        List<(int row, bool expanded)> expansions = [];
        TreeGridModel<Node> tree = Tree((row, expanded) => { expansions.Add((row, expanded)); return ValueTask.CompletedTask; },
            new("root", 0, true), new("leaf", 1));
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        var stroke = cut.FindComponents<KeyAction>().Single(k => k.Instance.CtrlKey && k.Instance.Key == " ");
        await cut.InvokeAsync(() => stroke.Instance.ProcessAsync(" ", null, null));
        Assert.That(expansions, Is.EqualTo(new[] { (0, true) }), "the root is current after the first render, and collapsed");
        await cut.InvokeAsync(() => cut.Instance.ToggleExpandAsync(1));
        Assert.Multiple(() =>
        {
            Assert.That(expansions, Has.Count.EqualTo(1), "a leaf has nothing to expand");
            Assert.That(tree.GetCurrentRow(), Is.EqualTo(1), "but it is current now");
        });
    }

    // ─────────────────────────── timeout ───────────────────────────

    /// <summary>An in-memory model made to look paged, whose fetch takes as long as the test says.</summary>
    private sealed class SlowModel() : DataGridModelWrapper<Product>(ModelOf(3))
    {
        public int FetchMs;

        public override bool RequiresFetch() => true;
        public override async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
        {
            if (FetchMs > 0)
                await Task.Delay(FetchMs, ct);
            return await base.FetchRowsAsync(rowIndex, count, ct);
        }
    }

    /// <summary>
    /// A paged model of ten rows that has read five and does not know its count until it reads the last; reading past what it
    /// has read takes as long as asked. Like the EF model, it takes any current row below its count.
    /// </summary>
    private sealed class UnknownCountModel() : DataGridModelWrapper<Product>(ModelOf(10))
    {
        public int FetchMs;
        private int _read = 5;
        private int _current;

        public override bool RequiresFetch() => true;
        public override int GetCurrentRowCount() => _read;
        public override int GetRowCount() => _read < base.GetRowCount() ? int.MaxValue : _read;
        public override int GetCurrentRow() => _current;
        public override void SetCurrentRow(int rowIndex) => _current = rowIndex;
        public override async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
        {
            if (rowIndex + count > _read)
            {
                if (FetchMs > 0)
                    await Task.Delay(FetchMs, ct);
                _read = Math.Min(base.GetRowCount(), rowIndex + count);
            }
            return Math.Max(0, Math.Min(count, _read - rowIndex));
        }
    }

    /// <summary>
    /// A paged model with rows and no current row gets the first, as an in-memory one does, also when the page's fetch is what
    /// finds the end.
    /// </summary>
    [Test]
    public void NoCurrentRowGetsTheFirstWhenThePageFindsTheEndTest()
    {
        using BunitContext ctx = NewContext();
        UnknownCountModel model = new();
        model.SetCurrentRow(-1);
        RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 20));
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(10), "the page's fetch found the end");
            Assert.That(model.GetCurrentRow(), Is.Zero);
        });
    }

    /// <summary>
    /// End that spends the budget reading to the end, with the timeout handled by the host, leaves the current row where it was:
    /// a row past what was read would make every later render read to the end again.
    /// </summary>
    [Test]
    public async Task EndThatTimesOutLeavesTheCurrentRowTest()
    {
        using BunitContext ctx = NewContext();
        UnknownCountModel model = new();
        List<GridTimeoutException> seen = [];
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 2).Add(g => g.GotoLastKnown, false)
            .Add(g => g.OnTimeout, (GridTimeoutException e) => { seen.Add(e); return true; }));
        int before = model.GetCurrentRow();

        cut.Instance._spinnerHelper = new SpinnerHelper(10, 20, 1);
        model.FetchMs = 5_000;
        var end = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == "End");
        await cut.InvokeAsync(() => end.Instance.ProcessAsync("End", null, null));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(before));
            Assert.That(seen, Has.Count.EqualTo(1), "the render after it reads nothing past the rows read");
        });
    }

    /// <summary>
    /// End that spends the budget, with no host to handle the timeout, reaches the renderer as a DOM event's exception does:
    /// nothing may render the model the abandoned fetch is still writing.
    /// </summary>
    [Test]
    public async Task EndThatTimesOutUnhandledReachesTheRendererTest()
    {
        using BunitContext ctx = NewContext();
        UnknownCountModel model = new();
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 2).Add(g => g.GotoLastKnown, false));

        cut.Instance._spinnerHelper = new SpinnerHelper(10, 20, 1);
        model.FetchMs = 5_000;
        var end = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == "End");
        await cut.InvokeAsync(() => end.Instance.ProcessAsync("End", null, null));
        Exception ex = await ctx.Renderer.UnhandledException.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(ex, Is.TypeOf<GridTimeoutException>());
    }

    /// <summary>A paged model that breaks the fetch contract: every fetch comes back short, and the count stays unknown.</summary>
    private sealed class ShortFetchModel() : DataGridModelWrapper<Product>(ModelOf(10))
    {
        public int Fetches;

        public override bool RequiresFetch() => true;
        public override int GetRowCount() => int.MaxValue;
        public override ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
        {
            if (count > 0)
                Fetches++;
            return ValueTask.FromResult(count / 2);
        }
    }

    /// <summary>
    /// A paged model whose count is still unknown after a short fetch gets one more fetch of the page, and the grid throws when
    /// that one comes back short too, instead of fetching for ever or rendering rows the model does not have.
    /// </summary>
    [Test]
    public void SecondShortFetchViolatesTheContractTest()
    {
        using BunitContext ctx = NewContext();
        ShortFetchModel model = new();
        Assert.Multiple(() =>
        {
            Assert.That(() => RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 4)),
                Throws.InvalidOperationException.With.Message.EqualTo("The model fetched fewer rows than it counts, a second time"));
            Assert.That(model.Fetches, Is.EqualTo(2), "the page is fetched again once");
        });
    }

    /// <summary>A paged model whose first query runs on a getter without a token, or on a fetch that can cancel it.</summary>
    private sealed class ColdModel() : DataGridModelWrapper<Product>(ModelOf(3))
    {
        public int SyncMs;
        private bool _synced;

        public void Cool() => _synced = false;

        public override bool RequiresFetch() => true;
        public override int GetCurrentRow()
        {
            Sync(default);
            return base.GetCurrentRow();
        }
        public override async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
        {
            Sync(ct);
            return await base.FetchRowsAsync(rowIndex, count, ct);
        }

        private void Sync(CancellationToken ct)
        {
            if (!_synced)
                Task.Delay(SyncMs, ct).GetAwaiter().GetResult();
            _synced = true;
        }
    }

    /// <summary>A paged model that counts more rows than it has read, as one does while a fetch is still to come.</summary>
    private sealed class PartlyReadModel : DataGridModel<Product>, IAsyncGridModel<Product>
    {
        public PartlyReadModel() : base(Rows(3))
        {
            AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
            AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        }

        public override int GetRowCount() => 6;
        public override Product GetItem(int rowIndex) =>
            rowIndex < base.GetRowCount() ? base.GetItem(rowIndex) : throw new ArgumentOutOfRangeException(nameof(rowIndex));
    }

    /// <summary>
    /// A row a paged model has not read yet renders a placeholder, in the wrapper grids too, whose model hides the paged one.
    /// </summary>
    [Test]
    public void RowNotReadYetRendersAPlaceholderInTheWrapperGridsTest()
    {
        using BunitContext ctx = NewContext();
        var multi = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, new PartlyReadModel()).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()).Add(g => g.PageSize, 6));
        Assert.Multiple(() =>
        {
            Assert.That(multi.Markup, Does.Contain("C02"), "the rows read render");
            Assert.That(multi.FindAll("td").Count(td => td.TextContent == "..."), Is.GreaterThan(0), "the others are placeholders");
        });
    }

    /// <summary>
    /// A fetch that spends the budget reaches the host's <c>OnTimeout</c> with the exception: a host that returns true has
    /// handled it and the render goes on, one that returns false, or none, lets it propagate.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TimeoutReachesTheHostTest(bool handled)
    {
        using BunitContext ctx = NewContext();
        SlowModel model = new();
        List<GridTimeoutException> seen = [];
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.OnTimeout, (GridTimeoutException e) => { seen.Add(e); return handled; }));
        Assert.That(cut.Markup, Does.Contain("C00"), "the first fetch was quick");
        // the first render's stabilizer renders again from its timer: let it finish, or its render times out a second time
        cut.WaitForState(() => cut.Instance.GetStableItem() != null, TimeSpan.FromSeconds(2));
        await cut.InvokeAsync(() => { });

        cut.Instance._spinnerHelper = new SpinnerHelper(10, 20, 1);
        model.FetchMs = 5_000;
        // rendered from inside the dispatcher: Render() does not throw when another thread still holds it and runs the render
        if (handled)
            Assert.DoesNotThrowAsync(() => cut.InvokeAsync(() => cut.Render()));
        else
            Assert.ThrowsAsync<GridTimeoutException>(() => cut.InvokeAsync(() => cut.Render()));
        Assert.Multiple(() =>
        {
            Assert.That(seen, Has.Count.EqualTo(1));
            Assert.That(seen[0].WorkStopped, Is.True, "the fetch took the cancellation");
            Assert.That(cut.Instance.IsIdle(), Is.True);
        });
    }

    /// <summary>
    /// A cold paged model's first query, and the restore that runs with it, take the cancellation when the budget is spent:
    /// they must not run on in the background after the timeout.
    /// </summary>
    [Test]
    public void TimeoutStopsTheFirstQueryOfAColdModelTest()
    {
        using BunitContext ctx = NewContext();
        ColdModel model = new();
        List<GridTimeoutException> seen = [];
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.OnTimeout, (GridTimeoutException e) => { seen.Add(e); return true; }));

        cut.Instance._spinnerHelper = new SpinnerHelper(10, 20, 1);
        model.Cool();
        model.SyncMs = 1_000;   // past the budget and the 500 ms grace, so a query that ignored the token would not have stopped
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(seen, Has.Count.EqualTo(1));
            Assert.That(seen[0].WorkStopped, Is.True, "the first query took the cancellation");
        });
    }

    /// <summary>A paged model sorted on a string column, whose search takes as long as asked.</summary>
    private sealed class SlowSearchModel() : DataGridModelWrapper<Product>(SortedByName())
    {
        public int SearchMs;

        public override bool RequiresFetch() => true;
        public override async ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default)
        {
            if (SearchMs > 0)
                await Task.Delay(SearchMs, ct);
            return await base.SearchOnSortedColumnAsync(value, ct);
        }

        private static DataGridModel<Product> SortedByName()
        {
            DataGridModel<Product> model = ModelOf(3);
            model.SetSortedColumn("Name", true);
            return model;
        }
    }

    /// <summary>
    /// A search typed in the search box, run by the debounce, that spends the budget reaches the renderer when the host does not
    /// handle it, as one from a key does: nothing may render the model the abandoned search is still writing.
    /// </summary>
    [Test]
    public async Task TimeoutInTheDebouncedSearchReachesTheRendererTest()
    {
        using BunitContext ctx = NewContext();
        SlowSearchModel model = new();
        var cut = RenderGrid(ctx, model);
        var typing = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == KeyAction.AnyChars);
        await cut.InvokeAsync(() => typing.Instance.ProcessAsync("N", null, null));   // opens the search box

        cut.Instance._spinnerHelper = new SpinnerHelper(100, 100, 1);
        model.SearchMs = 5_000;
        await cut.InvokeAsync(() => cut.FindComponents<Popup>().Single(p => p.Instance.Class == "searchPopup").Find("input")
            .Input(new ChangeEventArgs { Value = "Name01" }));
        Exception ex = await ctx.Renderer.UnhandledException.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(ex, Is.TypeOf<GridTimeoutException>());
    }

    // ─────────────────────────── paging keys, footer buttons, wheel ───────────────────────────

    private static Task KeyAsync(IRenderedComponent<Grigliaria<Product>> cut, string key)
    {
        KeyAction action = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == key && !k.Instance.CtrlKey).Instance;
        return cut.InvokeAsync(() => action.ProcessAsync(key, null, null));
    }

    private static void AssertCurrentRowShown(IRenderedComponent<Grigliaria<Product>> cut, int pageSize)
    {
        int row = cut.Instance.GetCurrentRow(), first = cut.Instance.GetPageIdx();
        Assert.That(row, Is.InRange(first, first + pageSize - 1), "the page shown holds the current row");
    }

    /// <summary>
    /// PageDown and PageUp move the current row by a page and stop at the first and the last row; Home goes to the first.
    /// </summary>
    [Test]
    public async Task PageKeysMoveAPageAndStopAtTheEndsTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(25);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 10));
        await KeyAsync(cut, "Home");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(0));

        await KeyAsync(cut, "PageDown");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(10));
        await KeyAsync(cut, "PageDown");
        await KeyAsync(cut, "PageDown");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(24), "stops at the last row");
        AssertCurrentRowShown(cut, 10);

        await KeyAsync(cut, "PageUp");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(14));
        AssertCurrentRowShown(cut, 10);
        await KeyAsync(cut, "PageUp");
        await KeyAsync(cut, "PageUp");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(0), "stops at the first row");

        await KeyAsync(cut, "PageDown");
        await KeyAsync(cut, "Home");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(0));
        AssertCurrentRowShown(cut, 10);
    }

    /// <summary>
    /// The footer's navigation buttons move as their keys do, and those that cannot move are disabled.
    /// </summary>
    [Test]
    public async Task NavigationButtonsMoveTheCurrentRowTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(25);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 10).Add(g => g.ShowNavigationButtons, true));
        async Task Click(string button)
        {
            await cut.InvokeAsync(() => cut.Find("button." + button).Click());
        }
        await Click("btnFirstRow");
        Assert.That(cut.Find("button.btnPrevPage").HasAttribute("disabled"), Is.True, "nothing before the first row");

        await Click("btnNextPage");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(10));
        await Click("btnNextRow");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(11));
        await Click("btnLastRow");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(24));
        Assert.That(cut.Find("button.btnNextPage").HasAttribute("disabled"), Is.True, "nothing after the last row");
        await Click("btnPrevPage");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(14));
        await Click("btnPrevRow");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(13));
        await Click("btnFirstRow");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(0));
        AssertCurrentRowShown(cut, 10);
    }

    /// <summary>
    /// The wheel moves the current row by one, down for a positive delta and up for a negative one.
    /// </summary>
    [Test]
    public async Task WheelMovesTheCurrentRowTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(25);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 10));
        await KeyAsync(cut, "Home");
        MouseAction wheel = cut.FindComponents<MouseAction>()
            .Single(m => m.Instance.Type == MouseAction.MouseType.Wheel && !m.Instance.CtrlKey).Instance;

        await cut.InvokeAsync(() => wheel.ProcessAsync(null, null, 0, 0, 1));
        await cut.InvokeAsync(() => wheel.ProcessAsync(null, null, 0, 0, 1));
        Assert.That(model.GetCurrentRow(), Is.EqualTo(2));
        await cut.InvokeAsync(() => wheel.ProcessAsync(null, null, 0, 0, -1));
        Assert.That(model.GetCurrentRow(), Is.EqualTo(1));
    }

    /// <summary>
    /// The touch scroller's callback, which any script on the page can call, scrolls one column however far it is asked to:
    /// a huge move does not break the next render.
    /// </summary>
    [Test]
    public async Task TouchScrollByAHugeMoveRendersTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(5);
        model.AddColumn("Extra", new Column<Product, string>("Extra") { PreferredSize = 10 }, p => p.Code);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 2)
            .Add(g => g.HorzResizer, GridHorzResizers.Fixed(25)).Add(g => g.TouchScrollerEnabled, true));
        TouchScroller scroller = cut.FindComponent<TouchScroller>().Instance;

        await cut.InvokeAsync(() => scroller.ProcessScrollBy(int.MaxValue, 0));
        Assert.That(() => cut.Render(), Throws.Nothing);
    }

    /// <summary>
    /// A column's predefined filter is its own <c>FilterDef</c>, or else the one the grid's factory creates for its type; a
    /// name the model does not have throws.
    /// </summary>
    [Test]
    public void PredefinedFilterIsTheColumnsOwnOrTheFactorysTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        SetDef<string> own = new(true) { AllValues = ["Name00", "Name01"] };
        model.AddColumn("Name", new Column<Product, string>("Name") { FilterDef = own }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price"), p => p.Price);
        var cut = RenderGrid(ctx, model);

        var name = cut.Instance.GetPredefinedFilterDef("Name");
        var price = cut.Instance.GetPredefinedFilterDef("Price");
        Assert.Multiple(() =>
        {
            Assert.That(name.FilterDef, Is.SameAs(own));
            Assert.That(name.Column.GetLabel(), Is.EqualTo("Name"));
            Assert.That(price.FilterDef, Is.InstanceOf<NumberDef<decimal>>());
            Assert.That(() => cut.Instance.GetPredefinedFilterDef("Missing"), Throws.ArgumentException);
        });
    }

    // ─────────────────────────── a paged model of the host's own ───────────────────────────

    private sealed record Order(int Id, string Customer);

    /// <summary>The paged model data-models.md shows, over a source that hands out rows in reads of 100.</summary>
    private sealed class OrdersModel : AGridModel<Order>, IDataGridModel<Order>, IAsyncGridModel<Order>
    {
        private const int ReadSize = 100;
        private readonly Order[] _source;
        private readonly List<Order> _rows = [];
        private bool _end;
        private int _version, _current = -1;
        public int Reads;

        public OrdersModel(int count)
        {
            _source = Enumerable.Range(0, count).Select(i => new Order(i, $"Customer{i:D3}")).ToArray();
            AddColumn("Customer", new Column<Order, string>("Customer") { PreferredSize = 20 }, o => o.Customer);
        }

        public override bool RequiresFetch() => true;

        public override async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
        {
            while (!_end && _rows.Count < rowIndex + count)
            {
                await Task.Yield();
                Order[] read = _source.Skip(_rows.Count).Take(ReadSize).ToArray();
                Reads++;
                _rows.AddRange(read);
                _end = read.Length < ReadSize;
                ++_version;
            }
            return Math.Clamp(_rows.Count - rowIndex, 0, count);
        }

        public override int GetRowCount() => _end ? _rows.Count : int.MaxValue;
        public override int GetCurrentRowCount() => _rows.Count;
        public override IReadOnlyList<Order> GetDataView() => _rows.AsReadOnly();
        public override Order GetItem(int rowIndex) => _rows[rowIndex];
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

    /// <summary>
    /// A paged model the host writes on <c>AGridModel</c> pages in the grid as the EF models do: the first render reads the
    /// first rows only, paging reads on as it goes past them, and End reads to the end, where the row count is found.
    /// </summary>
    [Test]
    public async Task HostPagedModelPagesInTheGridTest()
    {
        using BunitContext ctx = NewContext();
        OrdersModel model = new(250);
        var cut = ctx.Render<Grigliaria<Order>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.PageSize, 20));
        Assert.Multiple(() =>
        {
            Assert.That(model.Reads, Is.EqualTo(1), "the first render reads the first rows only");
            Assert.That(model.GetRowCount(), Is.EqualTo(int.MaxValue), "the count is not known yet");
            Assert.That(cut.Markup, Does.Contain("Customer000"));
            Assert.That(((IDataGridModel<Order>)model).CanDoContainsSearch(), Is.False, "the default, for a model that does not search");
        });

        KeyAction Key(string key) => cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == key && !k.Instance.CtrlKey).Instance;
        for (int step = 0; step < 6; step++)
            await cut.InvokeAsync(() => Key("PageDown").ProcessAsync("PageDown", null, null));
        int row = model.GetCurrentRow();
        Assert.Multiple(() =>
        {
            Assert.That(row, Is.GreaterThan(100), "past the first read");
            Assert.That(model.Reads, Is.EqualTo(2));
            Assert.That(cut.Markup, Does.Contain($"Customer{row:D3}"));
        });

        await cut.InvokeAsync(() => Key("End").ProcessAsync("End", null, null));
        Assert.That(model.GetCurrentRow(), Is.EqualTo(199), "End goes to the last row read");

        cut.Render(ps => ps.Add(g => g.GotoLastKnown, false));
        await cut.InvokeAsync(() => Key("End").ProcessAsync("End", null, null));
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(250), "End reads on and finds the end");
            Assert.That(model.GetCurrentRow(), Is.EqualTo(249));
            Assert.That(cut.Markup, Does.Contain("Customer249"));
        });
    }

    // ─────────────────────────── GridColumn arguments ───────────────────────────

    private static RenderFragment ColumnWith(bool property, bool efProperty) => builder =>
    {
        builder.OpenComponent<GridColumn<Product, string>>(0);
        builder.AddAttribute(1, "Label", "Code");
        if (property)
            builder.AddAttribute(2, "Property", (Func<Product, string>)(p => p.Code));
        if (efProperty)
            builder.AddAttribute(3, "EFProperty", (System.Linq.Expressions.Expression<Func<Product, string>>)(p => p.Code));
        builder.CloseComponent();
    };

    /// <summary>
    /// A column takes <c>Property</c> or <c>EFProperty</c>, one of them: both or neither is an <see cref="ArgumentException"/>
    /// naming the column at the first render.
    /// </summary>
    [TestCase(true, true, "both")]
    [TestCase(false, false, "Neither")]
    public void ColumnWithBothOrNeitherPropertyThrowsTest(bool property, bool efProperty, string said)
    {
        using BunitContext ctx = NewContext();
        var ex = Assert.Throws<ArgumentException>(() => RenderGrid(ctx, new DataGridModel<Product>(Rows(1)), ps => ps.Add(g => g.ChildContent, ColumnWith(property, efProperty))));
        Assert.That(ex!.Message, Does.Contain(said).And.Contain("Code"));
        Assert.DoesNotThrow(() => RenderGrid(ctx, new DataGridModel<Product>(Rows(1)), ps => ps.Add(g => g.ChildContent, ColumnWith(true, false))));
    }

    // ─────────────────────────── Anchor ───────────────────────────

    /// <summary>
    /// An enabled Anchor is a link that raises <c>OnClick</c> without navigating; a disabled one is inert.
    /// </summary>
    [Test]
    public async Task AnchorClicksOnlyWhenEnabledTest()
    {
        using BunitContext ctx = NewContext();
        int clicks = 0;
        var enabled = ctx.Render<Anchor>(ps => ps.Add(a => a.OnClick, () => clicks++).Add(a => a.Title, "go").AddChildContent("Go"));
        var disabled = ctx.Render<Anchor>(ps => ps.Add(a => a.OnClick, () => clicks++).Add(a => a.Disabled, true).AddChildContent("Go"));
        await enabled.Find("a.enabled").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(enabled.Find("a").GetAttribute("title"), Is.EqualTo("go"));
            Assert.That(disabled.Find("a.disabled").HasAttribute("href"), Is.False, "nothing to follow");
            Assert.That(disabled.Find("a").TextContent.Trim(), Is.EqualTo("Go"));
        });
    }

    // ─────────────────────────── layout dialog and size-0 columns ───────────────────────────

    private sealed class DialogGrid : Grigliaria<Product>
    {
        public Task OpenLayoutAsync() => OnLayoutAsync();
        public LayoutDefDlg<Product> Dialog => layoutDefDlg;
    }

    /// <summary>
    /// A column moved down past the last row the layout dialog's list shows stays in view: the list pages to it, as it does
    /// to its current row on a key.
    /// </summary>
    [Test]
    public async Task LayoutDialogKeepsTheMovedColumnInViewTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        string[] names = Enumerable.Range(0, 8).Select(i => $"C{i}").ToArray();
        foreach (string name in names)
            model.AddColumn(name, new Column<Product, string>(name) { PreferredSize = 10 }, p => name);
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true));
        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
        var right = dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>()
            .Single(l => l.Instance.Model.GetRowCount() == names.Length);
        var list = right.FindComponent<Grigliaria<LayoutDefDlg<Product>.ColumnInfo>>();
        await cut.InvokeAsync(() => right.Instance.Model.SetCurrentRow(4));   // the last row of the first page
        list.Render();
        Assert.That(list.Instance.GetPageIdx(), Is.EqualTo(0));

        await right.Find("button.btnMoveRowDown").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow().ElementAt(5), Is.EqualTo("C4"), "moved");
            Assert.That(right.Instance.Model.GetCurrentRow(), Is.EqualTo(5), "the current row follows it");
            Assert.That(list.Instance.GetPageIdx(), Is.EqualTo(1), "the list pages to it");
            Assert.That(list.Markup, Does.Contain(">C4<"), "and shows it");
        });

        // every step, down to the end and back up to the top
        int[] steps = [.. Enumerable.Repeat(1, 2), .. Enumerable.Repeat(-1, 7)];
        foreach (int step in steps)
        {
            await right.Find(step > 0 ? "button.btnMoveRowDown" : "button.btnMoveRowUp").ClickAsync(new());
            int row = right.Instance.Model.GetCurrentRow(), first = list.Instance.GetPageIdx();
            Assert.Multiple(() =>
            {
                Assert.That(cut.Instance.GetColumnsToShow().ElementAt(row), Is.EqualTo("C4"), "the current row is the column moved");
                Assert.That(row, Is.InRange(first, first + 4), "in the page shown");
                Assert.That(list.Markup, Does.Contain(">C4<"));
            });
        }
        Assert.That(right.Instance.Model.GetCurrentRow(), Is.EqualTo(0), "at the top");
    }

    /// <summary>
    /// A column the layout dialog moves stays in the grid's column window: moved past the window's last column the grid scrolls
    /// to it, and moved inside the window the grid does not scroll. The same holds for a column moved in from the hidden list.
    /// With a cell cursor the moved column becomes the grid's current column.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task LayoutDialogKeepsTheMovedColumnInTheGridTest(bool cellCursor)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        string[] names = Enumerable.Range(0, 6).Select(i => $"C{i}").ToArray();
        foreach (string name in names)
            model.AddColumn(name, new Column<Product, string>(name) { PreferredSize = 10 }, p => name);
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.HorzResizer, GridHorzResizers.Fixed(30))
            .Add(g => g.CellCursorEnabled, cellCursor));
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow(names[..5]));
        Assert.That(cut.Instance.GetShownColumns(), Is.EqualTo(new[] { "C0", "C1", "C2" }));
        void AssertShown(IEnumerable<string> shown, string moved, string because) => Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetShownColumns(), Is.EqualTo(shown), because);
            Assert.That(cut.Instance.GetCurrentCol(), Is.EqualTo(cellCursor ? moved : null), "the moved column is current");
        });
        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
        var lists = dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>();
        var right = lists.Single(l => l.Instance.Model.GetRowCount() == 5);
        var left = lists.Single(l => l != right);

        await cut.InvokeAsync(() => right.Instance.Model.SetCurrentRow(0));
        await right.Find("button.btnMoveRowDown").ClickAsync(new());
        AssertShown(["C1", "C0", "C2"], "C0", "moved inside the window: no scroll");

        await cut.InvokeAsync(() => right.Instance.Model.SetCurrentRow(2));
        await right.Find("button.btnMoveRowDown").ClickAsync(new());
        AssertShown(["C0", "C3", "C2"], "C2", "moved past the window: the grid scrolls to it");

        await right.Find("button.btnMoveRowUp").ClickAsync(new());
        AssertShown(["C0", "C2", "C3"], "C2", "moved inside a scrolled window: no scroll");

        await cut.InvokeAsync(() => right.Instance.Model.SetCurrentRow(4));
        await cut.InvokeAsync(() => left.Instance.SelectionModel.Select(left.Instance.Model.GetItem(0), true));
        dialog.Render();
        await dialog.Find("button.moveFromLeft").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetShownColumns(), Does.Contain("C5"), "moved in from the hidden list");
            Assert.That(cut.Instance.GetCurrentCol(), Is.EqualTo(cellCursor ? "C5" : null), "the moved column is current");
        });
    }

    /// <summary>
    /// A grid without a header, so without the layout button, opens its layout dialog from code.
    /// </summary>
    [Test]
    public async Task HeaderlessGridOpensItsLayoutDialogTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.ShowHeader, false));
        var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
        Assert.That(dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>(), Is.Empty, "closed");

        await cut.InvokeAsync(() => cut.Instance.ShowLayoutDialogAsync());
        Assert.That(dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>(), Is.Not.Empty, "open");
    }

    /// <summary>A wrapper grid without a header opens its layout dialog from code too.</summary>
    [Test]
    public async Task HeaderlessMultiSelGridOpensItsLayoutDialogTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()).Add(g => g.Layoutable, true).Add(g => g.ShowHeader, false));
        var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
        Assert.That(dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>(), Is.Empty, "closed");

        await cut.InvokeAsync(() => cut.Instance.ShowLayoutDialogAsync());
        Assert.That(dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>(), Is.Not.Empty, "open");
    }

    /// <summary>
    /// A shown column of <c>PreferredSize</c> 0 is in neither list of the layout dialog, and a move or a reorder made there keeps it
    /// shown where it was: after the column it followed, or last when that column was hidden.
    /// </summary>
    [Test]
    public async Task LayoutDialogKeepsAShownSizelessColumnTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Marker", new Column<Product, string>("Marker") { PreferredSize = 0 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true));
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Code", "Name", "Marker"));
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Marker" }));

        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
        var lists = dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>();
        Assert.That(lists.SelectMany(l => Enumerable.Range(0, l.Instance.Model.GetRowCount()).Select(i => l.Instance.Model.GetItem(i).ColumnName)),
            Does.Not.Contain("Marker"), "in neither list");

        await dialog.Find("button.moveAllFromLeft").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EquivalentTo(new[] { "Code", "Marker", "Name", "Price" }), "still shown after a move");
            Assert.That(cut.Instance.GetColumnsToShow().SkipWhile(c => c != "Name").Skip(1).First(), Is.EqualTo("Marker"), "after the column it followed");
        });

        var right = lists.Single(l => Enumerable.Range(0, l.Instance.Model.GetRowCount()).Any(i => l.Instance.Model.GetItem(i).ColumnName == "Name"));
        var name = Enumerable.Range(0, right.Instance.Model.GetRowCount()).Select(i => right.Instance.Model.GetItem(i)).Single(ci => ci.ColumnName == "Name");
        await cut.InvokeAsync(() => right.Instance.SelectionModel.Select(name, true));
        dialog.Render();
        await dialog.Find("button.moveFromRight").ClickAsync(new());
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Price", "Marker" }), "its anchor hidden, it goes last");

        await cut.InvokeAsync(() => cut.Instance.Dialog.SubmitAsync());
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Price", "Marker" }), "OK keeps it");
    }

    /// <summary>
    /// The grid reads an empty list of columns to show as its defaults, so with no fixed or size-0 column the layout dialog does
    /// not hide the last shown column: it would leave its list empty with the default columns shown behind it.
    /// </summary>
    [Test]
    public async Task LayoutDialogDoesNotHideTheLastColumnTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10, ShownByDefault = false }, p => p.Price);
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true));
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Name", "Price"));

        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
        Assert.That(dialog.Find("button.moveAllFromRight").HasAttribute("disabled"), Is.True, "hide all");

        var right = dialog.FindComponents<MultiSelGrid<LayoutDefDlg<Product>.ColumnInfo>>()
            .Single(l => Enumerable.Range(0, l.Instance.Model.GetRowCount()).Any(i => l.Instance.Model.GetItem(i).ColumnName == "Name"));
        LayoutDefDlg<Product>.ColumnInfo Shown(string name) =>
            Enumerable.Range(0, right.Instance.Model.GetRowCount()).Select(i => right.Instance.Model.GetItem(i)).Single(ci => ci.ColumnName == name);
        await cut.InvokeAsync(() => right.Instance.SelectionModel.Select(Shown("Name"), true));
        dialog.Render();
        await dialog.Find("button.moveFromRight").ClickAsync(new());
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Price" }), "one of two hidden");

        await cut.InvokeAsync(() => right.Instance.SelectionModel.Select(Shown("Price"), true));
        dialog.Render();
        Assert.Multiple(() =>
        {
            Assert.That(dialog.Find("button.moveFromRight").HasAttribute("disabled"), Is.True, "hide the last one");
            Assert.That(dialog.Find("button.moveAllFromRight").HasAttribute("disabled"), Is.True, "hide all");
        });
    }

    /// <summary>
    /// A memento written by another tool may leave its collections out; the deserializer binds them to null without complaint,
    /// and the restore reads that as no filters and no sort rather than failing inside the grid.
    /// </summary>
    [Test]
    public async Task MementoWithoutItsCollectionsRestoresTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model);
        await cut.InvokeAsync(() => model.SetSortedColumn("Name", true));
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        Assert.That(model.GetFilters(), Is.Not.Empty, "something for the restore to clear");
        Memento bare = System.Text.Json.JsonSerializer.Deserialize<Memento>("{\"Layout\":null}")!;
        Assert.That(bare.FiltersDefs, Is.Null, "what the deserializer hands over");
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(bare));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetSortedColumn(), Is.Null, "no sort in the memento");
            Assert.That(model.GetFilters(), Is.Empty, "no filters in the memento");
            Assert.That(cut.Instance.CreateMemento().SorterColumns, Is.Empty);
        });
    }

    /// <summary>
    /// A layoutable MultiSelGrid saves its layout and sort through CreateRestorePoint, and RestoreMemento puts them back.
    /// </summary>
    [Test]
    public async Task MultiSelGridSavesAndRestoresItsStateTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()).Add(g => g.Layoutable, true));
        Memento saved = cut.Instance.CreateMemento();
        Memento changed = saved with { Layout = saved.Layout!.Clone(c => c != "Name"), SorterColumns = [new("Price", false)] };
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(changed));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Does.Not.Contain("Name"));
            Assert.That(model.GetItem(0).Code, Is.EqualTo("C02"), "sorted by price, descending");
            Assert.That(cut.Instance.CreateMemento(), Is.EqualTo(changed), "what was restored is saved again");
        });
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(saved));
        Assert.That(cut.Instance.GetColumnsToShow(), Does.Contain("Name"));
    }

    /// <summary>
    /// An ordered MultiSelGrid has no filters and no sort: it restores its own memento and a reset, and a memento that carries a
    /// filter or a sort, saved over a filterable model, restores its layout without them.
    /// </summary>
    [Test]
    public async Task OrderedMultiSelGridRestoresAMementoTest()
    {
        using BunitContext ctx = NewContext();
        GridModel<Product> ordered = new(Rows(3));
        ordered.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        ordered.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        ordered.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, ordered).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()).Add(g => g.Layoutable, true));
        Memento saved = cut.Instance.CreateMemento();
        Memento foreign = saved with
        {
            Layout = saved.Layout!.Clone(c => c != "Name"),
            FiltersDefs = new Dictionary<string, AFilterDef> { ["Price"] = new NumberDef<decimal> { From = 1 } },
            SorterColumns = [new("Price", false)],
        };
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrowAsync(async () => await cut.InvokeAsync(() => cut.Instance.RestoreMemento(saved)), "its own memento");
            Assert.DoesNotThrowAsync(async () => await cut.InvokeAsync(() => cut.Instance.RestoreMemento(null)), "a reset");
            Assert.DoesNotThrowAsync(async () => await cut.InvokeAsync(() => cut.Instance.RestoreMemento(foreign)), "one with a filter and a sort");
        });
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Does.Not.Contain("Name"), "the layout applies");
            Assert.That(Enumerable.Range(0, ordered.GetRowCount()).Select(i => ordered.GetItem(i).Code), Is.EqualTo(new[] { "C00", "C01", "C02" }), "the rows keep their order");
        });
    }

    /// <summary>
    /// A restore while an edit is pending throws before it changes anything: the layout, the filters and the sort stay as they
    /// were.
    /// </summary>
    [Test]
    public async Task RestoreDuringAnEditChangesNothingTest()
    {
        using BunitContext ctx = NewContext();
        Product[] rows = Rows(3);
        EditableDataGridModel<Product> model = new(() => ValueTask.FromResult<IList<Product>>(rows.ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(rows.ToList());
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.Layoutable, true));
        Memento before = cut.Instance.CreateMemento();
        Memento other = before with
        {
            Layout = before.Layout!.Clone(c => c != "Name"),
            FiltersDefs = new Dictionary<string, AFilterDef> { ["Price"] = new NumberDef<decimal> { From = 1 } },
            SorterColumns = [new("Price", false)],
        };
        await cut.InvokeAsync(() => model.EditAsync().AsTask());

        Assert.That(async () => await cut.InvokeAsync(() => cut.Instance.RestoreMemento(other)), Throws.InvalidOperationException);
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.CreateMemento(), Is.EqualTo(before));
            Assert.That(cut.Instance.GetColumnsToShow(), Does.Contain("Name"), "the layout");
            Assert.That(model.IsFiltered(), Is.False, "the filters");
            Assert.That(model.GetSortedColumn(), Is.Null, "the sort");
        });
    }
}
