using Microsoft.Extensions.Logging;

namespace Glazor.Misc;

/// <summary>
/// A file that is removed when disposed.
/// </summary>
internal sealed class TempFile : IDisposable
{
    private static readonly ILogger<TempFile> logger = LoggerSupport.Logger<TempFile>();

    private static int _lastId = 0;

    private readonly int _id;
    private string _path;

    public TempFile() : this(System.IO.Path.GetTempFileName())
    {
    }

    public TempFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _id = Interlocked.Increment(ref _lastId);
        _path = path;
    }

    public string Path
    {
        get
        {
            ObjectDisposedException.ThrowIf(_path == null, this);
            return _path;
        }
    }

    ~TempFile()
    {
        DeleteFile(false);
    }

    public void Dispose()
    {
        DeleteFile(true);
        GC.SuppressFinalize(this);
    }

    private void DeleteFile(bool disposing)
    {
        if (_path != null)
        {
            // The finalizer does not log: a logging provider already disposed would throw there, and that ends the process
            if (disposing && logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Removing file: {path} id: {id}", _path, _id);
            try
            {
                File.Delete(_path);
            }
            catch (Exception e)
            {
                // best effort
                if (disposing)
                    logger.LogWarning("While deleting temporary file: {path} id: {id} error-message: {msg}", _path, _id, e.Message);
            }
            _path = null!;
        }
    }

    public override string? ToString()
    {
        return $"{base.ToString()} id: {_id} path: {_path}";
    }
}
