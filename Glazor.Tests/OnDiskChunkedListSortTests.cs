using Glazor.Misc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using static Glazor.Misc.OnDiskChunkedList;

namespace Glazor.Tests;

/// <summary>
/// </summary>
[Category("Performance")]
public class OnDiskChunkedListSortTests
{
    /// <summary>
    /// Carries a DateTime on purpose: the library's DateTime formatter must produce a single msgpack value for the
    /// streaming reader in the merge to delimit rows correctly, and this is where that is exercised.
    /// </summary>
    public sealed class Row
    {
        public int Key { get; set; }
        public int Seq { get; set; }
        public string Text { get; set; } = "";
        public DateTime When { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>Counts the library's own debug lines: one per chunk read back from disk, one per chunk written.</summary>
    private sealed class Counter : ILogger, ILoggerProvider, ILoggerFactory
    {
        public int ChunkReads, ChunkWrites;

        public void Reset() => ChunkReads = ChunkWrites = 0;
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Dispose() { }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            if (message.StartsWith("Retrieving a chunk")) ChunkReads++;
            else if (message.StartsWith("Writing a chunk")) ChunkWrites++;
        }
    }

    private static readonly IComparer<Row> ByKey = Comparer<Row>.Create((a, b) => a.Key.CompareTo(b.Key));

    private static OnDiskChunkedList<Row> Build(int rows, int chunkSize, int chunksInMemory, SortAlgorithm mode, int seed)
    {
        Random rnd = new(seed);
        OnDiskChunkedList<Row> list = new(chunksInMemory, chunkSize) { SortMode = mode };
        for (int i = 0; i < rows; i++)
            list.Add(new Row { Key = rnd.Next(rows / 4), Seq = i, Text = $"row-{i:D6}", When = DateTime.UnixEpoch.AddSeconds(i), Amount = i * 1.25m });
        return list;
    }

    /// <summary>Sorted by key, and still holding every row exactly once (Seq is unique per row).</summary>
    private static void AssertSortedPermutation(OnDiskChunkedList<Row> list, int rows)
    {
        Assert.That(list.Count, Is.EqualTo(rows));
        bool[] seen = new bool[rows];
        int prevKey = int.MinValue;
        for (int i = 0; i < rows; i++)
        {
            Row cur = list[i];
            if (cur.Key < prevKey)
                Assert.Fail($"out of order at {i}: {prevKey} then {cur.Key}");
            if (seen[cur.Seq])
                Assert.Fail($"row {cur.Seq} appears twice");
            seen[cur.Seq] = true;
            prevKey = cur.Key;
        }
    }

    [TestCase(5_000, 100, 3)]
    [TestCase(20_000, 256, 8)]
    [TestCase(50_000, 1024, 32)]
    public void SortProducesASortedPermutationTest(int rows, int chunkSize, int chunksInMemory)
    {
        using OnDiskChunkedList<Row> list = Build(rows, chunkSize, chunksInMemory, SortAlgorithm.ExternalMerge, 42);
        list.Sort(ByKey);
        AssertSortedPermutation(list, rows);
    }

    /// <summary>
    /// Sorting an empty list must leave the one-chunk invariant intact, otherwise the next Add or Insert throws
    /// from MoveCurrentChunk.
    /// </summary>
    [Test]
    public void SortingAnEmptyListKeepsItUsableTest()
    {
        using OnDiskChunkedList<Row> list = new(3, 32);
        list.Sort(ByKey);
        Assert.That(list.Count, Is.EqualTo(0));
        list.Add(new Row { Key = 1, Seq = 0 });
        list.Insert(0, new Row { Key = 0, Seq = 1 });
        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0].Key, Is.EqualTo(0));
        Assert.That(list[1].Key, Is.EqualTo(1));
    }

    /// <summary>
    /// The list is usable after the merge: the chunk structure it rebuilt must answer the indexer, survive
    /// eviction and reload, and accept more rows.
    /// </summary>
    [Test]
    public void ListIsFullyUsableAfterExternalMergeTest()
    {
        const int rows = 4_000, chunkSize = 100, chunksInMemory = 3;
        using OnDiskChunkedList<Row> list = Build(rows, chunkSize, chunksInMemory, SortAlgorithm.ExternalMerge, 7);
        list.Sort(ByKey);
        // Walk forward and back, which evicts and reloads the rebuilt chunks.
        for (int i = 0; i < rows; i++) _ = list[i];
        for (int i = rows - 1; i >= 0; i--) _ = list[i];
        AssertSortedPermutation(list, rows);
        list.Add(new Row { Key = int.MaxValue, Seq = rows, Text = "last" });
        Assert.That(list.Count, Is.EqualTo(rows + 1));
        Assert.That(list[rows].Text, Is.EqualTo("last"));
    }

    /// <summary>
    /// The measurement: the chunk files read back whole and written by the external merge under a memory budget the list does
    /// not fit in, which is where the time goes. Its streaming reads are not chunk loads and are not counted; its writes are.
    /// Bounded, not pinned: every input chunk is loaded at most once for its in-place sort, and every chunk written at most
    /// twice, once as an input evicted and once as an output.
    /// </summary>
    [TestCase(5_000, 100, 3)]
    [TestCase(20_000, 256, 8)]
    [TestCase(50_000, 1024, 32)]
    public void ExternalMergeSortChunkTrafficTest(int rows, int chunkSize, int chunksInMemory)
    {
        Counter counter = new();
        LoggerSupport.SetLoggerFactory(counter);
        using OnDiskChunkedList<Row> list = Build(rows, chunkSize, chunksInMemory, SortAlgorithm.ExternalMerge, 42);
        int chunks = (rows + chunkSize - 1) / chunkSize;
        counter.Reset();
        Stopwatch sw = Stopwatch.StartNew();
        list.Sort(ByKey);
        sw.Stop();
        TestContext.Out.WriteLine(
            $"rows={rows,6} chunk={chunkSize,5} inMemory={chunksInMemory,2} chunks={chunks,3}: " +
            $"{sw.ElapsedMilliseconds,7} ms  chunkLoads={counter.ChunkReads,7}  chunkWrites={counter.ChunkWrites,7}");
        Assert.Multiple(() =>
        {
            Assert.That(counter.ChunkReads, Is.LessThanOrEqualTo(chunks), "each input is loaded at most once");
            Assert.That(counter.ChunkWrites, Is.LessThanOrEqualTo(2 * chunks), "each chunk is written at most twice");
        });
        AssertSortedPermutation(list, rows);
    }
}
