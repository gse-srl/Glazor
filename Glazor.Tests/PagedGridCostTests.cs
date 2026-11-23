using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.EntityFrameworkCore;
using Glazor.Misc;
using Glazor.Tests.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Linq.Expressions;

namespace Glazor.Tests;

/// <summary>
/// Measures what a paged <see cref="EFDataGridModel{TDbContext, TGridItem}"/> actually costs the render
/// thread, rather than reasoning about it. Both the db fetch and the chunk swap announce themselves through
/// <see cref="LoggerSupport"/>, so a counting factory can attribute every one to the interaction that caused
/// it: an initial render, then paging forward and back over data already read.
/// </summary>
public class PagedGridCostTests
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<MGADbContext> _options = null!;

    private const int RowCount = 5_000;
    private const int PageSize = 20;

    /// <summary>
    /// Counts the library's own debug lines. Each is emitted once per real event: a db round trip, a chunk
    /// read back from disk, a chunk written out.
    /// </summary>
    private sealed class Counter : ILogger, ILoggerProvider, ILoggerFactory
    {
        public int DbFetches, ChunkReads, ChunkWrites;

        public void Reset() => DbFetches = ChunkReads = ChunkWrites = 0;
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Dispose() { }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            if (message.StartsWith("Fetched:")) DbFetches++;
            else if (message.StartsWith("Retrieving a chunk")) ChunkReads++;
            else if (message.StartsWith("Writing a chunk")) ChunkWrites++;
        }
    }

    [SetUp]
    public void Setup()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<MGADbContext>().UseSqlite(_connection).Options;
        using MGADbContext dbCtx = new(_options);
        dbCtx.Database.EnsureCreated();
        for (int i = 0; i < RowCount; i++)
        {
            dbCtx.MGAAdesione.Add(new MGAAdesione
            {
                NumeroPolizza = $"P{i:D6}",
                NumeroAdesione = (uint)i,
                Cognome = $"Cognome{i:D6}",
                Nome = $"Nome{i:D6}",
                DataNascita = new DateOnly(1980, 1, 1),
                // Titolare is the only Legame allowed to have no Titolare reference (see MGAAdesione.Check).
                Legame = MGAAdesione.TipoLegame.Titolare,
            });
        }
        dbCtx.SaveChanges();
    }

    [TearDown]
    public void TearDown() => _connection?.Dispose();

    private EFDataGridModel<MGADbContext, MGAAdesione> NewModel()
    {
        EFDataGridModel<MGADbContext, MGAAdesione> model = new()
        {
            DbContextFactory = new MockMGADbContextFactory(_options),
            BaseQueryFn = (dbCtx, _) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome") { PreferredSize = 20 }, a => a.Cognome);
        model.AddEFColumn("Nome", new Column<MGAAdesione, string>("Nome") { PreferredSize = 20 }, a => a.Nome);
        return model;
    }

    /// <summary>
    /// The question this answers: is the render thread paying repeatedly, or once per user action?<br/>
    /// A page step of 20 rows against MinRowsToRead of 100 means four steps are served from rows already
    /// read, and only the fifth needs the database. Re-rendering without touching the grid must cost nothing
    /// at all — that is the difference between "slow while you use it" and "slow all the time".
    /// </summary>
    [Test]
    public void PagingCostsTheDbOnlyWhenItRunsPastWhatWasRead()
    {
        Counter counter = new();
        LoggerSupport.SetLoggerFactory(counter);

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();

        EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();

        counter.Reset();
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.PageSize, PageSize)
            .Add(g => g.Spinner, new NoSpinner()));
        int fetchesOnFirstPaint = counter.DbFetches;

        NUnit.Framework.TestContext.Out.WriteLine($"first paint            : db={counter.DbFetches} chunkReads={counter.ChunkReads} chunkWrites={counter.ChunkWrites}");
        Assert.That(fetchesOnFirstPaint, Is.GreaterThan(0), "the first paint has to read something");

        // Re-render with nothing changed: the data window is untouched, so this must be free.
        counter.Reset();
        cut.Render();
        NUnit.Framework.TestContext.Out.WriteLine($"idle re-render         : db={counter.DbFetches} chunkReads={counter.ChunkReads}");
        Assert.That(counter.DbFetches, Is.Zero, "re-rendering without acting on the grid must not hit the db");

        // Three page steps, each rendered, stay inside the 100 rows already read; a fourth would touch the window's edge, where
        // the fetch contract looks one row ahead.
        counter.Reset();
        for (int step = 0; step < 3; step++)
        {
            cut.InvokeAsync(() => model.SetCurrentRow(model.GetCurrentRow() + PageSize));
            cut.Render();
        }
        int fetchesWithinReadData = counter.DbFetches;
        NUnit.Framework.TestContext.Out.WriteLine($"3 pages within 100 rows: db={counter.DbFetches} chunkReads={counter.ChunkReads}");

        // Then walk far past it.
        counter.Reset();
        for (int step = 0; step < 20; step++)
        {
            cut.InvokeAsync(() => model.SetCurrentRow(model.GetCurrentRow() + PageSize));
            cut.Render();
        }
        NUnit.Framework.TestContext.Out.WriteLine($"20 pages beyond        : db={counter.DbFetches} chunkReads={counter.ChunkReads}");

        Assert.Multiple(() =>
        {
            Assert.That(fetchesWithinReadData, Is.Zero, "paging inside data already read costs no fetch");
            Assert.That(counter.DbFetches, Is.GreaterThan(0),
                "and paging past it does reach the db, so the cost is tied to the user's action");
        });
    }

    /// <summary>
    /// Paging back over rows already read must not re-query. If it does, the cost is not "once per new
    /// window" but "every time the user moves", which is the difference that decides how much the blocking
    /// wait in <c>SpinnerHelper.Monitor</c> actually matters.
    /// </summary>
    [Test]
    public void PagingBackOverDataAlreadyReadDoesNotHitTheDb()
    {
        Counter counter = new();
        LoggerSupport.SetLoggerFactory(counter);

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();

        EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.PageSize, PageSize)
            .Add(g => g.Spinner, new NoSpinner()));

        // Walk forward far enough to have read a good deal.
        for (int step = 0; step < 30; step++)
        {
            cut.InvokeAsync(() => model.SetCurrentRow(model.GetCurrentRow() + PageSize));
            cut.Render();
        }
        int reachedRow = model.GetCurrentRow();

        // Now walk all the way back over rows that are certainly already read.
        counter.Reset();
        for (int step = 0; step < 30; step++)
        {
            cut.InvokeAsync(() => model.SetCurrentRow(Math.Max(0, model.GetCurrentRow() - PageSize)));
            cut.Render();
        }
        NUnit.Framework.TestContext.Out.WriteLine($"walked forward to row {reachedRow}, then back to {model.GetCurrentRow()}");
        NUnit.Framework.TestContext.Out.WriteLine($"walking back           : db={counter.DbFetches} chunkReads={counter.ChunkReads}");

        Assert.That(counter.DbFetches, Is.Zero,
            "revisiting rows already read must not re-query the database");
    }

    /// <summary>
    /// PageDown past the rows read reads on to the row it moves to, and the page shown holds it.
    /// </summary>
    [Test]
    public async Task PageDownPastTheRowsReadReachesItsRowTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();

        EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();
        var cut = ctx.Render<Grigliaria<MGAAdesione>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.PageSize, PageSize)
            .Add(g => g.Spinner, new NoSpinner()));
        KeyAction pageDown = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == "PageDown").Instance;
        int expected = model.GetCurrentRow();
        for (int step = 0; step < 6; step++)
        {
            await cut.InvokeAsync(() => pageDown.ProcessAsync("PageDown", null, null));
            expected = Math.Max(0, expected + PageSize);
        }

        int row = model.GetCurrentRow(), first = cut.Instance.GetPageIdx();
        Assert.Multiple(() =>
        {
            Assert.That(row, Is.EqualTo(expected).And.GreaterThan(100), "past the 100 rows read first");
            Assert.That(model.GetItem(row).Cognome, Is.EqualTo($"Cognome{row:D6}"));
            Assert.That(row, Is.InRange(first, first + PageSize - 1), "the page shown holds it");
        });
    }

    /// <summary>
    /// A backward contains-search miss from a row read must be final: everything before startFrom is already loaded, so no
    /// fetch may happen.
    /// </summary>
    [Test]
    public async Task BackwardContainsSearchMissDoesNotFetchTest()
    {
        using EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();
        _ = model.GetRowCount();   // first fetch; a paged model reports an unknown total until all rows are read
        int loaded = model.GetCurrentRowCount();
        Assert.That(loaded, Is.EqualTo(100));
        Assert.That(await model.ContainsSearchAsync("Cognome", "no-such-value", forward: false, startFrom: loaded - 1), Is.EqualTo(-1));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(loaded), "a backward miss must not fetch");
        Assert.That(await model.ContainsSearchAsync("Cognome", "Cognome000042", forward: false, startFrom: loaded - 1), Is.EqualTo(42));
        // Forward still walks the table.
        Assert.That(await model.ContainsSearchAsync("Cognome", "Cognome004999", forward: true, startFrom: 0), Is.EqualTo(4999));
        Assert.That(model.AllDataRead(), Is.True);
    }

    /// <summary>
    /// A miss whose insertion point lies before the last loaded row is final: no row beyond the loaded ones can change it.
    /// </summary>
    [Test]
    public async Task SortedSearchLandingBeforeTheLastLoadedRowDoesNotFetchTest()
    {
        using EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();
        model.SetSortedColumn("Nome", true);
        int loaded = model.GetCurrentRowCount();
        Assert.That(loaded, Is.EqualTo(100));
        int res = await model.SearchOnSortedColumnAsync("Nome000098a");
        Assert.Multiple(() =>
        {
            Assert.That(~res, Is.EqualTo(99), "would be inserted before the last loaded row");
            Assert.That(model.GetCurrentRowCount(), Is.EqualTo(loaded), "a final answer fetches nothing");
        });
        Assert.That(~await model.SearchOnSortedColumnAsync("Nome000099a"), Is.EqualTo(100), "past the last loaded row: more rows are read first");
        Assert.That(model.GetCurrentRowCount(), Is.GreaterThan(loaded));
    }

    /// <summary>
    /// While the total is unknown the model accepts any current row, so a page step can land past the end. Reading the
    /// current item must then find the end, clamp the row and return the last item, not throw.
    /// </summary>
    [Test]
    public void CurrentRowSetPastTheEndClampsWhenReadTest()
    {
        using EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();
        _ = model.GetRowCount();   // first window only: the total is still unknown
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(100));
        model.SetCurrentRow(RowCount + 5);
        MGAAdesione? item = model.GetCurrentItem();
        Assert.That(item, Is.Not.Null);
        Assert.That(item!.Cognome, Is.EqualTo($"Cognome{RowCount - 1:D6}"), "the last row");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(RowCount - 1));
        Assert.That(model.AllDataRead(), Is.True);
    }

    /// <summary>
    /// An in-memory filter that drops a few rows of every window must not make the first fetch read window after window: the
    /// loop has to stop once the rows added over all its passes reach what was asked, not when a single pass does. A "contains 9"
    /// filter keeps 19 rows of every hundred until the 900s, where it keeps them all: the old loop
    /// read 10 windows (271 rows) before a whole window passed; the cumulative exit stops after 6 windows (114 rows).
    /// </summary>
    [Test]
    public void InMemoryFilterDoesNotMultiplyTheFirstFetchTest()
    {
        Counter counter = new();
        LoggerSupport.SetLoggerFactory(counter);
        using EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();
        model.AddColumn("NomeInMem", new Column<MGAAdesione, string>("Nome, in memory"), a => a.Nome);   // not pushed down
        model.ApplyFilters([new("NomeInMem", new DefaultDef() { Value = "9", Contains = true })]);
        counter.Reset();
        _ = model.GetRowCount();
        NUnit.Framework.TestContext.Out.WriteLine($"in-memory filter, first fetch: db={counter.DbFetches} loaded={model.GetCurrentRowCount()}");
        Assert.That(counter.DbFetches, Is.EqualTo(6), "six windows of 19 kept rows reach the 100 requested");
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(114));
        Assert.That(model.AllDataRead(), Is.False);
    }

    /// <summary>
    /// A sort, a filter change or a reload reads the rows read so far again. Under an in-memory filter that keeps one row in ten,
    /// every pass asks for the whole count again, so the re-read takes about ten queries: asking only for the rows still missing
    /// shrank the passes one after the other, and tripled the queries.
    /// </summary>
    [Test]
    public async Task RestoreUnderASelectiveInMemoryFilterTakesFewQueriesTest()
    {
        Counter counter = new();
        LoggerSupport.SetLoggerFactory(counter);
        using EFDataGridModel<MGADbContext, MGAAdesione> model = NewModel();
        model.AddColumn("Kept", new Column<MGAAdesione, string>("Kept, in memory"), a => a.NumeroAdesione % 10 == 0 ? "yes" : "no");   // not pushed down
        model.ApplyFilters([new("Kept", new DefaultDef() { Value = "yes" })]);   // one row in ten kept
        while (model.GetCurrentRowCount() < 300)
            await model.FetchRowsAsync(model.GetCurrentRowCount(), 100);
        int loaded = model.GetCurrentRowCount();
        model.SetCurrentRow(loaded - 1);
        counter.Reset();
        model.SetSortedColumn("Cognome", true);   // pushed down, in the order already read: the rows read so far are read again
        int after = model.GetCurrentRowCount();
        NUnit.Framework.TestContext.Out.WriteLine($"selective in-memory filter, re-read of {loaded} rows: db={counter.DbFetches} loaded={after}");
        Assert.That(counter.DbFetches, Is.LessThanOrEqualTo(10), "each pass asks for the whole count");
        Assert.That(model.GetCurrentRow(), Is.EqualTo(loaded - 1), "the current row found again");
    }

    private sealed class StatementSpy : DbCommandInterceptor, IQueryExpressionInterceptor
    {
        public readonly List<string> Statements = [];
        public int Compilations;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Compilations++;
            return queryExpression;
        }
    }

    /// <summary>
    /// A single-row reload, which every post and cancel of an edit runs, looks its row up by key. The key must reach the database
    /// as a parameter: one compiled query and one statement for every row, not one of each per key.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task SingleRowReloadsShareOneQueryTest(bool compositeKey)
    {
        StatementSpy spy = new();
        using EFDataGridModel<MGADbContext, MGAAdesione> model = new()
        {
            DbContextFactory = new MockMGADbContextFactory(new DbContextOptionsBuilder<MGADbContext>().UseSqlite(_connection).AddInterceptors(spy).Options),
            BaseQueryFn = (dbCtx, _) => dbCtx.MGAAdesione.OrderBy(a => a.Id),
            ItemKeyExpr = compositeKey ? a => new { a.NumeroPolizza, a.NumeroAdesione } : a => a.Id,
            MinRowsToRead = 100,
        };
        model.AddEFColumn("Cognome", new Column<MGAAdesione, string>("Cognome"), a => a.Cognome);
        _ = model.GetRowCount();
        await model.ReloadItemAsync(0);   // the reload's query compiled once
        spy.Statements.Clear();
        spy.Compilations = 0;
        for (int i = 1; i <= 5; i++)
            await model.ReloadItemAsync(i);
        Assert.Multiple(() =>
        {
            Assert.That(spy.Statements, Has.Count.EqualTo(5));
            Assert.That(spy.Statements.Distinct().Count(), Is.EqualTo(1), "one statement for every key");
            Assert.That(spy.Compilations, Is.Zero, "no new query per key");
        });
    }
}
