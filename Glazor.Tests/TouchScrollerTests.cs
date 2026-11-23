using Bunit;
using Glazor.Grds;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

public class TouchScrollerTests
{
    /// <summary>
    /// A new element to scroll replaces the old one: the old is released before the new is registered, or a drag on it keeps
    /// scrolling the grid; a render on the same element releases nothing; and disposing releases the element last registered.
    /// </summary>
    [Test]
    public async Task NewScrollableElementReleasesThePreviousOneTest()
    {
        using BunitContext ctx = new();
        ctx.Services.AddLogging();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Grds/TouchScroller.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<TouchScroller>(ps => ps.Add(t => t.PageSize, 5).Add(t => t.ScrollableRef, new ElementReference("first")));
        cut.Render(ps => ps.Add(t => t.ScrollableRef, new ElementReference("second")));
        cut.Render();
        IEnumerable<string> Ids(string function) => module.Invocations[function].Select(i => ((ElementReference)i.Arguments[0]!).Id);
        Assert.Multiple(() =>
        {
            Assert.That(Ids("registerScrollerCallback"), Is.EqualTo(new[] { "first", "second", "second" }));
            Assert.That(Ids("cleanup"), Is.EqualTo(new[] { "first" }), "the first element is released when the second is registered");
        });
        await ctx.DisposeComponentsAsync();
        Assert.That(Ids("cleanup"), Is.EqualTo(new[] { "first", "second" }), "disposing releases the element last registered");
    }
}
