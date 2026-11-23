using Bunit;
using Glazor.Misc;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;

namespace Glazor.Tests;

/// <summary>
/// InputTextOnInput replaces InputText's markup; it must still expose the rendered element through the inherited
/// Element property, which consumers use to focus or measure a plain InputText.
/// </summary>
public class InputTextOnInputTests
{
    private string? _value = "abc";

    [Test]
    public void ElementIsCapturedTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLogging();
        Expression<Func<string?>> valueExpression = () => _value;
        var cut = ctx.Render<InputTextOnInput>(ps => ps
            .Add(c => c.Value, _value)
            .Add(c => c.ValueExpression, valueExpression));
        Assert.That(cut.Instance.Element.HasValue, Is.True, "the inherited Element must point at the rendered input");
        Assert.That(cut.Instance.Element!.Value.Id, Is.Not.Empty);
        Assert.That(cut.Find("input").GetAttribute("value"), Is.EqualTo("abc"));
    }

    private sealed class Probe : InputTextOnInput
    {
        public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
    }

    /// <summary>
    /// The oninput handler must tell the renderer it updates the value attribute, as @bind does; otherwise every re-render
    /// sends back the server's older value and overwrites what the user typed meanwhile.
    /// </summary>
    [Test]
    public void OnInputHandlerUpdatesTheValueAttributeTest()
    {
        RenderTreeBuilder builder = new();
        new Probe().Build(builder);
#pragma warning disable BL0006   // the handler's updated attribute shows only in the render frames
        var frames = builder.GetFrames();
        RenderTreeFrame onInput = frames.Array.Take(frames.Count).Single(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "oninput");
        Assert.That(onInput.AttributeEventUpdatesAttributeName, Is.EqualTo("value"));
#pragma warning restore BL0006
    }

    [Test]
    public void TypingUpdatesTheBoundValueTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLogging();
        Expression<Func<string?>> valueExpression = () => _value;
        var cut = ctx.Render<InputTextOnInput>(ps => ps
            .Add(c => c.Value, _value)
            .Add(c => c.ValueChanged, v => _value = v)
            .Add(c => c.ValueExpression, valueExpression));
        cut.Find("input").Input("abcd");
        Assert.That(_value, Is.EqualTo("abcd"));
    }
}
