using Glazor.Grds;
using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;

namespace Glazor.Tests;

public class PocoTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void GridModelTest()
    {
        DataGridModel<RProduct> model = new([
            new RProduct("Valentino", 900),
            new RProduct("Vallers", 1_000),
            new RProduct("Otta", 1_200),
            new RProduct("Ciucci", 1_300),
            new RProduct("Ben", 2_000),
            ]);
        model.AddColumn("Name", new Column<RProduct, string>("nome"), ci => ci.Name);
        model.AddColumn("Len", new Column<RProduct, int>("lunghezza"), ci => ci.Name.Length);
        model.AddColumn("Price", new Column<RProduct, decimal>("prezzo"), ci => ci.Price);
        model.SetSortedColumn("Name", true);
        model.Sync();
        Assert.That(model.GetColumnCount(), Is.EqualTo(3));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(5));
        Assert.That(model.GetRowCount(), Is.EqualTo(5));
        Assert.That(model.GetItem(0).Name, Is.EqualTo("Ben"));
        //
        model.ApplyFilters([new("Name", new DefaultDef() { Value = "le", Contains = true })]);
        model.Sync();
        Assert.That(model.GetColumnCount(), Is.EqualTo(3));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(2));
        Assert.That(model.GetRowCount(), Is.EqualTo(2));
        Assert.That(model.GetItem(0).Name, Is.EqualTo("Valentino"));
        //
        model.ApplyFilters([]);
        model.Sync();
        Assert.That(model.GetColumnCount(), Is.EqualTo(3));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(5));
        Assert.That(model.GetRowCount(), Is.EqualTo(5));
        Assert.That(model.GetItem(0).Name, Is.EqualTo("Ben"));
        //
        model.ApplyFilters([
            new("Name", new DefaultDef() { Value = "le", Contains = true }),
            new("Len", new NumberDef<int>() { To = 7 })
            ]);
        model.Sync();
        Assert.That(model.GetColumnCount(), Is.EqualTo(3));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(1));
        Assert.That(model.GetRowCount(), Is.EqualTo(1));
        Assert.That(model.GetItem(0).Name, Is.EqualTo("Vallers"));
        //
        model.ApplyFilters([]);
        model.Sync();
        Assert.That(model.GetColumnCount(), Is.EqualTo(3));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(5));
        Assert.That(model.GetRowCount(), Is.EqualTo(5));
        Assert.That(model.GetItem(0).Name, Is.EqualTo("Ben"));
    }

    [Test]
    public void GetCellClassFnTest()
    {
        Poco poco = new(null, null);
        Column<Poco, DateOnly?> dtcol = new("testValueType")
        {
            CellClassFn = pv => pv.Value == null ? "NULL" : "NOTNULL"
        };
        Assert.That(dtcol.CellClassFn(new DecoratorContext<Poco, DateOnly?>(poco, DateOnly.FromDateTime(DateTime.Now), 1, dtcol)), Is.EqualTo("NOTNULL"));
        Assert.That(dtcol.CellClassFn(new DecoratorContext<Poco, DateOnly?>(poco, null, 1, dtcol)), Is.EqualTo("NULL"));
        //
        Column<Poco, string?> scol = new("testRefType")
        {
            CellClassFn = pv => pv.Value == null ? "NULL" : "NOTNULL"
        };
        Assert.That(scol.CellClassFn(new DecoratorContext<Poco, string?>(poco, "Vallers", 1, dtcol)), Is.EqualTo("NOTNULL"));
        Assert.That(scol.CellClassFn(new DecoratorContext<Poco, string?>(poco, null, 1, dtcol)), Is.EqualTo("NULL"));
    }

    [Test]
    public void GetCellClassFnTest2()
    {
        Poco poco = new(null, null);
        IColumn<Poco> dtcol = new Column<Poco, DateOnly?>("testValueType")
        {
            CellClassFn = pv => pv.Value == null ? "NULL" : "NOTNULL"
        };
        Assert.That(dtcol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, DateOnly.FromDateTime(DateTime.Now), 1, dtcol)), Is.EqualTo("NOTNULL"));
        Assert.That(dtcol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, null, 1, dtcol)), Is.EqualTo("NULL"));
        //
        IColumn<Poco> scol = new Column<Poco, string?>("testRefType")
        {
            CellClassFn = pv => pv.Value == null ? "NULL" : "NOTNULL"
        };
        Assert.That(scol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, "Vallers", 1, dtcol)), Is.EqualTo("NOTNULL"));
        Assert.That(scol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, null, 1, dtcol)), Is.EqualTo("NULL"));
    }

    [Test]
    public void GetCellClassFnTest3()
    {
        Poco poco = new(null, null);
        IColumn<Poco> dtcol = new Column<Poco, DateOnly>("testValueType")
        {
            CellClassFn = pv => "NOTNULL"
        };
        Assert.That(dtcol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, DateOnly.FromDateTime(DateTime.Now), 1, dtcol)), Is.EqualTo("NOTNULL"));
        // A null cannot be unboxed into DateOnly: a named error, not a NullReferenceException from the cast
        var ex = Assert.Throws<InvalidOperationException>(() => dtcol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, null, 1, dtcol)));
        Assert.That(ex!.Message, Does.Contain("testValueType").And.Contain(nameof(DateOnly)));
        //
        IColumn<Poco> scol = new Column<Poco, string>("testRefType")
        {
            CellClassFn = pv => pv.Value == null ? "NULL" : "NOTNULL"
        };
        Assert.That(scol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, "Vallers", 1, dtcol)), Is.EqualTo("NOTNULL"));
        Assert.That(scol.GetCellClassFn()!.Invoke(new DecoratorContext<Poco, object>(poco, null, 1, dtcol)), Is.EqualTo("NULL"));
    }

    [Test]
    public void RegisterColumn()
    {
        {
            GridModel<Poco> model = new();
            model.AddColumn(nameof(Poco.DTValue), new Column<Poco, DateOnly?>("testValueType"), p => p.DTValue);
            model.AddColumn(nameof(Poco.SValue), new Column<Poco, string?>("testRefType"), p => p.SValue);
            Assert.That(model.GetColumnCount(), Is.EqualTo(2));
        }
        {
            GridModel<Poco> model = new();
            model.AddColumn(nameof(Poco.DTValue), new Column<Poco, DateOnly>("testValueType"), p => p.DTValue!.Value);
            model.AddColumn(nameof(Poco.SValue), new Column<Poco, string>("testRefType"), p => p.SValue!);
            Assert.That(model.GetColumnCount(), Is.EqualTo(2));
        }
    }

    [Test]
    public void RegisterColumn2()
    {
        {
            GridModel<Poco2> model = new();
            model.AddColumn(nameof(Poco2.DTValue), new Column<Poco2, DateOnly?>("testValueType"), p => p.DTValue);
            model.AddColumn(nameof(Poco2.SValue), new Column<Poco2, string?>("testRefType"), p => p.SValue);
            Assert.That(model.GetColumnCount(), Is.EqualTo(2));
        }
        {
            GridModel<Poco2> model = new();
            model.AddColumn(nameof(Poco2.DTValue), new Column<Poco2, DateOnly>("testValueType"), p => p.DTValue);
            model.AddColumn(nameof(Poco2.SValue), new Column<Poco2, string>("testRefType"), p => p.SValue);
            Assert.That(model.GetColumnCount(), Is.EqualTo(2));
        }
    }

    record RProduct(string Name, decimal Price);

    private record Poco(DateOnly? DTValue, string? SValue);
    private record Poco2(DateOnly DTValue, string SValue);
}
