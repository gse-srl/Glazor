using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// One factory, set as <see cref="FilterDefFactory.Default"/>, serves the grids without a parameter and the JSON converter of
/// <see cref="AFilterDef"/>: an allow-list in <c>GetDefType</c> therefore holds for a restore too, which the attribute's own
/// instance would have bypassed.
/// </summary>
public class FilterDefFactoryDefaultTests
{
    private sealed record Product(string Code, string Name, decimal Price);

    private sealed class NumbersOnlyFactory : FilterDefFactory
    {
        // Closed types, matched on the name before anything is resolved: a generic definition would let the store choose the type argument
        private static readonly Dictionary<string, Type> allowed = new[] { typeof(NumberDef<decimal>), typeof(NumberDef<int>) }.ToDictionary(TypeName);

        protected override Type GetDefType(string defType) =>
            allowed.TryGetValue(defType, out var type) ? type : throw new JsonException($"Filter type not allowed: {defType}");
    }

    [Test]
    public void GridWithoutAFactoryUsesTheDefaultTest()
    {
        FilterDefFactory original = FilterDefFactory.Default;
        try
        {
            NumbersOnlyFactory mine = new();
            FilterDefFactory.Default = mine;
            using BunitContext ctx = new();
            ctx.JSInterop.Mode = JSRuntimeMode.Loose;
            ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            ctx.Services.AddLogging();
            DataGridModel<Product> model = new([new Product("C01", "Name01", 1m)]);
            model.AddColumn("Price", new Column<Product, decimal>("Price"), p => p.Price);
            var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
            Assert.That(cut.Instance.GetFilterDefFactory(), Is.SameAs(mine));
        }
        finally
        {
            FilterDefFactory.Default = original;
        }
    }

    [Test]
    public void RestoreGoesThroughTheDefaultFactoryTest()
    {
        Memento memento = new(null,
            new Dictionary<string, AFilterDef> { ["Price"] = new NumberDef<decimal> { From = 10 }, ["Name"] = new DefaultDef { Value = "x" } },
            []);
        string json = JsonSerializer.Serialize(memento);
        Memento back = JsonSerializer.Deserialize<Memento>(json)!;
        Assert.That(back.FiltersDefs.Keys, Is.EquivalentTo(new[] { "Price", "Name" }), "the stock factory reads every built-in definition");

        FilterDefFactory original = FilterDefFactory.Default;
        try
        {
            FilterDefFactory.Default = new NumbersOnlyFactory();
            var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>(json));
            Assert.That(ex!.ToString(), Does.Contain("DefaultDef"), "the allow-list set once applies to the restore");
            string numbersOnly = JsonSerializer.Serialize(memento with { FiltersDefs = new Dictionary<string, AFilterDef> { ["Price"] = new NumberDef<decimal> { From = 10 } } });
            Assert.That(JsonSerializer.Deserialize<Memento>(numbersOnly)!.FiltersDefs.Keys, Is.EquivalentTo(new[] { "Price" }));
        }
        finally
        {
            FilterDefFactory.Default = original;
        }
    }

    private enum Status { Open, Closed }

    private sealed class NoEnumsFactory : FilterDefFactory
    {
        protected override Type GetEnumType(string enumType) => throw new JsonException($"Enum type not allowed: {enumType}");
    }

    /// <summary>
    /// An <see cref="EnumDef"/> persists its enum's type name and member names. A member the enum no longer has loses its
    /// selection, not the whole memento; a type that is not an enum is refused as JSON; and the type name goes through the factory
    /// that reads the memento, the default, one given in the options or one on a property, so a host's allow-list covers it.
    /// </summary>
    [Test]
    public void PersistedEnumDefToleratesARenamedMemberAndGoesThroughTheFactoryTest()
    {
        Memento memento = new(null, new Dictionary<string, AFilterDef> { ["Status"] = new EnumDef(typeof(Status), false) { Selected = [Status.Open, Status.Closed] } }, []);
        string json = JsonSerializer.Serialize(memento);
        Assert.That(json, Does.Contain("\"Closed\""));

        EnumDef renamed = (EnumDef)JsonSerializer.Deserialize<Memento>(json.Replace("\"Closed\"", "\"Archived\""))!.FiltersDefs["Status"];
        Assert.That(renamed.Selected, Is.EquivalentTo(new Enum[] { Status.Open }), "the member that still exists keeps its selection");

        string notAnEnum = System.Text.RegularExpressions.Regex.Replace(json, "\"EnumType\":\"[^\"]*\"", "\"EnumType\":\"System.String\"");   // the nested type's name is escaped in the JSON
        Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>(notAnEnum))!.ToString(), Does.Contain("Not an enum"));

        string noSelection = System.Text.RegularExpressions.Regex.Replace(json, @"""Selected"":\[[^\]]*\]", @"""Selected"":null");
        Assert.That(((EnumDef)JsonSerializer.Deserialize<Memento>(noSelection)!.FiltersDefs["Status"]).Selected, Is.Empty, "a null selection reads as none selected");

        FilterDefFactory original = FilterDefFactory.Default;
        try
        {
            FilterDefFactory.Default = new NoEnumsFactory();
            Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>(json))!.ToString(), Does.Contain("Enum type not allowed"));
        }
        finally
        {
            FilterDefFactory.Default = original;
        }
        JsonSerializerOptions withFactory = new() { Converters = { new NoEnumsFactory() } };
        Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>(json, withFactory))!.ToString(),
            Does.Contain("Enum type not allowed"), "a factory given in the options");
        string onProperty = JsonSerializer.Serialize(new SavedFilter(memento.FiltersDefs["Status"]));
        Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SavedFilter>(onProperty))!.ToString(),
            Does.Contain("Enum type not allowed"), "a factory given as a property's converter");
    }

    private sealed record SavedFilter([property: System.Text.Json.Serialization.JsonConverter(typeof(NoEnumsFactory))] AFilterDef Filter);

    /// <summary>
    /// A persisted number for a value the enum does not define is dropped like a renamed member: kept, it would never let the
    /// filter read as all selected, so its icon would stay lit with no row affected.
    /// </summary>
    [Test]
    public void PersistedEnumDefDropsAnUndefinedNumberTest()
    {
        Memento memento = new(null, new Dictionary<string, AFilterDef> { ["Status"] = new EnumDef(typeof(Status), false) { Selected = [Status.Open, Status.Closed] } }, []);
        string json = JsonSerializer.Serialize(memento).Replace("\"Closed\"", "\"Closed\",\"7\"");
        EnumDef restored = (EnumDef)JsonSerializer.Deserialize<Memento>(json)!.FiltersDefs["Status"];
        Assert.Multiple(() =>
        {
            Assert.That(restored.Selected, Is.EquivalentTo(new Enum[] { Status.Open, Status.Closed }));
            Assert.That(restored.IsVoid(), Is.True, "every member selected: no filter");
        });
    }
    private sealed record Gauge(string Code, byte? Level);

    /// <summary>
    /// A markup column with a filter definition of its own registers without the factory, which the filter dialog does not
    /// ask for it either: before, the factory was asked anyway and refused a type <c>InputNumber</c> cannot bind.
    /// </summary>
    [Test]
    public void MarkupColumnWithItsOwnFilterDefSkipsTheFactoryTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        void Render(AFilterDef<byte?>? def) => ctx.Render<Grigliaria<Gauge>>(ps => ps
            .Add(g => g.Model, new DataGridModel<Gauge>([new Gauge("G1", 3)])).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<GridColumn<Gauge, byte?>>(0);
                builder.AddAttribute(1, "Name", "Level");
                builder.AddAttribute(2, "Property", (Func<Gauge, byte?>)(g => g.Level));
                builder.AddAttribute(3, "FilterDef", def);
                builder.CloseComponent();
            })));
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => Render(new NumberDef<byte>()));
            Assert.That(() => Render(null), Throws.InvalidOperationException.With.Message.Contains("Level"), "the factory still refuses it without one");
        });
    }

    /// <summary>
    /// A column added in code is validated when the grid gets the model, as a markup one is when it registers: before, a type
    /// the factory refuses failed only when the user clicked the column's filter icon. A column that is not filterable, or
    /// brings its own definition, does not ask the factory.
    /// </summary>
    [Test]
    public void ColumnAddedInCodeIsValidatedWhenTheGridGetsTheModelTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        static DataGridModel<Gauge> Model(Column<Gauge, byte?> column)
        {
            DataGridModel<Gauge> model = new([new Gauge("G1", 3)]);
            model.AddColumn("Level", column, g => g.Level);
            return model;
        }
        DataGridModel<Gauge> unbindable = Model(new Column<Gauge, byte?>("Level"));
        Assert.Multiple(() =>
        {
            Assert.That(() => ctx.Render<Grigliaria<Gauge>>(ps => ps.Add(g => g.Model, unbindable).Add(g => g.Spinner, new NoSpinner())),
                Throws.InvalidOperationException.With.Message.Contains("Level"), "on the first render");
            Assert.DoesNotThrow(() => ctx.Render<Grigliaria<Gauge>>(ps => ps
                .Add(g => g.Model, Model(new Column<Gauge, byte?>("Level") { Filterable = false })).Add(g => g.Spinner, new NoSpinner())));
            Assert.DoesNotThrow(() => ctx.Render<Grigliaria<Gauge>>(ps => ps
                .Add(g => g.Model, Model(new Column<Gauge, byte?>("Level") { FilterDef = new NumberDef<byte>() })).Add(g => g.Spinner, new NoSpinner())));
        });

        var cut = ctx.Render<Grigliaria<Gauge>>(ps => ps
            .Add(g => g.Model, Model(new Column<Gauge, byte?>("Level") { Filterable = false })).Add(g => g.Spinner, new NoSpinner()));
        Assert.That(() => cut.Render(ps => ps.Add(g => g.Model, unbindable)),
            Throws.InvalidOperationException.With.Message.Contains("Level"), "when the grid is given another model");
    }

    /// <summary>
    /// A column added in code to the model the grid already has is validated at the next render, as one on a new model is:
    /// before, it failed only when the user clicked its filter icon.
    /// </summary>
    [Test]
    public void ColumnAddedInCodeToTheGridsModelIsValidatedTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddLogging();
        DataGridModel<Gauge> model = new([new Gauge("G1", 3)]);
        model.AddColumn("Code", new Column<Gauge, string>("Code"), g => g.Code);
        var cut = ctx.Render<Grigliaria<Gauge>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));

        model.AddColumn("Level", new Column<Gauge, byte?>("Level"), g => g.Level);
        Assert.That(() => cut.Render(), Throws.InvalidOperationException.With.Message.Contains("Level"));
    }
}
