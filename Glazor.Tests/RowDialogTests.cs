using Bunit;
using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Glazor.Tests.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Glazor.Tests;

/// <summary>
/// The row dialog, which RowDialogEnabled turns on: the current row as a list of its columns, those of the layout first and
/// the hidden ones after a separator, each value drawn as the grid draws it, with Previous and Next moving the grid's
/// current row. A column is listed unless it is fixed or has no label, or as its ShownInRowDialog says.
/// </summary>
public class RowDialogTests
{
    private sealed record Product(string Code, string Name, decimal Price, bool Active, string Note);

    private static Product[] Rows(int count) =>
        Enumerable.Range(0, count).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m, i % 2 == 0, $"Note{i:D2}")).ToArray();

    private static DataGridModel<Product> ModelOf(int rowCount, bool? codeShown = null, bool? noteShown = null)
    {
        DataGridModel<Product> model = new(Rows(rowCount));
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10, ShownInRowDialog = codeShown }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name")
        {
            PreferredSize = 10,
            Renderer = ctx => builder =>
            {
                builder.OpenElement(0, "b");
                builder.AddAttribute(1, "class", "nm");
                builder.AddContent(2, ctx.Value + "#" + ctx.RowIdx);
                builder.CloseElement();
            },
        }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10, CellClassFn = ctx => "price" }, p => p.Price);
        model.AddColumn("Active", new Column<Product, bool>("Active") { PreferredSize = 10 }, p => p.Active);
        model.AddColumn("Note", new Column<Product, string>("Note") { PreferredSize = 10, ShownInRowDialog = noteShown }, p => p.Note);
        model.AddColumn("Tool", new Column<Product, string>("") { PreferredSize = 4 }, p => p.Code);
        model.AddColumn("Key", new Column<Product, string>("Key") { PreferredSize = 0 }, p => p.Code);
        return model;
    }

    private static IRenderedComponent<Grigliaria<Product>> RenderGrid(BunitContext ctx, IDataGridModel<Product> model, bool enabled,
        params string[] toShow)
    {
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.RowDialogEnabled, enabled));
        if (toShow.Length > 0)
        {
            cut.InvokeAsync(() => cut.Instance.SetColumnsToShow(toShow));
            cut.Render();
        }
        return cut;
    }

    private sealed record Node(string Name, bool Children) : ITreeNode
    {
        public bool IsExpanded() => false;
        public int GetLevel() => 0;
        public bool IsLeaf() => !Children;
        public bool IsSubTotal() => false;
        public bool HasChildren() => Children;
    }

    private static TreeGridModel<Node> TreeOf(bool? nameShown = null)
    {
        TreeGridModel<Node> tree = new([new("root", true), new("leaf", false)]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Node, string>("Name") { PreferredSize = 10, ShownInRowDialog = nameShown }, n => n.Name);
        tree.AddColumn("Kids", new Column<Node, bool>("Kids") { PreferredSize = 10 }, n => n.Children);
        return tree;
    }

    // A column the host declares with the name of the wrapper grid's own one
    private static RenderFragment OwnColumn<T>(string name, string label, bool? shown) => builder =>
    {
        builder.OpenComponent<GridColumn<T, bool>>(0);
        builder.AddAttribute(1, "Name", name);
        builder.AddAttribute(2, "Label", label);
        builder.AddAttribute(3, "Property", (Func<T, bool>)(_ => false));
        if (shown != null)
            builder.AddAttribute(4, "ShownInRowDialog", shown);
        builder.CloseComponent();
    };

    private static string[] Labels<T>(IRenderedComponent<RowViewDlg<T>> dialog) =>
        dialog.FindComponent<Grigliaria<RowViewDlg<T>.Field>>().Instance.Model.GetDataView().Select(f => f.Label).ToArray();

    [Test]
    public async Task ListsTheLayoutThenTheHiddenColumnsTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), true, "Code", "Price", "Name");
        var dialog = cut.FindComponent<RowViewDlg<Product>>();
        Assert.That(dialog.FindComponents<Grigliaria<RowViewDlg<Product>.Field>>(), Is.Empty, "closed");

        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        Assert.Multiple(() =>
        {
            Assert.That(Labels(dialog), Is.EqualTo(new[] { "Price", "Name", "Hidden columns", "Active", "Note" }),
                "without the fixed column, the one with no label and the sizeless one");
            Assert.That(dialog.Find("h3.title").TextContent, Is.EqualTo("Row 1/3"));
            Assert.That(dialog.Find("b.nm").TextContent, Is.EqualTo("Name00#0"), "the column's renderer, with the grid's row");
            Assert.That(dialog.FindAll("td.price"), Has.Count.EqualTo(1), "the column's cell class");
            Assert.That(dialog.FindAll("td.separator"), Has.Count.EqualTo(2), "the separator's two cells");
            Assert.That(dialog.FindAll("input[type=checkbox]"), Has.Count.EqualTo(1), "a bool as the grid draws it");
            Assert.That(dialog.Markup, Does.Contain("Note00"));
        });
    }

    /// <summary>The dialog's lines are as tall as the grid's rows, or 40 px when the grid sizes its rows by content.</summary>
    [TestCase(32, 32)]
    [TestCase(null, 40)]
    public async Task LinesAreAsTallAsTheGridsRowsTest(int? rowHeight, int expected)
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(1)).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.RowDialogEnabled, true).Add(g => g.RowHeight, rowHeight));
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        var list = cut.FindComponent<RowViewDlg<Product>>().FindComponent<Grigliaria<RowViewDlg<Product>.Field>>();
        Assert.That(list.Instance.RowHeight, Is.EqualTo(expected));
    }

    /// <summary>A column's ShownInRowDialog wins over the default: a fixed column comes in, another goes out.</summary>
    /// <summary>
    /// Alt+Enter opens the row dialog, Windows' Properties key, under the footer button's own conditions: with every column the
    /// dialog lists in view it is off, and the key is left to the browser.
    /// </summary>
    [Test]
    public async Task AltEnterOpensTheRowDialogTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), true, "Code", "Price", "Name");
        IRenderedComponent<KeyAction> AltEnter() => cut.FindComponents<KeyAction>().Single(k => k.Instance.AltKey && k.Instance.Key == "Enter");
        Assert.That(AltEnter().Instance.Enabled, Is.True, "Active and Note are out of the layout");
        await cut.InvokeAsync(() => AltEnter().Instance.ProcessAsync(null, null, null));
        Assert.That(Labels(cut.FindComponent<RowViewDlg<Product>>()), Is.EqualTo(new[] { "Price", "Name", "Hidden columns", "Active", "Note" }));

        using BunitContext all = NewContext();
        var inView = RenderGrid(all, ModelOf(3), true, "Code", "Name", "Price", "Active", "Note");
        Assert.That(inView.FindComponents<KeyAction>().Single(k => k.Instance.AltKey && k.Instance.Key == "Enter").Instance.Enabled, Is.False,
            "every column it lists in view");
    }

    [Test]
    public async Task TheColumnFlagWinsTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(1, codeShown: true, noteShown: false), true);
        var dialog = cut.FindComponent<RowViewDlg<Product>>();
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        Assert.That(Labels(dialog), Is.EqualTo(new[] { "Code", "Name", "Price", "Active" }), "and no separator: no listed column is hidden");
    }

    [Test]
    public async Task PreviousAndNextMoveTheGridTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model, true);
        var dialog = cut.FindComponent<RowViewDlg<Product>>();
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        Assert.That(dialog.FindAll("button.move")[0].HasAttribute("disabled"), Is.True, "no row before the first");

        await dialog.FindAll("button.move")[1].ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(1));
            Assert.That(dialog.Find("h3.title").TextContent, Is.EqualTo("Row 2/3"));
            Assert.That(dialog.Find("b.nm").TextContent, Is.EqualTo("Name01#1"));
        });
        await dialog.FindAll("button.move")[1].ClickAsync(new());
        Assert.That(dialog.FindAll("button.move")[1].HasAttribute("disabled"), Is.True, "no row after the last");
        await dialog.FindAll("button.move")[0].ClickAsync(new());
        Assert.That(dialog.Find("b.nm").TextContent, Is.EqualTo("Name01#1"));
    }

    /// <summary>
    /// Ctrl+Left and Ctrl+Right move the row as the two buttons do. The dialog's list, a grid that does not scroll its columns,
    /// leaves the two keys alone, so they reach the dialog from inside it too.
    /// </summary>
    [Test]
    public async Task CtrlArrowsMoveTheGridTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model, true);
        var dialog = cut.FindComponent<RowViewDlg<Product>>();
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        var list = dialog.FindComponent<Grigliaria<RowViewDlg<Product>.Field>>();
        KeyAction Key(string key) => dialog.FindComponents<KeyAction>().Select(k => k.Instance)
            .Single(k => k.CtrlKey && !k.ShiftKey && k.Key == key && k.BindAncestorWithTag == "dialog");

        await cut.InvokeAsync(() => Key("ArrowRight").ProcessAsync(null, null, null));
        await cut.InvokeAsync(() => Key("ArrowRight").ProcessAsync(null, null, null));
        Assert.That(dialog.Find("h3.title").TextContent, Is.EqualTo("Row 3/3"));
        await cut.InvokeAsync(() => Key("ArrowLeft").ProcessAsync(null, null, null));
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(1));
            Assert.That(dialog.Find("b.nm").TextContent, Is.EqualTo("Name01#1"));
            Assert.That(list.FindComponents<KeyAction>().Select(k => k.Instance)
                .Where(k => k.CtrlKey && !k.ShiftKey && k.Key is "ArrowLeft" or "ArrowRight" && k.BindAncestorWithTag == "div")
                .Select(k => k.Enabled), Is.EqualTo(new[] { false, false }), "the list's own column keys are off");
        });
    }

    [Test]
    public void TheButtonIsEnabledOnlyWithColumnsOutOfViewTest()
    {
        using BunitContext ctx = NewContext();
        var all = RenderGrid(ctx, ModelOf(2), true);
        Assert.That(all.Find("button.btnShowRow").HasAttribute("disabled"), Is.True, "every column is on screen");

        var hidden = RenderGrid(ctx, ModelOf(2), true, "Code", "Name");
        Assert.That(hidden.Find("button.btnShowRow").HasAttribute("disabled"), Is.False, "a column out of the layout");

        var scrolled = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(2)).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.RowDialogEnabled, true).Add(g => g.HorzResizer, GridHorzResizers.Fixed(25)));
        Assert.That(scrolled.Find("button.btnShowRow").HasAttribute("disabled"), Is.False, "a column out of the window");

        var off = RenderGrid(ctx, ModelOf(2), false, "Code", "Name");
        Assert.That(off.FindAll("button.btnShowRow"), Is.Empty, "no button without RowDialogEnabled");
        Assert.That(off.FindComponents<RowViewDlg<Product>>(), Is.Empty, "and no dialog");

        var empty = RenderGrid(ctx, ModelOf(0), true, "Code", "Name");
        Assert.That(empty.Find("button.btnShowRow").HasAttribute("disabled"), Is.True, "no current row");
    }

    /// <summary>A hidden column the dialog does not list leaves the button disabled.</summary>
    [Test]
    public void TheButtonIgnoresColumnsNotListedTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(2, noteShown: false), true, "Code", "Name", "Price", "Active");
        Assert.That(cut.Find("button.btnShowRow").HasAttribute("disabled"), Is.True, "Note says no, Tool has no label");
    }

    /// <summary>Closed and opened again on another row, the dialog shows that row, with one list.</summary>
    [Test]
    public async Task ReopensOnAnotherRowTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), true);
        var dialog = cut.FindComponent<RowViewDlg<Product>>();
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        await dialog.InvokeAsync(() => dialog.Instance.CloseAsync());
        await dialog.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);   // the browser's close event
        await cut.InvokeAsync(() => cut.Instance.SetCurrentRow(2));
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        Assert.Multiple(() =>
        {
            Assert.That(dialog.Find("h3.title").TextContent, Is.EqualTo("Row 3/3"));
            Assert.That(dialog.Find("b.nm").TextContent, Is.EqualTo("Name02#2"));
            Assert.That(dialog.FindComponents<Grigliaria<RowViewDlg<Product>.Field>>(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task WrapperGridsLeaveTheirOwnColumnOutTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, ModelOf(2))
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>(new HashSet<Product>(), defensiveCopy: false))
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.RowDialogEnabled, true));
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        var dialog = cut.FindComponent<RowViewDlg<Product>>();
        Assert.That(Labels(dialog), Is.EqualTo(new[] { "Name", "Price", "Active", "Note" }));
    }

    /// <summary>
    /// A selection column the host declares, with a label: the grid fixes it after it is declared, so it stays out unless it
    /// says otherwise.
    /// </summary>
    [TestCase(null)]
    [TestCase(true)]
    public async Task DeclaredSelectionColumnTest(bool? shown)
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, ModelOf(2))
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>(new HashSet<Product>(), defensiveCopy: false))
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.RowDialogEnabled, true)
            .Add(g => g.ChildContent, OwnColumn<Product>(MultiSelGrid.SelColName, "Pick", shown)));
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        Assert.That(Labels(cut.FindComponent<RowViewDlg<Product>>()),
            Is.EqualTo(shown == true ? new[] { "Pick", "Name", "Price", "Active", "Note" } : new[] { "Name", "Price", "Active", "Note" }));
    }

    /// <summary>A tree's expansion column, declared by the host with a label, stays out as the selection column does.</summary>
    [TestCase(null)]
    [TestCase(true)]
    public async Task DeclaredExpansionColumnTest(bool? shown)
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, TreeOf()).Add(g => g.RowDialogEnabled, true)
            .Add(g => g.ChildContent, OwnColumn<Node>(Treegliaria.ExpColName, "Open", shown)));
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        Assert.That(Labels(cut.FindComponent<RowViewDlg<Node>>()),
            Is.EqualTo(shown == true ? new[] { "Open", "Name", "Kids" } : new[] { "Name", "Kids" }));
    }

    /// <summary>The column that draws the tree is listed, though the tree fixes it, unless the column says otherwise.</summary>
    [TestCase(null)]
    [TestCase(false)]
    public async Task TreeColumnIsListedTest(bool? shown)
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, TreeOf(shown)).Add(g => g.TreeColumn, "Name")
            .Add(g => g.RowDialogEnabled, true));
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        Assert.That(Labels(cut.FindComponent<RowViewDlg<Node>>()), Is.EqualTo(shown == false ? new[] { "Kids" } : new[] { "Name", "Kids" }));
    }

    /// <summary>Next past the rows read: the grid fetches the row, and the dialog shows it.</summary>
    [Test]
    public async Task NextOnAPagedModelReadsTheRowTest()
    {
        using SqliteConnection connection = new("Filename=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MGADbContext>().UseSqlite(connection).Options;
        using (MGADbContext dbCtx = new(options))
        {
            dbCtx.Database.EnsureCreated();
            for (int i = 0; i < 300; i++)
            {
                dbCtx.MGAAdesione.Add(new MGAAdesione
                {
                    NumeroPolizza = $"P{i:D6}", NumeroAdesione = (uint)i, Cognome = $"Cognome{i:D6}", Nome = $"Nome{i:D6}",
                    DataNascita = new DateOnly(1980, 1, 1), Legame = MGAAdesione.TipoLegame.Titolare,
                });
            }
            dbCtx.SaveChanges();
        }
        EFDataGridModel<MGADbContext, MGAAdesione> model = new()
        {
            DbContextFactory = new MockMGADbContextFactory(options),
            BaseQueryFn = (dbCtx, _) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome") { PreferredSize = 20 }, a => a.Cognome);
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome") { PreferredSize = 20 }, a => a.Nome);

        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps.Add(g => g.Model, model).Add(g => g.PageSize, 20)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.RowDialogEnabled, true));
        int read = model.GetCurrentRowCount();
        await cut.InvokeAsync(() => cut.Instance.ShowRowDialogAsync());
        var dialog = cut.FindComponent<RowViewDlg<MGAAdesione>>();
        for (int i = 0; i < read + 5; i++)
            await dialog.FindAll("button.move")[1].ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(read + 5));
            Assert.That(model.GetCurrentRowCount(), Is.GreaterThan(read), "the grid read on");
            Assert.That(dialog.Markup, Does.Contain($"Cognome{model.GetCurrentRow():D6}"));
        });
    }
}
