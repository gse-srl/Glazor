using Glazor.Misc;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Glazor.Tests;

/// <summary>
/// <see cref="OnDiskChunkedList{T}"/> spills chunks to temporary files once more than
/// <c>chunksInMemory</c> of them exist, and reads them back on demand. The whole point is the round trip
/// through the file system, so these tests assert that a chunk really left memory before reading it back —
/// otherwise they would pass without ever touching the code that matters.
/// </summary>
public class OnDiskChunkedListTests : IDisposable
{
    private readonly Logger logger;

    public OnDiskChunkedListTests()
    {
        logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();
        LoggerSupport.SetLoggerFactory(new SerilogLoggerFactory(logger));
    }

    void IDisposable.Dispose()
    {
        GC.SuppressFinalize(this);
        logger.Dispose();
    }

    [SetUp]
    public void Setup()
    {
    }

    private const int ChunksInMemory = OnDiskChunkedList.MinChunksInMemory;   // 3
    private const int ChunkSize = OnDiskChunkedList.MinChunkSize;              // 32
    // Enough to need more chunks than fit in memory.
    private const int ItemCount = ChunkSize * (ChunksInMemory + 3);

    private static string Item(int i) => $"item-{i:D4}";

    [Test]
    public void SpillAndReloadTest()
    {
        using OnDiskChunkedList<string> list = new(ChunksInMemory, ChunkSize);
        for (int i = 0; i < ItemCount; i++)
            list.Add(Item(i));

        Assert.That(list.Count, Is.EqualTo(ItemCount));

        // Reading every element in order walks past more chunks than fit in memory, so the earliest ones get
        // evicted to disk behind us.
        for (int i = 0; i < ItemCount; i++)
            Assert.That(list[i], Is.EqualTo(Item(i)), $"at index {i}");

        Assert.That(list.IsInMemory(0), Is.False,
            "the first chunk should have been evicted to disk; without that this test proves nothing");

        // Walking back reloads them from disk, which is the round trip under test.
        for (int i = ItemCount - 1; i >= 0; i--)
            Assert.That(list[i], Is.EqualTo(Item(i)), $"at index {i} on the way back");
    }

    public sealed class DatedRow
    {
        public DateTime Local { get; set; }
        public DateTime Utc { get; set; }
        public DateTime? Unspecified { get; set; }
        public DateTime? Missing { get; set; }
        public DateTime[] Array { get; set; } = [];
        public List<DateTime> List { get; set; } = [];
    }

    private static readonly DateTime Moment = new(2026, 11, 3, 10, 30, 0);

    /// <summary>
    /// A row read back from disk keeps each <see cref="DateTime.Kind"/>, which MessagePack's own timestamp drops: in a plain,
    /// a nullable, an array and a list member.
    /// </summary>
    [Test]
    public void SpilledRowKeepsDateTimeKindTest()
    {
        using OnDiskChunkedList<DatedRow> list = new(ChunksInMemory, ChunkSize);
        DateTime local = DateTime.SpecifyKind(Moment, DateTimeKind.Local), utc = DateTime.SpecifyKind(Moment, DateTimeKind.Utc);
        for (int i = 0; i < ItemCount; i++)
            list.Add(new DatedRow { Local = local, Utc = utc, Unspecified = Moment, Array = [local, utc, Moment], List = [utc, local] });
        for (int i = 0; i < ItemCount; i++)
            _ = list[i];
        Assert.That(list.IsInMemory(0), Is.False, "the first chunk should have been evicted to disk");

        DatedRow row = list[0];
        Assert.Multiple(() =>
        {
            Assert.That((row.Local, row.Local.Kind), Is.EqualTo((local, DateTimeKind.Local)));
            Assert.That((row.Utc, row.Utc.Kind), Is.EqualTo((utc, DateTimeKind.Utc)));
            Assert.That((row.Unspecified, row.Unspecified!.Value.Kind), Is.EqualTo(((DateTime?)Moment, DateTimeKind.Unspecified)));
            Assert.That(row.Missing, Is.Null);
            Assert.That(row.Array.Select(d => d.Kind), Is.EqualTo(new[] { DateTimeKind.Local, DateTimeKind.Utc, DateTimeKind.Unspecified }));
            Assert.That(row.List.Select(d => d.Kind), Is.EqualTo(new[] { DateTimeKind.Utc, DateTimeKind.Local }));
        });
    }

    /// <summary>
    /// A chunk that is modified after having been written must be written again correctly — including when the
    /// new contents are shorter than the old, which is only true if the write truncates the existing file.
    /// </summary>
    [Test]
    public void RewriteSpilledChunkTest()
    {
        using OnDiskChunkedList<string> list = new(ChunksInMemory, ChunkSize);
        for (int i = 0; i < ItemCount; i++)
            list.Add(Item(i));

        // Walk once so the early chunks have been written out at their full size.
        for (int i = 0; i < ItemCount; i++)
            _ = list[i];

        // Bring the first chunk back and replace two values with much shorter ones, so the next write of that
        // chunk is smaller than the file already on disk.
        list[0] = "x";
        list[1] = "y";

        // Walk away again so the modified chunk is re-written, then come back and read it.
        for (int i = 0; i < ItemCount; i++)
            _ = list[i];

        Assert.That(list.IsInMemory(0), Is.False, "the modified chunk should have been written out again");
        Assert.That(list[0], Is.EqualTo("x"));
        Assert.That(list[1], Is.EqualTo("y"));
        Assert.That(list[2], Is.EqualTo(Item(2)));
        // Nothing after the modified chunk may have been disturbed.
        Assert.That(list[ItemCount - 1], Is.EqualTo(Item(ItemCount - 1)));
        Assert.That(list.Count, Is.EqualTo(ItemCount));
    }

    /// <summary>
    /// ToList() over the read-only view calls CopyTo, which would hold every row at once: the list refuses it, spilled chunks
    /// or not.
    /// </summary>
    [Test]
    public void ChunkedListRejectsCopyToThroughAsReadOnlyTest()
    {
        const int chunksInMemory = OnDiskChunkedList.MinChunksInMemory;
        const int chunkSize = OnDiskChunkedList.MinChunkSize;
        const int count = chunkSize * (chunksInMemory + 3);
        using OnDiskChunkedList<string> list = new(chunksInMemory, chunkSize);
        for (int i = 0; i < count; i++)
            list.Add($"item-{i:D4}");
        IReadOnlyList<string> dataView = list.AsReadOnly();
        Assert.Throws<InvalidOperationException>(() => dataView.ToList(),
            "CopyTo() through the read-only wrapper must be rejected, not silently ignored");
    }

    /// <summary>
    /// The spill location is a data-at-rest decision for the host, so the directory it names must be the one
    /// that receives the chunks, and disposing the list must leave nothing behind in it.
    /// </summary>
    [Test]
    public void SpillDirectoryReceivesTheChunksAndIsEmptiedOnDisposeTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), "glazor-spill-" + Path.GetRandomFileName());
        try
        {
            using (OnDiskChunkedList<string> list = new(ChunksInMemory, ChunkSize, dir))
            {
                for (int i = 0; i < ItemCount; i++)
                    list.Add(Item(i));
                for (int i = 0; i < ItemCount; i++)
                    _ = list[i];

                Assert.That(list.IsInMemory(0), Is.False, "a chunk must have been evicted, or this proves nothing");
                Assert.That(Directory.GetFiles(dir), Is.Not.Empty, "evicted chunks are written under the given directory");
                Assert.That(list[0], Is.EqualTo(Item(0)), "and read back from there");
            }
            Assert.That(Directory.GetFiles(dir), Is.Empty, "disposing the list deletes its spilled chunks");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A model reloads by disposing its list and a fetch that ran past the spinner budget may still hold it, so the
    /// failure a late writer or reader gets must say what happened rather than be a NullReferenceException from a
    /// field the dispose cleared.
    /// </summary>
    [Test]
    public void UseAfterDisposeThrowsObjectDisposedExceptionTest()
    {
        OnDiskChunkedList<string> list = new(ChunksInMemory, ChunkSize);
        list.Add(Item(0));
        list.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(() => list[0], Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => list.Add(Item(1)), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => list.Count, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => list.Sort(Comparer<string>.Default), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => list.Dispose(), Throws.Nothing, "disposing twice is harmless");
        });
    }

    /// <summary>
    /// A chunk split off a full one stays in memory like any other and is written only when the budget is exceeded.
    /// </summary>
    [Test]
    public void SplitOffChunksStayWithinTheMemoryBudgetTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            using (OnDiskChunkedList<int> roomy = new(32, 100, dir))
            {
                for (int i = 0; i < 500; i++)
                    roomy.Add(i);
                Assert.That(Directory.GetFiles(dir), Is.Empty, "five chunks, room for thirty-two: nothing written");
                Assert.That(Enumerable.Range(0, 500).All(roomy.IsInMemory), Is.True);
            }
            using (OnDiskChunkedList<int> tight = new(3, 100, dir))
            {
                for (int i = 0; i < 500; i++)
                    tight.Add(i);
                Assert.Multiple(() =>
                {
                    Assert.That(Directory.GetFiles(dir), Has.Length.EqualTo(2), "the two chunks beyond the budget, and only those");
                    Assert.That(tight.IsInMemory(450), Is.True, "the newest chunk is resident");
                    Assert.That(tight.IsInMemory(50), Is.False, "the oldest was evicted to make room");
                    Assert.That(tight[50], Is.EqualTo(50), "and reads back");
                });
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A relative spill directory is the one the current directory named when the list was made: a later change of directory
    /// does not move the chunks the list writes.
    /// </summary>
    [Test]
    public void RelativeSpillDirectoryStaysWhereTheListWasMadeTest()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string other = Path.Combine(root, "other");
        Directory.CreateDirectory(other);
        string cwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            using OnDiskChunkedList<int> list = new(3, 100, "spill");
            Environment.CurrentDirectory = other;
            for (int i = 0; i < 500; i++)
                list.Add(i);
            Assert.Multiple(() =>
            {
                Assert.That(Directory.GetFiles(Path.Combine(root, "spill")), Has.Length.EqualTo(2), "the two chunks beyond the budget");
                Assert.That(Directory.Exists(Path.Combine(other, "spill")), Is.False);
                Assert.That(list[50], Is.EqualTo(50), "and they read back");
            });
        }
        finally
        {
            Environment.CurrentDirectory = cwd;
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A chunk file that reads back with another count than was written, rewritten under the list, fails the read that needs it
    /// with that count and leaves the chunk on disk: the other chunks read on.
    /// </summary>
    [Test]
    public void ChunkFileWithTheWrongCountLeavesTheListUsableTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            using OnDiskChunkedList<int> list = new(3, 100, dir);
            for (int i = 0; i < 500; i++)
                list.Add(i);
            foreach (string file in Directory.GetFiles(dir))
            {
                using FileStream ws = new(file, FileMode.Create);
                MessagePack.MessagePackSerializer.Serialize(ws, new List<int> { 1, 2, 3 }, MessagePackConf.Options);
            }
            Assert.That(() => list[50], Throws.InvalidOperationException.With.Message.Contains("100 != 3"));
            Assert.Multiple(() =>
            {
                Assert.That(list.IsInMemory(50), Is.False, "the chunk stays on disk");
                Assert.That(list[450], Is.EqualTo(450), "the other chunks read on");
            });
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A sort whose merge fails, here a comparer that throws once the merge has written chunks, leaves no spill file behind
    /// after Dispose: the output chunks it had made are not yet in the list's own chunks.
    /// </summary>
    [Test]
    public void FailedSortLeavesNoSpillFilesTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            int crossChunk = 0;
            IComparer<int> failing = Comparer<int>.Create((a, b) =>
            {
                if (a / 1000 != b / 1000 && ++crossChunk > 1000)   // only the merge compares values of two chunks
                    throw new InvalidOperationException("comparer failed");
                return a.CompareTo(b);
            });
            using (OnDiskChunkedList<int> list = new(ChunksInMemory, ChunkSize, dir))
            {
                for (int c = 0; c < 12; c++)
                    for (int k = ChunkSize - 1; k >= 0; k--)
                        list.Add(c * 1000 + k);
                Assert.Throws<InvalidOperationException>(() => list.Sort(failing));
            }
            Assert.That(Directory.GetFiles(dir), Is.Empty);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// A sort whose merge fails leaves the list within its residency budget: the chunks that were resident when it started go back
    /// under the LRU, and reading other chunks writes them out.
    /// </summary>
    [Test]
    public void FailedSortKeepsTheResidencyBudgetTest()
    {
        int crossChunk = 0;
        IComparer<int> failing = Comparer<int>.Create((a, b) =>
        {
            if (a / 1000 != b / 1000 && ++crossChunk > 100)   // only the merge compares values of two chunks
                throw new InvalidOperationException("comparer failed");
            return a.CompareTo(b);
        });
        using OnDiskChunkedList<int> list = new(ChunksInMemory, ChunkSize);
        const int chunks = 12;
        for (int c = 0; c < chunks; c++)
            for (int k = ChunkSize - 1; k >= 0; k--)
                list.Add(c * 1000 + k);
        Assert.Throws<InvalidOperationException>(() => list.Sort(failing));
        for (int i = 0; i < 9 * ChunkSize; i++)   // the chunks that were not resident when the sort started
            _ = list[i];
        int resident = Enumerable.Range(0, chunks).Count(c => list.IsInMemory(c * ChunkSize));
        Assert.That(resident, Is.LessThanOrEqualTo(ChunksInMemory));
    }

    /// <summary>
    /// A chunk whose write to disk fails, here because the spill directory is gone, stays under the LRU: the next load writes
    /// it out, and the list keeps its residency budget.
    /// </summary>
    [Test]
    public void FailedSpillKeepsTheResidencyBudgetTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            using OnDiskChunkedList<string> list = new(ChunksInMemory, ChunkSize, dir);
            int i = 0;
            for (int failure = 0; failure < 4; failure++)
            {
                Directory.Delete(dir, true);
                Assert.Throws(Is.InstanceOf<IOException>(), () => { for (; ; i++) list.Add(Item(i)); });
                Directory.CreateDirectory(dir);
            }
            for (int k = 0; k < ChunkSize * 6; k++, i++)
                list.Add(Item(i));
            int residentRows = Enumerable.Range(0, list.Count).Count(list.IsInMemory);
            Assert.That(residentRows, Is.LessThanOrEqualTo(ChunksInMemory * ChunkSize));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
