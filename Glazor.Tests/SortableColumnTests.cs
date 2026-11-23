using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.EntityFrameworkCore;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// A sortable column needs values the model can compare: <see cref="IComparable{T}"/> or <see cref="IComparable"/> ones,
/// nullable or not, or an object column's, compared value by value, unless the model supplies a comparer. One whose values cannot be compared is refused
/// when it registers, in code or in markup, pushed down to the database or not: the search on the sorted column compares in
/// memory either way.
/// </summary>
public class SortableColumnTests
{
    public sealed record Money(decimal Amount, string Currency);   // equal by value, with no order

    public readonly record struct Weight(decimal Kg) : IComparable<Weight>   // the generic interface only
    {
        public int CompareTo(Weight other) => Kg.CompareTo(other.Kg);
    }

    public sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Money Price { get; set; } = new(0, "EUR");
        public int? Stock { get; set; }
        public object? Tag { get; set; }
    }

    public sealed class NoContext : DbContext;

    private sealed class PriceModel(IEnumerable<Item> items) : DataGridModel<Item>(items)
    {
        protected override IComparer<object>? GetComparerForColumn(int columnIdx) => GetColumnName(columnIdx) == "Price"
            ? Comparer<object>.Create((a, b) => ((Money)a).Amount.CompareTo(((Money)b).Amount))
            : base.GetComparerForColumn(columnIdx);
    }

    private static Item[] Items() =>
    [
        new() { Id = 1, Name = "B", Price = new(20, "EUR"), Stock = 2, Tag = "y" },
        new() { Id = 2, Name = "C", Price = new(5, "EUR"), Stock = null, Tag = "z" },
        new() { Id = 3, Name = "A", Price = new(12, "EUR"), Stock = 1, Tag = "x" },
    ];

    // Registration queries nothing
    private static EFDataGridModel<NoContext, Item> EFModel() => new()
    {
        DbContextFactory = null!,
        BaseQueryFn = (_, _) => throw new InvalidOperationException("not queried"),
        ItemKeyExpr = i => i.Id,
    };

    private static void AddPrice(string how, bool sortable)
    {
        Column<Item, Money> column = new("Price") { Sortable = sortable };
        switch (how)
        {
            case "DataGridModel.AddColumn":
                new DataGridModel<Item>(Items()).AddColumn("Price", column, i => i.Price);
                break;
            case "EFDataGridModel.AddColumn":
                EFModel().AddColumn("Price", column, i => i.Price);
                break;
            case "EFDataGridModel.AddEFColumn":
                EFModel().AddEFColumn("Price", column, i => i.Price);
                break;
        }
    }

    /// <summary>
    /// Before, the column registered and the circuit ended when the user clicked its header.
    /// </summary>
    [TestCase("DataGridModel.AddColumn")]
    [TestCase("EFDataGridModel.AddColumn")]
    [TestCase("EFDataGridModel.AddEFColumn")]
    public void ColumnWhoseValuesCannotBeComparedIsRefusedTest(string how)
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => AddPrice(how, sortable: true));
        Assert.That(thrown!.Message, Does.Contain("Price").And.Contain(nameof(Money)).And.Contain("Sortable"));
        Assert.DoesNotThrow(() => AddPrice(how, sortable: false));
    }

    [Test]
    public void MarkupColumnWhoseValuesCannotBeComparedIsRefusedTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Item, Money>>(0);
            builder.AddAttribute(1, "Name", "Price");
            builder.AddAttribute(2, "Label", "Price");
            builder.AddAttribute(3, "Property", (Func<Item, Money>)(i => i.Price));
            builder.CloseComponent();
        };
        var thrown = Assert.Throws<InvalidOperationException>(() => ctx.Render<Grigliaria<Item>>(ps => ps
            .Add(g => g.Model, new DataGridModel<Item>(Items())).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ChildContent, columns)));
        Assert.That(thrown!.Message, Does.Contain("Price").And.Contain("Sortable"));
    }

    /// <summary>
    /// A nullable value type implements no <see cref="IComparable"/> itself, and an object column's values decide one by one.
    /// </summary>
    [Test]
    public void ColumnsWhoseValuesCanBeComparedStaySortableTest()
    {
        DataGridModel<Item> model = new(Items());
        model.AddColumn("Stock", new Column<Item, int?>("Stock"), i => i.Stock);
        model.AddColumn("Tag", new Column<Item, object?>("Tag"), i => i.Tag);
        model.SetSortedColumn("Stock", true);
        model.Sync();
        Assert.That(model.GetDataView().Select(i => i.Stock), Is.EqualTo(new int?[] { null, 1, 2 }));
        model.SetSortedColumn("Tag", false);
        model.Sync();
        Assert.That(model.GetDataView().Select(i => i.Tag), Is.EqualTo(new object[] { "z", "y", "x" }));
    }

    /// <summary>
    /// Comparer&lt;T&gt;.Default takes the generic interface, so a type that implements only IComparable&lt;T&gt; sorts and is searched
    /// like any other, nullable or not; before, it was refused as a type that cannot be compared.
    /// </summary>
    [Test]
    public async Task ColumnOfATypeComparableThroughTheGenericInterfaceOnlyIsSortableTest()
    {
        Assert.That(typeof(IComparable).IsAssignableFrom(typeof(Weight)), Is.False, "the test type implements the generic interface only");
        DataGridModel<Item> model = new(Items());
        model.AddColumn("Weight", new Column<Item, Weight>("Weight"), i => new Weight(i.Price.Amount));
        model.AddColumn("MaybeWeight", new Column<Item, Weight?>("Maybe"), i => i.Stock == null ? null : new Weight(i.Price.Amount));
        model.SetSortedColumn("Weight", true);
        model.Sync();
        Assert.That(model.GetDataView().Select(i => i.Price.Amount), Is.EqualTo(new[] { 5m, 12m, 20m }));
        Assert.That(await model.SearchOnSortedColumnAsync(new Weight(12)), Is.EqualTo(1), "the search on the sorted column compares through the same comparer");
        model.SetSortedColumn("MaybeWeight", false);
        model.Sync();
        Assert.That(model.GetDataView().Select(i => i.Price.Amount), Is.EqualTo(new[] { 20m, 12m, 5m }), "nulls last descending, the rest by the generic interface");
        Assert.DoesNotThrow(() => EFModel().AddColumn("Weight", new Column<Item, Weight>("Weight"), i => new Weight(i.Price.Amount)), "the EF model applies the same rule");
    }

    [Test]
    public void ColumnWithAComparerFromTheModelIsSortableTest()
    {
        PriceModel model = new(Items());
        model.AddColumn("Price", new Column<Item, Money>("Price"), i => i.Price);
        model.SetSortedColumn("Price", true);
        model.Sync();
        Assert.That(model.GetDataView().Select(i => i.Price.Amount), Is.EqualTo(new[] { 5m, 12m, 20m }));
    }

    // Any negative or positive value is a valid result, the extremes included
    private sealed class ExtremeComparerModel(IEnumerable<Item> items) : DataGridModel<Item>(items)
    {
        protected override IComparer<object>? GetComparerForColumn(int columnIdx) => Comparer<object>.Create((a, b) =>
            ((Money)a).Amount < ((Money)b).Amount ? int.MinValue : ((Money)a).Amount > ((Money)b).Amount ? int.MaxValue : 0);
    }

    [Test]
    public void DescendingSortTakesAComparerReturningIntMinValueTest()
    {
        ExtremeComparerModel model = new(Items());
        model.AddColumn("Price", new Column<Item, Money>("Price"), i => i.Price);
        model.SetSortedColumn("Price", false);
        model.Sync();
        Assert.That(model.GetDataView().Select(i => i.Price.Amount), Is.EqualTo(new[] { 20m, 12m, 5m }));
    }

    /// <summary>
    /// A memento outlives the code that saved it: its sort on a column whose values can no longer be compared is dropped. Before,
    /// the restore sorted the column and the render threw.
    /// </summary>
    [Test]
    public void RestoredSortOnAColumnNoLongerSortableIsIgnoredTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Item> model = new(Items());
        model.AddColumn("Name", new Column<Item, string>("Name"), i => i.Name);
        model.AddColumn("Price", new Column<Item, Money>("Price") { Sortable = false }, i => i.Price);
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        Memento saved = new(null, new Dictionary<string, AFilterDef>(), [new("Price", true)]);
        Assert.DoesNotThrow(() =>
        {
            cut.InvokeAsync(() => cut.Instance.RestoreMemento(saved)).GetAwaiter().GetResult();
            cut.Render();
        });
        Assert.That(model.GetSortedColumn(), Is.Null);
    }

    /// <summary>
    /// A sort the host sets in code on a column the user cannot sort, but whose values can be compared, comes back with a restore
    /// of the grid's own memento and with a reset.
    /// </summary>
    [Test]
    public void CodeSortOnAColumnTheUserCannotSortIsRestoredTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Item> model = new(Items());
        model.AddColumn("Name", new Column<Item, string>("Name"), i => i.Name);
        model.AddColumn("Id", new Column<Item, int>("Id") { Sortable = false }, i => i.Id);
        model.SetSortedColumn("Id", false);   // the host's own order
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        Memento own = cut.Instance.CreateMemento();
        cut.InvokeAsync(() => model.SetSortedColumn("Name", true)).GetAwaiter().GetResult();
        cut.InvokeAsync(() => cut.Instance.RestoreMemento(own)).GetAwaiter().GetResult();
        Assert.That(model.GetSortedColumn(), Is.EqualTo("Id"), "the grid's own memento");
        cut.InvokeAsync(() => model.SetSortedColumn("Name", true)).GetAwaiter().GetResult();
        cut.InvokeAsync(() => cut.Instance.RestoreMemento(null)).GetAwaiter().GetResult();
        Assert.That(model.GetSortedColumn(), Is.EqualTo("Id"), "the reset");
    }

    private sealed record Ranked(int Id, int Rank);

    /// <summary>
    /// Rows with equal values keep their order under an in-memory sort, so a row added or removed does not move the others.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void EqualRowsKeepTheirOrderUnderASortTest(bool ascending)
    {
        DataGridModel<Ranked> model = new(Enumerable.Range(0, 200).Select(i => new Ranked(i, i % 3)));
        model.AddColumn("Rank", new Column<Ranked, int>("Rank"), r => r.Rank);
        model.SetSortedColumn("Rank", ascending);
        int[] Ones() => [.. model.GetDataView().Where(r => r.Rank == 1).Select(r => r.Id)];
        int[] before = Ones();
        Assert.That(before, Is.Ordered, "in the order the rows were given");

        model.GetData().Add(new Ranked(200, 0));
        Assert.That(Ones(), Is.EqualTo(before));
    }
}
