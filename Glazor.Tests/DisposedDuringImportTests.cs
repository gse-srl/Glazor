using Bunit;
using Glazor.Grds;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Glazor.Tests;

/// <summary>
/// The components a grid renders in every cell are disposed while their import is in flight when paging replaces the rows
/// faster than a round trip: DisposeAsync then finds no module, and the one that arrives afterwards must be released by the
/// component itself.
/// </summary>
public class DisposedDuringImportTests
{
    private static async Task AssertModuleReleased<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
        where TComponent : IComponent
    {
        using BunitContext ctx = new();
        PendingImportJSRuntime js = new();
        ctx.Services.AddSingleton<IJSRuntime>(js);
        ctx.Services.AddLogging();
        var cut = parameters == null ? ctx.Render<TComponent>() : ctx.Render(parameters);
        Assert.That(js.Imports, Has.Count.EqualTo(1), "the first render asks for the module");
        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        js.CompleteAll();
        cut.WaitForAssertion(() => Assert.That(js.Modules.Single().Disposed, Is.EqualTo(1), "released by the component the import came back to"));
    }

    [Test] public Task ClamperTest() => AssertModuleReleased<Clamper>(ps => ps.Add(c => c.LinesToClamp, 1).AddChildContent("x"));
    [Test] public Task MouseActionTest() => AssertModuleReleased<MouseAction>(ps => ps.Add(m => m.Type, MouseAction.MouseType.Click));
    [Test] public Task DialogBoxTest() => AssertModuleReleased<DialogBox>(ps => ps.AddChildContent("x"));
    [Test] public Task KeyActionTest() => AssertModuleReleased<KeyAction>(ps => ps.Add(k => k.Key, "Escape"));
    [Test] public Task ResizeObserverTest() => AssertModuleReleased<ResizeObserver>();
    [Test] public Task IdleTrackerTest() => AssertModuleReleased<IdleTracker>(ps => ps.Add(i => i.ChildContent, (RenderFragment<IdleTracker>)(_ => b => b.AddContent(0, "x"))));
    [Test] public Task TriStateCheckboxTest() => AssertModuleReleased<TriStateCheckbox>();
    [Test] public Task ScrollerTest() => AssertModuleReleased<Scroller>(ps => ps.Add(s => s.RowCount, 10).Add(s => s.PageSize, 5));
    [Test] public Task TouchScrollerTest() => AssertModuleReleased<TouchScroller>(ps => ps.Add(t => t.PageSize, 5));

    /// <summary>
    /// The dialog also imports on demand, so the spinner can show during a fetch that runs inside the first render: a show that
    /// overlaps the first render's import must share it, not start a second one whose predecessor nobody releases.
    /// </summary>
    [Test]
    public async Task DialogBoxShownWhileItsFirstImportIsInFlightImportsOnceTest()
    {
        using BunitContext ctx = new();
        PendingImportJSRuntime js = new();
        ctx.Services.AddSingleton<IJSRuntime>(js);
        ctx.Services.AddLogging();
        var cut = ctx.Render<DialogBox>(ps => ps.AddChildContent("x"));
        Assert.That(js.Imports, Has.Count.EqualTo(1), "the first render asks for the module");
        Task<bool> shown = cut.InvokeAsync(() => cut.Instance.ShowModalAsync());
        Assert.That(js.Imports, Has.Count.EqualTo(1), "the show joins the import in flight");
        js.CompleteAll();
        Assert.That(await shown, Is.True);
        Assert.That(cut.Instance.IsVisible, Is.True);
        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        Assert.That(js.Modules.Single().Disposed, Is.EqualTo(1), "one module, released once");
    }

    /// <summary>
    /// A component whose JS cleanup fails, as it does on an element already gone, still releases its module: the cleanup and
    /// the release are separate steps of the disposal.
    /// </summary>
    private static async Task AssertModuleReleasedWhenCleanupFails<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
        where TComponent : IComponent
    {
        using BunitContext ctx = new();
        PendingImportJSRuntime js = new();
        ctx.Services.AddSingleton<IJSRuntime>(js);
        ctx.Services.AddLogging();
        var cut = parameters == null ? ctx.Render<TComponent>() : ctx.Render(parameters);
        js.CompleteAll();
        PendingImportJSRuntime.CountingModule module = js.Modules.Single();
        cut.WaitForAssertion(() => Assert.That(module.Calls, Is.Not.Empty, "registered"));
        module.FailOn = "cleanup";
        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        Assert.Multiple(() =>
        {
            Assert.That(module.Calls, Does.Contain("cleanup"), "the cleanup ran, and failed");
            Assert.That(module.Disposed, Is.EqualTo(1), "the module released all the same");
        });
    }

    [Test] public Task KeyActionCleanupFailsTest() => AssertModuleReleasedWhenCleanupFails<KeyAction>(ps => ps.Add(k => k.Key, "Escape"));
    [Test] public Task MouseActionCleanupFailsTest() => AssertModuleReleasedWhenCleanupFails<MouseAction>(ps => ps.Add(m => m.Type, MouseAction.MouseType.Click));
    [Test] public Task IdleTrackerCleanupFailsTest() => AssertModuleReleasedWhenCleanupFails<IdleTracker>(ps => ps.Add(i => i.ChildContent, (RenderFragment<IdleTracker>)(_ => b => b.AddContent(0, "x"))));
    [Test] public Task ScrollerCleanupFailsTest() => AssertModuleReleasedWhenCleanupFails<Scroller>(ps => ps.Add(s => s.RowCount, 10).Add(s => s.PageSize, 5));
    [Test] public Task TouchScrollerCleanupFailsTest() => AssertModuleReleasedWhenCleanupFails<TouchScroller>(ps => ps.Add(t => t.PageSize, 5).Add(t => t.ScrollableRef, new ElementReference("scrollable")));
}
