using Microsoft.Extensions.Logging;

namespace Glazor.Misc;

/// <summary>
/// Defines methods to control the visibility of a spinner or loading indicator in a user interface.
/// </summary>
/// <remarks>Implementations should ensure that methods can be called safely from any thread, including non-Blazor
/// threads. When updating UI state from a background thread, use appropriate mechanisms such as InvokeAsync to marshal
/// calls to the UI thread.</remarks>
public interface ISpinner
{
    /// <summary>
    /// This could be invoked even from a not blazor thread, typically use InvokeAsync() when implementing it.
    /// </summary>
    /// <returns>true if it has been shown now</returns>
    public bool Show();

    /// <summary>
    /// This should be callable even from a not blazor thread, typically use InvokeAsync() when implementing it.
    /// </summary>
    public void Hide();
}

/// <summary>
/// Provides helper methods for monitoring asynchronous tasks and managing spinner UI elements to indicate ongoing
/// operations.
/// </summary>
/// <remarks>SpinnerHelper is designed to be used from the UI thread or context. Using it outside of a UI context
/// may result in unpredictable behavior. This class tracks running tasks and coordinates the display of spinner
/// indicators to inform users of background activity.</remarks>
internal class SpinnerHelper
{
    private static readonly ILogger<SpinnerHelper> logger = LoggerSupport.Logger<SpinnerHelper>();

    private readonly int _firstWaitMs;
    private readonly int _attemptMs;
    private readonly int _maxAttempts;

    private static int _lastId = 0;

    private int _runningTasks = 0;

    public SpinnerHelper() : this(200, 10_000, 6)
    {
    }

    /// <param name="firstWaitMs">How long the work may take before the spinner shows.</param>
    /// <param name="attemptMs">Each wait once it has shown.</param>
    /// <param name="maxAttempts">The attempt after which the work is cancelled and <see cref="GridTimeoutException"/> thrown.</param>
    internal SpinnerHelper(int firstWaitMs, int attemptMs, int maxAttempts)
    {
        _firstWaitMs = firstWaitMs;
        _attemptMs = attemptMs;
        _maxAttempts = maxAttempts;
    }

    public bool IsIdle() => Volatile.Read(ref _runningTasks) == 0;

    public enum EndMode { IN_TIME, OUT_OF_TIME }

    public record struct MonitorRes<TResult>(EndMode EndMode, Task<TResult> Task);

    /// <summary>
    /// This is intended to be called from the UI thread/context, whatever it is, if used outside of it unpredictable results may happen.
    /// </summary>
    /// <param name="spinner">Shown while the work runs past the first wait.</param>
    /// <param name="work">Produces the task scheduled in some way, typically with Task.Run()</param>
    /// <param name="cancellationToken">Passed to every wait; cancelling it abandons the wait and the task.</param>
    /// <exception cref="GridTimeoutException">The work did not complete within the budget.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled, or the work itself was.</exception>
    /// <remarks>A fault of the work propagates as the original exception, not wrapped in an <see cref="AggregateException"/>;
    /// the returned task is therefore always completed successfully.</remarks>
    public MonitorRes<TResult> Monitor<TResult>(ISpinner spinner,
            Func<CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Interlocked.Increment(ref _runningTasks);
        Task<TResult> task = Task.Run(() => work(cts.Token), cts.Token);
        int id = Interlocked.Increment(ref _lastId);
        bool spinnerShown = false;
        try
        {
            if (WaitCompleted(task, _firstWaitMs, cts.Token)) // A reasonable value for the system not to slow down too much ...
            {
                return new(EndMode.IN_TIME, task);
            }
            spinnerShown = spinner.Show();
            if (spinnerShown)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("START spinning {id}", id);
            }
            for (int i = 0; ; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (WaitCompleted(task, _attemptMs, cts.Token))
                    break;
                logger.Log(i <= 1 ? LogLevel.Warning : LogLevel.Error,
                    "Task {task} did not complete within {millisecondsTimeout} ms, attempt {attempt}.",
                    task, _attemptMs, i);
                if (i >= _maxAttempts)
                {
                    cts.Cancel();
                    bool stopped = Task.WaitAny([ task ], 500) == 0; // NO .Wait(TimeSpan.FromMilliseconds(200));
                    logger.LogWarning("Task worker cancelled {task}:{id} in time: {stopped}.", task, id, stopped);
                    if (!stopped)
                    {
                        // Nobody will await this now; keep a later fault from surfacing as unobserved.
                        _ = task.ContinueWith(static t => _ = t.Exception,
                                              TaskContinuationOptions.OnlyOnFaulted);
                    }
                    throw new GridTimeoutException(task, stopped, $"Task did not complete within {_attemptMs * (_maxAttempts + 1)} ms.");
                }
            }
            return new(EndMode.OUT_OF_TIME, task);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Task {task}:{id} was cancelled.", task, id);
            throw;
        }
        finally
        {
            if (task.IsCompleted)
            {
                cts.Dispose();
            } 
            else
            {
                _ = task.ContinueWith(_ => cts.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
            }
            if (spinnerShown)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("STOP spinning {id}", id);
                spinner.Hide();
            }
            Interlocked.Decrement(ref _runningTasks);
        }
    }

    /// <summary>
    /// True when the task completed within <paramref name="milliseconds"/>. Unlike <see cref="Task.Wait(int, CancellationToken)"/>
    /// this does not wrap: a fault is rethrown as the original exception, a cancelled task as <see cref="OperationCanceledException"/>,
    /// and a cancelled <paramref name="ct"/> as <see cref="OperationCanceledException"/> too.
    /// </summary>
    private static bool WaitCompleted(Task task, int milliseconds, CancellationToken ct)
    {
        if (Task.WaitAny([task], milliseconds, ct) < 0)
            return false;
        if (!task.IsCompletedSuccessfully)
            task.GetAwaiter().GetResult();
        return true;
    }
}

/// <summary>
/// Thrown when a grid's fetch runs past the spinner's time budget. The grid hands it to its <c>OnTimeout</c> first; unhandled,
/// it ends the circuit.
/// </summary>
public sealed class GridTimeoutException : TimeoutException
{
    internal readonly Task _task;

    /// <summary>
    /// True when the work that timed out has finished unwinding, so the model is quiescent and safe to
    /// read again. False means it is still running: nothing stops it writing, and a render that resumes
    /// now reads the model while it does. <see cref="Wait"/> can turn this from false to true.
    /// </summary>
    public bool WorkStopped { get; private set; }

    internal GridTimeoutException(Task task, bool workStopped, string? message) 
        : base(message)
    {
        _task = task;
        WorkStopped = workStopped;
    }

    /// <summary>
    /// Give the work that timed out more time to stop, for a caller that would rather handle the timeout
    /// than let it propagate. Returns at once when <see cref="WorkStopped"/> is already true.
    /// </summary>
    /// <param name="timeout">
    /// How much longer to wait. This blocks the calling thread, which on the render path is the circuit's
    /// dispatcher — so a generous value extends the freeze past the budget that produced the timeout.
    /// </param>
    /// <returns>True once the work has stopped, which is then also what <see cref="WorkStopped"/> reports.</returns>
    public bool Wait(TimeSpan timeout)
    {
        if (WorkStopped)
            return true;
        WorkStopped = Task.WaitAny([ _task ], timeout) == 0;
        return WorkStopped;
    }
}
