using Bunit;
using Glazor.Grds;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// Grigliaria emits each column's width as a percentage: the columns with no <c>PreferredSize</c> are
/// reserved a share in proportion to how many of them there are, and the sized columns divide up what
/// remains. The arithmetic is a single integer division, which matters — computing the share and rescaling
/// it in two steps truncates twice and loses whole percentage points. These tests pin the emitted
/// percentages, because nothing else does and the failure is silent: a grid just renders narrower than it
/// should.
/// </summary>
public class ColumnWidthTests
{
    private sealed record Product(string Code, string Name, decimal Price);

    /// <param name="sizes">preferred size per column; a negative one means "auto"</param>
    private static DataGridModel<Product> ModelOf(params int[] sizes)
    {
        Product[] rows = Enumerable.Range(0, 3)
            .Select(i => new Product($"C{i}", $"N{i}", i)).ToArray();
        DataGridModel<Product> model = new(rows);
        for (int i = 0; i < sizes.Length; i++)
        {
            string name = $"c{i}";
            model.AddColumn(name, new Column<Product, string>(name)
            {
                // the first column must not be Fixed for the grid to accept the layout
                PreferredSize = sizes[i],
            }, p => p.Code);
        }
        return model;
    }

    /// <summary>
    /// The widths the grid emitted for its data columns, in order, "auto" included. Scoped to the grid's
    /// own table because sub-components render colgroups of their own, and filtered to percentages and
    /// "auto" because the template brackets the data columns with fixed-pixel spacer cols.
    /// </summary>
    private static List<string> EmittedWidths(IRenderedComponent<Grigliaria<Product>> grid) =>
        grid.FindAll("table.grigliaria > colgroup > col")
            .Select(c => c.GetAttribute("width") ?? "")
            .Where(w => w == "auto" || w.EndsWith('%'))
            .ToList();

    private static List<string> Render(params int[] sizes)
    {
        using BunitContext ctx = NewContext();
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps
            .Add(g => g.Model, ModelOf(sizes))
            .Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 3));
        return EmittedWidths(grid);
    }

    /// <summary>
    /// With every column sized, the reserve is 100% and the widths are each column's plain share. This is
    /// the common case and the one the two-division form already got right, so it is the guard against the
    /// fix having changed anything it should not have.
    /// </summary>
    [Test]
    public void AllColumnsSizedShareTheFullWidthTest()
    {
        Assert.That(Render(33, 67), Is.EqualTo(new[] { "33%", "67%" }));
        Assert.That(Render(10, 10, 10), Is.EqualTo(new[] { "33%", "33%", "33%" }));
    }

    /// <summary>
    /// One unsized column among three reserves a third of the width for it, leaving 67% to be split
    /// between the two sized ones. 33 of a 100-unit total therefore renders at 22% — its share of that
    /// 67 — and 67 at 44%. This is the case a two-step calculation gets wrong.
    /// </summary>
    [Test]
    public void AnAutoColumnLeavesTheSizedColumnsAProportionalShareTest()
    {
        // two sized (33, 67) plus one auto: 1 of 3 columns is unsized, so the sized ones get 67%
        Assert.That(Render(33, 67, -1), Is.EqualTo(new[] { "22%", "44%", "auto" }));
    }

    /// <summary>
    /// Three sized and three auto: half the columns carry no size, so the sized ones share half the width
    /// and the rest is left to the auto columns. Asserted as a range rather than exact percentages,
    /// because the point is that the total lands on the reserve without leaking points to truncation.
    /// </summary>
    [Test]
    public void HalfAutoColumnsLeaveHalfTheWidthTest()
    {
        List<string> widths = Render(8, 5, 6, -1, -1, -1);
        Assert.Multiple(() =>
        {
            Assert.That(widths.Count(w => w == "auto"), Is.EqualTo(3), "the unsized columns render auto");
            int sized = widths.Where(w => w != "auto")
                              .Sum(w => int.Parse(w.TrimEnd('%')));
            Assert.That(sized, Is.LessThanOrEqualTo(50), "the sized columns must not exceed their reserve");
            Assert.That(sized, Is.GreaterThanOrEqualTo(47), "and must not lose points to truncation");
        });
    }

    /// <summary>
    /// No column carries a size, so there is nothing to distribute and every column renders auto rather
    /// than dividing by a zero total.
    /// </summary>
    [Test]
    public void NoSizedColumnsRenderAllAutoTest()
    {
        Assert.That(Render(-1, -1), Is.EqualTo(new[] { "auto", "auto" }));
    }

    /// <summary>
    /// A column declared in code with no size takes the width its value type gives a markup column, string 20 and decimal 8,
    /// not one display unit; a negative size still renders auto.
    /// </summary>
    [Test]
    public void ACodeColumnWithNoSizeIsSizedByTypeTest()
    {
        DataGridModel<Product> model = new([new("C0", "N0", 1m)]);
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price"), p => p.Price);
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = -1 }, p => p.Code);
        using BunitContext ctx = NewContext();
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        Assert.Multiple(() =>
        {
            Assert.That(model.GetColumn(0).GetPreferredSize(), Is.EqualTo(20));
            Assert.That(model.GetColumn(1).GetPreferredSize(), Is.EqualTo(8));
            Assert.That(EmittedWidths(grid), Is.EqualTo(new[] { "47%", "19%", "auto" }), "20:8 of the two thirds left beside the auto one");
        });
    }

    /// <summary>A <c>DateTimeOffset</c> column with no size is as wide as a <c>DateTime</c> one.</summary>
    [Test]
    public void ADateTimeOffsetColumnIsSizedAsADateTimeTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new Column<Product, DateTimeOffset>("At").GetPreferredSize(), Is.EqualTo(12));
            Assert.That(new Column<Product, DateTimeOffset?>("At").GetPreferredSize(), Is.EqualTo(12));
        });
    }
}
