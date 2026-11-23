using Bunit;
using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Misc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// EFDistinctDef, the multi-select over the distinct values a column has in a query, and what it stands on: a set whose dialog
/// rows wrap the values, so that the column may hold a value type, which DynaSetDef cannot filter.
/// <para>
/// Nine rows: owner 1 has the statuses open, closed and none and the ranks 2, 10 and none; owner 2 has the status held and the
/// rank 7.
/// </para>
/// </summary>
public class DistinctDefTests
{
    public class Row
    {
        public int Id { get; set; }
        public int Owner { get; set; }
        public string? Status { get; set; }
        public int? Rank { get; set; }
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
            string?[] statuses = ["open", "closed", null];
            int?[] ranks = [10, 2, null];   // read in this order, so a dialog that lists 2 first has sorted them
            for (int i = 0; i < 6; i++)
                ctx.Rows.Add(new Row { Owner = 1, Status = statuses[i % 3], Rank = ranks[i % 3] });
            for (int i = 0; i < 3; i++)
                ctx.Rows.Add(new Row { Owner = 2, Status = "held", Rank = 7 });
            ctx.SaveChanges();
        }
        using (RowsContext ctx = _factory.CreateDbContext())
            _rows = ctx.Rows.AsNoTracking().OrderBy(r => r.Id).ToList();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    /// <summary>The definition a column of the grid of owner 1 would carry: its rows are the query's, not the table's.</summary>
    private EFDistinctDef<RowsContext, Row, VT> OwnerOne<VT>(System.Linq.Expressions.Expression<Func<Row, VT?>> column, bool startFull = true) => new(startFull)
    {
        DbContextFactoryFn = () => _factory,
        QueryFn = db => db.Rows.Where(r => r.Owner == 1),
        ColumnExpr = column,
    };

    private EFDataGridModel<RowsContext, Row> Model() => new()
    {
        DbContextFactory = _factory,
        BaseQueryFn = (ctx, _) => ctx.Rows.OrderBy(r => r.Id),
        ItemKeyExpr = r => r.Id,
    };

    private static async Task<List<int>> IdsAsync(EFDataGridModel<RowsContext, Row> model)
    {
        await model.ReadAllDataAsync(CancellationToken.None);
        return Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
    }

    /// <summary>
    /// The values offered are those of the rows the query gives, each once and without the null: the values of the whole table
    /// would offer a user what the grid, narrowed to his rows, never shows.
    /// </summary>
    [Test]
    public async Task OffersTheDistinctValuesOfTheQueryTest()
    {
        var statuses = OwnerOne<string>(r => r.Status);
        var ranks = OwnerOne<int?>(r => r.Rank);

        IList<DistinctRowValue<string>> offered = await statuses.PopulateAsync();
        IList<DistinctRowValue<int?>> offeredRanks = await ranks.PopulateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(offered.Select(v => v.Value), Is.EquivalentTo(new[] { "open", "closed" }), "not held, which is of owner 2");
            Assert.That(offeredRanks.Select(v => v.Value), Is.EquivalentTo(new int?[] { 2, 10 }), "a value type column too");
            Assert.That(statuses.Selected, Is.EquivalentTo(new[] { "open", "closed" }), "started full: every value selected");
            Assert.That(statuses.IsVoid(), Is.True, "so it filters nothing");
        });
    }

    /// <summary>The token given to PopulateAsync reaches the query: cancelled, nothing is read and the set is left as it was.</summary>
    [Test]
    public void PopulateIsCancelledByItsTokenTest()
    {
        var statuses = OwnerOne<string>(r => r.Status);

        Assert.That(async () => await statuses.PopulateAsync(new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(statuses.StartFull(), Is.True, "not populated");
    }

    /// <summary>
    /// A selection is pushed down to the rows Includes finds in memory, on a string column and on the value type columns that a
    /// set over the column's own values could not take, nullable or not.
    /// </summary>
    [Test]
    public async Task PushedDownFindsTheRowsIncludesFindsTest([Values("Status", "Rank", "Owner")] string column, [Values] bool includeNull)
    {
        var model = Model();
        AFilterDef def;
        Func<Row, object?> value;
        switch (column)
        {
            case "Status":
                model.AddEFColumn(column, new Column<Row, string?>(column), r => r.Status);
                var statuses = new EFDistinctDef<RowsContext, Row, string>(false) { IncludeNull = includeNull };
                statuses.Selected.Add("open");
                statuses.Selected.Add("held");
                (def, value) = (statuses, r => r.Status);
                break;
            case "Rank":
                model.AddEFColumn(column, new Column<Row, int?>(column), r => r.Rank);
                var ranks = new EFDistinctDef<RowsContext, Row, int?>(false) { IncludeNull = includeNull };
                ranks.Selected.Add(10);
                ranks.Selected.Add(7);
                (def, value) = (ranks, r => r.Rank);
                break;
            default:
                model.AddEFColumn(column, new Column<Row, int>(column), r => r.Owner);
                var owners = new EFDistinctDef<RowsContext, Row, int>(false) { IncludeNull = includeNull };
                owners.Selected.Add(2);
                (def, value) = (owners, r => r.Owner);
                break;
        }
        List<int> expected = _rows.Where(r => def.Includes(value(r))).Select(r => r.Id).ToList();

        model.ApplyFilters([new(column, def)]);

        Assert.That(await IdsAsync(model), Is.EqualTo(expected), "pushed down, the same rows as in memory");
        Assert.That(expected, Is.Not.Empty.And.Count.LessThan(_rows.Count), "a real filter");
    }

    /// <summary>
    /// Every value selected without null is any value but null, in memory and pushed down: a value added after the dialog read
    /// them passes too. With one value left out, the selection is the others only.
    /// </summary>
    [Test]
    public async Task EveryValueSelectedWithoutNullPassesAValueAddedLaterTest()
    {
        var def = OwnerOne<string>(r => r.Status);
        await def.PopulateAsync();   // open, closed
        def.IncludeNull = false;
        int added;
        using (RowsContext ctx = _factory.CreateDbContext())
        {
            Row row = new() { Owner = 1, Status = "new" };
            ctx.Rows.Add(row);
            ctx.SaveChanges();
            added = row.Id;
        }
        var model = Model();
        model.AddEFColumn("Status", new Column<Row, string?>("Status"), r => r.Status);
        model.ApplyFilters([new("Status", def)]);
        List<int> expected = _rows.Where(r => r.Status != null).Select(r => r.Id).Append(added).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(def.IsVoid(), Is.False, "the nulls are left out");
            Assert.That(def.Includes("new"), Is.True, "in memory");
            Assert.That(def.Includes(null), Is.False);
            Assert.That(await IdsAsync(model), Is.EqualTo(expected), "pushed down");
        });

        def.Selected.Remove("closed");
        Assert.That(def.Includes("new"), Is.False, "one value left out: the others only");
    }

    /// <summary>
    /// Every value selected, opened again once a value was added, shows that value selected too, so OK as it is keeps its rows;
    /// with a value left out, the one added is not selected.
    /// </summary>
    [Test]
    public async Task ReopenedWithEveryValueSelectedSelectsAValueAddedSinceTest()
    {
        var def = OwnerOne<string>(r => r.Status);
        await def.PopulateAsync();   // open, closed
        def.IncludeNull = false;
        var partial = def.Clone();
        partial.Selected.Remove("closed");
        using (RowsContext ctx = _factory.CreateDbContext())
        {
            ctx.Rows.Add(new Row { Owner = 1, Status = "new" });
            ctx.SaveChanges();
        }
        var reopened = def.Clone();   // as the dialog does
        await reopened.PopulateAsync();
        await partial.PopulateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(reopened.Selected, Is.EquivalentTo(new[] { "open", "closed", "new" }));
            Assert.That(reopened.Includes("new"), Is.True);
            Assert.That(partial.Selected, Is.EquivalentTo(new[] { "open" }));
        });
    }

    /// <summary>
    /// ColumnsFn gives the dialog columns of its own, a label for each value here, sorted by label; the selection holds the
    /// values whatever the columns show, and a clone and a restored definition the column's own completes keep the columns.
    /// </summary>
    [Test]
    public void ColumnsFnShowsLabelsAndTheSelectionKeepsTheValuesTest()
    {
        Dictionary<int, string> labels = new() { [2] = "low", [10] = "high" };
        EFDistinctDef<RowsContext, Row, int?> def = new(true)
        {
            DbContextFactoryFn = () => _factory,
            QueryFn = db => db.Rows.Where(r => r.Owner == 1),
            ColumnExpr = r => r.Rank,
            ColumnsFn = (model, texts) =>
            {
                model.AddColumn("label", new Column<DistinctRowValue<int?>, string>("Rank"), r => labels[r.Value!.Value]);
                model.SetSortedColumn("label", true);
            },
        };
        using BunitContext ctx = NewContext();

        var cut = ctx.Render<DistinctDefRenderer<EFDistinctDef<RowsContext, Row, int?>, int?>>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 4);

        List<string> cells = cut.FindAll("tr.current td, tr.even td, tr.odd td").Select(td => td.TextContent.Trim()).Where(t => t.Length > 0).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(cells, Is.EqualTo(new[] { "high", "low" }), "the labels, sorted by label: 10 before 2");
            Assert.That(def.Selected, Is.EquivalentTo(new int?[] { 2, 10 }), "the values, not the labels");
            Assert.That(def.Clone().ColumnsFn, Is.SameAs(def.ColumnsFn));
            Assert.That(def.InjectParamsInto(new(true)).ColumnsFn, Is.SameAs(def.ColumnsFn));
        });
    }

    /// <summary>
    /// A definition read back from a memento has the selection and none of what the page supplied: the column's own definition
    /// gives that back, and the restored one opens again. Its type, a closed generic, is found again by name.
    /// </summary>
    [Test]
    public async Task ARestoredDefinitionTakesWhatThePageSuppliedFromTheColumnsTest()
    {
        var ofTheColumn = OwnerOne<int?>(r => r.Rank);
        var edited = ofTheColumn.Clone();
        await edited.PopulateAsync();
        edited.Selected.Remove(2);
        edited.IncludeNull = false;

        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(JsonSerializer.Serialize<AFilterDef>(edited)).RequireNotNull();
        var injected = ofTheColumn.InjectParamsInto((EFDistinctDef<RowsContext, Row, int?>)restored);

        Assert.Multiple(async () =>
        {
            Assert.That(restored, Is.EqualTo(edited), "the same definition after the round trip");
            Assert.That((injected.Selected, injected.IncludeNull), Is.EqualTo((edited.Selected, false)), "the user's choices");
            Assert.That((await injected.PopulateAsync()).Select(v => v.Value), Is.EquivalentTo(new int?[] { 2, 10 }), "and it reads its values again");
            Assert.That(injected.Selected, Is.EquivalentTo(new int?[] { 10 }), "keeping the selection");
        });
    }

    /// <summary>
    /// The dialog lists the values one per row, in their own order: ranks sort as numbers, 2 before 10, which a list of texts
    /// would turn around.
    /// </summary>
    [Test]
    public void DialogListsTheValuesInTheirOwnOrderTest()
    {
        using BunitContext ctx = NewContext();
        var def = OwnerOne<int?>(r => r.Rank);

        var cut = ctx.Render<DistinctDefRenderer<EFDistinctDef<RowsContext, Row, int?>, int?>>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 4);   // include-null, select-all, one per value

        // The grid's data rows: the dialog lays itself out with a table too
        List<string> cells = cut.FindAll("tr.current td, tr.even td, tr.odd td").Select(td => td.TextContent.Trim()).Where(t => t.Length > 0).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(cells, Is.EqualTo(new[] { "2", "10" }));
            Assert.That(cut.FindAll("input[type=checkbox]").Select(i => i.HasAttribute("checked")), Is.All.True, "started full");
        });
    }

    /// <summary>
    /// A byte column, a tinyint, has no filter of its own, and the dialog's grid does not ask for one: its dialog opens.
    /// </summary>
    [Test]
    public void DialogOpensOnAColumnWithNoFilterOfItsOwnTest()
    {
        using BunitContext ctx = NewContext();
        var def = OwnerOne<byte?>(r => (byte?)r.Rank);

        var cut = ctx.Render<DistinctDefRenderer<EFDistinctDef<RowsContext, Row, byte?>, byte?>>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 4);

        Assert.That(def.Selected, Is.EquivalentTo(new byte?[] { 2, 10 }));
    }
}
