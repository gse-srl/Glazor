using Bunit;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The tracker reports each idle period once, and the activity that ends it, so a host can lock and unlock a page more than
/// once in a session.
/// </summary>
public class IdleTrackerTests
{
    [Test]
    public async Task IdleAndActiveAreReportedOncePerPeriodTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLogging();
        List<string> events = [];
        var cut = ctx.Render<IdleTracker>(ps => ps
            .Add(i => i.TimeoutMilliseconds, 1_000)
            .Add(i => i.OnUserIdle, () => events.Add("idle"))
            .Add(i => i.OnUserActive, () => events.Add("active"))
            .Add(i => i.ChildContent, (RenderFragment<IdleTracker>)(t => b => b.AddContent(0, t.IsIdle ? "IDLE" : "BUSY"))));

        await cut.InvokeAsync(() => cut.Instance.UserIsActive());   // activity while not idle reports nothing
        await cut.InvokeAsync(() => cut.Instance.UserIsIdle());
        Assert.That(cut.Markup, Does.Contain("IDLE"), "the content sees the state");
        await cut.InvokeAsync(() => cut.Instance.UserIsIdle());
        await cut.InvokeAsync(() => cut.Instance.UserIsActive());
        Assert.That(cut.Markup, Does.Contain("BUSY"));
        await cut.InvokeAsync(() => cut.Instance.UserIsIdle());
        Assert.That(events, Is.EqualTo(new[] { "idle", "active", "idle" }), "re-armed after the activity");
    }
}
