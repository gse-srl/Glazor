using Glazor.Misc;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds.Filters;

/// <summary>
/// The editor of a filter definition in the filter dialog. The dialog calls <see cref="ShownAsync"/> once it is shown, and
/// <see cref="Validate"/> before OK, which a <c>false</c> refuses.
/// </summary>
public abstract class AFilterDefRenderer : ComponentBase
{
    /// <summary>
    /// Prefix for the ids a renderer gives its inputs, so that a <c>&lt;label for&gt;</c> finds its own input when more than one
    /// filter dialog is on the page.
    /// </summary>
    protected string IdPrefix { get; } = "fd" + Guid.NewGuid().ToString("N");

    /// <summary>Cancelled when the filter dialog closes: give it to what the editor loads, so a load the user left stops.</summary>
    [Parameter]
    public CancellationToken DialogClosedToken { get; init; }

    [Inject] private IServiceProvider Services { get; init; } = null!;
    private IGlazorTexts? _texts;

    /// <summary>The texts the renderer shows: the host's <see cref="IGlazorTexts"/> service, or the built-in one.</summary>
    protected IGlazorTexts Texts => _texts ??= GlazorTexts.Resolve(Services);

    internal async ValueTask ShownAsyncImpl()
    {
        await ShownAsync();
    }
    /// <summary>Called once the dialog is shown, to load what the editor lists or to move the focus.</summary>
    protected internal abstract ValueTask ShownAsync();

    /// <summary>Called before OK; false keeps the dialog open.</summary>
    protected internal virtual bool Validate() => true;
}

/// <summary>An editor of <typeparamref name="TDef"/>: derive from it and return it from the definition's <c>Renderer()</c>.</summary>
public abstract class AFilterDefRenderer<TDef> : AFilterDefRenderer where TDef : AFilterDef
{
    /// <summary>The definition the editor edits.</summary>
    [Parameter]
    public TDef FilterDef { get; init; } = null!;

    /// <summary>Whether the column's value can be null; without, the "Include null" box is not shown.</summary>
    [Parameter]
    public bool NullableValue { get; set; } = true;
}

/// <summary>
/// To be used instead of the untyped C# Type so we can put a where condition on the generic we accept.<br/>
/// It has just one concrete subclass.
/// </summary>
public abstract record FilterDefRendererType
{
    /// <summary>The editor's component type.</summary>
    public abstract Type RendererType { get; }

    internal abstract void Dummy();
}

/// <summary>What a definition's <c>Renderer()</c> returns: <c>new FilterDefRendererType&lt;MyEditor&gt;()</c> for the editor <typeparamref name="T"/>.</summary>
public sealed record FilterDefRendererType<T> : FilterDefRendererType where T : AFilterDefRenderer
{
    /// <inheritdoc/>
    public override Type RendererType { get => typeof(T); }

    internal override void Dummy()  // trick
    {
        throw new NotImplementedException("Not to be called by anyone");
    }
}
