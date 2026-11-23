using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// A <see cref="ADataGrid{TGridItem}.Memento"/> is persisted per user and outlives the code that produced
/// it, so a column rename or removal between two versions of the host is a normal event. Restoring such a
/// memento must degrade — drop what no longer matches, log, keep the rest — rather than throw from inside a
/// render. The two tests differ only in <c>HorzResizer</c>, because the column names take a different path
/// through <c>AGrid.SyncShownColumns</c> depending on whether horizontal paging is on, and the outcome
/// must not depend on that.
/// </summary>
public class StaleMementoTests
{
    private sealed record Product(string Code, string Name, decimal Price);

    private static DataGridModel<Product> ModelOf(int rowCount, bool nameFilterable = true)
    {
        DataGridModel<Product> model = new(Enumerable.Range(0, rowCount)
            .Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray());
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, Filterable = nameFilterable }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        return model;
    }

    /// <summary>
    /// What a previous version of the host saved, when the column now called "Name" was called "Descr":
    /// it is in the visible set, it carries a filter, and it is the sorted column.
    /// </summary>
    private static Memento StaleMemento()
    {
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Descr", "Price"];
        return new Memento(layout,
            new Dictionary<string, AFilterDef> { ["Descr"] = new DefaultDef { Value = "x" } },
            [new("Descr", true)]);
    }

    private static void RestoreAndAssert(bool withHorzResizer)
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps =>
        {
            ps.Add(g => g.Model, ModelOf(5)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true);
            if (withHorzResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(25));
        });

        Assert.DoesNotThrow(() =>
        {
            cut.InvokeAsync(() => cut.Instance.RestoreMemento(StaleMemento())).GetAwaiter().GetResult();
            cut.Render();
        }, "a memento naming a column that no longer exists must not break the render");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Price" }),
                "the unknown column is dropped, the known ones survive in order");
            Assert.That(cut.Instance.GetFilters(), Is.Empty, "the filter on the unknown column is dropped");
            Assert.That(cut.Instance.GetModel().GetSortedColumn(), Is.Null, "the sort on the unknown column is dropped");
            Assert.That(cut.Markup, Does.Contain("C00"), "and the grid still renders its rows");
        });
    }

    [Test]
    public void StaleMementoIsToleratedWithoutHorzResizerTest() => RestoreAndAssert(withHorzResizer: false);

    [Test]
    public void StaleMementoIsToleratedWithHorzResizerTest() => RestoreAndAssert(withHorzResizer: true);

    /// <summary>
    /// A stored filter that filters nothing, as an older or hand-written store may hold, is left out as SetFilter leaves it out: the
    /// column does not show an active filter, and the next memento does not save it again.
    /// </summary>
    [Test]
    public async Task RestoredVoidFilterIsLeftOutTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(5)).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(new Memento(null,
            new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "" } }, [])));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.HasFilter("Name"), Is.False);
            Assert.That(cut.Instance.CreateMemento().FiltersDefs, Is.Empty);
        });
    }

    /// <summary>
    /// The other way a memento goes stale: the column is still there, but it is no longer filterable, or its value type
    /// changed. 
    /// </summary>
    [Test]
    public void StaleFilterOnAnUnfilterableOrRetypedColumnIsDroppedTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps =>
            ps.Add(g => g.Model, ModelOf(5, nameFilterable: false)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Name", "Price"];
        Memento memento = new(layout,
            new Dictionary<string, AFilterDef>
            {
                ["Name"] = new DefaultDef { Value = "Name01" },      // Name was filterable when this was saved
                ["Price"] = new NumberDef<int> { From = 100 },       // Price was an int when this was saved
            },
            []);

        Assert.DoesNotThrow(() =>
        {
            cut.InvokeAsync(() => cut.Instance.RestoreMemento(memento)).GetAwaiter().GetResult();
            cut.Render();
        }, "a stale filter must not break the render");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters(), Is.Empty, "both stale filters are dropped");
            Assert.That(cut.Instance.GetModel().GetRowCount(), Is.EqualTo(5), "no row is hidden");
            Assert.That(cut.Markup, Does.Contain("C00"));
        });
    }

    /// <summary>
    /// A text filter saved for a column that has since taken a definition of its own, a number here, is stale too: pushed down
    /// it would compare the value's text, or fail on a type the database cannot cast. One on a text column stays.
    /// </summary>
    [Test]
    public void StaleTextFilterOnAColumnWithItsOwnDefinitionIsDroppedTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps =>
            ps.Add(g => g.Model, ModelOf(5)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Name", "Price"];
        Memento memento = new(layout,
            new Dictionary<string, AFilterDef>
            {
                ["Name"] = new DefaultDef { Value = "Name01" },
                ["Price"] = new DefaultDef { Value = "1.5" },   // Price was a text column when this was saved
            },
            []);
        cut.InvokeAsync(() => cut.Instance.RestoreMemento(memento)).GetAwaiter().GetResult();
        Assert.That(cut.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }), "the text column keeps its filter");
    }
    private static IRenderedComponent<Grigliaria<Product>> RenderRestored(BunitContext ctx, IDataGridModel<Product> model,
        Memento? memento = null)
    {
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        memento ??= cut.Instance.CreateMemento();
        cut.InvokeAsync(() => cut.Instance.RestoreMemento(memento)).GetAwaiter().GetResult();
        return cut;
    }

    private static readonly Column<Product, string> Extra = new("Extra") { PreferredSize = 10 };

    /// <summary>
    /// A change to the column set keeps the columns the user arranged, with or without a restore: a hidden one stays hidden,
    /// the order stays, and an added column the defaults show is appended.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ColumnChangeKeepsTheColumnsTheUserArrangedTest(bool restored)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(6);
        var cut = restored ? RenderRestored(ctx, model)
            : ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Code", "Price"));
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Price", "Extra" }));
        await cut.InvokeAsync(() => model.RemoveColumn("Price"));
        cut.Render();
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Extra" }));
    }

    /// <summary>
    /// A new model shows the default columns, and raises OnLayoutChanged when that changes the columns shown, so a host that saves
    /// on it saves what the grid shows; a new model that shows the same columns raises nothing.
    /// </summary>
    [Test]
    public async Task NewModelThatChangesTheColumnsShownRaisesOnLayoutChangedTest()
    {
        using BunitContext ctx = NewContext();
        int raised = 0;
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(3)).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.OnLayoutChanged, () => raised++));
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Code", "Price"));
        raised = 0;
        cut.Render(ps => ps.Add(g => g.Model, ModelOf(3)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price" }), "the defaults");
            Assert.That(raised, Is.EqualTo(1), "the columns shown changed");
        });
        cut.Render(ps => ps.Add(g => g.Model, ModelOf(3)));
        Assert.That(raised, Is.EqualTo(1), "the same columns again");
    }

    /// <summary>
    /// A column added to or removed from the same model raises OnLayoutChanged when it changes the columns shown, as a new model
    /// does; a render that changes nothing raises nothing.
    /// </summary>
    [Test]
    public async Task ColumnChangeThatChangesTheColumnsShownRaisesOnLayoutChangedTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        int raised = 0;
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.OnLayoutChanged, () => raised++));
        raised = 0;
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price", "Extra" }));
            Assert.That(raised, Is.EqualTo(1), "a column added");
        });
        await cut.InvokeAsync(() => model.RemoveColumn("Price"));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Extra" }));
            Assert.That(raised, Is.EqualTo(2), "a column removed");
        });
        cut.Render();
        Assert.That(raised, Is.EqualTo(2), "nothing changed");
    }

    /// <summary>
    /// A column the restored layout does not name, because it was added after the memento was saved, shows when it is added
    /// after the restore, as a markup column an @if turns on does.
    /// </summary>
    [Test]
    public async Task ColumnAddedAfterARestoreThatItsLayoutDoesNotNameShowsTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = RenderRestored(ctx, model);
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price" }));
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price", "Extra" }));
        await cut.InvokeAsync(() => model.AddColumn("Later", new Column<Product, string>("Later") { PreferredSize = 10 }, p => "y"));
        cut.Render();
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price", "Extra", "Later" }),
            "the restored layout now holds the first one too");
    }

    /// <summary>
    /// After a restore, a change to the column set applies the restored layout again so that added columns follow it, but
    /// what the user did since stays: the filter, the sort and the order the user gave the columns.
    /// </summary>
    [Test]
    public async Task ColumnChangeAfterARestoreKeepsWhatTheUserDidTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(6);
        var cut = RenderRestored(ctx, model);
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name03" }));
        await cut.InvokeAsync(() => model.SetSortedColumn("Price", false));
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Code", "Price", "Name"));
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }));
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
            Assert.That(model.GetSortedColumn(), Is.EqualTo("Price"));
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Price", "Name", "Extra" }));
        });
    }

    /// <summary>
    /// The same column change while an edit is pending: nothing about the filters changed, so the frozen model must not be
    /// asked to apply them from inside the render.
    /// </summary>
    [Test]
    public async Task ColumnChangeAfterARestoreDuringAnEditDoesNotThrowTest()
    {
        using BunitContext ctx = NewContext();
        EditableDataGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(Enumerable.Range(0, 3).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i)).ToArray());
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        var cut = RenderRestored(ctx, model);
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        await cut.InvokeAsync(() => model.EditAsync().AsTask());
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        Assert.DoesNotThrow(() => cut.Render());
    }

    /// <summary>
    /// A column the restored layout names but the model did not have yet takes its place when it is added.
    /// </summary>
    [Test]
    public async Task ColumnAddedAfterARestoreFollowsTheRestoredLayoutTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Extra", "Name"];
        var cut = RenderRestored(ctx, model, new Memento(layout, new Dictionary<string, AFilterDef>(), []));
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name" }));
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Extra", "Name" }));
    }

    /// <summary>
    /// A column removed in code takes its filter and its sort with it, with or without a restored memento: kept, they name a
    /// column the model can no longer find.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ColumnRemovedInCodeDropsItsFilterAndSortTest(bool restored)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(6);
        var cut = restored ? RenderRestored(ctx, model)
            : ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name03" }));
        await cut.InvokeAsync(() => model.SetSortedColumn("Name", false));
        await cut.InvokeAsync(() => model.RemoveColumn("Name"));
        Assert.DoesNotThrow(() => cut.Render());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters(), Is.Empty);
            Assert.That(model.GetSortedColumn(), Is.Null);
            Assert.That(model.GetRowCount(), Is.EqualTo(6));
        });
    }

    /// <summary>
    /// After a change to the column set, the reset still goes back to the sizes the grid started with, whether a size was set
    /// in code or came with a restored memento, and shows the added column.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ResetAfterAColumnChangeDropsTheSizesSetSinceTheStartTest(bool restored)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        if (restored)
        {
            GridLayout layout = new();
            layout.columnsToShow = ["Code", "Name", "Price"];
            layout.attributes["Price"] = new(33);
            await cut.InvokeAsync(() => cut.Instance.RestoreMemento(new Memento(layout, new Dictionary<string, AFilterDef>(), [])));
        }
        else
            await cut.InvokeAsync(() => cut.Instance.SetPreferredSize("Price", 33));
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        int PriceSize() => cut.Instance.GetColumn(model.GetColumnIndex("Price")).GetPreferredSize();
        Assert.That(PriceSize(), Is.EqualTo(33));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(null));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(PriceSize(), Is.EqualTo(10));
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price", "Extra" }));
        });
    }

    /// <summary>
    /// SetLayout(null) puts back the default layout, the columns shown by default and no sizes, as a layout change:
    /// OnLayoutChanged is raised, and after a restore a later column change keeps it.
    /// </summary>
    [Test]
    public async Task SetLayoutToNullPutsBackTheDefaultLayoutTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        int layoutChanges = 0;
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true)
            .Add(g => g.OnLayoutChanged, () => layoutChanges++));
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Name"];
        layout.attributes["Price"] = new(55);
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(new Memento(layout, new Dictionary<string, AFilterDef>(), [])));
        int PriceSize() => cut.Instance.GetColumn(model.GetColumnIndex("Price")).GetPreferredSize();
        int changesBefore = layoutChanges;
        await cut.InvokeAsync(() => cut.Instance.SetLayout(null));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price" }), "Price, hidden by the restore, is back");
            Assert.That(PriceSize(), Is.EqualTo(10));
            Assert.That(layoutChanges, Is.GreaterThan(changesBefore));
        });
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price", "Extra" }), "the defaults, not the restored layout");
            Assert.That(PriceSize(), Is.EqualTo(10), "the restored size does not come back");
        });
    }

    /// <summary>Restores before its first render, when the columns declared in its markup are not registered yet.</summary>
    private sealed class EarlyRestoringGrid : Grigliaria<Product>
    {
        [Parameter] public Memento? Saved { get; set; }

        private bool _restored;

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (!_restored && Saved != null)
            {
                _restored = true;
                RestoreMemento(Saved);
            }
        }
    }

    /// <summary>
    /// A restore made before the markup columns register finds their filters and sort on unknown columns: it applies them once
    /// the columns register, and a later column change leaves alone what the user did since.
    /// </summary>
    [Test]
    public async Task EarlyRestoreAppliesItsFiltersAndSortOnceTheColumnsRegisterTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Enumerable.Range(0, 6).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray())
        {
            ItemKeyFn = p => p.Code,
        };
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Code");
            builder.AddAttribute(2, "Label", "Code");
            builder.AddAttribute(3, "Property", (Func<Product, string>)(p => p.Code));
            builder.CloseComponent();
            builder.OpenComponent<GridColumn<Product, string>>(4);
            builder.AddAttribute(5, "Name", "Name");
            builder.AddAttribute(6, "Label", "Name");
            builder.AddAttribute(7, "Property", (Func<Product, string>)(p => p.Name));
            builder.CloseComponent();
        };
        GridLayout layout = new();
        layout.columnsToShow = ["Name", "Code"];
        Memento saved = new(layout,
            new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "Name03" } }, [new("Name", false)]);
        var cut = ctx.Render<EarlyRestoringGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.ChildContent, columns).Add(g => g.Saved, saved));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Name", "Code" }));
            Assert.That(cut.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }));
            Assert.That(model.GetSortedColumn(), Is.EqualTo("Name"));
            Assert.That(model.GetSortType(), Is.EqualTo(SortType.Desc));
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
        });
        await cut.InvokeAsync(() => cut.Instance.SetFilters(new Dictionary<string, AFilterDef>()));
        await cut.InvokeAsync(() => model.SetSortedColumn("Code", true));
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters(), Is.Empty, "applied once, not again at the next column change");
            Assert.That(model.GetSortedColumn(), Is.EqualTo("Code"));
        });
    }

    /// <summary>
    /// GetLayout hands out a copy, to be saved and given back later: what the grid does afterwards leaves it alone, and SetLayout
    /// of it puts the saved columns and sizes back.
    /// </summary>
    [Test]
    public async Task GetLayoutGivesACopyToRestoreLaterTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        GridLayout saved = cut.Instance.GetLayout()!;
        await cut.InvokeAsync(() =>
        {
            cut.Instance.SetPreferredSize("Price", 40);
            cut.Instance.SetColumnsToShow("Code", "Price");
        });
        string[] savedColumns = saved.columnsToShow.ToArray();
        int savedSizes = saved.attributes.Count;
        await cut.InvokeAsync(() => cut.Instance.SetLayout(saved));
        Assert.Multiple(() =>
        {
            Assert.That(savedColumns, Is.EqualTo(new[] { "Code", "Name", "Price" }), "the copy is not the grid's own");
            Assert.That(savedSizes, Is.Zero);
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price" }), "restored");
            Assert.That(cut.Instance.GetColumn(model.GetColumnIndex("Price")).GetPreferredSize(), Is.EqualTo(10));
        });
    }

    /// <summary>
    /// A memento saved by a layoutable grid and restored into one that is not: its layout is ignored, now and at the next column
    /// change, so the grid keeps no layout link and its default columns; its filters and sort are restored.
    /// </summary>
    [Test]
    public async Task RestoreIntoANonLayoutableGridIgnoresTheLayoutTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(6);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Price"];
        layout.attributes["Price"] = new(40);
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(new Memento(layout,
            new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "Name03" } }, [])));
        cut.Render();
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.Layoutable, Is.False);
            Assert.That(cut.FindAll("span.icon.layout"), Is.Empty, "no layout link");
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price", "Extra" }));
            Assert.That(cut.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }));
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A layout a grid that is not layoutable ignored stays ignored when the grid becomes layoutable later: the next column change
    /// keeps the default columns and sizes, not the memento's.
    /// </summary>
    [Test]
    public async Task IgnoredLayoutStaysIgnoredOnceTheGridIsLayoutableTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        GridLayout layout = new();
        layout.columnsToShow = ["Price", "Code"];
        layout.attributes["Price"] = new(40);
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(new Memento(layout, new Dictionary<string, AFilterDef>(), [])));
        cut.Render(ps => ps.Add(g => g.Layoutable, true));
        await cut.InvokeAsync(() => model.AddColumn("Extra", Extra, p => "x"));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name", "Price", "Extra" }));
            Assert.That(cut.Instance.GetColumn(model.GetColumnIndex("Price")).GetPreferredSize(), Is.EqualTo(10));
        });
    }

    // A host's definition that takes injected state, as the docs' RolesFDef does
    private sealed class StatusFDef : DynaSetDef<StatusFDef, string, string>, IWithInjectedParams<StatusFDef>
    {
        public StatusFDef(IEnumerable<string> selected) : base(v => v, false) => Selected = new(selected);
        public override StatusFDef Clone() => new(Selected);
        protected override DataGridModel<string> CreateAllValuesModel() => new(Selected);
        protected override ValueTask<IList<string>> RetrieveAllValuesAsync(CancellationToken ct) => ValueTask.FromResult<IList<string>>(Selected.ToList());
        public override StatusFDef Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
            throw new NotSupportedException();
        public StatusFDef InjectParamsInto(StatusFDef toBeInjected) => toBeInjected;
    }

    /// <summary>
    /// A persisted filter that takes injected parameters, restored onto a column whose definition has changed class since, here
    /// to the factory's own: it is dropped with a warning, as a filter of another type is, rather than failing the cast.
    /// </summary>
    [Test]
    public void RestoredInjectedFilterOnAColumnWhoseDefinitionChangedIsIgnoredTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(5)).Add(g => g.Spinner, new NoSpinner()));
        Memento saved = new(null, new Dictionary<string, AFilterDef> { ["Name"] = new StatusFDef(["Name01"]) }, []);
        Assert.DoesNotThrowAsync(async () => await cut.InvokeAsync(() => cut.Instance.RestoreMemento(saved)));
        Assert.That(cut.Instance.GetFilter("Name"), Is.Null);
    }

    // An injected definition whose injector builds its own class, as the docs' RolesFDef does, and a host's narrowed subclass
    private class TagFDef : DynaSetDef<TagFDef, string, string>, IWithInjectedParams<TagFDef>
    {
        public TagFDef(IEnumerable<string> selected) : base(v => v, false) => Selected = new(selected);
        public override TagFDef Clone() => new(Selected);
        protected override DataGridModel<string> CreateAllValuesModel() => new(Selected);
        protected override ValueTask<IList<string>> RetrieveAllValuesAsync(CancellationToken ct) => ValueTask.FromResult<IList<string>>(Selected.ToList());
        public override TagFDef Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
            throw new NotSupportedException();
        public TagFDef InjectParamsInto(TagFDef toBeInjected) => new(toBeInjected.Selected);
    }

    private sealed class ActiveTagFDef : TagFDef
    {
        private readonly bool _ownClone;
        public ActiveTagFDef(bool ownClone, IEnumerable<string> selected) : base(selected) => _ownClone = ownClone;
        public override TagFDef Clone() => _ownClone ? new ActiveTagFDef(true, Selected) : base.Clone();
    }

    /// <summary>
    /// A column whose definition is a subclass of an injected definition keeps a filter of that definition through one restore
    /// of the grid's own memento after another, whether the filter was built by the subclass or by the definition it narrows.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task RestoredInjectedFilterOnASubclassDefinitionIsKeptTest(bool ownClone)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Enumerable.Range(0, 4).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i)).ToArray());
        ActiveTagFDef def = new(ownClone, []);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, FilterDef = def }, p => p.Name);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        TagFDef edited = def.Clone();
        edited.Selected.Add("Name01");
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", edited));
        for (int i = 0; i < 2; i++)
        {
            Memento own = cut.Instance.CreateMemento();
            await cut.InvokeAsync(() => cut.Instance.RestoreMemento(own));
            Assert.Multiple(() =>
            {
                Assert.That(cut.Instance.GetFilter("Name"), Is.Not.Null, $"restore {i + 1}");
                Assert.That(model.GetRowCount(), Is.EqualTo(1), $"restore {i + 1}");
            });
        }
    }

    private sealed class DialogGrid : Grigliaria<Product>
    {
        public Task OpenLayoutAsync() => OnLayoutAsync();
        public LayoutDefDlg<Product> Dialog => layoutDefDlg;
    }

    /// <summary>
    /// The layout dialog's Cancel puts back the layout it started from as it was, with the sizes of the columns out of the model:
    /// a markup column whose @if is off keeps the size set for it. Enter on Cancel cancels too, though OK has the dialog's Enter,
    /// and so does the dialog's own Escape action, which keeps Esc from a host's Escape around the grid.
    /// </summary>
    [TestCase("Cancel")]
    [TestCase("Enter on Cancel")]
    [TestCase("Escape action")]
    [TestCase("Esc")]
    public async Task CancelKeepsTheSizeOfAColumnOutOfTheModelTest(string action)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Enumerable.Range(0, 3).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray())
        {
            ItemKeyFn = p => p.Code,
        };
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        bool showPrice = true;
        RenderFragment columns = builder =>
        {
            if (showPrice)
            {
                builder.OpenComponent<GridColumn<Product, decimal>>(0);
                builder.AddAttribute(1, "Name", "Price");
                builder.AddAttribute(2, "Label", "Price");
                builder.AddAttribute(3, "Property", (Func<Product, decimal>)(p => p.Price));
                builder.CloseComponent();
            }
        };
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true)
            .Add(g => g.ChildContent, columns));
        await cut.InvokeAsync(() => cut.Instance.SetPreferredSize("Price", 33));
        showPrice = false;
        cut.Render();
        Assert.That(model.GetColumnIndex("Price"), Is.LessThan(0), "the column left before the dialog opened");
        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        if (action == "Cancel")
            await cut.InvokeAsync(() => cut.Instance.Dialog.CancelAsync());
        else if (action == "Enter on Cancel")
        {
            // the dialog's grids hold dialogs of their own, rendered before its buttons
            var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
            Assert.That(dialog.FindAll("button.cancel").Last().QuerySelector("span[hidden]"), Is.Not.Null, "Cancel carries its own Enter action");
            var enter = dialog.FindComponents<KeyAction>().Last(k => k.Instance.Key == "Enter" && k.Instance.BindAncestorWithTag == null);
            await cut.InvokeAsync(() => enter.Instance.ProcessAsync(null, null, null));
            Assert.That(dialog.FindComponent<DialogBox>().Instance.IsVisible, Is.False, "the dialog closed");
        }
        else if (action == "Escape action")
        {
            // the dialog's grids hold dialogs and popups of their own, rendered before its own Escape action
            var dialog = cut.FindComponent<LayoutDefDlg<Product>>();
            var esc = dialog.FindComponents<KeyAction>().Last(k => k.Instance.Key == "Escape");
            Assert.That(esc.Instance.BindAncestorWithTag, Is.EqualTo("dialog"), "on the dialog, before the key leaves it");
            await cut.InvokeAsync(() => esc.Instance.ProcessAsync(null, null, null));
            Assert.That(dialog.FindComponent<DialogBox>().Instance.IsVisible, Is.False, "the dialog closed");
        }
        else
            await cut.Find("dialog.layoutDefDlg").TriggerEventAsync("onclose", EventArgs.Empty);
        showPrice = true;
        cut.Render();
        Assert.That(cut.Instance.GetLayout()!.attributes.TryGetValue("Price", out var price) ? price.PreferredSize : (int?)null, Is.EqualTo(33));
    }

    /// <summary>
    /// CreateMemento saves the sizes of the registered columns only: a markup column whose @if is off keeps its size in the
    /// grid for the session, and the memento leaves it out.
    /// </summary>
    [Test]
    public async Task MementoLeavesOutTheSizeOfAColumnOutOfTheModelTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Enumerable.Range(0, 3).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray())
        {
            ItemKeyFn = p => p.Code,
        };
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        bool showPrice = true;
        RenderFragment columns = builder =>
        {
            if (showPrice)
            {
                builder.OpenComponent<GridColumn<Product, decimal>>(0);
                builder.AddAttribute(1, "Name", "Price");
                builder.AddAttribute(2, "Label", "Price");
                builder.AddAttribute(3, "Property", (Func<Product, decimal>)(p => p.Price));
                builder.CloseComponent();
            }
        };
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.ChildContent, columns));
        await cut.InvokeAsync(() => cut.Instance.SetPreferredSize("Price", 33));
        Assert.That(cut.Instance.CreateMemento().Layout!.Attributes.ContainsKey("Price"), Is.True, "registered");
        showPrice = false;
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.CreateMemento().Layout!.Attributes.ContainsKey("Price"), Is.False, "not registered");
            Assert.That(cut.Instance.GetLayout()!.Attributes.ContainsKey("Price"), Is.True, "the grid keeps it for this session");
        });
    }

    /// <summary>
    /// After a restore, a markup column whose @if turns off keeps its size, through a layout change the user makes meanwhile, as
    /// it does without a restore.
    /// </summary>
    [Test]
    public async Task ColumnThatLeavesAfterARestoreKeepsItsSizeTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Enumerable.Range(0, 3).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray())
        {
            ItemKeyFn = p => p.Code,
        };
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        bool showPrice = true;
        RenderFragment columns = builder =>
        {
            if (showPrice)
            {
                builder.OpenComponent<GridColumn<Product, decimal>>(0);
                builder.AddAttribute(1, "Name", "Price");
                builder.AddAttribute(2, "Label", "Price");
                builder.AddAttribute(3, "Property", (Func<Product, decimal>)(p => p.Price));
                builder.CloseComponent();
            }
        };
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.ChildContent, columns));
        await cut.InvokeAsync(() => cut.Instance.SetPreferredSize("Price", 33));
        Memento memento = cut.Instance.CreateMemento();
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(memento));
        showPrice = false;
        cut.Render();
        Assert.That(cut.Instance.GetLayout()!.Attributes.ContainsKey("Price"), Is.True, "the grid keeps it while the column is out");
        await cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Code"));   // a layout change by the user
        showPrice = true;
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Does.Contain("Price"));
            Assert.That(cut.Instance.GetLayout()!.attributes.TryGetValue("Price", out var price) ? price.PreferredSize : (int?)null, Is.EqualTo(33));
        });
    }

    /// <summary>
    /// After a restore, the layout dialog's Cancel and Esc undo what the dialog did, what the restore set aside included: a markup
    /// column whose @if was off at the restore takes its restored place and size when it comes back.
    /// </summary>
    [TestCase("Cancel")]
    [TestCase("Esc")]
    public async Task CancelAfterARestoreKeepsWhatTheRestoreSetAsideTest(string action)
    {
        using BunitContext ctx = NewContext();
        bool showExtra = false;
        RenderFragment columns = builder =>
        {
            if (showExtra)
            {
                builder.OpenComponent<GridColumn<Product, string>>(0);
                builder.AddAttribute(1, "Name", "Extra");
                builder.AddAttribute(2, "Label", "Extra");
                builder.AddAttribute(3, "Property", (Func<Product, string>)(p => p.Code));
                builder.CloseComponent();
            }
        };
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, ModelOf(3)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true)
            .Add(g => g.ChildContent, columns));
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Extra", "Price", "Name"];
        layout.attributes["Extra"] = new(44);
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(new Memento(layout, new Dictionary<string, AFilterDef>(), [])));
        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        if (action == "Cancel")
            await cut.InvokeAsync(() => cut.Instance.Dialog.CancelAsync());
        else
            await cut.Find("dialog.layoutDefDlg").TriggerEventAsync("onclose", EventArgs.Empty);
        showExtra = true;
        cut.Render();
        Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Extra", "Price", "Name" }), "the restored place");
        Assert.That(cut.Instance.GetLayout()!.attributes.TryGetValue("Extra", out var extra) ? extra.PreferredSize : (int?)null, Is.EqualTo(44), "the restored size");
    }

    /// <summary>
    /// A store written by hand may hold a column with a null filter or a null size: the restore skips the filter, the column keeps
    /// its own size, and the grid renders.
    /// </summary>
    [Test]
    public async Task NullEntriesInAStoredMementoAreSkippedTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true));
        Memento memento = System.Text.Json.JsonSerializer.Deserialize<Memento>("""
            {"Layout":{"columnsToShow":["Code","Name","Price"],"attributes":{"Name":null,"Price":{"PreferredSize":15}}},
             "FiltersDefs":{"Name":null},"SorterColumns":[]}
            """)!;
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(memento));
        Assert.DoesNotThrow(() => cut.Render());
        Assert.Multiple(() =>
        {
            Assert.That(model.IsFiltered(), Is.False);
            Assert.That(cut.Instance.GetColumn(model.GetColumnIndex("Name")).GetPreferredSize(), Is.EqualTo(10), "its own size");
            Assert.That(cut.Instance.GetColumn(model.GetColumnIndex("Price")).GetPreferredSize(), Is.EqualTo(15), "the stored size");
        });
    }

    /// <summary>
    /// Cancel puts the layout back once: the dialog's close event that follows it does not restore again, so a host saving the
    /// layout in OnLayoutChanged writes it once.
    /// </summary>
    [Test]
    public async Task CancelRestoresTheLayoutOnceTest()
    {
        using BunitContext ctx = NewContext();
        int changes = 0;
        var cut = ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, ModelOf(3)).Add(g => g.Spinner, new NoSpinner()).Add(g => g.Layoutable, true)
            .Add(g => g.OnLayoutChanged, () => changes++));
        await cut.InvokeAsync(() => cut.Instance.OpenLayoutAsync());
        int before = changes;
        await cut.InvokeAsync(() => cut.Instance.Dialog.CancelAsync());
        await cut.Find("dialog.layoutDefDlg").TriggerEventAsync("onclose", EventArgs.Empty);
        Assert.That(changes - before, Is.EqualTo(1));
    }

    /// <summary>
    /// A layout given directly to a grid that is not layoutable is the host's mistake, and it throws. Null is the default layout,
    /// which such a grid already has.
    /// </summary>
    [Test]
    public void SetLayoutOnANonLayoutableGridThrowsTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ModelOf(3)).Add(g => g.Spinner, new NoSpinner()));
        Assert.Multiple(() =>
        {
            Assert.That(() => cut.InvokeAsync(() => cut.Instance.SetLayout(new GridLayout())).GetAwaiter().GetResult(),
                Throws.InvalidOperationException);
            Assert.That(() => cut.InvokeAsync(() => cut.Instance.SetLayout(null)).GetAwaiter().GetResult(), Throws.Nothing);
            Assert.That(cut.Instance.Layoutable, Is.False);
        });
    }

    /// <summary>
    /// A restored filter waits for its column. When the column registers during an edit, which refuses a filter change, the filter
    /// applies at the first render after the edit, not at some later change of the columns.
    /// </summary>
    [Test]
    public async Task RestoredFilterWhoseColumnRegistersDuringAnEditAppliesAfterItTest()
    {
        using BunitContext ctx = NewContext();
        EditableDataGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(Enumerable.Range(0, 6).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray());
        bool showName = false;
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Code");
            builder.AddAttribute(2, "Label", "Code");
            builder.AddAttribute(3, "Property", (Func<Product, string>)(p => p.Code));
            builder.CloseComponent();
            if (showName)
            {
                builder.OpenComponent<GridColumn<Product, string>>(4);
                builder.AddAttribute(5, "Name", "Name");
                builder.AddAttribute(6, "Label", "Name");
                builder.AddAttribute(7, "Property", (Func<Product, string>)(p => p.Name));
                builder.CloseComponent();
            }
        };
        Memento saved = new(null, new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "Name03" } }, []);
        var cut = ctx.Render<EarlyRestoringGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns).Add(g => g.Saved, saved));
        Assert.That(model.GetRowCount(), Is.EqualTo(6), "the filter waits for its column");
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        await cut.InvokeAsync(() => model.EditAsync().AsTask());
        showName = true;
        cut.Render();
        Assert.That(cut.Instance.GetFilters(), Is.Empty, "not during the edit");
        await cut.InvokeAsync(() => model.CancelAsync().AsTask());
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }), "applied after the edit");
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A reset after a restore made before the first render goes back to how the grid started, not to the restored state: the
    /// default columns, no size, no filter and no sort.
    /// </summary>
    [Test]
    public async Task ResetAfterAnEarlyRestoreGoesBackToTheStartTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new(Enumerable.Range(0, 6).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray())
        {
            ItemKeyFn = p => p.Code,
        };
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Code");
            builder.AddAttribute(2, "Label", "Code");
            builder.AddAttribute(3, "Property", (Func<Product, string>)(p => p.Code));
            builder.CloseComponent();
            builder.OpenComponent<GridColumn<Product, string>>(4);
            builder.AddAttribute(5, "Name", "Name");
            builder.AddAttribute(6, "Label", "Name");
            builder.AddAttribute(7, "Property", (Func<Product, string>)(p => p.Name));
            builder.CloseComponent();
        };
        GridLayout layout = new();
        layout.columnsToShow = ["Name", "Code"];
        layout.attributes["Name"] = new(33);
        Memento saved = new(layout,
            new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "Name03" } }, [new("Name", false)]);
        var cut = ctx.Render<EarlyRestoringGrid>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true).Add(g => g.ChildContent, columns).Add(g => g.Saved, saved));
        Assert.That(model.GetRowCount(), Is.EqualTo(1), "restored");
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(null));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Code", "Name" }));
            Assert.That(cut.Instance.GetColumn(model.GetColumnIndex("Name")).GetPreferredSize(), Is.Not.EqualTo(33));
            Assert.That(cut.Instance.GetFilters(), Is.Empty);
            Assert.That(model.GetSortedColumn(), Is.Null);
            Assert.That(model.GetRowCount(), Is.EqualTo(6));
        });
    }

    /// <summary>
    /// What a restore set aside for a column the model lacks waits on that model: a new model brings its own filters and sort,
    /// even one that has the column.
    /// </summary>
    [Test]
    public async Task ANewModelDropsWhatARestoreSetAsideForTheOldOneTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> withoutName = new([new Product("C00", "Name00", 0m)]);
        withoutName.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, withoutName).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(new Memento(null,
            new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "Name03" } }, [new("Name", false)])));
        DataGridModel<Product> withName = ModelOf(5);
        cut.Render(ps => ps.Add(g => g.Model, withName));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters(), Is.Empty);
            Assert.That(withName.GetSortedColumn(), Is.Null);
            Assert.That(withName.GetRowCount(), Is.EqualTo(5));
        });
    }

    /// <summary>Restores <see cref="Saved"/> each time it gets a new model, before the model's markup columns register.</summary>
    private sealed class SwapRestoringGrid : Grigliaria<Product>
    {
        [Parameter] public Memento? Saved { get; set; }

        private IDataGridModel<Product>? _restoredFor;

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (Saved != null && !ReferenceEquals(_restoredFor, Model))
            {
                _restoredFor = Model;
                RestoreMemento(Saved);
            }
        }
    }

    /// <summary>
    /// A restore made when the model changes, before the new model's markup columns register, waits on the new model and
    /// applies its filters and sort once they do.
    /// </summary>
    [Test]
    public void ARestoreMadeAtASwapAppliesToTheNewModelTest()
    {
        using BunitContext ctx = NewContext();
        static DataGridModel<Product> Rows() =>
            new(Enumerable.Range(0, 6).Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray());
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Name", "Code");
            builder.AddAttribute(2, "Label", "Code");
            builder.AddAttribute(3, "Property", (Func<Product, string>)(p => p.Code));
            builder.CloseComponent();
            builder.OpenComponent<GridColumn<Product, string>>(4);
            builder.AddAttribute(5, "Name", "Name");
            builder.AddAttribute(6, "Label", "Name");
            builder.AddAttribute(7, "Property", (Func<Product, string>)(p => p.Name));
            builder.CloseComponent();
        };
        var cut = ctx.Render<SwapRestoringGrid>(ps => ps.Add(g => g.Model, Rows()).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        Memento saved = new(null,
            new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "Name03" } }, [new("Name", false)]);
        DataGridModel<Product> second = Rows();
        cut.Render(ps => ps.Add(g => g.Model, second).Add(g => g.Saved, saved));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }));
            Assert.That(second.GetSortedColumn(), Is.EqualTo("Name"));
            Assert.That(second.GetRowCount(), Is.EqualTo(1));
        });
    }

    private sealed record TreeRow(string Code, string Name) : ITreeNode
    {
        public bool IsExpanded() => false;
        public int GetLevel() => 0;
        public bool IsLeaf() => true;
        public bool IsSubTotal() => false;
        public bool HasChildren() => false;
    }

    private static TreeGridModel<TreeRow> TreeOf(bool withName)
    {
        TreeGridModel<TreeRow> tree = new(Enumerable.Range(0, 6).Select(i => new TreeRow($"C{i:D2}", $"Name{i:D2}")))
        {
            OnSetExpand = (_, _) => ValueTask.CompletedTask,
        };
        tree.AddColumn("Code", new Column<TreeRow, string>("Code"), r => r.Code);
        if (withName)
            tree.AddColumn("Name", new Column<TreeRow, string>("Name"), r => r.Name);
        return tree;
    }

    private static readonly Memento NameFilteredAndSorted = new(null,
        new Dictionary<string, AFilterDef> { ["Name"] = new DefaultDef { Value = "Name03" } }, [new("Name", false)]);

    /// <summary>A tree grid's new model, which its inner grid gets wrapped, drops what a restore set aside for the old one.</summary>
    [Test]
    public async Task ANewTreeModelDropsWhatARestoreSetAsideForTheOldOneTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<Treegliaria<TreeRow>>(ps => ps.Add(g => g.Model, TreeOf(withName: false)).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(NameFilteredAndSorted));
        TreeGridModel<TreeRow> withName = TreeOf(withName: true);
        cut.Render(ps => ps.Add(g => g.Model, withName));
        Assert.Multiple(() =>
        {
            Assert.That(withName.GetSortedColumn(), Is.Null);
            Assert.That(withName.GetRowCount(), Is.EqualTo(6));
        });
    }

    /// <summary>Restores <see cref="Saved"/> each time it gets a new model, before its inner grid gets the model wrapped.</summary>
    private sealed class SwapRestoringTree : Treegliaria<TreeRow>
    {
        [Parameter] public Memento? Saved { get; set; }

        private ITreeGridModel<TreeRow>? _restoredFor;

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (Saved != null && !ReferenceEquals(_restoredFor, Model) && _restoredFor != null)
                RestoreMemento(Saved);
            _restoredFor = Model;
        }
    }

    /// <summary>
    /// A tree grid's restore made when the model changes runs before its inner grid gets the new model: what it sets aside
    /// waits on the new one, and applies once its column registers.
    /// </summary>
    [Test]
    public async Task ARestoreMadeAtATreeSwapAppliesToTheNewModelTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<SwapRestoringTree>(ps => ps.Add(g => g.Model, TreeOf(withName: false)).Add(g => g.Spinner, new NoSpinner()));
        TreeGridModel<TreeRow> second = TreeOf(withName: false);
        cut.Render(ps => ps.Add(g => g.Model, second).Add(g => g.Saved, NameFilteredAndSorted));
        await cut.InvokeAsync(() => second.AddColumn("Name", new Column<TreeRow, string>("Name"), r => r.Name));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(second.GetSortedColumn(), Is.EqualTo("Name"));
            Assert.That(second.GetRowCount(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A new <c>TreeColumn</c> wraps the same model again: it is not a new model, and what a restore set aside stays, applied
    /// once its column registers.
    /// </summary>
    [Test]
    public async Task ANewTreeColumnKeepsWhatARestoreSetAsideTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<TreeRow> tree = TreeOf(withName: false);
        var cut = ctx.Render<Treegliaria<TreeRow>>(ps => ps.Add(g => g.Model, tree).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(NameFilteredAndSorted));
        cut.Render(ps => ps.Add(g => g.TreeColumn, "Code"));
        await cut.InvokeAsync(() => tree.AddColumn("Name", new Column<TreeRow, string>("Name"), r => r.Name));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(tree.GetSortedColumn(), Is.EqualTo("Name"));
            Assert.That(tree.GetRowCount(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A reset after a new model goes back to the filters and sort the new model came with, not to those of the model the grid
    /// started with.
    /// </summary>
    [Test]
    public async Task AResetAfterANewModelGoesBackToItsOwnStartTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> first = ModelOf(5);
        first.ApplyFilters([new("Name", new DefaultDef { Value = "Name03" })]);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, first).Add(g => g.Spinner, new NoSpinner()));
        DataGridModel<Product> second = ModelOf(5);
        second.SetSortedColumn("Code", false);
        cut.Render(ps => ps.Add(g => g.Model, second));
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "Name01" }));
        await cut.InvokeAsync(() => second.SetSortedColumn("Price", true));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(null));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters(), Is.Empty);
            Assert.That(second.GetSortedColumn(), Is.EqualTo("Code"));
            Assert.That(second.GetSortType(), Is.EqualTo(SortType.Desc));
            Assert.That(second.GetRowCount(), Is.EqualTo(5));
        });
    }

    /// <summary>
    /// A restore made when the model changes does not become what a reset goes back to: the reset gives the new model as it
    /// came.
    /// </summary>
    [Test]
    public async Task AResetAfterARestoreAtASwapGoesBackToTheNewModelsStartTest()
    {
        using BunitContext ctx = NewContext();
        var cut = ctx.Render<SwapRestoringGrid>(ps => ps.Add(g => g.Model, ModelOf(5)).Add(g => g.Spinner, new NoSpinner()));
        DataGridModel<Product> second = ModelOf(5);
        cut.Render(ps => ps.Add(g => g.Model, second).Add(g => g.Saved, NameFilteredAndSorted));
        Assert.That(second.GetRowCount(), Is.EqualTo(1), "restored");
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(null));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters(), Is.Empty);
            Assert.That(second.GetSortedColumn(), Is.Null);
            Assert.That(second.GetRowCount(), Is.EqualTo(5));
        });
    }

    /// <summary>A new <c>TreeColumn</c> wraps the same model again: a reset still goes back to how that model started.</summary>
    [Test]
    public async Task AResetAfterANewTreeColumnGoesBackToTheModelsStartTest()
    {
        using BunitContext ctx = NewContext();
        TreeGridModel<TreeRow> tree = TreeOf(withName: true);
        tree.ApplyFilters([new("Name", new DefaultDef { Value = "Name03" })]);
        var cut = ctx.Render<Treegliaria<TreeRow>>(ps => ps.Add(g => g.Model, tree).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => cut.Instance.SetFilters(new Dictionary<string, AFilterDef>()));
        cut.Render(ps => ps.Add(g => g.TreeColumn, "Code"));
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(null));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetFilters().Keys, Is.EqualTo(new[] { "Name" }));
            Assert.That(tree.GetRowCount(), Is.EqualTo(1));
        });
    }
}
