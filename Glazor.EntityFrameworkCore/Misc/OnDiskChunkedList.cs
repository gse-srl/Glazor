using Glazor.Grds.Mdls;
using MessagePack;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections;
using System.Diagnostics;

namespace Glazor.Misc;

/// <summary>The limits shared by every <see cref="OnDiskChunkedList{T}"/>.</summary>
public class OnDiskChunkedList
{
    /// <summary>The fewest chunks a list may keep in memory.</summary>
    public const int MinChunksInMemory = 3;
    /// <summary>The fewest items a chunk may hold.</summary>
    public const int MinChunkSize = 32;

    private protected OnDiskChunkedList()   // the limits of every OnDiskChunkedList<T>, not a list of its own
    {
    }

    /// <summary>
    /// How <see cref="OnDiskChunkedList{T}.Sort"/> orders the list.
    /// </summary>
    internal enum SortAlgorithm
    {
        /// <summary>
        /// Each chunk is sorted in memory, then all of them are merged in one pass through a heap keyed on each
        /// chunk's next element: a resident chunk is read from memory, a spilled one is streamed from its file one
        /// element at a time, so no chunk is swapped in to be read. Not stable.
        /// </summary>
        ExternalMerge,
    }
}

/// <summary>
/// A list which maintains data in chunks, chunks exceeding the
/// maximum number of in memory chunks are swapped to disk, freeing memory.<br/>
/// It performs well when accessed serially or pseudo-randomly, i.e. at random
/// but in contiguous ranges.<br/>
/// If real random access is needed (like when shuffled), it is better to have more 
/// small chunks in memory than fewer larger chunks.    
/// </summary>
/// <remarks><c>OnDiskChunkedList&lt;T&gt;</c> is designed for scenarios where the collection may grow too large to fit
/// entirely in memory. Chunks are loaded into memory on demand and evicted to disk based on usage patterns, allowing
/// efficient access to large datasets with limited memory consumption. The class is not thread-safe; external
/// synchronization is required for concurrent access. All standard list operations are supported, including sorting and
/// indexed access, except <see cref="CopyTo"/>, which throws rather than load every chunk into memory. A chunk read back from disk holds new instances, so an item kept
/// across its eviction is a copy the list no longer holds, and <see cref="IndexOf"/>, <see cref="Contains"/> and <see cref="Remove"/>,
/// which compare with <see cref="EqualityComparer{T}.Default"/>, find it only when <typeparamref name="T"/> defines its own equality.
/// The enumerator reads by index and does not detect a change made while it runs. Disposing the list releases all resources, including any temporary files used for chunk
/// storage.</remarks>
/// <typeparam name="T">Must be serializable with https://github.com/MessagePack-CSharp/MessagePack-CSharp 
///     ContractlessStandardResolverAllowPrivate</typeparam>
public sealed class OnDiskChunkedList<T> : OnDiskChunkedList, IDisposable, ISortableList<T>
{
    private static readonly ILogger<OnDiskChunkedList<T>> logger = LoggerSupport.Logger<OnDiskChunkedList<T>>();

    private readonly int _chunksInMemory;
    private readonly int _chunkSize;

    private readonly List<Chunk> _chunks = [];
    private readonly List<Chunk> _lruChunks = [];

    private bool _disposed;

    private int _size;
    private int _updCount;      // used to establish when to run CompactChunks()

    private int _currentChunk = -1;
    // Where the first element of the current chunk is in the whole list
    private int _currentChunkBaseIdx = -1;

    private readonly string? _spillDirectory;

    /// <param name="chunksInMemory">The chunks kept in memory, the rest written to disk; at least <see cref="OnDiskChunkedList.MinChunksInMemory"/>.</param>
    /// <param name="chunkSize">The rows per chunk; at least <see cref="OnDiskChunkedList.MinChunkSize"/>, at most <see cref="Array.MaxLength"/> / 9.</param>
    /// <param name="spillDirectory">Where evicted chunks are written; null means the OS temp directory. Created if missing.</param>
    public OnDiskChunkedList(int chunksInMemory, int chunkSize, string? spillDirectory = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksInMemory, MinChunksInMemory);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, MinChunkSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkSize, Array.MaxLength / 9);   // a chunk grows to 9/8 of it before a split
        spillDirectory = spillDirectory == null ? null : Path.GetFullPath(spillDirectory);   // a relative one, against today's directory
        if (spillDirectory != null && OperatingSystem.IsWindows())
            Directory.CreateDirectory(spillDirectory);
        else if (spillDirectory != null)
            Directory.CreateDirectory(spillDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _spillDirectory = spillDirectory;
        _chunks.Add(new Chunk(this, chunkSize));
        _chunkSize = chunkSize;
        _chunksInMemory = chunksInMemory;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Disposing {thisType}", this.GetType());
        foreach (Chunk chunk in _chunks)
        {
            chunk.Dispose();
        }
        _chunks.Clear();
        _lruChunks.Clear();
        _size = 0;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void RangeCheck(int index)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
    }

    /// <inheritdoc/>
    public T this[int index]
    {
        get
        {
            RangeCheck(index);
            MoveCurrentChunk(index);
            Chunk chunk = _chunks[_currentChunk];
            var data = chunk.GetData();
            T res = data[index - _currentChunkBaseIdx];
            return res;
        }
        set
        {
            RangeCheck(index);
            MoveCurrentChunk(index);
            Chunk chunk = _chunks[_currentChunk];
            var data = chunk.GetData();
            data[index - _currentChunkBaseIdx] = value;
        }
    }

    /// <summary>Whether the chunk holding the item at <paramref name="index"/> is in memory, so reading it costs no chunk read.</summary>
    public bool IsInMemory(int index)
    {
        RangeCheck(index);
        MoveCurrentChunk(index);
        Chunk chunk = _chunks[_currentChunk];
        return chunk.IsInMemory();
    }

    /// <summary>
    /// Brings the chunk holding the item at <paramref name="index"/> into memory, reading it back if it was written to disk,
    /// and marks it the most recently used; the least recently used chunk beyond the budget is written to disk.
    /// </summary>
    public void EnsureIsInMemory(int index)
    {
        RangeCheck(index);
        MoveCurrentChunk(index);
        Chunk chunk = _chunks[_currentChunk];
        chunk.EnsureIsInMemory();
    }

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _size;
        }
    }

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public void Add(T item)
    {
        int index = Count;
        MoveCurrentChunk(index);
        Chunk chunk = _chunks[_currentChunk];
        var data = chunk.GetData();
        data.Add(item);
        ++_size;
        ++_updCount;
        CompactChunks();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        ThrowIfDisposed();
        foreach (Chunk c in _chunks)
        {
            c.Dispose();
        }
        _currentChunk = -1;
        _currentChunkBaseIdx = -1;
        _chunks.Clear();
        _chunks.Add(new Chunk(this, _chunkSize));
        _lruChunks.Clear();
        _size = 0;
        _updCount = 0;
    }

    /// <inheritdoc/>
    public bool Contains(T item)
    {
        return Count != 0 && IndexOf(item) >= 0;
    }

    /// <summary>
    /// This is something you should not use because it ends up loading all chunks into memory.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always.</exception>
    [Obsolete("This is something you should not use because it ends up loading all chunks into memory.")]
    public void CopyTo(T[] array, int arrayIndex)
    {
        throw new InvalidOperationException("This is something you should not use because it ends up loading all chunks into memory.");
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    /// <inheritdoc/>
    public int IndexOf(T item)
    {
        // We must go through [] not to load all chunks in memory
        for (int i = 0; i < Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(item, this[i]))
                return i;
        }
        return -1;
    }

    /// <inheritdoc/>
    public void Insert(int index, T item)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);
        MoveCurrentChunk(index);
        Chunk chunk = _chunks[_currentChunk];
        var data = chunk.GetData();
        data.Insert(index - _currentChunkBaseIdx, item);
        ++_size;
        ++_updCount;
        CompactChunks();
    }

    /// <inheritdoc/>
    public bool Remove(T item)
    {
        for (int i = 0; i < Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(item, this[i]))
            {
                RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        RangeCheck(index);
        MoveCurrentChunk(index);
        Chunk chunk = _chunks[_currentChunk];
        var data = chunk.GetData();
        data.RemoveAt(index - _currentChunkBaseIdx);
        --_size;
        ++_updCount;
        CompactChunks();
    }

    /// <summary>
    /// Algorithm used by <see cref="Sort"/>; see <see cref="OnDiskChunkedList.SortAlgorithm"/>. Default <see cref="OnDiskChunkedList.SortAlgorithm.ExternalMerge"/>.
    /// </summary>
    internal SortAlgorithm SortMode { get; init; } = SortAlgorithm.ExternalMerge;

    /// <summary>
    /// Sorts in place. The resident chunks stay in memory until the merge ends while its output fills the LRU beside them, so the
    /// residency budget may be doubled while it runs.
    /// </summary>
    public void Sort(IComparer<T> comparer)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(comparer);
        long st = Stopwatch.GetTimestamp();
        bool merge = SortMode == SortAlgorithm.ExternalMerge;
        if (merge)
            ExternalMergeSort(comparer);
        else
            throw new InvalidOperationException($"Unexpected {SortMode}");
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Sorted {count} elements in {chunks} chunks with {algorithm} in {elapsedTime}ms",
                _size, _chunks.Count, SortMode, Stopwatch.GetElapsedTime(st).TotalMilliseconds);
    }

    private void ExternalMergeSort(IComparer<T> comparer)
    {
        // 1. Sort each chunk in memory. Resident chunks stay where they are; the LRU evicts the older ones as the
        //    next load, so every spilled chunk ends up on disk sorted.
        foreach (Chunk chunk in _chunks)
            chunk.GetData().Sort(comparer);
        // 2. One reader per chunk, and a heap that always yields the reader whose next element is the smallest.
        List<ChunkReader> readers = new(_chunks.Count);
        List<Chunk> output = new(_chunks.Count);
        try
        {
            PriorityQueue<ChunkReader, T> heap = new(_chunks.Count, comparer);
            foreach (Chunk chunk in _chunks)
            {
                _lruChunks.Remove(chunk);   // leave the LRU so that the output chunks registered below cannot evict them mid-read
                ChunkReader reader = new(chunk);
                readers.Add(reader);
                if (reader.TryPeek(out T head))
                    heap.Enqueue(reader, head);
            }
            // 3. Pop, append, re-enqueue with the reader's new head. Output chunks go into the LRU like any other,
            //    so the memory budget writes them out as usual.
            List<T> buffer = new(_chunkSize);
            while (heap.TryDequeue(out ChunkReader? reader, out T? top))
            {
                buffer.Add(top!);   // TryDequeue returned true, so the pair is populated
                reader.Advance();
                if (reader.TryPeek(out T next))
                    heap.Enqueue(reader, next);
                if (buffer.Count >= _chunkSize)
                {
                    output.Add(NewOutputChunk(buffer));
                    buffer = new(_chunkSize);
                }
            }
            if (buffer.Count > 0)
                output.Add(NewOutputChunk(buffer));
            if (output.Count == 0)
                output.Add(new Chunk(this, _chunkSize));   // an empty list still has one chunk: MoveCurrentChunk relies on it
        }
        catch
        {
            // The output chunks are only in the LRU and in output, where Dispose would not find their files
            foreach (Chunk chunk in _lruChunks.Except(_chunks).Concat(output).ToList())
            {
                _lruChunks.Remove(chunk);
                chunk.Dispose();
            }
            // the resident inputs the merge took out of the LRU go back at its end, so the next load evicts them as usual
            _lruChunks.AddRange(_chunks.Where(c => c.IsInMemory() && !_lruChunks.Contains(c)).ToList());
            throw;
        }
        finally
        {
            foreach (ChunkReader reader in readers)
                reader.Dispose();
        }
        foreach (Chunk chunk in _chunks)
            chunk.Dispose();
        _chunks.Clear();
        _chunks.AddRange(output);
        _currentChunk = -1;
        _currentChunkBaseIdx = -1;
        _updCount = 0;
    }

    private Chunk NewOutputChunk(List<T> data)
    {
        Chunk chunk = new(this, data);
        SwapChunks(chunk);
        return chunk;
    }

    /// <summary>
    /// Reads one sorted chunk front to back: from its list when resident, otherwise element by element from its
    /// file, so a spilled chunk is never loaded whole to be merged.
    /// </summary>
    private sealed class ChunkReader : IDisposable
    {
        private readonly List<T>? _data;
        private int _pos;

        private readonly FileStream? _stream;
        private readonly MessagePackStreamReader? _reader;
        private int _remaining;

        private bool _hasHead;
        private T _head = default!;

        internal ChunkReader(Chunk chunk)
        {
            if (chunk.IsInMemory())
            {
                _data = chunk.ResidentData;
            }
            else
            {
                _stream = File.OpenRead(chunk.FilePath.RequireNotNull());
                _reader = new MessagePackStreamReader(_stream, leaveOpen: false);
                _remaining = Await(_reader.ReadArrayHeaderAsync(CancellationToken.None));
            }
        }

        public bool TryPeek(out T item)
        {
            if (!_hasHead)
                _hasHead = ReadNext(out _head);
            item = _head;
            return _hasHead;
        }

        public void Advance() => _hasHead = false;

        private bool ReadNext(out T item)
        {
            if (_data != null)
            {
                if (_pos >= _data.Count)
                {
                    item = default!;
                    return false;
                }
                item = _data[_pos++];
                return true;
            }
            if (_remaining <= 0)
            {
                item = default!;
                return false;
            }
            ReadOnlySequence<byte>? msg = Await(_reader!.ReadAsync(CancellationToken.None));
            if (msg == null)
                throw new InvalidOperationException("Chunk file ended before its declared element count");
            item = MessagePackSerializer.Deserialize<T>(msg.Value, MessagePackConf.Options);
            --_remaining;
            return true;
        }

        // Sort is synchronous and runs off the UI thread; the reader's async calls complete synchronously
        // whenever its buffer already holds the next element, which is the common case.
        private static TResult Await<TResult>(ValueTask<TResult> task)
            => task.IsCompletedSuccessfully ? task.Result : task.AsTask().GetAwaiter().GetResult();

        public void Dispose()
        {
            _reader?.Dispose();
            _stream?.Dispose();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void MoveCurrentChunk(int index)
    {
        int chunkIdx = _currentChunk < 0 ? 0 : _currentChunk;
        int chunkBase = _currentChunkBaseIdx < 0 ? 0 : _currentChunkBaseIdx;
        if (index >= chunkBase)
        {
            for (int i = chunkIdx; i < _chunks.Count; i++)
            {
                Chunk chunk = _chunks[i];
                if (index < (chunkBase + chunk.GetSize()) || i == (_chunks.Count - 1))
                {
                    _currentChunk = i;
                    _currentChunkBaseIdx = chunkBase;
                    return;
                }
                chunkBase += chunk.GetSize();
            }
            throw new InvalidOperationException("Unexpected");
        }
        else
        {
            for (int i = chunkIdx - 1; i >= 0; i--)
            {
                Chunk chunk = _chunks[i];
                chunkBase -= chunk.GetSize();
                if (index >= chunkBase)
                {
                    _currentChunk = i;
                    _currentChunkBaseIdx = chunkBase;
                    return;
                }
            }
            throw new InvalidOperationException("Unexpected");
        }
    }

    private void SwapChunks(Chunk accessedChunk)
    {
        // refresh the LRU
        _lruChunks.Remove(accessedChunk);
        _lruChunks.Insert(0, accessedChunk);
        // swap to disk exceeding chunks
        while (_lruChunks.Count > _chunksInMemory)
        {
            int idx = _lruChunks.Count - 1;
            Chunk chunk = _lruChunks[idx];
            chunk.EnsureIsOnDisk();   // a write that fails leaves it in the LRU, to be written at the next load
            _lruChunks.RemoveAt(idx);
        }
    }

    private void CompactChunks()
    {
        if (_updCount < (_chunkSize / 8))
            return;
        _updCount = 0;
        // 1. remove empty ones
        if (_chunks.Count > 1)
        {
            for (int i = _chunks.Count - 1; i >= 0; i--)
            {
                Chunk chunk = _chunks[i];
                if (chunk.GetSize() <= 0)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("Removing empty chunk at: {idx}", i);
                    _chunks.RemoveAt(i);
                    _lruChunks.Remove(chunk);
                    if (i <= _currentChunk)
                    {
                        _currentChunk = -1;
                        _currentChunkBaseIdx = -1;
                    }
                    // else nothing to do, the removed chunk is empty
                    chunk.Dispose();
                }
            }
        }
        // 2. split big ones
        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            Chunk chunk = _chunks[i];
            if (chunk.GetSize() > (_chunkSize * 9 / 8))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Splitting chunk at: {idx} of {size} elements", i, chunk.GetSize());
                _currentChunk = -1;
                _currentChunkBaseIdx = -1;
                List<T> data = chunk.GetData();
                List<Chunk> added = [];
                for (int startFrom = _chunkSize; startFrom < data.Count; startFrom += _chunkSize)
                {
                    int newChunkSize = Math.Min(data.Count - startFrom, _chunkSize);
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("... adding chunk at: {idx} of {size} elements", i + added.Count + 1, newChunkSize);
                    added.Add(new Chunk(this, data.GetRange(startFrom, newChunkSize)));
                }
                data.RemoveRange(_chunkSize, data.Count - _chunkSize);   // before an eviction can write this chunk
                _chunks.InsertRange(i + 1, added);
                foreach (Chunk newChunk in added)
                    SwapChunks(newChunk);   // resident like any other chunk; the budget decides what goes to disk
            }
        }
        // 3. collapse chunks
        for (int i = _chunks.Count - 2; i >= 0; i--)
        {
            Chunk chunk = _chunks[i];
            Chunk nextChunk = _chunks[i + 1];
            if ((chunk.GetSize() + nextChunk.GetSize()) <= _chunkSize)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Collapsing chunks at: {idx} and {idx2}", i, i + 1);
                _currentChunk = -1;
                _currentChunkBaseIdx = -1;
                List<T> data = chunk.GetData();
                data.AddRange(nextChunk.GetData());
                _chunks.RemoveAt(i + 1);
                _lruChunks.Remove(nextChunk);
                nextChunk.Dispose();
            }
        }
    }

    internal sealed class Chunk : IDisposable
    {
        private readonly OnDiskChunkedList<T> _chunkedList;

        private List<T>? _data;  // when null the chunk is NOT in memory
        private int? _size;     // when null the chunk is in memory

        private TempFile? _file;

        /// <summary>The data while resident, without touching the LRU; null when spilled.</summary>
        internal List<T>? ResidentData => _data;

        /// <summary>The spill file while one exists; null for a chunk never evicted.</summary>
        internal string? FilePath => _file?.Path;

        /// <summary>Takes ownership of <paramref name="data"/>, no copy.</summary>
        internal Chunk(OnDiskChunkedList<T> chunkedList, List<T> data)
        {
            _chunkedList = chunkedList;
            _data = data;
        }

        internal Chunk(OnDiskChunkedList<T> chunkedList, int initialCapacity)
        {
            _chunkedList = chunkedList;
            _data = new(initialCapacity);
        }

        public int GetSize()
        {
            if (_size != null && _data != null)
                throw new InvalidOperationException();
            if (_size != null)
                return (int)_size;
            return _data.RequireNotNull().Count;
        }

        public bool IsInMemory()
        {
            if (_size != null && _data != null)
                throw new InvalidOperationException();
            return _data != null;
        }

        public List<T> GetData()
        {
            EnsureIsInMemory();
            return _data.RequireNotNull();
        }

        internal void EnsureIsInMemory()
        {
            if (_data != null)
            {
                _chunkedList.SwapChunks(this);
                return;
            }
            if (_size == null)
                throw new InvalidOperationException();
            if (_file == null)
                throw new InvalidOperationException();
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Retrieving a chunk of {size} elements from file: {fileName}", _size, _file.Path);
            using (var rs = File.OpenRead(_file.Path))
            {
                List<T> data = MessagePackSerializer.Deserialize<List<T>>(rs, MessagePackConf.Options);
                if (_size != data.Count)
                    throw new InvalidOperationException($"Unexpected {_size} != {data.Count}");
                _data = data;
                _size = null;
            }
            _chunkedList.SwapChunks(this);
        }

        internal void EnsureIsOnDisk()
        {
            if (_data == null)
                return;
            _file ??= _chunkedList._spillDirectory == null ? new TempFile()
                : new TempFile(Path.Combine(_chunkedList._spillDirectory, Path.GetRandomFileName()));
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Writing a chunk of {size} elements to file: {fileName}", _data.Count, _file.Path);
            FileStreamOptions options = new() { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            // The rows are plaintext: on Unix only the owner may read them, as GetTempFileName already does in the temp directory
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var ws = new FileStream(_file.Path, options))
            {
                MessagePackSerializer.Serialize<List<T>>(ws, _data, MessagePackConf.Options);
            }
            _size = _data.Count;
            _data = null;
        }

        public void Dispose()
        {
            _data = null;
            _size = null;
            _file?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
