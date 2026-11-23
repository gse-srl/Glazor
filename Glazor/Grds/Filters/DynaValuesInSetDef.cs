using System.Linq.Expressions;

namespace Glazor.Grds.Filters;

/// <summary>
/// Dynamic set of values where possible values are dynamically retrieved before asking to the user the filter.
/// The set of values is matched against multiple values, it is enough to have an intersection for the filter to be true: the
/// column is an <see cref="IQueryable{VT}"/>, its items of type <typeparamref name="VT"/> with keys of type <typeparamref name="KT"/>.
/// Its dialog lists the items themselves, so <typeparamref name="VT"/> is a reference type; for rows of another type see
/// <see cref="ADynaValuesInSetDef{SELF, VT, RT, KT}"/>.
/// <typeparamref name="KT"/> is persisted with the memento's options: use a type System.Text.Json writes, such as a record;
/// a ValueTuple keeps its items in fields, which it skips unless IncludeFields is set.
/// It is abstract and requires a no parameters ctor because we have to support json serialization and so each type must have only serializable attributes.
/// </summary>
public abstract class DynaValuesInSetDef<SELF, VT, KT> : ADynaValuesInSetDef<SELF, VT, VT, KT>
        where SELF : DynaValuesInSetDef<SELF, VT, KT> where VT : class
{
    /// <param name="alwaysInMemory">True keeps the filter in memory: see <see cref="AValuesInSetDef{SELF, EVT, VT, KT}.AlwaysInMemory"/>.</param>
    /// <param name="valueToKeyExpr">Gives an item's key, which <see cref="ADynaValuesInSetDef{SELF, VT, RT, KT}.Selected"/> holds.</param>
    /// <param name="startFull">True selects every candidate, and lets every collection that is not empty pass until the candidates are read.</param>
    public DynaValuesInSetDef(bool alwaysInMemory, Expression<Func<VT, KT>> valueToKeyExpr, bool startFull) : base(alwaysInMemory, valueToKeyExpr, startFull)
    {
    }

    /// <inheritdoc/>
    protected sealed override KT RowToKey(VT row) => ValueToKeyFn.Invoke(row);

    private static readonly FilterDefRendererType<DynaValuesInSetDefRenderer<SELF, VT, KT>> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor
}
