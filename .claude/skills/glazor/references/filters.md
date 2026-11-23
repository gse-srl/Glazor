# Glazor filters

Contents:
- [Built-in definitions](#built-in-definitions)
- [Reading and setting filters from code](#reading-and-setting-filters-from-code)
- [Writing your own: a database-backed multi-select](#writing-your-own-a-database-backed-multi-select)
- [Trap 1: `IWithInjectedParams`](#trap-1-iwithinjectedparams)
- [Trap 2: state the `Serialized` record does not carry](#trap-2-state-the-serialized-record-does-not-carry)
- [`IsStillApplicable`: discarding a filter that no longer makes sense](#isstillapplicable-discarding-a-filter-that-no-longer-makes-sense)

## Built-in definitions

`Glazor.Grds.Filters`. A `FilterDefFactory` picks one per column from the column's value type, so a reasonable
editor appears without you doing anything.

| Filter | For |
| --- | --- |
| `DefaultDef` | the columns without a filter of their own (strings, `Guid`, `TimeSpan`, `object`, custom types), matched on their text — contains or starts-with, by its `Contains` flag; on EF the text is the database's own cast. In memory it ignores case; pushed down, case follows the database's collation (ignored on SQL Server's default, respected on a case-sensitive one) |
| `NumberDef<T>`, `DateDef<T>` | ranges over numbers and dates |
| `BoolDef` | tri-state true / false / all, with an include-nulls option |
| `EnumDef` | multi-select over an enum's members. `[Flags]` enums are not supported: membership is exact, so `A \| B` is kept only when that exact value is selected; give such a column a filter of your own or `Filterable="false"` |
| `SetDef`, `DynaSetDef` | multi-select over a fixed or lazily-loaded value set. `SetDef` persists the values themselves: after a restore, a value type without value equality (not a string, a record, or a type overriding `Equals`) matches no row, so over entities use a `DynaSetDef` with a key. It persists its candidates too, so a restored one offers the values it was saved with: when they change over time, use a `DynaSetDef`, which reads them again each time its dialog opens |
| `EFDistinctDef` | multi-select over the distinct values the column has in a query, read each time its dialog opens. Ready to use, with nothing to derive, and the column may hold a value type too. In `Glazor.EntityFrameworkCore`: see below |
| `EFValuesInSetDef` | multi-select over a collection column, with dialog rows read from a query and shown in columns you give. Ready to use, with nothing to derive; the items may be a value type (ids). In `Glazor.EntityFrameworkCore`: see § A dialog with rows of its own |
| `AValuesInSetDef` subclasses | columns whose value is itself a collection — matches on intersection |

The factory falls through to `DefaultDef` for anything it does not recognise, and the column keeps its own type, so a
renderer still gets the original value. Pushed down, the text is what the database's cast produces, not .NET's
`ToString()`, so formats can differ (a `Guid`'s case on SQL Server), a custom type behind a value converter is matched on
the text the converter stores, and a type the database cannot convert to text fails when the filtered query runs. That is why a
collection-valued column needs `Filterable="false"` or a real `FilterDef`: a string contains filter over an
`IReadOnlyList<string>` is meaningless, and on an EF model it gets composed into the query. The exception is numeric types that `InputNumber` cannot edit (`byte`, `uint`, `Half`, …): those throw instead of falling through: when a markup `<GridColumn>` registers, or, for a column added with `AddColumn`, when its filter is first asked for. A type with a filter of its own (an enum, a number, a date) gets it from the factory: a `DefaultDef` forced on it is not supported.

The "Include null" box appears only when the column's value can be null: `Nullable<T>`, or a reference type whose
expression accessor (`EFProperty`, `AddEFColumn`) reads a member chain the nullable annotations do not mark non-null all
the way, a nullable navigation on the path included. A `Func` accessor (`Property`, `AddColumn`) or a computed expression
keeps the box.

## Reading and setting filters from code

```csharp
bool anyActive = grid.IsFiltered();
bool onPrice   = grid.HasFilter("Price");

grid.SetFilter("Price", new NumberDef<decimal> { From = 100 });
AFilterDef? current = grid.GetFilter("Price");
grid.SetFilter("Price", null);                        // clear one

IReadOnlyDictionary<string, AFilterDef> all = grid.GetFilters();
grid.SetFilters(all);                                 // restore in bulk
```

With an `EFDataGridModel` and `EFProperty` columns these become `WHERE` clauses. With `Property` columns, or
any list-backed model, they are applied in memory. An incrementally-read model still tests every row, as it reads it:
it queries again until enough rows pass, so a filter that admits few rows costs many queries.

## The values a column has: `EFDistinctDef`

Before writing a definition, check whether the filter is "the values this column has": `EFDistinctDef<TDbContext,
TGridItem, VT>`, in `Glazor.EntityFrameworkCore`, is that, with nothing to derive.

```razor
<GridColumn EFProperty="@(o => o.Status)" Name="Status" Label="Status" FilterDef="_statusFDef" />
```

```csharp
private readonly EFDistinctDef<AppDbContext, Order, string> _statusFDef;

public Orders() => _statusFDef = new(startFull: true)
{
    DbContextFactoryFn = () => DbContextFactory,   // a Func: the constructor runs before injection
    QueryFn = db => db.Orders.Where(o => o.CustomerId == _customerId),
    ColumnExpr = o => o.Status,
};
```

- `QueryFn`: the rows whose values are offered. Give it the grid's base query, so a user is offered what the grid can
  show and no other.
- `ColumnExpr`: the value read. `VT` is the column's type, the nullable one for a nullable column (`int?`, `string?`); a value type
  is fine, which `DynaSetDef` does not take. Nulls are left out: the "Include null" box stands for them.
- `ColumnsFn`, optional: adds the dialog's columns, `(model, texts) => model.AddColumn(...)`, each row a
  `DistinctRowValue<VT>` whose column shows what it derives from `r.Value`, a label for a code for instance. The selection
  holds the values whatever the columns show. Without it one "Value" column, sorted when the type can be compared.

It implements `IWithInjectedParams` itself, so a restored memento keeps the selection and takes the rest from the
column's own definition. `VT` is persisted: use a type System.Text.Json writes. With an allow-list in a
`FilterDefFactory`, list the closed type, `typeof(EFDistinctDef<AppDbContext, Order, string>)`.

Values from somewhere else: derive from `DistinctDef<SELF, VT>` and implement `RetrieveValuesAsync`, `Clone` and `Read`.

## A dialog with rows of its own

The dialog of a set definition is a small grid of candidate rows. To show more than the value (a name beside a code, fields
of another table), derive from the base with a row type `RT` of its own, a class:

- a column holding a value: `ADynaSetDef<SELF, VT, RT, KT>`, editor `ADynaSetDefRenderer<SELF, VT, RT, KT>`;
- a column holding a collection, `IQueryable<VT>`: `ADynaValuesInSetDef<SELF, VT, RT, KT>`, editor
  `ADynaValuesInSetDefRenderer<SELF, VT, RT, KT>`.

Implement `CreateAllValuesModel()` (the dialog's `DataGridModel<RT>`, one `AddColumn` per field to show; `Texts` labels them
in the dialog's culture), `RetrieveAllValuesAsync(ct)` (the rows, read when the dialog opens), `RowToKey(RT)` (a row's key,
which must equal the key the value or item gives through the base constructor's expression), `Clone`, `Read` and a
parameterless constructor. `Renderer()` returns an editor of your own: the bases name none, and a one-line class deriving
from the editor above is enough. `VT` may be a value type. Sample: `docs/grigliaria.md` § A dialog with rows of its own.

Over a collection whose rows come from EF, derive nothing: `EFValuesInSetDef<TDbContext, VT, RT, KT>` takes the item's key
expression and `startFull` in its constructor, and `DbContextFactoryFn`, `QueryFn` (the rows) and `RowKeyFn` (a row's key)
as properties. Optional, as on `EFDistinctDef`: `ColumnsFn` (adds the dialog's columns, `(model, texts) =>
model.AddColumn(...)`; without it one sorted column shows each row's `ToString()`). It implements
`IWithInjectedParams`, so a restored one takes all of that, the key expression included, from the column's own.

In every set whose dialog reads its values, every value selected means every value, those added later included: void with
"Include null", any value but null without it. One value left out makes it the values selected only.

## Writing your own: a database-backed multi-select

A common case: a column whose value is a collection of roles or
categories, and the filter should offer the ones that exist in the database as a checkable list.

Derive from `DynaValuesInSetDef<SELF, VT, KT>` — self type, the item type, the key type; the column is an
`IQueryable<VT>`, and the dialog lists the items themselves (rows of another type: § A dialog with rows of its own). The abstract members are `CreateAllValuesModel`, `RetrieveAllValuesAsync`, `Clone` and
`Read`, plus a parameterless constructor for the deserializer's prototype.

```csharp
public class RolesFDef
    : DynaValuesInSetDef<RolesFDef, Role, int>,
      IWithInjectedParams<RolesFDef>
{
    // A Func, not the factory itself: the definition may be constructed in a component's constructor,
    // where an injected property is not set yet.
    public Func<IDbContextFactory<AppDbContext>> DbContextFactoryFn { get; init; } = null!;

    protected RolesFDef() : this(false) { }        // the deserializer's prototype; may be non-public
    public RolesFDef(bool startFull) : base(true, r => r.Id, startFull) { }

    public override RolesFDef Clone() => new(StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,
        IncludeNullOrEmpty = IncludeNullOrEmpty,
        AllKeys            = AllKeys == null ? null : new(AllKeys),
        Selected           = new(Selected),
    };

    // The little grid the user picks values in. Columns go on the model directly, not as markup.
    // `Texts` is the dialog's IGlazorTexts, for a label that follows the culture: Texts.Get(TextKeys.Value).
    protected override DataGridModel<Role> CreateAllValuesModel()
    {
        DataGridModel<Role> model = new() { ItemKeyFn = x => x.Id };
        model.AddColumn("id", new Column<Role, int>("Id")
        {
            Alignment = Alignment.Center, PreferredSize = 5,
        }, v => v.Id);
        model.AddColumn("name", new Column<Role, string?>("Role")
        {
            Alignment = Alignment.Center, PreferredSize = 20,
        }, v => v.Name);
        return model;
    }

    // Called each time the user opens this filter.
    protected override async ValueTask<IList<Role>> RetrieveAllValuesAsync(CancellationToken ct)
    {
        using var db = await DbContextFactoryFn().CreateDbContextAsync(ct);
        return await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .ToListAsync(ct);
    }

    public override RolesFDef Read(ref Utf8JsonReader reader, Type typeToConvert,
                                   JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        return new(ser.StartFull)  // true only when it was saved full and never populated
        {
            IncludeNullOrEmpty = ser.IncludeNullOrEmpty,
            AllKeys            = ser.AllKeys?.ToHashSet(),
            Selected           = ser.Selected.ToHashSet(),
        };
    }

    public RolesFDef InjectParamsInto(RolesFDef toBeInjected) => new(toBeInjected.StartFull())
    {
        DbContextFactoryFn = DbContextFactoryFn,                  // ours
        IncludeNullOrEmpty = toBeInjected.IncludeNullOrEmpty,     // theirs
        AllKeys            = toBeInjected.AllKeys,
        Selected           = toBeInjected.Selected,
    };
}
```

Construct the live instance yourself so it carries what it needs, and hand it to the column:

```csharp
_rolesFDef = new(startFull: true) { DbContextFactoryFn = () => DbContextFactory };
```

```razor
<GridColumn Property="@(x => x.User.Roles.AsQueryable())" Name="Roles" Label="Roles"
            PreferredSize="40" Sortable="false" FilterDef="_rolesFDef" />
```

`Property` with `.AsQueryable()`, not `EFProperty`: the *matching* happens in memory against the collection,
while the *candidate values* come from your own query in `RetrieveAllValuesAsync`.

## Trap 1: `IWithInjectedParams`

A filter definition restored from a persisted `Memento` is built by `Activator.CreateInstance` on the
parameterless constructor. **There is no dependency injection in that path.** A definition that needs a
`DbContextFactory` comes back with it null and throws the moment the user opens the filter.

`IWithInjectedParams<SELF>` closes the gap. `ADataGrid.RestoreMemento` walks the restored definitions and, for
each one implementing the interface, calls `InjectParamsInto` **on the live definition declared on the
column**, letting that instance rebuild the restored one with its own injected state:

```csharp
if (filterDef is IWithInjectedParams)
{
    (_, var pdef) = GetPredefinedFilterDef(columnName);
    if ((pdef as IWithInjectedParams)?.InjectParamsInto(filterDef) is { } injected)
        return injected;
    // warning, and the filter is dropped
}
```

The restored filter is the one `InjectParamsInto` returns, so it has the class that method builds: `RolesFDef` above always
returns a `RolesFDef`. To show fewer candidates, give the definition a property for the limit, copied by `Clone` and taken
from `this` in `InjectParamsInto` as `DbContextFactoryFn` is, rather than a subclass: the limit then comes back with every
restore. A subclass keeps its class only if it overrides `Clone` and re-implements `IWithInjectedParams<RolesFDef>` to
return its own class; it cannot declare the interface for itself. Its injector copies everything but `AllKeys`, which a
subclass cannot read from a `RolesFDef` (CS1540); the dialog fills them again when it opens. A restored definition the
column's live definition does not take, because that is no longer an injector or its `SELF` is another class, after a
release that changed it or as a filter the host set in code, is dropped with a warning instead.

So: **if your definition holds anything that came from DI or from the page, implement
`IWithInjectedParams<SELF>`.** Inside `InjectParamsInto`, take the plumbing from `this` and the user state from
`toBeInjected`. Reverse those and you restore a filter with the right plumbing and the wrong selection — which
is worse than a crash, because it looks like it worked.

## Trap 2: state the `Serialized` record does not carry

`AFilterDef.Equals` and `GetHashCode` are derived from **the record the definition serializes to**, together
with its runtime type:

```csharp
public abstract class AFilterDef : IEquatable<AFilterDef>
{
    public bool Equals(AFilterDef? other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != this.GetType()) return false;
        return CreateSerRecord().Equals(other.CreateSerRecord());
    }
}
```

**So if you add state to a definition, you must extend its `Serialized` record and override
`CreateSerRecord` — otherwise the new field reaches neither the payload nor equality.** No compile error, no
runtime error: two definitions differing only in your field compare *equal*, and the field vanishes from every
persisted layout.

Doing it correctly means five things, and nothing fails at build time when one is missing:

```csharp
// 1. derive the record. Give it its own name rather than shadowing `Serialized` — a nested
//    `new record Serialized` compiles but invites CS0109 confusion for the next reader.
public record BCRSerialized(IEnumerable<int>? AllKeys, bool IncludeNullOrEmpty,
                            IEnumerable<int> Selected, bool StartFull = false, int? CompanyId = null)
    : DynaValuesInSetDef<ByCompanyRolesFDef, CompanyRole, int>
          .Serialized(AllKeys, IncludeNullOrEmpty, Selected, StartFull)
{
    protected static readonly new IEqualityComparer<BCRSerialized> comparer =
        EqComparers.Combo<BCRSerialized>(l => l.Add(l => l?.CompanyId));

    public virtual bool Equals(BCRSerialized? other)
    {
        // base.Equals supplies the null guard, the EqualityContract type check and the
        // reference short-circuit, then compares the four inherited members.
        if (!base.Equals(other))
            return false;
        return comparer.Equals(this, other);
    }

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), comparer.GetHashCode(this));
}

// 2. return it
protected override BCRSerialized CreateSerRecord()
    => new(AllKeys, IncludeNullOrEmpty, Selected, StartFull(), _companyId);

// 3. restore it
public override ByCompanyRolesFDef Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
{
    BCRSerialized ser = JsonSerializer.Deserialize<BCRSerialized>(ref reader, o).RequireNotNull();
    return new(ser.StartFull) { _companyId = ser.CompanyId, /* … */ };
}

// 4. copy it: the grid clones a definition before the dialog edits it, and the model clones it again when it applies it
public override ByCompanyRolesFDef Clone() => new(StartFull())
{
    _companyId = _companyId, CurrentCompanyIdFn = CurrentCompanyIdFn, /* … as RolesFDef */
};

// 5. keep it through a restore: the scope is the restored definition's, the delegate is the column's own
public ByCompanyRolesFDef InjectParamsInto(ByCompanyRolesFDef toBeInjected) => new(toBeInjected.StartFull())
{
    _companyId = toBeInjected._companyId, CurrentCompanyIdFn = CurrentCompanyIdFn, /* … as RolesFDef */
};
```

Steps 4 and 5 are the ones that fail quietly. The definition the model applies is a clone of the one the dialog filled, so a
`Clone` that leaves the new state out hands the model a definition without it; an `InjectParamsInto` that leaves it out
does the same to every restored filter.

That `base.Equals` call looks wrong and is not. `Serialized`'s own base, `AFilterDefSer`, *is* a record, so the
compiler-generated `Equals` there supplies the null check, the `EqualityContract` comparison and the reference
short-circuit. The same idiom on a record whose base is **not** a record would silently reduce to
`object.Equals` — reference equality — and break everything.

If the host persists with options that require constructor parameters and skip nulls or defaults
(`JsonSerializerOptions.Strict` with a `DefaultIgnoreCondition`), a parameter such as `int? CompanyId` left out of the
JSON fails the read. Give it a default equal to its type's own (`null`, `false`, `0`) when it and every parameter after
it can have one: a missing member then reads back as that default, JSON saved without it included. Where a parameter
that cannot have a default follows, mark it `[property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]` instead, so
it is always written. The built-in records do one or the other.

Persistence resolves a definition's type by name via `Type.GetType`, deliberately, so you can add a definition
and have it persist and restore with **no registration step**. Assembly version, culture and public key token
are stripped from the stored name, so rebuilding your assembly does not invalidate saved layouts.

## `IsStillApplicable`: discarding a filter that no longer makes sense

This is the reason a real definition carries extra state, and it is worth the paragraph.

Consider a roles filter whose candidate values are scoped to the current company. A user filters by two roles,
navigates away, switches company, comes back — and the persisted filter's selected keys refer to roles that
belong to a company they are no longer looking at. Applied silently, the grid shows nothing and the user has no
idea why.

`AFilterDef.IsStillApplicable()` exists for exactly this: return `false` and the filter is discarded rather
than applied. The definition records the scope it queried for, and compares:

```csharp
public Func<int?> CurrentCompanyIdFn { get; init; } = null!;

// The company the candidate values were queried for.
private int? _companyId;

public override bool IsStillApplicable() => _companyId == CurrentCompanyIdFn.Invoke();

protected override async ValueTask<IList<CompanyRole>> RetrieveAllValuesAsync(CancellationToken ct)
{
    _companyId = CurrentCompanyIdFn.Invoke();      // remember the scope, then query in it
    using var db = await DbContextFactoryFn().CreateDbContextAsync(ct);
    var q = db.CompanyRoles.AsNoTracking();
    if (_companyId != null)
        q = q.Where(cr => cr.CompanyId == _companyId);
    return await q.OrderBy(cr => cr.Name).ToListAsync(ct);
}
```

Which is what ties the two traps together: `_companyId` has to be in the `Serialized` record (trap 2) or a
restored filter comes back with it null and cannot tell whether it is stale; and `CurrentCompanyIdFn` has to be
re-supplied through `InjectParamsInto` (trap 1) or `IsStillApplicable` throws on the null delegate. `_companyId` also
has to be copied by `Clone` and taken from `toBeInjected` in `InjectParamsInto`: left out of either, the definition
the model holds has a null scope, `IsStillApplicable` answers false whenever a company is current, and the filter is
dropped as soon as the user presses OK, or at every restore. Miss any of these and the mechanism fails quietly, showing the user wrong data.
