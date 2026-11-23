using Bunit;
using Glazor.Grds;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The resizer factories and the idle tracker check their arguments where they are given: before, equal bounds divided by
/// zero and clamped silently to the minimum, and a timeout of zero reported the user idle on the first render.
/// </summary>
public class ResizerFactoryTests
{
    private sealed record Product(string Code);

    [Test]
    public void FactoriesRejectDegenerateBoundsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridHorzResizers.Fixed(0))!.ParamName, Is.EqualTo("dispUnits"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridHorzResizers.Proportional(50, 50, 400, 1600))!.ParamName, Is.EqualTo("maxDispUnits"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridHorzResizers.Proportional(50, 150, 800, 800))!.ParamName, Is.EqualTo("referencedMaxGridWidth"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridHorzResizers.DispUnitWidth(50, 150, 0))!.ParamName, Is.EqualTo("dispUnitWidth"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridHorzResizers.DispUnitWidth(150, 50, 12))!.ParamName, Is.EqualTo("maxDispUnits"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridVertResizers.Fixed(0))!.ParamName, Is.EqualTo("pageSize"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridVertResizers.Proportional("height: 60vh", 10, 10, 200, 800))!.ParamName, Is.EqualTo("maxPageSize"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => GridVertResizers.RowHeight("height: 60vh", 20, 4))!.ParamName, Is.EqualTo("maxPageSize"));
            Assert.That(Assert.Throws<ArgumentException>(() => GridVertResizers.RowHeight(" ", 4, 20))!.ParamName, Is.EqualTo("heightRefStyles"));
        });
    }

    [Test]
    public void ValidFactoriesComputeAsBeforeTest()
    {
        Grigliaria<Product> grid = new();   // HeaderHeight 50, RowHeight 40
        Assert.Multiple(() =>
        {
            Assert.That(GridHorzResizers.Fixed(80).GetDisplayedUnits(grid, 1234), Is.EqualTo(80));
            Assert.That(GridHorzResizers.Proportional(50, 150, 400, 1600).GetDisplayedUnits(grid, 1000), Is.EqualTo(100), "half way between the references");
            Assert.That(GridHorzResizers.Proportional(50, 150, 400, 1600).GetDisplayedUnits(grid, 100), Is.EqualTo(50), "clamped at the minimum");
            Assert.That(GridHorzResizers.DispUnitWidth(50, 150, 12).GetDisplayedUnits(grid, 1200), Is.EqualTo(100));
            Assert.That(GridVertResizers.Fixed(7).GetPageSize(grid, 1234), Is.EqualTo(7));
            Assert.That(GridVertResizers.Proportional("height: 60vh", 4, 20, 200, 1000).GetPageSize(grid, 650), Is.EqualTo(12), "600 px above the header, half way between the references");
            Assert.That(GridVertResizers.RowHeight("height: 60vh", 4, 20).GetPageSize(grid, 450), Is.EqualTo(9), "400 px of rows of 40, less the heuristic row");
        });
    }

    /// <summary>
    /// Once the grid has been drawn, the RowHeight resizer sizes the page by what was drawn: rows taller than <c>RowHeight</c>, a
    /// header and a footer settle on the largest page that fits the box, where the estimate alone overflowed it. The browser
    /// reports the body and the content one at a time, each followed by a render, and the page size only goes down.
    /// </summary>
    [Test]
    public async Task RowHeightSettlesOnThePageThatFitsWhatIsDrawnTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        DataGridModel<Product> model = new(Enumerable.Range(0, 50).Select(i => new Product($"C{i}")));
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.RowHeight, 32).Add(g => g.HeaderHeight, 40)
            .Add(g => g.VertResizer, GridVertResizers.RowHeight("height: 40vh;", 4, 20)));
        var observers = cut.FindComponents<ResizeObserver>();   // in the markup's order: the body, the height box, the whole content
        const double box = 307, besidesTheRows = 87, rowDrawn = 34, bodyBesidesTheRows = 18;   // as a browser drew them
        double Body(int rows) => bodyBesidesTheRows + rows * rowDrawn;

        await cut.InvokeAsync(() => observers[1].Instance.ProcessResize(600, box));
        List<int> pageSizes = [cut.Instance.GetPageSize()];
        for (int round = 0; round < 4; round++)
        {
            int drawn = cut.Instance.GetPageSize();
            await cut.InvokeAsync(() => observers[0].Instance.ProcessResize(600, Body(drawn)));
            pageSizes.Add(cut.Instance.GetPageSize());
            await cut.InvokeAsync(() => observers[2].Instance.ProcessResize(600, besidesTheRows + Body(drawn)));
            pageSizes.Add(cut.Instance.GetPageSize());
        }
        Assert.Multiple(() =>
        {
            Assert.That(pageSizes[0], Is.EqualTo(7), "the estimate before anything is drawn");
            Assert.That(besidesTheRows + Body(7), Is.GreaterThan(box), "which overflows the box");
            Assert.That(pageSizes, Is.Ordered.Descending, "it only goes down");
            Assert.That(pageSizes[^1], Is.EqualTo(5));
            Assert.That(besidesTheRows + Body(5), Is.LessThanOrEqualTo(box), "the page settled on fits");
            Assert.That(besidesTheRows + Body(6), Is.GreaterThan(box), "and one row more would not");
        });

        const double taller = 400;
        await cut.InvokeAsync(() => observers[1].Instance.ProcessResize(600, taller));
        int grown = cut.Instance.GetPageSize();
        Assert.Multiple(() =>
        {
            Assert.That(grown, Is.EqualTo(8), "a taller box takes the rows that fit in one step");
            Assert.That(besidesTheRows + Body(grown), Is.LessThanOrEqualTo(taller));
            Assert.That(besidesTheRows + Body(grown + 1), Is.GreaterThan(taller));
        });
    }

    /// <summary>
    /// Rows that change height without the page changing size, as text wraps in a narrower window and unwraps in a wider one,
    /// are compared only with rows drawn as tall: before, a page drawn with the taller rows took part in the row height, which
    /// came out so large that the page stopped a row short of the box.
    /// </summary>
    [Test]
    public async Task RowHeightRefitsWhenTheRowsChangeHeightTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        DataGridModel<Product> model = new(Enumerable.Range(0, 50).Select(i => new Product($"C{i}")));
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.RowHeight, 32).Add(g => g.HeaderHeight, 40)
            .Add(g => g.VertResizer, GridVertResizers.RowHeight("height: 40vh;", 4, 20)));
        var observers = cut.FindComponents<ResizeObserver>();   // in the markup's order: the body, the height box, the whole content
        const double box = 340, besidesTheRows = 87, bodyBesidesTheRows = 18;

        async Task<int> SettleAsync(double rowDrawn)
        {
            for (int round = 0; round < 8; round++)
            {
                int drawn = cut.Instance.GetPageSize();
                double body = bodyBesidesTheRows + drawn * rowDrawn;
                await cut.InvokeAsync(() => observers[0].Instance.ProcessResize(600, body));
                await cut.InvokeAsync(() => observers[2].Instance.ProcessResize(600, besidesTheRows + body));
            }
            return cut.Instance.GetPageSize();
        }
        bool Fits(int rows, double rowDrawn) => besidesTheRows + bodyBesidesTheRows + rows * rowDrawn <= box;

        await cut.InvokeAsync(() => observers[1].Instance.ProcessResize(600, box));
        int[] pages = [await SettleAsync(36), await SettleAsync(40), await SettleAsync(36)];
        Assert.Multiple(() =>
        {
            Assert.That(pages, Is.EqualTo(new[] { 6, 5, 6 }));
            Assert.That(Fits(6, 36) && !Fits(7, 36), "6 rows of 36 are what fit");
            Assert.That(Fits(5, 40) && !Fits(6, 40), "5 rows of 40 are what fit");
        });
    }

    [Test]
    public void IdleTrackerRejectsANonPositiveTimeoutTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLogging();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Render<IdleTracker>(ps => ps
            .Add(t => t.TimeoutMilliseconds, 0)
            .Add(t => t.ChildContent, (RenderFragment<IdleTracker>)(_ => b => b.AddContent(0, "x")))));
        Assert.That(ex!.ParamName, Is.EqualTo("TimeoutMilliseconds"));
        Assert.DoesNotThrow(() => ctx.Render<IdleTracker>(ps => ps
            .Add(t => t.TimeoutMilliseconds, 1000)
            .Add(t => t.ChildContent, (RenderFragment<IdleTracker>)(_ => b => b.AddContent(0, "x")))));
    }
}
