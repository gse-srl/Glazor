using Bunit;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

public class ResizeObserverTests
{
    /// <summary>
    /// A new element to observe replaces the old one: the old is released before the new is registered, or its resizes keep
    /// reaching the component; and disposing releases the element last observed.
    /// </summary>
    [Test]
    public async Task NewObservedElementReleasesThePreviousOneTest()
    {
        using BunitContext ctx = new();
        ctx.Services.AddLogging();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/ResizeObserver.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<ResizeObserver>(ps => ps.Add(r => r.ObservedRef, new ElementReference("first")));
        cut.Render(ps => ps.Add(r => r.ObservedRef, new ElementReference("second")));
        IEnumerable<string> Ids(string function) => module.Invocations[function].Select(i => ((ElementReference)i.Arguments[0]!).Id);
        Assert.Multiple(() =>
        {
            Assert.That(Ids("registerResizeObserverCallback"), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(Ids("cleanup"), Is.EqualTo(new[] { "first" }), "the first element is released when the second is registered");
        });
        await ctx.DisposeComponentsAsync();
        Assert.That(Ids("cleanup"), Is.EqualTo(new[] { "first", "second" }), "disposing releases the element last observed");
    }
}
