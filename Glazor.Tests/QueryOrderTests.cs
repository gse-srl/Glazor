using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Glazor.Tests;

/// <summary>
/// The EF model compares a reloaded row with the order its base query pages by, on the row's own members. An order it cannot
/// read on the row makes every reload re-query instead.
/// </summary>
public class QueryOrderTests
{
    public class Animal
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class Dog : Animal
    {
        public string? Breed { get; set; }
    }

    // One table for the whole hierarchy
    public class ZooContext(DbContextOptions<ZooContext> options) : DbContext(options)
    {
        public DbSet<Animal> Animals => Set<Animal>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>();
            modelBuilder.Entity<Dog>();
        }
    }

    private sealed class ZooContextFactory(DbContextOptions<ZooContext> options) : IDbContextFactory<ZooContext>
    {
        public ZooContext CreateDbContext() => new(options);
    }

    private sealed class CountingModel : EditableEFDataGridModel<ZooContext, Animal>
    {
        public int Queries;
        protected override IQueryable<Animal> CompleteQueryable(ZooContext dbContext, IQueryable<Animal> query, ReadRequest request)
        {
            Queries++;
            return base.CompleteQueryable(dbContext, query, request);
        }
    }

    private SqliteConnection _connection = null!;
    private ZooContextFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new ZooContextFactory(new DbContextOptionsBuilder<ZooContext>().UseSqlite(_connection).Options);
        using ZooContext dbContext = _factory.CreateDbContext();
        dbContext.Database.EnsureCreated();
        for (int i = 0; i < 30; i++)
            dbContext.Add(i % 2 == 0 ? new Animal { Name = $"Animal{i:D2}" } : new Dog { Name = $"Dog{i:D2}", Breed = $"Breed{i:D2}" });
        dbContext.SaveChanges();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private static System.Linq.Expressions.Expression Ordered<T>(Func<IQueryable<T>, IQueryable<T>> order) =>
        order(Array.Empty<T>().AsQueryable()).Expression;

    /// <summary>
    /// A member reached through a cast to a derived type is not one of the row's own, while a member the row inherits is.
    /// </summary>
    [Test]
    public void MemberOfADerivedTypeIsNotTheRowsOwnTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QueryOrder.RowMembers(Ordered<Animal>(q => q.OrderBy(a => ((Dog)a).Breed)), typeof(Animal)), Is.Null, "cast");
            Assert.That(QueryOrder.RowMembers(Ordered<Animal>(q => q.OrderBy(a => (a as Dog)!.Breed)), typeof(Animal)), Is.Null, "as");
            Assert.That(QueryOrder.RowMembers(Ordered<Dog>(q => q.OrderBy(d => d.Name)), typeof(Dog))!.Select(m => m.Name),
                Is.EqualTo(new[] { "Name" }), "inherited");
        });
    }

    /// <summary>
    /// A projection after the order may drop or remap the members it reads, even to the row's own type; an order after it is
    /// read as usual.
    /// </summary>
    [Test]
    public void OrderBeforeAProjectionIsNotTheRowsOwnTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QueryOrder.RowMembers(Ordered<Animal>(q => q.OrderBy(a => a.Name).Select(a => new Animal { Id = a.Id })), typeof(Animal)),
                Is.Null, "before");
            Assert.That(QueryOrder.RowMembers(Ordered<Animal>(q => q.Select(a => new Animal { Id = a.Id, Name = a.Name }).OrderBy(a => a.Name)), typeof(Animal))!
                .Select(m => m.Name), Is.EqualTo(new[] { "Name" }), "after");
        });
    }

    /// <summary>
    /// EF drops an order placed before a set operation, and the model then pages by the key alone: that order is not one the
    /// row can move in. An order after the set operation is read as usual.
    /// </summary>
    [Test]
    public void OrderBeforeASetOperationIsNotReadTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QueryOrder.RowMembers(Ordered<Animal>(q => q.OrderBy(a => a.Name).Distinct()), typeof(Animal)), Is.Empty, "before");
            Assert.That(QueryOrder.RowMembers(Ordered<Animal>(q => q.Distinct().OrderBy(a => a.Name)), typeof(Animal))!.Select(m => m.Name),
                Is.EqualTo(new[] { "Name" }), "after");
        });
    }

    private sealed class NamelessModel : EFDataGridModel<ZooContext, Animal>
    {
        protected override IQueryable<Animal> RefineQueryBeforeExec(ZooContext dbContext, bool singleItemReload, IQueryable<Animal> query)
            => query.Select(a => new Animal { Id = a.Id });   // the name the base query orders by is not read
    }

    /// <summary>
    /// A row that moves in an order RefineQueryBeforeExec projects out is not seen to move on the row, so its reload re-queries,
    /// and paging on reads every row once.
    /// </summary>
    [Test]
    public async Task OrderProjectedOutByTheRefinedQueryRequeriesTest()
    {
        var model = new NamelessModel
        {
            DbContextFactory = _factory,
            BaseQueryFn = (dbContext, _) => dbContext.Animals.OrderBy(a => a.Name),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Id", new Column<Animal, int>("Id"), a => a.Id);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one window loaded");
        using (ZooContext dbContext = _factory.CreateDbContext())
        {
            dbContext.Animals.Single(a => a.Id == model.GetItem(1).Id).Name = "Zebra";   // to the end of the order
            dbContext.SaveChanges();
        }
        await model.ReloadItemAsync(1);
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.Unique.And.Count.EqualTo(30));
    }

    /// <summary>
    /// A base query ordered by a member of a derived type: an edit starts, is cancelled, is posted, and a reload goes through,
    /// each posted edit re-querying since the order cannot be compared on the row.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task EditUnderAnOrderByAMemberOfADerivedTypeTest(bool typeAs)
    {
        CountingModel model = new()
        {
            DbContextFactory = _factory,
            BaseQueryFn = typeAs ? (dbContext, _) => dbContext.Animals.OrderBy(a => (a as Dog)!.Breed)
                : (dbContext, _) => dbContext.Animals.OrderBy(a => ((Dog)a).Breed),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Name", new Column<Animal, string>("Name"), a => a.Name);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one window loaded");
        model.SetCurrentRow(3);
        await model.EditAsync();
        await model.CancelAsync();
        Assert.That(model.IsEditPending(), Is.False);
        Animal edited = await model.EditAsync();
        edited.Name = "Renamed";
        using (ZooContext dbContext = _factory.CreateDbContext())
        {
            dbContext.Animals.Single(a => a.Id == edited.Id).Name = edited.Name;   // the host saves the form
            dbContext.SaveChanges();
        }
        int queries = model.Queries;
        await model.PostAsync();
        Assert.That(model.GetCurrentItem()!.Name, Is.EqualTo("Renamed"));
        Assert.That(model.Queries, Is.GreaterThan(queries), "a new query");
        Assert.That(await model.ReloadItemAsync(model.GetCurrentRow()), Is.True);
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.Unique.And.Count.EqualTo(30));
    }

    /// <summary>
    /// A grid over the base type may page a query over a derived one. When calls over the derived type follow its order, the
    /// key's ThenBy over the base type cannot go in before them, and the query pages as the host wrote it.
    /// </summary>
    [TestCase("Where")]
    [TestCase("AsNoTracking")]
    public async Task QueryOverADerivedTypeWithCallsAfterItsOrderPagesTest(string after)
    {
        var model = new EFDataGridModel<ZooContext, Animal>
        {
            DbContextFactory = _factory,
            BaseQueryFn = (dbContext, _) => after == "Where"
                ? dbContext.Animals.OfType<Dog>().OrderBy(d => d.Breed).Where(d => d.Name != "")
                : dbContext.Animals.OfType<Dog>().OrderBy(d => d.Breed).AsNoTracking(),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Name", new Column<Animal, string>("Name"), a => a.Name);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one window loaded");
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.Unique.And.Count.EqualTo(15));
    }
}
