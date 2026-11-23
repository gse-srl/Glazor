using Bunit;
using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// EFValuesInSetDef, the multi-select over a collection column whose dialog rows come from a query: a film's cast, the ids of
/// its actors, filtered by actors listed with their name.
/// <para>
/// Three actors, Ann (1), Bob (2) and Cy (3); three films, Alpha cast by Ann and Bob, Beta by Bob, Gamma by nobody.
/// </para>
/// </summary>
public class EFValuesInSetDefTests
{
    public class Film
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public List<Role> Cast { get; } = [];
    }

    public class Role
    {
        public int Id { get; set; }
        public int FilmId { get; set; }
        public int ActorId { get; set; }
    }

    public class Actor
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed record ActorRow(int Id, string Name);

    public sealed record NamedRow(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    public class FilmsContext(DbContextOptions<FilmsContext> options) : DbContext(options)
    {
        public DbSet<Film> Films => Set<Film>();
        public DbSet<Actor> Actors => Set<Actor>();
    }

    private sealed class FilmsContextFactory(DbContextOptions<FilmsContext> options) : IDbContextFactory<FilmsContext>
    {
        public FilmsContext CreateDbContext() => new(options);
    }

    private SqliteConnection _connection = null!;
    private FilmsContextFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new FilmsContextFactory(new DbContextOptionsBuilder<FilmsContext>().UseSqlite(_connection).Options);
        using FilmsContext ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.Actors.AddRange(new Actor { Id = 1, Name = "Ann" }, new Actor { Id = 2, Name = "Bob" }, new Actor { Id = 3, Name = "Cy" });
        Film alpha = new() { Id = 1, Title = "Alpha" }, beta = new() { Id = 2, Title = "Beta" };
        alpha.Cast.AddRange([new Role { ActorId = 1 }, new Role { ActorId = 2 }]);
        beta.Cast.Add(new Role { ActorId = 2 });
        ctx.Films.AddRange(alpha, beta, new Film { Id = 3, Title = "Gamma" });
        ctx.SaveChanges();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private EFValuesInSetDef<FilmsContext, int, ActorRow, int> CastDef(bool startFull = false) => new(id => id, startFull)
    {
        DbContextFactoryFn = () => _factory,
        QueryFn = db => db.Actors.Select(a => new ActorRow(a.Id, a.Name)),
        RowKeyFn = a => a.Id,
        ColumnsFn = (model, texts) =>
        {
            model.AddColumn("name", new Column<ActorRow, string>("Actor"), a => a.Name);
            model.SetSortedColumn("name", false);
        },
    };

    private List<string> Titles(AFilterDef def)
    {
        EFDataGridModel<FilmsContext, Film> model = new()
        {
            DbContextFactory = _factory,
            BaseQueryFn = (db, _) => db.Films.OrderBy(f => f.Id),
            ItemKeyExpr = f => f.Id,
        };
        model.AddEFColumn("Cast", new Column<Film, IQueryable<int>>("Cast") { Sortable = false }, f => f.Cast.Select(r => r.ActorId).AsQueryable());
        model.ApplyFilters([new("Cast", def)]);
        return Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Title).ToList();
    }

    /// <summary>The rows are the query's, keyed by RowKeyFn; the films kept are those cast by an actor selected, in the database.</summary>
    [Test]
    public async Task FiltersTheCollectionByTheRowsSelectedTest()
    {
        var def = CastDef();
        IList<ActorRow> rows = await def.PopulateAsync();
        def.Selected.Add(1);
        var noEmpty = def.Clone();
        noEmpty.IncludeNullOrEmpty = false;

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.Name), Is.EquivalentTo(new[] { "Ann", "Bob", "Cy" }));
            Assert.That(def.CanCreatePredicate(), Is.True, "pushed down");
            Assert.That(Titles(def), Is.EqualTo(new[] { "Alpha", "Gamma" }), "Ann's film, and the one with no cast");
            Assert.That(Titles(noEmpty), Is.EqualTo(new[] { "Alpha" }), "without the empty cast");
        });
    }

    /// <summary>
    /// A definition read back from a memento has the selection and none of what the page supplied, the key expression
    /// included: the column's own gives that back, and the restored one reads and filters again.
    /// </summary>
    [Test]
    public async Task ARestoredDefinitionTakesWhatThePageSuppliedFromTheColumnsTest()
    {
        var ofTheColumn = CastDef(startFull: true);
        var edited = ofTheColumn.Clone();
        await edited.PopulateAsync();
        edited.Selected.Remove(1);

        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(JsonSerializer.Serialize<AFilterDef>(edited)).RequireNotNull();
        var injected = ofTheColumn.InjectParamsInto((EFValuesInSetDef<FilmsContext, int, ActorRow, int>)restored);

        Assert.Multiple(async () =>
        {
            Assert.That(restored, Is.EqualTo(edited), "the same definition after the round trip");
            Assert.That(injected.Selected, Is.EquivalentTo(new[] { 2, 3 }), "the user's choices");
            Assert.That((await injected.PopulateAsync()).Select(r => r.Id), Is.EquivalentTo(new[] { 1, 2, 3 }), "it reads its rows again");
            Assert.That(Titles(injected), Is.EqualTo(new[] { "Alpha", "Beta", "Gamma" }), "and filters on the items' keys");
        });
    }

    /// <summary>
    /// Every row selected without the null or empty collections is any collection that has an item, in memory and pushed down:
    /// an actor added after the dialog read the rows passes too. With one row left out, the selection is the others only.
    /// </summary>
    [Test]
    public async Task EveryRowSelectedWithoutEmptyPassesAnItemAddedLaterTest()
    {
        var def = CastDef(startFull: true);
        await def.PopulateAsync();
        def.IncludeNullOrEmpty = false;
        using (FilmsContext ctx = _factory.CreateDbContext())
        {
            ctx.Actors.Add(new Actor { Id = 4, Name = "Dee" });
            Film delta = new() { Id = 4, Title = "Delta" };
            delta.Cast.Add(new Role { ActorId = 4 });
            ctx.Films.Add(delta);
            ctx.SaveChanges();
        }
        Assert.Multiple(() =>
        {
            Assert.That(def.IsVoid(), Is.False, "the empty casts are left out");
            Assert.That(def.Includes(new[] { 4 }.AsQueryable()), Is.True, "in memory");
            Assert.That(Titles(def), Is.EqualTo(new[] { "Alpha", "Beta", "Delta" }), "pushed down");
        });

        def.Selected.Remove(3);   // Cy, in no film
        Assert.Multiple(() =>
        {
            Assert.That(def.Includes(new[] { 4 }.AsQueryable()), Is.False, "one row left out: the others only");
            Assert.That(Titles(def), Is.EqualTo(new[] { "Alpha", "Beta" }));
        });
    }

    /// <summary>Every row selected, opened again once a row was added, shows that row selected too.</summary>
    [Test]
    public async Task ReopenedWithEveryRowSelectedSelectsARowAddedSinceTest()
    {
        var def = CastDef(startFull: true);
        await def.PopulateAsync();
        using (FilmsContext ctx = _factory.CreateDbContext())
        {
            ctx.Actors.Add(new Actor { Id = 4, Name = "Dee" });
            ctx.SaveChanges();
        }
        var reopened = def.Clone();   // as the dialog does
        await reopened.PopulateAsync();

        Assert.That(reopened.Selected, Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
    }

    /// <summary>
    /// A restored definition that the column's own has not completed has no key expression: it fails when it filters, in the
    /// database or in memory, rather than filtering on a key it does not have.
    /// </summary>
    [Test]
    public async Task ARestoredDefinitionNotCompletedFailsWhenItFiltersTest()
    {
        var edited = CastDef();
        await edited.PopulateAsync();
        edited.Selected.Add(1);
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(JsonSerializer.Serialize<AFilterDef>(edited)).RequireNotNull();

        Assert.Multiple(() =>
        {
            Assert.That(() => Titles(restored), Throws.InstanceOf<InvalidOperationException>(), "pushed down");
            Assert.That(() => restored.Includes(new[] { 1 }.AsQueryable()),
                Throws.InvalidOperationException.With.Message.Contains("RestoreMemento"), "in memory");
        });
    }

    /// <summary>Without ColumnsFn the dialog lists each row's text in one column, sorted.</summary>
    [Test]
    public void DialogWithoutColumnsFnListsTheRowsTextTest()
    {
        using BunitContext ctx = NewContext();
        var def = new EFValuesInSetDef<FilmsContext, int, NamedRow, int>(id => id, startFull: true)
        {
            DbContextFactoryFn = () => _factory,
            QueryFn = db => db.Actors.OrderByDescending(a => a.Name).Select(a => new NamedRow(a.Id, a.Name)),
            RowKeyFn = a => a.Id,
        };
        var cut = ctx.Render<EFValuesInSetDefRenderer<FilmsContext, int, NamedRow, int>>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 5);

        List<string> cells = cut.FindAll("tr.current td, tr.even td, tr.odd td").Select(td => td.TextContent.Trim()).Where(t => t.Length > 0).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(cells, Is.EqualTo(new[] { "Ann", "Bob", "Cy" }), "read in reverse, listed sorted");
            Assert.That(cut.Markup, Does.Contain("Value"), "the column's label");
        });
    }

    /// <summary>The dialog lists the rows in the columns ColumnsFn adds, in the order it sets.</summary>
    [Test]
    public void DialogShowsTheColumnsGivenTest()
    {
        using BunitContext ctx = NewContext();
        var def = CastDef(startFull: true);
        var cut = ctx.Render<EFValuesInSetDefRenderer<FilmsContext, int, ActorRow, int>>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 5);   // include-null, select-all, one per row

        List<string> cells = cut.FindAll("tr.current td, tr.even td, tr.odd td").Select(td => td.TextContent.Trim()).Where(t => t.Length > 0).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(cells, Is.EqualTo(new[] { "Cy", "Bob", "Ann" }), "sorted by name, descending");
            Assert.That(cut.Markup, Does.Contain("Actor"), "the column's label");
        });
    }
}
