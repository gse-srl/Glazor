using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Text.Json;

namespace Glazor.Tests;

/// <summary>
/// <see cref="ADataGrid{TGridItem}.Memento"/> exists to be compared: the documented persistence pattern saves
/// a snapshot on every AfterSync and relies on equality to drop the identical ones. Every member of the
/// record is a collection or a mutable class, and <see cref="ADataGrid{TGridItem}.CreateMemento"/> allocates
/// fresh instances on every call, so the equality has to be content based or the de-duplication silently
/// never fires.
/// </summary>
public class MementoEqualityTests
{
    private sealed record Product(string Code, string Name, decimal Price);

    private static DataGridModel<Product> ModelOf(int rowCount)
    {
        DataGridModel<Product> model = new(Enumerable.Range(0, rowCount)
            .Select(i => new Product($"C{i:D2}", $"Name{i:D2}", i * 1.5m)).ToArray());
        model.AddColumn("Code", new Column<Product, string>("Code") { Fixed = true, PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10 }, p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price") { PreferredSize = 10 }, p => p.Price);
        return model;
    }

    private static IRenderedComponent<Grigliaria<Product>> RenderLayoutableGrid(BunitContext ctx, IDataGridModel<Product> model)
        => ctx.Render<Grigliaria<Product>>(ps => ps
            .Add(g => g.Model, model)
            .Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.Layoutable, true));

    // ─────────────────────────────────── Layout ───────────────────────────────────

    [Test]
    public void LayoutCloneEqualsItsSourceTest()
    {
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Name"];
        layout.attributes.Add("Name", new(20));

        GridLayout clone = layout.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.EqualTo(layout), "a clone is the same layout");
            Assert.That(clone.GetHashCode(), Is.EqualTo(layout.GetHashCode()), "and hashes alike");
        });
    }

    /// <summary>
    /// columnsToShow carries the display order, so it must not be compared as a set.
    /// </summary>
    [Test]
    public void LayoutWithReorderedColumnsIsNotEqualTest()
    {
        GridLayout a = new() { columnsToShow = ["Code", "Name"] };
        GridLayout b = new() { columnsToShow = ["Name", "Code"] };

        Assert.That(b, Is.Not.EqualTo(a));
    }

    [Test]
    public void LayoutWithADifferentWidthIsNotEqualTest()
    {
        GridLayout a = new() { columnsToShow = ["Code"] };
        a.attributes.Add("Code", new(10));
        GridLayout b = new() { columnsToShow = ["Code"] };
        b.attributes.Add("Code", new(20));

        Assert.Multiple(() =>
        {
            Assert.That(b, Is.Not.EqualTo(a), "a resized column is a different layout");
            Assert.That(b.Clone(), Is.Not.EqualTo(a), "and so is its clone");
        });
    }

    [Test]
    public void LayoutWithAnExtraAttributeIsNotEqualTest()
    {
        GridLayout a = new() { columnsToShow = ["Code", "Name"] };
        a.attributes.Add("Code", new(10));
        GridLayout b = a.Clone();
        b.attributes.Add("Name", new(20));

        Assert.That(b, Is.Not.EqualTo(a));
    }

    // ─────────────────────────────────── Memento ───────────────────────────────────

    /// <summary>
    /// The regression this file is really about: nothing about the grid changed between the two calls, so the
    /// caller must be able to recognise the snapshot as a duplicate and skip the save.
    /// </summary>
    [Test]
    public void TwoMementosOfAnUnchangedGridAreEqualTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderLayoutableGrid(ctx, ModelOf(5));

        Memento first = cut.Instance.CreateMemento();
        Memento second = cut.Instance.CreateMemento();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first), "CreateMemento allocates, otherwise this proves nothing");
            Assert.That(second, Is.EqualTo(first), "an unchanged grid yields an equal memento");
            Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()));
        });
    }

    [Test]
    public void HidingAColumnChangesTheMementoTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderLayoutableGrid(ctx, ModelOf(5));

        Memento before = cut.Instance.CreateMemento();
        cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Code", "Name")).Wait();

        Assert.That(cut.Instance.CreateMemento(), Is.Not.EqualTo(before));
    }

    [Test]
    public void ResizingAColumnChangesTheMementoTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderLayoutableGrid(ctx, ModelOf(5));

        Memento before = cut.Instance.CreateMemento();
        cut.InvokeAsync(() => cut.Instance.SetPreferredSize("Name", 42)).Wait();

        Assert.That(cut.Instance.CreateMemento(), Is.Not.EqualTo(before));
    }

    [Test]
    public void SortingChangesTheMementoTest()
    {
        using BunitContext ctx = NewContext();
        var model = ModelOf(5);
        var cut = RenderLayoutableGrid(ctx, model);

        Memento unsorted = cut.Instance.CreateMemento();
        cut.InvokeAsync(() => model.SetSortedColumn("Name", true)).Wait();
        Memento ascending = cut.Instance.CreateMemento();
        cut.InvokeAsync(() => model.SetSortedColumn("Name", false)).Wait();
        Memento descending = cut.Instance.CreateMemento();

        Assert.Multiple(() =>
        {
            Assert.That(ascending, Is.Not.EqualTo(unsorted), "sorting is part of the snapshot");
            Assert.That(descending, Is.Not.EqualTo(ascending), "so is the direction");
            Assert.That(cut.Instance.CreateMemento(), Is.EqualTo(descending), "and it settles again");
        });
    }

    /// <summary>
    /// A snapshot read back from a store equals the one it was saved from, filters included: <see cref="AFilterDef"/>
    /// derives its equality from the record it serializes to, with the runtime type as part of the identity, so the
    /// dictionary comparer compares the definitions by value and the persistence pattern in docs/grigliaria.md does not
    /// write the same snapshot twice after a RestoreMemento. A definition written by a library user inherits that through
    /// its own serialized record.
    /// </summary>
    [Test]
    public void ADeserializedMementoEqualsTheOneItWasSavedFromTest()
    {
        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Name"];
        layout.attributes.Add("Name", new(20));
        DefaultDef nameFilter = new() { Value = "abc", Contains = true, IncludeNull = false };
        Memento saved = new(
            layout,
            ImmutableDictionary<string, AFilterDef>.Empty.Add("Name", nameFilter),
            [new("Name", true)]);

        string json = JsonSerializer.Serialize(saved);
        Memento restored =
            JsonSerializer.Deserialize<Memento>(json).RequireNotNull();

        // The parts that do compare by content, to show the round trip itself is sound.
        Assert.That(restored.Layout, Is.EqualTo(saved.Layout), "the layout round-trips and compares by content");
        Assert.That(restored.SorterColumns, Is.EqualTo(saved.SorterColumns), "so does the sort order");

        // The filter values are identical, only the instances differ.
        DefaultDef restoredFilter = (DefaultDef)restored.FiltersDefs["Name"];
        Assert.That((restoredFilter.Value, restoredFilter.Contains, restoredFilter.IncludeNull),
            Is.EqualTo((nameFilter.Value, nameFilter.Contains, nameFilter.IncludeNull)),
            "every field of the filter came back");

        Assert.That(restoredFilter, Is.EqualTo(nameFilter),
            "AFilterDef does not override Equals, so two definitions holding the same values are not equal");
        Assert.That(restored, Is.EqualTo(saved),
            "and that is what stops the whole memento from being recognised as unchanged");
    }

    /// <summary>
    /// The order the width overrides happen to sit in is not part of the layout. A Dictionary reuses the slot
    /// freed by a removal, so resizing a column, hiding it and resizing another leaves the entries in a
    /// different order from setting the survivors outright, with nothing about the layout differing.
    /// </summary>
    [Test]
    public void LayoutIgnoresTheOrderOfItsWidthOverridesTest()
    {
        GridLayout shuffled = new() { columnsToShow = ["Code", "Name", "Price"] };
        shuffled.attributes["Code"] = new(10);
        shuffled.attributes["Dropped"] = new(99);
        shuffled.attributes["Name"] = new(20);
        shuffled.attributes.Remove("Dropped");
        shuffled.attributes["Price"] = new(30);

        GridLayout straight = new() { columnsToShow = ["Code", "Name", "Price"] };
        straight.attributes["Code"] = new(10);
        straight.attributes["Name"] = new(20);
        straight.attributes["Price"] = new(30);

        Assert.That(shuffled.attributes.Keys, Is.Not.EqualTo(straight.attributes.Keys).AsCollection,
            "the premise: the two dictionaries enumerate in a different order");
        Assert.Multiple(() =>
        {
            Assert.That(shuffled, Is.EqualTo(straight), "same overrides, so the same layout");
            Assert.That(shuffled.GetHashCode(), Is.EqualTo(straight.GetHashCode()),
                "and it has to hash alike however it enumerates");
        });
    }

    /// <summary>
    /// The same, for the filters a snapshot carries.
    /// </summary>
    [Test]
    public void MementoIgnoresTheOrderOfItsFiltersTest()
    {
        DefaultDef code = new() { Value = "c" };
        DefaultDef name = new() { Value = "n" };
        DefaultDef price = new() { Value = "p" };

        // As above: the entry added after the removal reuses the freed slot, so the two enumerate differently.
        Dictionary<string, AFilterDef> shuffled = new()
        {
            ["Code"] = code,
            ["Dropped"] = new DefaultDef { Value = "d" },
            ["Name"] = name,
        };
        shuffled.Remove("Dropped");
        shuffled["Price"] = price;
        Dictionary<string, AFilterDef> straight = new()
        {
            ["Code"] = code,
            ["Name"] = name,
            ["Price"] = price,
        };

        Assert.That(shuffled.Keys, Is.Not.EqualTo(straight.Keys).AsCollection,
            "the premise: the two dictionaries enumerate in a different order");

        GridLayout layout = new() { columnsToShow = ["Code", "Name", "Price"] };
        Memento a = new(layout, shuffled, [new("Code", true)]);
        Memento b = new(layout, straight, [new("Code", true)]);

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    // a snapshot's own record type is part of its identity

    /// <summary>
    /// Memento is a public record, so extending it to carry an extra field is a reasonable thing for a host
    /// to do - a schema version, the key it was saved under. The Equals the compiler writes for the derived
    /// record compares EqualityContract first, so if the hand-written one on the base does not, the two
    /// disagree about the same pair of objects.
    /// </summary>
    private sealed record TaggedMemento(
        GridLayout? Layout,
        IReadOnlyDictionary<string, AFilterDef> FiltersDefs,
        IReadOnlyList<SortedColumn> SorterColumns,
        string Tag)
        : Memento(Layout, FiltersDefs, SorterColumns);

    private static GridLayout ALayout() => new() { columnsToShow = ["Code", "Name"] };

    [Test]
    public void ADerivedMementoIsNotItsBaseTest()
    {
        GridLayout layout = ALayout();
        IReadOnlyDictionary<string, AFilterDef> filters = ImmutableDictionary<string, AFilterDef>.Empty;
        SortedColumn[] sorters = [new("Code", true)];

        Memento plain = new(layout, filters, sorters);
        Memento derived = new TaggedMemento(layout, filters, sorters, "extra");

        Assert.Multiple(() =>
        {
            Assert.That(plain.Equals(derived), Is.False, "the derived snapshot carries state the base does not");
            Assert.That(derived.Equals(plain), Is.False, "and both sides have to agree about it");
            Assert.That(plain.Equals((object)derived), Is.False, "through the object overload too");
        });
    }

    /// <summary>
    /// The contract check must not cost the derived record its own equality.
    /// </summary>
    [Test]
    public void TwoDerivedMementosStillCompareTest()
    {
        GridLayout layout = ALayout();
        IReadOnlyDictionary<string, AFilterDef> filters = ImmutableDictionary<string, AFilterDef>.Empty;
        SortedColumn[] sorters = [new("Code", true)];

        TaggedMemento a = new(layout, filters, sorters, "same");
        TaggedMemento b = new(layout.Clone(), filters, [new("Code", true)], "same");
        TaggedMemento c = new(layout, filters, sorters, "different");

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b), "same type, same state");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(c), "and the extra state is what tells them apart");
        });
    }

    /// <summary>
    /// The recorded policy: only RestoreMemento tolerates a stale column name, the direct setters treat it as an error.
    /// Before, the name was accepted and then either thrown from inside the render tree, without a
    /// HorzResizer, or dropped silently, with one.
    /// </summary>
    [Test]
    public void SetColumnsToShowRejectsAnUnknownColumnTest()
    {
        using BunitContext ctx = NewContext();
        var cut = RenderLayoutableGrid(ctx, ModelOf(5));

        var ex = Assert.Throws<ArgumentException>(() => cut.InvokeAsync(() => cut.Instance.SetColumnsToShow("Code", "Descr")).GetAwaiter().GetResult());
        Assert.That(ex!.Message, Does.Contain("Descr"));
        Assert.That(cut.Instance.GetShownColumns(), Is.EqualTo(new[] { "Code", "Name", "Price" }), "nothing changed");

        GridLayout layout = new();
        layout.columnsToShow = ["Code", "Descr"];
        Assert.Throws<ArgumentException>(() => cut.InvokeAsync(() => cut.Instance.SetLayout(layout)).GetAwaiter().GetResult(), "SetLayout is a direct setter too");
        Assert.That(cut.Instance.GetLayout()!.columnsToShow, Is.EqualTo(new[] { "Code", "Name", "Price" }), "the layout is untouched");
        cut.Render();
        Assert.That(cut.Markup, Does.Contain("C00"), "and the grid still renders");
    }
}
