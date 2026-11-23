using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Linq.Expressions;
using System.Text.Json.Serialization;

namespace Glazor.Grds.Filters;

/// <summary>
/// A set of values read before the user is asked for the filter, matched against a collection: the column is an
/// <see cref="IQueryable{VT}"/>, and a row passes when the key of one of its items is selected. The dialog lists rows of type
/// <typeparamref name="RT"/>, and an item and a row give a key of type <typeparamref name="KT"/>, which the selection holds.
/// <see cref="DynaValuesInSetDef{SELF, VT, KT}"/> is the case where the rows are the collection's items.
/// <typeparamref name="KT"/> is persisted with the memento's options: use a type System.Text.Json writes, such as a record;
/// a ValueTuple keeps its items in fields, which it skips unless IncludeFields is set.
/// A subclass has a parameterless constructor, which may be non-public, for the deserializer.
/// </summary>
/// <typeparam name="SELF">The concrete type deriving from this class.</typeparam>
/// <typeparam name="VT">The type of the collection's items.</typeparam>
/// <typeparam name="RT">The type of the rows the dialog lists.</typeparam>
/// <typeparam name="KT">The type of the key of an item and of a row.</typeparam>
public abstract class ADynaValuesInSetDef<SELF, VT, RT, KT> : AValuesInSetDef<SELF, IQueryable<VT>, VT, KT>
        where SELF : ADynaValuesInSetDef<SELF, VT, RT, KT> where RT : class
{
    /// <summary>Whether a null or empty collection passes the filter; true by default.</summary>
    public bool IncludeNullOrEmpty { get; set; } = true;

    /// <summary>The keys of every candidate; null until they are read.</summary>
    protected internal HashSet<KT>? AllKeys { get; protected set; }

    /// <summary>The keys of the values selected; a set that starts full fills it with every candidate once it reads them.</summary>
    public HashSet<KT> Selected { get; protected set; } = [];

    private bool _startFull;

    static ADynaValuesInSetDef()
    {
        Type type = typeof(VT);
        Type etype = Nullable.GetUnderlyingType(type) ?? type;
        if (etype.IsEnum)
            throw new ArgumentException($"Please use EnumDef for: {type}");
    }

    /// <param name="alwaysInMemory">True keeps the filter in memory: see <see cref="AValuesInSetDef{SELF, EVT, VT, KT}.AlwaysInMemory"/>.</param>
    /// <param name="valueToKeyExpr">Gives an item's key, which <see cref="Selected"/> holds.</param>
    /// <param name="startFull">True selects every candidate, and lets every collection that is not empty pass until the candidates are read.</param>
    protected ADynaValuesInSetDef(bool alwaysInMemory, Expression<Func<VT, KT>> valueToKeyExpr, bool startFull) : base(alwaysInMemory, valueToKeyExpr)
    {
        this._startFull = startFull;
    }

    /// <summary>The key of a row of the dialog: the one the items it stands for give.</summary>
    protected abstract KT RowToKey(RT row);

    /// <summary>Whether the set starts full and has not read its candidates yet; false once it has.</summary>
    public bool StartFull() { return _startFull; }

    /// <summary>A selection model over the candidates that selects into <see cref="Selected"/>; <see cref="PopulateAsync"/> runs first.</summary>
    public SetSelectionModelWithSelectAll<RT, KT> CreateSetSelectionModel()
    {
        return new SetSelectionModelWithSelectAll<RT, KT>(this.AllKeys.RequireNotNull(), RowToKey, this.Selected,
            defensiveCopy: false);   // the dialog selects into the definition's own set
    }

    /// <summary>
    /// Retrieves the candidate rows and fills the set, selecting all of them when it starts full. An editor calls it before
    /// <see cref="CreateSetSelectionModel"/>, as the built-in one does when it is shown.
    /// </summary>
    /// <param name="ct">Cancels the retrieval.</param>
    /// <returns>The candidate rows, for the editor to list.</returns>
    public async ValueTask<IList<RT>> PopulateAsync(CancellationToken ct = default)
    {
        IList<RT>? allValues = await RetrieveAllValuesAsync(ct);
        HashSet<KT> keys = allValues.Select(RowToKey).ToHashSet();
        if (_startFull || (AllKeys != null && Selected.SetEquals(AllKeys)))   // every value selected: those added since too
        {
            Selected = new(keys);
            _startFull = false;
        }
        else
            Selected.IntersectWith(keys);
        AllKeys = keys;
        return allValues;
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
        if (!MustIncludeNullOrEmpty())
            return false;
        // Not populated yet (the dialog does it): a start-full set is all selected, an explicit selection is a real filter
        return AllKeys == null ? _startFull : Selected.SetEquals(AllKeys);
    }

    /// <inheritdoc/>
    public override bool MustIncludeNullOrEmpty() => IncludeNullOrEmpty;

    private protected override bool SelectsAll() => AllKeys == null ? _startFull : Selected.SetEquals(AllKeys);

    /// <inheritdoc/>
    public override IReadOnlySet<KT> GetSelected()
    {
        return Selected;
    }

    // Always written: strict options require them, and options that skip nulls or defaults would leave them out

    /// <summary>The definition as a memento stores it.</summary>
    /// <param name="AllKeys">The keys of the candidates offered when it was saved, null when they were never read.</param>
    /// <param name="IncludeNullOrEmpty">Whether a row whose set of values is null or empty passes.</param>
    /// <param name="Selected">The keys of the values that pass.</param>
    /// <param name="StartFull">Whether it started with every value selected and was never populated.</param>
    public record Serialized([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] IEnumerable<KT>? AllKeys, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool IncludeNullOrEmpty,

        IEnumerable<KT> Selected,

        // Started full and never populated: every value selected. False in a memento written before it existed
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool StartFull = false) : AFilterDefSer
    {
        /// <summary>The equality the record's <c>Equals</c> uses.</summary>
        protected static readonly IEqualityComparer<Serialized> comparer = EqComparers.Combo<Serialized>(l => l
             .Add(l => l?.AllKeys, EqComparers.ForSet<KT>())          // AllKeys is a set
             .Add(l => l?.IncludeNullOrEmpty)
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
    protected override Serialized CreateSerRecord() => new Serialized(AllKeys, MustIncludeNullOrEmpty(), GetSelected(), _startFull);
}
