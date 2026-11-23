using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Text.Json;

namespace Glazor.Tests;

public class FiltersTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void DefaultDefTest()
    {
        {
            DefaultDef def = new();
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes("value"), Is.True);
        }
        {
            DefaultDef def = new()
            {
                IncludeNull = false
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes("value"), Is.True);
        }
        {
            DefaultDef def = new()
            {
                IncludeNull = false,
                Value = "lue"
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes("value"), Is.True);
        }
        {
            DefaultDef def = new()
            {
                IncludeNull = false,
                Value = "lue",
                Contains = false
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes("value"), Is.False);
        }
        {
            DefaultDef def = new()
            {
                Value = "lue"
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes("value"), Is.True);
        }
        {
            DefaultDef def = new()
            {
                Value = "lue",
                Contains = false
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes("value"), Is.False);
        }
    }

    [Test]
    public void NumberDefTest()
    {
        {
            NumberDef<int> def = new();
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(10), Is.True);
        }
        {
            NumberDef<int> def = new()
            {
                IncludeNull = false
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(10), Is.True);
        }
        {
            NumberDef<int> def = new()
            {
                From = 10
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(0), Is.False);
            Assert.That(def.Includes(10), Is.True);
            Assert.That(def.Includes(100), Is.True);
        }
        {
            NumberDef<int> def = new()
            {
                To = 10
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(0), Is.True);
            Assert.That(def.Includes(10), Is.True);
            Assert.That(def.Includes(100), Is.False);
        }
        {
            NumberDef<int> def = new()
            {
                From = 10,
                To = 10
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(0), Is.False);
            Assert.That(def.Includes(10), Is.True);
            Assert.That(def.Includes(100), Is.False);
        }
        {
            NumberDef<int> def = new()
            {
                IncludeNull = false,
                From = 10
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(0), Is.False);
            Assert.That(def.Includes(10), Is.True);
            Assert.That(def.Includes(100), Is.True);
        }
        {
            NumberDef<int> def = new()
            {
                IncludeNull = false,
                To = 10
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(0), Is.True);
            Assert.That(def.Includes(10), Is.True);
            Assert.That(def.Includes(100), Is.False);
        }
        {
            NumberDef<int> def = new()
            {
                IncludeNull = false,
                From = 10,
                To = 10
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(0), Is.False);
            Assert.That(def.Includes(10), Is.True);
            Assert.That(def.Includes(100), Is.False);
        }
    }

    [Test]
    public void DateDefTest()
    {
        {
            DateDef<DateOnly> def = new();
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
        }
        {
            DateDef<DateOnly> def = new()
            {
                IncludeNull = false
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
        }
        {
            DateDef<DateOnly> def = new()
            {
                From = new DateOnly(1968, 5, 17)
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(DateOnly.MinValue), Is.False);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
            Assert.That(def.Includes(new DateOnly(2025, 12, 30)), Is.True);
        }
        {
            DateDef<DateOnly> def = new()
            {
                To = new DateOnly(1968, 5, 17)
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(DateOnly.MinValue), Is.True);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
            Assert.That(def.Includes(new DateOnly(2025, 12, 30)), Is.False);
        }
        {
            DateDef<DateOnly> def = new()
            {
                From = new DateOnly(1968, 5, 17),
                To = new DateOnly(1968, 5, 17)
            };
            Assert.That(def.Includes(null), Is.True);
            Assert.That(def.Includes(DateOnly.MinValue), Is.False);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
            Assert.That(def.Includes(new DateOnly(2025, 12, 30)), Is.False);
        }
        {
            DateDef<DateOnly> def = new()
            {
                IncludeNull = false,
                From = new DateOnly(1968, 5, 17)
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(DateOnly.MinValue), Is.False);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
            Assert.That(def.Includes(new DateOnly(2025, 12, 30)), Is.True);
        }
        {
            DateDef<DateOnly> def = new()
            {
                IncludeNull = false,
                To = new DateOnly(1968, 5, 17)
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(DateOnly.MinValue), Is.True);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
            Assert.That(def.Includes(new DateOnly(2025, 12, 30)), Is.False);
        }
        {
            DateDef<DateOnly> def = new()
            {
                IncludeNull = false,
                From = new DateOnly(1968, 5, 17),
                To = new DateOnly(1968, 5, 17)
            };
            Assert.That(def.Includes(null), Is.False);
            Assert.That(def.Includes(DateOnly.MinValue), Is.False);
            Assert.That(def.Includes(new DateOnly(1968, 5, 17)), Is.True);
            Assert.That(def.Includes(new DateOnly(2025, 12, 30)), Is.False);
        }
    }

    /// <summary>
    /// A set definition built by host code and handed to SetFilter never goes through its dialog, so PopulateAsync never
    /// runs and AllKeys stays null; IsVoid threw ArgumentNullException on it. SetDef has every value in
    /// memory, so it seeds itself on the first question; a start-full one is then all selected, an explicit one compares
    /// against the real keys.
    /// </summary>
    [Test]
    public void SetDefBuiltByHostCodeAnswersIsVoidTest()
    {
        SetDef<string> full = new(true) { AllValues = ["a", "b"] };
        Assert.That(full.IsVoid(), Is.True, "start full and nulls included: no filter at all");
        Assert.That(full.Includes("a"), Is.True);

        SetDef<string> fullNoNull = new(true) { AllValues = ["a", "b"], IncludeNull = false };
        Assert.Multiple(() =>
        {
            Assert.That(fullNoNull.IsVoid(), Is.False, "excluding nulls is a filter");
            Assert.That(fullNoNull.Includes("a"), Is.True, "every value is selected");
            Assert.That(fullNoNull.Includes(null), Is.False);
        });

        SetDef<string> explicitOne = new(false) { AllValues = ["a", "b"] };
        explicitOne.Selected.Add("a");
        Assert.That(explicitOne.IsVoid(), Is.False);
        Assert.That(explicitOne.Includes("b"), Is.False);
        explicitOne.Selected.Add("b");
        Assert.That(explicitOne.IsVoid(), Is.True, "everything selected by hand");
    }

    /// <summary>
    /// The lazily retrieved twin cannot seed itself; before its values are retrieved a start-full one means all selected.
    /// </summary>
    [Test]
    public void DynaValuesInSetDefAnswersIsVoidBeforePopulationTest()
    {
        DynaValuesInSetDefSubclassTests.ByCompanyRolesFDef full = new(true);
        DynaValuesInSetDefSubclassTests.ByCompanyRolesFDef explicitOne = new(false);
        explicitOne.Selected.Add(1);
        Assert.Multiple(() =>
        {
            Assert.That(full.IsVoid(), Is.True);
            Assert.That(new DynaValuesInSetDefSubclassTests.ByCompanyRolesFDef(true) { IncludeNullOrEmpty = false }.IsVoid(), Is.False);
            Assert.That(explicitOne.IsVoid(), Is.False);
        });
    }

    public sealed record Company(int Id, string Name);

    public sealed record Employee(int Id, Company? Company);

    /// <summary>A set read when its dialog opens, with no values at hand before.</summary>
    public sealed class LazyCompanyDef : DynaSetDef<LazyCompanyDef, Company, int>
    {
        public LazyCompanyDef() : this(false) { }
        public LazyCompanyDef(bool startFull) : base(c => c.Id, startFull) { }
        public override LazyCompanyDef Clone() => new(StartFull()) { AllKeys = AllKeys == null ? null : new(AllKeys), Selected = new(Selected), IncludeNull = IncludeNull };
        protected override DataGridModel<Company> CreateAllValuesModel() => new() { ItemKeyFn = c => c.Id };
        protected override ValueTask<IList<Company>> RetrieveAllValuesAsync(CancellationToken ct) => ValueTask.FromResult<IList<Company>>([new(1, "a"), new(2, "b")]);
        public override LazyCompanyDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
            return new(ser.StartFull) { IncludeNull = ser.IncludeNull, AllKeys = ser.AllKeys?.ToHashSet(), Selected = ser.Selected.ToHashSet() };
        }
    }

    /// <summary>
    /// A start-full set built in code and never shown has not read its values, and every value is selected: it keeps every row
    /// but the null ones it excludes, in memory, as a predicate and after a round trip.
    /// </summary>
    [Test]
    public void StartFullSetNeverPopulatedKeepsEveryValueTest()
    {
        LazyCompanyDef def = new(true) { IncludeNull = false };
        List<Employee> rows = [new(1, new(1, "a")), new(2, null), new(3, new(7, "unknown"))];
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(JsonSerializer.Serialize<AFilterDef>(def)).RequireNotNull();
        var predicate = def.CreatePredicate<Employee>(e => e.Company);
        Assert.Multiple(() =>
        {
            Assert.That(rows.Where(e => def.Includes(e.Company)).Select(e => e.Id), Is.EqualTo(new[] { 1, 3 }), "in memory");
            Assert.That(rows.AsQueryable().Where(predicate).Select(e => e.Id), Is.EqualTo(new[] { 1, 3 }), "as a predicate");
            Assert.That(rows.Where(e => restored.Includes(e.Company)).Select(e => e.Id), Is.EqualTo(new[] { 1, 3 }), "restored");
            Assert.That(restored, Is.EqualTo(def));
        });
    }

    /// <summary>Its twin over collections keeps every row with a value in the same state.</summary>
    [Test]
    public void StartFullValuesInSetNeverPopulatedKeepsEveryValueTest()
    {
        DynaValuesInSetDefSubclassTests.ByCompanyRolesFDef def = new(true) { IncludeNullOrEmpty = false };
        IQueryable<DynaValuesInSetDefSubclassTests.CompanyRole> Roles(params int[] ids) => ids.Select(i => new DynaValuesInSetDefSubclassTests.CompanyRole(i, 1, "r")).AsQueryable();
        Assert.Multiple(() =>
        {
            Assert.That(def.Includes(Roles(1, 9)), Is.True, "a value");
            Assert.That(def.Includes(Roles()), Is.False, "empty");
            Assert.That(def.Includes(null), Is.False, "null");
        });
    }

    /// <summary>A definition scoped to a context that can change, as a filter over one company's values.</summary>
    internal sealed class ScopedDef(Func<bool> applicable) : DefaultDef
    {
        public override bool IsStillApplicable() => applicable();

        public override DefaultDef Clone() => new ScopedDef(applicable) { Value = Value, Contains = Contains, IncludeNull = IncludeNull };
    }

    private sealed record Row(string Name);

    /// <summary>
    /// A filter that is no longer applicable is discarded at the next sync rather than applied: one restored from another
    /// context, and one whose context changed while it was on.
    /// </summary>
    [Test]
    public void FilterNoLongerApplicableIsDiscardedTest()
    {
        bool applicable = true;
        Glazor.Grds.Mdls.DataGridModel<Row> model = new([new("Valentino"), new("Vallers"), new("Ben")]);
        model.AddColumn("Name", new Glazor.Grds.Column<Row, string>("Name"), r => r.Name);
        model.ApplyFilters([new("Name", new ScopedDef(() => applicable) { Value = "Val" })]);
        Assert.That(model.GetRowCount(), Is.EqualTo(2), "applied");

        applicable = false;
        model.SetSortedColumn("Name", true);   // the next sync
        Assert.Multiple(() =>
        {
            Assert.That(model.GetRowCount(), Is.EqualTo(3), "its context changed");
            Assert.That(model.IsFiltered(), Is.False);
            Assert.That(model.GetFilters(), Is.Empty);
        });

        model.ApplyFilters([new("Name", new ScopedDef(() => false) { Value = "Val" })]);
        Assert.That(model.GetRowCount(), Is.EqualTo(3), "restored from another context");
    }
}
