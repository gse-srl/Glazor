using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using static Glazor.Grds.Filters.BoolDef;

namespace Glazor.Tests;

public class DefTests : IDisposable
{
    private readonly Logger logger;

    public DefTests()
    {
        logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", new LoggingLevelSwitch(LogEventLevel.Information))
            .MinimumLevel.Override("Glazor.Grds.filters", new LoggingLevelSwitch(LogEventLevel.Debug))
            .WriteTo.Console()
            .CreateLogger();
        LoggerSupport.SetLoggerFactory(new SerilogLoggerFactory(logger));
    }

    void IDisposable.Dispose()
    {
        GC.SuppressFinalize(this);
        logger.Dispose();
    }

    [SetUp]
    public void Setup()
    {
    }

    [TearDown]
    public void TearDown()
    {
    }

    private enum Continent
    {
        Europe, Asia, Africa, // ...
    }

    private record TstData(string? Name, int? Age, DateOnly? BirthDay = null, bool? Male = null,
        Continent? BirthContinent = null);

    [Test]
    public void DefaultDefTest()
    {
        List<TstData> values = [new("one", 10), new("vallers", 20), new("three", 30), new("ollers", 44), new("nine", null), new(null, null)];
        {
            DefaultDef def = new() { Value = "lers", IncludeNull = true };
            var predicate = def.CreatePredicate<TstData>(d => d.Name);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(3));
        }
        {
            DefaultDef def = new() { Value = "lers", IncludeNull = false };
            var predicate = def.CreatePredicate<TstData>(d => d.Name);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// A column keeps its own type and DefaultDef matches its text: the predicate calls ToString() as Includes does, so it runs
    /// wherever it is evaluated and finds the same rows, the nulls kept or dropped as asked.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void DefaultDefOnANonStringColumnTest(bool includeNull)
    {
        List<TstData> values = [new("one", 10), new("vallers", 20), new("three", 30), new("ollers", 44), new("nine", null), new(null, null)];
        DefaultDef def = new() { Value = "0", Contains = true, IncludeNull = includeNull };
        var inMemory = values.Where(v => def.Includes(v.Age)).Select(v => v.Name).ToList();
        Assert.That(inMemory, Is.EqualTo(includeNull ? new[] { "one", "vallers", "three", "nine", null } : new[] { "one", "vallers", "three" }));
        var predicate = def.CreatePredicate<TstData>(d => d.Age);
        Assert.That(values.AsQueryable().Where(predicate).Select(v => v.Name), Is.EqualTo(inMemory));
    }

    [Test]
    public void NumberDefTest()
    {
        List<TstData> values = [new("one", 10), new("vallers", 20), new("three", 30), new("ollers", 44), new("nine", null), new(null, null)];
        {
            NumberDef<int> def = new() { From = 30, IncludeNull = true };
            var predicate = def.CreatePredicate<TstData>(d => d.Age);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(4));
        }
        {
            NumberDef<int> def = new() { From = 30, IncludeNull = false };
            var predicate = def.CreatePredicate<TstData>(d => d.Age);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        //
        {
            NumberDef<int> def = new() { From = 20, To = 30, IncludeNull = true };
            var predicate = def.CreatePredicate<TstData>(d => d.Age);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(4));
        }
        {
            NumberDef<int> def = new() { From = 20, To = 30, IncludeNull = false };
            var predicate = def.CreatePredicate<TstData>(d => d.Age);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public void DateDefTest()
    {
        List<TstData> values = [new("one", 10) {  BirthDay = new DateOnly(1968, 5, 10) },
            new("vallers", 20)  {  BirthDay = new DateOnly(1968, 5, 20) },
            new("three", 30) {  BirthDay = new DateOnly(1968, 5, 30) },
            new("ollers", 44) {  BirthDay = new DateOnly(1968, 12, 31) },
            new("nine", null), new(null, null)];
        {
            DateDef<DateOnly> def = new() { From = new DateOnly(1968, 5, 30), IncludeNull = true };
            var predicate = def.CreatePredicate<TstData>(d => d.BirthDay);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(4));
        }
        {
            DateDef<DateOnly> def = new() { From = new DateOnly(1968, 5, 30), IncludeNull = false };
            var predicate = def.CreatePredicate<TstData>(d => d.BirthDay);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
        //
        {
            DateDef<DateOnly> def = new() { From = new DateOnly(1968, 5, 20), To = new DateOnly(1968, 5, 30), IncludeNull = true };
            var predicate = def.CreatePredicate<TstData>(d => d.BirthDay);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(4));
        }
        {
            DateDef<DateOnly> def = new() { From = new DateOnly(1968, 5, 20), To = new DateOnly(1968, 5, 30), IncludeNull = false };
            var predicate = def.CreatePredicate<TstData>(d => d.BirthDay);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public void BoolDefTest()
    {
        List<TstData> values = [new("one", 10) {  Male = true },
            new("vallers", 20)  {  Male = true },
            new("three", 30) {  Male = true },
            new("ollers", 44) {  Male = false },
            new("nine", null), new(null, null)];
        {
            BoolDef def = new() { Value = BoolValue.True, IncludeNull = true };
            var predicate = def.CreatePredicate<TstData>(d => d.Male);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(5));
        }
        {
            BoolDef def = new() { Value = BoolValue.True, IncludeNull = false };
            var predicate = def.CreatePredicate<TstData>(d => d.Male);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(3));
        }
        //
        {
            BoolDef def = new() { Value = BoolValue.All, IncludeNull = true };
            var predicate = def.CreatePredicate<TstData>(d => d.Male);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(6));
        }
        {
            BoolDef def = new() { Value = BoolValue.All, IncludeNull = false };
            var predicate = def.CreatePredicate<TstData>(d => d.Male);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(4));
        }
    }

    [Test]
    public void EnumDefTest()
    {
        List<TstData> values = [new("one", 10) {  BirthContinent = Continent.Europe },
            new("vallers", 20)  {  BirthContinent = Continent.Asia },
            new("three", 30) {  BirthContinent = Continent.Europe },
            new("ollers", 44) {  BirthContinent = Continent.Africa },
            new("nine", null), new(null, null)];
        {
            EnumDef def = new(typeof(Continent?), false) { IncludeNull = true };
            def.Selected.Add(Continent.Europe);
            def.Selected.Add(Continent.Asia);
            var predicate = def.CreatePredicate<TstData>(d => d.BirthContinent);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(5));
        }
        {
            EnumDef def = new(typeof(Continent?), false) { IncludeNull = false };
            def.Selected.Add(Continent.Europe);
            def.Selected.Add(Continent.Asia);
            var predicate = def.CreatePredicate<TstData>(d => d.BirthContinent);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(3));
        }
        //
        {
            EnumDef def = new(typeof(Continent?), false) { IncludeNull = true };
            def.Selected.Add(Continent.Europe);
            var predicate = def.CreatePredicate<TstData>(d => d.BirthContinent);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(4));
        }
        {
            EnumDef def = new(typeof(Continent?), false) { IncludeNull = false };
            def.Selected.Add(Continent.Europe);
            var predicate = def.CreatePredicate<TstData>(d => d.BirthContinent);
            IQueryable<TstData> query = values.AsQueryable();
            IQueryable<TstData> q = query.Where(predicate);
            List<TstData> filtered = q.ToList();
            Assert.That(filtered.Count, Is.EqualTo(2));
        }
    }

    private enum Status
    {
        Draft = 1, Legacy = 1, Final = 2,
    }

    /// <summary>
    /// The EnumDef dialog lists an EnumModel's rows: an enum with an alias gets one row per value, as the definition holds one
    /// value for both names.
    /// </summary>
    [Test]
    public void EnumModelListsEachValueOnceTest()
    {
        EnumModel model = new(typeof(Status?));
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(model.GetItem), Is.EqualTo(new Enum[] { Status.Draft, Status.Final }));
    }

    private record Company(int Id, string Name);
    private record Employee(int Id, Company? Company);

    private class CompanyDef : DynaSetDef<CompanyDef, Company, int>
    {
        public CompanyDef(bool startFull) : base(c => c.Id, startFull) { }
        public override CompanyDef Clone() => new(StartFull()) { AllKeys = AllKeys == null ? null : new(AllKeys), Selected = new(Selected), IncludeNull = IncludeNull };
        protected override DataGridModel<Company> CreateAllValuesModel() => new() { ItemKeyFn = c => c.Id };
        protected override ValueTask<IList<Company>> RetrieveAllValuesAsync(CancellationToken ct) => ValueTask.FromResult<IList<Company>>([new(1, "a"), new(2, "b")]);
        protected override IEnumerable<Company>? KnownValues() => [new(1, "a"), new(2, "b")];
        public override CompanyDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotImplementedException();
    }

    /// <summary>
    /// A set definition keyed by a projection of a reference value: the null test of its predicate must be on the value, whose
    /// type differs from the key's.
    /// </summary>
    [TestCase(true, true, new[] { 1, 2, 3 })]
    [TestCase(true, false, new[] { 1, 3 })]
    [TestCase(false, true, new[] { 1, 2 })]
    [TestCase(false, false, new[] { 1 })]
    public void SetPredicateWithAKeyProjectionTest(bool startFull, bool includeNull, int[] expected)
    {
        CompanyDef def = new(startFull) { IncludeNull = includeNull };
        if (!startFull)
            def.Selected.Add(1);
        List<Employee> rows = [new(1, new(1, "a")), new(2, null), new(3, new(2, "b"))];
        var predicate = def.CreatePredicate<Employee>(e => e.Company);
        Assert.Multiple(() =>
        {
            Assert.That(rows.AsQueryable().Where(predicate).Select(e => e.Id), Is.EqualTo(expected), "pushed down");
            Assert.That(rows.Where(e => def.Includes(e.Company)).Select(e => e.Id), Is.EqualTo(expected), "in memory");
        });
    }
    /// <summary>
    /// A set definition that populates itself on first use must describe itself the same way before and after: its hash and
    /// equality come from the record it serializes to, and a start-full set persisted before any use must restore full.
    /// </summary>
    [Test]
    public void SetDefinitionIsTheSameBeforeAndAfterItPopulatesTest()
    {
        CompanyDef dyna = new(false);
        dyna.Selected.Add(1);
        int first = dyna.GetHashCode();
        SetDef<string> set = new(true) { AllValues = ["a", "b"] };
        var restored = (SetDef<string>)JsonSerializer.Deserialize<AFilterDef>(JsonSerializer.Serialize<AFilterDef>(set)).RequireNotNull();
        Assert.Multiple(() =>
        {
            Assert.That(dyna.GetHashCode(), Is.EqualTo(first), "the first call populated it");
            Assert.That(restored.Includes("a") && restored.Includes("b"), Is.True, "all selected, as before it was persisted");
        });
    }

    /// <summary>A restored set definition offers its candidates in the order the host gave them, which is the order it saved.</summary>
    [Test]
    public void RestoredSetDefKeepsTheOrderOfItsCandidatesTest()
    {
        string[] values = ["Draft", "Review", "Approved", "Published", "Archived", "Withdrawn", "Expired", "Deleted"];
        SetDef<string> set = new(false) { AllValues = values };
        var restored = (SetDef<string>)JsonSerializer.Deserialize<AFilterDef>(JsonSerializer.Serialize<AFilterDef>(set)).RequireNotNull();
        Assert.That(restored.AllValues, Is.EqualTo(values));
    }

    private sealed record Stamped<VT>(VT? Value) where VT : struct;

    // Kept in memory and by the predicate a model pushes down, each as a bool
    private static (bool InMemory, bool PushedDown) Keeps<VT>(DateDef<VT> def, VT value) where VT : struct, IComparable<VT>
    {
        Func<Stamped<VT>, bool> predicate = def.CreatePredicate<Stamped<VT>>(s => s.Value).Compile();
        return (def.Includes(value), predicate(new Stamped<VT>(value)));
    }

    private sealed class DayDef : DateDef<DateTime>
    {
        protected internal override Microsoft.AspNetCore.Components.Forms.InputDateType GetInputDateType()
            => Microsoft.AspNetCore.Components.Forms.InputDateType.Date;
    }

    /// <summary>
    /// The editor picks a date and time to the minute, so a "To" it picked covers that whole minute, or that whole day with a
    /// date input; a "To" set in code with seconds ends where it is. In memory and pushed down alike.
    /// </summary>
    [Test]
    public void DateDefToCoversTheUnitTheEditorPicksTest()
    {
        DateTime minute = new(2026, 11, 19, 10, 30, 0);
        DateDef<DateTime> toMinute = new() { To = minute, IncludeNull = false };
        DateDef<DateTime> toSeconds = new() { To = minute.AddSeconds(20), IncludeNull = false };
        DayDef toDay = new() { To = minute.Date, IncludeNull = false };
        DateTimeOffset offsetMinute = new(minute, TimeSpan.FromHours(1));
        DateDef<DateTimeOffset> toOffsetMinute = new() { To = offsetMinute, IncludeNull = false };
        DateDef<TimeOnly> toTime = new() { To = new TimeOnly(10, 30), IncludeNull = false };
        DateDef<TimeOnly> toLastMinute = new() { To = new TimeOnly(23, 59), IncludeNull = false };
        DateTime lastMinute = new(9999, 12, 31, 23, 59, 0);
        DateDef<DateTime> toLastMinuteThereIs = new() { To = lastMinute, IncludeNull = false };
        DayDef toLastDay = new() { To = lastMinute.Date, IncludeNull = false };
        DateTimeOffset offsetLastMinute = new(lastMinute, TimeSpan.FromHours(1));
        DateDef<DateTimeOffset> toOffsetLastMinute = new() { To = offsetLastMinute, IncludeNull = false };
        Assert.Multiple(() =>
        {
            Assert.That(Keeps(toMinute, minute.AddSeconds(45)), Is.EqualTo((true, true)), "within the minute");
            Assert.That(Keeps(toMinute, minute.AddMinutes(1)), Is.EqualTo((false, false)), "the next minute");
            Assert.That(Keeps(toSeconds, minute.AddSeconds(20)), Is.EqualTo((true, true)), "set in code, its own second");
            Assert.That(Keeps(toSeconds, minute.AddSeconds(45)), Is.EqualTo((false, false)), "set in code, a later second");
            Assert.That(Keeps(toDay, minute.Date.AddHours(23).AddMinutes(59)), Is.EqualTo((true, true)), "within the day");
            Assert.That(Keeps(toDay, minute.Date.AddDays(1)), Is.EqualTo((false, false)), "the next day");
            Assert.That(Keeps(toOffsetMinute, offsetMinute.AddSeconds(45)), Is.EqualTo((true, true)), "an offset within the minute");
            Assert.That(Keeps(toOffsetMinute, offsetMinute.AddMinutes(1)), Is.EqualTo((false, false)), "an offset the next minute");
            Assert.That(Keeps(toTime, new TimeOnly(10, 30, 45)), Is.EqualTo((true, true)), "a time within the minute");
            Assert.That(Keeps(toTime, new TimeOnly(10, 31)), Is.EqualTo((false, false)), "a time the next minute");
            Assert.That(Keeps(toLastMinute, new TimeOnly(23, 59, 59)), Is.EqualTo((true, true)), "the day's last minute");
            Assert.That(Keeps(toLastMinuteThereIs, lastMinute.AddSeconds(59)), Is.EqualTo((true, true)), "the last minute there is");
            Assert.That(Keeps(toLastDay, lastMinute.AddSeconds(59)), Is.EqualTo((true, true)), "the last day there is");
            Assert.That(Keeps(toOffsetLastMinute, offsetLastMinute.AddSeconds(45)), Is.EqualTo((true, true)), "an offset's last minute");
            Assert.That(Keeps(toOffsetLastMinute, new DateTimeOffset(9999, 12, 31, 23, 30, 0, TimeSpan.Zero)), Is.EqualTo((false, false)),
                "an offset's last minute, an instant past it");
        });
    }
}
