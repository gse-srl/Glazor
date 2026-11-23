using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Glazor.Tests;

/// <summary>
/// DefaultDef is the filter of the columns that have none of their own: strings, and types such as TimeSpan, object or a
/// custom type stored through a value converter. Pushed down on an EF model it must find the rows Includes finds in memory.
/// </summary>
public class DefaultDefPushDownTests
{
    public readonly record struct Code(string Value)
    {
        public override string ToString() => Value;
    }

    public sealed record Tag(string Value)
    {
        public override string ToString() => Value;
    }

    public class Row
    {
        public int Id { get; set; }
        public int Qty { get; set; }
        public string? Note { get; set; }
        public TimeSpan? Lap { get; set; }
        public Code Code { get; set; }
        public Code? AltCode { get; set; }
        public Tag? Tag { get; set; }
    }

    public class RowsContext(DbContextOptions<RowsContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Row>().Property(r => r.Code).HasConversion(c => c.Value, s => new Code(s));
            mb.Entity<Row>().Property(r => r.AltCode).HasConversion(c => c!.Value.Value, s => new Code(s));
            mb.Entity<Row>().Property(r => r.Tag).HasConversion(t => t!.Value, s => new Tag(s));
        }
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
            for (int i = 0; i < 14; i++)
            {
                bool missing = i % 3 == 0;
                ctx.Rows.Add(new Row
                {
                    Qty = i * 7,
                    Note = missing ? null : $"n{i}e",
                    Lap = missing ? null : TimeSpan.FromHours(i),
                    Code = new Code($"c{i}x"),
                    AltCode = missing ? null : new Code($"k{i}e"),
                    Tag = missing ? null : new Tag($"t{i}a"),
                });
            }
            ctx.SaveChanges();
        }
        using (RowsContext ctx = _factory.CreateDbContext())
            _rows = ctx.Rows.AsNoTracking().OrderBy(r => r.Id).ToList();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private static readonly Dictionary<string, Action<EFDataGridModel<RowsContext, Row>>> columns = new()
    {
        ["Qty as object"] = m => Add<object>(m, r => (object)r.Qty),
        ["Note as object"] = m => Add<object?>(m, r => (object?)r.Note),
        ["Lap"] = m => Add(m, r => r.Lap),
        ["Code"] = m => Add(m, r => r.Code),
        ["AltCode"] = m => Add(m, r => r.AltCode),
        ["Tag"] = m => Add(m, r => r.Tag),
    };

    // Filtered only: Code and Tag have no order, and a sortable column must be comparable even when the database sorts it
    private static void Add<VT>(EFDataGridModel<RowsContext, Row> model, Expression<Func<Row, VT>> property)
        => model.AddEFColumn("C", new Column<Row, VT>("C") { Sortable = false }, property);

    private static readonly Dictionary<string, Func<Row, object?>> values = new()
    {
        ["Qty as object"] = r => r.Qty,
        ["Note as object"] = r => r.Note,
        ["Lap"] = r => r.Lap,
        ["Code"] = r => r.Code,
        ["AltCode"] = r => r.AltCode,
        ["Tag"] = r => r.Tag,
    };

    [Test]
    public void DefaultDefFiltersTheColumnsWithoutAFilterOfTheirOwnTest(
        [Values("Qty as object", "Note as object", "Lap", "Code", "AltCode", "Tag")] string column,
        [Values("1", "")] string value,
        [Values] bool includeNull)
    {
        var model = new EFDataGridModel<RowsContext, Row>()
        {
            DbContextFactory = _factory,
            BaseQueryFn = (ctx, _) => ctx.Rows.OrderBy(r => r.Id),
            ItemKeyExpr = r => r.Id,
            MinRowsToRead = 5,
        };
        columns[column](model);
        DefaultDef def = new() { Value = value, IncludeNull = includeNull };
        List<int> expected = _rows.Where(r => def.Includes(values[column](r))).Select(r => r.Id).ToList();
        model.ApplyFilters(new[] { new KeyValuePair<string, AFilterDef>("C", def) });
        model.ReadAllDataAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        List<int> found = Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
        Assert.That(found, Is.EqualTo(expected), "pushed down, the same rows as in memory");
    }
}
