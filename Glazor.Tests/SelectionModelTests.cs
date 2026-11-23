using Glazor.Grds.Mdls;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Glazor.Tests;

/// <summary>
/// The set selection models take the caller's set shared or copied, as the caller says with <c>defensiveCopy</c>.
/// </summary>
public class SelectionModelTests
{
    /// <summary>
    /// Shared, the caller's set is the selection and sees the model's changes. Copied, the model works on its own set with
    /// the caller's comparer, and a change the caller makes to its set does not reach it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void DefensiveCopyDecidesWhetherTheCallersSetIsSharedTest(bool defensiveCopy)
    {
        HashSet<string> values = new(StringComparer.OrdinalIgnoreCase) { "a" };
        SetSelectionModelWithSelectAll<string> byValue = new(["a", "b"], values, defensiveCopy);
        HashSet<int> keys = new() { 1 };
        SetSelectionModel<string, int> byKey = new(s => s.Length, keys, defensiveCopy);
        byValue.Select("b", true);
        byKey.Select("bb", true);
        Assert.Multiple(() =>
        {
            Assert.That(values.Contains("b"), Is.EqualTo(!defensiveCopy), "shared, the caller sees the model's selection");
            Assert.That(keys.Contains(2), Is.EqualTo(!defensiveCopy));
        });
        values.Clear();
        keys.Clear();
        Assert.Multiple(() =>
        {
            Assert.That(byValue.IsSelected("A"), Is.EqualTo(defensiveCopy), "a copy keeps the comparer; shared, the caller cleared it");
            Assert.That(byValue.SelectedCount(), Is.EqualTo(defensiveCopy ? 2 : 0));
            Assert.That(byKey.SelectedCount(), Is.EqualTo(defensiveCopy ? 2 : 0));
        });
    }

    /// <summary>
    /// A copy keeps the comparer of the set it copies, whatever kind of set exposes one, or an item selected under that
    /// comparer shows unselected and selecting it again adds a second entry.
    /// </summary>
    [TestCase("SortedSet")]
    [TestCase("ImmutableHashSet")]
    [TestCase("ImmutableSortedSet")]
    [TestCase("FrozenSet")]
    public void DefensiveCopyKeepsTheSetsComparerTest(string kind)
    {
        ISet<string> selected = kind switch
        {
            "SortedSet" => new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "ABC" },
            "ImmutableHashSet" => ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "ABC"),
            "ImmutableSortedSet" => ImmutableSortedSet.Create(StringComparer.OrdinalIgnoreCase, "ABC"),
            _ => new[] { "ABC" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
        };
        SetSelectionModel<string> model = new(selected, defensiveCopy: true);
        bool selectedBefore = model.IsSelected("abc");
        model.Select("abc", true);
        Assert.Multiple(() =>
        {
            Assert.That(selectedBefore, Is.True);
            Assert.That(model.SelectedCount(), Is.EqualTo(1), "the same entry, not a second one");
        });
    }

    /// <summary>
    /// A set that cannot change is copied even when the caller asks to share it, or the first selection would throw inside a
    /// grid event: a click lost, or the circuit ended. Every constructor copies it, and the caller's set is left alone.
    /// </summary>
    [TestCase("ImmutableHashSet")]
    [TestCase("ImmutableSortedSet")]
    [TestCase("FrozenSet")]
    [TestCase("ReadOnlySet")]
    public void ReadOnlySetIsCopiedEvenWhenSharedTest(string kind)
    {
        ISet<int> ReadOnly() => kind switch
        {
            "ImmutableHashSet" => ImmutableHashSet.Create(1),
            "ImmutableSortedSet" => ImmutableSortedSet.Create(1),
            "FrozenSet" => new[] { 1 }.ToFrozenSet(),
            _ => new ReadOnlySet<int>(new HashSet<int> { 1 }),
        };
        ISet<int> callers = ReadOnly();
        SetSelectionModel<int> byValue = new(callers, defensiveCopy: false);
        SetSelectionModel<string, int> byKey = new(s => s.Length, ReadOnly(), defensiveCopy: false);
        SetSelectionModelWithSelectAll<int> withSelectAll = new([1, 2], ReadOnly(), defensiveCopy: false);
        byValue.Select(2, true);
        byKey.Select("bb", true);
        withSelectAll.SelectAll();
        Assert.Multiple(() =>
        {
            Assert.That(byValue.Selected(), Is.EquivalentTo(new[] { 1, 2 }), "by value");
            Assert.That(callers, Is.EquivalentTo(new[] { 1 }), "the caller's set is left alone");
            Assert.That(byKey.Selected(), Is.EquivalentTo(new[] { 1, 2 }), "by key");
            Assert.That(withSelectAll.AllSelected(), Is.True, "with select-all");
        });
    }

    /// <summary>
    /// With nothing to select, because there are no rows or every row is refused, not all is selected, by value and by key, so
    /// MultiSelGrid's select-all box is not checked over no rows.
    /// </summary>
    [Test]
    public void NothingToSelectIsNotAllSelectedTest()
    {
        SetSelectionModelWithSelectAll<string> none = new([]);
        SetSelectionModelWithSelectAll<string> refused = new(["a", "b"], new HashSet<string>(), defensiveCopy: false, _ => false);
        SetSelectionModelWithSelectAll<string, int> noneByKey = new(Array.Empty<string>(), s => s.Length);
        SetSelectionModelWithSelectAll<string, int> noKeys = new(Array.Empty<int>(), s => s.Length);
        SetSelectionModelWithSelectAll<string, int> refusedByKey = new(["a", "bb"], s => s.Length, new HashSet<int>(), defensiveCopy: false, _ => false);
        SetSelectionModelWithSelectAll<string> all = new(["a", "b"]);
        all.SelectAll();
        Assert.Multiple(() =>
        {
            Assert.That(none.AllSelected(), Is.False, "no rows");
            Assert.That(refused.AllSelected(), Is.False, "every row refused");
            Assert.That(noneByKey.AllSelected(), Is.False, "no rows, by key");
            Assert.That(noKeys.AllSelected(), Is.False, "no keys");
            Assert.That(refusedByKey.AllSelected(), Is.False, "every row refused, by key");
            Assert.That(all.AllSelected(), Is.True, "every candidate selected");
        });
    }

    private sealed record Row(string Code);

    /// <summary>
    /// The select-all state follows the comparer of the selected set, which a copy keeps: with every row selected under it, all
    /// are selected, whatever the candidates' own equality says.
    /// </summary>
    [TestCase("HashSet")]
    [TestCase("SortedSet")]
    public void AllSelectedFollowsTheSetsComparerTest(string kind)
    {
        ISet<string> Selected() => kind == "HashSet" ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ABC" }
            : new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "ABC" };
        SetSelectionModelWithSelectAll<string> byValue = new(["abc", "def"], Selected(), defensiveCopy: true);
        byValue.Select("def", true);
        SetSelectionModelWithSelectAll<Row, string> byKey = new(new[] { new Row("abc"), new Row("def") }, r => r.Code, Selected(), defensiveCopy: true);
        byKey.Select(new Row("def"), true);
        Assert.Multiple(() =>
        {
            Assert.That(byValue.IsSelected("abc") && byValue.IsSelected("def"), Is.True);
            Assert.That(byValue.AllSelected(), Is.True, "by value");
            Assert.That(byKey.IsSelected(new Row("abc")) && byKey.IsSelected(new Row("def")), Is.True);
            Assert.That(byKey.AllSelected(), Is.True, "by key");
        });
    }

    private sealed record Role(int Id, bool Locked);

    /// <summary>
    /// Select-all and deselect-all change only the candidates: a row the selection function refuses keeps its state, and so
    /// does a selected value or key that is not a candidate. All is selected once every candidate is.
    /// </summary>
    [Test]
    public void SelectAllChangesOnlyTheCandidatesTest()
    {
        Role admin = new(1, true), editor = new(2, false), viewer = new(3, false), guest = new(4, true), other = new(9, false);
        Role[] roles = [admin, editor, viewer, guest];
        SetSelectionModelWithSelectAll<Role> byValue = new(roles, new HashSet<Role> { admin, editor, other }, defensiveCopy: false, r => !r.Locked);
        SetSelectionModelWithSelectAll<Role, int> byKey = new(roles, r => r.Id, new HashSet<int> { 1, 2, 9 }, defensiveCopy: false, r => !r.Locked);
        byValue.SelectAll();
        byKey.SelectAll();
        Role[] valuesAfterSelectAll = byValue.Selected().ToArray();
        int[] keysAfterSelectAll = byKey.Selected().ToArray();
        bool[] allAfterSelectAll = [byValue.AllSelected(), byKey.AllSelected()];
        byValue.DeselectAll();
        byKey.DeselectAll();
        Assert.Multiple(() =>
        {
            Assert.That(valuesAfterSelectAll, Is.EquivalentTo(new[] { admin, editor, viewer, other }), "select-all, by value");
            Assert.That(keysAfterSelectAll, Is.EquivalentTo(new[] { 1, 2, 3, 9 }), "select-all, by key");
            Assert.That(allAfterSelectAll, Is.EqualTo(new[] { true, true }), "all is selected");
            Assert.That(byValue.Selected(), Is.EquivalentTo(new[] { admin, other }), "deselect-all, by value");
            Assert.That(byKey.Selected(), Is.EquivalentTo(new[] { 1, 9 }), "deselect-all, by key");
            Assert.That(byValue.AllSelected() || byKey.AllSelected(), Is.False);
        });
    }

    /// <summary>
    /// Select refuses to select a locked row and leaves it as it was, selected or not, so a host re-applying a selection keeps a
    /// locked row it holds. Deselecting one is still the caller's.
    /// </summary>
    [Test]
    public void SelectingALockedRowKeepsItsStateTest()
    {
        Role admin = new(1, false), guest = new(2, false);
        HashSet<int> locked = [];
        bool Unlocked(Role r) => !locked.Contains(r.Id);
        SetSelectionModel<Role> byValue = new(new HashSet<Role>(), defensiveCopy: false, Unlocked);
        SetSelectionModel<Role, int> byKey = new(r => r.Id, new HashSet<int>(), defensiveCopy: false, Unlocked);
        ISelectionModel<Role>[] models = [byValue, byKey, new SingleSelectionModel<Role>(Unlocked), new SingleSelectionModel<Role, int>(r => r.Id, Unlocked)];
        foreach (var model in models)
            model.Select(admin, true);
        locked.UnionWith([admin.Id, guest.Id]);
        int[] versions = [byValue.Version, byKey.Version];
        foreach (var model in models)
        {
            model.Select(admin, true);
            model.Select(guest, true);
        }
        Assert.Multiple(() =>
        {
            Assert.That(models.Select(m => m.IsSelected(admin)), Is.All.True, "a locked row that is selected stays selected");
            Assert.That(models.Select(m => m.IsSelected(guest)), Is.All.False, "one that is not stays so");
            Assert.That(new[] { byValue.Version, byKey.Version }, Is.EqualTo(versions), "nothing changed");
        });
        foreach (var model in models)
            model.Select(admin, false);
        Assert.That(models.Select(m => m.IsSelected(admin)), Is.All.False, "deselecting a locked row");
    }

    private sealed record Coded(int Id, string? Code);

    /// <summary>
    /// A single selection may hold a null item or a null key, an optional code for one: the other rows are then not selected,
    /// and the selected one can be deselected.
    /// </summary>
    [Test]
    public void SingleSelectionOfANullKeyOrItemTest()
    {
        Coded noCode = new(1, null), coded = new(2, "A");
        SingleSelectionModel<Coded, string?> byKey = new(c => c.Code);
        byKey.Select(noCode, true);
        SingleSelectionModel<Coded?> byValue = new();
        byValue.Select(null, true);
        Assert.Multiple(() =>
        {
            Assert.That(byKey.IsSelected(coded), Is.False);
            Assert.That(byKey.IsSelected(noCode), Is.True);
            Assert.That(byValue.IsSelected(coded), Is.False);
            Assert.That(byValue.IsSelected(null), Is.True);
        });
        byKey.Select(noCode, false);
        byValue.Select(null, false);
        Assert.That(byKey.SelectedCount() + byValue.SelectedCount(), Is.Zero);
    }

    /// <summary>
    /// A single selection holds one row: selecting another replaces it, and deselecting a row that is not selected leaves the
    /// selection alone.
    /// </summary>
    [Test]
    public void SingleSelectionReplacesTheSelectedRowTest()
    {
        Coded a = new(1, "A"), b = new(2, "B");
        ISelectionModel<Coded>[] models = [new SingleSelectionModel<Coded>(), new SingleSelectionModel<Coded, int>(c => c.Id)];
        foreach (var model in models)
        {
            model.Select(a, true);
            model.Select(b, true);
            model.Select(a, false);
        }
        Assert.Multiple(() =>
        {
            Assert.That(models.Select(m => m.IsSelected(a)), Is.All.False, "replaced");
            Assert.That(models.Select(m => m.IsSelected(b)), Is.All.True, "the new one, kept by deselecting another");
            Assert.That(models.Select(m => m.SelectedCount()), Is.All.EqualTo(1));
        });
    }
}
