using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.EntityFrameworkCore;
using Glazor.Grds.Props;
using Glazor.Misc;
using Glazor.Tests.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using System.Linq.Expressions;
using static Glazor.Tests.Models.MGAAdesione;

namespace Glazor.Tests;

public class EFCoreTests : IDisposable
{
    // https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database#sqlite-in-memory
    private SqliteConnection? _connection;
    private DbContextOptions<MGADbContext>? _contextOptions;

    private IDbContextFactory<MGADbContext> _dbCtxFactory; 

    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<EFCoreTests> logger;

    public EFCoreTests()
    {
        Logger seriLogger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", new LoggingLevelSwitch(LogEventLevel.Information))
            .MinimumLevel.Override("Glazor", new LoggingLevelSwitch(LogEventLevel.Debug))
            .WriteTo.Console()
            .CreateLogger();
        loggerFactory = new SerilogLoggerFactory(seriLogger);
        LoggerSupport.SetLoggerFactory(loggerFactory);
        logger = loggerFactory.CreateLogger<EFCoreTests>();
    }

    void IDisposable.Dispose()
    {
        GC.SuppressFinalize(this);
        loggerFactory.Dispose();
    }

    [SetUp]
    public void Setup()
    {
        logger.LogInformation("Setting up in memory DB");
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _contextOptions = new DbContextOptionsBuilder<MGADbContext>()
            .UseLoggerFactory(loggerFactory)
            .UseSqlite(_connection)
            .Options;
        _dbCtxFactory = new MockMGADbContextFactory(_contextOptions);
        //
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        dbCtx.Database.EnsureCreated();
        MGAAdesione titolare = new()
        {
            NumeroPolizza = "0001",
            NumeroAdesione = 1,
            Cognome = "Pro",
            Nome = "Vallers",
            DataNascita = new DateOnly(1948, 6, 16),
            DataInizio = new DateOnly(1996, 1, 31),
            Legame = TipoLegame.Titolare,
            Carenze = true,
            CodiceFiscaleSocio = "PRTVNT",
            Tariffa = TipoTariffa.Azienda,
        };
        dbCtx.Add(titolare);
        dbCtx.Add(new MGAAdesione()
        {
            NumeroPolizza = "0001",
            NumeroAdesione = 2,
            Cognome = "Pro",
            Nome = "Erlumo",
            DataNascita = new DateOnly(1996, 6, 24),
            Titolare = titolare,
            Legame = TipoLegame.Figlio,
            Carenze = false,
        });
        dbCtx.Add(new MGAAdesione()
        {
            NumeroPolizza = "0001",
            NumeroAdesione = 3,
            Cognome = "Pro",
            Nome = "Erlumoa",
            DataNascita = new DateOnly(1998, 6, 24),
            Titolare = titolare,
            Legame = TipoLegame.Figlia,
            Carenze = false,
            CodiceFiscaleSocio = "PRTELM",
        });
        dbCtx.SaveChanges();
        //
        MGASinistro sinistro = new()
        {
            NumeroSinistro = "S0001",
            LastProgressivoOperazione = 1,
            DataContabile = new DateOnly(2025, 10, 30),
            OraAccadimento = new TimeOnly(10, 13),
            NumeroDenuncia = "D0001",
            DataDenuncia = new DateOnly(2025, 10, 29),
            Adesione = titolare,
        };
        dbCtx.Add(sinistro);
        dbCtx.Add(new MGAAttore()
        {
            Sinistro = sinistro,
            ChiaveEsterna = "ATT0001",
            Cognome = "Oropallo",
            Nome = "Pinco",
        });
        dbCtx.Add(new MGAAttore()
        {
            Sinistro = sinistro,
            ChiaveEsterna = "ATT0002",
            Cognome = "Pig",
            Nome = "Peppa",
        });
        dbCtx.SaveChanges();
    }

    [TearDown]
    public void TearDown()
    {
        _connection?.Dispose();
    }

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
#pragma warning disable EF1001 // Internal EF Core API usage.
    // https://stackoverflow.com/questions/77623562/how-can-i-get-an-iupdateentry-from-a-dbcontext-for-a-object-that-is-read
    private static IUpdateEntry GetUpdateEntry<TEntity>(DbSet<TEntity> dbSet, TEntity ent) where TEntity : class
        // => dbSet.GetInfrastructure<InternalEntityEntry>();
        => dbSet.Entry(ent!).GetInfrastructure<InternalEntityEntry>();
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
#pragma warning restore EF1001 // Internal EF Core API usage.

    [Test]
    public void CheckDataTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        Assert.That(dbCtx.MGAAdesione.Count, Is.EqualTo(3));
        var sinistri = dbCtx.MGASinistro.AsNoTracking()
            .Include(x => x.Adesione)
            .Include(x => x.Attori)
            .OrderBy(x => x.NumeroDenuncia)
            .ToList();
        Assert.That(sinistri.Count, Is.EqualTo(1));
        Assert.That(sinistri[0].Attori.Count, Is.EqualTo(2));
        Assert.That(sinistri[0].Attori.First().Sinistro, Is.EqualTo(sinistri[0]));
    }

    [Test]
    public void GetPKTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGAAdesione> dbAdesioni = dbCtx.MGAAdesione;
        IKey pk = dbAdesioni.EntityType.FindPrimaryKey()!;
        IReadOnlyList<IClrPropertyGetter> getters = pk.Properties.Select(p => p.GetGetter()).ToList().AsReadOnly();
        //
        {
            Assert.That(getters.Count, Is.EqualTo(1));
            IQueryable<MGAAdesione> qadesioni = dbAdesioni.AsNoTracking()
                .OrderBy(x => x.Id);
            var adesioni = qadesioni.ToList();
            Assert.That(adesioni.Count, Is.EqualTo(3));
            //{   // Abandoned
            //    IPrincipalKeyValueFactory pkvf = pk.GetPrincipalKeyValueFactory();
            //    EquatableKeyValue<int> pkv = (EquatableKeyValue<int>)pkvf.CreateEquatableKey(GetUpdateEntry(dbAdesioni, adesioni[0]));
            //    Console.WriteLine($"pkv: {pkv}\n\ttype: {pkv.GetType()}");
            //}
            {
                IList<object?> pkvs = getters.Select(g => g.GetClrValue(adesioni[0])).ToList();
                Assert.That(pkvs.Count, Is.EqualTo(1));
                object? pkv = pkvs[0];
                Assert.That(pkv, Is.EqualTo(1));
                Assert.That(pkv.GetType(), Is.EqualTo(typeof(int)));
            }
            {
                IList<object?> pkvs = getters.Select(g => g.GetClrValue(adesioni[1])).ToList();
                Assert.That(pkvs.Count, Is.EqualTo(1));
                object? pkv = pkvs[0];
                Assert.That(pkv, Is.EqualTo(2));
                Assert.That(pkv.GetType(), Is.EqualTo(typeof(int)));
            }
        }
        //
        {
            logger.LogInformation("Visually check the executed sql query order by condition");
            IProperty pkp = pk.Properties[0];
            var parameter = Expression.Parameter(typeof(MGAAdesione), "e");
            var exp = Expression.Property(parameter, pkp.Name);
            Expression<Func<MGAAdesione, int>> expo = Expression.Lambda<Func<MGAAdesione, int>>(exp, parameter);
            IQueryable<MGAAdesione> qadesioni = dbAdesioni.AsNoTracking()
                .OrderByDescending(expo);
            var adesioni = qadesioni.ToList();
            Assert.That(adesioni.Count, Is.EqualTo(3));
            //
            IList<object?> pkvs = getters.Select(g => g.GetClrValue(adesioni[0])).ToList();
            Assert.That(pkvs.Count, Is.EqualTo(1));
            object? pkv = pkvs[0];
            Assert.That(pkv, Is.EqualTo(3));
        }
    }

    /// <summary>
    /// NumberDef filter test usage with EF core
    /// </summary>
    [Test]
    public void DynamicWhereNumDefTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGAAdesione> dbAdesioni = dbCtx.MGAAdesione;
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = true };
            NumberDef<int> ndef = new() { From = 1, IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ndef.CreatePredicate<MGAAdesione>(d => d.TitolareId),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = false };
            NumberDef<int> ndef = new() { From = 1, IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ndef.CreatePredicate<MGAAdesione>(d => d.TitolareId),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = true, Contains = false };
            NumberDef<int> ndef = new() { From = 1, IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ndef.CreatePredicate<MGAAdesione>(d => d.TitolareId),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = false, Contains = false };
            NumberDef<int> ndef = new() { From = 1, IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ndef.CreatePredicate<MGAAdesione>(d => d.TitolareId),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// ReadAllDataAsync as the very first call on a ReadAll model: the requested count is int.MaxValue and must not
    /// become the backing list's capacity.
    /// </summary>
    [Test]
    public async Task ReadAllDataAsyncOnColdReadAllModelTest()
    {
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
            ReadAll = true,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(model.AllDataRead(), Is.True);
        Assert.That(model.GetRowCount(), Is.EqualTo(3));
    }

    /// <summary>
    /// ReadAllDataAsync right after ForceReload(true) or a filter change reads the whole table and leaves the memento for the
    /// next sync, which finds fewer rows than it recorded: that sync must not read the table again on top of them.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadAllAfterAReloadReadsEachRowOnceTest(bool filterChange)
    {
        SeedRows(50);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            ReadAll = true,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Assert.That(model.GetRowCount(), Is.EqualTo(53));
        if (filterChange)
            model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Id", new NumberDef<int>() { From = 29 })]);
        else
        {
            using (var dbCtx = _dbCtxFactory.CreateDbContext())
            {
                dbCtx.RemoveRange(dbCtx.MGAAdesione.Where(a => a.Id >= 10 && a.Id < 15));
                dbCtx.SaveChanges();
            }
            model.ForceReload(true);
        }
        await model.ReadAllDataAsync(CancellationToken.None);
        List<int> inDb;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            inDb = dbCtx.MGAAdesione.Where(a => !filterChange || a.Id >= 29).OrderBy(a => a.Id).Select(a => a.Id).ToList();
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.EqualTo(inDb), "each row once");
    }

    private sealed class PausingReadModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        public int PauseAt { get; set; } = -1;
        public readonly TaskCompletionSource Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _read;

        protected override async ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            if (phase == RefineItemPhase.BeforeFiltering && ++_read == PauseAt)
            {
                Paused.SetResult();
                await Resume.Task;
            }
            return item;
        }
    }

    /// <summary>
    /// A model disposed while a fetch is under way, as when a circuit ends after a fetch outlived its timeout, deletes the chunks
    /// it spilled, and the fetch ends with an exception instead of filling a list nobody disposes.
    /// </summary>
    [Test]
    public async Task DisposeDuringAFetchEndsTheFetchAndLeavesNoSpillFileTest()
    {
        SeedRows(300);
        string spill = Path.Combine(Path.GetTempPath(), "glazor-dispose-" + Path.GetRandomFileName());
        Directory.CreateDirectory(spill);
        try
        {
            var model = new PausingReadModel()
            {
                DbContextFactory = _dbCtxFactory,
                BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
                ItemKeyExpr = a => a.Id,
                CreateItemsListFn = size => ValueTask.FromResult<ISortableList<MGAAdesione>>(new OnDiskChunkedList<MGAAdesione>(3, 32, spill)),
                PauseAt = 250,
            };
            model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
            Task<int> fetch = Task.Run(async () => await ((IGridModel<MGAAdesione>)model).FetchRowsAsync(0, 300));
            await model.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(Directory.GetFiles(spill), Is.Not.Empty, "chunks spilled before the pause");
            model.Dispose();
            model.Resume.SetResult();
            Assert.CatchAsync(async () => await fetch.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(Directory.GetFiles(spill), Is.Empty);
        }
        finally
        {
            Directory.Delete(spill, true);
        }
    }

    /// <summary>
    /// A host's read still under way when a sort click reloads the model, as an export awaiting ReadAllDataAsync outside the
    /// spinner: it ends with OperationCanceledException instead of appending its rows, in the old order, to the new read's.
    /// </summary>
    [Test]
    public async Task AReadOvertakenByAReloadIsCancelledTest()
    {
        SeedRows(50);
        var model = new PausingReadModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
            PauseAt = 15,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Task read = Task.Run(async () => await model.ReadAllDataAsync(CancellationToken.None));
        await model.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.SetSortedColumn("Id", false);
        Assert.That(model.GetCurrentRowCount(), Is.GreaterThan(0), "the render after the click reads the first window");
        model.Resume.SetResult();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(10)));

        await model.ReadAllDataAsync(CancellationToken.None);
        List<int> ids = Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
        Assert.That(ids, Is.Unique.And.Count.EqualTo(53).And.Ordered.Descending);
    }

    /// <summary>
    /// A host's read awaiting the database while the grid's own fetch reads on, as an export outside the spinner when the user
    /// presses End, ends cancelled: both would append from the same offset, and the rows read must each come once.
    /// </summary>
    [Test]
    public async Task AReadOvertakenByAnotherReadIsCancelledTest()
    {
        SeedRows(50);
        var model = new PausingReadModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
            PauseAt = 15,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Task read = Task.Run(async () => await model.ReadAllDataAsync(CancellationToken.None));
        await model.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.FetchRowsAsync(0, 30);
        model.Resume.SetResult();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(10)));

        await model.ReadAllDataAsync(CancellationToken.None);
        List<int> ids = Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
        Assert.That(ids, Is.Unique.And.Count.EqualTo(53).And.Ordered.Ascending);
    }

    private sealed class FailingReadModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        public int FailAt { get; set; } = -1;
        public CancellationTokenSource? CancelInstead { get; set; }
        private int _read;

        protected override ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            if (phase == RefineItemPhase.BeforeFiltering && FailAt >= 0 && ++_read == FailAt)
            {
                if (CancelInstead == null)
                    throw new InvalidOperationException("the host's refine failed");
                CancelInstead.Cancel();
            }
            return ValueTask.FromResult(item);
        }
    }

    /// <summary>
    /// A ReadAll read that throws or is cancelled part way keeps the rows it added, and a ReadAll query starts again at row 0:
    /// the next read must not append the whole table on top of them.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadAllAfterAFailedReadReadsEachRowOnceTest(bool cancel)
    {
        var model = new FailingReadModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            ReadAll = true,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        SeedRows(50);
        using CancellationTokenSource cts = new();
        model.FailAt = 20;
        model.CancelInstead = cancel ? cts : null;
        Assert.CatchAsync(async () => await model.ReadAllDataAsync(cts.Token));
        model.FailAt = -1;
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.Unique.And.Count.EqualTo(53));
    }

    /// <summary>
    /// A paged reload that throws or is cancelled part way comes back without its current row, keeps its filter, and keeps
    /// the rows it read: the next read goes on from them, each row once.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedPagedReloadComesBackColdAndKeepsItsFilterTest(bool cancel)
    {
        SeedRows(300);
        var model = new FailingReadModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 20,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Id", new NumberDef<int>() { From = 29 })]);
        IGridModel<MGAAdesione> grid = model;
        await grid.FetchRowsAsync(0, 40);
        model.SetCurrentRow(5);

        using CancellationTokenSource cts = new();
        model.FailAt = 25;   // in the second pass of the reload's read, after the first has added its rows
        model.CancelInstead = cancel ? cts : null;
        model.ForceReload(true);
        Assert.CatchAsync(async () => await grid.FetchRowsAsync(0, 40, cts.Token));
        model.FailAt = -1;
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(-1), "no current row");
            Assert.That(model.GetFilters().Keys, Is.EqualTo(new[] { "Id" }), "the filter stays");
        });

        await model.ReadAllDataAsync(CancellationToken.None);
        List<int> inDb;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            inDb = dbCtx.MGAAdesione.Where(a => a.Id >= 29).OrderBy(a => a.Id).Select(a => a.Id).ToList();
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.EqualTo(inDb), "each row once");
    }

    /// <summary>
    /// An edited row whose chunk is written to disk and read back while the edit is open is a copy of the item EditAsync handed
    /// out: the post must still compare the values the edit started from, or it misses the row's move in the paging order.
    /// </summary>
    [Test]
    public async Task EditedRowReadBackFromDiskStillMovesTest()
    {
        SeedRows(300);
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
            CreateItemsListFn = size => ValueTask.FromResult<ISortableList<MGAAdesione>>(new OnDiskChunkedList<MGAAdesione>(3, 32)),
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetSortedColumn("Cognome", true);
        await ((Glazor.Grds.Mdls.IGridModel<MGAAdesione>)model).FetchRowsAsync(0, 100);
        model.SetCurrentRow(3);
        MGAAdesione edited = await model.EditAsync();
        edited.Cognome = "zzz";   // last in the order the database pages by
        var view = model.GetDataView();
        for (int i = 0; i < view.Count; i++)
            _ = view[i].Cognome;   // host code reading the rows during the edit: the edited row's chunk goes to disk and back
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.MGAAdesione.Single(a => a.Id == edited.Id).Cognome = "zzz";
            dbCtx.SaveChanges();
        }
        await model.PostAsync();
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.Unique.And.Count.EqualTo(303));
    }

    /// <summary>
    /// An EditAsync that throws, the host's base query failing when the edit reads the order the database pages by, leaves no
    /// edit pending: the row is not frozen, and the next EditAsync starts as usual.
    /// </summary>
    [Test]
    public async Task EditThatThrowsLeavesNoEditPendingTest()
    {
        SeedRows(5);
        bool fail = false;
        using var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => fail ? throw new InvalidOperationException("base query failed") : dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetCurrentRow(1);
        fail = true;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await model.EditAsync());
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False);
            Assert.That(model.IsCurrentRowFrozen(), Is.False);
        });
        fail = false;
        Assert.That(await model.EditAsync(), Is.Not.Null);
        Assert.That(model.IsEditPending(), Is.True);
    }

    /// <summary>
    /// Asking for every row from the top, with some rows already read, reads the whole table and reports its real size.
    /// </summary>
    [Test]
    public async Task FetchOfEveryRowFromTheTopReadsTheWholeTableTest()
    {
        SeedRows(300);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        var gridModel = (Glazor.Grds.Mdls.IGridModel<MGAAdesione>)model;
        await gridModel.FetchRowsAsync(0, 5);
        int fetched = await gridModel.FetchRowsAsync(0, model.GetRowCount());
        Assert.That((fetched, model.AllDataRead()), Is.EqualTo((303, true)));
    }

    /// <summary>
    /// On a paged model sorted in memory, a fetch while an edit is open sorts the rows read again and moves the edited row: the
    /// current row follows it, and the post reloads it, or it misses the row's move in the paging order.
    /// </summary>
    [Test]
    public async Task PostAfterASortInMemoryDuringTheEditReloadsTheEditedRowTest()
    {
        SeedRows(300);
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMem", new Column<MGAAdesione, string>("Nome"), a => a.Nome);   // not pushed down
        model.SetSortedColumn("NomeInMem", false);
        var gridModel = (Glazor.Grds.Mdls.IGridModel<MGAAdesione>)model;
        await gridModel.FetchRowsAsync(0, 7);
        model.SetCurrentRow(5);
        MGAAdesione edited = await model.EditAsync();
        edited.Cognome = "zzz";   // last in the order the database pages by
        await gridModel.FetchRowsAsync(model.GetCurrentRowCount(), 7);   // read on during the edit: the rows read are sorted again
        Assert.That(model.GetItem(model.GetCurrentRow()), Is.SameAs(edited), "the current row follows the edited row");
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.MGAAdesione.Single(a => a.Id == edited.Id).Cognome = "zzz";
            dbCtx.SaveChanges();
        }
        await model.PostAsync();
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id), Is.Unique.And.Count.EqualTo(303));
    }

    /// <summary>
    /// On a paged model sorted in memory, every fetch sorts the rows read again: the current row stays on the record the user
    /// chose, not on the index it had.
    /// </summary>
    [Test]
    public async Task CurrentRowFollowsItsRecordAcrossASortInMemoryTest()
    {
        SeedRows(300);
        using var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMem", new Column<MGAAdesione, string>("Nome"), a => a.Nome);   // not pushed down
        model.SetSortedColumn("NomeInMem", false);
        var gridModel = (Glazor.Grds.Mdls.IGridModel<MGAAdesione>)model;
        await gridModel.FetchRowsAsync(0, 7);
        model.SetCurrentRow(5);
        int chosen = model.GetCurrentItem()!.Id;
        for (int i = 0; i < 5; i++)
            await gridModel.FetchRowsAsync(model.GetCurrentRowCount(), 7);
        Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(chosen));
    }

    /// <summary>
    /// Under a sort in memory a reloaded row keeps its index, out of order: the type-ahead sorts the rows read again before it
    /// searches them, and the current row follows its record.
    /// </summary>
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task TypeAheadSortsAgainAfterAReloadTest(bool editable, bool readAll)
    {
        SeedRows(300);
        using EFDataGridModel<MGADbContext, MGAAdesione> model = editable
            ? new EditableEFDataGridModel<MGADbContext, MGAAdesione>() { DbContextFactory = _dbCtxFactory,
                BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome), ItemKeyExpr = a => a.Id, MinRowsToRead = 10, ReadAll = readAll }
            : new EFDataGridModel<MGADbContext, MGAAdesione>() { DbContextFactory = _dbCtxFactory,
                BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome), ItemKeyExpr = a => a.Id, MinRowsToRead = 10, ReadAll = readAll };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMem", new Column<MGAAdesione, string>("Nome"), a => a.Nome);   // not pushed down
        model.SetSortedColumn("NomeInMem", true);
        await ((Glazor.Grds.Mdls.IGridModel<MGAAdesione>)model).FetchRowsAsync(0, 20);
        int row = Enumerable.Range(0, model.GetCurrentRowCount()).First(i => model.GetItem(i).Nome == "Nome000002");
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.MGAAdesione.Single(a => a.Nome == "Nome000002").Nome = "zzz";   // same place in the order the database pages by
            dbCtx.SaveChanges();
        }
        model.SetCurrentRow(row);
        if (model is EditableEFDataGridModel<MGADbContext, MGAAdesione> editableModel)
        {
            (await editableModel.EditAsync()).Nome = "zzz";
            await editableModel.PostAsync();
        }
        else
            await model.ReloadItemAsync(row);
        int found = await model.SearchOnSortedColumnAsync("Nome000004");
        var view = model.GetDataView();
        Assert.Multiple(() =>
        {
            Assert.That(view.Select(a => a.Nome), Is.Ordered);
            Assert.That(view[found].Nome, Is.EqualTo("Nome000004"));
            Assert.That(model.GetCurrentItem()!.Nome, Is.EqualTo("zzz"), "the current row follows its record");
        });
    }

    /// <summary>
    /// Under a sort in memory a read that throws or is cancelled part way keeps the rows it added, out of order: the type-ahead
    /// sorts the rows read again before it searches them.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TypeAheadSortsAgainAfterAFailedReadTest(bool cancel)
    {
        SeedRows(300);
        var model = new FailingReadModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMem", new Column<MGAAdesione, string>("Nome"), a => a.Nome);   // not pushed down
        model.SetSortedColumn("NomeInMem", false);
        IGridModel<MGAAdesione> grid = model;
        await grid.FetchRowsAsync(0, 20);
        int readBefore = model.GetCurrentRowCount();

        using CancellationTokenSource cts = new();
        model.FailAt = 40;   // in the second read, after it has added rows
        model.CancelInstead = cancel ? cts : null;
        Assert.CatchAsync(async () => await grid.FetchRowsAsync(0, 120, cts.Token));
        model.FailAt = -1;
        Assert.That(model.GetCurrentRowCount(), Is.GreaterThan(readBefore), "the failed read added rows");
        string last = model.GetItem(model.GetCurrentRowCount() - 1).Nome;   // the last row it added, out of order

        int found = await model.SearchOnSortedColumnAsync(last);
        Assert.That(found, Is.GreaterThanOrEqualTo(0), "found");
        Assert.That(model.GetItem(found).Nome, Is.EqualTo(last));
    }

    private EFDataGridModel<MGADbContext, MGAAdesione> SortedInMemory(bool editable)
    {
        SeedRows(300);
        EFDataGridModel<MGADbContext, MGAAdesione> model = editable
            ? new EditableEFDataGridModel<MGADbContext, MGAAdesione>() { DbContextFactory = _dbCtxFactory,
                BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome), ItemKeyExpr = a => a.Id, MinRowsToRead = 10 }
            : new EFDataGridModel<MGADbContext, MGAAdesione>() { DbContextFactory = _dbCtxFactory,
                BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome), ItemKeyExpr = a => a.Id, MinRowsToRead = 10 };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome") { PreferredSize = 20 }, a => a.Cognome);
        model.AddColumn("NomeInMem", new Column<MGAAdesione, string>("Nome") { PreferredSize = 20 }, a => a.Nome);   // not pushed down
        model.SetSortedColumn("NomeInMem", false);
        return model;
    }

    /// <summary>
    /// The grid edits a row of an EF model in place: with OnPost the page saves it and the row shows what it saved; without it the
    /// grid posts through the model, whose reload reads the database the grid never wrote.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task GridEditsARowOfAnEFModelInPlaceTest(bool withOnPost)
    {
        SeedRows(20);
        using var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        RenderFragment columns = b =>
        {
            b.OpenComponent<GridColumn<MGAAdesione, string>>(0);
            b.AddAttribute(1, "Name", "Cognome");
            b.AddAttribute(2, "EFProperty", (Expression<Func<MGAAdesione, string>>)(a => a.Cognome));
            b.CloseComponent();
        };
        Func<GridPost<MGAAdesione>, Task> save = async post =>
        {
            using (var dbCtx = _dbCtxFactory.CreateDbContext())
            {
                dbCtx.MGAAdesione.Single(a => a.Id == post.Item.Id).Cognome = post.Item.Cognome;
                dbCtx.SaveChanges();
            }
            await model.PostAsync();
        };
        using BunitContext ctx = GridContext();
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps =>
        {
            ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.EditMode, EditMode.Manual)
                .Add(g => g.ChildContent, columns);
            if (withOnPost)
                ps.Add(g => g.OnPost, save);
        });
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        int id = model.GetCurrentItem()!.Id;
        string before = model.GetCurrentItem()!.Cognome;

        KeyAction f2 = cut.FindComponents<KeyAction>()
            .Single(k => k.Instance.Key == "F2" && !k.Instance.CtrlKey && k.Instance.BindAncestorWithTag == "div" && k.Instance.Enabled).Instance;
        await cut.InvokeAsync(() => f2.ProcessAsync(null, "F2", null));
        await cut.InvokeAsync(() => cut.Find("td.editing input").ChangeAsync(new ChangeEventArgs { Value = "Changed" }));
        await cut.InvokeAsync(() => cut.Instance.PostAsync());
        string inDb;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            inDb = dbCtx.MGAAdesione.Single(a => a.Id == id).Cognome;
        string expected = withOnPost ? "Changed" : before;
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False);
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(id));
            Assert.That(model.GetCurrentItem()!.Cognome, Is.EqualTo(expected));
            Assert.That(inDb, Is.EqualTo(expected));
        });
    }

    private sealed class RefineRecordingModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        public readonly List<(bool SingleItemReload, bool Paged)> Queries = [];
        public readonly List<(string Phase, int Id)> Refined = [];   // RefineItemPhase is protected

        protected override IQueryable<MGAAdesione> RefineQueryBeforeExec(MGADbContext dbContext, bool singleItemReload, IQueryable<MGAAdesione> query)
        {
            Queries.Add((singleItemReload, query.Expression.ToString().Contains("Skip")));
            return base.RefineQueryBeforeExec(dbContext, singleItemReload, query);
        }

        protected override ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            Refined.Add((phase.ToString(), item.Id));
            return base.RefineItemAsync(dbContext, item, phase, ct);
        }
    }

    /// <summary>
    /// RefineQueryBeforeExec sees each query before its page is cut, and is told a single row's reload from a fetch; RefineItemAsync
    /// refines every row read before a filter applied in memory, and after it only the rows the filter keeps.
    /// </summary>
    [Test]
    public async Task RefineHooksSeeTheQueriesAndTheRowsTest()
    {
        SeedRows(30);
        using var model = new RefineRecordingModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        model.AddColumn("IdInMem", new Column<MGAAdesione, int>("Id, in memory"), a => a.Id);   // not pushed down
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("IdInMem", new NumberDef<int>() { From = 10 })]);
        await ((IGridModel<MGAAdesione>)model).FetchRowsAsync(0, 10);
        List<int> before = model.Refined.Where(r => r.Phase == "BeforeFiltering").Select(r => r.Id).ToList();
        List<int> after = model.Refined.Where(r => r.Phase == "AfterFiltering").Select(r => r.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(model.Queries, Is.Not.Empty.And.All.EqualTo((false, false)), "a fetch, before Skip and Take");
            Assert.That(before, Does.Contain(9), "read and refined, then filtered out");
            Assert.That(after, Is.EqualTo(Enumerable.Range(0, model.GetCurrentRowCount()).Select(i => model.GetItem(i).Id)),
                "only the rows kept");
            Assert.That(after, Is.All.GreaterThanOrEqualTo(10));
        });

        model.Queries.Clear();
        await model.ReloadItemAsync(0);
        Assert.That(model.Queries, Does.Contain((true, false)), "a single row's reload");
    }

    /// <summary>
    /// A sort that moves the current record past the rows read reads on to find it again, unless AlwaysRestoreCurrentPK is false:
    /// then the current row keeps its index, on another record, and nothing more is read.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task AlwaysRestoreCurrentPKDecidesWhetherARestoreReadsOnTest(bool restore)
    {
        SeedRows(300);
        using var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
            AlwaysRestoreCurrentPK = restore,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetSortedColumn("Cognome", true);
        await ((IGridModel<MGAAdesione>)model).FetchRowsAsync(0, 20);
        model.SetCurrentRow(5);
        int chosen = model.GetCurrentItem()!.Id;

        model.SetSortedColumn("Cognome", false);   // the record goes near the end
        if (restore)
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(chosen));
        else
            Assert.Multiple(() =>
            {
                Assert.That(model.GetCurrentRow(), Is.EqualTo(5));
                Assert.That(model.GetCurrentItem()!.Id, Is.Not.EqualTo(chosen));
                Assert.That(model.GetCurrentRowCount(), Is.LessThan(100), "not read on");
            });
    }

    private static BunitContext GridContext()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        return ctx;
    }

    /// <summary>
    /// The grid's render reads on past the rows read: under a sort in memory the current row stays on its record, and with an
    /// edit open the render does not try to move the frozen row back to its old index.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void GridKeepsTheCurrentRecordWhenItsRenderReadsOnTest(bool editing)
    {
        using BunitContext ctx = GridContext();
        using var model = SortedInMemory(editable: editing);
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps.Add(g => g.Model, model).Add(g => g.PageSize, 5).Add(g => g.Spinner, new NoSpinner()));
        cut.InvokeAsync(() => model.SetCurrentRow(3)).Wait();
        int chosen = model.GetCurrentItem()!.Id;
        if (editing)
            cut.InvokeAsync(async () => await ((EditableEFDataGridModel<MGADbContext, MGAAdesione>)model).EditAsync()).Wait();
        Assert.DoesNotThrow(() => cut.Render(ps => ps.Add(g => g.PageSize, 20)), "a taller grid: the render reads on");
        Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(chosen));
    }

    /// <summary>
    /// A current row set past the rows read keeps its index when the fetch reaches it: only a row already read follows its
    /// record across the sort in memory.
    /// </summary>
    [Test]
    public void CurrentRowPastTheRowsReadKeepsItsIndexTest()
    {
        using var model = SortedInMemory(editable: false);
        model.GetCurrentRowCount();   // the first window
        model.SetCurrentRow(25);
        model.GetCurrentItem();       // reads on up to it
        Assert.That(model.GetCurrentRow(), Is.EqualTo(25));
    }

    /// <summary>
    /// Under a sort in memory a fetch can place the rows it adds before those already searched: a forward search scans again
    /// from its start, so a value read later is found wherever it lands.
    /// </summary>
    [TestCase(-1)]
    [TestCase(0)]
    public async Task ContainsSearchFindsRowsTheSortInMemoryPlacesBeforeTest(int startFrom)
    {
        using var model = SortedInMemory(editable: false);
        int found = await model.ContainsSearchAsync("NomeInMem", "Nome000150", forward: true, startFrom);
        Assert.That(found, Is.GreaterThanOrEqualTo(0));
        Assert.That(model.GetItem(found).Nome, Is.EqualTo("Nome000150"));
    }

    /// <summary>
    /// A forward search that starts past the rows read finds nothing before its start: the fetches it makes do not move the
    /// start back to the end of the rows read.
    /// </summary>
    [Test]
    public async Task ContainsSearchPastTheRowsReadStartsWhereAskedTest()
    {
        SeedRows(300);
        using var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        Assert.That(model.GetCurrentRowCount(), Is.LessThan(200), "the rows read end before the start");
        int found = await model.ContainsSearchAsync("Nome", "Nome", forward: true, startFrom: 200);
        Assert.That(found, Is.EqualTo(200));
    }

    /// <summary>
    /// A backward search that starts past the rows read reads up to its start first: the rows between the last one read and
    /// its start are searched too.
    /// </summary>
    [Test]
    public async Task BackwardContainsSearchPastTheRowsReadReadsUpToItsStartTest()
    {
        SeedRows(300);
        using var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        int read = model.GetCurrentRowCount();
        Assert.That(read, Is.LessThan(150), "the rows read end before the match");
        int found = await model.ContainsSearchAsync("Nome", "Nome000150", forward: false, startFrom: 200);
        Assert.Multiple(async () =>
        {
            Assert.That(found, Is.GreaterThanOrEqualTo(read), "a row past those read first");
            Assert.That(model.GetItem(found).Nome, Is.EqualTo("Nome000150"));
            Assert.That(await model.ContainsSearchAsync("Nome", "Nome", forward: false, startFrom: 200), Is.EqualTo(200), "the start itself is searched");
        });
    }

    /// <summary>
    /// A backward search that starts on the first row not read reads it and searches it, as any start is searched.
    /// </summary>
    [Test]
    public async Task BackwardContainsSearchFromTheFirstRowNotReadSearchesItTest()
    {
        SeedRows(300);
        using var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        int read = model.GetCurrentRowCount();
        string expected;
        using (var db = _dbCtxFactory.CreateDbContext())
            expected = db.MGAAdesione.OrderBy(a => a.Id).Skip(read).First().Nome;
        Assert.That(await model.ContainsSearchAsync("Nome", expected, forward: false, startFrom: read), Is.EqualTo(read));
    }

    /// <summary>
    /// A backward search from the row count, int.MaxValue until every row is read, starts from the end, as on the in-memory
    /// models: it reads every row, then searches back.
    /// </summary>
    [Test]
    public async Task BackwardContainsSearchFromTheRowCountStartsFromTheEndTest()
    {
        SeedRows(300);
        using var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        Assert.That(model.CanDoContainsSearch(), Is.True);
        Assert.That(model.GetRowCount(), Is.EqualTo(int.MaxValue), "not every row read yet");
        int found = await model.ContainsSearchAsync("Nome", "Nome000150", forward: false, model.GetRowCount());
        Assert.That(model.GetItem(found).Nome, Is.EqualTo("Nome000150"));
    }

    /// <summary>
    /// A filter that is no longer applicable is discarded by the next query, on a column pushed down or applied in memory.
    /// </summary>
    [TestCase("Cognome")]
    [TestCase("NomeInMem")]
    public async Task FilterNoLongerApplicableIsDiscardedTest(string column)
    {
        using var model = SortedInMemory(editable: false);
        model.ApplyFilters([new(column, new FiltersTests.ScopedDef(() => true) { Value = "000150" })]);
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.That(model.GetRowCount(), Is.EqualTo(1), "applied");
        model.ApplyFilters([new(column, new FiltersTests.ScopedDef(() => false) { Value = "000150" })]);
        await model.ReadAllDataAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(303), "discarded");
            Assert.That(model.IsFiltered(), Is.False);
        });
    }

    /// <summary>
    /// A filter that stops being applicable between two pages stays on until the next fresh query: the pages appended are
    /// counted under the filters the first one was read with, so none is skipped or read twice.
    /// </summary>
    [Test]
    public async Task FilterStaysOnForThePagesOfAQueryTest()
    {
        SeedRows(300);
        bool applicable = true;
        using var model = new EFDataGridModel<MGADbContext, MGAAdesione>
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.ApplyFilters([new("Cognome", new FiltersTests.ScopedDef(() => applicable) { Value = "Cognome0001" })]);
        await model.FetchRowsAsync(0, 10);
        applicable = false;
        await model.ReadAllDataAsync(CancellationToken.None);
        List<string> names = Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Cognome).ToList();
        Assert.That(names, Is.Unique.And.Count.EqualTo(100).And.All.StartsWith("Cognome0001"), "the query's own filter");

        model.ForceReload(false);
        Assert.That(ReadAllIds(model), Has.Count.EqualTo(303), "a fresh query discards it");
        Assert.That(model.IsFiltered(), Is.False);
    }

    /// <summary>
    /// While an edit is pending a paged model is frozen: a reload of the whole model or of one row throws.
    /// </summary>
    [Test]
    public async Task FrozenModelRefusesReloadsTest()
    {
        using var model = (EditableEFDataGridModel<MGADbContext, MGAAdesione>)SortedInMemory(editable: true);
        model.SetCurrentRow(2);
        await model.EditAsync();
        Assert.Multiple(() =>
        {
            Assert.That(() => model.ForceReload(true), Throws.InvalidOperationException);
            Assert.That(async () => await model.ReloadItemAsync(2), Throws.InvalidOperationException);
            Assert.That(() => model.RemoveColumn("NomeInMem"), Throws.InvalidOperationException, "removing the sorted column");
            Assert.That(model.GetSortedColumn(), Is.EqualTo("NomeInMem"), "the sort stays");
        });
        await model.CancelAsync();
        Assert.That(() => model.ForceReload(true), Throws.Nothing, "thawed");
    }

    /// <summary>
    /// When the render reads on and the sort in memory moves the current row down with its record, the page follows it: going
    /// down row by row, the current row is always shown.
    /// </summary>
    [Test]
    public void PageFollowsTheCurrentRowTheFetchMovesTest()
    {
        using BunitContext ctx = GridContext();
        using var model = SortedInMemory(editable: false);
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps.Add(g => g.Model, model).Add(g => g.PageSize, 5).Add(g => g.Spinner, new NoSpinner()));
        List<string> offPage = [];
        for (int i = 0; i < 40; i++)
        {
            cut.InvokeAsync(() => model.SetCurrentRow(model.GetCurrentRow() + 1)).Wait();
            cut.Render();
            int current = model.GetCurrentRow(), page = cut.Instance.GetPageIdx();
            if (current < page || current >= page + 5)
                offPage.Add($"step {i}: current {current}, page {page}");
        }
        Assert.That(offPage, Is.Empty);
    }

    /// <summary>Records, for each row a query reads, whether the grid's spinner monitor was running.</summary>
    private sealed class MonitorRecordingModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        public readonly List<bool> Monitored = [];
        public Func<bool>? IsMonitored;
        protected override ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            if (phase == RefineItemPhase.BeforeFiltering)
                lock (Monitored)
                    Monitored.Add(IsMonitored?.Invoke() ?? false);
            return base.RefineItemAsync(dbContext, item, phase, ct);
        }
    }

    /// <summary>
    /// The query a sort or a reload leaves pending runs in the render under the spinner's monitor, and with it its timeout.
    /// </summary>
    [Test]
    public void RenderQueriesUnderTheSpinnerTest()
    {
        SeedRows(300);
        using BunitContext ctx = GridContext();
        using var model = new MonitorRecordingModel() { DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Cognome), ItemKeyExpr = a => a.Id, MinRowsToRead = 10 };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome") { PreferredSize = 20 }, a => a.Cognome);
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps.Add(g => g.Model, model).Add(g => g.PageSize, 5).Add(g => g.Spinner, new NoSpinner()));
        model.IsMonitored = () => !cut.Instance.IsIdle();
        model.Monitored.Clear();
        cut.InvokeAsync(() => model.SetSortedColumn("Cognome", false)).Wait();
        cut.Render();
        Assert.That(model.Monitored, Is.Not.Empty.And.All.True, "after a sort");
        model.Monitored.Clear();
        cut.InvokeAsync(() => model.ForceReload(true)).Wait();
        cut.Render();
        Assert.That(model.Monitored, Is.Not.Empty.And.All.True, "after a reload");
    }

    private sealed class FailingReadEditableModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        public int FailAt { get; set; } = -1;
        private int _read;

        protected override ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            if (phase == RefineItemPhase.BeforeFiltering && FailAt >= 0 && ++_read == FailAt)
                throw new InvalidOperationException("the host's refine failed");
            return ValueTask.FromResult(item);
        }
    }

    /// <summary>
    /// A ReadAll read that starts again after a failed one replaces the rows, the one being edited included: the post must
    /// reload by the key the edited item is stored under, or a row whose key member was edited is lost.
    /// </summary>
    [Test]
    public async Task PostAfterAReadAllReReadDuringTheEditKeepsTheRowTest()
    {
        var model = new FailingReadEditableModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => new { a.NumeroPolizza, a.NumeroAdesione },
            ReadAll = true,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        SeedRows(50);
        model.FailAt = 20;
        Assert.CatchAsync(async () => await model.ReadAllDataAsync(CancellationToken.None));
        model.FailAt = -1;
        model.SetCurrentRow(5);
        MGAAdesione edited = await model.EditAsync();
        edited.NumeroAdesione = 99999;
        await model.ReadAllDataAsync(CancellationToken.None);   // the read starts again while the edit is open
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.MGAAdesione.Single(a => a.Id == edited.Id).NumeroAdesione = 99999;
            dbCtx.SaveChanges();
        }
        await model.PostAsync();
        List<int> ids = Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
        Assert.That(ids, Is.Unique.And.Count.EqualTo(53).And.Contains(edited.Id));
    }

    /// <summary>
    /// The single-row reload with a value-type key: the key predicate must compare the unboxed member, not the boxed
    /// object, or Expression.Equal throws at construction.
    /// </summary>
    [Test]
    public async Task ReloadItemAsyncOnAnIntKeyTest()
    {
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Assert.That(model.GetRowCount(), Is.EqualTo(3));
        int id = model.GetItem(1).Id;
        Assert.That(await model.ReloadItemAsync(1), Is.True, "the row still exists");
        Assert.That(model.GetItem(1).Id, Is.EqualTo(id));
    }

    /// <summary>
    /// The single-row reload observes its token like the paged reads do: a cancelled one stops before the query, and the row
    /// stays as it was.
    /// </summary>
    [Test]
    public async Task ReloadItemAsyncObservesItsTokenTest()
    {
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        MGAAdesione before = model.GetItem(1);
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await model.ReloadItemAsync(1, before.Id, cts.Token));
        Assert.That(model.GetItem(1), Is.SameAs(before), "nothing was replaced");
        Assert.That(await model.ReloadItemAsync(1, before.Id), Is.True, "without a token the reload runs as before");
        await model.ReadAllDataAsync();   // the token is optional here too
        Assert.That(model.GetRowCount(), Is.EqualTo(3));
    }

    /// <summary>
    /// Edit then cancel on the EF editable model: cancel reloads the row through ReloadItemAsync, the path M1 broke.
    /// </summary>
    [Test]
    public async Task EditThenCancelOnTheEfEditableModelTest()
    {
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Assert.That(model.GetRowCount(), Is.EqualTo(3));
        model.SetCurrentRow(0);
        MGAAdesione draft = await model.EditAsync();
        Assert.That(model.IsEditPending(), Is.True);
        await model.CancelAsync();
        Assert.That(model.IsEditPending(), Is.False);
        Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(draft.Id));
    }

    /// <summary>
    /// Removing the sorted or a filtered column must clear the sort and the filter on it, as the in-memory model does;
    /// otherwise the next query composes a column that no longer exists.
    /// </summary>
    [Test]
    public void RemoveColumnClearsItsSortAndFilterTest()
    {
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        model.AddEFColumn("Titolare", new Column<MGAAdesione, string>("Titolare"), a => a.Titolare!.Cognome);
        model.SetSortedColumn("Titolare", true);
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Titolare", new DefaultDef() { IncludeNull = false })]);
        Assert.That(model.GetRowCount(), Is.EqualTo(2));
        Assert.That(model.RemoveColumn("Titolare"), Is.Not.Null);
        Assert.That(model.GetSortedColumn(), Is.Null);
        Assert.That(model.GetFilters(), Is.Empty);
        Assert.That(model.GetRowCount(), Is.EqualTo(3), "queries again without the removed column");
    }

    /// <summary>
    /// A date's "To" on a whole minute keeps the rows within that minute in the database too: the row stamped last is kept by
    /// the minute it was stamped in.
    /// </summary>
    [Test]
    public void DateDefToCoversItsMinuteInTheDatabaseTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        MGAAdesione last = dbCtx.MGAAdesione.AsNoTracking().OrderByDescending(x => x.CreationTime).First();
        long ticks = last.CreationTime.Ticks;
        DateDef<DateTime> def = new() { To = new DateTime(ticks - ticks % TimeSpan.TicksPerMinute), IncludeNull = false };
        List<MGAAdesione> kept = dbCtx.MGAAdesione.AsNoTracking()
            .Where(def.CreatePredicate<MGAAdesione>(x => x.CreationTime)).ToList();
        Assert.That(kept.Select(x => x.Id), Does.Contain(last.Id));
    }

    /// <summary>
    /// DateDef filter test usage with EF core
    /// </summary>
    [Test]
    public void DynamicWhereDateDefTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGAAdesione> dbAdesioni = dbCtx.MGAAdesione;
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = true };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataNascita),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataNascita),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = true, Contains = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataNascita),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = false, Contains = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataNascita),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// DateDef filter test usage with EF core and a nullable property
    /// </summary>
    [Test]
    public void DynamicWhereNullDateDefTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGAAdesione> dbAdesioni = dbCtx.MGAAdesione;
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = true };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = false };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = true, Contains = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = false, Contains = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = false };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGAAdesione>(d => d.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// SetDef filter test usage with EF core
    /// </summary>
    [Test]
    public async Task DynamicWhereSetDefTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGAAdesione> dbAdesioni = dbCtx.MGAAdesione;
        {
            SetDef<string> sdef = new(false) { AllValues = ["PRTVNT", "PRTVNT68", "123456"], IncludeNull = true };
            sdef.Selected.Add("PRTVNT");
            sdef.Selected.Add("PRTVNT68");
            await sdef.PopulateAsync();
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    sdef.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            SetDef<string> sdef = new(false) { AllValues = ["PRTVNT", "PRTVNT68", "123456"], IncludeNull = false };
            sdef.Selected.Add("PRTVNT");
            sdef.Selected.Add("PRTVNT68");
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    sdef.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// EnulDef filter test usage with EF core
    /// </summary>
    [Test]
    public void DynamicWhereEnumDefTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGAAdesione> dbAdesioni = dbCtx.MGAAdesione;
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = true };
            EnumDef edef = new(typeof(TipoLegame), false) { IncludeNull = true };
            edef.Selected.Add(TipoLegame.Titolare);
            edef.Selected.Add(TipoLegame.Figlio);
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    edef.CreatePredicate<MGAAdesione>(d => d.Legame),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = false };
            EnumDef edef = new(typeof(TipoLegame), false) { IncludeNull = false };
            edef.Selected.Add(TipoLegame.Titolare);
            edef.Selected.Add(TipoLegame.Figlio);
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    edef.CreatePredicate<MGAAdesione>(d => d.Legame),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = true, Contains = false };
            EnumDef edef = new(typeof(TipoLegame), false) { IncludeNull = true };
            edef.Selected.Add(TipoLegame.Titolare);
            edef.Selected.Add(TipoLegame.Figlio);
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    edef.CreatePredicate<MGAAdesione>(d => d.Legame),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = false, Contains = false };
            EnumDef edef = new(typeof(TipoLegame), false) { IncludeNull = false };
            edef.Selected.Add(TipoLegame.Titolare);
            edef.Selected.Add(TipoLegame.Figlio);
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    edef.CreatePredicate<MGAAdesione>(d => d.Legame),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// EnumDef filter test usage with EF core and a nullable property
    /// </summary>
    [Test]
    public void DynamicWhereNullEnumDefTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGAAdesione> dbAdesioni = dbCtx.MGAAdesione;
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = true };
            EnumDef edef = new(typeof(TipoTariffa?), true) { IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    edef.CreatePredicate<MGAAdesione>(d => d.Tariffa),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = false };
            EnumDef edef = new(typeof(TipoTariffa?), true) { IncludeNull = false };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbAdesioni.AsNoTracking()
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGAAdesione, bool>([
                    def.CreatePredicate<MGAAdesione>(d => d.CodiceFiscaleSocio),
                    edef.CreatePredicate<MGAAdesione>(d => d.Tariffa),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// DateDef filter test usage with EF core, a join and a nullable property
    /// </summary>
    [Test]
    public void WithJoinDynamicWhereNullDateDefTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        DbSet<MGASinistro> dbSinistri = dbCtx.MGASinistro;
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = true };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbSinistri.AsNoTracking().Include(x => x.Adesione)
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGASinistro, bool>([
                    def.CreatePredicate<MGASinistro>(d => d.Adesione.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGASinistro>(d => d.Adesione.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
        {
            DefaultDef def = new() { Value = "VNT", IncludeNull = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = false };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbSinistri.AsNoTracking().Include(x => x.Adesione)
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGASinistro, bool>([
                    def.CreatePredicate<MGASinistro>(d => d.Adesione.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGASinistro>(d => d.Adesione.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = true, Contains = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = true };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbSinistri.AsNoTracking().Include(x => x.Adesione)
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGASinistro, bool>([
                    def.CreatePredicate<MGASinistro>(d => d.Adesione.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGASinistro>(d => d.Adesione.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
        {
            DefaultDef def = new() { Value = "PRTVNT", IncludeNull = false, Contains = false };
            DateDef<DateOnly> ddef = new() { To = new DateOnly(1996, 6, 24), IncludeNull = false };
            logger.LogInformation("Visually check the executed sql query where condition");
            var filtered = dbSinistri.AsNoTracking().Include(x => x.Adesione)
                .Where(x => x.Id > 0)
                .Where(ExprUtils.CombineExpressions<MGASinistro, bool>([
                    def.CreatePredicate<MGASinistro>(d => d.Adesione.CodiceFiscaleSocio),
                    ddef.CreatePredicate<MGASinistro>(d => d.Adesione.DataInizio),
                ], (e1, e2) => Expression.AndAlso(e1, e2)))
                .ToList();
            Assert.That(filtered.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// DateDef filter test usage with EF core
    /// </summary>
    [Test]
    public void ModelTest()
    {
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx,en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        model.AddEFColumn("Titolare", new Column<MGAAdesione, string>("Titolare"), a => a.Titolare!.Cognome);
        model.AddEFColumn("NomeTitolare", new Column<MGAAdesione, string>("Nome titolare"), a => a.Titolare!.Nome);
        Assert.That(model.GetRowCount(), Is.EqualTo(3));
        Assert.That(model.GetDataView().Where(r => r.Titolare != null).Count, Is.EqualTo(2));
        int cidx = model.GetColumnIndex("Titolare");
        int notNullCount = 0;
        for (int i = 0; i < model.GetRowCount(); i++)
        {
            var cognome = model.GetValueAt(i, cidx);
            if (cognome != null)
                notNullCount++;
        }
        Assert.That(notNullCount, Is.EqualTo(2));
        //
        logger.LogInformation("Visually check the executed sql query where condition");
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Titolare", new DefaultDef() {
            IncludeNull = false,
        })]);
        Assert.That(model.GetRowCount(), Is.EqualTo(2));
        //
        logger.LogInformation("Visually check the executed sql query where condition");
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("NomeTitolare", new DefaultDef() {
            IncludeNull = false,
            Value = "Vallers",
        })]);
        Assert.That(model.GetRowCount(), Is.EqualTo(2));
    }

    private void SeedRows(int count)
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        for (int i = 0; i < count; i++)
            dbCtx.Add(new MGAAdesione { NumeroPolizza = $"P{i:D6}", NumeroAdesione = (uint)(100 + i), Cognome = $"Cognome{i:D6}",
                Nome = $"Nome{i:D6}", DataNascita = new DateOnly(1980, 1, 1), Legame = MGAAdesione.TipoLegame.Titolare });
        dbCtx.SaveChanges();
    }

    /// <summary>
    /// Paging is by offset. A row that ReloadItemAsync finds gone has shifted every later database row up by one, so the
    /// offset must follow.
    /// </summary>
    [Test]
    public async Task RemovedRowDoesNotShiftTheNextWindowTest()
    {
        SeedRows(300);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        int deletedId = model.GetItem(5).Id;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.Remove(dbCtx.MGAAdesione.Single(a => a.Id == deletedId));
            dbCtx.SaveChanges();
        }
        Assert.That(await model.ReloadItemAsync(5), Is.False, "the row is gone");
        await model.ReadAllDataAsync(CancellationToken.None);
        HashSet<int> inDb;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            inDb = dbCtx.MGAAdesione.Select(a => a.Id).ToHashSet();
        var inModel = Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToHashSet();
        Assert.That(inModel, Is.EquivalentTo(inDb), "every remaining row, exactly once");
    }

    /// <summary>
    /// The pending row of an insert is counted by the model but is not in the backing list, so a paged fetch that covers it
    /// must ask the database for one row less and answer as if it had fetched it.
    /// </summary>
    [Test]
    public async Task InsertOnThePagedEfEditableModelRendersTest()
    {
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()).Add(g => g.PageSize, 5));
        Assert.That(cut.Markup, Does.Contain("Erlumoa"), "the three rows render");
        model.SetCurrentRow(2);
        await cut.InvokeAsync(() => model.InsertAsync(new MGAAdesione
        {
            NumeroPolizza = "0002", NumeroAdesione = 9, Cognome = "New", Nome = "Newcomer", DataNascita = new DateOnly(1980, 1, 1), Legame = TipoLegame.Titolare,
        }, afterCurrentRow: true).AsTask());
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(model.IsInsertPending(), Is.True);
            Assert.That(model.GetRowCount(), Is.EqualTo(4), "the pending row counts");
            Assert.That(cut.Markup, Does.Contain("Newcomer").And.Contain("Erlumoa"), "the pending row renders with the others");
        });
    }

    private sealed class QueryCapturingModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        public string LastSql = "";
        public int Queries;
        protected override IQueryable<MGAAdesione> CompleteQueryable(MGADbContext dbContext, IQueryable<MGAAdesione> query, ReadRequest request)
        {
            var res = base.CompleteQueryable(dbContext, query, request);
            Queries++;
            LastSql = res.ToQueryString().Replace("\r", "").Replace("\n", " ");
            return res;
        }
    }

    private QueryCapturingModel NewQueryCapturingModel(Func<MGADbContext, IQueryable<MGAAdesione>> baseQuery)
    {
        QueryCapturingModel model = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, en) => baseQuery(dbCtx), ItemKeyExpr = a => a.Id };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        return model;
    }

    /// <summary>The filters' values reach the database as parameters, so a value typed anew reuses the query's plan.</summary>
    [Test]
    public void FilterValuesAreSentAsParametersTest()
    {
        var model = NewQueryCapturingModel(dbCtx => dbCtx.MGAAdesione);
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        model.ApplyFilters([new("Cognome", new DefaultDef { Value = "Zxq", Contains = true }), new("Id", new NumberDef<int> { From = 12345 })]);
        model.GetCurrentRowCount();
        string statement = model.LastSql[model.LastSql.IndexOf("SELECT", StringComparison.Ordinal)..];
        Assert.That(statement, Does.Not.Contain("Zxq").And.Not.Contain("12345"), "no literal in the statement: " + model.LastSql);
    }

    /// <summary>A string column is filtered as it is, not cast, so an index on it serves the filter.</summary>
    [Test]
    public void StringFilterReadsTheColumnUncastTest()
    {
        var model = NewQueryCapturingModel(dbCtx => dbCtx.MGAAdesione);
        SetDef<string> names = new(false) { AllValues = ["Rossi", "Bianchi"], IncludeNull = false };
        names.Selected.Add("Rossi");
        foreach (AFilterDef def in new AFilterDef[] { names, new DefaultDef { Value = "Ro" } })
        {
            model.ApplyFilters([new("Cognome", def)]);
            model.GetCurrentRowCount();
            Assert.That(model.LastSql, Does.Not.Contain("CAST"), def.GetType().Name + ": " + model.LastSql);
        }
    }

    private sealed class ActorsDef : DynaValuesInSetDef<ActorsDef, MGAAttore, string?>
    {
        public ActorsDef(bool startFull = false) : base(false, a => a.Cognome, startFull) { }
        public override ActorsDef Clone() => new(StartFull()) { IncludeNullOrEmpty = IncludeNullOrEmpty, AllKeys = AllKeys == null ? null : new(AllKeys), Selected = new(Selected) };
        protected override DataGridModel<MGAAttore> CreateAllValuesModel() => new();
        protected override ValueTask<IList<MGAAttore>> RetrieveAllValuesAsync(CancellationToken ct)
            => ValueTask.FromResult<IList<MGAAttore>>([new() { Cognome = "Oropallo" }, new() { Cognome = "Pig" }, new() { Cognome = "Nobody" }]);
        public override ActorsDef Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => throw new NotSupportedException();
    }

    /// <summary>
    /// A filter on a collection column is pushed down when its definition allows it, with its selection as parameters: on the
    /// predicate alone, and on a model's column, whose expression the model wraps.
    /// </summary>
    [Test]
    public async Task ValuesInSetFilterIsPushedDownTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        ActorsDef actors = new() { IncludeNullOrEmpty = false };
        await actors.PopulateAsync();
        actors.Selected.Add("Pig");
        var claims = dbCtx.MGASinistro.Where(actors.CreatePredicate<MGASinistro>(s => s.Attori.AsQueryable()));
        string sql = claims.ToQueryString();
        Assert.That(sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..], Does.Not.Contain("Pig"), "no literal in the statement: " + sql);
        Assert.That(claims.Count(), Is.EqualTo(1), "an actor selected");

        EFDataGridModel<MGADbContext, MGASinistro> model = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (db, en) => db.MGASinistro, ItemKeyExpr = s => s.Id };
        model.AddEFColumn("Attori", new Column<MGASinistro, IQueryable<MGAAttore>>("Attori") { Sortable = false }, s => s.Attori.AsQueryable());
        model.ApplyFilters([new("Attori", actors)]);
        Assert.That(model.GetRowCount(), Is.EqualTo(1), "an actor selected, on the model");
        ActorsDef nobody = new() { IncludeNullOrEmpty = true };
        await nobody.PopulateAsync();
        nobody.Selected.Add("Nobody");
        model.ApplyFilters([new("Attori", nobody)]);
        Assert.That(model.GetRowCount(), Is.Zero, "no actor selected, on the model");
    }

    private sealed class LazyCodesDef : DynaSetDef<LazyCodesDef, string, string>
    {
        public LazyCodesDef(bool startFull) : base(c => c, startFull) { }
        public override LazyCodesDef Clone() => new(StartFull()) { IncludeNull = IncludeNull, AllKeys = AllKeys == null ? null : new(AllKeys), Selected = new(Selected) };
        protected override DataGridModel<string> CreateAllValuesModel() => new();
        protected override ValueTask<IList<string>> RetrieveAllValuesAsync(CancellationToken ct) => ValueTask.FromResult<IList<string>>(["PRTVNT"]);
        public override LazyCodesDef Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => throw new NotSupportedException();
    }

    /// <summary>
    /// A start-full set built in code and never shown has not read its values: pushed down, it keeps every row with a value, for a
    /// set of values and for a set over a collection.
    /// </summary>
    [Test]
    public void StartFullSetNeverPopulatedKeepsEveryValueWhenPushedDownTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        EFDataGridModel<MGADbContext, MGAAdesione> members = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (db, en) => db.MGAAdesione, ItemKeyExpr = a => a.Id };
        members.AddEFColumn("Code", new Column<MGAAdesione, string?>("Code"), a => a.CodiceFiscaleSocio);
        members.ApplyFilters([new("Code", new LazyCodesDef(true) { IncludeNull = false })]);
        EFDataGridModel<MGADbContext, MGASinistro> claims = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (db, en) => db.MGASinistro, ItemKeyExpr = s => s.Id };
        claims.AddEFColumn("Attori", new Column<MGASinistro, IQueryable<MGAAttore>>("Attori") { Sortable = false }, s => s.Attori.AsQueryable());
        claims.ApplyFilters([new("Attori", new ActorsDef(true) { IncludeNullOrEmpty = false })]);
        int withCode = dbCtx.MGAAdesione.Count(a => a.CodiceFiscaleSocio != null);
        int withActors = dbCtx.MGASinistro.Count(s => s.Attori.Any());
        Assert.Multiple(() =>
        {
            Assert.That(withCode, Is.Positive.And.LessThan(dbCtx.MGAAdesione.Count()), "the fixture has rows with and without a code");
            Assert.That(members.GetRowCount(), Is.EqualTo(withCode));
            Assert.That(withActors, Is.Positive, "the fixture has claims with actors");
            Assert.That(claims.GetRowCount(), Is.EqualTo(withActors));
        });
    }

    /// <summary>A set filter's selection reaches the database as parameters too.</summary>
    [Test]
    public void SetFilterSelectionIsSentAsParametersTest()
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        SetDef<string> codes = new(false) { AllValues = ["PRTVNT", "PRTVNT68", "123456"], IncludeNull = false };
        codes.Selected.Add("PRTVNT");
        codes.Selected.Add("PRTVNT68");
        var members = dbCtx.MGAAdesione.Where(codes.CreatePredicate<MGAAdesione>(a => a.CodiceFiscaleSocio));
        string sql = members.ToQueryString();
        Assert.That(sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..], Does.Not.Contain("PRTVNT"), "no literal in the statement: " + sql);
        Assert.That(members.Count(), Is.EqualTo(1));
    }

    /// <summary>
    /// Paging by offset needs a total order: without one the windows of a paged query may overlap or leave gaps. When the grid's
    /// sort is not pushed down the key completes whatever order the host gave, before any filter is appended, and replaces nothing.
    /// </summary>
    [Test]
    public void PagedQueryIsAlwaysOrderedTest()
    {
        KeyValuePair<string, AFilterDef>[] filter = [new("Cognome", new DefaultDef { Value = "Pro", Contains = true })];

        var plain = NewQueryCapturingModel(dbCtx => dbCtx.MGAAdesione);
        plain.GetCurrentRowCount();
        Assert.That(plain.LastSql, Does.Match("ORDER BY \"m\".\"Id\" LIMIT"), "no order at all: the key orders");

        var hostOrdered = NewQueryCapturingModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Cognome));
        hostOrdered.GetCurrentRowCount();
        Assert.That(hostOrdered.LastSql, Does.Match("ORDER BY \"m\".\"Cognome\", \"m\".\"Id\" LIMIT"), "the host's order comes first, the key completes it");
        hostOrdered.ApplyFilters(filter);
        hostOrdered.GetCurrentRowCount();
        Assert.That(hostOrdered.LastSql, Does.Match("WHERE .* ORDER BY \"m\".\"Cognome\", \"m\".\"Id\" LIMIT"), "and a filter changes nothing about that");

        var hostFiltered = NewQueryCapturingModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Cognome).Where(a => a.Id > 0));
        hostFiltered.GetCurrentRowCount();
        Assert.That(hostFiltered.LastSql, Does.Match("ORDER BY \"m\".\"Cognome\", \"m\".\"Id\" LIMIT"), "a base the host ordered and then filtered gets the key after its order");

        var sorted = NewQueryCapturingModel(dbCtx => dbCtx.MGAAdesione);
        sorted.SetSortedColumn("Cognome", false);
        sorted.GetCurrentRowCount();
        Assert.That(sorted.LastSql, Does.Match("ORDER BY \"m\".\"Cognome\" DESC, \"m\".\"Id\" DESC LIMIT"), "a pushed-down sort is left as it was");
    }

    /// <summary>
    /// The host's OrderBy need not be the base query's last call: the key goes right after it when every call that follows keeps
    /// the row type, so paging over ties stays total. After a projection from another type the order is left as it is.
    /// </summary>
    [TestCase("AsNoTracking")]
    [TestCase("Include")]
    [TestCase("Select")]
    [TestCase("projection from another type")]
    public void KeyCompletesAnOrderThatIsNotTheLastCallTest(string shape)
    {
        var model = NewQueryCapturingModel(dbCtx => shape switch
        {
            "AsNoTracking" => dbCtx.MGAAdesione.OrderBy(a => a.Cognome).AsNoTracking(),
            "Include" => dbCtx.MGAAdesione.OrderBy(a => a.Cognome).Include(a => a.Titolare),
            "Select" => dbCtx.MGAAdesione.OrderBy(a => a.Cognome).Select(a => new MGAAdesione { Id = a.Id, Cognome = a.Cognome }),
            _ => dbCtx.MGAAdesione.OrderBy(a => a.Cognome).Select(a => new { Row = a }).Select(x => x.Row),
        });
        model.GetCurrentRowCount();
        Assert.That(model.LastSql, shape == "projection from another type"   // an Include pages in a subquery
            ? Does.Match("ORDER BY \"m\".\"Cognome\"\\s+LIMIT") : Does.Match("ORDER BY \"m\".\"Cognome\", \"m\".\"Id\"\\s+LIMIT"));
    }

    /// <summary>
    /// EF drops an order that a set operation follows, so the key orders the query on its own: without it the paged query had
    /// no ORDER BY at all. An order after the set operation is completed as usual.
    /// </summary>
    [TestCase("Distinct")]
    [TestCase("Union")]
    [TestCase("ordered after")]
    public void BaseOrderedBeforeASetOperationIsOrderedByTheKeyTest(string shape)
    {
        var model = NewQueryCapturingModel(dbCtx => shape switch
        {
            "Distinct" => dbCtx.MGAAdesione.OrderBy(a => a.Cognome).Distinct(),
            "Union" => dbCtx.MGAAdesione.OrderBy(a => a.Cognome).Union(dbCtx.MGAAdesione.Where(a => a.Id > 0)),
            _ => dbCtx.MGAAdesione.OrderBy(a => a.Nome).Distinct().OrderBy(a => a.Cognome),
        });
        Assert.That(model.GetCurrentRowCount(), Is.GreaterThan(0));
        Assert.That(model.LastSql, shape == "ordered after"
            ? Does.Match("ORDER BY \"\\w+\".\"Cognome\", \"\\w+\".\"Id\"\\s+LIMIT") : Does.Match("ORDER BY \"\\w+\".\"Id\"\\s+LIMIT"));
    }

    /// <summary>
    /// The current row may lie past the loaded window while the total is unknown; a sort must still find it again afterwards.
    /// </summary>
    [Test]
    public void SortWithTheCurrentRowPastTheLoadedWindowTest()
    {
        SeedRows(300);
        string expected;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            expected = dbCtx.MGAAdesione.OrderBy(a => a.Id).Skip(250).First().Nome;
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        model.SetCurrentRow(250);
        model.SetSortedColumn("Nome", false);
        Assert.That(model.GetCurrentItem()!.Nome, Is.EqualTo(expected), "the same row is current after the sort");
    }

    private EFDataGridModel<MGADbContext, MGAAdesione> NewPagedModelWithARowDeleted(int currentRow, int deletedRow)
    {
        SeedRows(10);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
        };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        Assert.That(model.GetRowCount(), Is.EqualTo(13));
        model.SetCurrentRow(currentRow);
        int deletedId = model.GetItem(deletedRow).Id;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.Remove(dbCtx.MGAAdesione.Single(a => a.Id == deletedId));
            dbCtx.SaveChanges();
        }
        return model;
    }

    /// <summary>
    /// A row that ReloadItemAsync finds gone is removed from the list; the current row must keep pointing at the same item.
    /// </summary>
    [Test]
    public async Task ReloadOfARemovedRowAboveTheCurrentOneKeepsTheCurrentItemTest()
    {
        var model = NewPagedModelWithARowDeleted(currentRow: 5, deletedRow: 1);
        string current = model.GetCurrentItem()!.Nome;
        Assert.That(await model.ReloadItemAsync(1), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(4));
            Assert.That(model.GetCurrentItem()!.Nome, Is.EqualTo(current));
        });
    }

    [Test]
    public async Task ReloadOfARemovedRowAboveTheLastCurrentOneKeepsTheCurrentItemTest()
    {
        var model = NewPagedModelWithARowDeleted(currentRow: 12, deletedRow: 4);
        string current = model.GetCurrentItem()!.Nome;
        Assert.That(await model.ReloadItemAsync(4), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(11));
            Assert.That(model.GetCurrentItem()!.Nome, Is.EqualTo(current), "the last row was current: without the shift this throws");
        });
    }

    /// <summary>
    /// Under an in-memory sort every fetch re-sorts the loaded rows, so the search for the current key must look at the whole list
    /// after each window, not only at the rows appended.
    /// </summary>
    [Test]
    public void RestoreOfTheCurrentKeyUnderAnInMemorySortStopsAtItsWindowTest()
    {
        SeedRows(300);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        model.AddColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);   // not pushed down
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Id", new NumberDef<int>() { From = 10 })]);
        model.SetSortedColumn("Nome", false);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one window, Ids 10 to 19");
        model.SetCurrentRow(Enumerable.Range(0, 10).First(i => model.GetItem(i).Id == 19));
        model.ApplyFilters([]);   // Id 19 is now in the second window
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()?.Id, Is.EqualTo(19), "the current key is found again");
            Assert.That(model.AllDataRead(), Is.False, "the search stops at the window holding it");
            Assert.That(model.GetCurrentRowCount(), Is.EqualTo(20));
        });
    }

    /// <summary>
    /// The reload after a post looks for the new row's key; when the active filter excludes it, one existence query must say so
    /// rather than a walk to the end of the table.
    /// </summary>
    [Test]
    public async Task PostingAnInsertTheFilterExcludesStaysWithinItsWindowTest()
    {
        SeedRows(300);
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 50,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Cognome", new DefaultDef() { Value = "Cognome", Contains = true })]);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(50));
        model.SetCurrentRow(3);
        await model.InsertAsync(new MGAAdesione
        {
            NumeroPolizza = "0002", NumeroAdesione = 9, Cognome = "New", Nome = "Newcomer", DataNascita = new DateOnly(1980, 1, 1), Legame = MGAAdesione.TipoLegame.Titolare,
        }, afterCurrentRow: true);
        await model.PostAsync();
        Assert.Multiple(() =>
        {
            Assert.That(model.IsInsertPending(), Is.False);
            Assert.That(model.AllDataRead(), Is.False, "the excluded key is not searched for to the end of the table");
            Assert.That(model.GetCurrentRowCount(), Is.LessThan(100), "one window");
            Assert.That(model.GetCurrentRow(), Is.InRange(0, model.GetCurrentRowCount() - 1));
        });
    }

    // What the in-memory filter reads comes from RefineItemAsync, not from the form
    private sealed class RefiningEditableModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        protected override ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            item.CodiceFiscaleSocio = item.Cognome.StartsWith('Z') ? "low" : "high";
            return ValueTask.FromResult(item);
        }
    }

    /// <summary>
    /// With no filter in the database, a posted insert that a filter applied in memory excludes once refined stays shown, at its
    /// place in the new query: the form's item, which RefineItemAsync never saw, must not decide.
    /// </summary>
    [Test]
    public async Task PostedInsertOutOfARefinedInMemoryFilterStaysShownTest()
    {
        SeedRows(100);
        var model = new RefiningEditableModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("Grade", new Column<MGAAdesione, string?>("Grade, in memory"), a => a.CodiceFiscaleSocio);   // not pushed down
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Grade", new DefaultDef() { Value = "high" })]);
        _ = model.GetRowCount();
        model.SetCurrentRow(3);
        MGAAdesione added = await model.InsertAsync(new MGAAdesione
        {
            NumeroPolizza = "0002", NumeroAdesione = 9, Cognome = "Zeta", Nome = "Newcomer", DataNascita = new DateOnly(1980, 1, 1),
            Legame = MGAAdesione.TipoLegame.Titolare, CodiceFiscaleSocio = "high",   // the form's value passes the filter
        }, afterCurrentRow: true);
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.Add(added);
            dbCtx.SaveChanges();
        }
        await model.PostAsync();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()?.Id, Is.EqualTo(added.Id), "the new row is current");
            Assert.That(Enumerable.Range(0, model.GetCurrentRowCount()).Any(i => model.GetItem(i).Id == added.Id), Is.True, "and shown");
        });
    }

    /// <summary>
    /// A paged model accepts a current row past the rows read while its total is unknown. An insert there reads up to that row
    /// first, so the post puts the new row where it was shown instead of past the end of the rows read.
    /// </summary>
    [Test]
    public async Task InsertAtACurrentRowPastTheRowsReadPostsTest()
    {
        SeedRows(30);
        using var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        await model.FetchRowsAsync(0, 5);
        model.SetCurrentRow(25);
        MGAAdesione added = await model.InsertAsync(new MGAAdesione
        {
            NumeroPolizza = "0002", NumeroAdesione = 9, Cognome = "Zeta", Nome = "Newcomer", DataNascita = new DateOnly(1980, 1, 1),
            Legame = MGAAdesione.TipoLegame.Titolare,
        }, afterCurrentRow: false);
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.Add(added);
            dbCtx.SaveChanges();
        }
        await model.PostAsync();
        Assert.That(model.GetCurrentItem()?.Id, Is.EqualTo(added.Id), "the new row is current");
    }

    public enum KeyShape { Anonymous, PositionalRecord, ConstructorAndInitializer, NewValueTuple, ValueTupleCreate, NewTuple, TupleCreate, Class }

    private record PolicyKey(string NumeroPolizza, uint NumeroAdesione);

    // A class compared by value, whose constructor names its properties in camelCase
    private sealed class ClassKey(string numeroPolizza, uint numeroAdesione) : IEquatable<ClassKey>
    {
        public string NumeroPolizza { get; } = numeroPolizza;
        public uint NumeroAdesione { get; } = numeroAdesione;
        public bool Equals(ClassKey? other) => other != null && other.NumeroPolizza == NumeroPolizza && other.NumeroAdesione == NumeroAdesione;
        public override bool Equals(object? obj) => Equals(obj as ClassKey);
        public override int GetHashCode() => HashCode.Combine(NumeroPolizza, NumeroAdesione);
    }

    private record InitKey(string NumeroPolizza)
    {
        public uint NumeroAdesione { get; init; }
    }

    private QueryCapturingModel NewCompositeKeyModel(KeyShape shape)
    {
        Expression<Func<MGAAdesione, object>> key = shape switch
        {
            KeyShape.PositionalRecord => a => new PolicyKey(a.NumeroPolizza, a.NumeroAdesione),
            KeyShape.ConstructorAndInitializer => a => new InitKey(a.NumeroPolizza) { NumeroAdesione = a.NumeroAdesione },
            KeyShape.NewValueTuple => a => new ValueTuple<string, uint>(a.NumeroPolizza, a.NumeroAdesione),   // an expression takes no tuple literal
            KeyShape.ValueTupleCreate => a => ValueTuple.Create(a.NumeroPolizza, a.NumeroAdesione),
            KeyShape.NewTuple => a => new Tuple<string, uint>(a.NumeroPolizza, a.NumeroAdesione),
            KeyShape.TupleCreate => a => Tuple.Create(a.NumeroPolizza, a.NumeroAdesione),
            KeyShape.Class => a => new ClassKey(a.NumeroPolizza, a.NumeroAdesione),
            _ => a => new { a.NumeroPolizza, a.NumeroAdesione },
        };
        QueryCapturingModel model = new()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = key,
            MinRowsToRead = 50,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMem", new Column<MGAAdesione, string>("Nome"), a => a.Nome);   // not pushed down
        return model;
    }

    private static List<int> ReadAllIds(EFDataGridModel<MGADbContext, MGAAdesione> model)
    {
        model.ReadAllDataAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
    }

    /// <summary>
    /// A key made of several members is used member by member: in the paging order, in the order under a pushed-down sort, in the
    /// in-memory tie-break and in the key predicate, none of which EF or the comparer can do with the whole object. An anonymous
    /// type names its members; a positional record names them by its constructor's parameters, a class by its constructor's
    /// parameters in camelCase, and an initializer adds its own to the constructor's; a tuple, built with new or Create, has its
    /// items.
    /// </summary>
    [TestCase(KeyShape.Anonymous)]
    [TestCase(KeyShape.PositionalRecord)]
    [TestCase(KeyShape.ConstructorAndInitializer)]
    [TestCase(KeyShape.NewValueTuple)]
    [TestCase(KeyShape.ValueTupleCreate)]
    [TestCase(KeyShape.NewTuple)]
    [TestCase(KeyShape.TupleCreate)]
    [TestCase(KeyShape.Class)]
    public void CompositeItemKeyPagesSortsAndRestoresTest(KeyShape shape)
    {
        SeedRows(300);
        var model = NewCompositeKeyModel(shape);
        model.GetRowCount();
        string orderBy = model.LastSql.Substring(model.LastSql.IndexOf("ORDER BY"));
        Assert.That(orderBy, Does.Contain("NumeroPolizza").And.Contain("NumeroAdesione"), "paged windows are ordered by the whole key");
        Assert.That(ReadAllIds(model), Is.Unique.And.Count.EqualTo(303), "windows disjoint");

        model = NewCompositeKeyModel(shape);
        model.SetSortedColumn("Cognome", false);
        Assert.That(model.GetRowCount(), Is.GreaterThan(0), "pushed-down sort");
        Assert.That(ReadAllIds(model), Is.Unique.And.Count.EqualTo(303));

        model = NewCompositeKeyModel(shape);
        model.SetSortedColumn("NomeInMem", false);
        Assert.That(model.GetRowCount(), Is.GreaterThan(0), "in-memory sort");

        model = NewCompositeKeyModel(shape);
        model.SetCurrentRow(1);
        var current = model.GetCurrentItem()!;
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Cognome", new DefaultDef() { Value = "Cognome", Contains = true })]);
        Assert.That(model.GetCurrentRow(), Is.InRange(0, model.GetCurrentRowCount() - 1), "the key restore queries the database");
    }

    [TestCase(KeyShape.Anonymous)]
    [TestCase(KeyShape.PositionalRecord)]
    [TestCase(KeyShape.ConstructorAndInitializer)]
    [TestCase(KeyShape.NewValueTuple)]
    [TestCase(KeyShape.ValueTupleCreate)]
    [TestCase(KeyShape.NewTuple)]
    [TestCase(KeyShape.TupleCreate)]
    [TestCase(KeyShape.Class)]
    public async Task CompositeItemKeyReloadsItsRowTest(KeyShape shape)
    {
        SeedRows(300);
        var model = NewCompositeKeyModel(shape);
        string before = model.GetItem(5).Cognome;
        int id = model.GetItem(5).Id;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.MGAAdesione.Single(a => a.Id == id).Cognome = before + "!";
            dbCtx.SaveChanges();
        }
        Assert.That(await model.ReloadItemAsync(5), Is.True);
        Assert.That(model.GetItem(5).Cognome, Is.EqualTo(before + "!"));
    }

    private EFDataGridModel<MGADbContext, MGAAdesione> NewPagedCognomeModel(Func<MGADbContext, IQueryable<MGAAdesione>> baseQuery)
    {
        SeedRows(300);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => baseQuery(dbCtx),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        return model;
    }

    private void SetCognomeInDb(int id, string cognome)
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        dbCtx.MGAAdesione.Single(a => a.Id == id).Cognome = cognome;
        dbCtx.SaveChanges();
    }

    /// <summary>
    /// A paged model spills the rows it has read to its SpillDirectory, the host's choice of where they rest on disk, and
    /// leaves nothing there once disposed.
    /// </summary>
    [Test]
    public void PagedModelSpillsToItsSpillDirectoryTest()
    {
        SeedRows(1200);   // more than the 32 resident chunks of 32 rows hold
        string dir = Path.Combine(Path.GetTempPath(), "glazor-spill-" + Path.GetRandomFileName());
        try
        {
            EFDataGridModel<MGADbContext, MGAAdesione> model = new() { DbContextFactory = _dbCtxFactory,
                BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione, ItemKeyExpr = a => a.Id, MinRowsToRead = 20, SpillDirectory = dir };
            Assert.That(AllIdsOf(model), Has.Count.GreaterThanOrEqualTo(1200), "every row read");
            Assert.That(Directory.GetFiles(dir), Is.Not.Empty, "the chunks evicted are written under the given directory");
            model.Dispose();
            Assert.That(Directory.GetFiles(dir), Is.Empty, "disposing the model deletes them");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A reload drops the rows read, and their spill files with them, at once: the next read starts from an empty directory.
    /// </summary>
    [Test]
    public void AReloadDeletesTheSpilledRowsTest()
    {
        SeedRows(1200);
        string dir = Path.Combine(Path.GetTempPath(), "glazor-spill-" + Path.GetRandomFileName());
        try
        {
            using EFDataGridModel<MGADbContext, MGAAdesione> model = new() { DbContextFactory = _dbCtxFactory,
                BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione, ItemKeyExpr = a => a.Id, MinRowsToRead = 20, SpillDirectory = dir };
            int count = AllIdsOf(model).Count;
            Assert.That(Directory.GetFiles(dir), Is.Not.Empty, "spilled");
            model.ForceReload(false);
            Assert.That(Directory.GetFiles(dir), Is.Empty, "the reload deletes them before reading again");
            Assert.That(AllIdsOf(model), Has.Count.EqualTo(count), "and reads every row again");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A wrapper asks the paged model it wraps for the current item: a current row set past the end while the count is unknown
    /// is read up to, and finding the end puts it on the last row, where GetItem on that row would throw.
    /// </summary>
    [Test]
    public void AWrapperReadsUpToACurrentRowPastTheRowsReadTest()
    {
        SeedRows(100);
        using EFDataGridModel<MGADbContext, MGAAdesione> model = new() { DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id), ItemKeyExpr = a => a.Id, MinRowsToRead = 20 };
        PlainWrapper wrapper = new(model);
        Assert.That(wrapper.GetCurrentRowCount(), Is.LessThan(100), "the count is not known yet");
        wrapper.SetCurrentRow(150);
        MGAAdesione? current = wrapper.GetCurrentItem();
        Assert.Multiple(() =>
        {
            Assert.That(wrapper.GetCurrentRow(), Is.EqualTo(model.GetRowCount() - 1), "the end found");
            Assert.That(current?.Id, Is.EqualTo(model.GetItem(model.GetRowCount() - 1).Id));
        });
    }

    private sealed class PlainWrapper(IDataGridModel<MGAAdesione> wrapped) : DataGridModelWrapper<MGAAdesione>(wrapped);

    private static List<int> AllIdsOf(EFDataGridModel<MGADbContext, MGAAdesione> model)
    {
        model.ReadAllDataAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Id).ToList();
    }

    /// <summary>Counts the paging queries, to tell a reload of the model from a reload of one row.</summary>
    private sealed class CountingEditableModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        public int Queries;
        protected override IQueryable<MGAAdesione> CompleteQueryable(MGADbContext dbContext, IQueryable<MGAAdesione> query, ReadRequest request)
        {
            Queries++;
            return base.CompleteQueryable(dbContext, query, request);
        }
    }

    private CountingEditableModel NewEditableCognomeModel(Func<MGADbContext, IQueryable<MGAAdesione>> baseQuery)
    {
        SeedRows(300);
        var model = new CountingEditableModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => baseQuery(dbCtx),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        return model;
    }

    private static KeyValuePair<string, AFilterDef> CognomeFilter() => new("Cognome", new DefaultDef() { Value = "Cognome", Contains = true });

    private List<int> IdsWhere(Func<MGAAdesione, bool> predicate)
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        return dbCtx.MGAAdesione.AsEnumerable().Where(predicate).Select(a => a.Id).ToList();
    }

    private static bool CognomeContainsCognome(MGAAdesione a) => a.Cognome.Contains("Cognome", StringComparison.OrdinalIgnoreCase);

    // Edits row 5 through the grid's own path: the form changes the list's item, and the host saves it
    private async Task<MGAAdesione> PostEditAsync(EditableEFDataGridModel<MGADbContext, MGAAdesione> model, Action<MGAAdesione> change)
    {
        model.SetCurrentRow(5);
        MGAAdesione edited = await model.EditAsync();
        change(edited);
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            MGAAdesione stored = dbCtx.MGAAdesione.Single(a => a.Id == edited.Id);
            stored.Cognome = edited.Cognome;
            stored.Nome = edited.Nome;
            dbCtx.SaveChanges();
        }
        await model.PostAsync();
        return edited;
    }

    private void DeleteFromDb(int id)
    {
        using var dbCtx = _dbCtxFactory.CreateDbContext();
        dbCtx.MGAAdesione.Remove(dbCtx.MGAAdesione.Single(a => a.Id == id));
        dbCtx.SaveChanges();
    }

    /// <summary>
    /// Paging is by offset. An edit that takes a reloaded row out of the filter moves every later row up by one, so the row
    /// leaves the list and the offset follows it. One that changes the value the database sorts on may have moved it past the
    /// last row read, so a new query puts it in its place.
    /// </summary>
    [Test]
    public async Task EditThatTakesARowOutOfTheFilterDoesNotShiftTheNextWindowTest()
    {
        var model = NewPagedCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Id));
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Cognome", new DefaultDef() { Value = "Cognome", Contains = true })]);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        int editedId = model.GetItem(5).Id;
        SetCognomeInDb(editedId, "Other");
        Assert.That(await model.ReloadItemAsync(5), Is.False, "the row left the filter");
        HashSet<int> expected;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            expected = dbCtx.MGAAdesione.Where(a => a.Cognome.Contains("Cognome")).Select(a => a.Id).ToHashSet();
        Assert.That(AllIdsOf(model), Is.EquivalentTo(expected), "every matching row, exactly once");
    }

    // A host that translates the Cognome filter itself, as a prefix match, and lets the base add the rest
    private static IQueryable<MGAAdesione> PrefixCognome(IQueryable<MGAAdesione> query, ref IReadOnlyDictionary<string, AFilterDef> filterDefs)
    {
        if (filterDefs.TryGetValue("Cognome", out AFilterDef? def) && def is DefaultDef { Value: { Length: > 0 } value })
        {
            filterDefs = filterDefs.Where(e => e.Key != "Cognome").ToDictionary();
            return query.Where(a => EF.Functions.Like(a.Cognome, value + "%"));
        }
        return query;
    }

    private sealed class PrefixFilterModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        protected override IQueryable<MGAAdesione> FilterQueryable(MGADbContext dbContext, IQueryable<MGAAdesione> query,
            IReadOnlyDictionary<string, AFilterDef> filterDefs)
            => base.FilterQueryable(dbContext, PrefixCognome(query, ref filterDefs), filterDefs);
    }

    private sealed class EditablePrefixFilterModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        protected override IQueryable<MGAAdesione> FilterQueryable(MGADbContext dbContext, IQueryable<MGAAdesione> query,
            IReadOnlyDictionary<string, AFilterDef> filterDefs)
            => base.FilterQueryable(dbContext, PrefixCognome(query, ref filterDefs), filterDefs);
    }

    /// <summary>
    /// A host that changes which rows a filter admits does it in FilterQueryable, which the single-row reload goes through too:
    /// a row the host's filter now excludes leaves the rows read, or is pinned on the editable model, and the next window
    /// neither skips nor repeats a row.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReloadFiltersAsTheHostsFilterQueryableDoesTest(bool editable)
    {
        SeedRows(300);
        EFDataGridModel<MGADbContext, MGAAdesione> model = editable
            ? new EditablePrefixFilterModel { DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
                ItemKeyExpr = a => a.Id, MinRowsToRead = 100 }
            : new PrefixFilterModel { DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
                ItemKeyExpr = a => a.Id, MinRowsToRead = 100 };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.ApplyFilters([CognomeFilter()]);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        int editedId = model.GetItem(5).Id;
        SetCognomeInDb(editedId, "XCognome");   // still contains the value, but no longer starts with it
        bool kept = await model.ReloadItemAsync(5);
        List<int> ids = AllIdsOf(model);
        List<int> expected = IdsWhere(a => a.Cognome.StartsWith("Cognome", StringComparison.OrdinalIgnoreCase));
        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(editable), editable ? "pinned where it was" : "the row left the filter");
            Assert.That(ids, Is.EquivalentTo(editable ? expected.Append(editedId) : expected), "every matching row, exactly once");
        });
    }

    [Test]
    public async Task EditThatMovesARowPastTheRowsReadDoesNotShiftTheNextWindowTest()
    {
        var model = NewPagedCognomeModel(dbCtx => dbCtx.MGAAdesione);
        model.SetSortedColumn("Cognome", true);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        int editedId = model.GetItem(5).Id;
        SetCognomeInDb(editedId, "ZZZ");
        Assert.That(await model.ReloadItemAsync(5), Is.True, "the row still exists: a new query puts it in its place");
        List<int> ids = AllIdsOf(model);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Unique.And.Count.EqualTo(303), "every row, exactly once");
            Assert.That(ids[^1], Is.EqualTo(editedId), "the edited row in its new place");
        });
    }

    [Test]
    public async Task EditThatKeepsARowAmongTheRowsReadStaysInPlaceTest()
    {
        var model = NewPagedCognomeModel(dbCtx => dbCtx.MGAAdesione);
        model.SetSortedColumn("Cognome", true);
        int editedId = model.GetItem(5).Id;
        SetCognomeInDb(editedId, model.GetItem(5).Cognome + "x");   // still before the last row read
        Assert.That(await model.ReloadItemAsync(5), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetItem(5).Id, Is.EqualTo(editedId));
            Assert.That(AllIdsOf(model), Is.Unique.And.Count.EqualTo(303));
        });
    }

    /// <summary>
    /// The grid's own edit changes the list's item in place and posts it, so only the database can tell whether the row left the
    /// filter. On the editable model such a row stays shown where it was, pinned until the next query, and every other row
    /// still comes once.
    /// </summary>
    [Test]
    public async Task PostedEditThatTakesARowOutOfTheFilterDoesNotShiftTheNextWindowTest()
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Id));
        model.ApplyFilters([CognomeFilter()]);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        int queries = model.Queries;
        MGAAdesione edited = await PostEditAsync(model, e => e.Cognome = "Other");
        Assert.Multiple(() =>
        {
            Assert.That(model.GetItem(5).Id, Is.EqualTo(edited.Id), "still shown where it was");
            Assert.That(model.Queries, Is.EqualTo(queries), "no new query");
        });
        Assert.That(AllIdsOf(model), Is.EquivalentTo(IdsWhere(CognomeContainsCognome).Append(edited.Id)), "every matching row once, and the edited one");
    }

    /// <summary>
    /// The EF edit support is built only by EditableEFDataGridModel: a second one over the same model would keep an edit the
    /// model cannot see, and miss a move in the order at the reload. So the type is not public at all.
    /// </summary>
    [Test]
    public void EFEditSupportIsBuiltOnlyByTheEditableModelTest()
    {
        Assert.That(typeof(Glazor.EntityFrameworkCore.EditSupport<,>).IsPublic, Is.False);
        Assert.That(typeof(Glazor.Grds.Mdls.EditSupport<>).IsPublic, Is.False);
    }

    /// <summary>
    /// A posted edit of the value the database sorts on may have moved the row past the last one read, so a new query puts it in
    /// its place. The form edited the list's own item, so the change is told by the value the row had when the edit started.
    /// </summary>
    [Test]
    public async Task PostedEditOfTheSortedColumnPutsTheRowInItsPlaceTest()
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione);
        model.SetSortedColumn("Cognome", true);
        MGAAdesione edited = await PostEditAsync(model, e => e.Cognome = "ZZZ");
        Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(edited.Id), "the current row follows it");
        List<int> ids = AllIdsOf(model);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Unique.And.Count.EqualTo(303), "every row, exactly once");
            Assert.That(ids[^1], Is.EqualTo(edited.Id), "the edited row in its new place");
        });
    }

    /// <summary>
    /// The base query's own OrderBy pages the same way: an edit of the member it orders by puts the row in its place with a new
    /// query, an edit of any other member keeps it where it is with none.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task PostedEditUnderTheBaseQuerysOrderTest(bool ofTheOrderedMember)
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Cognome));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        int queries = model.Queries;
        MGAAdesione edited = await PostEditAsync(model, e => { if (ofTheOrderedMember) e.Cognome = "ZZZ"; else e.Nome = "Renamed"; });
        Assert.That(model.GetItem(5).Id, ofTheOrderedMember ? Is.Not.EqualTo(edited.Id) : Is.EqualTo(edited.Id));
        Assert.That(model.Queries, ofTheOrderedMember ? Is.GreaterThan(queries) : Is.EqualTo(queries));
        List<int> ids = AllIdsOf(model);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Unique.And.Count.EqualTo(303), "every row, exactly once");
            Assert.That(ids.IndexOf(edited.Id), Is.EqualTo(ofTheOrderedMember ? 302 : 5));
        });
    }

    /// <summary>
    /// A host that closes the base query over page state applies a new one with ForceReload, and the order may change with it:
    /// a reloaded row is compared with the order the base query has now.
    /// </summary>
    [Test]
    public async Task PostedEditUnderAnOrderTheHostSwitchedTest()
    {
        bool byCognome = false;
        var model = NewEditableCognomeModel(dbCtx => byCognome ? dbCtx.MGAAdesione.OrderBy(a => a.Cognome) : dbCtx.MGAAdesione.OrderBy(a => a.NumeroAdesione));
        await PostEditAsync(model, e => e.Nome = "Renamed");   // compared under the first order
        byCognome = true;
        model.ForceReload(true);
        MGAAdesione edited = await PostEditAsync(model, e => e.Cognome = "ZZZ");
        List<int> ids = AllIdsOf(model);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Unique.And.Count.EqualTo(303), "every row, exactly once");
            Assert.That(ids[^1], Is.EqualTo(edited.Id), "the edited row in its new place");
        });
    }

    /// <summary>
    /// An order through a navigation reads values the row may not have loaded, so the model cannot tell what an edit moved: every
    /// posted edit puts the row in its place with a new query.
    /// </summary>
    [Test]
    public async Task PostedEditUnderAnOrderThroughANavigationReloadsTest()
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Titolare!.Cognome));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100), "one window loaded");
        int queries = model.Queries;
        await PostEditAsync(model, e => e.Nome = "Renamed");
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100));
        Assert.That(model.Queries, Is.GreaterThan(queries), "a new query");
        Assert.That(AllIdsOf(model), Is.Unique.And.Count.EqualTo(303));
    }

    /// <summary>
    /// A new row the filter excludes stays shown where it was inserted, pinned until the next query. It was never among the rows
    /// read, so paging goes on unchanged.
    /// </summary>
    [Test]
    public async Task InsertedRowOutsideTheFilterStaysWhereItWasInsertedTest()
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Id));
        model.ApplyFilters([CognomeFilter()]);
        model.SetCurrentRow(3);
        MGAAdesione inserted = await model.InsertAsync(new MGAAdesione
        {
            NumeroPolizza = "0002", NumeroAdesione = 9, Cognome = "New", Nome = "Newcomer", DataNascita = new DateOnly(1980, 1, 1), Legame = MGAAdesione.TipoLegame.Titolare,
        }, afterCurrentRow: true);
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            dbCtx.Add(inserted);   // the host saves the form, and the stored key lands on the item
            dbCtx.SaveChanges();
        }
        await model.PostAsync();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetItem(4).Id, Is.EqualTo(inserted.Id), "where it was inserted");
            Assert.That(model.GetCurrentRow(), Is.EqualTo(4));
        });
        Assert.That(AllIdsOf(model), Is.EquivalentTo(IdsWhere(CognomeContainsCognome).Append(inserted.Id)));
    }

    /// <summary>
    /// A pinned row edited back into the filter is among the database's rows again, maybe among those read, so a new query puts
    /// it in its place and it comes once.
    /// </summary>
    [Test]
    public async Task PinnedRowEditedBackIntoTheFilterComesOnceTest()
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Id));
        model.ApplyFilters([CognomeFilter()]);
        string cognome = model.GetItem(5).Cognome;
        await PostEditAsync(model, e => e.Cognome = "Other");
        await PostEditAsync(model, e => e.Cognome = cognome);
        Assert.That(AllIdsOf(model), Is.EquivalentTo(IdsWhere(CognomeContainsCognome)));
    }

    /// <summary>
    /// A pinned row is no longer among the rows read, so deleting it moves nothing in the database.
    /// </summary>
    [Test]
    public async Task PinnedRowDeletedLeavesTheOffsetAloneTest()
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Id));
        model.ApplyFilters([CognomeFilter()]);
        MGAAdesione edited = await PostEditAsync(model, e => e.Cognome = "Other");
        DeleteFromDb(edited.Id);
        Assert.That(await model.ReloadItemAsync(5), Is.False, "gone");
        Assert.That(AllIdsOf(model), Is.EquivalentTo(IdsWhere(CognomeContainsCognome)));
    }

    private sealed class ReloadFailingModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        public bool FailReload;

        protected override ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
            => FailReload && phase == RefineItemPhase.SingleItemReload
                ? throw new InvalidOperationException("the database is gone")
                : ValueTask.FromResult(item);
    }

    /// <summary>
    /// A pinned row whose next reload fails is still not among the rows read: the edit ends, and deleting the row later moves
    /// nothing in the database.
    /// </summary>
    [Test]
    public async Task PinnedRowWhoseReloadFailsStaysOutOfTheOffsetTest()
    {
        SeedRows(300);
        var model = new ReloadFailingModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, _) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.ApplyFilters([CognomeFilter()]);
        MGAAdesione edited = await PostEditAsync(model, e => e.Cognome = "Other");

        model.FailReload = true;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await PostEditAsync(model, e => e.Nome = "Again"));
        model.FailReload = false;
        DeleteFromDb(edited.Id);
        Assert.That(await model.ReloadItemAsync(5), Is.False, "gone");
        Assert.That(AllIdsOf(model), Is.EquivalentTo(IdsWhere(CognomeContainsCognome)));
    }

    // A change of the host's at the moment a row is read again, as a click handled while the query awaits
    private sealed class OvertakenReloadModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        public Func<Task>? DuringReload;

        protected override async ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            if (phase == RefineItemPhase.SingleItemReload && DuringReload is { } change)
            {
                DuringReload = null;
                await change();
            }
            return item;
        }
    }

    private sealed class OvertakenEditableReloadModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        public Func<Task>? DuringReload;

        protected override async ValueTask<MGAAdesione> RefineItemAsync(MGADbContext dbContext, MGAAdesione item, RefineItemPhase phase, CancellationToken ct)
        {
            if (phase == RefineItemPhase.SingleItemReload && DuringReload is { } change)
            {
                DuringReload = null;
                await change();
            }
            return item;
        }
    }

    /// <summary>
    /// A filter applied, and its rows read, while a row is read again: the reload ends with OperationCanceledException and
    /// leaves the new list alone, each row the filter admits once, on the model and on the editable model.
    /// </summary>
    [Test]
    public async Task ReloadOvertakenByAFilterLeavesTheNewListAloneTest()
    {
        SeedRows(300);
        var model = new OvertakenReloadModel
        {
            DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, _) => dbCtx.MGAAdesione.OrderBy(a => a.Id), ItemKeyExpr = a => a.Id, MinRowsToRead = 100,
        };
        var editable = new OvertakenEditableReloadModel
        {
            DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, _) => dbCtx.MGAAdesione.OrderBy(a => a.Id), ItemKeyExpr = a => a.Id, MinRowsToRead = 100,
        };
        foreach (EFDataGridModel<MGADbContext, MGAAdesione> m in new EFDataGridModel<MGADbContext, MGAAdesione>[] { model, editable })
        {
            m.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
            await m.FetchRowsAsync(0, 10);
        }
        // the filter leaves out the first rows, so index 5 holds another row once it is applied
        model.DuringReload = async () =>
        {
            model.ApplyFilters([CognomeFilter()]);
            await model.FetchRowsAsync(0, 10);
        };
        editable.DuringReload = async () =>
        {
            editable.ApplyFilters([CognomeFilter()]);
            await editable.FetchRowsAsync(0, 10);
        };

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<OperationCanceledException>(async () => await model.ReloadItemAsync(5), "model");
            Assert.ThrowsAsync<OperationCanceledException>(async () => await editable.ReloadItemAsync(5), "editable");
        });
        Assert.Multiple(() =>
        {
            Assert.That(AllIdsOf(model), Is.EquivalentTo(IdsWhere(CognomeContainsCognome)), "model");
            Assert.That(AllIdsOf(editable), Is.EquivalentTo(IdsWhere(CognomeContainsCognome)), "editable");
        });
    }

    /// <summary>
    /// The next query ends the pin: without the filter the same row is an ordinary row among those read, and deleting it moves
    /// the rows after it.
    /// </summary>
    [Test]
    public async Task PinLastsUntilTheNextQueryTest()
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione.OrderBy(a => a.Id));
        model.ApplyFilters([CognomeFilter()]);
        MGAAdesione edited = await PostEditAsync(model, e => e.Cognome = "Other");
        model.ApplyFilters([]);
        int row = Enumerable.Range(0, model.GetCurrentRowCount()).First(i => model.GetItem(i).Id == edited.Id);
        DeleteFromDb(edited.Id);
        Assert.That(await model.ReloadItemAsync(row), Is.False, "gone");
        Assert.That(AllIdsOf(model), Is.EquivalentTo(IdsWhere(_ => true)));
    }

    /// <summary>
    /// A filter on a column without EFProperty is applied in memory, so the database still counts the rows it excludes. A row the
    /// edit takes out of it stays shown, pinned; when the edit also moves it in the order, the new query shows it in its place.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task PostedEditOutOfAFilterAppliedInMemoryStaysShownTest(bool alsoMoved)
    {
        var model = NewEditableCognomeModel(dbCtx => dbCtx.MGAAdesione);
        model.AddColumn("NomeInMemory", new Column<MGAAdesione, string>("NomeInMemory"), a => a.Nome);
        model.SetSortedColumn("Cognome", true);
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("NomeInMemory", new DefaultDef() { Value = "Nome", Contains = true })]);
        MGAAdesione edited = await PostEditAsync(model, e => { e.Nome = "Other"; if (alsoMoved) e.Cognome = "ZZZ"; });
        int row = model.RowIndexOf(model.GetCurrentItem()!);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetItem(row).Id, Is.EqualTo(edited.Id), "the current row follows it");
        });
        List<int> ids = AllIdsOf(model);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.EquivalentTo(IdsWhere(a => a.Nome.Contains("Nome", StringComparison.OrdinalIgnoreCase)).Append(edited.Id)));
            Assert.That(ids.IndexOf(edited.Id), Is.EqualTo(alsoMoved ? ids.Count - 1 : 5));
        });
    }

    /// <summary>
    /// On the plain model, a reloaded row that moved in the order and that a filter applied in memory now excludes would not come
    /// back with the new query: it leaves the list first, so the current row goes to the next one and nothing is read looking
    /// for it.
    /// </summary>
    [Test]
    public async Task MovedRowOutOfAFilterAppliedInMemoryLeavesTheListTest()
    {
        SeedRows(300);
        QueryCapturingModel model = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione, ItemKeyExpr = a => a.Id, MinRowsToRead = 20 };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMemory", new Column<MGAAdesione, string>("NomeInMemory"), a => a.Nome);
        model.SetSortedColumn("Cognome", true);
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("NomeInMemory", new DefaultDef() { Value = "Nome", Contains = true })]);
        model.SetCurrentRow(5);
        int editedId = model.GetItem(5).Id;
        int nextId = model.GetItem(6).Id;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            MGAAdesione stored = dbCtx.MGAAdesione.Single(a => a.Id == editedId);
            stored.Cognome = "ZZZ";
            stored.Nome = "Other";
            dbCtx.SaveChanges();
        }
        bool stillThere = await model.ReloadItemAsync(5);
        model.Queries = 0;
        Assert.Multiple(() =>
        {
            Assert.That(stillThere, Is.False, "the row left the list");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(nextId), "the current row goes to the next one");
            Assert.That(model.AllDataRead(), Is.False, "nothing read looking for the row");
            Assert.That(model.Queries, Is.EqualTo(1), "the rows read, again");
        });
    }

    /// <summary>
    /// The same when the row is the last one read, with more rows in the table: the current row goes to the next one, which
    /// was not read yet, not back to the one before.
    /// </summary>
    [Test]
    public async Task MovedLastReadRowOutOfAFilterAppliedInMemoryGoesToTheNextRowTest()
    {
        SeedRows(300);
        QueryCapturingModel model = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione, ItemKeyExpr = a => a.Id, MinRowsToRead = 20 };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMemory", new Column<MGAAdesione, string>("NomeInMemory"), a => a.Nome);
        model.SetSortedColumn("Cognome", true);
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("NomeInMemory", new DefaultDef() { Value = "Nome", Contains = true })]);
        int last = model.GetCurrentRowCount() - 1;
        model.SetCurrentRow(last);
        int editedId = model.GetItem(last).Id;
        int nextId;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            nextId = dbCtx.MGAAdesione.OrderBy(a => a.Cognome).ThenBy(a => a.Id).Skip(last + 1).First().Id;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            MGAAdesione stored = dbCtx.MGAAdesione.Single(a => a.Id == editedId);
            stored.Cognome = "ZZZ";
            stored.Nome = "Other";
            dbCtx.SaveChanges();
        }
        Assert.That(model.AllDataRead(), Is.False, "more rows to read");
        bool stillThere = await model.ReloadItemAsync(last);
        Assert.Multiple(() =>
        {
            Assert.That(stillThere, Is.False, "the row left the list");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(nextId), "the current row goes to the next one");
        });
    }

    /// <summary>
    /// A row deleted when it is the last one read, with more rows in the table, takes the current row to the next one, which
    /// was not read yet, as a row deleted anywhere else in the list does.
    /// </summary>
    [Test]
    public async Task DeletedLastReadRowGoesToTheNextRowTest()
    {
        SeedRows(300);
        QueryCapturingModel model = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id), ItemKeyExpr = a => a.Id, MinRowsToRead = 20 };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        int last = model.GetCurrentRowCount() - 1;
        model.SetCurrentRow(last);
        int deletedId = model.GetItem(last).Id;
        int nextId;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            nextId = dbCtx.MGAAdesione.OrderBy(a => a.Id).Skip(last + 1).First().Id;
            dbCtx.MGAAdesione.Remove(dbCtx.MGAAdesione.Single(a => a.Id == deletedId));
            dbCtx.SaveChanges();
        }
        Assert.That(model.AllDataRead(), Is.False, "more rows to read");
        bool stillThere = await model.ReloadItemAsync(last);
        Assert.Multiple(() =>
        {
            Assert.That(stillThere, Is.False, "the row left the list");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(nextId), "the current row goes to the next one");
            Assert.That(model.GetCurrentRow(), Is.EqualTo(last), "in the deleted row's place");
        });
    }

    /// <summary>
    /// When everything is already read there is nothing left to walk, so a current key the reload did not return needs no
    /// existence query: the reload is the only query.
    /// </summary>
    [Test]
    public void KeyRestoreOnAFullyReadModelQueriesOnceTest()
    {
        QueryCapturingModel model = new() { DbContextFactory = _dbCtxFactory, BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id), ItemKeyExpr = a => a.Id };
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome"), a => a.Nome);
        Assert.That(model.GetRowCount(), Is.EqualTo(3), "the table fits in one window");
        model.SetCurrentRow(0);
        Assert.That(model.GetCurrentItem()!.Nome, Is.EqualTo("Vallers"));
        model.Queries = 0;
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>("Nome", new DefaultDef() { Value = "Erlumo", Contains = true })]);
        Assert.That(model.GetRowCount(), Is.EqualTo(2), "the current row is filtered out");
        Assert.That(model.Queries, Is.EqualTo(1));
    }

    /// <summary>
    /// A filter applied in memory does not stop the existence query: the database still answers for the filters it applies, and
    /// a current row they exclude is not looked for through the rest of the table.
    /// </summary>
    [Test]
    public void KeyRestoreUnderAFilterAppliedInMemoryAsksTheDatabaseTest()
    {
        SeedRows(300);
        QueryCapturingModel model = new()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.AddColumn("NomeInMemory", new Column<MGAAdesione, string>("Nome"), a => a.Nome);   // not pushed down
        KeyValuePair<string, AFilterDef> inMemory = new("NomeInMemory", new DefaultDef { Value = "Nome", Contains = true });
        model.ApplyFilters([inMemory]);
        model.GetCurrentRowCount();
        model.SetCurrentRow(5);
        SetCognomeInDb(model.GetCurrentItem()!.Id, "Other");
        model.Queries = 0;
        model.ApplyFilters([inMemory, CognomeFilter()]);
        model.GetCurrentRowCount();
        Assert.Multiple(() =>
        {
            Assert.That(model.AllDataRead(), Is.False, "nothing read looking for the row");
            Assert.That(model.Queries, Is.EqualTo(2), "the rows read again, and the existence query");
        });
    }
    /// <summary>
    /// With an insert pending after the last row read, the next row of the table comes one place further down: it is fetched,
    /// not taken for the pending row, and read by value it is the same row.
    /// </summary>
    [Test]
    public async Task RowAfterAPendingInsertPastTheRowsReadIsFetchedTest()
    {
        SeedRows(300);
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        int read = model.GetCurrentRowCount();
        int nextId;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            nextId = dbCtx.MGAAdesione.OrderBy(a => a.Id).Skip(read).First().Id;
        model.SetCurrentRow(read - 1);
        await model.InsertAsync(new MGAAdesione
        {
            NumeroPolizza = "0002", NumeroAdesione = 9, Cognome = "New", Nome = "Newcomer", DataNascita = new DateOnly(1980, 1, 1), Legame = MGAAdesione.TipoLegame.Titolare,
        }, afterCurrentRow: true);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetItem(read + 1).Id, Is.EqualTo(nextId));
            Assert.That(model.GetValueAt(read + 1, 0), Is.EqualTo(model.GetItem(read + 1).Cognome));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(read), "the row being inserted is the current one");
        });
    }
    /// <summary>
    /// The form edits the list's own item, key members included: cancelling must reload the row the edit started from, not
    /// the one the typed key names, nor lose it and the paging offset with it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellingAnEditOfAKeyMemberReloadsTheSameRowTest(bool keyOfAnotherRow)
    {
        SeedRows(50);
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => new { a.NumeroPolizza, a.NumeroAdesione },
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetCurrentRow(3);
        List<int> before = Enumerable.Range(0, 20).Select(i => model.GetItem(i).Id).ToList();
        MGAAdesione other = model.GetItem(5);
        MGAAdesione edited = await model.EditAsync();
        if (keyOfAnotherRow)
            (edited.NumeroPolizza, edited.NumeroAdesione) = (other.NumeroPolizza, other.NumeroAdesione);
        else
            edited.NumeroAdesione = 12345;
        await model.CancelAsync();
        Assert.That(Enumerable.Range(0, 20).Select(i => model.GetItem(i).Id), Is.EqualTo(before));
    }

    private sealed class ReloadCountingModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        public int Reloads;

        public override ValueTask<bool> ReloadItemAsync(int rowIndex, CancellationToken ct = default)
        {
            Reloads++;
            return base.ReloadItemAsync(rowIndex, ct);
        }
    }

    /// <summary>
    /// A subclass hooks row reloads by overriding ReloadItemAsync, and a cancel that changed no key member must go through it.
    /// </summary>
    [Test]
    public async Task CancelReloadsThroughAnOverrideOfReloadItemAsyncTest()
    {
        SeedRows(20);
        var model = new ReloadCountingModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetCurrentRow(3);
        string cognome = model.GetItem(3).Cognome;
        MGAAdesione edited = await model.EditAsync();
        edited.Cognome = "changed";
        await model.CancelAsync();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetItem(3).Cognome, Is.EqualTo(cognome), "the row is read again");
            Assert.That(model.Reloads, Is.EqualTo(1));
        });
    }

    private sealed class KeyedReloadCountingModel : EditableEFDataGridModel<MGADbContext, MGAAdesione>
    {
        public readonly List<object> Keys = new();

        protected internal override ValueTask<bool> ReloadItemAsync(int rowIndex, object pk, CancellationToken ct = default)
        {
            Keys.Add(pk);
            return base.ReloadItemAsync(rowIndex, pk, ct);
        }
    }

    /// <summary>
    /// A cancel that restores a changed key member reloads the row by the key it had when the edit started, which the row's
    /// own key can no longer find; the overload taking the key sees that reload, and every other one.
    /// </summary>
    [Test]
    public async Task EveryReloadGoesThroughTheOverloadTakingTheKeyTest()
    {
        SeedRows(20);
        var model = new KeyedReloadCountingModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetCurrentRow(3);
        int id = model.GetItem(3).Id;
        MGAAdesione edited = await model.EditAsync();
        edited.Id = 12345;
        await model.CancelAsync();
        Assert.That(await model.ReloadItemAsync(3), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetItem(3).Id, Is.EqualTo(id), "the row is read again by its original key");
            Assert.That(model.Keys, Is.EqualTo(new object[] { id, id }), "the cancel's reload, then the host's");
        });
    }

    /// <summary>
    /// The token given to a reload, a post or a cancel cancels the query that reads the row again; the edit ends all the same.
    /// </summary>
    [TestCase("reload")]
    [TestCase("post")]
    [TestCase("cancel")]
    public async Task ACancelledTokenStopsTheRowReloadTest(string action)
    {
        SeedRows(20);
        var model = new EditableEFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetCurrentRow(3);
        var cancelled = new CancellationToken(true);
        if (action == "reload")
        {
            Assert.That(async () => await model.ReloadItemAsync(3, cancelled), Throws.InstanceOf<OperationCanceledException>());
            return;
        }
        await model.EditAsync();
        if (action == "post")
            Assert.That(async () => await model.PostAsync(cancelled), Throws.InstanceOf<OperationCanceledException>());
        else
            Assert.That(async () => await model.CancelAsync(cancelled), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(model.IsEditPending(), Is.False, "no edit pending");
    }

    private class PlainKey(string NumeroPolizza, uint NumeroAdesione)
    {
        public string NumeroPolizza { get; } = NumeroPolizza;
        public uint NumeroAdesione { get; } = NumeroAdesione;
    }

    private class PlainInitKey
    {
        public string NumeroPolizza { get; init; } = "";
        public uint NumeroAdesione { get; init; }
    }

    /// <summary>
    /// A composite key is compared as a whole to find the current row again, so a class key without value equality would page
    /// and sort and then silently lose the current row: it is refused on first use, with a message that says why.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void CompositeKeyWithoutValueEqualityIsRefusedTest(bool initializer)
    {
        SeedRows(10);
        Expression<Func<MGAAdesione, object>> key = a => new PlainKey(a.NumeroPolizza, a.NumeroAdesione);
        if (initializer)
            key = a => new PlainInitKey { NumeroPolizza = a.NumeroPolizza, NumeroAdesione = a.NumeroAdesione };
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = key,
            MinRowsToRead = 5,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        var e = Assert.Catch(() => model.GetRowCount());
        Assert.That(e!.GetBaseException(), Is.TypeOf<InvalidOperationException>().With.Message.Contains(initializer ? nameof(PlainInitKey) : nameof(PlainKey)));
    }
    /// <summary>
    /// DefaultDef matches a column of any type on its text: pushed down it has the database convert the value, in memory it
    /// calls ToString(), and both find the same rows, a nullable column's nulls kept or dropped as asked.
    /// </summary>
    [TestCase("Id", true)]
    [TestCase("TitolareId", true)]
    [TestCase("TitolareId", false)]
    public void DefaultDefMatchesANonStringColumnOnItsTextTest(string column, bool includeNull)
    {
        SeedRows(100);
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
        {
            foreach (var a in dbCtx.MGAAdesione.Where(a => a.Id % 4 == 0))
                (a.Legame, a.TitolareId) = (MGAAdesione.TipoLegame.Coniuge, a.Id - 1);   // the row before stays a titolare
            dbCtx.SaveChanges();
        }
        QueryCapturingModel model = new()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 20,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        model.AddEFColumn("TitolareId", new Column<MGAAdesione, int?>("TitolareId"), a => a.TitolareId);
        DefaultDef def = new() { Value = "2", Contains = true, IncludeNull = includeNull };
        List<int> expected;
        using (var dbCtx = _dbCtxFactory.CreateDbContext())
            expected = dbCtx.MGAAdesione.AsEnumerable().Where(a => def.Includes(column == "Id" ? a.Id : a.TitolareId)).Select(a => a.Id).OrderBy(i => i).ToList();
        model.ApplyFilters([new KeyValuePair<string, AFilterDef>(column, def)]);
        Assert.Multiple(() =>
        {
            Assert.That(ReadAllIds(model).OrderBy(i => i), Is.EqualTo(expected), "pushed down, the same rows as in memory");
            Assert.That(model.LastSql, Does.Contain("CAST"), "the text is the database's");
            Assert.That(expected, Is.Not.Empty);
        });
    }

    /// <summary>
    /// A fetch and a search on the sorted column observe their token: cancelled, they end with an OperationCanceledException
    /// and the model answers the next call.
    /// </summary>
    [Test]
    public async Task FetchAndSearchObserveTheirTokenTest()
    {
        SeedRows(50);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione,
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        model.SetSortedColumn("Cognome", true);
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await ((Glazor.Grds.Mdls.IGridModel<MGAAdesione>)model).FetchRowsAsync(0, 10, cts.Token), "fetch");
        Assert.CatchAsync<OperationCanceledException>(async () => await model.SearchOnSortedColumnAsync("Cognome000040", cts.Token), "search");
        Assert.That(await ((Glazor.Grds.Mdls.IGridModel<MGAAdesione>)model).FetchRowsAsync(0, 10, CancellationToken.None), Is.EqualTo(10), "usable after");
        Assert.That(await model.SearchOnSortedColumnAsync("Cognome000040", CancellationToken.None), Is.EqualTo(40), "the seeded rows sort before the fixture's");
    }

    /// <summary>
    /// A row out of range is one exception on a paged model, <see cref="ArgumentOutOfRangeException"/> as on an in-memory one,
    /// whether the table end is still unknown or every row has been read; the cell renderer catches it for the rows a page draws
    /// past the end.
    /// </summary>
    [Test]
    public void RowOutOfRangeIsOneExceptionBeforeAndAfterTheTableEndIsKnownTest()
    {
        SeedRows(30);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one window read, the end unknown");
        Assert.Throws<ArgumentOutOfRangeException>(() => model.GetItem(500), "past the table, found out by the fetch");
        Assert.That(model.AllDataRead(), Is.True, "and that fetch reached the end");
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => model.GetItem(33), "past the known end");
            Assert.Throws<ArgumentOutOfRangeException>(() => model.GetValueAt(33, 0));
            Assert.That(model.GetItem(32).Id, Is.EqualTo(model.GetItem(31).Id + 1), "the last row still reads, in the Id order the query sets");
        });
    }

    /// <summary>
    /// A fetch past the end gives the rows found there, none for a window that starts past it, before the end is known and after.
    /// </summary>
    [Test]
    public async Task FetchPastTheEndGivesTheRowsFoundTest()
    {
        SeedRows(30);
        var model = new EFDataGridModel<MGADbContext, MGAAdesione>()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Id", new Column<MGAAdesione, int>("Id"), a => a.Id);
        Assert.That(model.GetRowCount(), Is.EqualTo(int.MaxValue), "one window read, the end unknown");
        Assert.That(await model.FetchRowsAsync(40, 5), Is.Zero, "past the end, found by this fetch");
        Assert.That(model.AllDataRead(), Is.True);
        Assert.That(await model.FetchRowsAsync(40, 5), Is.Zero, "past the known end");
        Assert.That(await model.FetchRowsAsync(20, 100), Is.EqualTo(model.GetRowCount() - 20), "the rows there are");
    }

    private sealed class QueryCountingModel : EFDataGridModel<MGADbContext, MGAAdesione>
    {
        public int Queries;

        protected override IQueryable<MGAAdesione> CompleteQueryable(MGADbContext dbContext, IQueryable<MGAAdesione> query, ReadRequest request)
        {
            Queries++;
            return base.CompleteQueryable(dbContext, query, request);
        }
    }

    /// <summary>
    /// Clearing the sort on a model that has none is no change, whatever direction flag comes with the null: on a paged model a
    /// change is a full reload, so the flag alone must not force one.
    /// </summary>
    [Test]
    public void ClearingTheSortOnAnUnsortedPagedModelDoesNotReloadTest()
    {
        SeedRows(30);
        var model = new QueryCountingModel()
        {
            DbContextFactory = _dbCtxFactory,
            BaseQueryFn = (dbCtx, en) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 10,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10));
        int queries = model.Queries;
        model.SetSortedColumn(null, false);
        model.SetSortedColumn(null, true);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "the rows read are still there");
            Assert.That(model.Queries, Is.EqualTo(queries), "nothing to re-query");
        });
        model.SetSortedColumn("Cognome", true);
        model.GetCurrentRowCount();
        Assert.That(model.Queries, Is.GreaterThan(queries), "a real sort re-queries");
        queries = model.Queries;
        model.SetSortedColumn("Cognome", true);
        model.GetCurrentRowCount();
        Assert.That(model.Queries, Is.EqualTo(queries), "the same sort again is no change");
        model.SetSortedColumn(null, false);
        model.GetCurrentRowCount();
        Assert.That(model.Queries, Is.GreaterThan(queries), "clearing a sort is a change");
    }
}
