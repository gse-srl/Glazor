using Glazor.Misc;
using System.Globalization;

namespace Glazor.Tests;

/// <summary>
/// The work <see cref="AsyncHelper.RunSync(Func{Task})"/> runs on the thread pool sees the caller's cultures, so the formatting
/// and the texts it produces follow the user's culture.
/// </summary>
public class AsyncHelperTests
{
    [Test]
    public void RunSyncFlowsTheCallersCulturesTest()
    {
        CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            (string, string) seen = AsyncHelper.RunSync(async () =>
            {
                await Task.Yield();
                return (CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name);
            });
            string? seenByVoid = null;
            AsyncHelper.RunSync(async () =>
            {
                await Task.Yield();
                seenByVoid = CultureInfo.CurrentCulture.Name;
            });
            Assert.Multiple(() =>
            {
                Assert.That(seen, Is.EqualTo(("it-IT", "de-DE")));
                Assert.That(seenByVoid, Is.EqualTo("it-IT"));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    /// <summary>
    /// A fault in the work comes back as the exception it threw, not wrapped in an AggregateException, so a caller catches it
    /// by its own type: the paged models' fetches rely on it for a query's exception and a cancellation.
    /// </summary>
    [Test]
    public void RunSyncRethrowsTheOriginalExceptionTest()
    {
        InvalidOperationException failure = new("the query failed");
        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<InvalidOperationException>(() => AsyncHelper.RunSync<int>(async () =>
            {
                await Task.Yield();
                throw failure;
            })), Is.SameAs(failure));
            Assert.That(Assert.Throws<InvalidOperationException>(() => AsyncHelper.RunSync(async () =>
            {
                await Task.Yield();
                throw failure;
            })), Is.SameAs(failure));
            Assert.Throws<OperationCanceledException>(() => AsyncHelper.RunSync(async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            }));
        });
    }
}
