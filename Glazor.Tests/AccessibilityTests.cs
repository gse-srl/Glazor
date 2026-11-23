using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Glazor.Tabs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// What assistive technology gets from the grids, at the level QuickGrid sets: a name on every icon-only control, an alt on
/// every image, the sort state on the header cell, the row counts of a paged table, the current row, labels on the form
/// controls, a name on the dialogs, ids that are unique and valid.
/// </summary>
public class AccessibilityTests
{
    private sealed record Product(string Code, string Name, decimal Price, bool Active);

    /// <summary>An icon is decorative and hidden, or an image with a name.</summary>
    private static bool IsNamedOrHidden(AngleSharp.Dom.IElement icon) =>
        icon.GetAttribute("aria-hidden") == "true" || (icon.GetAttribute("role") == "img" && !string.IsNullOrEmpty(icon.GetAttribute("aria-label")));

    private static Product[] Rows(int count) =>
        Enumerable.Range(0, count).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m, i % 2 == 0)).ToArray();

    private static DataGridModel<Product> ModelOf(int rowCount)
    {
        DataGridModel<Product> model = new(Rows(rowCount));
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10, Sortable = false, Filterable = false }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        model.AddColumn("Active", new Column<Product, bool>("Active") { PreferredSize = 10 }, p => p.Active);
        return model;
    }

    private static IRenderedComponent<Grigliaria<Product>> RenderGrid(BunitContext ctx, DataGridModel<Product> model, bool layoutable = false)
        => ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, layoutable).Add(g => g.ShowNavigationButtons, true));

    /// <summary>
    /// Every icon-only button in the footer has a name, and no image in the grid is left without an alt.
    /// </summary>
    [Test]
    public async Task IconControlsAreNamedAndImagesHaveAltTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model, layoutable: true);
        await cut.InvokeAsync(() => model.SetSortedColumn("Name", true));
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Price", new NumberDef<decimal> { From = 1m }));
        cut.Render();
        Assert.Multiple(() =>
        {
            foreach ((string cls, string name) in new[] { ("btnFirstRow", "First row"), ("btnPrevPage", "Previous page"), ("btnPrevRow", "Previous row"),
                ("btnNextRow", "Next row"), ("btnNextPage", "Next page"), ("btnLastRow", "Last row"), ("btnSearchOnCurrentSortedColumn", "Search on current sorted column") })
            {
                Assert.That(cut.Find($"button.{cls}").GetAttribute("aria-label"), Is.EqualTo(name), cls);
                Assert.That(cut.Find($"button.{cls}").GetAttribute("title"), Is.EqualTo(name), cls);
            }
            Assert.That(cut.FindAll(".icon").Where(i => !IsNamedOrHidden(i)), Is.Empty, "every icon is hidden from assistive technology or named");
            Assert.That(cut.FindAll(".icon").Count, Is.GreaterThanOrEqualTo(3), "the layout icon, the sort arrow and the filter icons are there");
            Assert.That(cut.FindAll("a[title='Filter active']"), Has.Count.EqualTo(1), "the filtered column's icon says so");
            Assert.That(cut.FindAll("a[title='Filter']"), Has.Count.EqualTo(2), "the other filterable columns");
            Assert.That(cut.FindAll("a[title='Grid layout - Filtered data']"), Has.Count.EqualTo(1), "the layout link says the grid is filtered");
            Assert.That(cut.FindAll(".vertScrollBar[aria-hidden='true']"), Has.Count.EqualTo(1), "the custom scrollbar is not announced; the keys scroll");
            Assert.That(cut.Find("input[type=checkbox][aria-label='Active']"), Is.Not.Null, "a bool cell is named after its column");
        });
    }

    /// <summary>
    /// The header cells carry the sort state as QuickGrid does: <c>none</c> on a sortable column, the direction on the sorted one,
    /// nothing on a column that cannot be sorted; every header cell has its scope.
    /// </summary>
    [Test]
    public async Task HeaderCellsCarryTheSortStateTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderGrid(ctx, model);
        // the header text comes first in the cell, the column menu's items after it
        string? SortOf(string label) => cut.FindAll("thead th").Single(th => th.TextContent.TrimStart().StartsWith(label)).GetAttribute("aria-sort");
        Assert.Multiple(() =>
        {
            Assert.That(SortOf("Name"), Is.EqualTo("none"));
            Assert.That(SortOf("Code"), Is.Null, "not sortable");
            Assert.That(cut.FindAll("thead th").All(th => th.GetAttribute("scope") == "col"), Is.True);
        });
        await cut.InvokeAsync(() => model.SetSortedColumn("Name", true));
        cut.Render();
        Assert.That(SortOf("Name"), Is.EqualTo("ascending"));
        await cut.InvokeAsync(() => model.SetSortedColumn("Name", false));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(SortOf("Name"), Is.EqualTo("descending"));
            Assert.That(SortOf("Price"), Is.EqualTo("none"));
            Assert.That(cut.FindAll("th > a .icon.sort-asc[aria-hidden='true'], th > a .icon.sort-desc[aria-hidden='true']"), Has.Count.EqualTo(1), "the arrow repeats what aria-sort says");
        });
    }

    /// <summary>
    /// A paged table tells where the page sits: the row count and each row's index count the header row, and the current row is
    /// marked, once, with <c>aria-current</c>.
    /// </summary>
    [Test]
    public async Task RowCountsAndTheCurrentRowAreExposedTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(7));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("table").GetAttribute("aria-rowcount"), Is.EqualTo("8"), "seven rows and the header row");
            Assert.That(cut.Find("thead tr").GetAttribute("aria-rowindex"), Is.EqualTo("1"));
            Assert.That(cut.FindAll("tbody tr[aria-rowindex]").Select(tr => tr.GetAttribute("aria-rowindex")), Is.EqualTo(new[] { "2", "3", "4", "5", "6" }), "the page of five");
            Assert.That(cut.FindAll("tr[aria-current='true']"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("tr[aria-current='true']").GetAttribute("aria-rowindex"), Is.EqualTo("2"), "the first row is current");
            Assert.That(cut.FindAll("[id='body'], [id='trh'], [id='trsb'], [id='trg']"), Is.Empty, "no static ids a second grid would repeat");
        });
        await cut.InvokeAsync(() => cut.Instance.SetCurrentRow(6));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("tr[aria-current='true']"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("tr[aria-current='true']").GetAttribute("aria-rowindex"), Is.EqualTo("8"));
        });
    }

    /// <summary>
    /// A grid with no sortable or filterable column and no footer buttons has no link or button: the keyboard still reaches it,
    /// its key-bound container being a Tab stop.
    /// </summary>
    [Test]
    public void KeyboardReachesAGridWithNoControlTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Rows(3));
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, Sortable = false, Filterable = false }, p => p.Name);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ShowDefaultButtons, false));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("a, button").Where(e => e.Closest("dialog") == null), Is.Empty, "nothing else to Tab to, the closed dialogs aside");
            Assert.That(cut.Find("div.grigliaria > div").GetAttribute("tabindex"), Is.EqualTo("0"));
        });
    }

    private sealed record Step(string Name);

    /// <summary>
    /// The selection column's controls and the move buttons have names, so a MultiSelGrid reads as "Select all", "Select row",
    /// "Move row up" rather than three unlabeled boxes and two buttons.
    /// </summary>
    [Test]
    public void MultiSelGridControlsAreNamedTest()
    {
        using BunitContext ctx = NewContext();
        GridModel<Step> model = new() { ItemKeyFn = s => s.Name };
        model.GetData().Set([new("a"), new("b")]);
        model.AddColumn("Name", new Column<Step, string>("Name"), s => s.Name);
        var cut = ctx.Render<MultiSelGrid<Step>>(ps => ps.Add(g => g.Model, model)
            .Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Step>(model.GetData().ToList())).Add(g => g.Spinner, new NoSpinner()));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("thead input[type=checkbox]").GetAttribute("title"), Is.EqualTo("Select all"));
            Assert.That(cut.FindAll("tbody input[type=checkbox][aria-label='Select row']"), Has.Count.EqualTo(2));
            Assert.That(cut.Find("button.btnMoveRowUp").GetAttribute("aria-label"), Is.EqualTo("Move row up"));
            Assert.That(cut.Find("button.btnMoveRowDown").GetAttribute("aria-label"), Is.EqualTo("Move row down"));
            Assert.That(cut.FindAll(".icon").Where(i => !IsNamedOrHidden(i)), Is.Empty);
        });
    }

    private sealed record Node(string Name, bool Children) : ITreeNode
    {
        public bool IsExpanded() => false;
        public int GetLevel() => 0;
        public bool IsLeaf() => !Children;
        public bool IsSubTotal() => false;
        public bool HasChildren() => Children;
    }

    /// <summary>
    /// The tree's expand control says what it does and whether the node is open.
    /// </summary>
    [Test]
    public void TreeExpandControlIsNamedTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<Node> tree = new([new("root", true), new("leaf", false)]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Node, string>("Name"), n => n.Name);
        var cut = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree));
        var expand = cut.Find("td.expandable a");
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("td.indent table").GetAttribute("role"), Is.EqualTo("presentation"), "the indent table is layout, not data");
            Assert.That(expand.GetAttribute("title"), Is.EqualTo("Expand"));
            Assert.That(expand.GetAttribute("aria-expanded"), Is.EqualTo("false"), "passed through the Anchor's attribute splat");
            Assert.That(cut.FindAll(".icon").Where(i => !IsNamedOrHidden(i)), Is.Empty);
        });
    }

    /// <summary>
    /// The filter dialog is named by its title, and two renderers on one page give their inputs different ids, so each label
    /// finds its own checkbox.
    /// </summary>
    [Test]
    public async Task FilterDialogIsNamedAndRendererIdsAreUniqueTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3));
        await cut.InvokeAsync(() => cut.Instance.ShowFilterDialogAsync("Name", null));
        var dialog = cut.FindComponent<FilterDefDlg>();
        string? labelledBy = dialog.Find("dialog").GetAttribute("aria-labelledby");
        Assert.Multiple(() =>
        {
            Assert.That(labelledBy, Is.Not.Null.And.Not.Empty);
            Assert.That(dialog.Find("h3.title").GetAttribute("id"), Is.EqualTo(labelledBy));
        });

        var first = ctx.Render<BoolDefRenderer>(ps => ps.Add(r => r.FilterDef, new BoolDef()));
        var second = ctx.Render<BoolDefRenderer>(ps => ps.Add(r => r.FilterDef, new BoolDef()));
        string firstId = first.Find("input[type=checkbox]").GetAttribute("id")!;
        string secondId = second.Find("input[type=checkbox]").GetAttribute("id")!;
        Assert.Multiple(() =>
        {
            Assert.That(firstId, Is.Not.EqualTo(secondId));
            Assert.That(first.Find("label").GetAttribute("for"), Is.EqualTo(firstId), "the label points at its own input");
            Assert.That(second.Find("label").GetAttribute("for"), Is.EqualTo(secondId));
        });
    }

    /// <summary>
    /// Every value field of the filter renderers has a label of its own: a screen reader names From, To and Value, not "edit text".
    /// The bool renderer's radios are labelled by their text; the group carries the "Value" label.
    /// </summary>
    [Test]
    public void FilterRendererValueFieldsAreLabelledTest()
    {
        using BunitContext ctx = NewContext();
        IRenderedComponent<IComponent>[] renderers =
        [
            ctx.Render<DefaultDefRenderer>(ps => ps.Add(r => r.FilterDef, new DefaultDef())),
            ctx.Render<NumberDefRenderer<decimal>>(ps => ps.Add(r => r.FilterDef, new NumberDef<decimal>())),
            ctx.Render<DateDefRenderer<DateTime>>(ps => ps.Add(r => r.FilterDef, new DateDef<DateTime>())),
        ];
        Assert.Multiple(() =>
        {
            foreach (var cut in renderers)
            {
                var inputs = cut.FindAll("input:not([type=checkbox])");
                Assert.That(inputs, Is.Not.Empty, cut.Instance.GetType().Name);
                foreach (var input in inputs)
                {
                    string? id = input.GetAttribute("id");
                    Assert.That(id, Is.Not.Null.And.Not.Empty, $"{cut.Instance.GetType().Name}: an input without an id");
                    Assert.That(cut.FindAll($"label[for='{id}']"), Has.Count.EqualTo(1), $"{cut.Instance.GetType().Name}: {id}");
                }
            }
            var bools = ctx.Render<BoolDefRenderer>(ps => ps.Add(r => r.FilterDef, new BoolDef()));
            var group = bools.Find("[role=radiogroup]");
            string? labelledBy = group.GetAttribute("aria-labelledby");
            Assert.That(labelledBy, Is.Not.Null.And.Not.Empty);
            Assert.That(bools.FindAll($"#{labelledBy}"), Has.Count.EqualTo(1), "the group's label exists");
            Assert.That(bools.FindAll("input[type=radio]").Select(r => r.ParentElement?.TagName), Has.All.EqualTo("LABEL"), "each radio sits in its label");
        });
    }

    private static void AddTab(RenderTreeBuilder builder, int seq, string name)
    {
        builder.OpenComponent<Tab>(seq);
        builder.AddAttribute(seq + 1, "Name", name);
        builder.AddAttribute(seq + 2, "Label", name);
        builder.AddAttribute(seq + 3, "ChildContent", (RenderFragment)(b => b.AddContent(0, $"PANEL-{name}")));
        builder.CloseComponent();
    }

    /// <summary>
    /// A tab name with a space or punctuation still gives valid ids, so <c>aria-controls</c> and <c>aria-labelledby</c> point at
    /// one element.
    /// </summary>
    [Test]
    public void TabIdsAreValidWhateverTheNameTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment tabs = builder => { AddTab(builder, 0, "Claim parties"); AddTab(builder, 10, "A/B (test)"); AddTab(builder, 20, "Claim_parties"); AddTab(builder, 30, "A.B (test)"); };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, LabelsPosition.Top).Add(t => t.ChildContent, tabs));
        var buttons = cut.FindAll("button[role=tab]");
        var panel = cut.Find("[role=tabpanel]");
        Assert.Multiple(() =>
        {
            Assert.That(buttons.Select(b => b.GetAttribute("id")), Has.All.Matches<string>(id => !id.Any(char.IsWhiteSpace) && !id.Contains('/') && !id.Contains('(')));
            Assert.That(buttons.Select(b => b.GetAttribute("id")), Is.Unique, "names that differ only by punctuation keep distinct ids");
            Assert.That(buttons[0].GetAttribute("aria-controls"), Is.EqualTo(panel.GetAttribute("id")), "the current tab controls the shown panel");
            Assert.That(buttons.Count(b => b.HasAttribute("aria-controls")), Is.EqualTo(1), "the other panels are not in the DOM, so nothing to control");
            Assert.That(cut.Find("[role=tablist]").HasAttribute("aria-orientation"), Is.False, "horizontal is the default");
            Assert.That(panel.GetAttribute("aria-labelledby"), Is.EqualTo(buttons[0].GetAttribute("id")));
            Assert.That(cut.Markup, Does.Contain("PANEL-Claim parties"), "the name itself is untouched");
        });
    }

    private static DataGridModel<Product> GroupedModelOf(int rowCount)
    {
        DataGridModel<Product> model = new(Rows(rowCount));
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, GroupId = "Details" }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10, GroupId = "Details" }, p => p.Price);
        return model;
    }

    /// <summary>
    /// With column groups the group header spans its columns with <c>scope="colgroup"</c>, and no header cell is left without a
    /// scope.
    /// </summary>
    [Test]
    public void GroupHeaderCellsHaveTheirScopeTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, GroupedModelOf(2)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ShowColumnGroups, true));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("thead th[scope='colgroup']"), Has.Count.EqualTo(1));
            Assert.That(cut.FindAll("thead th").All(th => th.HasAttribute("scope")), Is.True);
            Assert.That(cut.FindAll("thead tr[aria-rowindex]").Select(tr => tr.GetAttribute("aria-rowindex")), Is.EqualTo(new[] { "1", "2" }), "two header rows");
            Assert.That(cut.Find("table").GetAttribute("aria-rowcount"), Is.EqualTo("4"), "two rows and two header rows");
        });
    }

    /// <summary>A model the grid reads as paged whose total is not known yet.</summary>
    private sealed class UnknownCountModel() : DataGridModelWrapper<Product>(ModelOf(3))
    {
        public override bool RequiresFetch() => true;
        public override int GetRowCount() => int.MaxValue;
    }

    /// <summary>
    /// The rows that only fill the page, the rule rows and the scrollbar row are hidden from assistive technology, so the rows
    /// a reader counts are the ones the indexes name; without a header the rule row is hidden and the data rows start at 1; a
    /// paged model whose total is unknown says so with -1.
    /// </summary>
    [Test]
    public void FillerRowsAreHiddenAndAnUnknownCountIsMinusOneTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(2));
        Assert.Multiple(() =>
        {
            var table = cut.Find("table");
            Assert.That(table.QuerySelectorAll("tr[aria-hidden='true']").Select(tr => tr.OuterHtml[..Math.Min(90, tr.OuterHtml.Length)]).ToList(), Has.Count.EqualTo(5), "three fillers of a page of five, the scrollbar row and the rule row");
            Assert.That(table.QuerySelectorAll("tr[aria-rowindex]"), Has.Count.EqualTo(3), "the header row and two data rows");
            Assert.That(table.QuerySelectorAll("tr").Where(tr => !tr.HasAttribute("aria-rowindex") && !tr.HasAttribute("aria-hidden")).Select(tr => tr.OuterHtml[..Math.Min(100, tr.OuterHtml.Length)]), Is.Empty,
                "every row of the grid table is counted or hidden");
            Assert.That(cut.FindAll("table[role='presentation']"), Has.Count.EqualTo(2), "the footer table and the layout dialog's are layout, not data");
            Assert.That(table.HasAttribute("role"), Is.False, "the data table keeps its table role");
        });

        var headless = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(2)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ShowHeader, false));
        Assert.Multiple(() =>
        {
            Assert.That(headless.FindAll("thead tr[aria-hidden='true']"), Has.Count.EqualTo(1), "the rule row that stands in for the header");
            Assert.That(headless.FindAll("tbody tr[aria-rowindex]").First().GetAttribute("aria-rowindex"), Is.EqualTo("1"));
            Assert.That(headless.Find("table").GetAttribute("aria-rowcount"), Is.EqualTo("2"));
        });

        var empty = RenderGrid(ctx, ModelOf(0));
        Assert.Multiple(() =>
        {
            Assert.That(empty.FindAll("tbody tr[aria-rowindex]"), Has.Count.EqualTo(1), "the row that says the table is empty");
            Assert.That(empty.Find("table").QuerySelectorAll("tr[aria-hidden='true']"), Has.Count.EqualTo(6), "four fillers, the scrollbar row and the rule row");
            Assert.That(empty.Find("table").GetAttribute("aria-rowcount"), Is.EqualTo("2"), "the header row and the row that says the table is empty");
            Assert.That(empty.Find("tbody tr[aria-rowindex]").GetAttribute("aria-rowindex"), Is.EqualTo("2"), "not past the count");
        });

        var paged = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, new UnknownCountModel()).Add(g => g.Spinner, new NoSpinner()).Add(g => g.PageSize, 3));
        Assert.That(paged.Find("table").GetAttribute("aria-rowcount"), Is.EqualTo("-1"), "unknown total");
    }

    private sealed class DialogGrid : Grigliaria<Product>
    {
        public Task OpenLayoutAsync() => OnLayoutAsync();
    }

    /// <summary>
    /// The layout dialog's move buttons are named, as every other icon-only control is.
    /// </summary>
    [Test]
    public async Task LayoutDialogMoveButtonsAreNamedTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, ModelOf(2)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
        Assert.That(new[] { "moveAllFromLeft", "moveFromLeft", "moveFromRight", "moveAllFromRight" }.Select(cls => dialog.Find($"button.{cls}").GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Show all columns", "Show selected columns", "Hide selected columns", "Hide all columns" }));
        Assert.Multiple(() =>
        {
            Assert.That(dialog.Find("div.content > table").GetAttribute("role"), Is.EqualTo("presentation"), "the dialog lays its lists out in a table");
            Assert.That(dialog.FindAll("table[aria-label]").Select(t => t.GetAttribute("aria-label")), Is.EquivalentTo(new[] { "Hidden columns", "Visible columns" }), "the two lists are named");
        });
    }

    /// <summary>
    /// A disabled Anchor keeps its link role and its name, announced disabled, instead of leaving the accessibility tree.
    /// </summary>
    [Test]
    public void DisabledAnchorStaysANamedLinkTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Anchor>(ps => ps.Add(a => a.Disabled, true).Add(a => a.Title, "Filter").Add(a => a.OnClick, () => { }).AddChildContent("x"));
        var a = cut.Find("a");
        Assert.Multiple(() =>
        {
            Assert.That(a.GetAttribute("role"), Is.EqualTo("link"));
            Assert.That(a.GetAttribute("aria-disabled"), Is.EqualTo("true"));
            Assert.That(a.GetAttribute("title"), Is.EqualTo("Filter"));
            Assert.That(a.HasAttribute("href"), Is.False, "not focusable, not followable");
        });
    }

    /// <summary>
    /// A grid's table takes its accessible name from <c>AriaLabel</c>, through the wrappers too, so a page with more than one grid
    /// names each.
    /// </summary>
    [Test]
    public void GridTableTakesItsNameFromAriaLabelTest()
    {
        using BunitContext ctx = NewContext();
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(1)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.AriaLabel, "Products"));
        GridModel<Step> steps = new() { ItemKeyFn = s => s.Name };
        steps.GetData().Set([new("a")]);
        steps.AddColumn("Name", new Column<Step, string>("Name"), s => s.Name);
        var multi = ctx.Render<MultiSelGrid<Step>>(ps => ps.Add(g => g.Model, steps).Add(g => g.SelectionModel, new SetSelectionModelWithSelectAll<Step>(steps.GetData().ToList()))
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.AriaLabel, "Steps"));
        TreeGridModel<Node> tree = new([new("root", true)]) { OnSetExpand = (_, _) => ValueTask.CompletedTask };
        tree.AddColumn("Name", new Column<Node, string>("Name"), n => n.Name);
        var treeGrid = ctx.Render<Treegliaria<Node>>(ps => ps.Add(g => g.Model, tree).Add(g => g.AriaLabel, "Nodes"));
        Assert.Multiple(() =>
        {
            Assert.That(grid.Find("table[aria-rowcount]").GetAttribute("aria-label"), Is.EqualTo("Products"));
            Assert.That(multi.Find("table[aria-rowcount]").GetAttribute("aria-label"), Is.EqualTo("Steps"), "forwarded by MultiSelGrid");
            Assert.That(treeGrid.Find("table[aria-rowcount]").GetAttribute("aria-label"), Is.EqualTo("Nodes"), "forwarded by Treegliaria");
            Assert.That(RenderGrid(ctx, ModelOf(1)).Find("table[aria-rowcount]").HasAttribute("aria-label"), Is.False, "none given, none rendered");
        });
    }

    /// <summary>
    /// A host-given <c>IdPrefix</c> goes through the same escape as the tab names, so a prefix with a space still gives valid ids.
    /// </summary>
    [Test]
    public void TabIdPrefixIsEscapedTooTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment tabs = builder => { AddTab(builder, 0, "A"); AddTab(builder, 10, "B"); };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.IdPrefix, "my tabs").Add(t => t.ChildContent, tabs));
        var buttons = cut.FindAll("button[role=tab]");
        var panel = cut.Find("[role=tabpanel]");
        Assert.Multiple(() =>
        {
            Assert.That(buttons.Select(b => b.GetAttribute("id")!), Has.All.Matches<string>(id => !id.Any(char.IsWhiteSpace)));
            Assert.That(panel.GetAttribute("aria-labelledby"), Is.EqualTo(buttons[0].GetAttribute("id")));
            Assert.That(buttons[0].GetAttribute("aria-controls"), Is.EqualTo(panel.GetAttribute("id")));
        });
    }
}
