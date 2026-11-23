using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Collections.Immutable;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// A filter definition derives its identity from the record <c>CreateSerRecord()</c> builds, so a member that
/// is a <em>set</em> has to be compared as one. The enumeration order of a HashSet follows its insertion and
/// removal history rather than its content, which is exactly what a user produces by clicking values on and
/// off in a filter dialog: comparing those members as sequences reports two identical filters as different.
/// <para>
/// The round trip tests here also cover the other half of persistence — a definition that cannot be
/// reconstructed by <see cref="FilterDefFactory"/> makes a saved layout unrestorable.
/// </para>
/// </summary>
public class FilterDefEqualityTests
{
    private enum Colour { RED, GREEN, BLUE, WHITE }

    private static EnumDef ColoursOf(params Colour[] selected)
    {
        EnumDef def = new(typeof(Colour), false) { IncludeNull = false };
        foreach (Colour c in selected)
            def.Selected.Add(c);
        return def;
    }

    // ───────────────────────────── set-valued members ─────────────────────────────

    [Test]
    public void EnumDefIgnoresTheOrderTheValuesWereSelectedInTest()
    {
        EnumDef a = ColoursOf(Colour.RED, Colour.WHITE);
        EnumDef b = ColoursOf(Colour.WHITE, Colour.RED);

        Assert.Multiple(() =>
        {
            Assert.That(a.Selected.SetEquals(b.Selected), Is.True, "the same members are selected");
            Assert.That(a, Is.EqualTo(b), "so it is the same filter");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()), "and it must hash alike");
        });
    }

    /// <summary>
    /// The realistic path: a HashSet reuses the slot freed by a removal, so selecting three values, dropping
    /// one and selecting a fourth leaves the elements in a different order from selecting the same three
    /// outright. Nothing about the resulting filter differs.
    /// </summary>
    [Test]
    public void EnumDefIgnoresTheSelectionHistoryTest()
    {
        EnumDef a = new(typeof(Colour), false) { IncludeNull = false };
        a.Selected.Add(Colour.RED);
        a.Selected.Add(Colour.GREEN);
        a.Selected.Add(Colour.BLUE);
        a.Selected.Remove(Colour.GREEN);
        a.Selected.Add(Colour.WHITE);

        EnumDef b = ColoursOf(Colour.RED, Colour.BLUE, Colour.WHITE);

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void EnumDefStillSeesARealDifferenceTest()
    {
        EnumDef two = ColoursOf(Colour.RED, Colour.WHITE);

        Assert.Multiple(() =>
        {
            Assert.That(ColoursOf(Colour.RED, Colour.BLUE), Is.Not.EqualTo(two), "a different member");
            Assert.That(ColoursOf(Colour.RED), Is.Not.EqualTo(two), "a missing member");
            Assert.That(ColoursOf(Colour.RED, Colour.WHITE, Colour.BLUE), Is.Not.EqualTo(two), "an extra member");
            Assert.That(new EnumDef(typeof(Colour), false) { IncludeNull = true }.Selected.Count,
                Is.EqualTo(0), "sanity: nothing selected by default");
        });
    }

    [Test]
    public void EnumDefIncludeNullStillCountsTest()
    {
        EnumDef a = ColoursOf(Colour.RED);
        EnumDef b = ColoursOf(Colour.RED);
        b.IncludeNull = !a.IncludeNull;

        Assert.That(a, Is.Not.EqualTo(b), "including nulls is part of the filter");
    }

    [Test]
    public void SetDefIgnoresTheOrderOfItsCandidateValuesTest()
    {
        SetDef<string> a = new(false) { AllValues = new[] { "x", "y", "z" } };
        a.Selected.Add("y");
        SetDef<string> b = new(false) { AllValues = new[] { "z", "y", "x" } };
        b.Selected.Add("y");

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b), "the candidate values are a set");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    /// <summary>
    /// A DefaultDef whose text was cleared filters as one never given a text, so it is the same filter: with nulls left out it
    /// stays in the memento, which must not change for it.
    /// </summary>
    [Test]
    public void DefaultDefWithAnEmptyTextIsTheOneWithNoneTest()
    {
        DefaultDef cleared = new() { IncludeNull = false, Value = "" };
        DefaultDef none = new() { IncludeNull = false };

        Assert.Multiple(() =>
        {
            Assert.That(cleared, Is.EqualTo(none));
            Assert.That(cleared.GetHashCode(), Is.EqualTo(none.GetHashCode()));
            Assert.That(JsonSerializer.Serialize<AFilterDef>(cleared), Is.EqualTo(JsonSerializer.Serialize<AFilterDef>(none)), "and they persist alike");
        });
    }

    // ───────────────────────────── restoring a saved filter ─────────────────────────────

    /// <summary>
    /// <see cref="FilterDefFactory.Read"/> builds a prototype through the parameterless c-tor before letting
    /// the definition read itself back, so a definition without one cannot be restored however well it
    /// serializes.
    /// </summary>
    [Test]
    public void SetDefRoundTripsThroughJsonTest()
    {
        SetDef<string> def = new(false) { AllValues = new[] { "x", "y", "z" }, IncludeNull = false };
        def.Selected.Add("y");

        string json = JsonSerializer.Serialize<AFilterDef>(def);
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(json).RequireNotNull();

        Assert.That(restored, Is.TypeOf<SetDef<string>>());
        SetDef<string> back = (SetDef<string>)restored;
        Assert.Multiple(() =>
        {
            Assert.That(back.AllValues, Is.EquivalentTo(def.AllValues), "the candidate values came back");
            Assert.That(back.Selected, Is.EquivalentTo(def.Selected), "so did the selection");
            Assert.That(back.IncludeNull, Is.EqualTo(def.IncludeNull));
            Assert.That(back, Is.EqualTo(def), "and it is recognised as the same filter");
        });
    }

    [Test]
    public void EnumDefRoundTripsThroughJsonTest()
    {
        EnumDef def = ColoursOf(Colour.WHITE, Colour.RED);

        string json = JsonSerializer.Serialize<AFilterDef>(def);
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(json).RequireNotNull();

        Assert.That(restored, Is.EqualTo(def));
    }

    /// <summary>
    /// The two halves together: a saved snapshot carrying a set-valued filter has to come back equal, which
    /// is what lets a host drop the redundant saves.
    /// </summary>
    [Test]
    public void AMementoCarryingASetFilterRoundTripsTest()
    {
        GridLayout layout = new();
        layout.columnsToShow = ["Colour"];
        Memento saved = new(
            layout,
            ImmutableDictionary<string, AFilterDef>.Empty.Add("Colour", ColoursOf(Colour.WHITE, Colour.RED)),
            [new("Colour", true)]);

        string json = JsonSerializer.Serialize(saved);
        Memento restored =
            JsonSerializer.Deserialize<Memento>(json).RequireNotNull();

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(saved));
            Assert.That(restored.GetHashCode(), Is.EqualTo(saved.GetHashCode()));
        });
    }

    /// <summary>
    /// Unlike Selected and AllKeys, which are HashSets, AllValues is a plain IEnumerable the caller supplies,
    /// so it can carry repetitions. It still denotes a set of candidate values.
    /// </summary>
    [Test]
    public void SetDefTreatsItsCandidateValuesAsASetTest()
    {
        SetDef<string> repeated = new(false) { AllValues = new[] { "x", "y", "x" } };
        SetDef<string> distinct = new(false) { AllValues = new[] { "x", "y" } };
        SetDef<string> different = new(false) { AllValues = new[] { "x", "x" } };

        Assert.Multiple(() =>
        {
            Assert.That(repeated, Is.EqualTo(distinct), "a repeated candidate value adds nothing");
            Assert.That(repeated.GetHashCode(), Is.EqualTo(distinct.GetHashCode()));
            Assert.That(different, Is.Not.EqualTo(distinct),
                "but two different candidate sets of the same length are not the same filter");
        });
    }

    // the shape docs/grigliaria.md tells users to write

    /// <summary>
    /// A concrete DynaValuesInSetDef, the pattern the documentation shows for filtering a collection-valued
    /// column against values loaded from a database. Nothing exercised this family before, and it is the one
    /// a library user writes by hand.
    /// </summary>
    private sealed class RolesFilterDef : DynaValuesInSetDef<RolesFilterDef, string, string>
    {
        public RolesFilterDef() : this(false) { }          // the deserialization prototype

        public RolesFilterDef(bool startFull) : base(true, v => v, startFull) { }

        internal void SeedKeys(params string[] keys) => AllKeys = new HashSet<string>(keys);

        public override RolesFilterDef Clone()
        {
            RolesFilterDef res = new(StartFull()) { IncludeNullOrEmpty = IncludeNullOrEmpty };
            if (AllKeys != null)
                res.SeedKeys([.. AllKeys]);
            foreach (string s in Selected)
                res.Selected.Add(s);
            return res;
        }

        protected override DataGridModel<string> CreateAllValuesModel() => new(AllKeys ?? []);

        protected override ValueTask<IList<string>> RetrieveAllValuesAsync(CancellationToken ct)
            => ValueTask.FromResult<IList<string>>([.. AllKeys ?? []]);

        public override RolesFilterDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
            RolesFilterDef res = new(false) { IncludeNullOrEmpty = ser.IncludeNullOrEmpty };
            if (ser.AllKeys != null)
                res.SeedKeys([.. ser.AllKeys]);
            foreach (string s in ser.Selected)
                res.Selected.Add(s);
            return res;
        }
    }

    private static RolesFilterDef Roles(string[] allKeys, params string[] selected)
    {
        RolesFilterDef def = new(false) { IncludeNullOrEmpty = false };
        def.SeedKeys(allKeys);
        foreach (string s in selected)
            def.Selected.Add(s);
        return def;
    }

    [Test]
    public void DynaValuesInSetDefIgnoresTheOrderOfBothItsSetsTest()
    {
        RolesFilterDef a = Roles(["admin", "user", "guest"], "admin", "guest");
        RolesFilterDef b = Roles(["guest", "admin", "user"], "guest", "admin");

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b), "AllKeys and Selected are both sets");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void DynaValuesInSetDefStillSeesARealDifferenceTest()
    {
        RolesFilterDef baseline = Roles(["admin", "user", "guest"], "admin", "guest");
        RolesFilterDef nullsIncluded = baseline.Clone();
        nullsIncluded.IncludeNullOrEmpty = !baseline.IncludeNullOrEmpty;

        Assert.Multiple(() =>
        {
            Assert.That(Roles(["admin", "user", "guest"], "admin"), Is.Not.EqualTo(baseline), "a different selection");
            Assert.That(Roles(["admin", "user"], "admin", "guest"), Is.Not.EqualTo(baseline), "a different candidate set");
            Assert.That(nullsIncluded, Is.Not.EqualTo(baseline), "a different null handling");
            Assert.That(baseline.Clone(), Is.EqualTo(baseline), "and a clone is the same filter");
        });
    }

    [Test]
    public void DynaValuesInSetDefRoundTripsThroughJsonTest()
    {
        RolesFilterDef def = Roles(["admin", "user", "guest"], "admin", "guest");

        string json = JsonSerializer.Serialize<AFilterDef>(def);
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(json).RequireNotNull();

        Assert.That(restored, Is.TypeOf<RolesFilterDef>());
        Assert.That(restored, Is.EqualTo(def));
    }

    // a definition's runtime type is part of its identity

    /// <summary>
    /// The hierarchy is open by design, and a definition that derives from a built-in one inherits its
    /// CreateSerRecord unless it overrides it. Its own state is then invisible to the serialized record, so
    /// the type has to take part in the comparison or the two are indistinguishable.
    /// </summary>
    private sealed class TaggedDefaultDef : DefaultDef
    {
        public string Tag { get; set; } = "";
    }

    [Test]
    public void ASubclassIsNotItsBaseTest()
    {
        DefaultDef plain = new() { Value = "abc", Contains = true, IncludeNull = false };
        TaggedDefaultDef tagged = new() { Value = "abc", Contains = true, IncludeNull = false, Tag = "extra" };

        Assert.Multiple(() =>
        {
            Assert.That(plain.Equals(tagged), Is.False, "the subclass carries state the base does not");
            Assert.That(tagged.Equals(plain), Is.False, "and equality has to be symmetric about it");
            Assert.That(plain.Equals((object)tagged), Is.False, "through the object overload too");
        });
    }

    /// <summary>
    /// The type check must not cost the subclass its own equality.
    /// </summary>
    [Test]
    public void TwoInstancesOfTheSameSubclassStillCompareTest()
    {
        TaggedDefaultDef a = new() { Value = "abc", Tag = "x" };
        TaggedDefaultDef b = new() { Value = "abc", Tag = "x" };
        TaggedDefaultDef c = new() { Value = "different", Tag = "x" };

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b), "same type, same serialized state");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(c), "and a real difference is still seen");
        });
    }

    /// <summary>
    /// Not required by the equality contract - unequal values may share a hash - but folding the type in
    /// keeps a base and a derived definition out of the same bucket.
    /// </summary>
    [Test]
    public void ASubclassHashesApartFromItsBaseTest()
    {
        DefaultDef plain = new() { Value = "abc" };
        TaggedDefaultDef tagged = new() { Value = "abc" };

        Assert.That(tagged.GetHashCode(), Is.Not.EqualTo(plain.GetHashCode()));
    }
}
