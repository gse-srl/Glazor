using Glazor.Misc;

namespace Glazor.Tests;

/// <summary>
/// The error contract of <see cref="SpinnerHelper.Monitor"/>: a fault of the work reaches the caller as the original
/// exception, before the spinner is up and after alike; cancellation is an OperationCanceledException; the budget spent
/// is a <see cref="GridTimeoutException"/> that tells whether the work stopped; the spinner shown is always hidden.
/// The waits are the helper's own, shortened here through its internal constructor, so no test takes the real 70 s.
/// </summary>
public class SpinnerHelperTests
{
    private sealed class CountingSpinner : ISpinner
    {
        public int Shown, Hidden;
        public bool Show() { Shown++; return true; }
        public void Hide() { Hidden++; }
    }

    private const int FirstWait = 50;
    private const int PastTheFirstWait = 3 * FirstWait;

    /// <summary>The production budget except the first wait: nothing here is meant to time out.</summary>
    private static SpinnerHelper Helper() => new(FirstWait, 10_000, 6);

    [Test]
    public void ResultInTimeTest()
    {
        SpinnerHelper helper = Helper();
        CountingSpinner spinner = new();
        var res = helper.Monitor(spinner, _ => Task.FromResult(42), CancellationToken.None);
        Assert.That(res.EndMode, Is.EqualTo(SpinnerHelper.EndMode.IN_TIME));
        Assert.That(res.Task.Result, Is.EqualTo(42));
        Assert.That(spinner.Shown, Is.Zero);
        Assert.That(helper.IsIdle(), Is.True);
    }

    [Test]
    public void FastFaultIsNotWrappedTest()
    {
        SpinnerHelper helper = Helper();
        CountingSpinner spinner = new();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            helper.Monitor<int>(spinner, _ => throw new InvalidOperationException("db down"), CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo("db down"));
        Assert.That(spinner.Shown, Is.Zero);
        Assert.That(helper.IsIdle(), Is.True);
    }

    [Test]
    public void SlowFaultIsNotWrappedAndHidesTheSpinnerTest()
    {
        SpinnerHelper helper = Helper();
        CountingSpinner spinner = new();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            helper.Monitor<int>(spinner, async _ => { await Task.Delay(PastTheFirstWait); throw new InvalidOperationException("db down later"); },
                CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo("db down later"));
        Assert.That(spinner.Shown, Is.EqualTo(1));
        Assert.That(spinner.Hidden, Is.EqualTo(1));
        Assert.That(helper.IsIdle(), Is.True);
    }

    [Test]
    public void CancellationSurfacesAsOperationCanceledTest()
    {
        SpinnerHelper helper = Helper();
        CountingSpinner spinner = new();
        using CancellationTokenSource cts = new(PastTheFirstWait);   // so the spinner has shown
        Assert.Catch<OperationCanceledException>(() =>
            helper.Monitor<int>(spinner, async ct => { await Task.Delay(5_000, ct); return 1; }, cts.Token));
        Assert.That(spinner.Shown, Is.EqualTo(1));
        Assert.That(spinner.Hidden, Is.EqualTo(1));
        Assert.That(helper.IsIdle(), Is.True);
    }

    [Test]
    public void ResultAfterTheSpinnerTest()
    {
        SpinnerHelper helper = Helper();
        CountingSpinner spinner = new();
        var res = helper.Monitor(spinner, async _ => { await Task.Delay(PastTheFirstWait); return 7; }, CancellationToken.None);
        Assert.That(res.EndMode, Is.EqualTo(SpinnerHelper.EndMode.OUT_OF_TIME));
        Assert.That(res.Task.Result, Is.EqualTo(7));
        Assert.That(spinner.Shown, Is.EqualTo(1));
        Assert.That(spinner.Hidden, Is.EqualTo(1));
    }

    /// <summary>
    /// Work that takes the cancellation stops when the budget ends, and the exception says so.
    /// </summary>
    [Test]
    public void TimeoutOfWorkThatStopsTest()
    {
        SpinnerHelper helper = new(10, 20, 1);
        CountingSpinner spinner = new();
        var ex = Assert.Throws<GridTimeoutException>(() =>
            helper.Monitor<int>(spinner, async ct => { await Task.Delay(5_000, ct); return 1; }, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.WorkStopped, Is.True);
            Assert.That(ex.Wait(TimeSpan.Zero), Is.True, "nothing left to wait for");
            Assert.That(ex.Message, Does.Contain("40 ms"), "the budget: each attempt plus the first");
            Assert.That(spinner.Shown, Is.EqualTo(1));
            Assert.That(spinner.Hidden, Is.EqualTo(1));
            Assert.That(helper.IsIdle(), Is.True);
        });
    }

    /// <summary>
    /// Work that ignores the cancellation is still running when the exception is thrown; <see cref="GridTimeoutException.Wait"/>
    /// gives it more time and reports when it has stopped.
    /// </summary>
    [Test]
    public void TimeoutOfWorkThatRunsOnTest()
    {
        SpinnerHelper helper = new(10, 20, 1);
        CountingSpinner spinner = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var ex = Assert.Throws<GridTimeoutException>(() =>
            helper.Monitor<int>(spinner, async _ => { await release.Task; return 1; }, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.WorkStopped, Is.False, "not stopped within the 500 ms the helper gives it");
            Assert.That(ex.Wait(TimeSpan.FromMilliseconds(1)), Is.False, "still running");
        });
        release.SetResult();
        Assert.Multiple(() =>
        {
            Assert.That(ex.Wait(TimeSpan.FromSeconds(10)), Is.True, "waited out");
            Assert.That(ex.WorkStopped, Is.True);
            Assert.That(spinner.Hidden, Is.EqualTo(1), "hidden before the throw");
            Assert.That(helper.IsIdle(), Is.True);
        });
    }
}
