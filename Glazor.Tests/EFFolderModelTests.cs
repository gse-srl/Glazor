using Bunit;
using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Data.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// <see cref="EFFolderModel{TDbContext, TGridItem}"/> over a small file system in SQLite: roots read in passes, a folder's
/// children read when it is first expanded, filters that keep the folders above a match, siblings sorted, the current row put
/// back by its path, reload and go to.
/// </summary>
public class EFFolderModelTests
{
    public sealed class Entry
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = "";
        public bool IsFolder { get; set; }
        public int Size { get; set; }
    }

    public sealed class FolderDb(DbContextOptions<FolderDb> options) : DbContext(options)
    {
        public DbSet<Entry> Entries => Set<Entry>();
    }

    private sealed class Factory(DbContextOptions<FolderDb> options) : IDbContextFactory<FolderDb>
    {
        public FolderDb CreateDbContext() => new(options);
    }

    private SqliteConnection _connection = null!;
    private Factory _factory = null!;
    private int _rootQueries, _childQueries;
    private int _childQueryMs;   // a slow children query
    private Action? _onRootQuery;

    // r1 / a / a1 apple, a2 banana; r1 / b cherry; r2 / c (empty); r3 leaf; then 25 leaf roots
    private const int R1 = 1, A = 2, A1 = 3, A2 = 4, B = 5, R2 = 6, C = 7, R3 = 8, Unfiled = 1000;

    [SetUp]
    public void Setup()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _factory = new(new DbContextOptionsBuilder<FolderDb>().UseSqlite(_connection).Options);
        using FolderDb db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Entries.AddRange(
            new Entry { Id = R1, Name = "r1", IsFolder = true, Size = 30 },
            new Entry { Id = A, ParentId = R1, Name = "a", IsFolder = true, Size = 20 },
            new Entry { Id = A1, ParentId = A, Name = "apple", Size = 1 },
            new Entry { Id = A2, ParentId = A, Name = "banana", Size = 2 },
            new Entry { Id = B, ParentId = R1, Name = "cherry", Size = 3 },
            new Entry { Id = R2, Name = "r2", IsFolder = true, Size = 10 },
            new Entry { Id = C, ParentId = R2, Name = "c", IsFolder = true, Size = 0 },
            new Entry { Id = R3, Name = "r3", Size = 5 });
        for (int i = 0; i < 25; i++)
            db.Entries.Add(new Entry { Id = 100 + i, Name = $"z{i:D2}", Size = 100 + i });
        // filed under the virtual folder, which is no row of the table
        db.Entries.AddRange(
            new Entry { Id = 50, ParentId = Unfiled, Name = "unfiled one", Size = 7 },
            new Entry { Id = 51, ParentId = Unfiled, Name = "unfiled two", Size = 8 });
        db.SaveChanges();
        _rootQueries = _childQueries = 0;
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private EFFolderModel<FolderDb, Entry> NewModel(bool efSize = false, bool unfiled = false, int childrenPageSize = int.MaxValue, bool shared = false,
        IDbContextFactory<FolderDb>? factory = null, bool rootsByNameDescThenWhere = false, bool noMoreRow = false,
        bool rootsByNameDescThenProjection = false)
    {
        EFFolderModel<FolderDb, Entry> model = new()
        {
            VirtualChildrenFn = parent => unfiled && parent == null ? [UnfiledFolder()] : [],
            ChildrenPageSize = childrenPageSize,
            MoreRowFn = noMoreRow ? null : parent => new Entry { Id = -parent.Id, Name = "more", IsFolder = true },
            DbContextFactory = factory ?? _factory,
            RootsQueryFn = db =>
            {
                _rootQueries++;
                _onRootQuery?.Invoke();
                IQueryable<Entry> roots = db.Entries.AsNoTracking().Where(e => e.ParentId == null);
                if (rootsByNameDescThenProjection)
                    return roots.Select(e => new { e.Id, e.ParentId, e.Name, e.IsFolder, e.Size }).OrderByDescending(x => x.Name)
                        .Select(x => new Entry { Id = x.Id, ParentId = x.ParentId, Name = x.Name, IsFolder = x.IsFolder, Size = x.Size });
                return rootsByNameDescThenWhere ? roots.OrderByDescending(e => e.Name).Where(e => e.Size >= 0) : roots;
            },
            ChildrenQueryFn = (db, parent) =>
            {
                _childQueries++;   // shared: a is under r2 too
                Thread.Sleep(_childQueryMs);
                return db.Entries.AsNoTracking().Where(e => e.ParentId == parent.Id || (shared && parent.Id == R2 && e.Id == A)).OrderBy(e => e.Name);
            },
            ParentFn = async (db, child, ct) => child.ParentId == Unfiled ? UnfiledFolder()
                : await db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == child.ParentId, ct),
            IsFolderFn = e => e.IsFolder,
            ItemKeyExpr = e => e.Id,
            MinRowsToRead = 10,
        };
        model.AddColumn("Name", new Column<Entry, string>("Name"), e => e.Name);
        if (efSize)
            model.AddEFColumn("Size", new Column<Entry, int>("Size"), e => e.Size);
        else
            model.AddColumn("Size", new Column<Entry, int>("Size"), e => e.Size);
        return model;
    }

    private sealed class PausingInterceptor : DbCommandInterceptor
    {
        public volatile bool Armed;
        public readonly TaskCompletionSource Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Paused.SetResult();
                await Resume.Task;
            }
            return result;
        }
    }

    private static Entry UnfiledFolder() => new() { Id = Unfiled, Name = "unfiled", IsFolder = true };

    private static int[] Ids(EFFolderModel<FolderDb, Entry> model) => model.GetDataView().Select(e => e.Id).ToArray();

    private static int RowOf(EFFolderModel<FolderDb, Entry> model, int id) => Ids(model).ToList().IndexOf(id);

    private void Exec(string sql) => new SqliteCommand(sql, _connection).ExecuteNonQuery();

    /// <summary>
    /// A host's search awaiting its query while the grid reads on through the roots ends cancelled: both would append from the
    /// same offset, and every root must still come once.
    /// </summary>
    [Test]
    public async Task AReadOvertakenByAnotherReadIsCancelledTest()
    {
        PausingInterceptor pause = new();
        using var model = NewModel(factory: new Factory(new DbContextOptionsBuilder<FolderDb>().UseSqlite(_connection).AddInterceptors(pause).Options));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10));
        pause.Armed = true;
        Task<int> search = Task.Run(async () => await model.ContainsSearchAsync("Name", "no match", true, 0));
        await pause.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await model.FetchRowsAsync(0, 20);
        pause.Resume.SetResult();
        Assert.CatchAsync<OperationCanceledException>(async () => await search.WaitAsync(TimeSpan.FromSeconds(10)));

        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model), Is.Unique.And.Length.EqualTo(28));
    }

    /// <summary>
    /// A host's search awaiting its query while the user sorts ends cancelled: the sort dropped the rows the search was adding
    /// to, and the sorted rows are built afresh.
    /// </summary>
    [Test]
    public async Task AReadOvertakenByASortIsCancelledTest()
    {
        PausingInterceptor pause = new();
        using var model = NewModel(factory: new Factory(new DbContextOptionsBuilder<FolderDb>().UseSqlite(_connection).AddInterceptors(pause).Options));
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10));
        pause.Armed = true;
        Task<int> search = Task.Run(async () => await model.ContainsSearchAsync("Name", "no match", true, 0));
        await pause.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.SetSortedColumn("Name", false);
        pause.Resume.SetResult();
        Assert.CatchAsync<OperationCanceledException>(async () => await search.WaitAsync(TimeSpan.FromSeconds(10)));

        await model.FetchRowsAsync(0, 100);
        string[] names = model.GetDataView().Select(e => e.Name).ToArray();
        Assert.That(names, Is.Unique.And.Length.EqualTo(28).And.Ordered.Descending);
    }

    /// <summary>
    /// An expand reads nothing itself: it is done at once, and the next fetch, the grid's render, reads the children, once.
    /// </summary>
    [Test]
    public async Task AnExpandIsReadByTheNextFetchTest()
    {
        using var model = NewModel();
        model.SetCurrentRow(RowOf(model, R1));
        ValueTask expand = model.SetExpandedAsync(RowOf(model, R1), true);
        Assert.Multiple(() =>
        {
            Assert.That(expand.IsCompleted, Is.True);
            Assert.That(_childQueries, Is.Zero, "nothing read yet");
            Assert.That(model.RequiresFetch(), Is.True);
        });
        await model.FetchRowsAsync(0, 5);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(4), Is.EqualTo(new[] { R1, A, B, R2 }));
            Assert.That(_childQueries, Is.EqualTo(1));
            Assert.That(model.GetCurrentItem()?.Id, Is.EqualTo(R1));
        });
    }

    /// <summary>
    /// A backward search that starts past the roots read reads up to its start first, so it finds a root not read yet.
    /// </summary>
    [Test]
    public async Task BackwardContainsSearchPastTheRowsReadReadsUpToItsStartTest()
    {
        using var model = NewModel();
        Assert.That(model.CanDoContainsSearch(), Is.True);
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one pass read");
        int found = await model.ContainsSearchAsync("Name", "z20", forward: false, startFrom: 26);
        Assert.That(found, Is.GreaterThanOrEqualTo(10), "a root past the first pass");
        Assert.That(model.GetItem(found).Name, Is.EqualTo("z20"));
    }

    /// <summary>
    /// A backward search that starts on the first root not read reads it and searches it, as any start is searched.
    /// </summary>
    [Test]
    public async Task BackwardContainsSearchFromTheFirstRootNotReadSearchesItTest()
    {
        string expected;
        using (var all = NewModel())
        {
            await all.FetchRowsAsync(0, 100);
            expected = all.GetItem(10).Name;
        }
        using var model = NewModel();
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one pass read");
        Assert.That(await model.ContainsSearchAsync("Name", expected, forward: false, startFrom: 10), Is.EqualTo(10));
    }

    /// <summary>
    /// A call after the query's OrderBy, a Where or an AsNoTracking, keeps its order: the key goes right after the OrderBy.
    /// </summary>
    [Test]
    public void ACallAfterTheOrderByKeepsTheOrderTest()
    {
        using var model = NewModel(rootsByNameDescThenWhere: true);
        model.GetRowCount();
        Assert.That(Ids(model).Take(3), Is.EqualTo(new[] { 124, 123, 122 }), "z24, z23, z22: by name, descending");
    }

    /// <summary>
    /// A query ordered before a projection from another type keeps its order, as on EFDataGridModel: a key ordering appended
    /// after the projection would replace it.
    /// </summary>
    [Test]
    public async Task AnOrderBeforeAProjectionIsKeptTest()
    {
        using var model = NewModel(rootsByNameDescThenProjection: true);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model), Is.EqualTo(Enumerable.Range(100, 25).Reverse().Concat([R3, R2, R1])),
            "z24 … z00, r3, r2, r1: by name, descending, across the passes");
    }

    /// <summary>
    /// A current row past the rows read, as a page down sets it, stays on its record when an expand, a filter, a sort, a reload
    /// or a tree-state restore builds the rows again: the change reads up to it first, as EFDataGridModel's reload does, so its
    /// path finds it again.
    /// </summary>
    [Test]
    public async Task ACurrentRowPastTheRowsReadKeepsItsRecordTest()
    {
        using var expanded = NewModel();
        Assert.That(expanded.GetCurrentRowCount(), Is.EqualTo(10), "one pass read");
        expanded.SetCurrentRow(15);
        await expanded.SetExpandedAsync(RowOf(expanded, R1), true);
        Assert.That(expanded.GetCurrentItem()?.Name, Is.EqualTo("z12"), "after an expand");

        using var filtered = NewModel();
        Assert.That(filtered.GetCurrentRowCount(), Is.EqualTo(10));
        filtered.SetCurrentRow(15);
        filtered.ApplyFilters([new("Name", new DefaultDef { Value = "z", Contains = true })]);
        Assert.That(filtered.GetCurrentItem()?.Name, Is.EqualTo("z12"), "after a filter");

        using var sorted = NewModel();
        Assert.That(sorted.GetCurrentRowCount(), Is.EqualTo(10));
        sorted.SetCurrentRow(15);
        sorted.SetSortedColumn("Name", false);
        Assert.That(sorted.GetCurrentItem()?.Name, Is.EqualTo("z12"), "after a sort");

        using var reloaded = NewModel();
        Assert.That(reloaded.GetCurrentRowCount(), Is.EqualTo(10));
        reloaded.SetCurrentRow(15);
        Assert.That(await reloaded.ReloadItemAsync(RowOf(reloaded, R1)), Is.True);
        Assert.That(reloaded.GetCurrentItem()?.Name, Is.EqualTo("z12"), "after a reload");

        using var restored = NewModel();
        Assert.That(restored.GetCurrentRowCount(), Is.EqualTo(10));
        restored.SetCurrentRow(15);
        restored.RestoreTreeState(restored.GetTreeState());
        Assert.That(restored.GetCurrentItem()?.Name, Is.EqualTo("z12"), "after a tree-state restore");
    }

    [Test]
    public async Task RootsArePagedAndChildrenReadOnExpandTest()
    {
        using var model = NewModel();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(int.MaxValue), "more roots than one pass: the count is not known yet");
            Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "a getter reads the first pass");
            Assert.That(_childQueries, Is.Zero);
            Assert.That(model.GetTreeNode(RowOf(model, R1)).HasChildren(), Is.True, "not read yet: it may have some");
            Assert.That(model.GetTreeNode(RowOf(model, R3)).IsLeaf(), Is.True);
        });
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(4), Is.EqualTo(new[] { R1, A, B, R2 }), "the children under their folder, in their query's order");
            Assert.That(model.GetTreeNode(1).GetLevel(), Is.EqualTo(1));
            Assert.That(_childQueries, Is.EqualTo(1));
        });
        await model.SetExpandedAsync(RowOf(model, R1), false);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Assert.That(_childQueries, Is.EqualTo(1), "read once, kept while collapsed");
        await model.SetExpandedAsync(RowOf(model, R2), true);
        Assert.That(model.GetTreeNode(RowOf(model, C)).HasChildren(), Is.True, "c is not read yet");
        await model.SetExpandedAsync(RowOf(model, C), true);
        Assert.That(model.GetTreeNode(RowOf(model, C)).HasChildren(), Is.False, "read, and empty");

        Assert.That(await model.FetchRowsAsync(0, 100), Is.EqualTo(3 + 2 + 1 + 25), "past the end: every row there is");
        Assert.That(model.GetRowCount(), Is.EqualTo(31), "all the roots read: the count is known");
        Assert.That(model.RequiresFetch(), Is.False);
    }

    /// <summary>
    /// While the roots are read in passes their count is unknown, so a page down may set a current row past the roots read, as
    /// on EFDataGridModel: the getters read up to it, and finding the end first puts it on the last row.
    /// </summary>
    [Test]
    public async Task CurrentRowPastTheRootsReadIsReadOrClampedTest()
    {
        using var model = NewModel();
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "one pass read");
        model.SetCurrentRow(15);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem(), Is.SameAs(model.GetItem(15)), "read up to it");
            Assert.That(model.GetCurrentTreeNode(), Is.SameAs(model.GetTreeNode(15)));
        });

        using var fetched = NewModel();
        fetched.SetCurrentRow(40);   // 28 roots
        await fetched.FetchRowsAsync(0, 100);
        Assert.That(fetched.GetCurrentRow(), Is.EqualTo(27), "the grid's fetch found the end");

        using var zoomed = NewModel();
        zoomed.SetCurrentRow(40);
        Assert.Multiple(() =>
        {
            Assert.That(zoomed.CanZoomIn(), Is.False, "the zoom bar asks before the fetch, and reads nothing");
            Assert.That(zoomed.GetCurrentItem(), Is.SameAs(zoomed.GetItem(27)), "a getter reads up to it");
            Assert.That(zoomed.GetCurrentRow(), Is.EqualTo(27));
        });
    }

    /// <summary>
    /// A folder stays shown while a row read below it matches, collapsed too; a folder never expanded has nothing read below it
    /// to match.
    /// </summary>
    [Test]
    public async Task FilterKeepsTheFoldersAboveAMatchTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, A), true);
        model.ApplyFilters([new("Name", new DefaultDef { Value = "apple" })]);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1, A, A1 }));
        await model.SetExpandedAsync(RowOf(model, A), false);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1, A }), "collapsed, a still holds the match");
        model.ApplyFilters([]);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model).Take(5), Is.EqualTo(new[] { R1, A, B, R2, R3 }));
    }

    /// <summary>
    /// A column removed before the filtered one moves it down an index: the roots read after it are still filtered on it.
    /// </summary>
    [Test]
    public async Task RemovingAColumnKeepsTheFilterOnItsColumnTest()
    {
        using var model = NewModel();
        model.AddColumn("Tag", new Column<Entry, string>("Tag"), e => e.Name);
        model.ApplyFilters([new("Tag", new DefaultDef { Value = "z", Contains = true })]);
        Assert.That(model.GetCurrentRowCount(), Is.GreaterThan(0));
        Assert.That(model.RequiresFetch(), Is.True, "roots left to read");
        model.RemoveColumn("Size");
        await model.FetchRowsAsync(0, 100);
        Assert.That(model.GetDataView().Select(e => e.Name), Is.EqualTo(Enumerable.Range(0, 25).Select(i => $"z{i:D2}")));
    }

    [Test]
    public async Task SiblingsAreSortedAndTheCurrentRowFollowsItsPathTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, A), true);
        model.SetCurrentRow(RowOf(model, A1));
        model.SetSortedColumn("Name", false);   // not an EF column: every root is read, to be sorted in memory
        await model.FetchRowsAsync(0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRowCount(), Is.EqualTo(32));
            Assert.That(model.GetDataView().Select(e => e.Name).Take(3), Is.EqualTo(new[] { "z24", "z23", "z22" }));
            Assert.That(Ids(model).SkipWhile(id => id != R1).Take(5), Is.EqualTo(new[] { R1, B, A, A2, A1 }), "each level sorted in memory");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(A1), "found again by its path");
        });
        await model.SetExpandedAsync(RowOf(model, R1), false);
        Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(R1), "hidden: on its nearest ancestor shown");
    }

    [Test]
    public async Task AnEFColumnSortsTheRootsInTheDatabaseTest()
    {
        using var model = NewModel(efSize: true);
        model.SetSortedColumn("Size", false);
        await model.FetchRowsAsync(0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10), "still paged");
            Assert.That(model.GetDataView().Select(e => e.Size), Is.EqualTo(Enumerable.Range(115, 10).Reverse()));
        });
        await model.FetchRowsAsync(0, 100);
        Assert.That(model.GetDataView().Select(e => e.Size).TakeLast(5), Is.EqualTo(new[] { 101, 100, 30, 10, 5 }), "the next passes in the same order");
    }

    [Test]
    public async Task ReloadReadsTheRowAgainOrDropsItTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Exec($"UPDATE Entries SET Name = 'cherries' WHERE Id = {B}");
        Assert.That(await model.ReloadItemAsync(RowOf(model, B)), Is.True);
        Assert.That(model.GetItem(RowOf(model, B)).Name, Is.EqualTo("cherries"));

        model.SetCurrentRow(RowOf(model, B));
        Exec($"DELETE FROM Entries WHERE Id = {B}");
        Assert.That(await model.ReloadItemAsync(RowOf(model, B)), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Does.Not.Contain(B));
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(R1), "on its parent");
        });

        Exec($"INSERT INTO Entries (Id, ParentId, Name, IsFolder, Size) VALUES (9, {R1}, 'date', 0, 4)");
        model.ForceReload(true);
        await model.FetchRowsAsync(0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(4), Is.EqualTo(new[] { R1, A, 9, R2 }), "read again, r1 still expanded");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(R1));
        });
    }

    /// <summary>
    /// A current row deleted and reloaded leaves the next build nothing to look for: a root gone keeps the current row on the
    /// root after it without reading the roots to their end, and a row below a folder gone goes to that folder's parent.
    /// </summary>
    [Test]
    public async Task ReloadOfADeletedCurrentRowReadsNoFurtherTest()
    {
        using var model = NewModel();
        int row = RowOf(model, R3);   // a root in the first pass of ten
        int next = Ids(model)[row + 1];
        model.SetCurrentRow(row);
        Exec($"DELETE FROM Entries WHERE Id = {R3}");
        Assert.That(await model.ReloadItemAsync(row), Is.False);
        _rootQueries = 0;
        int current = model.GetCurrentItem()!.Id;
        Assert.Multiple(() =>
        {
            Assert.That(current, Is.EqualTo(next), "on the root after it");
            Assert.That(_rootQueries, Is.Zero, "the roots read are enough");
        });

        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, A), true);
        model.SetCurrentRow(RowOf(model, A1));
        Exec($"DELETE FROM Entries WHERE Id = {A}");
        Assert.That(await model.ReloadItemAsync(RowOf(model, A)), Is.False);
        Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(R1), "below the folder gone: on its parent");
    }

    /// <summary>The folder zoomed on deleted and reloaded zooms out without reading its level to the end.</summary>
    [Test]
    public async Task ReloadOfADeletedZoomedFolderReadsNoFurtherTest()
    {
        using var model = NewModel();
        model.SetCurrentRow(RowOf(model, R1));
        await model.ZoomInAsync();
        await model.FetchRowsAsync(0, 0);
        Exec($"DELETE FROM Entries WHERE Id = {R1}");
        Assert.That(await model.ReloadItemAsync(0), Is.False);
        _rootQueries = 0;
        await model.FetchRowsAsync(0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetZoomLevel(), Is.Zero, "zoomed out");
            Assert.That(_rootQueries, Is.Zero, "the roots read are enough");
            Assert.That(model.RequiresFetch(), Is.True, "the roots still paged");
        });
    }

    [Test]
    public async Task GoToExpandsTheAncestorsTest()
    {
        using var model = NewModel();
        Entry banana;
        using (FolderDb db = _factory.CreateDbContext())
            banana = db.Entries.AsNoTracking().Single(e => e.Id == A2);
        await model.GoToAsync(banana);
        Assert.That((model.RequiresFetch(), _childQueries), Is.EqualTo((true, 0)), "only the ancestors read: the children are the next fetch's");
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(A2));
            Assert.That(Ids(model).Take(5), Is.EqualTo(new[] { R1, A, A1, A2, B }));
            Assert.That(model.GetCurrentTreeNode()!.GetLevel(), Is.EqualTo(2));
        });
    }

    /// <summary>
    /// A row moved to the top level after every root was read is a root GoToAsync reads, as it reads a row moved under a folder
    /// whose children were read.
    /// </summary>
    [Test]
    public async Task GoToReadsARootAddedSinceTheRootsWereReadTest()
    {
        using var model = NewModel();
        await model.FetchRowsAsync(0, 100);
        Assert.That(model.RequiresFetch(), Is.False, "every root read");
        Exec($"UPDATE Entries SET ParentId = NULL WHERE Id = {A}");
        Entry a;
        using (FolderDb db = _factory.CreateDbContext())
            a = db.Entries.AsNoTracking().Single(e => e.Id == A);
        await model.GoToAsync(a);
        await model.FetchRowsAsync(0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()?.Id, Is.EqualTo(A));
            Assert.That(model.GetCurrentTreeNode()?.GetLevel(), Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A reload of the model while a row is read again drops the row's level: the row's reload ends with
    /// OperationCanceledException, and the next build reads the tree as it was.
    /// </summary>
    [Test]
    public async Task ReloadOvertakenByAForceReloadEndsCancelledTest()
    {
        using var model = NewModel();
        await model.FetchRowsAsync(0, 100);
        List<int> before = Ids(model).ToList();
        int row = RowOf(model, R2);
        _onRootQuery = () =>
        {
            _onRootQuery = null;
            model.ForceReload(true);
        };

        Assert.ThrowsAsync<OperationCanceledException>(async () => await model.ReloadItemAsync(row));
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model), Is.EqualTo(before));
    }

    /// <summary>GoToAsync to a row outside the folder zoomed on zooms out as far as it takes to show the row.</summary>
    [Test]
    public async Task GoToOutsideTheZoomZoomsOutTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        model.SetCurrentRow(RowOf(model, A));
        await model.ZoomInAsync();
        Entry b, c;
        using (FolderDb db = _factory.CreateDbContext())
        {
            b = db.Entries.AsNoTracking().Single(e => e.Id == B);
            c = db.Entries.AsNoTracking().Single(e => e.Id == C);
        }

        await model.GoToAsync(b);   // beside the folder zoomed on
        await model.FetchRowsAsync(0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()?.Id, Is.EqualTo(B));
            Assert.That(model.GetZoomedItem()?.Id, Is.EqualTo(R1));
        });

        await model.GoToAsync(c);   // under another root
        await model.FetchRowsAsync(0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()?.Id, Is.EqualTo(C));
            Assert.That(model.GetZoomedItem(), Is.Null);
        });
    }

    /// <summary>
    /// A parent chain that comes back to a row already on it, bad data in a self-referencing table, makes GoToAsync throw instead of
    /// walking it for ever.
    /// </summary>
    [Test]
    public void GoToOnACyclicParentChainThrowsTest()
    {
        using var model = NewModel();
        Exec($"UPDATE Entries SET ParentId = {A2} WHERE Id = {R1}");   // banana → a → r1 → banana
        Entry banana;
        using (FolderDb db = _factory.CreateDbContext())
            banana = db.Entries.AsNoTracking().Single(e => e.Id == A2);
        // On the pool: SQLite completes synchronously, so a walk with no end would never return to the timeout
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Task.Run(async () => await model.GoToAsync(banana)).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.That(ex!.Message, Does.Contain("own ancestor"));
    }

    /// <summary>A label given to DefaultTreeCell takes the place of the expansion column's value, which names the folders in the zoom bar.</summary>
    [Test]
    public async Task ATreeCellTemplateLabelsTheNodesTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        using var model = NewModel();
        RenderFragment columns = b =>
        {
            b.OpenComponent<GridColumn<Entry, string>>(0);
            b.AddAttribute(1, "Name", Treegliaria.ExpColName);
            b.AddAttribute(2, "Property", (Func<Entry, string>)(e => e.Name.ToUpperInvariant()));
            b.CloseComponent();
        };
        RenderFragment<Treegliaria<Entry>.CellTemplateParam> cell = p => b =>
        {
            b.OpenComponent<Glazor.Grds.Decs.DefaultTreeCell<Entry>>(0);
            b.AddAttribute(1, "Param", p);
            b.AddAttribute(2, "ChildContent", (RenderFragment)(c =>
            {
                c.OpenElement(0, "b");
                c.AddContent(1, p.Model.GetItem(p.CellParam.RowIdx).Name);
                c.CloseElement();
            }));
            b.CloseComponent();
        };
        var cut = ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32).Add(g => g.ChildContent, columns).Add(g => g.TreeCellTemplate, cell));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("td.label b").Select(b => b.TextContent).Take(3), Is.EqualTo(new[] { "r1", "r2", "r3" }));
            Assert.That(cut.FindAll("td.expandable"), Has.Count.EqualTo(2), "r1 and r2 keep their expander");
            Assert.That(cut.FindComponents<Glazor.Grds.Decs.DefaultCell<Entry>>().Count(c => c.Instance.Param.ColIdx == 0),
                Is.EqualTo(cut.FindAll("td.label").Count).And.Positive,
                "each label drawn by DefaultCell, which takes the click that makes its row current");
        });

        await cut.InvokeAsync(async () => await cut.Instance.ToggleExpandAsync(0));
        await cut.InvokeAsync(() => model.SetCurrentRow(RowOf(model, A)));
        await cut.InvokeAsync(async () => await model.ZoomInAsync());
        Assert.That(cut.FindAll("nav.zoombar a").Select(a => a.TextContent.Trim()), Is.EqualTo(new[] { "R1" }),
            "named by the expansion column's value");
    }

    /// <summary>ZoomLabel names the folders in the zoom bar's path, in place of the tree column's value.</summary>
    [Test]
    public async Task ZoomLabelNamesTheFoldersInTheZoomBarTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        using var model = NewModel();
        var cut = ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32).Add(g => g.TreeColumn, "Name").Add(g => g.ZoomLabel, e => "#" + e.Name));
        await cut.InvokeAsync(async () => await cut.Instance.ToggleExpandAsync(0));
        await cut.InvokeAsync(() => model.SetCurrentRow(RowOf(model, A)));
        await cut.InvokeAsync(async () => await model.ZoomInAsync());
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("nav.zoombar a").Select(a => a.TextContent.Trim()), Is.EqualTo(new[] { "#r1" }));
            Assert.That(cut.Find("nav.zoombar span[aria-current]").TextContent.Trim(), Is.EqualTo("#a"));
        });
    }

    /// <summary>A column of the model draws the tree: no expansion column of its own, and the column stays fixed and sortable.</summary>
    [Test]
    public async Task ATreeColumnCarriesTheExpanderTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        using var model = NewModel();
        var cut = ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32).Add(g => g.TreeColumn, "Name"));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetColumnsToShow(), Is.EqualTo(new[] { "Name", "Size" }), "no expansion column");
            Assert.That(cut.FindAll("td.indent").Select(td => td.TextContent.Trim()).Take(3), Is.EqualTo(new[] { "r1", "r2", "r3" }));
            Assert.That(cut.FindAll("td.indent td.expandable"), Has.Count.EqualTo(2), "r1 and r2 have their expander");
            var name = cut.FindAll("th").Single(th => th.TextContent.Contains("Name"));
            Assert.That(name.ClassList, Does.Contain("fixed"));
            Assert.That(name.GetAttribute("aria-sort"), Is.EqualTo("none"), "sortable as the model's column");
        });

        await cut.FindAll("th").Single(th => th.TextContent.Contains("Name")).QuerySelector("a.enabled")!.ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.GetSortedColumn(), Is.EqualTo("Name"), "its header sorts the model");
            Assert.That(model.GetSortType(), Is.EqualTo(SortType.Asc));
        });
    }

    [Test]
    public void ATreeColumnMustBeAColumnOfTheModelTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        using var model = NewModel();
        Assert.Throws<InvalidOperationException>(() => ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.TreeColumn, "Missing")));
        RenderFragment expansion = b =>
        {
            b.OpenComponent<GridColumn<Entry, string>>(0);
            b.AddAttribute(1, "Name", Treegliaria.ExpColName);
            b.AddAttribute(2, "Property", (Func<Entry, string>)(e => e.Name));
            b.CloseComponent();
        };
        Assert.Throws<InvalidOperationException>(() => ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model)
            .Add(g => g.Spinner, new NoSpinner()).Add(g => g.TreeColumn, "Name").Add(g => g.ChildContent, expansion)),
            "with a tree column there is no expansion column to declare");
    }

    /// <summary>The grid reads the first pass under the spinner, and its expander reads the children.</summary>
    [Test]
    public async Task TreegliariaShowsAndExpandsTheFoldersTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        using var model = NewModel();
        var cut = ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32));
        Assert.That(cut.Markup, Does.Contain("r1").And.Not.Contain("cherry"));
        await cut.InvokeAsync(async () => await cut.Instance.ToggleExpandAsync(0));
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("cherry"));
            Assert.That(cut.FindAll("td.expandable a").First().GetAttribute("aria-expanded"), Is.EqualTo("true"));
        });

        async Task PressAsync(string key)
        {
            KeyAction action = cut.FindComponents<KeyAction>().Single(k => k.Instance.CtrlKey && k.Instance.ShiftKey && k.Instance.Key == key).Instance;
            await cut.InvokeAsync(() => action.ProcessAsync(null, key, null));
            cut.Render();
        }
        await cut.InvokeAsync(() => model.SetCurrentRow(RowOf(model, A)));
        await PressAsync("ArrowRight");
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("apple").And.Not.Contain("cherry"), "zoomed on a");
            Assert.That(cut.FindAll("nav.zoombar a").Select(a => a.TextContent.Trim()), Is.EqualTo(new[] { "r1" }), "the way back out");
            Assert.That(cut.Find("nav.zoombar [aria-current]").TextContent, Is.EqualTo("a"));
        });
        await cut.Find("nav.zoombar a").ClickAsync(new());
        Assert.That(cut.Find("nav.zoombar [aria-current]").TextContent, Is.EqualTo("r1"));
        await PressAsync("ArrowLeft");
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("cherry"));
            Assert.That(cut.FindAll("nav.zoombar a, nav.zoombar [aria-current]"), Is.Empty, "no zoom, no path");
            Assert.That(cut.Find("button.btnZoomOut").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find("button.btnZoomOutAll").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find("button.btnZoomIn").HasAttribute("disabled"), Is.False, "on a, a folder");
        });

        await cut.Find("button.btnZoomIn").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("nav.zoombar [aria-current]").TextContent, Is.EqualTo("a"));
            Assert.That(cut.Find("button.btnZoomIn").HasAttribute("disabled"), Is.True, "zoomed on a already");
        });
        await cut.Find("button.btnZoomOut").ClickAsync(new());
        Assert.That(cut.Find("nav.zoombar [aria-current]").TextContent, Is.EqualTo("r1"));
        await cut.Find("button.btnZoomOutAll").ClickAsync(new());
        Assert.That(cut.FindAll("nav.zoombar [aria-current]"), Is.Empty);

        cut.Render(ps => ps.Add(g => g.ShowZoomBar, false));
        Assert.That(cut.FindAll("nav.zoombar"), Is.Empty);
    }

    /// <summary>
    /// A virtual folder shows first among its siblings, and its children come from the children's query; with no values of its
    /// own, a filter keeps it only for a row read below it that matches.
    /// </summary>
    [Test]
    public async Task VirtualFolderComesFirstAndMatchesOnlyBelowTest()
    {
        using var model = NewModel(unfiled: true);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(2), Is.EqualTo(new[] { Unfiled, R1 }));
            Assert.That(model.GetTreeNode(0).HasChildren(), Is.True);
        });
        await model.SetExpandedAsync(0, true);
        Assert.That(Ids(model).Take(4), Is.EqualTo(new[] { Unfiled, 50, 51, R1 }));
        model.ApplyFilters([new("Name", new DefaultDef { Value = "two" })]);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model), Is.EqualTo(new[] { Unfiled, 51 }));

        using var fresh = NewModel(unfiled: true);
        fresh.ApplyFilters([new("Name", new DefaultDef { Value = "unfiled" })]);
        await fresh.FetchRowsAsync(0, 100);
        Assert.That(Ids(fresh), Is.Empty, "its own name matches, but nothing is read below it");
    }

    [Test]
    public async Task ReloadingAVirtualFolderReadsItsChildrenAgainTest()
    {
        using var model = NewModel(unfiled: true);
        await model.SetExpandedAsync(0, true);
        Exec($"INSERT INTO Entries (Id, ParentId, Name, IsFolder, Size) VALUES (52, {Unfiled}, 'unfiled three', 0, 9)");
        Assert.That(await model.ReloadItemAsync(0), Is.True);
        await model.FetchRowsAsync(0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(5), Is.EqualTo(new[] { Unfiled, 50, 52, 51, R1 }), "by name: three before two");
            Assert.That(_childQueries, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// Below a page of children a folder shows the more row, which reads the next page when expanded and makes its first row
    /// current; a page asks for one row more, so no more row shows once the folder is read whole.
    /// </summary>
    [Test]
    public async Task PagedChildrenReadTheNextPageFromTheMoreRowTest()
    {
        using var model = NewModel(childrenPageSize: 1);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Assert.That(Ids(model).Take(4), Is.EqualTo(new[] { R1, A, -R1, R2 }));
        ITreeNode more = model.GetTreeNode(2);
        Assert.Multiple(() =>
        {
            Assert.That(more.HasChildren() && !more.IsExpanded() && !more.IsLeaf(), Is.True, "drawn as a collapsed folder");
            Assert.That(more.GetLevel(), Is.EqualTo(1));
        });
        model.SetCurrentRow(2);
        await model.SetExpandedAsync(2, true);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(4), Is.EqualTo(new[] { R1, A, B, R2 }), "read whole: no more row");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(B), "the first row read");
        });
    }

    [Test]
    public async Task AFilterKeepsAFolderWithMoreToReadTest()
    {
        using var model = NewModel(childrenPageSize: 1);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        model.ApplyFilters([new("Name", new DefaultDef { Value = "cherry" })]);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1, -R1 }), "nothing read matches yet, but there is more to read");
        await model.SetExpandedAsync(0, false);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1 }), "collapsed, it still has more to read");
        await model.SetExpandedAsync(0, true);
        await model.SetExpandedAsync(1, true);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1, B }));
    }

    /// <summary>A paged level is sorted by the database for an EF column, and read whole to be sorted in memory otherwise.</summary>
    [Test]
    public async Task PagedChildrenFollowTheSortTest()
    {
        using var model = NewModel(efSize: true, childrenPageSize: 1);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        model.SetSortedColumn("Size", true);
        await model.FetchRowsAsync(0, 0);
        Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, B, -R1 }), "read again, smallest first");
        model.SetSortedColumn("Name", false);
        await model.FetchRowsAsync(0, 0);
        Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, B, A }), "read whole, sorted in memory");
    }

    /// <summary>A level read whole in the database's order follows a later sort, sorted in memory with no query.</summary>
    [Test]
    public async Task AWholeLevelFollowsTheSortItWasNotReadInTest()
    {
        using var model = NewModel(efSize: true);
        model.SetSortedColumn("Size", true);
        await model.FetchRowsAsync(0, 100);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, B, A }), "read by the database, smallest first");
        int childQueries = _childQueries;
        model.SetSortedColumn("Size", false);
        await model.FetchRowsAsync(0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, A, B }), "largest first");
            Assert.That(_childQueries, Is.EqualTo(childQueries), "all of it is here: not read again");
        });
    }

    /// <summary>A reloaded row of a level read whole in the database's order takes its new place in that order.</summary>
    [Test]
    public async Task AReloadedRowMovesInTheDatabaseOrderTest()
    {
        using var model = NewModel(efSize: true);
        model.SetSortedColumn("Size", true);
        await model.FetchRowsAsync(0, 100);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Exec($"UPDATE Entries SET Size = 25 WHERE Id = {B}");
        Assert.That(await model.ReloadItemAsync(RowOf(model, B)), Is.True);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, A, B }), "cherry now after a");
    }

    /// <summary>With no sort, a reloaded row of a level read whole takes its new place in the order of the children's query.</summary>
    [Test]
    public async Task AReloadedRowMovesInTheQueryOrderTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Exec($"UPDATE Entries SET Name = '0aaa' WHERE Id = {B}");
        Assert.That(await model.ReloadItemAsync(RowOf(model, B)), Is.True);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, B, A }), "0aaa now before a");
    }

    /// <summary>
    /// A row read again while its level is sorted in memory takes its new place in the order the level was read in once that
    /// order shows again: the children's query's when the sort goes, the database's when the sort it was read under returns.
    /// </summary>
    [Test]
    public async Task ARowReloadedUnderASortInMemoryMovesWhenTheReadOrderReturnsTest()
    {
        using var model = NewModel(efSize: true);
        await model.FetchRowsAsync(0, 100);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.FetchRowsAsync(0, 100);   // r1's children read in their query's order, by name
        model.SetSortedColumn("Size", false);   // all here: sorted in memory
        await model.FetchRowsAsync(0, 100);
        Exec($"UPDATE Entries SET Name = '0aaa' WHERE Id = {B}");
        Assert.That(await model.ReloadItemAsync(RowOf(model, B)), Is.True);
        model.SetSortedColumn(null, true);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, B, A }), "0aaa now before a");

        model.ForceReload(false);
        model.SetSortedColumn("Size", true);   // read in the database's order: 0aaa 3, a 20
        await model.FetchRowsAsync(0, 100);
        model.SetSortedColumn("Name", true);
        await model.FetchRowsAsync(0, 100);
        Exec($"UPDATE Entries SET Size = 25 WHERE Id = {B}");
        Assert.That(await model.ReloadItemAsync(RowOf(model, B)), Is.True);
        model.SetSortedColumn("Size", true);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model).SkipWhile(id => id != R1).Take(3), Is.EqualTo(new[] { R1, A, B }), "0aaa, now 25, after a");
    }

    /// <summary>The token given to a reload cancels the query that reads the row again.</summary>
    [Test]
    public async Task ACancelledTokenStopsTheRowReloadTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        int row = RowOf(model, B);
        Assert.That(async () => await model.ReloadItemAsync(row, new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>The parent function returns a virtual folder as it is, with no query to project it from.</summary>
    [Test]
    public async Task GoToPassesThroughAVirtualFolderTest()
    {
        using var model = NewModel(unfiled: true);
        Entry two;
        using (FolderDb db = _factory.CreateDbContext())
            two = db.Entries.AsNoTracking().Single(e => e.Id == 51);
        await model.GoToAsync(two);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(51));
            Assert.That(Ids(model).Take(3), Is.EqualTo(new[] { Unfiled, 50, 51 }));
        });
    }

    [Test]
    public async Task GoToReadsThePagesDownToTheRowTest()
    {
        using var model = NewModel(childrenPageSize: 1);
        Entry cherry;
        using (FolderDb db = _factory.CreateDbContext())
            cherry = db.Entries.AsNoTracking().Single(e => e.Id == B);
        await model.GoToAsync(cherry);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(B));
            Assert.That(Ids(model).Take(3), Is.EqualTo(new[] { R1, A, B }));
        });
    }

    /// <summary>
    /// A row under two parents has two places: each is expanded on its own, its children are read once for both, and the
    /// current row stays on its own place when the rows are built again.
    /// </summary>
    [Test]
    public async Task ARowUnderTwoParentsHasTwoPlacesTest()
    {
        using var model = NewModel(shared: true);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, R2), true);
        Assert.That(Ids(model).Take(7), Is.EqualTo(new[] { R1, A, B, R2, A, C, R3 }));
        await model.SetExpandedAsync(4, true);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(9), Is.EqualTo(new[] { R1, A, B, R2, A, A1, A2, C, R3 }), "expanded under r2 only");
            Assert.That(model.GetTreeNode(1).IsExpanded(), Is.False);
            Assert.That(model.GetTreeNode(4).IsExpanded(), Is.True);
        });
        int queries = _childQueries;
        await model.SetExpandedAsync(1, true);
        Assert.That(_childQueries, Is.EqualTo(queries), "a's children read once, for both places");

        model.SetCurrentRow(Ids(model).ToList().LastIndexOf(A1));   // apple under r2
        model.SetSortedColumn("Name", false);   // r2 before r1 now
        await model.FetchRowsAsync(0, 0);
        List<int> ids = Ids(model).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(ids.IndexOf(A1)), "the apple now first");
            Assert.That(model.GetCurrentRow(), Is.GreaterThan(ids.IndexOf(R2)).And.LessThan(ids.IndexOf(R1)), "still under r2");
        });
    }

    [Test]
    public async Task ReloadingARowUpdatesEveryPlaceOfItTest()
    {
        using var model = NewModel(shared: true);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, R2), true);
        Exec($"UPDATE Entries SET Name = 'aa' WHERE Id = {A}");
        Assert.That(await model.ReloadItemAsync(1), Is.True);
        await model.FetchRowsAsync(0, 0);
        Assert.That(model.GetDataView().Where(e => e.Id == A).Select(e => e.Name), Is.EqualTo(new[] { "aa", "aa" }));
    }

    [Test]
    public async Task ARowDeletedLeavesEveryPlaceOfItTest()
    {
        using var model = NewModel(shared: true, childrenPageSize: 1);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, R2), true);
        Assert.That(Ids(model).Take(7), Is.EqualTo(new[] { R1, A, -R1, R2, A, -R2, R3 }));
        Exec($"DELETE FROM Entries WHERE Id = {A}");
        Assert.That(await model.ReloadItemAsync(1), Is.False);
        await model.FetchRowsAsync(0, 0);
        Assert.That(Ids(model).Take(5), Is.EqualTo(new[] { R1, B, R2, C, R3 }), "gone under r2 too, and r2's page starts at c");
    }

    /// <summary>
    /// A row reloaded through one parent takes its new place under the other too: a level read a page at a time keeps no stale
    /// place, from which the next page, read by offset, would skip a row and repeat this one.
    /// </summary>
    [Test]
    public async Task AReloadedRowMovesUnderEveryParentTest()
    {
        using var model = NewModel(shared: true, childrenPageSize: 1);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, R2), true);
        Assert.That(Ids(model).Take(7), Is.EqualTo(new[] { R1, A, -R1, R2, A, -R2, R3 }));
        Exec($"UPDATE Entries SET Name = 'd' WHERE Id = {A}");   // after cherry and c now
        Assert.That(await model.ReloadItemAsync(1), Is.True);
        await model.FetchRowsAsync(0, 0);
        Assert.That(Ids(model).Take(7), Is.EqualTo(new[] { R1, B, -R1, R2, C, -R2, R3 }), "c first under r2, as under r1 cherry");
    }

    [Test]
    public async Task ARowLeavingOneParentStaysUnderTheOtherTest()
    {
        using var model = NewModel(shared: true);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, R2), true);
        Exec($"UPDATE Entries SET ParentId = {R3} WHERE Id = {A}");   // out of r1, still under r2
        Assert.That(await model.ReloadItemAsync(1), Is.False);
        await model.FetchRowsAsync(0, 0);
        Assert.That(Ids(model).Take(5), Is.EqualTo(new[] { R1, B, R2, A, C }));
    }

    [Test]
    public void ChildrenPageSizeWithoutMoreRowFnFailsAtTheFirstBuildTest()
    {
        using var model = NewModel(childrenPageSize: 1, noMoreRow: true);
        Assert.Throws<InvalidOperationException>(() => model.GetRowCount());
    }

    [Test]
    public async Task ExpandAllReadsEveryPageBelowAndCollapseAllClosesThemTest()
    {
        using var model = NewModel(childrenPageSize: 1);
        await model.ExpandAllAsync(RowOf(model, R1));
        Assert.That(Ids(model).Take(6), Is.EqualTo(new[] { R1, A, A1, A2, B, R2 }), "every page read: no more row");
        await model.CollapseAllAsync(RowOf(model, A));
        Assert.That(Ids(model).Take(4), Is.EqualTo(new[] { R1, A, B, R2 }), "r1 stays open");
        await model.SetExpandedAsync(RowOf(model, A), true);
        Assert.That(model.GetTreeNode(RowOf(model, A2)).GetLevel(), Is.EqualTo(2), "a's children kept");

        await model.ExpandAllAsync(-1);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(8), Is.EqualTo(new[] { R1, A, A1, A2, B, R2, C, R3 }));
            Assert.That(model.GetRowCount(), Is.EqualTo(8 + 25), "every root read");
        });
        await model.CollapseAllAsync(-1);
        Assert.That(Ids(model).Take(3), Is.EqualTo(new[] { R1, R2, R3 }));
    }

    [Test]
    public async Task ZoomShowsTheFolderAloneAndZoomOutClimbsBackTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        model.SetCurrentRow(RowOf(model, A));
        await model.ZoomInAsync();
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Is.EqualTo(new[] { A, A1, A2 }));
            Assert.That(model.GetRowCount(), Is.EqualTo(3));
            Assert.That(model.GetTreeNode(0).GetLevel(), Is.Zero);
            Assert.That(model.GetTreeNode(1).GetLevel(), Is.EqualTo(1));
            Assert.That(model.GetZoomLevel(), Is.EqualTo(2));
            Assert.That(model.GetZoomedItem()!.Id, Is.EqualTo(A));
            Assert.That(model.GetZoomPath().Select(e => e.Id), Is.EqualTo(new[] { R1, A }));
            Assert.That(model.CanZoomIn(), Is.False, "zoomed on it already");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(A));
        });

        await model.ZoomOutAsync(1);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Is.EqualTo(new[] { R1, A, A1, A2, B }));
            Assert.That(model.GetZoomLevel(), Is.EqualTo(1));
            Assert.That(model.GetZoomedItem()!.Id, Is.EqualTo(R1));
        });
        await model.ZoomOutAsync(0);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model).Take(6), Is.EqualTo(new[] { R1, A, A1, A2, B, R2 }));
            Assert.That(model.GetZoomLevel(), Is.Zero);
            Assert.That(model.GetZoomedItem(), Is.Null);
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(A));
        });
    }

    /// <summary>Collapse all while zoomed collapses the folders below the folder zoomed on, which stays expanded.</summary>
    [Test]
    public async Task CollapseAllWhileZoomedKeepsTheZoomedFolderOpenTest()
    {
        using var model = NewModel();
        model.SetCurrentRow(RowOf(model, R1));
        await model.ZoomInAsync();
        await model.SetExpandedAsync(RowOf(model, A), true);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1, A, A1, A2, B }));

        await model.CollapseAllAsync(-1);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1, A, B }), "the whole tree");
        await model.SetExpandedAsync(RowOf(model, A), true);
        await model.CollapseAllAsync(0);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Is.EqualTo(new[] { R1, A, B }), "the row of the folder zoomed on");
            Assert.That(model.GetZoomedItem()!.Id, Is.EqualTo(R1));
        });
    }

    [Test]
    public async Task ALeafZoomsOnItsFolderTest()
    {
        using var model = NewModel();
        model.SetCurrentRow(RowOf(model, R3));
        Assert.That(model.CanZoomIn(), Is.False, "a root leaf has no folder");

        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, A), true);
        model.SetCurrentRow(RowOf(model, A1));
        Assert.That(model.CanZoomIn(), Is.True);
        await model.ZoomInAsync();
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Is.EqualTo(new[] { A, A1, A2 }), "zoomed on a, the apple's folder");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(A1), "the current row stays on the leaf");
            Assert.That(model.CanZoomIn(), Is.False, "its folder is zoomed on already");
        });
    }

    [Test]
    public async Task AZoomOnAFolderGoneMovesToItsParentTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        model.SetCurrentRow(RowOf(model, A));
        await model.ZoomInAsync();
        Exec($"DELETE FROM Entries WHERE Id IN ({A}, {A1}, {A2})");
        model.ForceReload(true);
        await model.FetchRowsAsync(0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Is.EqualTo(new[] { R1, B }));
            Assert.That(model.GetZoomLevel(), Is.EqualTo(1));
            Assert.That(model.GetZoomedItem()!.Id, Is.EqualTo(R1));
        });
    }

    // ---- memento

    private static IRenderedComponent<Treegliaria<Entry>> RenderTree(BunitContext ctx, EFFolderModel<FolderDb, Entry> model)
        => ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32));

    /// <summary>
    /// Over a folder model the tree grid saves the folders expanded with its layout, filters and sort, as a TreeMemento that a
    /// store holding every grid's state as a Memento writes and reads back as itself; restored on another grid, the same
    /// folders show expanded.
    /// </summary>
    [Test]
    public async Task TreeMementoRoundTripsTheFoldersThroughJsonTest()
    {
        using BunitContext ctx = NewContext();
        using var model = NewModel();
        var cut = RenderTree(ctx, model);
        await cut.InvokeAsync(async () =>
        {
            await model.SetExpandedAsync(RowOf(model, R1), true);
            await model.SetExpandedAsync(RowOf(model, A), true);
        });
        Memento memento = await cut.InvokeAsync(() => cut.Instance.CreateMemento());
        string json = System.Text.Json.JsonSerializer.Serialize(memento);   // typed Memento, as a store for every grid holds it
        Memento? read = System.Text.Json.JsonSerializer.Deserialize<Memento>(json);
        Assert.Multiple(() =>
        {
            Assert.That(memento, Is.TypeOf<TreeMemento>());
            Assert.That(((TreeMemento)memento).Tree.Expanded.Select(p => string.Join("/", p.Keys)), Is.EquivalentTo(new[] { $"{R1}", $"{R1}/{A}" }));
            Assert.That(json, Does.StartWith("{\"$type\":\"tree\""));
            Assert.That(json, Does.Contain("\"Tree\":{\"Expanded\":[[").And.Contain("\"Zoom\":null"), "each path a bare array of keys");
            Assert.That(read, Is.TypeOf<TreeMemento>());
            Assert.That(read, Is.EqualTo(memento), "equal once read back, its keys JSON elements");
        });

        using var other = NewModel();
        var restoredOn = RenderTree(ctx, other);
        await restoredOn.InvokeAsync(() => restoredOn.Instance.RestoreMemento(read));
        await other.FetchRowsAsync(0, 0);
        Assert.That(Ids(other).Take(5), Is.EqualTo(new[] { R1, A, A1, A2, B }));
    }

    /// <summary>
    /// The zoom comes back with the folders; on a folder gone since, it goes to the nearest ancestor still there.
    /// </summary>
    [Test]
    public async Task RestoredZoomFallsBackToTheNearestAncestorTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        model.SetCurrentRow(RowOf(model, A));
        await model.ZoomInAsync();
        TreeState state = model.GetTreeState();
        Assert.That(state.Zoom!.Keys, Is.EqualTo(new object[] { R1, A }));

        using var same = NewModel();
        same.RestoreTreeState(state);
        await same.FetchRowsAsync(0, 0);
        Assert.That((same.GetZoomLevel(), Ids(same)), Is.EqualTo((2, new[] { A, A1, A2 })));

        Exec($"DELETE FROM Entries WHERE Id IN ({A}, {A1}, {A2})");
        using var gone = NewModel();
        gone.RestoreTreeState(state);
        await gone.FetchRowsAsync(0, 0);
        Assert.That((gone.GetZoomLevel(), gone.GetZoomedItem()!.Id), Is.EqualTo((1, R1)));
    }

    /// <summary>
    /// A saved path whose keys no longer fit the key type, as after a change of <c>ItemKeyExpr</c>, is skipped like a folder
    /// gone: the other folders are restored.
    /// </summary>
    [Test]
    public async Task ASavedKeyOfAnotherTypeIsSkippedTest()
    {
        TreeState state = System.Text.Json.JsonSerializer.Deserialize<TreeState>($$"""{"Expanded":[["x"],[{{R1}}]],"Zoom":["x"]}""")!;
        using var model = NewModel();
        Assert.DoesNotThrow(() => model.RestoreTreeState(state));
        await model.FetchRowsAsync(0, 0);
        Assert.That((model.GetZoomLevel(), Ids(model).Take(3)), Is.EqualTo((0, new[] { R1, A, B })));
    }

    /// <summary>
    /// The keys of a tree state round-trip through a host's own JSON options, ones that write numbers as strings or read
    /// unknown values as nodes: the folders come back expanded.
    /// </summary>
    [TestCase(System.Text.Json.Serialization.JsonNumberHandling.WriteAsString, System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement)]
    [TestCase(System.Text.Json.Serialization.JsonNumberHandling.Strict, System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonNode)]
    public async Task TreeStateKeysIgnoreTheHostOptionsTest(System.Text.Json.Serialization.JsonNumberHandling numbers,
        System.Text.Json.Serialization.JsonUnknownTypeHandling unknown)
    {
        System.Text.Json.JsonSerializerOptions options = new() { NumberHandling = numbers, UnknownTypeHandling = unknown };
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        string json = System.Text.Json.JsonSerializer.Serialize(model.GetTreeState(), options);
        TreeState read = System.Text.Json.JsonSerializer.Deserialize<TreeState>(json, options)!;

        using var other = NewModel();
        other.RestoreTreeState(read);
        await other.FetchRowsAsync(0, 0);
        Assert.That(Ids(other).Take(3), Is.EqualTo(new[] { R1, A, B }));
    }

    /// <summary>A stored tree state with no expanded nodes at all fails as a malformed store does, with a JsonException.</summary>
    [Test]
    public void TreeStateWithoutExpandedNodesIsAMalformedStoreTest()
    {
        Assert.That(() => System.Text.Json.JsonSerializer.Deserialize<TreeState>("""{"Expanded":null,"Zoom":null}"""),
            Throws.TypeOf<System.Text.Json.JsonException>());
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<TreeState>("""{"Expanded":[],"Zoom":null}""")!.Expanded, Is.Empty,
            "none expanded is a state");
    }

    /// <summary>Tree states compare their expanded nodes as a set, so a path listed twice is the same state, and hashes the same.</summary>
    [Test]
    public void EqualTreeStatesHashTheSameTest()
    {
        NodePath p = new([1, 7]);
        TreeState twice = new([p, p], null);
        TreeState once = new([p], null);
        Assert.That(twice, Is.EqualTo(once));
        Assert.That(twice.GetHashCode(), Is.EqualTo(once.GetHashCode()));
    }

    /// <summary>A plain memento, one saved before the tree memento existed, restores the grid and leaves the folders as they are.</summary>
    [Test]
    public async Task APlainMementoLeavesTheFoldersAsTheyAreTest()
    {
        using BunitContext ctx = NewContext();
        using var model = NewModel();
        var cut = RenderTree(ctx, model);
        await cut.InvokeAsync(async () => await model.SetExpandedAsync(RowOf(model, R1), true));
        Memento plain = new(null, new Dictionary<string, AFilterDef>(), []);
        await cut.InvokeAsync(() => cut.Instance.RestoreMemento(plain));
        await model.FetchRowsAsync(0, 0);
        Assert.That(Ids(model).Take(3), Is.EqualTo(new[] { R1, A, B }));
    }

    /// <summary>A tree memento whose store lost its tree restores the grid's own part and leaves the folders as they are.</summary>
    [Test]
    public async Task ATreeMementoWithoutItsTreeRestoresTheGridTest()
    {
        using BunitContext ctx = NewContext();
        using var model = NewModel();
        var cut = RenderTree(ctx, model);
        Memento? read = System.Text.Json.JsonSerializer.Deserialize<Memento>("""{"$type":"tree","Layout":null,"FiltersDefs":{},"SorterColumns":[]}""");
        Assert.That(read, Is.TypeOf<TreeMemento>());
        await cut.InvokeAsync(() => Assert.DoesNotThrow(() => cut.Instance.RestoreMemento(read)));
    }

    /// <summary>A folder found gone at the last build is left out of the state saved, so a stale one does not stay in the store.</summary>
    [Test]
    public async Task AFolderGoneIsLeftOutOfTheStateTest()
    {
        using var model = NewModel();
        await model.SetExpandedAsync(RowOf(model, R1), true);
        await model.SetExpandedAsync(RowOf(model, A), true);
        Exec($"DELETE FROM Entries WHERE Id IN ({A}, {A1}, {A2})");
        model.ForceReload(true);
        await model.FetchRowsAsync(0, 0);
        Assert.That(model.GetTreeState().Expanded.Select(p => string.Join("/", p.Keys)), Is.EqualTo(new[] { $"{R1}" }));
    }

    /// <summary>A key of several members cannot be read back from JSON as it was, so the state refuses it.</summary>
    [Test]
    public void ACompositeKeyCannotBeSavedTest()
    {
        using EFFolderModel<FolderDb, Entry> model = new()
        {
            DbContextFactory = _factory,
            RootsQueryFn = db => db.Entries.Where(e => e.ParentId == null),
            ChildrenQueryFn = (db, parent) => db.Entries.Where(e => e.ParentId == parent.Id),
            IsFolderFn = e => e.IsFolder,
            ItemKeyExpr = e => new { e.Id, e.Name },
        };
        Assert.That(model.CanSaveTreeState(), Is.False);
        Assert.Throws<NotSupportedException>(() => model.GetTreeState());
    }

    private sealed class HostWrapper(EFFolderModel<FolderDb, Entry> wrapped) : TreeGridModelWrapper<Entry>(wrapped);

    /// <summary>
    /// A host's wrapper around the folder model keeps what the model has: the tree grid shows the zoom bar and zooms through it,
    /// and saves the folders expanded in a tree memento.
    /// </summary>
    [Test]
    public async Task AWrapperKeepsTheFoldersAndTheTreeStateTest()
    {
        using BunitContext ctx = NewContext();
        using var model = NewModel();
        HostWrapper wrapper = new(model);
        var cut = ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, wrapper).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32));
        await cut.InvokeAsync(async () =>
        {
            await model.SetExpandedAsync(RowOf(model, R1), true);
            model.SetCurrentRow(RowOf(model, A));
            await wrapper.ZoomInAsync();
        });
        Memento memento = await cut.InvokeAsync(() => cut.Instance.CreateMemento());
        Assert.Multiple(() =>
        {
            Assert.That((wrapper.IsFolderModel(), wrapper.CanSaveTreeState()), Is.EqualTo((true, true)));
            Assert.That(cut.FindAll("nav.zoombar"), Has.Count.EqualTo(1));
            Assert.That(model.GetZoomedItem()!.Id, Is.EqualTo(A));
            Assert.That(memento, Is.TypeOf<TreeMemento>());
            Assert.That(((TreeMemento)memento).Tree.Zoom!.Keys, Is.EqualTo(new object[] { R1, A }));
        });
    }

    /// <summary>Over a key the state refuses, the tree grid still saves its layout, filters and sort, as a plain memento.</summary>
    [Test]
    public async Task ACompositeKeySavesThePlainMementoTest()
    {
        using BunitContext ctx = NewContext();
        using EFFolderModel<FolderDb, Entry> model = new()
        {
            DbContextFactory = _factory,
            RootsQueryFn = db => db.Entries.Where(e => e.ParentId == null),
            ChildrenQueryFn = (db, parent) => db.Entries.Where(e => e.ParentId == parent.Id),
            IsFolderFn = e => e.IsFolder,
            ItemKeyExpr = e => new { e.Id, e.Name },
        };
        model.AddColumn("Name", new Column<Entry, string>("Name"), e => e.Name);
        var cut = RenderTree(ctx, model);
        await cut.InvokeAsync(() => model.SetSortedColumn("Name", false));
        Memento memento = await cut.InvokeAsync(() => cut.Instance.CreateMemento());
        Assert.That((memento.GetType(), memento.SorterColumns), Is.EqualTo((typeof(Memento), new[] { new SortedColumn("Name", false) })));
    }

    private sealed class CountingSpinner : ISpinner
    {
        public int Shown, Hidden;
        public bool Show() { Shown++; return true; }
        public void Hide() { Hidden++; }
    }

    /// <summary>
    /// The tree grid's expand reads the children in its fetch, under its spinner, as any read of the grid; the model's change is
    /// raised on the dispatcher.
    /// </summary>
    [Test]
    public async Task AnExpandFromTheGridReadsUnderTheSpinnerTest()
    {
        using BunitContext ctx = NewContext();
        using var model = NewModel();
        CountingSpinner spinner = new();
        var cut = ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, spinner)
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32));
        cut.FindComponent<Grigliaria<Entry>>().Instance._spinnerHelper = new SpinnerHelper(10, 10_000, 6);
        List<bool> onDispatcher = [];
        model.OnModelChanged += _ => onDispatcher.Add(ctx.Renderer.Dispatcher.CheckAccess());
        _childQueryMs = 100;
        await cut.InvokeAsync(() => cut.Instance.ToggleExpandAsync(RowOf(model, R1)));
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("cherry")), TimeSpan.FromSeconds(2));   // r1's child, drawn
        _childQueryMs = 0;
        Assert.Multiple(() =>
        {
            Assert.That((spinner.Shown, spinner.Hidden), Is.EqualTo((1, 1)), "the spinner shown past its first wait, then hidden");
            Assert.That(_childQueries, Is.EqualTo(1));
            Assert.That(onDispatcher, Is.Not.Empty.And.All.True);
        });
    }

    /// <summary>
    /// A sort with the "More…" row current reads one page of its folder again, in the new order, and the folder becomes current:
    /// the row's own key is never read, and looking for it would read the folder to its end.
    /// </summary>
    [Test]
    public async Task ASortOnTheMoreRowReadsOnePageTest()
    {
        for (int i = 0; i < 6; i++)
            Exec($"INSERT INTO Entries (Id, ParentId, Name, IsFolder, Size) VALUES ({200 + i}, {R1}, 'x{i}', 0, {50 + i})");
        using var model = NewModel(efSize: true, childrenPageSize: 2);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        Assert.That(Ids(model).Take(5), Is.EqualTo(new[] { R1, A, B, -R1, R2 }));
        model.SetCurrentRow(3);   // the "More…" row
        int before = _childQueries;
        model.SetSortedColumn("Size", true);
        await model.FetchRowsAsync(0, 5);
        Assert.Multiple(() =>
        {
            Assert.That(_childQueries - before, Is.EqualTo(1), "one page of r1's children");
            Assert.That(Ids(model).Take(6), Is.EqualTo(new[] { R3, R2, R1, B, A, -R1 }), "by size, then More");
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(R1));
        });
    }

    /// <summary>
    /// Under a filter, "More…" makes the first row of the new page that is shown current, and stays current when the filter
    /// shows none of the page, so that the next page is one more expand away.
    /// </summary>
    [Test]
    public async Task MoreUnderAFilterMakesTheFirstNewRowShownCurrentTest()
    {
        string[] names = ["aa keep", "ca drop", "d drop", "dz keep", "e drop"];   // r1's pages of 2: a, aa keep | ca drop, cherry | d drop, dz keep | e drop
        for (int i = 0; i < names.Length; i++)
            Exec($"INSERT INTO Entries (Id, ParentId, Name, IsFolder, Size) VALUES ({200 + i}, {R1}, '{names[i]}', 0, 1)");
        using var model = NewModel(childrenPageSize: 2);
        await model.SetExpandedAsync(RowOf(model, R1), true);
        model.ApplyFilters([new("Name", new DefaultDef { Value = "keep", Contains = true })]);
        await model.FetchRowsAsync(0, 100);
        Assert.That(Ids(model), Is.EqualTo(new[] { R1, 200, -R1 }));

        model.SetCurrentRow(2);
        await model.SetExpandedAsync(2, true);   // ca drop, cherry: none shown
        await model.FetchRowsAsync(0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Is.EqualTo(new[] { R1, 200, -R1 }));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(2), "still on More…, the next page one expand away");
        });

        await model.SetExpandedAsync(2, true);   // d drop, dz keep: the first hidden
        await model.FetchRowsAsync(0, 100);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(model), Is.EqualTo(new[] { R1, 200, 203, -R1 }));
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(203), "dz keep, the first row of the page shown");
        });
    }

    /// <summary>
    /// A page down past the roots read is read in the grid's fetch, under its spinner: the zoom bar, drawn before it, reads nothing.
    /// </summary>
    [Test]
    public async Task APageDownPastTheRootsReadIsReadUnderTheSpinnerTest()
    {
        using BunitContext ctx = NewContext();
        using var model = NewModel();
        var cut = RenderTree(ctx, model);   // a page of 5: one pass of 10 roots read
        var grid = cut.FindComponent<Grigliaria<Entry>>().Instance;
        Assert.That(model.GetCurrentRowCount(), Is.EqualTo(10));
        List<bool> idle = [];
        _onRootQuery = () => idle.Add(grid.IsIdle());
        await cut.InvokeAsync(() => model.SetCurrentRow(12));   // as MovePage does from row 7
        _onRootQuery = null;
        Assert.That(idle, Is.EqualTo(new[] { false }), "one pass, in the grid's fetch");
    }

    /// <summary>
    /// When the read of an expand runs out of time, the spinner's token stops it, and a host that handles the timeout keeps the
    /// grid up.
    /// </summary>
    [Test]
    public async Task AnExpandReadOutOfTimeTakesTheCancellationTest()
    {
        using BunitContext ctx = NewContext();
        using var model = NewModel();
        List<GridTimeoutException> seen = [];
        var cut = ctx.Render<Treegliaria<Entry>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.PageSize, 5).Add(g => g.RowHeight, 32).Add(g => g.OnTimeout, (GridTimeoutException e) => { seen.Add(e); return true; }));
        var inner = cut.FindComponent<Grigliaria<Entry>>().Instance;
        inner._spinnerHelper = new SpinnerHelper(10, 20, 1);
        _childQueryMs = 150;
        await cut.InvokeAsync(() => cut.Instance.ToggleExpandAsync(RowOf(model, R1)));
        cut.WaitForAssertion(() => Assert.That(seen, Has.Count.EqualTo(1)));
        _childQueryMs = 0;
        Assert.Multiple(() =>
        {
            Assert.That(seen[0].WorkStopped, Is.True, "the read took the cancellation");
            Assert.That(inner.IsIdle(), Is.True);
        });
    }
}
