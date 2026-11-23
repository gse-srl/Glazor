using Bunit;
using Glazor.Grds;
using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The render path had no coverage at all: <see cref="AGrid{TGridItem}.SyncShownColumns"/> alone is about
/// 120 lines of index arithmetic deciding which columns are visible, and nothing exercised it. These are the
/// cases where a regression would be silent — column windowing, paging, the current-row marker, and the
/// tri-state select-all — rather than an attempt at breadth.
/// </summary>
public class GridRenderTests
{
    private sealed record Product(string Code, string Name, decimal Price);

    private static Product[] Rows(int count) =>
        Enumerable.Range(0, count).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray();

    /// <summary>
    /// Columns are registered on the model rather than declared as GridColumn children: the razor form just
    /// calls into the model, and building them here keeps the tests independent of the collection dance.
    /// </summary>
    private static DataGridModel<Product> ModelOf(int rowCount, params string[] extraColumns)
    {
        DataGridModel<Product> model = new(Rows(rowCount));
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        foreach (string extra in extraColumns)
            model.AddColumn(extra, new Column<Product, string>(extra) { PreferredSize = 10 }, p => extra);
        return model;
    }

    private static IRenderedComponent<Grigliaria<Product>> RenderGrid(
        BunitContext ctx, IDataGridModel<Product> model, Action<ComponentParameterCollectionBuilder<Grigliaria<Product>>>? extra = null)
        => ctx.Render<Grigliaria<Product>>(ps =>
        {
            ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner());
            extra?.Invoke(ps);
        });

    // Each header shows its label and the place the grid gives it, "Name@1", where a column offset would show
    private static readonly RenderFragment<IGrid<Product>.HCellTemplateParam> PlacedHeader = p => b =>
    {
        b.OpenElement(0, "th");
        b.AddContent(1, $"{p.Column.GetLabel()}@{p.ToShow.Idx}");
        b.CloseElement();
    };

    /// <summary>
    /// Without the cell cursor the row heading takes the click for the whole row: it must let the click through, or a checkbox,
    /// a button or a link in a cell does nothing.
    /// </summary>
    [Test]
    public void RowClickWithoutTheCellCursorLetsTheClickThroughTest()
    {
        using BunitContext ctx = NewContext();
        var mouse = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/MouseAction.razor.js");
        var cut = RenderGrid(ctx, ModelOf(3), ps => ps.Add(g => g.CellCursorEnabled, false));
        cut.WaitForAssertion(() =>
        {
            var rowClicks = mouse.Invocations["registerMouseHandler"]
                .Where(i => Equals(i.Arguments[3], "click") && Equals(i.Arguments[7], "tr")).ToList();
            Assert.That(rowClicks, Has.Count.EqualTo(3));
            Assert.That(rowClicks.Select(i => i.Arguments[8]), Is.All.EqualTo(false), "CancelBubble");
        });
    }

    /// <summary>
    /// ←/→ and Ctrl+wheel move the cell cursor: a grid without one leaves them to the page, which scrolls or zooms with them,
    /// rather than taking them for nothing.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void ColumnKeysAreTheGridsOnlyWithTheCellCursorTest(bool cellCursor)
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), ps => ps.Add(g => g.CellCursorEnabled, cellCursor));
        var arrows = cut.FindComponents<KeyAction>().Select(k => k.Instance)
            .Where(k => !k.CtrlKey && k.Key is "ArrowLeft" or "ArrowRight" && k.BindAncestorWithTag == "div");
        var ctrlWheel = cut.FindComponents<MouseAction>().Select(m => m.Instance)
            .Where(m => m.CtrlKey && m.Type == MouseAction.MouseType.Wheel);
        Assert.Multiple(() =>
        {
            Assert.That(arrows.Select(k => k.Enabled), Is.EqualTo(new[] { cellCursor, cellCursor }));
            Assert.That(ctrlWheel.Select(m => m.Enabled), Is.EqualTo(new[] { cellCursor }));
        });
    }

    // ─────────────────────────── rendering and cell values ───────────────────────────

    /// <summary>
    /// The whole wiring in one assertion: model, column registration, Defer, SyncBeforeRendering, the cell
    /// decorators. If this breaks, nothing below is meaningful.
    /// </summary>
    [Test]
    public void RendersCellValuesForEveryColumnTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("C00"), "a fixed column's value");
            Assert.That(cut.Markup, Does.Contain("Name00"), "a scrolling column's value");
            Assert.That(cut.Markup, Does.Contain("Name02"), "the last row is rendered too");
            Assert.That(cut.Markup, Does.Contain("Code"), "the header label");
        });
    }

    /// <summary>
    /// An empty model must take the placeholder branch rather than trying to render row 0 — the row loop and
    /// the empty-row loop are separate paths in the template and only one of them is right here.
    /// </summary>
    [Test]
    public void EmptyModelRendersThePlaceholderRowTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(0));

        Assert.That(cut.Markup, Does.Contain("noData"),
            "an empty model must reach the empty-row branch");
        Assert.That(cut.Markup, Does.Not.Contain("Name00"));
    }

    // ─────────────────────────── paging ───────────────────────────

    /// <summary>
    /// PageSize caps the rendered data rows, and the remainder of the page is padded with empty rows so the
    /// grid keeps a stable height. Both halves matter: too few rows and the layout jumps, too many and the
    /// grid is rendering data it was told not to.
    /// </summary>
    [Test]
    public void RendersAtMostPageSizeDataRowsTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(20), ps => ps.Add(g => g.PageSize, 5));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Name00"), "first row of the page");
            Assert.That(cut.Markup, Does.Contain("Name04"), "last row of the page");
            Assert.That(cut.Markup, Does.Not.Contain("Name05"), "PageSize must cap the page");
            Assert.That(cut.Markup, Does.Not.Contain("Name19"));
        });
    }

    /// <summary>
    /// The current row carries a distinct class, which is what the cell cursor and the row heading key off.
    /// A model with rows always has a current row: SyncBeforeRendering promotes row 0 when none is set.
    /// </summary>
    [Test]
    public void CurrentRowIsMarkedTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(5);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 5));

        Assert.That(model.GetCurrentRow(), Is.EqualTo(0), "row 0 becomes current when none was set");
        Assert.That(cut.FindAll("tr.current"), Has.Count.EqualTo(1),
            "exactly one row may be marked current");

        cut.InvokeAsync(() => model.SetCurrentRow(2));
        cut.Render();

        Assert.That(model.GetCurrentRow(), Is.EqualTo(2));
        Assert.That(cut.FindAll("tr.current"), Has.Count.EqualTo(1),
            "still exactly one after the current row moves");
    }

    // ─────────────────────────── column windowing (SyncShownColumns) ───────────────────────────

    /// <summary>
    /// With a horizontal resizer the grid shows only as many columns as fit the displayed units, and fixed
    /// columns are always among them. This is the arithmetic in SyncShownColumns, which had no coverage:
    /// 25 units against a fixed 10-unit column plus 10-unit scrolling columns leaves room for one scroller.
    /// </summary>
    [Test]
    public void HorizontalWindowShowsFixedColumnsPlusWhatFitsTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3, "Extra1", "Extra2"),
            ps => ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(25)));

        IReadOnlyList<string> showed = cut.Instance.GetShownColumns().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(showed, Does.Contain("Code"), "a fixed column is always shown");
            Assert.That(showed.Count, Is.LessThan(5), "not every column fits in 25 units");
            Assert.That(showed.Count, Is.GreaterThan(1), "the fixed column plus at least one more");
        });
    }

    /// <summary>
    /// NextCol / PrevCol move the window over the non-fixed columns while leaving the fixed ones in place.
    /// The fixed-column boundary is computed separately from the scrolling window, so a shift that dragged a
    /// fixed column out — or failed to move at all — would be an easy regression here.
    /// </summary>
    [Test]
    public void NextColAndPrevColShiftTheWindowKeepingFixedColumnsTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3, "Extra1", "Extra2"),
            ps => ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(25)));

        IReadOnlyList<string> first = cut.Instance.GetShownColumns().ToList();
        Assert.That(cut.Instance.CanNextCol(), Is.True, "there are columns to the right");

        cut.InvokeAsync(() => cut.Instance.NextCol());
        cut.Render();
        IReadOnlyList<string> shifted = cut.Instance.GetShownColumns().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shifted, Does.Contain("Code"), "the fixed column survives a shift");
            Assert.That(shifted, Is.Not.EqualTo(first), "the window actually moved");
            Assert.That(cut.Instance.CanPrevCol(), Is.True, "and can move back");
        });

        cut.InvokeAsync(() => cut.Instance.PrevCol());
        cut.Render();
        Assert.That(cut.Instance.GetShownColumns(), Is.EqualTo(first),
            "moving right then left returns to the starting window");
    }

    /// <summary>
    /// Without a horizontal resizer there is no window and every column is shown. Worth pinning separately:
    /// the no-resizer path bypasses the arithmetic entirely (_horzDisplayedUnits stays below zero), so it
    /// could break independently of the windowed one.
    /// </summary>
    [Test]
    public void WithoutAHorizontalResizerEveryColumnIsShownTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3, "Extra1", "Extra2"));

        Assert.That(cut.Instance.GetShownColumns().Count(), Is.EqualTo(5));
    }

    // ─────────────────────────── MultiSelGrid tri-state select-all ───────────────────────────

    /// <summary>
    /// The header checkbox is tri-state: false when nothing is selected, null when some are, true when the
    /// selection equals every selectable row. That last comparison is the one that has to consult the
    /// selectable source, so it is worth asserting the three states rather than just that it renders.
    /// </summary>
    [Test]
    public void SelectAllReportsTheThreeSelectionStatesTest()
    {
        using BunitContext ctx = NewContext();
        Product[] rows = Rows(3);
        DataGridModel<Product> model = ModelOf(0);
        foreach (Product row in rows)
            model.GetData().Add(row);

        SetSelectionModelWithSelectAll<Product> selection = new(rows, new HashSet<Product>(), defensiveCopy: false);

        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()));

        Assert.That(cut.Instance.SelectAll, Is.False, "nothing selected");

        cut.InvokeAsync(() => selection.Select(rows[0], true));
        cut.Render();
        Assert.That(cut.Instance.SelectAll, Is.Null, "some selected reads as indeterminate");

        cut.InvokeAsync(() => cut.Instance.SelectAll = true);
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.SelectAll, Is.True, "all selected");
            Assert.That(selection.SelectedCount(), Is.EqualTo(rows.Length));
        });

        cut.InvokeAsync(() => cut.Instance.SelectAll = false);
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.SelectAll, Is.False, "back to none");
            Assert.That(selection.SelectedCount(), Is.Zero);
        });
    }

    /// <summary>A host opens a column's filter dialog on the wrapper grid too, with no header in view, and its OK filters the grid.</summary>
    [Test]
    public async Task MultiSelGridOpensTheFilterDialogTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(10);
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>())
            .Add(g => g.ShowHeader, false)
            .Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Name", null));
        var dialog = cut.FindComponent<FilterDefDlg>();
        await dialog.Find("input[id$=_value]").ChangeAsync(new ChangeEventArgs { Value = "Name01" });
        await dialog.Find("button.ok").ClickAsync(new());
        Assert.That(model.GetRowCount(), Is.EqualTo(1));
    }

    /// <summary>
    /// Select-all works on every candidate, the rows the filter hides included, so the box is disabled while the grid is filtered
    /// and enabled again once the filter is cleared.
    /// </summary>
    [Test]
    public void SelectAllIsDisabledWhileTheGridIsFilteredTest()
    {
        using BunitContext ctx = NewContext();
        Product[] rows = Rows(3);
        DataGridModel<Product> model = ModelOf(0);
        foreach (Product row in rows)
            model.GetData().Add(row);
        SetSelectionModelWithSelectAll<Product> selection = new(rows);
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()));
        bool BoxDisabled() => cut.FindComponents<TriStateCheckbox>().Single().Instance.Disabled;
        Assert.That(BoxDisabled(), Is.False);
        cut.InvokeAsync(() => model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Name", new DefaultDef { Value = "Name01" })]));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
            Assert.That(BoxDisabled(), Is.True, "filtered");
        });
        cut.InvokeAsync(() => model.ApplyFilters([]));
        cut.Render();
        Assert.That(BoxDisabled(), Is.False, "the filter cleared");
    }

    private sealed record Role(int Id, string Name, bool Locked);

    /// <summary>
    /// A row the selection function refuses shows its box disabled, and nothing else in the grid changes it either: the
    /// select-all box, Ctrl+Space and a double-click leave a locked selected row selected, and the last two raise no
    /// OnSelectionChanged.
    /// </summary>
    [TestCase("select-all box")]
    [TestCase("Ctrl+Space")]
    [TestCase("double-click")]
    public async Task LockedRowKeepsItsSelectionTest(string path)
    {
        using BunitContext ctx = NewContext();
        Role[] roles = [new(1, "Admin", true), new(2, "Editor", false), new(3, "Viewer", false)];
        DataGridModel<Role> model = new(roles) { ItemKeyFn = r => r.Id };
        model.AddColumn("Name", new Column<Role, string>("Name"), r => r.Name);
        HashSet<int> userRoles = [1, 2];
        SetSelectionModelWithSelectAll<Role, int> selection = new(roles, r => r.Id, userRoles, defensiveCopy: false, r => !r.Locked);
        int changes = 0;
        var cut = ctx.Render<MultiSelGrid<Role>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.OnSelectionChanged, (Action)(() => changes++)));
        var grid = cut.FindComponent<Grigliaria<Role>>();
        switch (path)
        {
            case "select-all box":
                await cut.InvokeAsync(() => cut.Instance.SelectAll = true);   // what the header box sets
                int[] afterSelectAll = userRoles.Order().ToArray();
                bool? boxAfterSelectAll = cut.Instance.SelectAll;
                await cut.InvokeAsync(() => cut.Instance.SelectAll = false);
                Assert.Multiple(() =>
                {
                    Assert.That(afterSelectAll, Is.EqualTo(new[] { 1, 2, 3 }), "select-all");
                    Assert.That(boxAfterSelectAll, Is.True, "every row the user can change is selected");
                    Assert.That(userRoles, Is.EqualTo(new[] { 1 }), "deselect-all");
                });
                return;
            case "Ctrl+Space":
                await cut.InvokeAsync(() => grid.Instance.SetCurrentRow(0));
                var ctrlSpace = cut.FindComponents<KeyAction>().Single(k => k.Instance.CtrlKey && k.Instance.Key == " ");
                await cut.InvokeAsync(() => ctrlSpace.Instance.ProcessAsync(" ", null, null));
                break;
            case "double-click":
                await cut.InvokeAsync(() => grid.Instance.OnDblClick.InvokeAsync(new DblClickEvent(0, null)));
                break;
        }
        Assert.That(userRoles.Order(), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(changes, Is.Zero, "nothing changed");
    }

    /// <summary>
    /// A click on the select-all box raises OnSelectionChanged as a click on a row's box does, so a host that shows the selection
    /// count re-renders.
    /// </summary>
    [Test]
    public async Task SelectAllBoxRaisesOnSelectionChangedTest()
    {
        using BunitContext ctx = NewContext();
        Role[] roles = [new(1, "Admin", false), new(2, "Editor", false), new(3, "Viewer", false)];
        DataGridModel<Role> model = new(roles) { ItemKeyFn = r => r.Id };
        model.AddColumn("Name", new Column<Role, string>("Name"), r => r.Name);
        SetSelectionModelWithSelectAll<Role, int> selection = new(roles, r => r.Id);
        int changes = 0;
        var cut = ctx.Render<MultiSelGrid<Role>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.OnSelectionChanged, (Action)(() => changes++)));
        await cut.FindComponent<TriStateCheckbox>().Find("input").ChangeAsync(new ChangeEventArgs { Value = true });   // awaited: the handler runs on the renderer's thread
        Assert.That((selection.SelectedCount(), changes), Is.EqualTo((3, 1)), "select-all");
        await cut.FindComponent<TriStateCheckbox>().Find("input").ChangeAsync(new ChangeEventArgs { Value = false });
        Assert.That((selection.SelectedCount(), changes), Is.EqualTo((0, 2)), "deselect-all");
        await cut.InvokeAsync(() => cut.Instance.SelectAll = false);
        Assert.That(changes, Is.EqualTo(2), "nothing left to deselect, no event");
    }

    /// <summary>An in-memory model that counts the reloads asked of it.</summary>
    private sealed class ReloadableRoles(IList<Role> roles) : DataGridModel<Role>(roles), IReloadableGridModel<Role>
    {
        public List<bool> Reloads { get; } = [];
        public void ForceReload(bool restoreCurrentRow) => Reloads.Add(restoreCurrentRow);
        public ValueTask<bool> ReloadItemAsync(int rowIndex, CancellationToken ct = default) => ValueTask.FromResult(true);
    }

    /// <summary>
    /// With ReloadEnabled the footer has a reload button when the model can reload, also through MultiSelGrid's own model, and
    /// a click reloads keeping the current row; without it, or over a model that cannot reload, there is no button.
    /// </summary>
    [TestCase("Grigliaria")]
    [TestCase("MultiSelGrid")]
    public async Task ReloadButtonReloadsTheModelTest(string grid)
    {
        using BunitContext ctx = NewContext();
        Role[] roles = [new(1, "Admin", false), new(2, "Editor", false)];
        ReloadableRoles model = new(roles) { ItemKeyFn = r => r.Id };
        model.AddColumn("Name", new Column<Role, string>("Name"), r => r.Name);
        IRenderedComponent<IComponent> Render(IDataGridModel<Role> m, bool enabled) => grid == "Grigliaria"
            ? ctx.Render<Grigliaria<Role>>(ps => ps.Add(g => g.Model, m).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ReloadEnabled, enabled))
            : ctx.Render<MultiSelGrid<Role>>(ps => ps.Add(g => g.Model, m).Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Role, int>(roles, r => r.Id))
                .Add(g => g.Spinner, new NoSpinner()).Add(g => g.ReloadEnabled, enabled));
        DataGridModel<Role> plain = new(roles);
        plain.AddColumn("Name", new Column<Role, string>("Name"), r => r.Name);

        var cut = Render(model, true);
        await cut.Find("button.btnReload").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.Reloads, Is.EqualTo(new[] { true }), "one reload, keeping the current row");
            Assert.That(Render(model, false).FindAll("button.btnReload"), Is.Empty, "not enabled");
            Assert.That(Render(plain, true).FindAll("button.btnReload"), Is.Empty, "a model that cannot reload");
        });
    }

    private sealed record City(int Id, string Name);

    /// <summary>
    /// Typing on a grid sorted on a string column lands on the first row that starts with the text, in both directions: sorted
    /// descending, the search's insertion point falls after those rows, and an exact match is any one of its equals.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TypingLandsOnTheFirstRowThatStartsWithTheTextTest(bool ascending)
    {
        using BunitContext ctx = NewContext();
        City[] cities = [new(1, "Ancona"), new(2, "Rome"), new(3, "Rome"), new(4, "Rome"), new(5, "Rome"), new(6, "Rome"), new(7, "Rome"), new(8, "Zagarolo")];
        DataGridModel<City> model = new(cities) { ItemKeyFn = c => c.Id };
        model.AddColumn("Name", new Column<City, string>("Name"), c => c.Name);
        model.SetSortedColumn("Name", ascending);
        var cut = ctx.Render<Grigliaria<City>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        var typing = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == KeyAction.AnyChars);
        foreach (string typed in new[] { "R", "Rome" })
        {
            await cut.InvokeAsync(() => typing.Instance.ProcessAsync(typed, null, null));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(1), $"the first Rome, typing {typed}");
        }
    }

    /// <summary>
    /// The footer's search button opens the search on the current row's own value and leaves the current row where it is, even
    /// where an earlier row starts with that value or holds it in another case.
    /// </summary>
    [TestCase(false, "Roma")]
    [TestCase(true, "ROME")]
    public async Task SearchButtonKeepsTheCurrentRowTest(bool ascending, string current)
    {
        using BunitContext ctx = NewContext();
        City[] cities = [new(1, "Zagarolo"), new(2, "Rovigo"), new(3, "ROME"), new(4, "rome"), new(5, "Romagna"), new(6, "Roma"), new(7, "Ancona")];
        DataGridModel<City> model = new(cities) { ItemKeyFn = c => c.Id };
        model.AddColumn("Name", new Column<City, string>("Name"), c => c.Name);
        model.SetSortedColumn("Name", ascending);
        var cut = ctx.Render<Grigliaria<City>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.PageSize, 10));
        await cut.InvokeAsync(() => model.SetCurrentItem(cities.Single(c => c.Name == current)));
        cut.Render();
        await cut.Find("button.btnSearchOnCurrentSortedColumn").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.That(model.GetCurrentItem()!.Name, Is.EqualTo(current));
    }

    /// <summary>
    /// Characters typed while the search popup opens reach the grid's type-ahead after the ones that opened it: they follow
    /// them in the search box, and the search runs on the whole text.
    /// </summary>
    [Test]
    public async Task CharactersTypedWhileTheSearchOpensFollowTheFirstTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<City> model = new([new(1, "Ancona"), new(2, "Rapallo"), new(3, "Rome"), new(4, "Zagarolo")]) { ItemKeyFn = c => c.Id };
        model.AddColumn("Name", new Column<City, string>("Name"), c => c.Name);
        model.SetSortedColumn("Name", true);
        var cut = ctx.Render<Grigliaria<City>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        var typing = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == KeyAction.AnyChars);
        await cut.InvokeAsync(() => typing.Instance.ProcessAsync("R", null, null));
        Assert.That(model.GetCurrentItem()?.Name, Is.EqualTo("Rapallo"));
        await cut.InvokeAsync(() => typing.Instance.ProcessAsync("o", null, null));   // as the popup's own render sends it
        Assert.That(cut.FindComponent<InputTextOnInput>().Instance.Value, Is.EqualTo("Ro"));
        cut.WaitForState(() => model.GetCurrentItem()?.Name == "Rome", TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Esc in the search box closes the search popup, and goes no further: a host's own Escape action around the grid, closing
    /// the dialog that holds it for instance, must not run.
    /// </summary>
    [Test]
    public async Task EscClosesTheSearchPopupTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<City> model = new([new(1, "Ancona"), new(2, "Rome"), new(3, "Zagarolo")]) { ItemKeyFn = c => c.Id };
        model.AddColumn("Name", new Column<City, string>("Name"), c => c.Name);
        model.SetSortedColumn("Name", true);
        var cut = ctx.Render<Grigliaria<City>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        var typing = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == KeyAction.AnyChars);
        await cut.InvokeAsync(() => typing.Instance.ProcessAsync("R", null, null));
        var searchPopup = cut.FindComponents<Popup>().Single(p => p.Instance.Class == "searchPopup");
        Popup search = searchPopup.Instance;
        Assert.That(search.IsVisible, Is.True);
        var esc = searchPopup.FindComponents<KeyAction>().Where(k => k.Instance.Key == "Escape").ToList();
        Assert.That(esc, Has.Count.EqualTo(1), "the search popup takes Esc");
        Assert.That(esc[0].Instance.BindAncestorWithTag, Is.EqualTo("dialog"), "on the popup, before the key leaves it");
        await cut.InvokeAsync(() => esc[0].Instance.ProcessAsync(null, null, null));
        Assert.That(search.IsVisible, Is.False);
    }

    /// <summary>
    /// Esc in a column menu closes it, and goes no further, as in the search popup.
    /// </summary>
    [Test]
    public async Task EscClosesTheColumnMenuTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), ps => ps.Add(g => g.ColumnsPopupEnabled, true));
        var open = cut.FindComponents<MouseAction>().First(m => m.Instance.Type == MouseAction.MouseType.ContextMenu);
        await cut.InvokeAsync(() => open.Instance.ProcessAsync(null, null, 10, 10, 0));
        var menu = cut.FindComponents<Popup>().Single(p => p.Instance.Class == "columnPopup" && p.Instance.IsVisible);
        var esc = menu.FindComponents<KeyAction>().Where(k => k.Instance.Key == "Escape").ToList();
        Assert.That(esc, Has.Count.EqualTo(1), "the column menu takes Esc");
        Assert.That(esc[0].Instance.BindAncestorWithTag, Is.EqualTo("dialog"), "on the menu, before the key leaves it");
        await cut.InvokeAsync(() => esc[0].Instance.ProcessAsync(null, null, null));
        Assert.That(menu.Instance.IsVisible, Is.False);
    }

    private static IRenderedComponent<DefaultHCell<Product>> HeaderOf(IRenderedComponent<Grigliaria<Product>> cut, string column) =>
        cut.FindComponents<DefaultHCell<Product>>().Single(h => h.Instance.Param.Model.GetColumnName(h.Instance.Param.ColIdx) == column);

    // Opens the column's menu and gives the entry with that text
    private static async Task<IRenderedComponent<Anchor>> MenuEntryAsync(IRenderedComponent<Grigliaria<Product>> cut, string column, string textKey)
    {
        var header = HeaderOf(cut, column);
        var open = header.FindComponents<MouseAction>().Single(m => m.Instance.Type == MouseAction.MouseType.ContextMenu);
        await cut.InvokeAsync(() => open.Instance.ProcessAsync(null, null, 10, 10, 0));
        var menu = header.FindComponents<Popup>().Single(p => p.Instance.Class == "columnPopup" && p.Instance.IsVisible);
        string text = GlazorTexts.Default.Get(textKey);
        return menu.FindComponents<Anchor>().Single(a => a.Find("a").TextContent.Trim() == text);
    }

    /// <summary>
    /// The column menu does what it says: Shift left is off on the first column that moves, Shift right swaps the column with
    /// the next one, Sort descending sorts the column descending, and Clear filter removes its filter.
    /// </summary>
    [Test]
    public async Task ColumnMenuEntriesActOnTheColumnTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.ColumnsPopupEnabled, true));

        Assert.That((await MenuEntryAsync(cut, "Name", TextKeys.ShiftLeft)).Instance.Disabled, Is.True, "Name is the first column that moves");
        await (await MenuEntryAsync(cut, "Name", TextKeys.ShiftRight)).Find("a").ClickAsync(new());
        Assert.Multiple(async () =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Price", "Name" }));
            Assert.That((await MenuEntryAsync(cut, "Price", TextKeys.ShiftLeft)).Instance.Disabled, Is.True, "Price is now first");
            Assert.That((await MenuEntryAsync(cut, "Name", TextKeys.ShiftLeft)).Instance.Disabled, Is.False);
        });

        await (await MenuEntryAsync(cut, "Name", TextKeys.SortDescending)).Find("a").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetSortedColumn(), Is.EqualTo("Name"));
            Assert.That(model.GetSortType(), Is.EqualTo(SortType.Desc));
        });

        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        Assert.That(model.GetRowCount(), Is.EqualTo(1));
        await (await MenuEntryAsync(cut, "Name", TextKeys.ClearFilter)).Find("a").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.HasFilter("Name"), Is.False);
            Assert.That(model.GetRowCount(), Is.EqualTo(3));
        });
    }

    /// <summary>
    /// A click on a sortable header sorts ascending, the next one descending, and the third removes the sort.
    /// </summary>
    [Test]
    public async Task HeaderClicksCycleAscendingDescendingAndNoSortTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3));
        List<string?> states = [];
        for (int i = 0; i < 3; i++)
        {
            await HeaderOf(cut, "Name").FindAll("th > a")[0].ClickAsync(new());
            states.Add(HeaderOf(cut, "Name").Find("th").GetAttribute("aria-sort"));
        }
        Assert.Multiple(() =>
        {
            Assert.That(states, Is.EqualTo(new[] { "ascending", "descending", "none" }));
            Assert.That(cut.Instance.GetModel() is IDataGridModel<Product> m ? m.GetSortedColumn() : "?", Is.Null);
        });
    }

    /// <summary>
    /// SetPreferredSize with a null size clears the size set: the layout drops it and the column takes its own PreferredSize.
    /// </summary>
    [Test]
    public async Task SetPreferredSizeToNullClearsTheSizeSetTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.Layoutable, true));
        int SizeOfName() => cut.Instance.GetColumn(model.GetColumnIndex("Name")).GetPreferredSize();
        await cut.InvokeAsync(() => cut.Instance.SetPreferredSize("Name", 33));
        Assert.That(SizeOfName(), Is.EqualTo(33));
        await cut.InvokeAsync(() => cut.Instance.SetPreferredSize("Name", null));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetLayout()!.Attributes.ContainsKey("Name"), Is.False);
            Assert.That(SizeOfName(), Is.EqualTo(10), "the column's own PreferredSize");
        });
    }

    /// <summary>
    /// While a column menu is open the grid's keys and wheel are off, as while the search popup is: a key pressed in the menu
    /// does not move the rows behind it. They come back when the menu closes, however it closes.
    /// </summary>
    [Test]
    public async Task ColumnMenuTakesTheGridsKeysWhileOpenTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(5), ps => ps.Add(g => g.ColumnsPopupEnabled, true));
        bool[] GridKeys() => cut.FindComponents<KeyAction>().Where(k => k.Instance.Key is "ArrowDown" or "PageDown" or "Home")
            .Select(k => k.Instance.Enabled)
            .Concat(cut.FindComponents<MouseAction>().Where(m => m.Instance.Type == MouseAction.MouseType.Wheel).Select(m => m.Instance.Enabled))
            .ToArray();
        Assert.That(GridKeys(), Is.All.True, "on before the menu opens");

        var open = cut.FindComponents<MouseAction>().First(m => m.Instance.Type == MouseAction.MouseType.ContextMenu);
        await cut.InvokeAsync(() => open.Instance.ProcessAsync(null, null, 10, 10, 0));
        Assert.That(GridKeys(), Is.All.False, "off while it is open");

        var menu = cut.FindComponents<Popup>().Single(p => p.Instance.Class == "columnPopup" && p.Instance.IsVisible);
        await cut.InvokeAsync(() => menu.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty));
        Assert.That(GridKeys(), Is.All.True, "on again once the browser closes it");
    }

    /// <summary>
    /// SetFilter and SetFilters called from host code, outside a UI event, show the filtered rows at once, as the other setters
    /// of the grid do.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task SetFilterRendersTheFilteredRowsTest(bool bulk)
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3));
        Assert.That(cut.Markup, Does.Contain("Name02"));
        DefaultDef filter = new() { Value = "Name01" };
        if (bulk)
            await cut.InvokeAsync(() => cut.Instance.SetFilters(new Dictionary<string, AFilterDef> { ["Name"] = filter }));
        else
            await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", filter));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Name01"));
            Assert.That(cut.Markup, Does.Not.Contain("Name02"));
        });
    }

    /// <summary>The filters GetFilters returns can be kept: a later change leaves them as they were, and SetFilters restores them.</summary>
    [Test]
    public async Task GetFiltersCanBeKeptAndRestoredTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model);
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        IReadOnlyDictionary<string, AFilterDef> saved = cut.Instance.GetFilters();
        await cut.InvokeAsync(() => cut.Instance.SetFilters(new Dictionary<string, AFilterDef> { ["Price"] = new NumberDef<decimal> { From = 3m } }));
        Assert.That(saved.Keys, Is.EquivalentTo(new[] { "Name" }), "the copy kept");
        await cut.InvokeAsync(() => cut.Instance.SetFilters(saved));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters().Keys, Is.EquivalentTo(new[] { "Name" }), "restored");
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// OK in the filter dialog applies what its editor holds: the grid filters the column, the dialog closes and the host's
    /// callback gets the column and the definition.
    /// </summary>
    [Test]
    public async Task OkInTheFilterDialogAppliesTheFilterTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(10);
        var cut = RenderGrid(ctx, model);
        List<FilterDefDlg.SubmitEvent> submitted = [];
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Name", submitted.Add));
        var dialog = cut.FindComponent<FilterDefDlg>();
        await dialog.Find("input[id$=_value]").ChangeAsync(new ChangeEventArgs { Value = "Name01" });
        await dialog.Find("button.ok").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
            Assert.That((cut.Instance.GetFilter("Name") as DefaultDef)?.Value, Is.EqualTo("Name01"));
            Assert.That(dialog.FindComponent<DialogBox>().Instance.IsVisible, Is.False);
            Assert.That(submitted.Select(s => s.ColumnName), Is.EqualTo(new[] { "Name" }));
            Assert.That((submitted[0].FilterDef as DefaultDef)?.Value, Is.EqualTo("Name01"));
        });
    }

    /// <summary>
    /// OK on a range whose From is past its To is refused: the dialog stays open, nothing is filtered and the host hears
    /// nothing; once the range is put right, OK applies it.
    /// </summary>
    [Test]
    public async Task OkInTheFilterDialogRefusesAnInvertedRangeTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(10);
        var cut = RenderGrid(ctx, model);
        List<FilterDefDlg.SubmitEvent> submitted = [];
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Price", submitted.Add));
        var dialog = cut.FindComponent<FilterDefDlg>();
        await dialog.Find("input[id$=_from]").ChangeAsync(new ChangeEventArgs { Value = "5" });
        await dialog.Find("input[id$=_to]").ChangeAsync(new ChangeEventArgs { Value = "1" });
        await dialog.Find("button.ok").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(dialog.FindComponent<DialogBox>().Instance.IsVisible, Is.True, "the dialog stays open");
            Assert.That(model.GetRowCount(), Is.EqualTo(10), "nothing filtered");
            Assert.That(cut.Instance.HasFilter("Price"), Is.False);
            Assert.That(submitted, Is.Empty);
        });

        await dialog.Find("input[id$=_to]").ChangeAsync(new ChangeEventArgs { Value = "10" });
        await dialog.Find("button.ok").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(3), "6, 7.5 and 9");
            Assert.That(dialog.FindComponent<DialogBox>().Instance.IsVisible, Is.False);
            Assert.That(submitted, Has.Count.EqualTo(1));
        });
    }

    private enum Light { Red, Amber, Green }
    private sealed record Lamp(int Id, Light Light);

    /// <summary>
    /// An EnumDef given as a column's FilterDef makes it an Enum column, while the definition reports its own enum type: OK
    /// with a member deselected applies the filter, and a memento holding it restores it.
    /// </summary>
    [Test]
    public async Task OkInTheFilterDialogAppliesAnEnumDefOnAnEnumColumnTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Lamp> model = new([new(1, Light.Red), new(2, Light.Amber), new(3, Light.Green)]);
        var cut = ctx.Render<Grigliaria<Lamp>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .AddChildContent<GridColumn<Lamp, Enum>>(c => c
                .Add(x => x.Property, l => l.Light).Add(x => x.Label, "Light").Add(x => x.FilterDef, new EnumDef(typeof(Light), true))));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Light", null));
        var dialog = cut.FindComponent<FilterDefDlg>();
        var editor = dialog.FindComponent<EnumDefRenderer>();
        await cut.InvokeAsync(() => editor.Instance.SelectionModel.Select(Light.Amber, false));
        await dialog.FindAll("button.ok")[^1].ClickAsync(new());   // the editor's own grid has a filter dialog too, ahead of this one
        Assert.That(model.GetRowCount(), Is.EqualTo(2));

        Memento memento = cut.Instance.CreateMemento();
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Light", null));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(memento));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.HasFilter("Light"), Is.True);
            Assert.That(model.GetRowCount(), Is.EqualTo(2));
        });
    }

    /// <summary>
    /// Esc in the filter dialog closes it, leaving the filter as it was, and goes no further, as in the column menu.
    /// </summary>
    [Test]
    public async Task EscClosesTheFilterDialogTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model);
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Name", null));
        var dialog = cut.FindComponent<FilterDefDlg>();
        var esc = dialog.FindComponents<KeyAction>().Where(k => k.Instance.Key == "Escape").ToList();
        Assert.That(esc, Has.Count.EqualTo(1), "the filter dialog takes Esc");
        Assert.That(esc[0].Instance.BindAncestorWithTag, Is.EqualTo("dialog"), "on the dialog, before the key leaves it");
        await cut.InvokeAsync(() => esc[0].Instance.ProcessAsync(null, null, null));
        Assert.Multiple(() =>
        {
            Assert.That(dialog.FindComponent<DialogBox>().Instance.IsVisible, Is.False);
            Assert.That(model.GetRowCount(), Is.EqualTo(1), "the filter is kept");
        });
    }

    /// <summary>
    /// The filter dialog's Enter action, OK, is bound to its whole content: Cancel and Clear take Enter themselves.
    /// </summary>
    [TestCase("cancel")]
    [TestCase("clear")]
    public async Task EnterOnTheFilterDialogsCancelAndClearRunsThemTest(string button)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model);
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Name", null));
        var dialog = cut.FindComponent<FilterDefDlg>();
        Assert.That(dialog.FindAll($"button.{button} > span[hidden]"), Has.Count.EqualTo(1), "the button carries its own Enter action");
        var own = dialog.FindComponents<KeyAction>().Where(k => k.Instance.Key == "Enter" && k.Instance.BindAncestorWithTag == null).ToList();
        await cut.InvokeAsync(() => own[button == "cancel" ? 0 : 1].Instance.ProcessAsync(null, null, null));
        Assert.Multiple(() =>
        {
            Assert.That(dialog.FindComponent<DialogBox>().Instance.IsVisible, Is.False);
            Assert.That(model.GetRowCount(), Is.EqualTo(button == "cancel" ? 1 : 3));
        });
    }

    /// <summary>A DistinctDef whose values never come: it records the token each load was given.</summary>
    private sealed class PendingNamesDef(List<CancellationToken> tokens) : DistinctDef<PendingNamesDef, string>(true)
    {
        public override PendingNamesDef Clone() => new(tokens);

        protected override async ValueTask<IEnumerable<string?>> RetrieveValuesAsync(CancellationToken ct)
        {
            tokens.Add(ct);
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        }

        public override PendingNamesDef Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Cancel in the filter dialog cancels the load of the values it was waiting for, and the next opening loads again.
    /// </summary>
    [Test]
    public async Task CancelInTheFilterDialogCancelsTheValuesLoadTest()
    {
        using BunitContext ctx = NewContext();
        List<CancellationToken> tokens = [];
        DataGridModel<Product> model = new(Rows(3));
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, FilterDef = new PendingNamesDef(tokens) }, p => p.Name);
        var cut = RenderGrid(ctx, model);
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Name", null));
        var dialog = cut.FindComponent<FilterDefDlg>();
        await dialog.FindAll("button.cancel")[^1].ClickAsync(new());   // the editor's own grid has a filter dialog too, ahead of this one
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Name", null));
        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Count.EqualTo(2));
            Assert.That(tokens[0].IsCancellationRequested, Is.True, "Cancel stops the first load");
            Assert.That(tokens[1].IsCancellationRequested, Is.False, "the second opening loads again");
        });
    }

    private sealed record Step(string Name, bool Pinned);

    /// <summary>
    /// An ordered MultiSelGrid asks at every render whether the current row can move. With no current row there is nothing to
    /// ask, and a host's swap test that reads its rows must not get row -1, or the render throws and the circuit ends. A model
    /// read while empty keeps no current row when its rows arrive, before the first render or after it. With a current row the
    /// test is asked.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task NoCurrentRowAsksNoSwapTestTest(bool rowsLater)
    {
        using BunitContext ctx = NewContext();
        Step[] steps = [new("a", false), new("b", true), new("c", false)];
        List<(int, int)> asked = [];
        GridModel<Step> model = new()
        {
            ItemKeyFn = s => s.Name,
            AreRowsSwappableFn = (m, a, b) => { asked.Add((a, b)); return !m.GetItem(a).Pinned && !m.GetItem(b).Pinned; }
        };
        model.AddColumn("Name", new Column<Step, string>("Name"), s => s.Name);
        if (!rowsLater)
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(-1), "read while empty");
            model.GetData().Set(steps);
        }
        var cut = ctx.Render<MultiSelGrid<Step>>(ps => ps.Add(g => g.Model, model)
            .Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Step>(steps)).Add(g => g.Spinner, new NoSpinner()));
        if (rowsLater)
        {
            await cut.InvokeAsync(() => model.GetData().Set(steps));
            cut.Render();
        }
        var grid = cut.FindComponent<Grigliaria<Step>>();
        await cut.InvokeAsync(() => grid.Instance.SetCurrentRow(0));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(asked, Has.None.Matches<(int Row1, int Row2)>(p => p.Row1 < 0 || p.Row2 < 0 || p.Row1 >= 3 || p.Row2 >= 3),
                "only rows that exist");
            Assert.That(asked, Does.Contain((0, 1)), "asked once there is a current row");
            Assert.That(cut.Find("button.btnMoveRowDown").HasAttribute("disabled"), Is.True, "the next row is pinned");
        });
    }

    /// <summary>
    /// A VertResizer decides the effective page size, but the PageSize parameter belongs to the parent and the grid
    /// must not overwrite it: wrappers forward the parameter down and the parent's next SetParametersAsync would fight
    /// the resizer. GetPageSize() is what the grid, its scrollers and its paging use.
    /// </summary>
    [Test]
    public void VertResizerDoesNotOverwriteThePageSizeParameterTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(20), ps => ps.Add(g => g.PageSize, 5).Add(g => g.VertResizer, GridVertResizers.Fixed(7)));
        Assert.That(cut.Instance.PageSize, Is.EqualTo(5), "the parameter is the parent's");
        Assert.That(cut.Instance.GetPageSize(), Is.EqualTo(7), "the resizer decides the effective page size");
        Assert.That(cut.Markup, Does.Contain("Name06").And.Not.Contain("Name07"), "seven data rows are rendered");
    }

    /// <summary>
    /// A Razor `EFProperty="a => a.X"` builds a new expression tree on every render. The column must compare it structurally,
    /// or every render re-registers the column and recompiles its getter. The ColumnInfo instance the
    /// model holds is the witness: it must survive a re-render.
    /// </summary>
    [Test]
    public void EFColumnIsNotReRegisteredOnEveryRenderTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(3)) { ItemKeyFn = p => p.Code };
        RenderFragment column = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Code");
            builder.AddAttribute(2, "EFProperty", (System.Linq.Expressions.Expression<Func<Product, string>>)(p => p.Code));   // a new tree per render, as Razor does
            builder.CloseComponent();
        };
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ChildContent, column));
        object registered = model.GetColumnInfo(0);
        cut.Render();
        cut.Render();
        Assert.That(model.GetColumnInfo(0), Is.SameAs(registered), "an unchanged EF column must not be re-registered");
        Assert.That(cut.Markup, Does.Contain("C00"));
    }

    /// <summary>
    /// A horizontal swipe scrolls columns only when horizontal paging is on; without a HorzResizer nothing clamps the
    /// offset, so it must not move. Vertical scrolling works in both cases.
    /// </summary>
    [Test]
    public async Task ScrollByMovesColumnsOnlyWithAHorzResizerTest()
    {
        using BunitContext ctx = NewContext();
        var plain = RenderGrid(ctx, ModelOf(5), ps => ps.Add(g => g.PageSize, 2).Add(g => g.HCellTemplate, PlacedHeader));
        await plain.InvokeAsync(() => plain.Instance.ScrollBy(1, 1));
        Assert.Multiple(() =>
        {
            Assert.That(plain.Markup, Does.Contain("Name@1"), "no horizontal paging, no column offset");
            Assert.That(plain.Instance.GetPageIdx(), Is.EqualTo(1), "rows still scroll");
        });

        var paged = RenderGrid(ctx, ModelOf(5), ps => ps.Add(g => g.PageSize, 2).Add(g => g.HorzResizer, GridHorzResizers.Fixed(25)));
        await paged.InvokeAsync(() => paged.Instance.ScrollBy(1, 0));
        Assert.That(paged.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Price" }), "with horizontal paging the offset moves");
    }

    private sealed record Node(string Name) : ITreeNode
    {
        public bool IsExpanded() => false;
        public int GetLevel() => 0;
        public bool IsLeaf() => true;
        public bool IsSubTotal() => false;
        public bool HasChildren() => false;
    }


    private sealed record Sized(string Name, int Size) : ITreeNode
    {
        public bool IsExpanded() => false;
        public int GetLevel() => 0;
        public bool IsLeaf() => true;
        public bool IsSubTotal() => false;
        public bool HasChildren() => false;
    }

    // Double-clicks every cell of the first row and returns the column indexes OnDblClick reports, nulls left out
    private static async Task<List<int>> DblClickFirstRowAsync<T>(IRenderedComponent<Grigliaria<T>> grid, List<DblClickEvent> clicks)
    {
        foreach (var dbl in grid.FindComponents<MouseAction>().Where(m => m.Instance.Type == MouseAction.MouseType.DblClick))
            await grid.InvokeAsync(() => dbl.Instance.ProcessAsync("td", "", 0, 0, 0));
        return clicks.Where(c => c.RowIdx == 0 && c.ColIdx != null).Select(c => c.ColIdx!.Value).ToList();
    }

    /// <summary>
    /// The wrapper grids add a column of their own in front of the host's: the column OnDblClick reports is the host model's.
    /// </summary>
    [Test]
    public async Task WrapperDblClickReportsTheHostColumnTest()
    {
        using BunitContext ctx = NewContext();
        List<DblClickEvent> treeClicks = [];
        TreeGridModel<Sized> tree = new([new("root", 1), new("child", 2)]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Sized, string>("Name"), n => n.Name);
        tree.AddColumn("Size", new Column<Sized, int>("Size"), n => n.Size);
        var treeGrid = ctx.Render<Treegliaria<Sized>>(ps => ps.Add(g => g.Model, tree)
            .Add(g => g.OnDblClick, EventCallback.Factory.Create<DblClickEvent>(this, e => treeClicks.Add(e))));

        List<DblClickEvent> selClicks = [];
        Role[] roles = [new(1, "Admin", false), new(2, "Editor", false)];
        DataGridModel<Role> model = new(roles) { ItemKeyFn = r => r.Id };
        model.AddColumn("Name", new Column<Role, string>("Name"), r => r.Name);
        model.AddColumn("Id", new Column<Role, int>("Id"), r => r.Id);
        var selGrid = ctx.Render<MultiSelGrid<Role>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, new SetSelectionModel<Role>())
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.ReadOnly, true)
            .Add(g => g.OnDblClick, EventCallback.Factory.Create<DblClickEvent>(this, e => selClicks.Add(e))));

        List<int> treeCols = await DblClickFirstRowAsync(treeGrid.FindComponent<Grigliaria<Sized>>(), treeClicks);
        List<int> selCols = await DblClickFirstRowAsync(selGrid.FindComponent<Grigliaria<Role>>(), selClicks);
        Assert.Multiple(() =>
        {
            Assert.That(treeCols.Select(tree.GetColumnName), Is.EqualTo(new[] { "Name", "Size" }), "Treegliaria");
            Assert.That(selCols.Select(model.GetColumnName), Is.EqualTo(new[] { "Name", "Id" }), "MultiSelGrid");
        });
    }

    /// <summary>
    /// OnModelChanged reports the model's changes: AfterSync once a filter is applied, Other when the current row moves. A
    /// model the grid no longer holds reaches it no more.
    /// </summary>
    [Test]
    public async Task OnModelChangedFollowsTheModelTest()
    {
        using BunitContext ctx = NewContext();
        List<GridModelChangeType> changes = [];
        DataGridModel<Product> model = ModelOf(5);
        var cut = RenderGrid(ctx, model, ps => ps.Add(g => g.OnModelChanged, changes.Add));

        changes.Clear();
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef() { Value = "Name01" }));
        cut.Render();
        Assert.That(changes, Does.Contain(GridModelChangeType.AfterSync), "a filter");

        changes.Clear();
        await cut.InvokeAsync(() => model.SetCurrentRow(-1));
        Assert.That(changes, Is.EqualTo(new[] { GridModelChangeType.Other }), "the current row");

        cut.Render(ps => ps.Add(g => g.Model, ModelOf(5)));
        changes.Clear();
        await cut.InvokeAsync(() => model.SetCurrentRow(0));
        Assert.That(changes, Is.Empty, "the model replaced");
    }

    /// <summary>
    /// The wrapper grids pass the host model's changes on to their own OnModelChanged.
    /// </summary>
    [Test]
    public async Task WrapperOnModelChangedReportsTheHostModelTest()
    {
        using BunitContext ctx = NewContext();
        List<GridModelChangeType> treeChanges = [];
        TreeGridModel<Sized> tree = new([new("root", 1), new("child", 2)]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Sized, string>("Name"), n => n.Name);
        ctx.Render<Treegliaria<Sized>>(ps => ps.Add(g => g.Model, tree).Add(g => g.OnModelChanged, treeChanges.Add));

        List<GridModelChangeType> selChanges = [];
        DataGridModel<Role> model = new([new(1, "Admin", false), new(2, "Editor", false)]) { ItemKeyFn = r => r.Id };
        model.AddColumn("Name", new Column<Role, string>("Name"), r => r.Name);
        var selGrid = ctx.Render<MultiSelGrid<Role>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, new SetSelectionModel<Role>())
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.OnModelChanged, selChanges.Add));

        treeChanges.Clear();
        selChanges.Clear();
        await selGrid.InvokeAsync(() => tree.SetCurrentRow(1));
        await selGrid.InvokeAsync(() => model.SetCurrentRow(1));
        Assert.Multiple(() =>
        {
            Assert.That(treeChanges, Is.EqualTo(new[] { GridModelChangeType.Other }), "Treegliaria");
            Assert.That(selChanges, Is.EqualTo(new[] { GridModelChangeType.Other }), "MultiSelGrid");
        });
    }

    /// <summary>
    /// A host's CellTemplate draws the data cells of a tree grid too; the expansion column keeps its own rendering.
    /// </summary>
    [Test]
    public void TreegliariaHonoursTheHostCellTemplateTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = new([new("root"), new("child")]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Node, string>("Name"), n => n.Name);
        RenderFragment<IGrigliaria<Node>.CellTemplateParam> template = p => builder =>
        {
            builder.OpenElement(0, "td");
            builder.AddAttribute(1, "class", "host-cell");
            builder.AddContent(2, "HOST:" + p.Model.GetValueAt(p.RowIdx, p.ColIdx));
            builder.CloseElement();
        };
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree).Add(g => g.CellTemplate, template));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("td.host-cell").Select(td => td.TextContent), Is.EqualTo(new[] { "HOST:root", "HOST:child" }), "every data cell goes through the host's template");
            Assert.That(cut.FindAll("td.indent"), Has.Count.EqualTo(2), "the expansion column is still the tree's own");
        });
    }

    [Test]
    public void MultiSelGridHonoursTheHostCellTemplateTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(2);
        SetSelectionModel<Product> selection = new();
        RenderFragment<IGrigliaria<Product>.CellTemplateParam> template = p => builder =>
        {
            builder.OpenElement(0, "td");
            builder.AddAttribute(1, "class", "host-cell");
            builder.AddContent(2, p.Model.GetColumnName(p.ColIdx));
            builder.CloseElement();
        };
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.CellTemplate, template));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("td.host-cell").Select(td => td.TextContent), Is.Not.Empty.And.Not.Contains(MultiSelGrid.SelColName),
                "the data cells go through the host's template");
            Assert.That(cut.FindAll("tbody input[type=checkbox]"), Has.Count.EqualTo(2), "the selection column is still the grid's own");
        });
    }

    /// <summary>
    /// Ctrl+Space toggles the current row's selection; with no current row there is nothing to toggle, and the stroke must not
    /// throw out of its handler, which would end the circuit.
    /// </summary>
    [Test]
    public void CtrlSpaceWithNoCurrentRowIsIgnoredTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(0);
        SetSelectionModelWithSelectAll<Product> selection = new(Array.Empty<Product>(), new HashSet<Product>(), defensiveCopy: false);
        var grid = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, selection).Add(g => g.Spinner, new NoSpinner()));
        var stroke = grid.FindComponents<KeyAction>().Single(k => k.Instance.CtrlKey && k.Instance.Key == " ");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(-1));
        Assert.DoesNotThrowAsync(async () => await grid.InvokeAsync(() => stroke.Instance.ProcessAsync(" ", null, null)));
    }

    /// <summary>
    /// The tree's Ctrl+Space expands or collapses the current node: with no current row, or a leaf, it has nothing to do.
    /// </summary>
    [Test]
    public async Task CtrlSpaceOnATreeWithoutAnExpandableRowIsIgnoredTest()
    {
        using BunitContext ctx = NewContext();
        List<(int row, bool expanded)> expansions = [];
        TreeGridModel<Node> tree = new([new("leaf")]) { OnSetExpand = (row, expanded) => { expansions.Add((row, expanded)); return ValueTask.CompletedTask; } };
        tree.AddColumn("Name", new Column<Node, string>("Name"), n => n.Name);
        var grid = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        var stroke = grid.FindComponents<KeyAction>().Single(k => k.Instance.CtrlKey && k.Instance.Key == " ");
        TreeGridModel<Node> empty = new([]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        empty.AddColumn("Name", new Column<Node, string>("Name"), n => n.Name);
        var emptyGrid = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, empty));
        var emptyStroke = emptyGrid.FindComponents<KeyAction>().Single(k => k.Instance.CtrlKey && k.Instance.Key == " ");
        Assert.DoesNotThrowAsync(async () => await emptyGrid.InvokeAsync(() => emptyStroke.Instance.ProcessAsync(" ", null, null)), "no current row");
        await grid.InvokeAsync(() => stroke.Instance.ProcessAsync(" ", null, null));
        Assert.That(expansions, Is.Empty, "a leaf has nothing to expand");
    }

    /// <summary>
    /// Markup columns register on the model the grid has when they are set up; a new model instance must receive them too,
    /// the declared columns and the enrichments of its own columns alike.
    /// </summary>
    [Test]
    public void NewModelInstanceReceivesTheMarkupColumnsTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> NewModel()
        {
            DataGridModel<Product> m = new(Rows(3)) { ItemKeyFn = p => p.Code };
            m.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
            return m;
        }
        RenderFragment<DecoratorContext<Product, string>> enriched = c => b => b.AddContent(0, "ENRICHED:" + c.Value);
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Code");
            builder.AddAttribute(2, "EFProperty", (System.Linq.Expressions.Expression<Func<Product, string>>)(p => p.Code));
            builder.CloseComponent();
            builder.OpenComponent<EnrichGridColumn<Product, string>>(3);
            builder.AddAttribute(4, "Name", "Name");
            builder.AddAttribute(5, "DisplayTemplate", enriched);
            builder.CloseComponent();
        };
        DataGridModel<Product> first = NewModel(), second = NewModel();
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, first).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ChildContent, columns));
        Assert.That(grid.Markup, Does.Contain("C00").And.Contain("ENRICHED:Name00"));
        grid.Render(ps => ps.Add(g => g.Model, second));
        Assert.Multiple(() =>
        {
            Assert.That(second.GetColumnIndex("Code"), Is.GreaterThanOrEqualTo(0), "the declared column");
            Assert.That(grid.Markup, Does.Contain("C00").And.Contain("ENRICHED:Name00"), "both render over the new model");
        });
    }

    /// <summary>
    /// The scrollbar's repeat timer runs while an arrow or the track is held and can call in after the host emptied the model:
    /// scrolling an empty grid has nothing to move to and must not throw out of the callback.
    /// </summary>
    [Test]
    public async Task ScrollToOnAModelEmptiedUnderTheScrollbarIsIgnoredTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var grid = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 2));
        var scroller = grid.FindComponent<Scroller>();
        await grid.InvokeAsync(() => model.GetData().Clear());
        Assert.DoesNotThrowAsync(async () => await grid.InvokeAsync(() => scroller.Instance.ProcessScrollTo(1)));
        Assert.That(model.GetCurrentRow(), Is.EqualTo(-1));
    }

    /// <summary>
    /// Setting the HorzResizer back to null turns horizontal paging off: every column shows again, and the column offset
    /// the header adds to its column indexes goes back to zero.
    /// </summary>
    [Test]
    public async Task RemovingTheHorzResizerTurnsHorizontalPagingOffTest()
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(5, "Extra"), ps => ps.Add(g => g.PageSize, 2).Add(g => g.HorzResizer, GridHorzResizers.Fixed(25))
            .Add(g => g.HCellTemplate, PlacedHeader));
        await grid.InvokeAsync(() => grid.Instance.ScrollBy(1, 0));
        Assert.That(grid.Instance.CanPrevCol(), Is.True, "paged, and moved one column");
        grid.Render(ps => ps.Add(g => g.HorzResizer, (IGridHorzResizer?)null));
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetHorzDisplayedUnits(), Is.EqualTo(-1), "paging off");
            Assert.That(grid.Markup, Does.Contain("Name@1"), "no offset without paging");
            Assert.That(grid.Markup, Does.Contain("Name").And.Contain("Price").And.Contain("Extra"), "every column shows");
        });
    }

    private sealed class SwitchingHorzResizer : IGridHorzResizer
    {
        public int Units = 25;
        public int GetDisplayedUnits<TGridItem>(AGrid<TGridItem> grid, double gridWidth) => Units;
    }

    /// <summary>A HorzResizer that returns -1, unlimited, turns horizontal paging off as a null one does, column offset included.</summary>
    [Test]
    public async Task HorzResizerReturningUnlimitedTurnsHorizontalPagingOffTest()
    {
        using BunitContext ctx = NewContext();
        SwitchingHorzResizer resizer = new();
        var grid = RenderGrid(ctx, ModelOf(5, "Extra"), ps => ps.Add(g => g.PageSize, 2).Add(g => g.HorzResizer, resizer)
            .Add(g => g.HCellTemplate, PlacedHeader));
        await grid.InvokeAsync(() => grid.Instance.ScrollBy(1, 0));
        Assert.That(grid.Instance.CanPrevCol(), Is.True, "paged, and moved one column");
        resizer.Units = -1;   // the window got wide
        grid.Render();
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetHorzDisplayedUnits(), Is.EqualTo(-1), "paging off");
            Assert.That(grid.Markup, Does.Contain("Name@1"), "no offset without paging");
        });
    }

    private static RenderFragment Enrichments(params (RenderFragment<HDecoratorContext<Product>>? header, RenderFragment<DecoratorContext<Product, string>>? cell)[] enrichments) => builder =>
    {
        int seq = 0;
        foreach (var (header, cell) in enrichments)
        {
            builder.OpenComponent<EnrichGridColumn<Product, string>>(seq++);
            builder.AddAttribute(seq++, "Name", "Name");
            if (header != null)
                builder.AddAttribute(seq++, "HeaderDisplayTemplate", header);
            if (cell != null)
                builder.AddAttribute(seq++, "DisplayTemplate", cell);
            builder.CloseComponent();
        }
    };

    /// <summary>
    /// An enrichment adds the templates it is given and keeps the column's others: a header template must not drop the cell
    /// renderer, on a typed model column and on a column an earlier enrichment produced.
    /// </summary>
    [Test]
    public void EnrichmentKeepsTheTemplatesItIsNotGivenTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment<HDecoratorContext<Product>> header = h => b => b.AddContent(0, "HEAD:" + h.Column.GetLabel());
        RenderFragment<DecoratorContext<Product, string>> cell = c => b => b.AddContent(0, "CELL:" + c.Value);

        DataGridModel<Product> typed = new(Rows(1)) { ItemKeyFn = p => p.Code };
        typed.AddColumn("Name", new Column<Product, string>("Name") { Renderer = cell }, p => p.Name);
        var first = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, typed).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, Enrichments((header, null))));

        DataGridModel<Product> plain = new(Rows(1)) { ItemKeyFn = p => p.Code };
        plain.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        var second = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, plain).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, Enrichments((null, cell), (header, null))));

        Assert.Multiple(() =>
        {
            Assert.That(first.Markup, Does.Contain("HEAD:Name").And.Contain("CELL:Name00"), "typed column, header added");
            Assert.That(second.Markup, Does.Contain("HEAD:Name").And.Contain("CELL:Name00"), "enriched column, header added after the cell");
        });
    }

    /// <summary>
    /// A markup column registers again on every render when one of its parameters is new each time, a lambda capturing a loop
    /// variable or a FilterDef created in the markup. That replaces the model's column, so the enrichments after it must be
    /// applied again.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void EnrichmentsSurviveAColumnThatRegistersAgainTest(bool newFilterDef)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(3)) { ItemKeyFn = p => p.Code };
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        RenderFragment<HDecoratorContext<Product>> header = h => b => b.AddContent(0, "HEAD:" + h.Column.GetLabel());
        RenderFragment<DecoratorContext<Product, string>> cell = c => b => b.AddContent(0, "CELL:" + c.Value);
        RenderFragment columns = builder =>
        {
            string suffix = "";   // a new closure on every render, as a loop variable is
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Name");
            builder.AddAttribute(2, "Label", "Name");
            builder.AddAttribute(3, "Property", newFilterDef ? (Func<Product, string>)(p => p.Name) : p => p.Name + suffix);
            if (newFilterDef)
                builder.AddAttribute(4, "FilterDef", new DefaultDef());
            builder.CloseComponent();
            builder.AddContent(5, Enrichments((null, cell), (header, null)));
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        Assert.That(grid.Markup, Does.Contain("HEAD:Name").And.Contain("CELL:Name00"), "the first render");
        grid.Render();
        Assert.That(grid.Markup, Does.Contain("HEAD:Name").And.Contain("CELL:Name00"), "after the column registered again");
    }

    /// <summary>
    /// MultiSelGrid enriches its selection column after the host's content, so the column shows its checkboxes on every render
    /// whatever the host enriches it with.
    /// </summary>
    [Test]
    public void SelectionColumnKeepsItsCheckboxesUnderAHostEnrichmentTest()
    {
        using BunitContext ctx = NewContext();
        Product[] rows = Rows(3);
        DataGridModel<Product> model = ModelOf(0);
        foreach (Product row in rows)
            model.GetData().Add(row);
        SetSelectionModelWithSelectAll<Product> selection = new(rows, new HashSet<Product>(), defensiveCopy: false);
        RenderFragment<HDecoratorContext<Product>> hostHeader = h => b => b.AddContent(0, "HOSTHEAD");
        RenderFragment<DecoratorContext<Product, bool>> hostCell = c => b => b.AddContent(0, "HOSTCELL");
        RenderFragment content = builder =>
        {
            builder.OpenComponent<EnrichGridColumn<Product, bool>>(0);
            builder.AddAttribute(1, "Name", MultiSelGrid.SelColName);
            builder.AddAttribute(2, "HeaderDisplayTemplate", hostHeader);
            builder.AddAttribute(3, "DisplayTemplate", hostCell);
            builder.CloseComponent();
        };
        var grid = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.ChildContent, content));
        for (int render = 1; render <= 2; render++)
        {
            Assert.That(grid.Markup, Does.Not.Contain("HOSTHEAD").And.Not.Contain("HOSTCELL"), $"render {render}");
            Assert.That(grid.FindAll("input[type=checkbox]"), Has.Count.EqualTo(rows.Length + 1), "a checkbox per row, and select-all");
            grid.Render();
        }
    }

    /// <summary>
    /// An enrichment whose VT is not the column's is applied again on every render by a column that registers again, by a
    /// second enrichment, or by a template that is new each time. Each time must leave the column as it was, or a restored
    /// layout is re-applied on every render and the grid never settles.
    /// </summary>
    [TestCase("re-registering column")]
    [TestCase("two enrichments")]
    [TestCase("capturing template")]
    public async Task MismatchedEnrichmentLetsARestoredGridSettleTest(string setup)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(3)) { ItemKeyFn = p => p.Code };
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        if (setup == "two enrichments")
            model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        RenderFragment<DecoratorContext<Product, decimal>> priceCell = c => b => b.AddContent(0, "CELL:" + c.Value);
        RenderFragment<DecoratorContext<Product, object>> anyCell = c => b => b.AddContent(0, "CELL:" + c.Value);
        RenderFragment<HDecoratorContext<Product>> header = h => b => b.AddContent(0, "HEAD:" + h.Column.GetLabel());
        RenderFragment columns = builder =>
        {
            string suffix = "";   // a new closure on every render
            switch (setup)
            {
                case "re-registering column":
                    builder.OpenComponent<GridColumn<Product, decimal?>>(0);
                    builder.AddAttribute(1, "Name", "Price");
                    builder.AddAttribute(2, "Label", "Price");
                    builder.AddAttribute(3, "Property", (Func<Product, decimal?>)(p => suffix == "" ? p.Price : null));
                    builder.CloseComponent();
                    builder.OpenComponent<EnrichGridColumn<Product, decimal>>(4);
                    builder.AddAttribute(5, "Name", "Price");
                    builder.AddAttribute(6, "DisplayTemplate", priceCell);
                    builder.CloseComponent();
                    break;
                case "two enrichments":
                    builder.OpenComponent<EnrichGridColumn<Product, object>>(10);
                    builder.AddAttribute(11, "Name", "Name");
                    builder.AddAttribute(12, "DisplayTemplate", anyCell);
                    builder.CloseComponent();
                    builder.OpenComponent<EnrichGridColumn<Product, string>>(13);
                    builder.AddAttribute(14, "Name", "Name");
                    builder.AddAttribute(15, "HeaderDisplayTemplate", header);
                    builder.CloseComponent();
                    break;
                case "capturing template":
                    builder.OpenComponent<GridColumn<Product, string>>(20);
                    builder.AddAttribute(21, "Name", "Name");
                    builder.AddAttribute(22, "Label", "Name");
                    builder.AddAttribute(23, "Property", (Func<Product, string>)(p => p.Name + suffix));
                    builder.CloseComponent();
                    builder.OpenComponent<EnrichGridColumn<Product, object>>(24);
                    builder.AddAttribute(25, "Name", "Name");
                    builder.AddAttribute(26, "DisplayTemplate", (RenderFragment<DecoratorContext<Product, object>>)(c => b => b.AddContent(0, "CELL:" + c.Value + suffix)));
                    builder.CloseComponent();
                    break;
            }
        };
        int layoutChanges = 0;
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.ChildContent, columns)
            .Add(g => g.OnLayoutChanged, () => { if (++layoutChanges > 20) throw new InvalidOperationException("the layout is re-applied on every render"); }));
        await grid.InvokeAsync(() => grid.Instance.RestoreMemento(grid.Instance.CreateMemento()));
        int version = model.GetColumnsVersion();
        grid.Render();
        grid.Render();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetColumnsVersion(), Is.EqualTo(version), "applying the enrichments again leaves the columns as they were");
            Assert.That(layoutChanges, Is.LessThanOrEqualTo(2));
            Assert.That(grid.Markup, Does.Contain("CELL:"));
        });
    }

    /// <summary>
    /// An enrichment only attaches templates: the column keeps its value type, which picks its filter and its sort comparer,
    /// whatever VT the enrichment declares.
    /// </summary>
    [Test]
    public void EnrichmentKeepsTheColumnsValueTypeTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(2);
        RenderFragment<DecoratorContext<Product, object>> cell = c => b => b.AddContent(0, "CELL:" + c.Value);
        RenderFragment content = builder =>
        {
            builder.OpenComponent<EnrichGridColumn<Product, object>>(0);
            builder.AddAttribute(1, "Name", "Price");
            builder.AddAttribute(2, "DisplayTemplate", cell);
            builder.CloseComponent();
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, content));
        Assert.Multiple(() =>
        {
            Assert.That(grid.Markup, Does.Contain("CELL:1.5").Or.Contain("CELL:1,5"));
            Assert.That(model.GetColumn(model.GetColumnIndex("Price")).GetValueType(), Is.EqualTo(typeof(decimal)));
        });
    }

    private static void NameColumn(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder, int seq, Func<Product, string> property)
    {
        builder.OpenComponent<GridColumn<Product, string>>(seq);
        builder.AddAttribute(seq + 1, "Name", "Name");
        builder.AddAttribute(seq + 2, "Label", "Name");
        builder.AddAttribute(seq + 3, "Property", property);
        builder.CloseComponent();
    }

    private static DataGridModel<Product> CodeOnlyModel()
    {
        DataGridModel<Product> model = new(Rows(3)) { ItemKeyFn = p => p.Code };
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        return model;
    }

    /// <summary>
    /// MultiSelGrid's selection column and Treegliaria's expand column are made by the grid itself and cannot be removed. The
    /// only way a removal reaches them is a GridColumn given their reserved name that later leaves the tree, and that throws.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void BuiltInColumnCannotLeaveWithItsGridColumnTest(bool multiSelGrid)
    {
        using BunitContext ctx = NewContext();
        bool declared = true;
        InvalidOperationException? thrown;
        if (multiSelGrid)
        {
            Product[] rows = Rows(3);
            DataGridModel<Product> model = ModelOf(0);
            foreach (Product row in rows)
                model.GetData().Add(row);
            RenderFragment content = builder =>
            {
                if (!declared)
                    return;
                builder.OpenComponent<GridColumn<Product, bool>>(0);
                builder.AddAttribute(1, "Name", MultiSelGrid.SelColName);
                builder.AddAttribute(2, "Label", "Pick");
                builder.AddAttribute(3, "Property", (Func<Product, bool>)(p => false));
                builder.CloseComponent();
            };
            var grid = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model)
                .Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Product>(rows))
                .Add(g => g.Spinner, new NoSpinner()).Add(g => g.ChildContent, content));
            declared = false;
            thrown = Assert.Throws<InvalidOperationException>(() => grid.Render());
        }
        else
        {
            TreeGridModel<Node> tree = new([new("root")]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
            tree.AddColumn("Name", new Column<Node, string>("Name"), n => n.Name);
            RenderFragment content = builder =>
            {
                if (!declared)
                    return;
                builder.OpenComponent<GridColumn<Node, bool>>(0);
                builder.AddAttribute(1, "Name", Treegliaria.ExpColName);
                builder.AddAttribute(2, "Label", "Open");
                builder.AddAttribute(3, "Property", (Func<Node, bool>)(n => false));
                builder.CloseComponent();
            };
            var grid = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree).Add(g => g.ChildContent, content));
            declared = false;
            thrown = Assert.Throws<InvalidOperationException>(() => grid.Render());
        }
        Assert.That(thrown!.Message, Does.Contain(multiSelGrid ? MultiSelGrid.SelColName : Treegliaria.ExpColName));
    }

    /// <summary>
    /// A wrapper grid's filter, column and stable-item methods act on its inner grid: what one sets the other reads.
    /// </summary>
    private static async Task AssertForwardsAsync<THost, TItem>(IRenderedComponent<THost> host, Grigliaria<TItem> inner, string column,
        Action<string, AFilterDef?> setFilter, Func<string, AFilterDef?> getFilter, Func<string, bool> hasFilter, Func<bool> isFiltered,
        Func<IReadOnlyDictionary<string, AFilterDef>> getFilters, Action<IReadOnlyDictionary<string, AFilterDef>> setFilters,
        Action<string[]> setColumnsToShow, Func<string, bool> ensureColumnIsShown, Func<Task> stabilize, Func<TItem?> getStableItem) where THost : IComponent
    {
        await host.InvokeAsync(() => setFilter(column, new DefaultDef { Value = "1" }));
        Assert.Multiple(() =>
        {
            Assert.That(inner.GetFilter(column), Is.TypeOf<DefaultDef>(), "set on the inner grid");
            Assert.That(getFilter(column), Is.SameAs(inner.GetFilter(column)));
            Assert.That(hasFilter(column), Is.True);
            Assert.That(isFiltered(), Is.True);
            Assert.That(getFilters().Keys, Is.EqualTo(new[] { column }));
        });
        await host.InvokeAsync(() => setFilters(new Dictionary<string, AFilterDef>()));
        Assert.Multiple(() =>
        {
            Assert.That(inner.HasFilter(column), Is.False, "cleared on the inner grid");
            Assert.That(isFiltered(), Is.False);
        });

        await host.InvokeAsync(() => setColumnsToShow([column]));
        Assert.Multiple(() =>
        {
            Assert.That(inner.GetColumnsToShow(), Does.Contain(column).And.Not.Contain("Other"), "shown by the inner grid");
            Assert.That(ensureColumnIsShown(column), Is.EqualTo(inner.EnsureColumnIsShown(column)));
        });

        await host.InvokeAsync(() => inner.Model.SetCurrentRow(1));
        await host.InvokeAsync(stabilize);
        Assert.That(getStableItem(), Is.EqualTo(inner.GetStableItem()).And.Not.Null);
    }

    [Test]
    public async Task MultiSelGridForwardsToItsGridTest()
    {
        using BunitContext ctx = NewContext();
        Product[] rows = Rows(3);
        DataGridModel<Product> model = ModelOf(0, "Other");
        foreach (Product row in rows)
            model.GetData().Add(row);
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model)
            .Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Product>(rows)).Add(g => g.Spinner, new NoSpinner()));
        MultiSelGrid<Product> grid = cut.Instance;
        await AssertForwardsAsync(cut, cut.FindComponent<Grigliaria<Product>>().Instance, "Name",
            grid.SetFilter, grid.GetFilter, grid.HasFilter, grid.IsFiltered, grid.GetFilters, grid.SetFilters,
            c => grid.SetColumnsToShow(c), grid.EnsureColumnIsShown, grid.StabilizeItemAsync, grid.GetStableItem);
    }

    [Test]
    public async Task TreegliariaForwardsToItsGridTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = new([new("a1"), new("b1"), new("c1")]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Node, string>("Name"), n => n.Name);
        tree.AddColumn("Other", new Column<Node, string>("Other"), n => "x");
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        Treegliaria<Node> grid = cut.Instance;
        await AssertForwardsAsync(cut, cut.FindComponent<Grigliaria<Node>>().Instance, "Name",
            grid.SetFilter, grid.GetFilter, grid.HasFilter, grid.IsFiltered, grid.GetFilters, grid.SetFilters,
            c => grid.SetColumnsToShow(c), grid.EnsureColumnIsShown, grid.StabilizeItemAsync, grid.GetStableItem);
    }

    /// <summary>
    /// A markup column follows the @if around it: turned off it leaves the model and the grid, turned on again it comes back.
    /// </summary>
    [Test]
    public void MarkupColumnTurnedOffByAnIfLeavesTheGridTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = CodeOnlyModel();
        bool showName = true;
        RenderFragment columns = builder =>
        {
            if (showName)
                NameColumn(builder, 0, p => p.Name);
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        Assert.That(grid.Markup, Does.Contain("Name00"));
        showName = false;
        grid.Render();
        grid.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(model.GetColumnIndex("Name"), Is.LessThan(0), "the column left the model");
            Assert.That(grid.Markup, Does.Not.Contain("Name00"));
        }));
        showName = true;
        grid.Render();
        Assert.That(grid.Markup, Does.Contain("Name00"), "and comes back with the @if");
    }

    /// <summary>
    /// Columns written with @foreach: without @key Blazor matches the components by position, so removing the first column
    /// renames each component to the next one and disposes the last. Either way the first column leaves the grid, with the
    /// name given, or with @key when it comes from the label.
    /// </summary>
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ColumnRemovedFromAForeachLeavesTheGridTest(bool withKey, bool withName)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(3)) { ItemKeyFn = p => p.Code };
        List<(string Name, Func<Product, string> Property)> columnList =
            [("Code", p => p.Code), ("Name", p => p.Name), ("Upper", p => p.Name.ToUpperInvariant())];
        RenderFragment columns = builder =>
        {
            foreach (var (name, property) in columnList)
            {
                builder.OpenComponent<GridColumn<Product, string>>(0);
                if (withKey)
                    builder.SetKey(name);
                if (withName)
                    builder.AddAttribute(1, "Name", name);
                builder.AddAttribute(2, "Label", name);
                builder.AddAttribute(3, "Property", property);
                builder.CloseComponent();
            }
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Name", "Upper" }));
        columnList.RemoveAt(0);
        grid.Render();
        grid.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(model.GetColumnIndex("Code"), Is.LessThan(0), "the column left the model");
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Name", "Upper" }));
        }));
    }

    /// <summary>
    /// A column with no Name is named after its first label: a later label changes its header, and the column keeps its
    /// filter, sort, size and place.
    /// </summary>
    [Test]
    public async Task LabelChangeKeepsTheColumnTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = CodeOnlyModel();
        string label = "Name (it)";
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Label", label);
            builder.AddAttribute(2, "Property", (Func<Product, string>)(p => p.Name));
            builder.CloseComponent();
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.ChildContent, columns));
        await grid.InvokeAsync(() =>
        {
            grid.Instance.SetFilter("Name (it)", new DefaultDef { Value = "Name01" });
            model.SetSortedColumn("Name (it)", false);
            grid.Instance.SetColumnsToShow("Name (it)", "Code");
            grid.Instance.SetPreferredSize("Name (it)", 33);
        });
        label = "Name (en)";
        grid.Render();
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Name (it)", "Code" }));
            Assert.That(grid.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name (it)" }));
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
            Assert.That(model.GetSortedColumn(), Is.EqualTo("Name (it)"));
            Assert.That(grid.Instance.GetColumn(model.GetColumnIndex("Name (it)")).GetPreferredSize(), Is.EqualTo(33));
            Assert.That(grid.Markup, Does.Contain("Name (en)").And.Not.Contain("Name (it)"), "the header follows the label");
        });
    }

    /// <summary>
    /// A column's Name may be omitted, and then its first label is its name. One that is passed must be a name: null or empty is
    /// refused at once, rather than read as omitted at every render, which would rename the column when its label changes.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    public void NamePassedWithNoValueIsRefusedTest(string? name)
    {
        using BunitContext ctx = NewContext();
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", name);
            builder.AddAttribute(2, "Label", "Name (it)");
            builder.AddAttribute(3, "Property", (Func<Product, string>)(p => p.Name));
            builder.CloseComponent();
        };
        var thrown = Assert.Throws<ArgumentException>(() => ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, CodeOnlyModel())
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.ChildContent, columns)));
        Assert.That(thrown!.Message, Does.Contain("Name").And.Contain("Name (it)"));
    }

    /// <summary>
    /// A reset goes back to the filters the grid started with, and one of them can be on a markup column an @if has taken out
    /// since: it is set aside, as a restored memento's is, and applied when the column comes back.
    /// </summary>
    [Test]
    public async Task ResetWithAStartingFilterOnAColumnAnIfTookOutTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = CodeOnlyModel();   // kept by the host across page visits
        bool showName = true;
        RenderFragment columns = builder =>
        {
            if (showName)
                NameColumn(builder, 0, p => p.Name);
        };
        var firstVisit = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        await firstVisit.InvokeAsync(() => firstVisit.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        await ctx.DisposeComponentsAsync();
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        Assert.That(model.GetRowCount(), Is.EqualTo(1), "the grid starts filtered");
        showName = false;
        grid.Render();
        grid.WaitForAssertion(() => Assert.That(model.GetColumnIndex("Name"), Is.LessThan(0)));
        Assert.DoesNotThrowAsync(async () =>
        {
            await grid.InvokeAsync(() => grid.Instance.RestoreMemento(null));
            grid.Render();
        });
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetFilters(), Is.Empty);
            Assert.That(model.GetRowCount(), Is.EqualTo(3));
        });
        showName = true;
        grid.Render();
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }), "back with its column");
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// An @if that swaps a markup column for another of the same name, in one render, keeps the column: the one that left no
    /// longer owns it.
    /// </summary>
    [Test]
    public void MarkupColumnSwappedForOneOfTheSameNameStaysTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = CodeOnlyModel();
        bool second = false;
        RenderFragment columns = builder =>
        {
            if (second)
                NameColumn(builder, 10, p => p.Name + "!");
            else
                NameColumn(builder, 0, p => p.Name);
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        second = true;
        grid.Render();
        grid.Render();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetColumnIndex("Name"), Is.GreaterThanOrEqualTo(0));
            Assert.That(grid.Markup, Does.Contain("Name00!"));
        });
    }

    /// <summary>
    /// Tearing a grid down disposes its markup columns too, and a model the host keeps must still have them, with their
    /// filters: the grid removes a column that left only while it goes on rendering.
    /// </summary>
    [Test]
    public async Task GridTornDownLeavesItsModelsColumnsTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = CodeOnlyModel();
        RenderFragment columns = builder => NameColumn(builder, 0, p => p.Name);
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        await grid.InvokeAsync(() => grid.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        await ctx.DisposeComponentsAsync();
        await Task.Yield();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetColumnIndex("Name"), Is.GreaterThanOrEqualTo(0));
            Assert.That(model.GetFilters().Keys, Is.EqualTo(new[] { "Name" }));
        });
    }

    /// <summary>
    /// A markup column that leaves while an edit holds the model is removed at the first render after the edit ends: the model
    /// refuses changes meanwhile, and the grid must not throw.
    /// </summary>
    [Test]
    public async Task MarkupColumnLeavingDuringAnEditGoesWhenTheEditEndsTest()
    {
        using BunitContext ctx = NewContext();
        EditableDataGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(Rows(3));
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        bool showName = true;
        RenderFragment columns = builder =>
        {
            if (showName)
                NameColumn(builder, 0, p => p.Name);
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        await grid.InvokeAsync(() => model.SetCurrentRow(1));
        await grid.InvokeAsync(() => model.EditAsync().AsTask());
        showName = false;
        Assert.DoesNotThrow(() => grid.Render());
        Assert.That(model.GetColumnIndex("Name"), Is.GreaterThanOrEqualTo(0), "kept while the edit holds the model");
        await grid.InvokeAsync(() => model.CancelAsync().AsTask());
        grid.Render();
        Assert.That(model.GetColumnIndex("Name"), Is.LessThan(0));
    }

    /// <summary>
    /// While a row is being edited the grid leaves the navigation keys to an editor in a cell, whose caret they move; the frozen
    /// row would not move anyway. Ctrl+←/→ too, which jump a word there. They come back when the edit ends.
    /// </summary>
    [Test]
    public async Task NavigationKeysAreLeftToAnEditorWhileARowIsEditedTest()
    {
        using BunitContext ctx = NewContext();
        EditableDataGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(Rows(3));
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        var grid = RenderGrid(ctx, model, ps => ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(25)));   // Ctrl+←/→ are the grid's only while it scrolls its columns
        string[] navigation = ["Home", "End", "PageUp", "PageDown", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"];
        List<bool> Enabled() => grid.FindComponents<KeyAction>().Where(k => navigation.Contains(k.Instance.Key))
            .Select(k => k.Instance.Enabled).ToList();
        Assert.That(Enabled(), Has.Count.EqualTo(10), "Ctrl+ArrowLeft and Ctrl+ArrowRight included");
        Assert.That(Enabled(), Is.All.True);
        await grid.InvokeAsync(() => model.EditAsync().AsTask());
        grid.Render();
        Assert.That(Enabled(), Is.All.False, "while the row is edited");
        await grid.InvokeAsync(() => model.CancelAsync().AsTask());
        grid.Render();
        Assert.That(Enabled(), Is.All.True, "after the edit");
    }

    /// <summary>
    /// While a row is being edited the grid leaves the wheel to the page, so an editor in a cell scrolls; the frozen row would
    /// not move anyway. The wheel comes back when the edit ends.
    /// </summary>
    [Test]
    public async Task WheelIsLeftToAnEditorWhileARowIsEditedTest()
    {
        using BunitContext ctx = NewContext();
        EditableDataGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(Rows(3));
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        var grid = RenderGrid(ctx, model);
        List<bool> Enabled() => grid.FindComponents<MouseAction>().Where(m => m.Instance.Type == MouseAction.MouseType.Wheel)
            .Select(m => m.Instance.Enabled).ToList();
        Assert.That(Enabled(), Has.Count.EqualTo(2));
        Assert.That(Enabled(), Is.All.True);
        await grid.InvokeAsync(() => model.EditAsync().AsTask());
        grid.Render();
        Assert.That(Enabled(), Is.All.False, "while the row is edited");
        await grid.InvokeAsync(() => model.CancelAsync().AsTask());
        grid.Render();
        Assert.That(Enabled(), Is.All.True, "after the edit");
    }

    private static DataGridModel<Product> ModelOfSizes(params (string name, int size)[] columns)
    {
        DataGridModel<Product> model = new(Rows(2));
        foreach (var (name, size) in columns)
            model.AddColumn(name, new Column<Product, string>(name) { PreferredSize = size }, p => name);
        return model;
    }

    /// <summary>
    /// A first scrolling column wider than the displayed units is shown anyway, and the columns after it must still be
    /// reachable: column-right stays enabled, and moving onto a second oversized column shows that one.
    /// </summary>
    [Test]
    public async Task ColumnsAfterAnOversizedFirstColumnStayReachableTest()
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOfSizes(("Wide", 30), ("Wider", 30), ("Narrow", 10)), ps => ps.Add(g => g.PageSize, 2).Add(g => g.HorzResizer, GridHorzResizers.Fixed(20)));
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Wide" }), "shown although it does not fit");
            Assert.That(grid.Instance.CanNextCol(), Is.True, "more columns exist");
        });
        await grid.InvokeAsync(() => grid.Instance.NextCol());
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Wider" }), "the next oversized column, not the first again");
        await grid.InvokeAsync(() => grid.Instance.NextCol());
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Narrow" }));
    }

    /// <summary>
    /// A last column wider than the displayed units, shown anyway, is the end: column-right turns off and the horizontal
    /// scroller's thumb covers it, at the end of its track.
    /// </summary>
    [Test]
    public async Task AnOversizedLastColumnEndsTheColumnsTest()
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOfSizes(("Narrow", 10), ("Wide", 30)), ps => ps.Add(g => g.PageSize, 2).Add(g => g.HorzResizer, GridHorzResizers.Fixed(20)));
        Assert.That(grid.Instance.CanNextCol(), Is.True);
        await grid.InvokeAsync(() => grid.Instance.NextCol());
        var scroller = grid.FindComponents<Scroller>().Single(s => s.Instance.Orientation == Scroller.OrientationType.Horizontal);
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Wide" }), "shown although it does not fit");
            Assert.That(grid.Instance.CanNextCol(), Is.False, "no column after it");
            Assert.That(scroller.Instance.PageSize, Is.EqualTo(1));
            Assert.That(scroller.Instance.PageIdx + scroller.Instance.PageSize, Is.EqualTo(scroller.Instance.RowCount), "the thumb at the end");
        });
    }

    /// <summary>
    /// With the cell cursor enabled the first render puts it on the first column that is not fixed, with or without horizontal
    /// paging, so ArrowRight then moves it to the next column.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CellCursorStartsOnTheFirstScrollingColumnTest(bool withHorzResizer)
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(3), ps =>
        {
            ps.Add(g => g.PageSize, 2).Add(g => g.CellCursorEnabled, true);
            if (withHorzResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(30));
        });
        Assert.That(grid.Instance.GetCurrentCol(), Is.EqualTo("Name"));
        var right = grid.FindComponents<KeyAction>().Single(k => !k.Instance.CtrlKey && k.Instance.Key == "ArrowRight");
        await grid.InvokeAsync(() => right.Instance.ProcessAsync(null, "ArrowRight", null));
        Assert.That(grid.Instance.GetCurrentCol(), Is.EqualTo("Price"));
    }

    /// <summary>
    /// The cell cursor does not rest on a fixed column, with or without horizontal paging: moved onto one, the next render puts
    /// it back on the first column that is not fixed.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CellCursorDoesNotRestOnAFixedColumnTest(bool withHorzResizer)
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(3), ps =>
        {
            ps.Add(g => g.PageSize, 2);
            if (withHorzResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(30));
        });
        await grid.InvokeAsync(() => grid.Instance.SetCurrentCol("Code"));   // Code is fixed
        Assert.That(grid.Instance.GetCurrentCol(), Is.EqualTo("Name"));
    }

    /// <summary>
    /// The methods that change what columns are shown, or how wide, render the grid themselves, as SetColumnsToShow and
    /// ScrollToCol do: a host calling them from its own handler sees the change at once.
    /// </summary>
    [Test]
    public async Task ColumnMethodsRenderTheGridTest()
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(3, "Extra"), ps => ps.Add(g => g.Layoutable, true).Add(g => g.HorzResizer, GridHorzResizers.Fixed(20)));
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Name" }));
        Assert.That(await grid.InvokeAsync(() => grid.Instance.EnsureColumnIsShown("Price")), Is.False, "not shown yet");
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Price" }), "EnsureColumnIsShown");
        Assert.That(await grid.InvokeAsync(() => grid.Instance.EnsureColumnIsShown(grid.Instance.GetModel().GetColumnIndex("Extra"))), Is.False);
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Extra" }), "EnsureColumnIsShown by index");
        int renders = grid.RenderCount;
        await grid.InvokeAsync(() => grid.Instance.SetPreferredSize("Extra", 15));
        Assert.That(grid.RenderCount, Is.GreaterThan(renders), "SetPreferredSize");
    }

    /// <summary>
    /// ScrollToCol puts the scrolling column at the offset given first beside the fixed ones; without a HorzResizer every
    /// column is shown and it changes nothing.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ScrollToColMovesTheScrollingWindowTest(bool withHorzResizer)
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(3, "Extra"), ps =>
        {
            if (withHorzResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(20));
        });
        IEnumerable<string> before = grid.Instance.GetShownColumns().ToList();
        await grid.InvokeAsync(() => grid.Instance.ScrollToCol(2));
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(withHorzResizer ? new[] { "Code", "Extra" } : before));
        await grid.InvokeAsync(() => grid.Instance.ScrollToCol(1));
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(withHorzResizer ? new[] { "Code", "Price" } : before));
    }

    /// <summary>
    /// The cell cursor does not rest on a column no longer shown, with or without horizontal paging: hidden by the layout, the
    /// next render puts it on the first column that is not fixed.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CellCursorLeavesAColumnNoLongerShownTest(bool withHorzResizer)
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(3), ps =>
        {
            ps.Add(g => g.PageSize, 2).Add(g => g.Layoutable, true);
            if (withHorzResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(30));
        });
        await grid.InvokeAsync(() => grid.Instance.SetCurrentCol("Price"));
        Assert.That(grid.Instance.GetCurrentCol(), Is.EqualTo("Price"));
        await grid.InvokeAsync(() => grid.Instance.SetColumnsToShow("Code", "Name"));
        Assert.That(grid.Instance.GetCurrentCol(), Is.EqualTo("Name"));
    }

    /// <summary>
    /// With no column fixed or shown by default the grid renders empty, with or without horizontal paging, the arrow keys have
    /// no column to move to, and the columns set later show.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task GridWithNoColumnToShowRendersEmptyTest(bool withHorzResizer)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(3));
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10, ShownByDefault = false }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, ShownByDefault = false }, p => p.Name);
        IRenderedComponent<Grigliaria<Product>>? grid = null;
        Assert.DoesNotThrow(() => grid = RenderGrid(ctx, model, ps =>
        {
            if (withHorzResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(30));
        }));
        Assert.That(grid!.Instance.GetShownColumns(), Is.Empty);
        foreach (var stroke in grid.FindComponents<KeyAction>().Where(k => !k.Instance.CtrlKey && k.Instance.Key is "ArrowLeft" or "ArrowRight"))
            Assert.DoesNotThrowAsync(async () => await grid.InvokeAsync(() => stroke.Instance.ProcessAsync(stroke.Instance.Key, null, null)), stroke.Instance.Key);
        await grid.InvokeAsync(() => grid.Instance.SetColumnsToShow("Name"));
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Name" }));
            Assert.That(grid.Markup, Does.Contain("Name01"));
        });
    }

    /// <summary>
    /// With RowsPopupEnabled a right click on a row raises OnRowPopup with its index, and on a filler row below the last with null,
    /// each with where the pointer is.
    /// </summary>
    [Test]
    public async Task RowPopupGivesTheRowOrNullBelowTheLastTest()
    {
        using BunitContext ctx = NewContext();
        List<RowPopupEvent> popups = [];
        var cut = RenderGrid(ctx, ModelOf(2), ps => ps.Add(g => g.PageSize, 4).Add(g => g.RowsPopupEnabled, true)
            .Add(g => g.OnRowPopup, (RowPopupEvent e) => popups.Add(e)));
        static bool IsMenu(IRenderedComponent<MouseAction> m) => m.Instance.Type == MouseAction.MouseType.ContextMenu;
        foreach (var menu in cut.FindComponents<DefaultRowHeading<Product>>().SelectMany(h => h.FindComponents<MouseAction>()).Where(IsMenu))
            await cut.InvokeAsync(() => menu.Instance.ProcessAsync(null, null, 120, 45, 0));
        Assert.That(popups, Is.EqualTo(new RowPopupEvent[] { new(0, 120, 45), new(1, 120, 45) }), "the rows");
        popups.Clear();
        foreach (var menu in cut.FindComponents<DefaultEmptyRow<Product>>().SelectMany(h => h.FindComponents<MouseAction>()).Where(IsMenu))
            await cut.InvokeAsync(() => menu.Instance.ProcessAsync(null, null, 7, 9, 0));
        Assert.That(popups, Is.Not.Empty.And.All.EqualTo(new RowPopupEvent(null, 7, 9)), "the filler rows");
    }

    /// <summary>A double-click on the row heading raises OnDblClick for the row, with no column, with or without the cell cursor.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task RowHeadingDblClickGivesTheRowTest(bool cellCursor)
    {
        using BunitContext ctx = NewContext();
        List<DblClickEvent> clicks = [];
        var cut = RenderGrid(ctx, ModelOf(2), ps => ps.Add(g => g.CellCursorEnabled, cellCursor)
            .Add(g => g.OnDblClick, EventCallback.Factory.Create<DblClickEvent>(this, e => clicks.Add(e))));
        foreach (var dbl in cut.FindComponents<DefaultRowHeading<Product>>().SelectMany(h => h.FindComponents<MouseAction>())
                     .Where(m => m.Instance.Type == MouseAction.MouseType.DblClick))
            await cut.InvokeAsync(() => dbl.Instance.ProcessAsync(null, null, 0, 0, 0));
        Assert.That(clicks, Is.EqualTo(new[] { new DblClickEvent(0, null), new DblClickEvent(1, null) }));
    }

    [Test]
    public async Task HasFilterTellsTheColumnsFilteredTest()
    {
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, ModelOf(3));
        Assert.That(grid.Instance.HasFilter("Name"), Is.False);
        await grid.InvokeAsync(() => grid.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.HasFilter("Name"), Is.True);
            Assert.That(grid.Instance.HasFilter("Price"), Is.False);
        });
        await grid.InvokeAsync(() => grid.Instance.SetFilter("Name", null));
        Assert.That(grid.Instance.HasFilter("Name"), Is.False, "cleared");
    }

    /// <summary>A column's HeaderLinesToClamp clamps its header to that many lines; by default one, with an ellipsis.</summary>
    [Test]
    public void HeaderLinesToClampClampsTheHeaderTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment columns = b =>
        {
            b.OpenComponent<GridColumn<Product, string>>(0);
            b.AddAttribute(1, "Name", "Code");
            b.AddAttribute(2, "Label", "Code");
            b.AddAttribute(3, "Property", (Func<Product, string>)(p => p.Code));
            b.CloseComponent();
            b.OpenComponent<GridColumn<Product, string>>(4);
            b.AddAttribute(5, "Name", "Name");
            b.AddAttribute(6, "Label", "Name");
            b.AddAttribute(7, "Property", (Func<Product, string>)(p => p.Name));
            b.AddAttribute(8, "HeaderLinesToClamp", 3);
            b.CloseComponent();
        };
        var grid = RenderGrid(ctx, new DataGridModel<Product>(Rows(2)), ps => ps.Add(g => g.ChildContent, columns));
        var code = grid.FindAll("th").Single(th => th.TextContent.Contains("Code"));
        var name = grid.FindAll("th").Single(th => th.TextContent.Contains("Name"));
        Assert.Multiple(() =>
        {
            Assert.That(code.QuerySelector(".clamper .ellipsis"), Is.Not.Null);
            Assert.That(name.QuerySelector(".clamper .line-clamp")?.GetAttribute("style"), Is.EqualTo("--lines-to-clamp:3"));
        });
    }

    /// <summary>MultiSelCellTemplate draws every cell of a MultiSelGrid, the checkbox column's included.</summary>
    [Test]
    public void MultiSelCellTemplateDrawsEveryCellTest()
    {
        using BunitContext ctx = NewContext();
        Product[] rows = Rows(3);
        DataGridModel<Product> model = ModelOf(0);
        foreach (Product row in rows)
            model.GetData().Add(row);
        SetSelectionModel<Product> selection = new(new HashSet<Product>(), defensiveCopy: false);
        RenderFragment<MultiSelGrid<Product>.CellTemplateParam> template = p => b =>
        {
            b.OpenElement(0, "td");
            b.AddAttribute(1, "class", "mine");
            b.AddContent(2, p.Model.GetColumnName(p.CellParam.ColIdx));
            b.CloseElement();
        };
        var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.SelectionModel, selection)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.MultiSelCellTemplate, template));
        var cells = cut.FindAll("td.mine").Select(td => td.TextContent).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(cells.Distinct(), Is.EquivalentTo(new[] { MultiSelGrid.SelColName, "Code", "Name", "Price" }));
            Assert.That(cells, Has.Count.EqualTo(3 * 4));
            Assert.That(cut.FindAll("tbody input[type=checkbox]"), Is.Empty, "no default cell left");
        });
    }

    /// <summary>
    /// Columns registered in code are on the model before the grid sees it: a new model instance, or a column removed from the
    /// same one, must still change the columns shown, with or without horizontal paging.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ColumnsRegisteredInCodeFollowTheModelTest(bool withHorzResizer)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> first = new(Rows(2)), second = new(Rows(2));
        first.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        first.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        second.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        second.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        var grid = RenderGrid(ctx, first, ps =>
        {
            if (withHorzResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(100));
        });
        grid.Render(ps => ps.Add(g => g.Model, second));
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Price" }), "the new model's columns");
        await grid.InvokeAsync(() => second.RemoveColumn("Price"));
        grid.Render();
        Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code" }), "a column removed in code");
    }

    private sealed class LayoutGrid : Grigliaria<Product>
    {
        public Task OpenLayoutAsync() => OnLayoutAsync();
    }

    /// <summary>
    /// "&lt;&lt;" in the layout dialog keeps the fixed columns, so on a grid with one and horizontal paging only fixed columns
    /// are left to show: the grid shows them, and nothing scrolls.
    /// </summary>
    [Test]
    public async Task LayoutOfOnlyFixedColumnsRendersUnderHorizontalPagingTest()
    {
        using BunitContext ctx = NewContext();
        var grid = ctx.Render<LayoutGrid>(ps => ps.Add(g => g.Model, ModelOf(3)).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.HorzResizer, GridHorzResizers.Fixed(30)).Add(g => g.Layoutable, true));
        // not through the anchor: bUnit answers JS at once, so the click would render the dialog only after its handler ended
        await grid.InvokeAsync(() => grid.Instance.OpenLayoutAsync());
        // awaited: Click() does not wait for the handler when another thread still holds the dispatcher
        await grid.WaitForElement("button.moveAllFromRight").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code" }));
            Assert.That(grid.Instance.CanNextCol(), Is.False, "nothing to scroll to");
            Assert.That(grid.Markup, Does.Contain("C00"));
        });
    }
    /// <summary>
    /// While an insert is pending the row being inserted is the current one, after the current row as before it: it is the row
    /// marked as edited, with an icon in its heading that is not the edit's, and cancelling gives the previous current row back.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task PendingInsertIsTheCurrentRowTest(bool afterCurrentRow)
    {
        Product[] rows = Rows(4);
        EditableDataGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(rows);
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        using BunitContext ctx = NewContext();
        var grid = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 10));
        await grid.InvokeAsync(() => model.SetCurrentRow(1));
        Product proto = await grid.InvokeAsync(() => model.InsertAsync(rows[0] with { Code = "NEW" }, afterCurrentRow).AsTask());
        grid.Render();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(afterCurrentRow ? 2 : 1));
            Assert.That(model.GetCurrentItem(), Is.SameAs(proto));
            Assert.That(grid.FindAll("tr.edited").Single().TextContent, Does.Contain("NEW"));
            Assert.That(grid.FindAll("tr.edited .icon.inserting"), Has.Count.EqualTo(1), "the row heading tells an insert from an edit");
            Assert.That(grid.FindAll(".icon.editing"), Is.Empty);
        });
        await grid.InvokeAsync(() => model.CancelAsync().AsTask());
        Assert.That(model.GetCurrentRow(), Is.EqualTo(1), "cancelling gives the current row back");
        await grid.InvokeAsync(() => model.EditAsync().AsTask());
        grid.Render();
        Assert.Multiple(() =>
        {
            Assert.That(grid.FindAll("tr.edited .icon.editing"), Has.Count.EqualTo(1));
            Assert.That(grid.FindAll(".icon.inserting"), Is.Empty);
        });
    }
    /// <summary>
    /// An enrichment whose header template alone changes must apply the new one, with a display template that stays the same.
    /// </summary>
    [Test]
    public void EnrichmentFollowsAHeaderTemplateChangeTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment<DecoratorContext<Product, string>> cell = c => b => b.AddContent(0, "CELL:" + c.Value);
        RenderFragment Columns(string headerText) => builder =>
        {
            builder.OpenComponent<EnrichGridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Name");
            builder.AddAttribute(2, "DisplayTemplate", cell);
            builder.AddAttribute(3, "HeaderDisplayTemplate", (RenderFragment<HDecoratorContext<Product>>)(h => b => b.AddContent(0, "HEAD:" + headerText)));
            builder.CloseComponent();
        };
        var grid = RenderGrid(ctx, ModelOf(2), ps => ps.Add(g => g.ChildContent, Columns("one")));
        Assert.That(grid.Markup, Does.Contain("HEAD:one"));
        grid.Render(ps => ps.Add(g => g.ChildContent, Columns("two")));
        Assert.That(grid.Markup, Does.Contain("HEAD:two").And.Not.Contain("HEAD:one"));
    }
    /// <summary>
    /// With the fixed columns taking up the units, the window's first scrolling column is still shown, as without fixed
    /// columns, and the cell cursor rests on it rather than on a column that is not rendered.
    /// </summary>
    [Test]
    public async Task ScrollingColumnIsShownWhenTheFixedColumnsFillTheUnitsTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(2));
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Wide", new Column<Product, string>("Wide") { PreferredSize = 30 }, p => "wide");
        model.AddColumn("Narrow", new Column<Product, string>("Narrow") { PreferredSize = 10 }, p => "narrow");
        var grid = RenderGrid(ctx, model, ps => ps.Add(g => g.PageSize, 2).Add(g => g.HorzResizer, GridHorzResizers.Fixed(15)));
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Wide" }));
            Assert.That(grid.Instance.GetCurrentCol(), Is.EqualTo("Wide"));
            Assert.That(grid.Markup, Does.Contain("wide"));
        });
        await grid.InvokeAsync(() => grid.Instance.NextCol());
        Assert.Multiple(() =>
        {
            Assert.That(grid.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Narrow" }));
            Assert.That(grid.Instance.GetCurrentCol(), Is.EqualTo("Narrow"));
        });
    }

    private sealed record Ev(DateTime At, DateTimeOffset AtOffset);

    /// <summary>A <c>DateTimeOffset</c> is a date: centred by default, as a <c>DateTime</c> is.</summary>
    [Test]
    public void DateTimeOffsetIsCentredAsADateTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Ev> model = new([new Ev(new DateTime(2026, 1, 1), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))]);
        model.AddColumn("At", new Column<Ev, DateTime>("At") { PreferredSize = 10 }, e => e.At);
        model.AddColumn("AtOffset", new Column<Ev, DateTimeOffset>("AtOffset") { PreferredSize = 10 }, e => e.AtOffset);
        var cut = ctx.Render<Grigliaria<Ev>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        cut.WaitForAssertion(() =>
            Assert.That(cut.FindAll("td.data").Select(td => td.GetAttribute("align")), Is.EqualTo(new[] { "center", "center" })));
    }
}
