using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Linq.Expressions;
using System.Text.Json.Serialization;

namespace Glazor.Grds.Filters;

/// <summary>
/// A set of values read before the user is asked for the filter, where the rows its dialog lists have a type of their own: the
/// column holds <typeparamref name="VT"/>, the dialog lists <typeparamref name="RT"/>, and a value and a row give a key of type
/// <typeparamref name="KT"/>, which the selection holds. <see cref="DynaSetDef{SELF, VT, KT}"/> is the case where the rows are
/// the column's values; <see cref="DistinctDef{SELF, VT}"/> the one where each row wraps a value, so that the column need not
/// hold a reference type.
/// <typeparamref name="KT"/> is persisted with the memento's options: use a type System.Text.Json writes, such as a record;
/// a ValueTuple keeps its items in fields, which it skips unless IncludeFields is set.
/// </summary>
/// <typeparam name="SELF">The concrete type deriving from this class.</typeparam>
/// <typeparam name="VT">The type of the column's value.</typeparam>
/// <typeparam name="RT">The type of the rows the dialog lists.</typeparam>
/// <typeparam name="KT">The type of the key of a value and of a row.</typeparam>
public abstract class ADynaSetDef<SELF, VT, RT, KT> : ASetDef<SELF, VT, KT> where SELF : ADynaSetDef<SELF, VT, RT, KT> where RT : class
{
    /// <summary>Whether a null value passes the filter; true by default.</summary>
    public bool IncludeNull { get; set; } = true;

    /// <summary>The keys of every candidate; null until they are read.</summary>
    protected internal HashSet<KT>? AllKeys { get; protected set; }

    /// <summary>The keys of the values selected; a set that starts full fills it with every candidate once it reads them.</summary>
    public HashSet<KT> Selected { get; protected set; } = [];

    private bool _startFull;

    static ADynaSetDef()
    {
        Type type = typeof(VT);
        Type etype = Nullable.GetUnderlyingType(type) ?? type;
        if (etype.IsEnum)
            throw new ArgumentException($"Please use EnumDef for: {type}");
    }

    /// <param name="valueToKeyExpr">Gives a value's key, which <see cref="Selected"/> holds.</param>
    /// <param name="startFull">True selects every candidate, and lets every value that is not null pass until the candidates are read.</param>
    protected ADynaSetDef(Expression<Func<VT, KT>> valueToKeyExpr, bool startFull) : base(valueToKeyExpr)
    {
        this._startFull = startFull;
    }

    /// <summary>The key of a row of the dialog: the one its value gives.</summary>
    protected abstract KT RowToKey(RT row);

    /// <summary>
    /// Retrieves the candidate rows and fills the set, selecting all of them when it starts full. An editor calls it before
    /// <see cref="CreateSetSelectionModel"/>, as the built-in one does when it is shown.
    /// </summary>
    /// <param name="ct">Cancels the retrieval.</param>
    /// <returns>The candidate rows, for the editor to list.</returns>
    public async ValueTask<IList<RT>> PopulateAsync(CancellationToken ct = default)
    {
        IList<RT>? allValues = await RetrieveAllValuesAsync(ct);
        Populate(allValues);
        return allValues;
    }

    private void Populate(IEnumerable<RT> allValues)
    {
        HashSet<KT> keys = allValues.Select(RowToKey).ToHashSet();
        if (_startFull || (AllKeys != null && Selected.SetEquals(AllKeys)))   // every value selected: those added since too
        {
            Selected = new(keys);
            _startFull = false;
        }
        else
            Selected.IntersectWith(keys);
        AllKeys = keys;
    }

    /// <summary>
    /// The candidate rows when they are at hand without retrieving anything, null (the default) when they must be retrieved.
    /// A definition built by host code and handed to the grid never opens its dialog, so <see cref="PopulateAsync"/> never runs:
    /// with the rows at hand it populates itself on the first question instead, and behaves like an opened one.
    /// </summary>
    protected virtual IEnumerable<RT>? KnownValues() => null;

    private HashSet<KT>? EnsurePopulated()
    {
        if (AllKeys == null && KnownValues() is { } values)
            Populate(values);
        return AllKeys;
    }

    /// <summary>Whether the set starts full and has not read its candidates yet; false once it has.</summary>
    public bool StartFull() { return _startFull; }

    /// <summary>A selection model over the candidates that selects into <see cref="Selected"/>; <see cref="PopulateAsync"/> runs first.</summary>
    public SetSelectionModelWithSelectAll<RT, KT> CreateSetSelectionModel()
    {
        return new SetSelectionModelWithSelectAll<RT, KT>(this.AllKeys.RequireNotNull(), RowToKey, this.Selected,
            defensiveCopy: false);   // the dialog selects into the definition's own set
    }

    /// <summary>The texts of the dialog showing this definition, for the labels <see cref="CreateAllValuesModel"/> gives its columns.</summary>
    protected IGlazorTexts Texts { get; private set; } = GlazorTexts.Default;

    internal DataGridModel<RT> CreateAllValuesModel_(IGlazorTexts texts)
    {
        Texts = texts;
        return CreateAllValuesModel();
    }

    /// <summary>
    /// The grid the dialog lists the rows in: a model with one column per field of <typeparamref name="RT"/> to show, added with
    /// <c>AddColumn</c>, labelled with <see cref="Texts"/> when they follow the culture. Called each time the dialog opens.
    /// </summary>
    protected abstract DataGridModel<RT> CreateAllValuesModel();

    /// <summary>The rows to choose from, read each time the dialog opens.</summary>
    /// <param name="ct">The token given to <see cref="PopulateAsync"/>.</param>
    protected abstract ValueTask<IList<RT>> RetrieveAllValuesAsync(CancellationToken ct);

    /// <inheritdoc/>
    public override bool IsVoid()
    {
        if (!MustIncludeNull())
            return false;
        // Not populated and nothing at hand to do it with: a start-full set is all selected, an explicit one is a real filter
        return EnsurePopulated() is { } all ? Selected.SetEquals(all) : _startFull;
    }

    /// <inheritdoc/>
    public override bool MustIncludeNull() => IncludeNull;

    private protected override bool SelectsAll() => EnsurePopulated() is { } all ? Selected.SetEquals(all) : _startFull;

    /// <inheritdoc/>
    public override IReadOnlySet<KT> GetSelected()
    {
        EnsurePopulated();
        return Selected;
    }

    // Always written: strict options require them, and options that skip nulls or defaults would leave them out

    /// <summary>The definition as a memento stores it.</summary>
    /// <param name="AllKeys">The keys of the candidates offered when it was saved, null when they were never read.</param>
    /// <param name="IncludeNull">Whether a null value passes.</param>
    /// <param name="Selected">The keys of the values that pass.</param>
    /// <param name="StartFull">Whether it started with every value selected and was never populated.</param>
    public record Serialized([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] IEnumerable<KT>? AllKeys, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool IncludeNull,

        IEnumerable<KT> Selected,

        // Started full and never populated: every value selected. False in a memento written before it existed
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool StartFull = false) : AFilterDefSer
    {
        /// <summary>The equality the record's <c>Equals</c> uses.</summary>
        protected static readonly IEqualityComparer<Serialized> comparer = EqComparers.Combo<Serialized>(l => l
             .Add(l => l?.AllKeys, EqComparers.ForSet<KT>())          // AllKeys is a set
             .Add(l => l?.IncludeNull)
             .Add(l => l?.Selected, EqComparers.ForSet<KT>())       // and so is Selected
             .Add(l => l?.StartFull));

        /// <inheritdoc/>
        public virtual bool Equals(Serialized? other)
        {
            if (!base.Equals(other))
                return false;
            return comparer.Equals(this, other);
        }

        //public override bool Equals(object? obj) Not needed, generated by the compiler ...

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), comparer.GetHashCode(this));
        }
    }

    /// <inheritdoc/>
    protected override Serialized CreateSerRecord()
    {
        IReadOnlySet<KT> selected = GetSelected();   // may populate AllKeys, so before reading them
        return new Serialized(AllKeys, MustIncludeNull(), selected, _startFull);
    }
}
