using Bunit;
using Glazor.Misc;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// The JS side reports MouseEvent.clientX/Y, which the browser defines as doubles and sends with fractions under page
/// zoom or on high-DPI screens. The interop entry point must accept them; the event the consumer sees is in pixels.
/// </summary>
public class MouseActionTests
{
    [Test]
    public void FractionalCoordinatesAreAcceptedAndRoundedTest()
    {
        // Why the entry point cannot take int: the interop layer binds the JSON arguments with System.Text.Json.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<int>("12.6"));

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLogging();
        MouseAction.MouseEvent? seen = null;
        var cut = ctx.Render<MouseAction>(ps => ps
            .Add(c => c.Type, MouseAction.MouseType.Click)
            .Add(c => c.OnMouse, evt => seen = evt));
        cut.InvokeAsync(() => cut.Instance.ProcessAsync("btn", "id1", 12.6, 20.25, 0)).GetAwaiter().GetResult();
        Assert.That(seen, Is.Not.Null);
        Assert.That((seen!.ClientX, seen.ClientY), Is.EqualTo((13, 20)));
        Assert.That(seen.TargetName, Is.EqualTo("btn"));
    }

    /// <summary>
    /// A render that arrives while the previous one waits on the JS cleanup, as at the start of an edit in the grid, finds
    /// the new stroke already registered and does not register it a second time. KeyAction works the same way.
    /// </summary>
    [Test]
    public void MouseActionRegistersOnceWhenRendersOverlapTest() => AssertRegistersOnceWhenRendersOverlap<MouseAction>(
        "./_content/Glazor/Misc/MouseAction.razor.js", "registerMouseHandler",
        enabled => ps => ps.Add(c => c.Type, MouseAction.MouseType.Click).Add(c => c.OnMouse, _ => { }).Add(c => c.Enabled, enabled));

    [Test]
    public void KeyActionRegistersOnceWhenRendersOverlapTest() => AssertRegistersOnceWhenRendersOverlap<KeyAction>(
        "./_content/Glazor/Misc/KeyAction.razor.js", "registerKeyboardHandler",
        enabled => ps => ps.Add(c => c.Key, "Escape").Add(c => c.OnKey, _ => { }).Add(c => c.Enabled, enabled));

    private static void AssertRegistersOnceWhenRendersOverlap<TComponent>(string modulePath, string register,
        Func<bool, Action<ComponentParameterCollectionBuilder<TComponent>>> parameters) where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        using BunitContext ctx = new();
        ctx.Services.AddLogging();
        var module = ctx.JSInterop.SetupModule(modulePath);
        module.SetupVoid(register, _ => true).SetVoidResult();
        var cleanup = module.SetupVoid("cleanup", _ => true);   // held back until both renders have run
        var cut = ctx.Render(parameters(true));
        cut.WaitForAssertion(() => Assert.That(module.Invocations[register], Has.Count.EqualTo(1)));
        cut.Render(parameters(false));
        cut.Render(parameters(false));
        cut.InvokeAsync(() => cleanup.SetVoidResult()).GetAwaiter().GetResult();
        cut.WaitForAssertion(() => Assert.That(module.Invocations[register], Has.Count.EqualTo(2)));
        Assert.That(module.Invocations["cleanup"], Has.Count.EqualTo(1));
    }
}
