using Glazor.Misc;
using System.Numerics;
using System.Text.Json;

namespace Glazor.Grds.Filters;

/// <summary>
/// The filter of a numeric column: a range from <see cref="From"/> to <see cref="To"/>, both included and either open, and
/// whether a null value passes.
/// </summary>
public class NumberDef<VT> : ARangeDef<NumberDef<VT>, VT?> where VT : struct, INumber<VT>
{
    /// <summary>Whether a null value passes the filter; true by default.</summary>
    public bool IncludeNull { get; set; } = true;

    /// <summary>The lower bound, included; null for none.</summary>
    public VT? From { get; set; }
    /// <summary>The upper bound, included; null for none.</summary>
    public VT? To { get; set; }

    /// <inheritdoc/>
    public override NumberDef<VT> Clone()
    {
        return new NumberDef<VT> { 
            IncludeNull = this.IncludeNull,
            From = this.From,
            To = this.To
        };
    }

    /// <inheritdoc/>
    public override bool MustIncludeNull() => IncludeNull;

    /// <inheritdoc/>
    public override VT? GetFrom() => From;

    /// <inheritdoc/>
    public override VT? GetTo() => To;

    private static readonly FilterDefRendererType<NumberDefRenderer<VT>> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor

    /// <inheritdoc/>
    public override NumberDef<VT> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        IncludeNull = ser.IncludeNull;
        From = ser.From;
        To = ser.To;
        return this;
    }
}
