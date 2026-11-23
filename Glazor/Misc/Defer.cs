using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Glazor.Misc;

// https://stackoverflow.com/questions/77449730/understanding-quickgrid-internals-defer-hack

/// <summary>
/// Renders its child content one step later than the surrounding markup, so that the grid's own render tree
/// sees the columns registered by the ChildContent components before it is built.
/// </summary>
public sealed class Defer : ComponentBase
{
    /// <summary>The content rendered in a later pass than its siblings.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, ChildContent);
    }
}
