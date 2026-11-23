using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;

namespace Glazor.EntityFrameworkCore;

/// <summary>
/// A multi-select over the distinct values a column has in the database: the values to choose from are read each time the dialog
/// opens, from the rows <see cref="QueryFn"/> gives, so a query narrowed to what a user may see offers the values found there
/// and no other. Hand it to the column as its <c>FilterDef</c>; on a column the database filters it is pushed down as the
/// built-in sets are.
/// <code>
/// _statusFDef = new(startFull: true)
/// {
///     DbContextFactoryFn = () => DbContextFactory,
///     QueryFn = db => db.Orders.Where(o => o.CustomerId == _customerId),
///     ColumnExpr = o => o.Status,
/// };
/// </code>
/// What the page supplies is not persisted: a definition restored from a memento takes it from the column's own, as
/// <see cref="IWithInjectedParams{SELF}"/> describes, and keeps the selection it was saved with.
/// </summary>
/// <typeparam name="TDbContext">The EF Core context the values are read through.</typeparam>
/// <typeparam name="TGridItem">The type of the rows <see cref="QueryFn"/> gives, the grid's for one.</typeparam>
/// <typeparam name="VT">The type of the column's value, the nullable type when the column is nullable.</typeparam>
public class EFDistinctDef<TDbContext, TGridItem, VT> : DistinctDef<EFDistinctDef<TDbContext, TGridItem, VT>, VT>,
        IWithInjectedParams<EFDistinctDef<TDbContext, TGridItem, VT>>
    where TDbContext : DbContext
{
    // A Func, not the factory itself: the definition may be constructed in a component's constructor, where an injected
    // property is not set yet
    /// <summary>The factory of the contexts the values are read through, called each time they are read.</summary>
    public Func<IDbContextFactory<TDbContext>> DbContextFactoryFn { get; init; } = null!;

    /// <summary>The rows whose values are offered: the grid's base query, for the values the grid can show.</summary>
    public Func<TDbContext, IQueryable<TGridItem>> QueryFn { get; init; } = null!;

    /// <summary>The value read from a row: the column's own, which the selection holds.</summary>
    public Expression<Func<TGridItem, VT?>> ColumnExpr { get; init; } = null!;

    /// <summary>
    /// Adds the dialog's columns to its model, each showing what it derives from a row's <c>Value</c>, a label for a code for
    /// instance, labelled with the dialog's texts when they follow the culture; it may sort the model too. The selection holds
    /// the values whatever the columns show. Null, the default: one column, "Value", with the values, sorted when their type
    /// can be compared.
    /// </summary>
    public Action<DataGridModel<DistinctRowValue<VT>>, IGlazorTexts>? ColumnsFn { get; init; }

    /// <summary>The prototype a restore reads into.</summary>
    protected EFDistinctDef() : this(false)
    {
    }

    /// <summary>A filter with nothing selected, or with every value when <paramref name="startFull"/>, those read later included.</summary>
    public EFDistinctDef(bool startFull) : base(startFull)
    {
    }

    /// <inheritdoc/>
    public override EFDistinctDef<TDbContext, TGridItem, VT> Clone() => new(StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,
        QueryFn = QueryFn,
        ColumnExpr = ColumnExpr,
        ColumnsFn = ColumnsFn,
        IncludeNull = IncludeNull,
        AllKeys = AllKeys == null ? null : new(AllKeys),
        Selected = new(Selected),
    };

    /// <inheritdoc/>
    protected override async ValueTask<IEnumerable<VT?>> RetrieveValuesAsync(CancellationToken ct)
    {
        await using TDbContext db = await DbContextFactoryFn().CreateDbContextAsync(ct);
        return await QueryFn(db).Select(ColumnExpr).Distinct().ToListAsync(ct);
    }

    /// <inheritdoc/>
    protected override DataGridModel<DistinctRowValue<VT>> CreateAllValuesModel()
    {
        if (ColumnsFn == null)
            return base.CreateAllValuesModel();
        DataGridModel<DistinctRowValue<VT>> model = new();
        ColumnsFn(model, Texts);
        return model;
    }

    /// <inheritdoc/>
    public override EFDistinctDef<TDbContext, TGridItem, VT> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        return new(ser.StartFull)
        {
            IncludeNull = ser.IncludeNull,
            AllKeys = ser.AllKeys?.ToHashSet(),
            Selected = ser.Selected.ToHashSet(),
        };
    }

    /// <inheritdoc/>
    public EFDistinctDef<TDbContext, TGridItem, VT> InjectParamsInto(EFDistinctDef<TDbContext, TGridItem, VT> toBeInjected) => new(toBeInjected.StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,
        QueryFn = QueryFn,
        ColumnExpr = ColumnExpr,
        ColumnsFn = ColumnsFn,
        IncludeNull = toBeInjected.IncludeNull,
        AllKeys = toBeInjected.AllKeys,
        Selected = toBeInjected.Selected,
    };
}
