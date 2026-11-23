using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Text.Json;
using System.Reflection;
using static Glazor.Grds.Filters.BoolDef;

namespace Glazor.Tests;

/// <summary>
/// Small contracts that are easy to break without anything failing to compile: interface members that a
/// consumer reaches through the interface rather than the concrete type, and deserialization guards.
/// </summary>
public class ContractsTests
{
    [SetUp]
    public void Setup()
    {
    }

    /// <summary>
    /// <see cref="ICollection{T}.IsReadOnly"/> has to answer, not throw. Anything that inspects a collection
    /// generically — serializers, binding, LINQ helpers — may read it, and these lists are mutable.
    /// </summary>
    [Test]
    public void IsReadOnlyIsImplementedTest()
    {
        DataGridModel<RProduct> model = new([new RProduct("Vallers", 1_000)]);
        ICollection<RProduct> asList = model.GetData();
        Assert.That(asList.IsReadOnly, Is.False);

        ICollection<int> versioned = new VersionedList<int>([1, 2, 3], trackUpdates: false);
        Assert.That(versioned.IsReadOnly, Is.False);

        ICollection<string> chunked = new OnDiskChunkedList<string>(
            OnDiskChunkedList.MinChunksInMemory, OnDiskChunkedList.MinChunkSize);
        Assert.That(chunked.IsReadOnly, Is.False);
    }

    private sealed class PlainList(IList<string> list) : AList<string>(list)
    {
        protected override void OnChange(string? newItem) { }

        protected override void OnGoto(string? item) { }
    }

    /// <summary>A list built over an array takes an item, as one given an array through <c>Set</c> does: it says it is writable.</summary>
    [Test]
    public void AListBuiltOverAnArrayIsWritableTest()
    {
        var list = new PlainList(new[] { "a", "b" });
        list.Add("c");
        Assert.That(list, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    /// <summary>
    /// A persisted filter is host-application state that can be hand-edited or go stale across versions.
    /// <see cref="System.Text.Json"/> does not check that a numeric enum value is a declared member, so
    /// <c>BoolDef.Read</c> has to — otherwise the bad value survives deserialization and only surfaces later,
    /// from inside filtering during a render.
    /// </summary>
    [Test]
    public void BoolDefRejectsUndeclaredEnumValueTest()
    {
        string valid = Envelope("{\"IncludeNull\":false,\"Value\":1}");
        var def = (BoolDef)JsonSerializer.Deserialize<AFilterDef>(valid).RequireNotNull();
        Assert.That(def.Value, Is.EqualTo(BoolValue.True));
        Assert.That(def.IncludeNull, Is.False);

        // -1, 0 and 1 are All, False and True; 99 is not a member.
        string invalid = Envelope("{\"IncludeNull\":false,\"Value\":99}");
        Assert.That(() => JsonSerializer.Deserialize<AFilterDef>(invalid), Throws.Exception,
            "an undeclared BoolValue value must be rejected at deserialization time");
    }

    /// <summary>
    /// The persisted <c>defType</c> is a wire contract, and it is fed straight back into
    /// <see cref="Type.GetType(string, bool)"/>. Two properties have to hold together: no assembly identity
    /// in the string, so that a rebuild does not invalidate stored filters; and still resolvable, which the
    /// stripping could easily break for closed generics, where the version sits inside the brackets too.
    /// </summary>
    [Test]
    public void PersistedTypeNameCarriesNoAssemblyIdentityTest()
    {
        Type[] types = [
            typeof(BoolDef), typeof(DefaultDef), typeof(EnumDef),
            typeof(DateDef<DateTime>), typeof(DateDef<DateOnly>),
            typeof(NumberDef<int>), typeof(NumberDef<long>), typeof(NumberDef<decimal>),
        ];
        Assert.Multiple(() =>
        {
            foreach (Type type in types)
            {
                string name = FilterDefFactory.TypeName(type);
                Assert.That(name, Does.Not.Contain("Version="), $"{type}");
                Assert.That(name, Does.Not.Contain("Culture="), $"{type}");
                Assert.That(name, Does.Not.Contain("PublicKeyToken="), $"{type}");
                Assert.That(Type.GetType(name, false), Is.EqualTo(type),
                    $"{name} must still resolve, otherwise nothing deserializes");
            }
        });
        // Distinct closed generics must not collapse onto one name.
        Assert.That(FilterDefFactory.TypeName(typeof(NumberDef<int>)),
            Is.Not.EqualTo(FilterDefFactory.TypeName(typeof(NumberDef<long>))));
    }

    private static string Envelope(string def) =>
        $"{{\"defType\":\"{typeof(BoolDef).AssemblyQualifiedName}\",\"def\":{def}}}";

    private record RProduct(string Name, decimal Price);

    /// <summary>
    /// The in-memory models track the item to restore through a nullable field, which a struct defeats: a value-type item is
    /// refused by the compiler instead.
    /// </summary>
    [Test]
    public void InMemoryModelsRequireAReferenceTypeItemTest()
    {
        foreach (Type model in new[] { typeof(AMemGridModel<>), typeof(DataGridModel<>), typeof(GridModel<>), typeof(TreeGridModel<>), typeof(SetDef<>) })
        {
            Type item = model.GetGenericArguments()[0];
            Assert.That(item.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint), Is.True, model.Name);
        }
    }

    /// <summary>
    /// Replacing the rows keeps the current item current, where it now sits; SetAndGoto makes the item it is given current
    /// instead.
    /// </summary>
    [Test]
    public void SetKeepsTheCurrentItemAndSetAndGotoMovesToTheOneGivenTest()
    {
        RProduct a = new("A", 1), b = new("B", 2), c = new("C", 3);
        DataGridModel<RProduct> kept = new([a, b]);
        kept.SetCurrentRow(0);
        kept.GetData().Set(new List<RProduct> { c, a, b });
        DataGridModel<RProduct> moved = new([a, b]);
        moved.SetCurrentRow(0);
        moved.GetData().SetAndGoto(new List<RProduct> { c, a, b }, b);
        Assert.Multiple(() =>
        {
            Assert.That((kept.GetCurrentItem(), kept.GetCurrentRow()), Is.EqualTo((a, 1)), "Set");
            Assert.That((moved.GetCurrentItem(), moved.GetCurrentRow()), Is.EqualTo((b, 2)), "SetAndGoto");
        });
    }

    /// <summary>
    /// Without an ItemKeyFn the current row is found again with Equals, as ItemKeyFn's doc says: fresh copies of the rows keep
    /// the same record current, and SetAndGoto goes to a copy of the item it is given.
    /// </summary>
    [Test]
    public void KeylessModelRestoresTheCurrentRowByEqualsTest()
    {
        RProduct a = new("A", 1), b = new("B", 2), c = new("C", 3);
        foreach (AMemGridModel<RProduct> model in new AMemGridModel<RProduct>[] { new DataGridModel<RProduct>([a, b]), new GridModel<RProduct>([a, b]) })
        {
            model.SetCurrentRow(0);
            model.GetData().Set(new List<RProduct> { c, a with { }, b with { } });
            (RProduct?, int) afterSet = (model.GetCurrentItem(), model.GetCurrentRow());
            model.GetData().SetAndGoto(new List<RProduct> { b with { }, c with { }, a with { } }, c with { });
            Assert.Multiple(() =>
            {
                Assert.That(afterSet, Is.EqualTo((a, 1)), $"Set on {model.GetType().Name}");
                Assert.That((model.GetCurrentItem(), model.GetCurrentRow()), Is.EqualTo((c, 1)), $"SetAndGoto on {model.GetType().Name}");
            });
        }
    }

    /// <summary>
    /// SetAndGoto on a model that has not synced yet, as a page fills a new model, makes the item it is given current; Add
    /// before the first sync leaves the first row current.
    /// </summary>
    [Test]
    public void SetAndGotoBeforeTheFirstSyncMovesToTheOneGivenTest()
    {
        RProduct a = new("A", 1), b = new("B", 2), c = new("C", 3);
        DataGridModel<RProduct> moved = new();
        moved.GetData().SetAndGoto(new List<RProduct> { a, b, c }, c);
        GridModel<RProduct> ordered = new();
        ordered.GetData().SetAndGoto(new List<RProduct> { a, b, c }, c);
        DataGridModel<RProduct> added = new();
        added.GetData().Add(a);
        added.GetData().Add(b);
        GridModel<RProduct> orderedAdded = new();
        orderedAdded.GetData().Add(a);
        orderedAdded.GetData().Add(b);
        Assert.Multiple(() =>
        {
            Assert.That((moved.GetCurrentItem(), moved.GetCurrentRow()), Is.EqualTo((c, 2)), "DataGridModel");
            Assert.That((ordered.GetCurrentItem(), ordered.GetCurrentRow()), Is.EqualTo((c, 2)), "GridModel");
            Assert.That(added.GetCurrentRow(), Is.EqualTo(0), "Add");
            Assert.That(orderedAdded.GetCurrentRow(), Is.EqualTo(0), "Add on GridModel");
        });
    }

    /// <summary>On a model that has synced, Add makes the new row current, as Insert does; AddRange leaves the current row where it is.</summary>
    [Test]
    public void AddMovesTheCurrentRowAndAddRangeDoesNotTest()
    {
        RProduct a = new("A", 1), b = new("B", 2), c = new("C", 3), d = new("D", 4), e = new("E", 5);
        foreach (AMemGridModel<RProduct> model in new AMemGridModel<RProduct>[] { new DataGridModel<RProduct>([a, b]), new GridModel<RProduct>([a, b]) })
        {
            model.SetCurrentRow(0);
            model.GetRowCount();
            model.GetData().Add(c);
            model.GetRowCount();
            (RProduct?, int) afterAdd = (model.GetCurrentItem(), model.GetCurrentRow());
            model.GetData().AddRange([d, e]);
            model.GetRowCount();
            Assert.Multiple(() =>
            {
                Assert.That(afterAdd, Is.EqualTo((c, 2)), $"Add on {model.GetType().Name}");
                Assert.That((model.GetCurrentItem(), model.GetCurrentRow()), Is.EqualTo((c, 2)), $"AddRange on {model.GetType().Name}");
            });
        }
    }

    /// <summary>
    /// SetAndGoto makes the item it is given current also after another change in the same handler, which has already captured
    /// the current row to restore.
    /// </summary>
    [Test]
    public void SetAndGotoAfterAnotherChangeStillGoesToItsItemTest()
    {
        RProduct a = new("A", 1), b = new("B", 2), c = new("C", 3);
        DataGridModel<RProduct> sorted = new([a, b]);
        sorted.AddColumn("Name", new Column<RProduct, string>("Name"), p => p.Name);
        sorted.SetCurrentRow(0);
        sorted.SetSortedColumn("Name", false);
        sorted.GetData().SetAndGoto(new List<RProduct> { a, b, c }, c);
        GridModel<RProduct> ordered = new([a, b]);
        ordered.SetCurrentRow(0);
        ordered.GetData().Remove(b);
        ordered.GetData().SetAndGoto(new List<RProduct> { a, c }, c);
        Assert.Multiple(() =>
        {
            Assert.That(sorted.GetCurrentItem(), Is.SameAs(c), "after a sort");
            Assert.That(ordered.GetCurrentItem(), Is.SameAs(c), "after a removal");
        });
    }

    /// <summary>
    /// On a sorted and filtered model, a goto to a row the filter hides keeps the row that was current, also after an earlier
    /// change in the same handler moved the rows above it.
    /// </summary>
    [Test]
    public void GotoHiddenByTheFilterKeepsTheCurrentRowTest()
    {
        RProduct a = new("a", 1), c = new("c", 3), e = new("e", 5), g = new("g", 7);
        RProduct b = new("b", 2), z = new("z", 500);
        DataGridModel<RProduct> model = new([a, c, e, g]);
        model.AddColumn("Name", new Column<RProduct, string>("Name"), p => p.Name);
        model.AddColumn("Price", new Column<RProduct, decimal>("Price"), p => p.Price);
        model.SetSortedColumn("Name", true);
        model.ApplyFilters([new("Price", new NumberDef<decimal> { From = 0, To = 100 })]);
        model.SetCurrentRow(2);
        model.GetData().Add(b);
        model.GetData().Add(z);
        Assert.That((model.GetCurrentItem(), model.GetCurrentRow()), Is.EqualTo((e, 3)));
    }

    /// <summary>A goto to a row the new rows lack keeps the row that was current, sorted or not.</summary>
    [Test]
    public void GotoToAMissingRowKeepsTheCurrentRowUnsortedTooTest()
    {
        RProduct a = new("a", 1), b = new("b", 2), c = new("c", 3), d = new("d", 4), x = new("0", 0), q = new("q", 9);
        foreach (bool sorted in new[] { false, true })
        {
            DataGridModel<RProduct> model = new([a, b, c, d]) { ItemKeyFn = p => p.Name };
            model.AddColumn("Name", new Column<RProduct, string>("Name"), p => p.Name);
            if (sorted)
                model.SetSortedColumn("Name", true);
            model.SetCurrentRow(2);
            model.GetData().SetAndGoto(new List<RProduct> { x, a, b, c, d }, q);
            Assert.That((model.GetCurrentItem(), model.GetCurrentRow()), Is.EqualTo((c, 3)), $"sorted: {sorted}");
        }
    }

    /// <summary>On the ordered model too, a goto to a row the new rows lack keeps the row that was current.</summary>
    [Test]
    public void GotoToAMissingRowKeepsTheCurrentRowOnTheOrderedModelTest()
    {
        RProduct a = new("a", 1), b = new("b", 2), c = new("c", 3), d = new("d", 4), x = new("0", 0), q = new("q", 9);
        GridModel<RProduct> model = new([a, b, c, d]) { ItemKeyFn = p => p.Name };
        model.SetCurrentRow(2);
        model.GetData().SetAndGoto(new List<RProduct> { x, a, b, c, d }, q);
        Assert.That((model.GetCurrentItem(), model.GetCurrentRow()), Is.EqualTo((c, 3)));
    }

    /// <summary>
    /// Filters naming a column the model does not have, or one column twice, are refused at the call, as a sort on an unknown
    /// column is, and leave the filters as they were.
    /// </summary>
    [Test]
    public void FiltersOnAnUnknownOrRepeatedColumnAreRefusedAtTheCallTest()
    {
        DataGridModel<RProduct> model = new([new RProduct("A", 1), new RProduct("B", 2)]);
        model.AddColumn("Name", new Column<RProduct, string>("Name"), p => p.Name);
        KeyValuePair<string, AFilterDef> nameIsA = new("Name", new DefaultDef() { Value = "A", Contains = false });
        model.ApplyFilters([nameIsA]);
        Assert.Multiple(() =>
        {
            Assert.That(() => model.ApplyFilters([new("Missing", new DefaultDef() { Value = "x" })]), Throws.ArgumentException);
            Assert.That(() => model.ApplyFilters([nameIsA, nameIsA]), Throws.ArgumentException);
            Assert.That(model.GetFilters().Keys, Is.EqualTo(new[] { "Name" }), "the filters as they were");
            Assert.That(model.GetRowCount(), Is.EqualTo(1), "still filtered");
        });
    }

    /// <summary>
    /// An in-memory model answers a fetch past its end with the rows it has, as the interface says and the paged EF model does.
    /// </summary>
    [Test]
    public async Task InMemoryFetchPastTheEndGivesTheRowsFoundTest()
    {
        DataGridModel<RProduct> model = new([new RProduct("A", 1), new RProduct("B", 2), new RProduct("C", 3)]);
        model.AddColumn("Name", new Column<RProduct, string>("Name"), p => p.Name);
        Assert.That(await model.FetchRowsAsync(1, 10), Is.EqualTo(2));
        Assert.That(await model.FetchRowsAsync(5, 10), Is.Zero);
    }

    /// <summary>
    /// Removing an item the list does not hold changes nothing, so the model is not sent through a new sync for it.
    /// </summary>
    [Test]
    public void RemovingAMissingItemLeavesTheModelInSyncTest()
    {
        RProduct b = new("B", 2);
        DataGridModel<RProduct> model = new([new RProduct("A", 1), b]);
        model.AddColumn("Name", new Column<RProduct, string>("Name"), p => p.Name);
        model.GetRowCount();   // the first sync
        int version = model.GetDataVersion();
        Assert.Multiple(() =>
        {
            Assert.That(model.GetData().Remove(new RProduct("Z", 9)), Is.False);
            model.GetRowCount();   // a getter runs whatever sync is pending
            Assert.That(model.GetDataVersion(), Is.EqualTo(version), "no sync for a miss");
            Assert.That(model.GetData().Remove(b), Is.True);
            Assert.That(model.GetRowCount(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The wrapper grids declare again each parameter of the grid they wrap, so a parameter added to <c>Grigliaria</c> must be
    /// added to them too; only the editing ones are left out, as grigliaria.md states.
    /// </summary>
    [TestCase(typeof(MultiSelGrid<>))]
    [TestCase(typeof(Treegliaria<>))]
    public void AWrapperGridTakesEveryGrigliariaParameterTest(Type wrapper)
    {
        static IEnumerable<string> Params(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(Microsoft.AspNetCore.Components.ParameterAttribute), true)).Select(p => p.Name);
        var missing = Params(typeof(Grigliaria<>)).Except(Params(wrapper)).Except(["EditMode", "NewItem", "OnPost", "OnRemove"]);
        Assert.That(missing, Is.Empty);
    }

    /// <summary>A row whose Equals ignores its key, as a class that compares names only.</summary>
    private sealed class Named(int id, string name)
    {
        public int Id => id;
        public string Name => name;
        public override bool Equals(object? obj) => obj is Named other && other.Name == Name;
        public override int GetHashCode() => Name.GetHashCode();
    }

    /// <summary>
    /// With a key function the key alone tells rows apart, as in the current-row restore: two rows that are Equals but have
    /// different keys are different items. Without one, Equals still finds the row.
    /// </summary>
    [Test]
    public void KeyAloneTellsRowsApartTest()
    {
        Named first = new(1, "Same"), second = new(2, "Same");
        DataGridModel<Named> keyed = new([first, second]) { ItemKeyFn = n => n.Id };
        keyed.SetCurrentRow(0);
        int found = keyed.RowIndexOf(new Named(2, "Same"));
        keyed.SetCurrentItem(new Named(2, "Same"));
        DataGridModel<Named> unkeyed = new([first, second]);
        Assert.Multiple(() =>
        {
            Assert.That(found, Is.EqualTo(1), "the row with the key, not the current one that is Equals");
            Assert.That(keyed.GetCurrentRow(), Is.EqualTo(1), "SetCurrentItem moves to it");
            Assert.That(keyed.SameItem(first, second), Is.False);
            Assert.That(keyed.SameItem(second, new Named(2, "Other")), Is.True, "the same key");
            Assert.That(unkeyed.RowIndexOf(new Named(2, "Same")), Is.EqualTo(0), "no key: the first row that is Equals");
        });
    }
}
