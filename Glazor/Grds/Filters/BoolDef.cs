using Glazor.Misc;
using System.Text.Json;

namespace Glazor.Grds.Filters;

/// <summary>
/// The filter of a <c>bool</c> column: true, false or either, and whether a null value passes.
/// </summary>
public class BoolDef : AValueDef<BoolDef, bool?>
{
    /// <summary>The choice in the editor: every row, or only the false or the true ones.</summary>
    public enum BoolValue
    {
        /// <summary>Every row passes.</summary>
        All = -1,
        /// <summary>Only the false rows pass.</summary>
        False = 0, 
        /// <summary>Only the true rows pass.</summary>
        True = 1,
    }

    /// <summary>Whether a null value passes the filter; true by default.</summary>
    public bool IncludeNull { get; set; } = true;
    /// <summary>The values that pass: <see cref="BoolValue.All"/>, the default, or only false or only true.</summary>
    public BoolValue Value { get; set; } = BoolValue.All;

    /// <inheritdoc/>
    public override BoolDef Clone()
    {
        return new BoolDef()
        {
            IncludeNull = this.IncludeNull,
            Value = this.Value,
        };
    }

    /// <inheritdoc/>
    public override bool MustIncludeNull() => IncludeNull;

    /// <inheritdoc/>
    public override bool? GetValue() => Value switch
    {
        BoolValue.All => null,
        BoolValue.False => false,
        BoolValue.True => true,
        _ => throw new ArgumentException($"Unexpected {Value}")
    };

    private static readonly FilterDefRendererType<BoolDefRenderer> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor

    // Each default is the type's own, the value options that skip nulls or defaults leave out of the JSON
    /// <summary>The definition as a memento stores it.</summary>
    /// <param name="IncludeNull">Whether a null value passes.</param>
    /// <param name="Value">The value that passes, or <see cref="BoolValue.All"/>.</param>
    public record Serialized(bool IncludeNull = false, BoolValue Value = BoolValue.False) : AFilterDefSer;

    /// <inheritdoc/>
    protected override Serialized CreateSerRecord() => new Serialized(IncludeNull, Value);

    /// <inheritdoc/>
    public override BoolDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        if (!Enum.IsDefined(ser.Value))
            throw new JsonException($"Unexpected {ser.Value}");
        IncludeNull = ser.IncludeNull;
        Value = ser.Value;
        return this;
    }
}
