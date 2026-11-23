using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Misc;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Glazor.Grds.Filters.BoolDef;

namespace Glazor.Tests;

public class JsonSerializationTests
{
    [SetUp]
    public void Setup()
    {
    }

    /// <summary>
    /// Only a TreeMemento carries a "$type": a plain memento is written as before, and one stored before the tree memento
    /// existed, with no "$type", reads back as a plain Memento.
    /// </summary>
    [Test]
    public void APlainMementoCarriesNoTypeTest()
    {
        Memento plain = new(null, new Dictionary<string, AFilterDef>(), [new SortedColumn("Name", true)]);
        string json = JsonSerializer.Serialize(plain);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("$type"));
            Assert.That(JsonSerializer.Deserialize<Memento>(json), Is.TypeOf<Memento>().And.EqualTo(plain));
        });
    }

    /// <summary>
    /// A store that reorders the keys, as PostgreSQL's jsonb does, moves "$type" from the front: with
    /// AllowOutOfOrderMetadataProperties the tree memento still reads back as itself.
    /// </summary>
    [Test]
    public void AReorderedTreeMementoReadsBackWithOutOfOrderMetadataTest()
    {
        Memento tree = new TreeMemento(null, new Dictionary<string, AFilterDef>(), [new SortedColumn("Name", true)],
            new([new([1]), new([1, 7])], new([1])));
        JsonObject json = JsonNode.Parse(JsonSerializer.Serialize(tree))!.AsObject();
        JsonNode type = json["$type"]!;
        json.Remove("$type");
        json.Add("$type", type);
        Assert.That(json.ToJsonString(), Does.Not.StartWith("{\"$type\""));
        Memento? read = JsonSerializer.Deserialize<Memento>(json.ToJsonString(), new JsonSerializerOptions { AllowOutOfOrderMetadataProperties = true });
        Assert.That(read, Is.TypeOf<TreeMemento>().And.EqualTo(tree));
    }

    private sealed record HostMemento(GridLayout? Layout, IReadOnlyDictionary<string, AFilterDef> FiltersDefs,
        IReadOnlyList<SortedColumn> SorterColumns, string Extra) : Memento(Layout, FiltersDefs, SorterColumns);

    /// <summary>
    /// A host's own derived memento, stored as a Memento, is written as a plain one and reads back as one.
    /// </summary>
    [Test]
    public void AHostDerivedMementoIsWrittenAsAPlainOneTest()
    {
        Memento derived = new HostMemento(null, new Dictionary<string, AFilterDef>(), [new SortedColumn("Name", true)], "extra");
        string json = JsonSerializer.Serialize(derived);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("$type"));
            Assert.That(JsonSerializer.Deserialize<Memento>(json),
                Is.TypeOf<Memento>().And.EqualTo(new Memento(null, new Dictionary<string, AFilterDef>(), [new SortedColumn("Name", true)])));
        });
    }

    [Test]
    public void LayoutSerializationTest()
    {
        GridLayout layout = new();
        layout.columnsToShow = [nameof(RProduct.Name), nameof(RProduct.Price)];
        layout.attributes.Add(nameof(RProduct.Name), new(20));
        layout.attributes.Add(nameof(RProduct.Price), new(10));
        string json = JsonSerializer.Serialize(layout);
        GridLayout? layout2 = JsonSerializer.Deserialize<GridLayout>(json);
        Assert.That(layout2!.columnsToShow, Is.EquivalentTo(layout.columnsToShow));
        Assert.That(layout2!.attributes, Is.EquivalentTo(layout.attributes));
    }

    /// <summary>
    /// A host reads a layout through ColumnsToShow and Attributes, and the JSON keeps only the persisted members: a store
    /// written before the two views existed reads back the same.
    /// </summary>
    [Test]
    public void LayoutViewsReadTheLayoutAndStayOutOfTheJsonTest()
    {
        GridLayout layout = new();
        layout.columnsToShow = [nameof(RProduct.Name), nameof(RProduct.Price)];
        layout.attributes.Add(nameof(RProduct.Name), new(20));
        string json = JsonSerializer.Serialize(layout);
        Assert.Multiple(() =>
        {
            Assert.That(layout.ColumnsToShow, Is.EqualTo(new[] { nameof(RProduct.Name), nameof(RProduct.Price) }));
            Assert.That(layout.Attributes[nameof(RProduct.Name)].PreferredSize, Is.EqualTo(20));
            Assert.That(JsonNode.Parse(json)!.AsObject().Select(p => p.Key), Is.EquivalentTo(new[] { "columnsToShow", "attributes" }));
        });
    }

    [Test]
    public void TypeSerializationTest()
    {
        Type orgType = typeof(NumberDef<decimal>);
        Type type = Type.GetType(orgType.AssemblyQualifiedName.RequireNotNull())!;
        Assert.That(type, Is.EqualTo(orgType));
    }

    [Test]
    public void FiltersSerializationTest()
    {
        Dictionary<string, AFilterDef> filterDefs = new();
        var boolDef = new BoolDef() { IncludeNull = false, Value = BoolValue.True };
        var dateDef = new DateDef<DateTime>() { IncludeNull = false, From = new DateTime(2026, 3, 10, 9, 0, 0), To = new DateTime(2026, 4, 9, 18, 0, 0) };
        var numDef = new NumberDef<decimal>() { IncludeNull = false, From = -123.321M, To = 456.654M };
        var defaultDef = new DefaultDef() { IncludeNull = false, Contains = false, Value = "vallers" };
        var enumDef = new EnumDef(typeof(TestEnum), false) { IncludeNull = false, Selected = [TestEnum.ONE, TestEnum.FOUR] };
        // A nullable enum column yields EnumDef(typeof(TestEnum?)), and GetValueType() has to keep reporting
        // the nullable type, because ADataGrid.CheckFilter compares a filter against the column's type.
        var nullableEnumDef = new EnumDef(typeof(TestEnum?), false) { IncludeNull = true, Selected = [TestEnum.TWO] };
        filterDefs.Add("aBool", boolDef);
        filterDefs.Add("aDate", dateDef);
        filterDefs.Add("aNum", numDef);
        filterDefs.Add("aString", defaultDef);
        filterDefs.Add("anEnum", enumDef);
        filterDefs.Add("aNullableEnum", nullableEnumDef);
        string json = JsonSerializer.Serialize(filterDefs);
        Dictionary<string, AFilterDef> filterDefs2 = JsonSerializer.Deserialize<Dictionary<string, AFilterDef>>(json).RequireNotNull();
        Assert.That(filterDefs2.Count, Is.EqualTo(filterDefs.Count));
        //
        Assert.That(((BoolDef)filterDefs2["aBool"]).IncludeNull, Is.EqualTo(boolDef.IncludeNull));
        Assert.That(((BoolDef)filterDefs2["aBool"]).Value, Is.EqualTo(boolDef.Value));
        //
        Assert.That(((DateDef<DateTime>)filterDefs2["aDate"]).IncludeNull, Is.EqualTo(dateDef.IncludeNull));
        Assert.That(((DateDef<DateTime>)filterDefs2["aDate"]).From, Is.EqualTo(dateDef.From));
        Assert.That(((DateDef<DateTime>)filterDefs2["aDate"]).To, Is.EqualTo(dateDef.To));
        //
        Assert.That(((NumberDef<decimal>)filterDefs2["aNum"]).IncludeNull, Is.EqualTo(numDef.IncludeNull));
        Assert.That(((NumberDef<decimal>)filterDefs2["aNum"]).From, Is.EqualTo(numDef.From));
        Assert.That(((NumberDef<decimal>)filterDefs2["aNum"]).To, Is.EqualTo(numDef.To));
        //
        Assert.That(((DefaultDef)filterDefs2["aString"]).IncludeNull, Is.EqualTo(defaultDef.IncludeNull));
        Assert.That(((DefaultDef)filterDefs2["aString"]).Value, Is.EqualTo(defaultDef.Value));
        Assert.That(((DefaultDef)filterDefs2["aString"]).Contains, Is.EqualTo(defaultDef.Contains));
        //
        Assert.That(((EnumDef)filterDefs2["anEnum"]).IncludeNull, Is.EqualTo(enumDef.IncludeNull));
        Assert.That(((EnumDef)filterDefs2["anEnum"]).Selected, Is.EquivalentTo(enumDef.Selected));
        Assert.That(((EnumDef)filterDefs2["anEnum"]).Selected.Contains(TestEnum.ONE), Is.True);
        Assert.That(((EnumDef)filterDefs2["anEnum"]).Selected.Contains(TestEnum.TWO), Is.False);
        Assert.That(((EnumDef)filterDefs2["anEnum"]).Selected.Contains(TestEnum.THREE), Is.False);
        Assert.That(((EnumDef)filterDefs2["anEnum"]).Selected.Contains(TestEnum.FOUR), Is.True);
        // The non-nullable case passes even if nullability is dropped, so assert it on its own.
        Assert.That(((EnumDef)filterDefs2["anEnum"]).EnumType, Is.EqualTo(typeof(TestEnum)));
        //
        var nullableEnumDef2 = (EnumDef)filterDefs2["aNullableEnum"];
        Assert.That(nullableEnumDef2.EnumType, Is.EqualTo(typeof(TestEnum?)),
            "a nullable enum must not come back as its underlying type");
        Assert.That(nullableEnumDef2.GetValueType(), Is.EqualTo(typeof(TestEnum?)),
            "GetValueType() feeds ADataGrid.CheckFilter, which would then reject a valid filter");
        Assert.That(nullableEnumDef2.IncludeNull, Is.EqualTo(nullableEnumDef.IncludeNull));
        Assert.That(nullableEnumDef2.Selected, Is.EquivalentTo(nullableEnumDef.Selected));
        //
        // The whole point of stripping the assembly identity: rebuilding Glazor, or the assembly declaring
        // the enum, must not invalidate a stored filter.
        Assert.That(json, Does.Not.Contain("Version="), "no assembly version may reach the payload");
        Assert.That(json, Does.Not.Contain("PublicKeyToken="), "no public key token may reach the payload");
    }

    [Test]
    public void MementoSerializationTest()
    {
        GridLayout layout = new();
        layout.columnsToShow = [nameof(RProduct.Name), nameof(RProduct.Price)];
        layout.attributes.Add(nameof(RProduct.Name), new(20));
        layout.attributes.Add(nameof(RProduct.Price), new(10));
        Dictionary<string, AFilterDef> filterDefs = new();
        var enumDef = new EnumDef(typeof(TestEnum), false) { IncludeNull = false, Selected = [TestEnum.ONE, TestEnum.FOUR] };
        filterDefs.Add("anEnum", enumDef);
        Memento memento = new(layout, filterDefs, [new(nameof(RProduct.Name), true)]);
        string json = JsonSerializer.Serialize(memento);
        //Console.WriteLine($"{json}");
        Memento memento2 = JsonSerializer.Deserialize<Memento>(json).RequireNotNull();
        //
        Assert.That(memento2.Layout!.columnsToShow, Is.EquivalentTo(layout.columnsToShow));
        Assert.That(memento2.Layout.attributes, Is.EquivalentTo(layout.attributes));
        //
        Assert.That(((EnumDef)memento2.FiltersDefs["anEnum"]).IncludeNull, Is.EqualTo(enumDef.IncludeNull));
        Assert.That(((EnumDef)memento2.FiltersDefs["anEnum"]).Selected, Is.EquivalentTo(enumDef.Selected));
        Assert.That(((EnumDef)memento2.FiltersDefs["anEnum"]).Selected.Contains(TestEnum.ONE), Is.True);
        Assert.That(((EnumDef)memento2.FiltersDefs["anEnum"]).Selected.Contains(TestEnum.TWO), Is.False);
        Assert.That(((EnumDef)memento2.FiltersDefs["anEnum"]).Selected.Contains(TestEnum.THREE), Is.False);
        Assert.That(((EnumDef)memento2.FiltersDefs["anEnum"]).Selected.Contains(TestEnum.FOUR), Is.True);
        //
        Assert.That(memento2.SorterColumns[0].Name, Is.EqualTo(nameof(RProduct.Name)));
        Assert.That(memento2.SorterColumns[0].Asc, Is.True);
    }

    /// <summary>
    /// A store that orders an object's keys, a PostgreSQL jsonb or a MySQL JSON column, gives each filter back with "def"
    /// before "defType": the memento must still read, from a string or from a stream read in small blocks.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task MementoWithItsKeysReorderedReadsBackTest(bool fromStream)
    {
        Dictionary<string, AFilterDef> filterDefs = new()
        {
            ["aNum"] = new NumberDef<decimal>() { IncludeNull = false, From = -1.5M, To = 2.5M },
            ["anEnum"] = new EnumDef(typeof(TestEnum), false) { IncludeNull = false, Selected = [TestEnum.TWO] },
        };
        Memento memento = new(null, filterDefs, []);
        string stored = DefFirst(JsonNode.Parse(JsonSerializer.Serialize(memento)))!.ToJsonString();
        Assert.That(stored, Does.Contain("{\"def\":"), "the order such a store gives back");
        using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(stored));
        Memento memento2 = (fromStream
            ? await JsonSerializer.DeserializeAsync<Memento>(stream, new JsonSerializerOptions { DefaultBufferSize = 16 })
            : JsonSerializer.Deserialize<Memento>(stored)).RequireNotNull();
        Assert.Multiple(() =>
        {
            Assert.That(((NumberDef<decimal>)memento2.FiltersDefs["aNum"]).From, Is.EqualTo(-1.5M));
            Assert.That(((NumberDef<decimal>)memento2.FiltersDefs["aNum"]).To, Is.EqualTo(2.5M));
            Assert.That(((EnumDef)memento2.FiltersDefs["anEnum"]).Selected, Is.EquivalentTo(new Enum[] { TestEnum.TWO }));
        });
    }

    private static JsonNode? DefFirst(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key == "def" ? 0 : 1)
            .Select(p => KeyValuePair.Create(p.Key, DefFirst(p.Value?.DeepClone())))),
        JsonArray arr => new JsonArray(arr.Select(e => DefFirst(e?.DeepClone())).ToArray()),
        _ => node,
    };

    enum TestEnum
    {
        ONE, TWO, THREE, FOUR
    }

    record RProduct(string Name, decimal Price);

    private static IEnumerable<AFilterDef> EveryBuiltInDefinition()
    {
        yield return new NumberDef<int>() { From = 3, To = 9, IncludeNull = true };
        yield return new DateDef<DateOnly>() { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31), IncludeNull = true };
        yield return new DateDef<DateTime>() { From = new DateTime(2026, 1, 1, 8, 30, 0), To = new DateTime(2026, 12, 31, 18, 0, 0) };
        yield return new DateDef<DateTimeOffset>() { From = new DateTimeOffset(2026, 1, 1, 8, 30, 0, TimeSpan.FromHours(1)), IncludeNull = true };
        yield return new DateDef<TimeOnly>() { From = new TimeOnly(8, 0), To = new TimeOnly(18, 0) };
        yield return new BoolDef() { Value = BoolValue.True, IncludeNull = true };
        yield return new DefaultDef() { Value = "abc", Contains = true, IncludeNull = true };
        SetDef<string> set = new(false) { AllValues = ["a", "b", "c"], IncludeNull = true };
        set.Selected.Add("b");
        yield return set;
        EnumDef days = new(typeof(DayOfWeek?), true) { IncludeNull = false };
        days.Selected.Remove(DayOfWeek.Sunday);
        yield return days;
    }

    /// <summary>
    /// A definition is written with the caller's options, so it must be read with them: under a naming policy, as with the Web
    /// defaults, a reader that ignores them matches no property and restores a different filter.
    /// </summary>
    [TestCaseSource(nameof(EveryBuiltInDefinition))]
    public void FilterRoundTripsUnderTheCallersOptionsTest(AFilterDef def)
    {
        JsonSerializerOptions web = new(JsonSerializerDefaults.Web);
        string json = JsonSerializer.Serialize(def, web);
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(json, web).RequireNotNull();
        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(def));
            Assert.That(restored.GetHashCode(), Is.EqualTo(def.GetHashCode()));
        });
    }
    private static IEnumerable<TestCaseData> DefinitionsWithANullValue()
    {
        yield return new TestCaseData(new NumberDef<int>() { From = 3 }).SetArgDisplayNames("number from");
        yield return new TestCaseData(new NumberDef<int>() { To = 9 }).SetArgDisplayNames("number to");
        yield return new TestCaseData(new DateDef<DateOnly>() { From = new DateOnly(2026, 1, 1) }).SetArgDisplayNames("date from");
        yield return new TestCaseData(new DefaultDef() { IncludeNull = true }).SetArgDisplayNames("default, nulls only");
    }

    /// <summary>
    /// A host may persist with options that skip null values and require every constructor parameter, as
    /// <see cref="JsonSerializerOptions.Strict"/> does: a definition whose persisted form holds a null must still read back.
    /// </summary>
    [TestCaseSource(nameof(DefinitionsWithANullValue))]
    public void FilterWithANullValueRoundTripsUnderStrictOptionsTest(AFilterDef def)
    {
        JsonSerializerOptions strict = new(JsonSerializerOptions.Strict) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        string json = JsonSerializer.Serialize(def, strict);
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(json, strict).RequireNotNull();
        Assert.That(restored, Is.EqualTo(def));
    }
    private static IEnumerable<TestCaseData> DefinitionsWithAFalseOrMissingValue()
    {
        yield return new TestCaseData(new NumberDef<int>() { From = 3, IncludeNull = false }).SetArgDisplayNames("number, no nulls");
        yield return new TestCaseData(new DateDef<DateOnly>() { From = new DateOnly(2026, 1, 1), IncludeNull = false }).SetArgDisplayNames("date, no nulls");
        yield return new TestCaseData(new BoolDef() { Value = BoolValue.False, IncludeNull = false }).SetArgDisplayNames("bool false");
        yield return new TestCaseData(new DefaultDef() { Value = "abc", IncludeNull = false }).SetArgDisplayNames("default, no nulls");
        EnumDef days = new(typeof(DayOfWeek), true) { IncludeNull = false };
        yield return new TestCaseData(days).SetArgDisplayNames("enum, no nulls");
        SetDef<string> set = new(false) { AllValues = ["a", "b"] };
        set.Selected.Add("a");
        yield return new TestCaseData(set).SetArgDisplayNames("set, no nulls");
    }

    /// <summary>
    /// Options that skip default values drop <c>false</c> as well as null, and strict options then miss what the reader needs:
    /// those members are written whatever the host's ignore condition.
    /// </summary>
    [TestCaseSource(nameof(DefinitionsWithAFalseOrMissingValue))]
    public void FilterRoundTripsUnderStrictOptionsSkippingDefaultsTest(AFilterDef def)
    {
        JsonSerializerOptions strict = new(JsonSerializerOptions.Strict) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
        string json = JsonSerializer.Serialize(def, strict);
        AFilterDef restored = JsonSerializer.Deserialize<AFilterDef>(json, strict).RequireNotNull();
        Assert.That(restored, Is.EqualTo(def));
    }

    /// <summary>
    /// A grid that is not layoutable saves a memento without a layout, and a descending sort saves <c>Asc</c> as false: both
    /// must read back under strict options that skip nulls, or defaults.
    /// </summary>
    [TestCase(JsonIgnoreCondition.WhenWritingNull)]
    [TestCase(JsonIgnoreCondition.WhenWritingDefault)]
    public void MementoRoundTripsUnderStrictOptionsTest(JsonIgnoreCondition skip)
    {
        JsonSerializerOptions strict = new(JsonSerializerOptions.Strict) { DefaultIgnoreCondition = skip };
        Memento memento = new(null, new Dictionary<string, AFilterDef>(), [new(nameof(RProduct.Name), false)]);
        string json = JsonSerializer.Serialize(memento, strict);
        var restored = JsonSerializer.Deserialize<Memento>(json, strict).RequireNotNull();
        Assert.Multiple(() =>
        {
            Assert.That(restored.Layout, Is.Null);
            Assert.That(restored.SorterColumns.Single(), Is.EqualTo(new SortedColumn(nameof(RProduct.Name), false)));
        });
    }
    /// <summary>
    /// A member the writer left out reads back as its default, so JSON saved without it, by options that skip defaults or
    /// before the member was written at all, still restores under strict options.
    /// </summary>
    [Test]
    public void MissingMembersReadBackAsTheirDefaultsTest()
    {
        var memento = JsonSerializer.Deserialize<Memento>(
            """{"Layout":null,"FiltersDefs":{},"SorterColumns":[{"Name":"Name"}]}""", JsonSerializerOptions.Strict).RequireNotNull();
        Assert.That(memento.SorterColumns.Single().Asc, Is.False);
        AFilterDef def = new NumberDef<int>() { From = 3, IncludeNull = false };
        string json = JsonSerializer.Serialize(def,
            new JsonSerializerOptions(JsonSerializerOptions.Strict) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault });
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("IncludeNull"));
            Assert.That(JsonSerializer.Deserialize<AFilterDef>(json, JsonSerializerOptions.Strict), Is.EqualTo(def));
            Assert.That(JsonSerializer.Deserialize<LayoutAttr>("{}", JsonSerializerOptions.Strict), Is.EqualTo(new LayoutAttr(0)), "a size of 0");
        });
    }
}
