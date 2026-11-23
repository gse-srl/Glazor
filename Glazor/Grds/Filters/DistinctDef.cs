using Glazor.Grds.Mdls;
using Glazor.Misc;

namespace Glazor.Grds.Filters;

/// <summary>A row of the dialog of a <see cref="DistinctDef{SELF, VT}"/>: one of the values to choose from.</summary>
/// <param name="Value">The value.</param>
public sealed record DistinctRowValue<VT>(VT Value);

/// <summary>
/// A multi-select over the values a column holds, read before the user is asked for the filter: a value is its own key, and the
/// dialog lists them one per row, each wrapped in a <see cref="DistinctRowValue{VT}"/>. So the column need not hold a
/// reference type, as <see cref="DynaSetDef{SELF, VT, KT}"/> asks: a number, a Guid or a date do too, typed by the nullable
/// type when the column is nullable. A subclass says where the values come from, <see cref="RetrieveValuesAsync"/>.
/// <typeparamref name="VT"/> is persisted with the memento's options: use a type System.Text.Json writes.
/// </summary>
/// <typeparam name="SELF">The concrete type deriving from this class.</typeparam>
/// <typeparam name="VT">The type of the column's value.</typeparam>
public abstract class DistinctDef<SELF, VT> : ADynaSetDef<SELF, VT, DistinctRowValue<VT>, VT> where SELF : DistinctDef<SELF, VT>
{
    /// <param name="startFull">True selects every value, and lets every value that is not null pass until the values are read.</param>
    protected DistinctDef(bool startFull) : base(v => v, startFull)
    {
    }

    /// <inheritdoc/>
    protected sealed override VT RowToKey(DistinctRowValue<VT> row) => row.Value;

    /// <summary>The values to choose from, in any order: nulls and repeated values are left out.</summary>
    /// <param name="ct">The token given to <see cref="ADynaSetDef{SELF, VT, RT, KT}.PopulateAsync"/>.</param>
    protected abstract ValueTask<IEnumerable<VT?>> RetrieveValuesAsync(CancellationToken ct);

    /// <inheritdoc/>
    protected sealed override async ValueTask<IList<DistinctRowValue<VT>>> RetrieveAllValuesAsync(CancellationToken ct)
    {
        List<DistinctRowValue<VT>> rows = [];
        foreach (VT? value in (await RetrieveValuesAsync(ct)).Distinct())
        {
            if (value is not null)
                rows.Add(new(value));
        }
        return rows;
    }

    /// <summary>One column with the values, sorted when their type can be compared.</summary>
    protected override DataGridModel<DistinctRowValue<VT>> CreateAllValuesModel()
    {
        DataGridModel<DistinctRowValue<VT>> model = new();
        bool sortable = PocoColumnComparer.CanCompare(typeof(VT));
        model.AddColumn("value", new Column<DistinctRowValue<VT>, VT>(Texts.Get(TextKeys.Value)) { Sortable = sortable, Filterable = false }, r => r.Value);
        if (sortable)
            model.SetSortedColumn("value", true);
        return model;
    }

    private static readonly FilterDefRendererType<DistinctDefRenderer<SELF, VT>> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor
}
