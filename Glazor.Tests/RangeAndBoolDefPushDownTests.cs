using System.Linq.Expressions;
using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Glazor.Tests;

/// <summary>
/// NumberDef, DateDef and BoolDef pushed down on an EF model find the rows Includes finds in memory, null values included.
/// </summary>
public class RangeAndBoolDefPushDownTests
{
    public class Row
    {
        public int Id { get; set; }
        public int? Num { get; set; }
        public DateTime? When { get; set; }
        public bool? Flag { get; set; }
    }

    public class RowsContext(DbContextOptions<RowsContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class RowsContextFactory(DbContextOptions<RowsContext> options) : IDbContextFactory<RowsContext>
    {
        public RowsContext CreateDbContext() => new(options);
    }

    private SqliteConnection _connection = null!;
    private RowsContextFactory _factory = null!;
    private List<Row> _rows = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new RowsContextFactory(new DbContextOptionsBuilder<RowsContext>().UseSqlite(_connection).Options);
        using (RowsContext ctx = _factory.CreateDbContext())
        {
            ctx.Database.EnsureCreated();
            for (int i = 0; i < 12; i++)
                ctx.Rows.Add(new Row
                {
                    Num = i % 4 == 0 ? null : i,
                    When = i % 5 == 0 ? null : new DateTime(2026, 1, 1, i, 30, 0),
                    Flag = i % 3 == 0 ? null : i % 2 == 0,
                });
            ctx.SaveChanges();
        }
        using (RowsContext ctx = _factory.CreateDbContext())
            _rows = ctx.Rows.AsNoTracking().OrderBy(r => r.Id).ToList();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private async Task<(List<int> Found, List<int> Expected)> FilterAsync<VT>(Func<Row, VT> value, Expression<Func<Row, VT>> expr,
        AFilterDef<VT> def)
    {
        var model = new EFDataGridModel<RowsContext, Row>()
        {
            DbContextFactory = _factory,
            BaseQueryFn = (ctx, _) => ctx.Rows.OrderBy(r => r.Id),
            ItemKeyExpr = r => r.Id,
        };
        model.AddEFColumn("C", new Column<Row, VT>("C"), expr);
        List<int> expected = _rows.Where(r => def.Includes(value(r))).Select(r => r.Id).ToList();
        model.ApplyFilters([new("C", def)]);
        await model.ReadAllDataAsync(CancellationToken.None);
        return (Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList(), expected);
    }

    [Test]
    public async Task NumberDefPushedDownFindsTheRowsIncludesFindsTest(
        [Values] bool includeNull,
        [Values(null, 3)] int? from,
        [Values(null, 9)] int? to)
    {
        var (found, expected) = await FilterAsync(r => r.Num, r => r.Num,
            new NumberDef<int> { IncludeNull = includeNull, From = from, To = to });
        Assert.That(found, Is.EqualTo(expected), "pushed down, the same rows as in memory");
    }

    [Test]
    public async Task DateDefPushedDownFindsTheRowsIncludesFindsTest([Values] bool includeNull)
    {
        var (found, expected) = await FilterAsync(r => r.When, r => r.When,
            new DateDef<DateTime> { IncludeNull = includeNull, From = new DateTime(2026, 1, 1, 3, 0, 0), To = new DateTime(2026, 1, 1, 8, 30, 0) });
        Assert.That(found, Is.EqualTo(expected), "pushed down, the same rows as in memory");
    }

    [Test]
    public async Task BoolDefPushedDownFindsTheRowsIncludesFindsTest([Values] bool includeNull, [Values] BoolDef.BoolValue value)
    {
        var (found, expected) = await FilterAsync(r => r.Flag, r => r.Flag,
            new BoolDef { IncludeNull = includeNull, Value = value });
        Assert.That(found, Is.EqualTo(expected), "pushed down, the same rows as in memory");
    }
}
