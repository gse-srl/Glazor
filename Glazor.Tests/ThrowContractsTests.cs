using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.EntityFrameworkCore;
using Glazor.Misc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// The exception contract follows the BCL's: a bad argument is an <see cref="ArgumentException"/>, a call the object's state
/// does not allow is an <see cref="InvalidOperationException"/>, and sibling methods agree with each other.
/// </summary>
public class ThrowContractsTests
{
    private sealed record Product(string Code, string Name, decimal Price);

    private static DataGridModel<Product> ModelOf(int rowCount)
    {
        DataGridModel<Product> model = new(Enumerable.Range(0, rowCount)
            .Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray());
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10, Filterable = false }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        return model;
    }

    private static IRenderedComponent<Grigliaria<Product>> RenderGrid(BunitContext ctx, IDataGridModel<Product> model, bool layoutable)
        => ctx.Render<Grigliaria<Product>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, layoutable));

    private static T Sync<T>(IRenderedComponent<Grigliaria<Product>> cut, Func<T> action)
        => cut.InvokeAsync(action).GetAwaiter().GetResult();

    /// <summary>
    /// SetCurrentCell refuses a column that does not exist or is not shown before it moves the row, with or without a
    /// HorzResizer: a call that throws leaves the current row where it was.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void SetCurrentCellChecksTheColumnBeforeMovingTheRowTest(bool withResizer)
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(5);
        var cut = ctx.Render<Grigliaria<Product>>(ps =>
        {
            ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())
                .Add(g => g.CellCursorEnabled, true);
            if (withResizer)
                ps.Add(g => g.HorzResizer, GridHorzResizers.Fixed(30));
        });
        Sync(cut, () => { cut.Instance.SetColumnsToShow("Name"); return 0; });   // Price not shown
        int startRow = model.GetCurrentRow();
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Sync(cut, () => cut.Instance.SetCurrentCell(3, 99)));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(startRow), "a column that does not exist");
            Assert.Throws<ArgumentException>(() => Sync(cut, () => cut.Instance.SetCurrentCell(3, model.GetColumnIndex("Price"))));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(startRow), "a column that is not shown");
        });
        Assert.That(Sync(cut, () => cut.Instance.SetCurrentCell(3, model.GetColumnIndex("Name"))), Is.True);
        Assert.That((model.GetCurrentRow(), cut.Instance.GetCurrentCol()), Is.EqualTo((3, "Name")));
    }

    [Test]
    public void SetPreferredSizeOnAGridThatIsNotLayoutableIsAStateErrorTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), layoutable: false);
        Assert.Throws<InvalidOperationException>(() => Sync(cut, () => { cut.Instance.SetPreferredSize("Name", 20); return 0; }),
            "the same state SetLayout refuses with the same exception");
    }

    [Test]
    public void SetPreferredSizeOnAnUnknownColumnNamesTheArgumentTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), layoutable: true);
        var ex = Assert.Throws<ArgumentException>(() => Sync(cut, () => { cut.Instance.SetPreferredSize("Descr", 20); return 0; }));
        Assert.That(ex!.ParamName, Is.EqualTo("columnName"));
        Assert.That(ex.Message, Does.Contain("Descr"));
    }

    /// <summary>
    /// SetFilters is programmer input like SetFilter: a column it cannot filter is an error, not a row to skip. Only
    /// RestoreMemento tolerates a stale name.
    /// </summary>
    [Test]
    public void SetFiltersRejectsWhatSetFilterRejectsTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderGrid(ctx, ModelOf(3), layoutable: false);
        DefaultDef def = new() { Value = "Name0" };

        var unknown = Assert.Throws<ArgumentException>(() => Sync(cut, () =>
            { cut.Instance.SetFilters(new Dictionary<string, AFilterDef> { ["Descr"] = def }); return 0; }));
        Assert.That(unknown!.Message, Does.Contain("Descr"));
        var notFilterable = Assert.Throws<ArgumentException>(() => Sync(cut, () =>
            { cut.Instance.SetFilters(new Dictionary<string, AFilterDef> { ["Code"] = def }); return 0; }));
        Assert.That(notFilterable!.Message, Does.Contain("Code"));
        Assert.That(cut.Instance.GetFilters(), Is.Empty, "a refused set applies nothing");

        Assert.Throws<ArgumentException>(() => Sync(cut, () => { cut.Instance.SetFilter("Code", def); return 0; }), "as SetFilter does");

        Sync(cut, () => { cut.Instance.SetFilters(new Dictionary<string, AFilterDef> { ["Name"] = def, ["Price"] = new NumberDef<decimal>() }); return 0; });
        Assert.That(cut.Instance.GetFilters().Keys, Is.EquivalentTo(new[] { "Name" }), "a void definition is still left out");

        Sync(cut, () => { cut.Instance.SetFilters(new Dictionary<string, AFilterDef> { ["Name"] = def, ["Price"] = null! }); return 0; });
        Assert.That(cut.Instance.GetFilters().Keys, Is.EquivalentTo(new[] { "Name" }), "a null one too, as SetFilter takes it");
    }

    /// <summary>
    /// A definition must take every value its column holds: a number on an object column is refused when it is set, not on
    /// the first row it cannot compare, during the render.
    /// </summary>
    [Test]
    public void SetFilterRefusesADefinitionNarrowerThanTheColumnTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = ModelOf(3);
        model.AddColumn("Any", new Column<Product, object>("Any") { PreferredSize = 10 }, p => p.Name);
        var cut = RenderGrid(ctx, model, layoutable: false);
        Assert.Throws<ArgumentException>(() => Sync(cut, () => { cut.Instance.SetFilter("Any", new NumberDef<int> { From = 1 }); return 0; }));
        Assert.That(cut.Instance.GetFilters(), Is.Empty);
    }

    [Test]
    public void ChunkedListRangeErrorsAreArgumentOutOfRangeTest()
    {
        using OnDiskChunkedList<string> list = new(OnDiskChunkedList.MinChunksInMemory, OnDiskChunkedList.MinChunkSize);
        list.Add("a");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(2, "c"), "one past the end is allowed, two is not");
        Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(1));
        list.Insert(1, "b");
        Assert.That(list, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void ChunkedListConstructorNamesTheArgumentBelowItsMinimumTest()
    {
        var chunks = Assert.Throws<ArgumentOutOfRangeException>(() => new OnDiskChunkedList<string>(OnDiskChunkedList.MinChunksInMemory - 1, OnDiskChunkedList.MinChunkSize));
        Assert.That(chunks!.ParamName, Is.EqualTo("chunksInMemory"));
        var size = Assert.Throws<ArgumentOutOfRangeException>(() => new OnDiskChunkedList<string>(OnDiskChunkedList.MinChunksInMemory, OnDiskChunkedList.MinChunkSize - 1));
        Assert.That(size!.ParamName, Is.EqualTo("chunkSize"));
    }

    /// <summary>
    /// A missing ItemKeyFn is a configuration error: it is reported when the edit or the insert starts, as a state error, not
    /// at the post or the cancel that would need the key.
    /// </summary>
    [Test]
    public async Task EditableModelWithoutAKeyRefusesToStartAnEditTest()
    {
        EditableDataGridModel<Product> model = new(() => ValueTask.FromResult<IList<Product>>([]));
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        model.GetData().Add(new Product("C01", "Name01", 1m));
        model.SetCurrentRow(0);

        var edit = Assert.ThrowsAsync<InvalidOperationException>(async () => await model.EditAsync());
        Assert.That(edit!.Message, Does.Contain("ItemKeyFn"));
        Assert.That(model.IsEditPending(), Is.False, "nothing started");
        Assert.ThrowsAsync<InvalidOperationException>(async () => await model.InsertAsync(new Product("C02", "Name02", 2m), true));
        Assert.That(model.IsInsertPending(), Is.False);

        EditableDataGridModel<Product> keyed = new(() => ValueTask.FromResult<IList<Product>>([new Product("C01", "Name01", 1m)])) { ItemKeyFn = p => p.Code };
        keyed.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        keyed.GetData().Add(new Product("C01", "Name01", 1m));
        keyed.SetCurrentRow(0);
        await keyed.EditAsync();
        Assert.That(keyed.IsEditPending(), Is.True, "with a key the same call starts the edit");
        await keyed.CancelAsync();
    }

    /// <summary>
    /// A grid model that is neither ordered nor a data grid model: the smallest <see cref="AGridModel{TGridItem}"/> there is.
    /// </summary>
    private sealed class BareModel : AGridModel<Product>
    {
        public override int GetCurrentRowCount() => 0;
        public override int GetRowCount() => 0;
        public override bool RequiresFetch() => false;
        public override ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default) => ValueTask.FromResult(0);
        public override int GetDataVersion() => 0;
        public override Product GetItem(int rowIndex) => throw new ArgumentOutOfRangeException(nameof(rowIndex));
        public override int GetCurrentRow() => -1;
        public override IReadOnlyList<Product> GetDataView() => [];
        public override void SetCurrentRow(int rowIndex) { }
        public override Func<Product, object>? GetItemKeyFn() => null;
    }

    /// <summary>
    /// <see cref="MultiSelGrid{TGridItem}"/> wraps an ordered model or a data grid model; any other is a bad argument,
    /// named as such at the first render instead of an <see cref="InvalidCastException"/>.
    /// </summary>
    [Test]
    public void MultiSelGridNamesAModelItCannotWrapTest()
    {
        using BunitContext ctx = NewContext();
        var ex = Assert.Throws<ArgumentException>(() => ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, new BareModel())));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.ParamName, Is.EqualTo("Model"));
            Assert.That(ex.Message, Does.Contain("BareModel"));
        });
    }

    /// <summary>
    /// The filter factory refuses a numeric type <c>InputNumber</c> cannot bind, so that a filterable column of such a type fails
    /// where it is declared; the consumer sets <c>Filterable="false"</c>.
    /// </summary>
    [Test]
    public void FilterFactoryRefusesANumericTypeInputNumberCannotBindTest()
    {
        DataGridModel<Product> model = ModelOf(0);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new FilterDefFactory().CreateDefinition(model, "Qty", new Column<Product, uint>("Qty")));
        Assert.That(ex!.Message, Does.Contain("UInt32").And.Contain("Qty"));
        Assert.That(new FilterDefFactory().CreateDefinition(model, "Price", new Column<Product, decimal>("Price")), Is.TypeOf<NumberDef<decimal>>());
    }

    /// <summary>
    /// A persisted <c>defType</c> that is not a filter definition is refused before anything is instantiated, and every way a
    /// stored definition can fail to read, a type renamed or removed among them, is a <see cref="JsonException"/> whose inner
    /// exception names the cause.
    /// </summary>
    [Test]
    public void PersistedDefTypeMustBeAFilterDefinitionTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>("{\"defType\":\"System.Text.StringBuilder\",\"def\":{}}"))!.InnerException,
                Is.TypeOf<InvalidCastException>());
            Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>("{\"defType\":\"System.NoSuchFilterDef\",\"def\":{}}"))!.InnerException,
                Is.TypeOf<TypeLoadException>());
            Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>("{\"defType\":\"Glazor.NoSuchDef, NoSuchAssembly\",\"def\":{}}"))!.InnerException,
                Is.InstanceOf<FileNotFoundException>());
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>("{\"defType\":null,\"def\":{}}"));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>("{\"defType\":\"Glazor.Grds.Filters.ARangeDef`2, Glazor\",\"def\":{}}"),
                "an open generic or abstract definition cannot be built");
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>("{\"def\":{}}"), "a def with no type is malformed JSON, not a null argument");
        });
    }

    /// <summary>
    /// A type name nested deep enough to overflow the stack of <c>Type.GetType</c> is refused as JSON, for a definition and for
    /// an <see cref="EnumDef"/>'s enum, before any <c>GetDefType</c> override sees it.
    /// </summary>
    [Test]
    public void DeeplyNestedTypeNameFailsAsJsonTest()
    {
        string deep = string.Concat(Enumerable.Repeat("a`1[[", 10_000)) + "a" + string.Concat(Enumerable.Repeat("]]", 10_000));
        string name = JsonSerializer.Serialize(deep);
        Assert.Multiple(() =>
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>("{\"defType\":" + name + ",\"def\":{}}"));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AFilterDef>(
                "{\"defType\":\"Glazor.Grds.Filters.EnumDef, Glazor\",\"def\":{\"EnumType\":" + name + ",\"IncludeNull\":true,\"Selected\":[]}}"));
        });
    }

    /// <summary>A value of the wrong type inside a stored definition is reported at the definition's place in the memento.</summary>
    [Test]
    public void JsonErrorInsideADefinitionHasTheMementosPathTest()
    {
        Memento memento = new(null, new Dictionary<string, AFilterDef> { ["Price"] = new NumberDef<decimal> { From = 10 } }, []);
        string json = JsonSerializer.Serialize(memento).Replace("\"From\":10", "\"From\":\"abc\"");
        Assert.That(json, Does.Contain("\"abc\""));
        Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>(json))!.Path, Is.EqualTo("$.FiltersDefs.Price"));
    }

    /// <summary>
    /// A memento naming an enum type that was renamed since it was stored, or holding a layout without its members, fails as
    /// JSON, the one exception a host catches around a restore.
    /// </summary>
    [Test]
    public void StaleOrMalformedMementoFailsAsJsonTest()
    {
        Memento memento = new(null, new Dictionary<string, AFilterDef> { ["Day"] = new EnumDef(typeof(DayOfWeek), false) { Selected = [DayOfWeek.Monday] } }, []);
        string json = JsonSerializer.Serialize(memento);
        string renamedEnum = json.Replace("System.DayOfWeek", "System.NoSuchDayOfWeek");
        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>(renamedEnum))!.InnerException, Is.TypeOf<TypeLoadException>());
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>("{\"Layout\":{\"columnsToShow\":null,\"attributes\":{}},\"FiltersDefs\":{},\"SorterColumns\":[]}"));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Memento>("{\"Layout\":{\"columnsToShow\":[],\"attributes\":null},\"FiltersDefs\":{},\"SorterColumns\":[]}"));
        });
    }

    private sealed class Tag(string text) { public string Text => text; }
    private sealed class PlainKey { public int Id; }
    private sealed record Row(int Id, Tag Tag, string Code);

    /// <summary>
    /// The key of a paged model is compared member by member, so each member must be comparable; a composite key of a class
    /// that does not compare by value cannot find the current row again, and is refused when the model is built.
    /// </summary>
    [Test]
    public void ItemKeyMembersMustCompareTest()
    {
        ItemKeyHelper<Row> composite = new(r => new { r.Id, r.Tag });
        Tag shared = new("t");
        Assert.Multiple(() =>
        {
            Assert.That(composite.Compare(new Row(1, shared, "a"), new Row(2, shared, "b")), Is.LessThan(0), "decided by Id");
            Assert.That(composite.Compare(new Row(1, shared, "a"), new Row(1, shared, "b")), Is.Zero, "the same tag instance compares equal");
            var ex = Assert.Throws<InvalidOperationException>(() => composite.Compare(new Row(1, new Tag("t"), "a"), new Row(1, new Tag("t"), "b")));
            Assert.That(ex!.Message, Does.Contain("IComparable"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => new ItemKeyHelper<Row>(r => new PlainKey { Id = r.Id }))!.Message,
                Does.Contain("compare by value"));
            Assert.DoesNotThrow(() => new ItemKeyHelper<Row>(r => r.Code), "a plain key is its own only member");
        });
    }

    /// <summary>
    /// An in-memory model reads its rows from a list, so a row out of range is the list's <see cref="ArgumentOutOfRangeException"/>,
    /// the exception the paged models throw too and <c>IGridModel</c> documents.
    /// </summary>
    [Test]
    public void InMemoryRowOutOfRangeIsTheListsExceptionTest()
    {
        DataGridModel<Product> model = ModelOf(3);
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => model.GetItem(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.GetValueAt(3, 0));
            Assert.That(model.GetItem(2).Code, Is.EqualTo("C02"));
        });
    }

    /// <summary>
    /// Clearing the sort on an in-memory model that has none is no change either: the data version stays, so the grid does not
    /// re-render for it.
    /// </summary>
    [Test]
    public void ClearingTheSortOnAnUnsortedModelIsNoChangeTest()
    {
        DataGridModel<Product> model = ModelOf(3);
        model.Sync();   // the version moves at the sync that follows a change
        int version = model.GetDataVersion();
        model.SetSortedColumn(null, false);
        model.SetSortedColumn(null, true);
        model.Sync();
        Assert.That(model.GetDataVersion(), Is.EqualTo(version));
        model.SetSortedColumn("Name", false);
        model.Sync();
        Assert.That(model.GetDataVersion(), Is.Not.EqualTo(version), "a real sort is a change");
    }

    /// <summary>
    /// An in-memory model whose sync throws stays in sync: the next read does not run the sync again, and the next change
    /// does.
    /// </summary>
    [Test]
    public void AFailedSyncIsNotRetriedUntilTheNextChangeTest()
    {
        bool fail = false;
        int reads = 0;
        DataGridModel<Product> model = new([new("C1", "b", 1m), new("C2", "a", 2m), new("C3", "c", 3m)]);
        model.AddColumn("Name", new Column<Product, string>("Name"), p => { reads++; return fail ? throw new InvalidOperationException("getter") : p.Name; });
        model.Sync();

        fail = true;
        model.SetSortedColumn("Name", true);
        Assert.Throws<InvalidOperationException>(() => model.Sync());
        int version = model.GetDataVersion();
        reads = 0;
        Assert.DoesNotThrow(() => model.GetRowCount());
        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.Zero, "the sort is not run again");
            Assert.That(model.GetDataVersion(), Is.EqualTo(version));
        });

        fail = false;
        model.SetSortedColumn("Name", false);
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Name), Is.EqualTo(new[] { "c", "b", "a" }));
    }

    private sealed class PagedModel() : DataGridModelWrapper<Product>(ModelOf(3))
    {
        public override bool RequiresFetch() => true;
    }

    /// <summary>
    /// A grid over a model that fetches needs the application's spinner: without one the first render throws.
    /// </summary>
    [Test]
    public void PagedModelWithoutASpinnerThrowsOnTheFirstRenderTest()
    {
        using BunitContext ctx = NewContext();
        Assert.Throws<ArgumentNullException>(() => ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, new PagedModel())));
    }
}
