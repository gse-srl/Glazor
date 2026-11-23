using System.Linq.Expressions;

namespace Glazor.Grds.Filters;

/// <summary>
/// Dynamic set of values where possible values are dynamically retrieved before asking to the user the filter. Its dialog lists
/// the column's values themselves, so <typeparamref name="VT"/> is a reference type; for a column of another type see
/// <see cref="DistinctDef{SELF, VT}"/>.
/// <typeparamref name="KT"/> is persisted with the memento's options: use a type System.Text.Json writes, such as a record;
/// a ValueTuple keeps its items in fields, which it skips unless IncludeFields is set.
/// </summary>
public abstract class DynaSetDef<SELF, VT, KT> : ADynaSetDef<SELF, VT, VT, KT> where SELF : DynaSetDef<SELF, VT, KT> where VT : class
{
    /// <param name="valueToKeyExpr">Gives a value's key, which <see cref="ADynaSetDef{SELF, VT, RT, KT}.Selected"/> holds.</param>
    /// <param name="startFull">True selects every candidate, and lets every value that is not null pass until the candidates are read.</param>
    public DynaSetDef(Expression<Func<VT, KT>> valueToKeyExpr, bool startFull) : base(valueToKeyExpr, startFull)
    {
    }

    /// <inheritdoc/>
    protected sealed override KT RowToKey(VT row) => ValueToKeyFn.Invoke(row);

    private static readonly FilterDefRendererType<DynaSetDefRenderer<SELF, VT, KT>> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor
}
