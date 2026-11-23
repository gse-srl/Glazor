using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The filter dialog shows its "Include null" box only for a column whose value can be null: a Nullable&lt;T&gt;, or a
/// reference type whose expression the nullable annotations mark non-null. A Func accessor has no expression, so the box stays.
/// </summary>
public class IncludeNullTests
{
    private sealed record Item(int Count, int? Qty, string Name, string? Note, Item? Parent, bool Flag, Status State, Status? MaybeState);

    private enum Status { On, Off }

    private static DataGridModel<Item> ModelOf()
    {
        Item parent = new(1, null, "P", null, null, true, Status.On, null);
        DataGridModel<Item> model = new([new(2, 3, "A", "n", parent, false, Status.Off, Status.On)]);
        model.AddColumn("Count", new Column<Item, int>("Count") { PreferredSize = 5 }, i => i.Count);
        model.AddColumn("Qty", new Column<Item, int?>("Qty") { PreferredSize = 5 }, i => i.Qty);
        model.AddEFColumn("Name", new Column<Item, string>("Name") { PreferredSize = 5 }, i => i.Name);
        model.AddEFColumn("Note", new Column<Item, string?>("Note") { PreferredSize = 5 }, i => i.Note);
        model.AddEFColumn("ParentName", new Column<Item, string?>("Parent") { PreferredSize = 5 }, i => i.Parent!.Name);
        model.AddColumn("Len", new Column<Item, int>("Len") { PreferredSize = 5 }, i => i.Name.Length);
        model.AddEFColumn("Upper", new Column<Item, string>("Upper") { PreferredSize = 5 }, i => i.Name.ToUpperInvariant());
        model.AddColumn("Func", new Column<Item, string>("Func") { PreferredSize = 5 }, i => i.Name);   // a Func: no expression to read
        model.AddColumn("Flag", new Column<Item, bool>("Flag") { PreferredSize = 5 }, i => i.Flag);
        model.AddColumn("State", new Column<Item, Status>("State") { PreferredSize = 5 }, i => i.State);
        model.AddColumn("MaybeState", new Column<Item, Status?>("Maybe") { PreferredSize = 5 }, i => i.MaybeState);
        return model;
    }

    [TestCase("Count", false, TestName = "IncludeNull_int_hidden")]
    [TestCase("Qty", true, TestName = "IncludeNull_nullable_int_shown")]
    [TestCase("Name", false, TestName = "IncludeNull_string_hidden")]
    [TestCase("Note", true, TestName = "IncludeNull_nullable_string_shown")]
    [TestCase("ParentName", true, TestName = "IncludeNull_path_through_nullable_navigation_shown")]
    [TestCase("Len", false, TestName = "IncludeNull_computed_int_hidden")]
    [TestCase("Upper", true, TestName = "IncludeNull_computed_string_shown")]
    [TestCase("Func", true, TestName = "IncludeNull_func_getter_shown")]
    [TestCase("Flag", false, TestName = "IncludeNull_bool_hidden")]
    [TestCase("State", false, TestName = "IncludeNull_enum_hidden")]
    [TestCase("MaybeState", true, TestName = "IncludeNull_nullable_enum_shown")]
    public async Task IncludeNullBoxFollowsTheColumnsNullabilityTest(string column, bool shown)
    {
        using BunitContext ctx = NewContext();
        var model = ModelOf();
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync(column, null));
        var dialog = cut.FindComponent<FilterDefDlg>();
        Assert.That(dialog.FindAll("input[id$=_include]"), Has.Count.EqualTo(shown ? 1 : 0));
    }

    private sealed record Node(string Name, int Level, int? Qty) : ITreeNode
    {
        public bool IsExpanded() => false;
        public int GetLevel() => Level;
        public bool IsLeaf() => true;
        public bool IsSubTotal() => false;
        public bool HasChildren() => false;
    }

    /// <summary>The tree grid's wrapper puts its expand column first: the info has to shift with the other column accessors.</summary>
    [Test]
    public async Task TreegliariaShiftsTheColumnInfoTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = new([new("root", 0, null), new("leaf", 1, 2)]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Node, string>("Name") { PreferredSize = 20 }, n => n.Name);
        tree.AddColumn("Level", new Column<Node, int>("Level") { PreferredSize = 10 }, n => n.Level);
        tree.AddColumn("Qty", new Column<Node, int?>("Qty") { PreferredSize = 10 }, n => n.Qty);
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        var grid = cut.FindComponent<Grigliaria<Node>>();
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Level", null));   // through the wrapper, as a host does
        Assert.That(grid.FindComponent<FilterDefDlg>().FindAll("input[id$=_include]"), Is.Empty, "int, read through the shifted index");
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Qty", null));
        Assert.That(grid.FindComponent<FilterDefDlg>().FindAll("input[id$=_include]"), Has.Count.EqualTo(1), "the last column, one past the inner list without the shift");
    }

    private sealed class PlainWrapper(IDataGridModel<Item> wrapped) : DataGridModelWrapper<Item>(wrapped);

    /// <summary>A wrapper that remaps nothing forwards the info as it is.</summary>
    [Test]
    public async Task WrapperForwardsTheColumnInfoTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, new PlainWrapper(ModelOf())).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Count", null));
        Assert.That(cut.FindComponent<FilterDefDlg>().FindAll("input[id$=_include]"), Is.Empty);
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Qty", null));
        Assert.That(cut.FindComponent<FilterDefDlg>().FindAll("input[id$=_include]"), Has.Count.EqualTo(1));
    }

    private sealed record Owner(string Name, string? Note);

    private sealed record Box(Owner Owner, Owner? Maybe)
    {
        public static string StaticName => "s";
        public static string? StaticNote => null;
    }

    /// <summary>The whole member chain is read: a non-null path hides the box, a nullable member anywhere on it keeps it; a static member is read too.</summary>
    [TestCase("OwnerName", false, TestName = "IncludeNull_non_null_chain_hidden")]
    [TestCase("OwnerNote", true, TestName = "IncludeNull_nullable_leaf_shown")]
    [TestCase("MaybeName", true, TestName = "IncludeNull_nullable_navigation_shown")]
    [TestCase("StaticName", false, TestName = "IncludeNull_static_non_null_hidden")]
    [TestCase("StaticNote", true, TestName = "IncludeNull_static_nullable_shown")]
    public async Task IncludeNullBoxReadsTheWholeChainTest(string column, bool shown)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Box> model = new([new(new("o", null), null)]);
        model.AddEFColumn("OwnerName", new Column<Box, string>("OwnerName") { PreferredSize = 5 }, b => b.Owner.Name);
        model.AddEFColumn("OwnerNote", new Column<Box, string?>("OwnerNote") { PreferredSize = 5 }, b => b.Owner.Note);
        model.AddEFColumn("MaybeName", new Column<Box, string?>("MaybeName") { PreferredSize = 5 }, b => b.Maybe!.Name);
        model.AddEFColumn("StaticName", new Column<Box, string>("StaticName") { PreferredSize = 5 }, b => Box.StaticName);
        model.AddEFColumn("StaticNote", new Column<Box, string?>("StaticNote") { PreferredSize = 5 }, b => Box.StaticNote);
        var cut = ctx.Render<Grigliaria<Box>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync(column, null));
        Assert.That(cut.FindComponent<FilterDefDlg>().FindAll("input[id$=_include]"), Has.Count.EqualTo(shown ? 1 : 0));
    }

    /// <summary>The wrapper of MultiSelGrid puts its selection column first and forwards the rest.</summary>
    [Test]
    public async Task MultiSelGridForwardsTheNullabilityTest()
    {
        using BunitContext ctx = NewContext();
        var model = ModelOf();
        var cut = ctx.Render<MultiSelGrid<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.SelectionModel, new SetSelectionModel<Item>()));
        var grid = cut.FindComponent<Grigliaria<Item>>();
        await cut.InvokeAsync(() => grid.Instance.ShowFilterDialogAsync("Count", null));
        Assert.That(grid.FindComponent<FilterDefDlg>().FindAll("input[id$=_include]"), Is.Empty);
        await cut.InvokeAsync(() => grid.Instance.ShowFilterDialogAsync("Qty", null));
        Assert.That(grid.FindComponent<FilterDefDlg>().FindAll("input[id$=_include]"), Has.Count.EqualTo(1));
    }
}
