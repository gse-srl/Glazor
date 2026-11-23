using Bunit;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The checkbox keeps two DOM properties in step with Value through JS on every render. The case that matters is
/// indeterminate -> unchecked: the browser sets checked=true on the click, Value becomes false, and the rendered
/// checked attribute is false as it was before, so Blazor emits no DOM edit; only the JS call puts it right.
/// </summary>
public class TriStateCheckboxTests
{
    [Test]
    public async Task IndeterminateToUncheckedPushesCheckedFalseToTheDomTest()
    {
        using BunitContext ctx = new();
        ctx.Services.AddLogging();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/TriStateCheckbox.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        bool? bound = null;
        var cut = ctx.Render<TriStateCheckbox>(ps => ps
            .Add(c => c.Value, bound)
            .Add(c => c.ValueChanged, v => bound = v));
        var first = module.Invocations["setState"].Last();
        Assert.That(first.Arguments[1], Is.True, "indeterminate at start");
        Assert.That(first.Arguments[2], Is.False, "not checked at start");

        await cut.InvokeAsync(() => cut.Find("input").ChangeAsync(new ChangeEventArgs { Value = true }));   // what the browser does on the click
        Assert.That(bound, Is.False, "indeterminate toggles to unchecked");
        cut.WaitForAssertion(() =>
        {
            var last = module.Invocations["setState"].Last();
            Assert.That(last.Arguments[1], Is.False, "indeterminate cleared");
            Assert.That(last.Arguments[2], Is.False, "checked pushed to false, because the attribute did not change");
        });
    }

    [Test]
    public async Task UncheckedToCheckedToIndeterminateTest()
    {
        using BunitContext ctx = new();
        ctx.Services.AddLogging();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/TriStateCheckbox.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        bool? bound = false;
        var cut = ctx.Render<TriStateCheckbox>(ps => ps
            .Add(c => c.Value, bound)
            .Add(c => c.ValueChanged, v => bound = v));
        await cut.InvokeAsync(() => cut.Find("input").ChangeAsync(new ChangeEventArgs { Value = true }));
        Assert.That(bound, Is.True);
        cut.WaitForAssertion(() => Assert.That(module.Invocations["setState"].Last().Arguments[2], Is.True, "checked"));
        await cut.InvokeAsync(() => cut.Find("input").ChangeAsync(new ChangeEventArgs { Value = false }));
        Assert.That(bound, Is.Null);
        cut.WaitForAssertion(() => Assert.That(module.Invocations["setState"].Last().Arguments[1], Is.True, "indeterminate"));
    }

    /// <summary>
    /// A host's ToggleFn replaces the default cycle: here a two-state box that never turns indeterminate. A toggle that returns
    /// the value it was given changes nothing and raises nothing.
    /// </summary>
    [Test]
    public async Task ToggleFnReplacesTheCycleTest()
    {
        using BunitContext ctx = new();
        ctx.Services.AddLogging();
        ctx.JSInterop.SetupModule("./_content/Glazor/Misc/TriStateCheckbox.razor.js").Mode = JSRuntimeMode.Loose;
        List<bool?> raised = [];
        var cut = ctx.Render<TriStateCheckbox>(ps => ps
            .Add(c => c.Value, false)
            .Add(c => c.ToggleFn, v => v != true)
            .Add(c => c.ValueChanged, v => raised.Add(v)));
        await cut.InvokeAsync(() => cut.Find("input").ChangeAsync(new ChangeEventArgs { Value = true }));
        await cut.InvokeAsync(() => cut.Find("input").ChangeAsync(new ChangeEventArgs { Value = false }));
        var stuck = ctx.Render<TriStateCheckbox>(ps => ps
            .Add(c => c.Value, true)
            .Add(c => c.ToggleFn, v => v)
            .Add(c => c.ValueChanged, v => raised.Add(v)));
        await stuck.InvokeAsync(() => stuck.Find("input").ChangeAsync(new ChangeEventArgs { Value = false }));
        Assert.That(raised, Is.EqualTo(new bool?[] { true, false }), "unchecked, checked, unchecked; the stuck one raises nothing");
    }

    /// <summary>
    /// The attributes a host passes reach the input, so a page can name the box or point a label at it; the type stays the
    /// checkbox's own.
    /// </summary>
    [Test]
    public void HostAttributesReachTheInputTest()
    {
        using BunitContext ctx = new();
        ctx.Services.AddLogging();
        ctx.JSInterop.SetupModule("./_content/Glazor/Misc/TriStateCheckbox.razor.js").Mode = JSRuntimeMode.Loose;
        var input = ctx.Render<TriStateCheckbox>(ps => ps
            .AddUnmatched("id", "all")
            .AddUnmatched("aria-label", "Select every row")
            .AddUnmatched("type", "text")).Find("input");
        Assert.Multiple(() =>
        {
            Assert.That(input.Id, Is.EqualTo("all"));
            Assert.That(input.GetAttribute("aria-label"), Is.EqualTo("Select every row"));
            Assert.That(input.GetAttribute("type"), Is.EqualTo("checkbox"));
        });
    }
}
