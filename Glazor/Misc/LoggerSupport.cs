using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glazor.Misc;

/// <summary>
/// Used to enable logging in some Glazor classes
/// </summary>
public static class LoggerSupport
{
    private static ILoggerFactory? _loggerFactory;
    private static int _version;

    /// <summary>Routes the library's own logging to <paramref name="loggerFactory"/>; call once at startup. Until then it logs nothing.</summary>
    public static void SetLoggerFactory(ILoggerFactory loggerFactory)
    {
        Volatile.Write(ref _loggerFactory, loggerFactory.RequireNotNull());
        // Bumped after the factory is in place, so a reader that observes the new version observes it too.
        Interlocked.Increment(ref _version);
    }

    /// <summary>A logger for <typeparamref name="T"/> that writes through the factory set when it logs.</summary>
    public static ILogger<T> Logger<T>() => new Forwarding<T>();

    /// <summary>
    /// Resolves against whatever factory is current when a message is written. A logger taken before
    /// <see cref="SetLoggerFactory"/>, as one held in a static field is, would otherwise stay bound to
    /// <see cref="NullLogger{T}"/> for the life of the process.
    /// </summary>
    private sealed class Forwarding<T> : ILogger<T>
    {
        private sealed record Resolved(int Version, ILogger Logger);

        private Resolved _resolved = new(-1, NullLogger<T>.Instance);

        private ILogger Current()
        {
            int version = Volatile.Read(ref _version);
            Resolved resolved = Volatile.Read(ref _resolved);
            if (resolved.Version != version)
            {
                resolved = new Resolved(version, _loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance);
                Volatile.Write(ref _resolved, resolved);
            }
            return resolved.Logger;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => Current().BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => Current().IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            => Current().Log(logLevel, eventId, state, exception, formatter);
    }
}
