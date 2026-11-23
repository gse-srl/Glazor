using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Glazor.Tests;

/// <summary>
/// An EnumDef pushed down on an EF model finds the rows Includes finds in memory, whether it is typed by the column's nullable
/// enum or by the enum under it.
/// </summary>
public class EnumDefPushDownTests
{
    public enum Status { On, Off, Hold }

    public class Row
    {
        public int Id { get; set; }
        public Status State { get; set; }
        public Status? MaybeState { get; set; }
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
            for (int i = 0; i < 9; i++)
                ctx.Rows.Add(new Row { State = (Status)(i % 3), MaybeState = i % 3 == 0 ? null : (Status)(i % 3) });
            ctx.SaveChanges();
        }
        using (RowsContext ctx = _factory.CreateDbContext())
            _rows = ctx.Rows.AsNoTracking().OrderBy(r => r.Id).ToList();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public async Task EnumDefPushedDownFindsTheRowsIncludesFindsTest(
        [Values] bool nullableColumn,
        [Values] bool nullableDef,
        [Values] bool includeNull)
    {
        var model = new EFDataGridModel<RowsContext, Row>()
        {
            DbContextFactory = _factory,
            BaseQueryFn = (ctx, _) => ctx.Rows.OrderBy(r => r.Id),
            ItemKeyExpr = r => r.Id,
        };
        if (nullableColumn)
            model.AddEFColumn("C", new Column<Row, Status?>("C"), r => r.MaybeState);
        else
            model.AddEFColumn("C", new Column<Row, Status>("C"), r => r.State);
        EnumDef def = new(nullableDef ? typeof(Status?) : typeof(Status), true) { IncludeNull = includeNull };
        def.Selected.Remove(Status.Off);
        Func<Row, object?> value = nullableColumn ? r => r.MaybeState : r => r.State;
        List<int> expected = _rows.Where(r => def.Includes(value(r))).Select(r => r.Id).ToList();
        model.ApplyFilters([new("C", def)]);
        await model.ReadAllDataAsync(CancellationToken.None);
        List<int> found = Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
        Assert.That(found, Is.EqualTo(expected), "pushed down, the same rows as in memory");
    }
}
