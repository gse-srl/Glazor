using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Text.Json;

namespace Glazor.Grds.Filters;

/// <summary>
/// A multi-select over a fixed set of values, <see cref="AllValues"/>, persisted as the values themselves. A restored one holds
/// deserialized copies, so <typeparamref name="VT"/> must compare by value: a string, a record, or a type overriding
/// <c>Equals</c> and <c>GetHashCode</c>. Over entities, derive from <see cref="DynaSetDef{SELF, VT, KT}"/> with a key instead.
/// </summary>
public class SetDef<VT> : DynaSetDef<SetDef<VT>, VT, VT> where VT : class
{
    /// <summary>The candidate values, in the order the dialog lists them; persisted with the selection.</summary>
    public required IEnumerable<VT> AllValues { get; init; }

    static SetDef()
    {
        Type type = typeof(VT);
        Type etype = Nullable.GetUnderlyingType(type) ?? type;
        if (etype.IsEnum)
            throw new ArgumentException($"Please use EnumDef for: {type}");
    }

    /// <summary>
    /// Just the prototype c-tor, we use it upon deserialization: <see cref="FilterDefFactory"/> creates one
    /// through the parameterless c-tor and then lets <see cref="Read"/> build the real instance, so without
    /// this a persisted SetDef could not be restored at all.<br/>
    /// AllValues is left unset on purpose, the prototype is never used as a filter.
    /// </summary>
    private SetDef() : base(v => v, false)
    {
        AllValues = null!;
    }

    /// <summary>A filter with nothing selected, or with every value when <paramref name="startFull"/>, those read later included.</summary>
    public SetDef(bool startFull) : base(v => v, startFull)
    {
    }

    /// <inheritdoc/>
    public override SetDef<VT> Clone()
    {
        return new SetDef<VT>(StartFull())
        {
            AllValues = this.AllValues,
            IncludeNull = this.IncludeNull,
            AllKeys = this.AllKeys == null ? null : new(this.AllKeys),
            Selected = new(this.Selected),
        };
    }

    /// <inheritdoc/>
    protected override DataGridModel<VT> CreateAllValuesModel()
    {
        DataGridModel<VT> model = new(AllValues);
        model.AddColumn("value", new Column<VT, string?>(Texts.Get(TextKeys.Value)) { Filterable = false }, v => v?.ToString());
        return model;
    }

    /// <inheritdoc/>
    protected override ValueTask<IList<VT>> RetrieveAllValuesAsync(CancellationToken ct)
    {
        return ValueTask.FromResult<IList<VT>>(AllValues.ToList());
    }

    /// <inheritdoc/>
    protected override IEnumerable<VT>? KnownValues() => AllValues;   // in memory: no dialog needed to know them

    /// <inheritdoc/>
    protected override Serialized CreateSerRecord() => new Serialized(AllValues, IncludeNull, GetSelected());   // a start-full set is filled on first use

    /// <inheritdoc/>
    public override SetDef<VT> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        SetDef<VT> res = new(false)   // never startFull upon deserialization
        {
            AllValues = ser.AllKeys.RequireNotNull().Distinct().ToList().AsReadOnly(),   // in the order saved, which the dialog shows
            AllKeys = new HashSet<VT>(ser.AllKeys)
        };
        res.IncludeNull = ser.IncludeNull;
        foreach (VT vv in ser.Selected ?? [])
        {
            res.Selected.Add(vv);
        }
        return res;
    }
}
