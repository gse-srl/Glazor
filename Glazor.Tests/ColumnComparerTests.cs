using Glazor.Grds;
using Glazor.Grds.Mdls;

namespace Glazor.Tests;

/// <summary>
/// <see cref="PocoColumnComparer.BinarySearch"/> is used by the incremental "search on the sorted column"
/// feature. Its contract is the same as <see cref="Array.BinarySearch(Array, object)"/>: a non-negative index
/// when the value is found, otherwise the bitwise complement of the insertion point, i.e. -(insertionIdx + 1).
/// The insertion point has to be right for both sort directions, which is what these tests pin down.
/// </summary>
public class ColumnComparerTests
{
    [SetUp]
    public void Setup()
    {
    }

    /// <summary>
    /// Ascending: "Ben", "Ciucci", "Otta", "Valentino", "Vallers".
    /// </summary>
    [Test]
    public void BinarySearchAscendingTest()
    {
        var model = NewSortedModel(ascending: true);
        var view = model.GetDataView();
        int colIdx = model.GetColumnIndex(NameColumn);

        Assert.That(view.Select(p => p.Name),
            Is.EqualTo(new[] { "Ben", "Ciucci", "Otta", "Valentino", "Vallers" }));

        // Found
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Ben", true, null), Is.EqualTo(0));
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Otta", true, null), Is.EqualTo(2));
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Vallers", true, null), Is.EqualTo(4));

        // Not found: -(insertionIdx + 1). "Dario" sorts between "Ciucci" (1) and "Otta" (2).
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Dario", true, null), Is.EqualTo(-3));
        // Before everything, and after everything.
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Aaa", true, null), Is.EqualTo(-1));
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Zzz", true, null), Is.EqualTo(-6));
    }

    /// <summary>
    /// Descending: "Vallers", "Valentino", "Otta", "Ciucci", "Ben".
    /// The insertion point must be computed against the descending order too — returning the ascending answer,
    /// or anything derived from <c>high</c> rather than <c>low</c>, silently lands the caller on the wrong row.
    /// </summary>
    [Test]
    public void BinarySearchDescendingTest()
    {
        var model = NewSortedModel(ascending: false);
        var view = model.GetDataView();
        int colIdx = model.GetColumnIndex(NameColumn);

        Assert.That(view.Select(p => p.Name),
            Is.EqualTo(new[] { "Vallers", "Valentino", "Otta", "Ciucci", "Ben" }));

        // Found
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Vallers", false, null), Is.EqualTo(0));
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Otta", false, null), Is.EqualTo(2));
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Ben", false, null), Is.EqualTo(4));

        // Not found: "Dario" is below "Otta" (2) and above "Ciucci" (3) when descending,
        // so it belongs at index 3 => -(3 + 1).
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Dario", false, null), Is.EqualTo(-4));
        // "Zzz" is the largest, so it heads a descending list; "Aaa" is the smallest, so it tails it.
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Zzz", false, null), Is.EqualTo(-1));
        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, "Aaa", false, null), Is.EqualTo(-6));

        // A miss must never come back as 0, which would be indistinguishable from "found at index 0".
        foreach (string missing in new[] { "Aaa", "Dario", "Zzz" })
        {
            Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, missing, false, null),
                Is.LessThan(0), $"a miss on '{missing}' must be negative");
        }
    }

    /// <summary>
    /// When an <see cref="IComparer{Object}"/> is supplied it must be handed the <em>column value</em>, not the
    /// item the value was read from. Passing the item instead compiles fine and stays dormant while
    /// <c>GetComparerByType</c> returns null, which is exactly why it needs a test.
    /// </summary>
    [Test]
    public void BinarySearchWithComparerTest()
    {
        var model = NewSortedModel(ascending: true);
        var view = model.GetDataView();
        int colIdx = model.GetColumnIndex(NameColumn);

        List<object?> received = [];
        IComparer<object> comparer = Comparer<object>.Create((a, b) =>
        {
            received.Add(a);
            return StringComparer.Ordinal.Compare(a as string, b as string);
        });

        // Not the interned literal: BinarySearch short-circuits on ReferenceEquals before it ever consults
        // the comparer, so searching for "Otta" directly would never exercise this path.
        string target = new(['O', 't', 't', 'a']);
        Assert.That(ReferenceEquals(target, "Otta"), Is.False, "the probe value must not be reference-equal");

        Assert.That(PocoColumnComparer.BinarySearch(view, model, colIdx, target, true, comparer), Is.EqualTo(2));

        Assert.That(received, Is.Not.Empty, "the comparer should have been used");
        Assert.That(received, Is.All.InstanceOf<string>(),
            "the comparer must receive the column value, not the item it was read from");
    }

    private const string NameColumn = "Name";

    private static DataGridModel<RProduct> NewSortedModel(bool ascending)
    {
        DataGridModel<RProduct> model = new([
            new RProduct("Valentino", 900),
            new RProduct("Vallers", 1_000),
            new RProduct("Otta", 1_200),
            new RProduct("Ciucci", 1_300),
            new RProduct("Ben", 2_000),
            ]);
        model.AddColumn(NameColumn, new Column<RProduct, string>("nome"), p => p.Name);
        model.SetSortedColumn(NameColumn, ascending);
        model.Sync();
        return model;
    }

    private record RProduct(string Name, decimal Price);
}
