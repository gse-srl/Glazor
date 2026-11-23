using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;

namespace Glazor.EntityFrameworkCore;

/// <summary>
/// A multi-select over a collection column, <see cref="IQueryable{VT}"/>, whose dialog lists rows read from the database: the
/// rows <see cref="QueryFn"/> gives, read each time the dialog opens, shown in the columns <see cref="ColumnsFn"/> adds, or in
/// one column with each row's text when it adds none. A row
/// passes when the key of one of its items is selected. Hand it to the column as its <c>FilterDef</c>; on a column the database
/// filters it is pushed down as the built-in sets are.
/// <code>
/// _castFDef = new(id => id, startFull: true)
/// {
///     DbContextFactoryFn = () => DbContextFactory,
///     QueryFn = db => db.Actors.Select(a => new ActorRow(a.ActorId, a.FirstName + " " + a.LastName)),
///     RowKeyFn = a => a.Id,
///     ColumnsFn = (model, texts) => model.AddColumn("name", new Column&lt;ActorRow, string&gt;("Actor"), a => a.Name),
/// };
/// </code>
/// What the page supplies, the item's key expression included, is not persisted: a definition restored from a memento takes it
/// from the column's own, as <see cref="IWithInjectedParams{SELF}"/> describes, and keeps the selection it was saved with.
/// </summary>
/// <typeparam name="TDbContext">The EF Core context the rows are read through.</typeparam>
/// <typeparam name="VT">The type of the collection's items.</typeparam>
/// <typeparam name="RT">The type of the rows the dialog lists.</typeparam>
/// <typeparam name="KT">The type of the key of an item and of a row, which the selection holds.</typeparam>
public class EFValuesInSetDef<TDbContext, VT, RT, KT> : ADynaValuesInSetDef<EFValuesInSetDef<TDbContext, VT, RT, KT>, VT, RT, KT>,
        IWithInjectedParams<EFValuesInSetDef<TDbContext, VT, RT, KT>>
    where TDbContext : DbContext where RT : class
{
    // A Func, not the factory itself: the definition may be constructed in a component's constructor, where an injected
    // property is not set yet
    /// <summary>The factory of the contexts the values are read through, called each time they are read.</summary>
    public Func<IDbContextFactory<TDbContext>> DbContextFactoryFn { get; init; } = null!;

    /// <summary>The rows to choose from, read without tracking.</summary>
    public Func<TDbContext, IQueryable<RT>> QueryFn { get; init; } = null!;

    /// <summary>A row's key: the one the items it stands for give through the constructor's expression.</summary>
    public Func<RT, KT> RowKeyFn { get; init; } = null!;

    /// <summary>
    /// Adds the dialog's columns to its model, one per field of a row to show, labelled with the dialog's texts when they follow
    /// the culture; it may sort the model too. The model finds a row by <see cref="RowKeyFn"/>. Null, the default: one column,
    /// "Value", with each row's <c>ToString()</c>, sorted.
    /// </summary>
    public Action<DataGridModel<RT>, IGlazorTexts>? ColumnsFn { get; init; }

    // The deserializer's prototype: its key expression throws until the column's own definition gives the real one
    /// <summary>The prototype a restore reads into.</summary>
    protected EFValuesInSetDef() : this(NotInjected, false)
    {
    }

    private static readonly Expression<Func<VT, KT>> NotInjected = Expression.Lambda<Func<VT, KT>>(
        Expression.Throw(Expression.New(typeof(InvalidOperationException).GetConstructor([typeof(string)])!, Expression.Constant(
            "A restored EFValuesInSetDef has no key expression: restore it with RestoreMemento, which takes it from the column")), typeof(KT)),
        Expression.Parameter(typeof(VT)));

    /// <param name="valueToKeyExpr">Gives an item's key, which the selection holds; part of the predicate pushed down.</param>
    /// <param name="startFull">True selects every row, and lets every collection that is not empty pass until the rows are read.</param>
    /// <param name="alwaysInMemory">True keeps the filter in memory: see <see cref="AValuesInSetDef{SELF, EVT, VT, KT}.AlwaysInMemory"/>.</param>
    public EFValuesInSetDef(Expression<Func<VT, KT>> valueToKeyExpr, bool startFull, bool alwaysInMemory = false)
        : base(alwaysInMemory, valueToKeyExpr, startFull)
    {
    }

    /// <inheritdoc/>
    protected override KT RowToKey(RT row) => RowKeyFn(row);

    /// <inheritdoc/>
    protected override DataGridModel<RT> CreateAllValuesModel()
    {
        DataGridModel<RT> model = new() { ItemKeyFn = r => RowKeyFn(r)! };
        if (ColumnsFn != null)
        {
            ColumnsFn(model, Texts);
            return model;
        }
        model.AddColumn("value", new Column<RT, string>(Texts.Get(TextKeys.Value)), r => r.ToString() ?? "");
        model.SetSortedColumn("value", true);
        return model;
    }

    /// <inheritdoc/>
    protected override async ValueTask<IList<RT>> RetrieveAllValuesAsync(CancellationToken ct)
    {
        await using TDbContext db = await DbContextFactoryFn().CreateDbContextAsync(ct);
        return await QueryFn(db).AsNoTracking().ToListAsync(ct);
    }

    /// <inheritdoc/>
    public override EFValuesInSetDef<TDbContext, VT, RT, KT> Clone() => new(ValueToKeyExpr, StartFull(), AlwaysInMemory)
    {
        DbContextFactoryFn = DbContextFactoryFn,
        QueryFn = QueryFn,
        RowKeyFn = RowKeyFn,
        ColumnsFn = ColumnsFn,
        IncludeNullOrEmpty = IncludeNullOrEmpty,
        AllKeys = AllKeys == null ? null : new(AllKeys),
        Selected = new(Selected),
    };

    /// <inheritdoc/>
    public override EFValuesInSetDef<TDbContext, VT, RT, KT> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        return new(ValueToKeyExpr, ser.StartFull, AlwaysInMemory)
        {
            IncludeNullOrEmpty = ser.IncludeNullOrEmpty,
            AllKeys = ser.AllKeys?.ToHashSet(),
            Selected = ser.Selected.ToHashSet(),
        };
    }

    /// <inheritdoc/>
    public EFValuesInSetDef<TDbContext, VT, RT, KT> InjectParamsInto(EFValuesInSetDef<TDbContext, VT, RT, KT> toBeInjected) =>
        new(ValueToKeyExpr, toBeInjected.StartFull(), AlwaysInMemory)
        {
            DbContextFactoryFn = DbContextFactoryFn,
            QueryFn = QueryFn,
            RowKeyFn = RowKeyFn,
            ColumnsFn = ColumnsFn,
            IncludeNullOrEmpty = toBeInjected.IncludeNullOrEmpty,
            AllKeys = toBeInjected.AllKeys,
            Selected = toBeInjected.Selected,
        };

    private static readonly FilterDefRendererType<EFValuesInSetDefRenderer<TDbContext, VT, RT, KT>> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor
}

/// <summary>The editor of an <see cref="EFValuesInSetDef{TDbContext, VT, RT, KT}"/>: the "Include null or empty" box and the rows.</summary>
public class EFValuesInSetDefRenderer<TDbContext, VT, RT, KT> : ADynaValuesInSetDefRenderer<EFValuesInSetDef<TDbContext, VT, RT, KT>, VT, RT, KT>
    where TDbContext : DbContext where RT : class
{
}
