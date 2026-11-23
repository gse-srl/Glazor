using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;

namespace Glazor.Tests;

/// <summary>
/// The editable in-memory model through its public edit API: insert, post, cancel. Until 1.0.9 nothing exercised
/// EditSupport at all.
/// </summary>
public class EditableModelTests
{
    private sealed record Product(int Id, string Name, decimal Price);

    private static EditableDataGridModel<Product> NewModel(List<Product> store)
    {
        EditableDataGridModel<Product>? model = null;
        // The host's "reload everything" callback: what the storage holds now, which here is the model's own data.
        model = new(() => ValueTask.FromResult<IList<Product>>([.. model!.GetData()]))
        {
            ItemKeyFn = p => p.Id,
        };
        model.GetData().Set(store);
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        model.AddColumn("Price", new Column<Product, decimal>("Price"), p => p.Price);
        return model;
    }

    [Test]
    public async Task InsertAfterLastRowThenPostTest()
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000), new(3, "Ben", 2_000)]);
        model.SetCurrentRow(2);
        Product proto = await model.InsertAsync(new Product(4, "Otta", 1_200), afterCurrentRow: true);
        Assert.That(model.IsInsertPending(), Is.True);
        Assert.That(model.GetRowCount(), Is.EqualTo(4), "the pending row counts");
        Assert.That(model.GetItem(3), Is.SameAs(proto));
        await model.PostAsync();
        Assert.That(model.IsInsertPending(), Is.False);
        Assert.That(model.GetRowCount(), Is.EqualTo(4));
        Assert.That(model.GetCurrentRow(), Is.EqualTo(3));
        Assert.That(model.GetCurrentItem()!.Name, Is.EqualTo("Otta"));
    }

    /// <summary>
    /// The filter hides the row being posted, so the view does not grow: the model must not throw positioning
    /// the current row past the view's end.
    /// </summary>
    [Test]
    public async Task PostingAnInsertExcludedByTheFilterDoesNotThrowTest()
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000), new(3, "Ben", 2_000)]);
        model.ApplyFilters([new("Name", new DefaultDef() { Value = "Val", Contains = true })]);
        Assert.That(model.GetRowCount(), Is.EqualTo(2));
        model.SetCurrentRow(1);
        await model.InsertAsync(new Product(4, "Otta", 1_200), afterCurrentRow: true);
        await model.PostAsync();
        Assert.That(model.IsInsertPending(), Is.False);
        Assert.That(model.GetRowCount(), Is.EqualTo(2), "the new row is filtered out of the view");
        Assert.That(model.GetCurrentRow(), Is.LessThan(model.GetRowCount()));
        model.ApplyFilters([]);
        Assert.That(model.GetRowCount(), Is.EqualTo(4), "but it is in the data");
    }

    /// <summary>
    /// A host's reload that ends in ToArray(): the model's rows must still take the insert a post makes.
    /// </summary>
    [Test]
    public async Task InsertIntoRowsGivenAsAnArrayTest()
    {
        EditableDataGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToArray()))
        {
            ItemKeyFn = p => p.Id,
        };
        model.GetData().Set(new Product[] { new(1, "Valentino", 900), new(2, "Vallers", 1_000) });
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        model.SetCurrentRow(1);
        await model.InsertAsync(new Product(3, "Ben", 2_000), afterCurrentRow: true);
        await model.PostAsync();   // into the rows Set was given
        await model.InsertAsync(new Product(4, "Otta", 1_200), afterCurrentRow: true);
        await model.PostAsync();   // into the rows the reload returned
        Assert.That(model.GetDataView().Select(p => p.Name), Is.EqualTo(new[] { "Valentino", "Vallers", "Ben", "Otta" }));
    }

    [Test]
    public async Task CancelInsertTest()
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000)]);
        model.SetCurrentRow(0);
        await model.InsertAsync(new Product(3, "Ben", 2_000), afterCurrentRow: false);
        Assert.That(model.GetRowCount(), Is.EqualTo(3));
        await model.CancelAsync();
        Assert.That(model.IsInsertPending(), Is.False);
        Assert.That(model.GetRowCount(), Is.EqualTo(2));
    }

    /// <summary>A cancel with nothing pending does nothing: it raises no change.</summary>
    [Test]
    public async Task CancelWithNothingPendingRaisesNoChangeTest()
    {
        var model = NewModel([new(1, "Alpha", 900), new(2, "Beta", 1_000)]);
        model.SetCurrentRow(0);
        int changes = 0;
        model.OnModelChanged += _ => changes++;
        await model.CancelAsync();
        Assert.That(changes, Is.Zero);
    }

    /// <summary>
    /// A negative start of the forward contains-search is clamped like the backward loop clamps its end.
    /// </summary>
    [Test]
    public async Task ForwardSearchWithANegativeStartTest()
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000), new(3, "Ben", 2_000)]);
        Assert.That(model.CanDoContainsSearch(), Is.True);
        Assert.That(await model.ContainsSearchAsync("Name", "Ben", forward: true, startFrom: -1), Is.EqualTo(2), "a negative start means from the beginning");
    }

    /// <summary>
    /// A search finds a row to move to, and a frozen row cannot be left: while an insert or an edit is pending both searches
    /// throw, as filtering and sorting do.
    /// </summary>
    [Test]
    public async Task SearchingWhileFrozenThrowsTest()
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000), new(3, "Ben", 2_000)]);
        model.SetSortedColumn("Name", true);
        model.SetCurrentRow(0);
        await model.InsertAsync(new Product(4, "Otta", 1_200), afterCurrentRow: false);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await model.ContainsSearchAsync("Name", "Ben", forward: true, startFrom: 0));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await model.SearchOnSortedColumnAsync("Ben"));
        await model.CancelAsync();
        await model.EditAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await model.ContainsSearchAsync("Name", "Ben", forward: true, startFrom: 0));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await model.SearchOnSortedColumnAsync("Ben"));
        await model.CancelAsync();
        Assert.That(await model.SearchOnSortedColumnAsync("Ben"), Is.EqualTo(0), "searches again once the row is free");
    }

    /// <summary>
    /// A posted insert must land where the pending row was shown, not at the end.
    /// </summary>
    [Test]
    public async Task InsertBeforeAndBetweenRowsThenPostTest()
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000), new(3, "Ben", 2_000)]);
        model.SetCurrentRow(0);
        await model.InsertAsync(new Product(4, "First", 1), afterCurrentRow: false);
        Assert.That(model.GetItem(0).Name, Is.EqualTo("First"), "pending, shown first");
        await model.PostAsync();
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Name), Is.EqualTo(new[] { "First", "Valentino", "Vallers", "Ben" }));
        Assert.That(model.GetCurrentRow(), Is.EqualTo(0));

        model.SetCurrentRow(1);
        await model.InsertAsync(new Product(5, "Second", 2), afterCurrentRow: true);
        await model.PostAsync();
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Name), Is.EqualTo(new[] { "First", "Valentino", "Second", "Vallers", "Ben" }));
        Assert.That(model.GetCurrentRow(), Is.EqualTo(2));
    }

    /// <summary>
    /// With rows but no current row, an insert before the current row goes to the top, as one after it does.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task InsertWithNoCurrentRowGoesToTheTopTest(bool afterCurrentRow)
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000)]);
        model.SetCurrentRow(-1);
        await model.InsertAsync(new Product(3, "First", 1), afterCurrentRow);
        Assert.That(model.GetItem(0).Name, Is.EqualTo("First"), "pending, shown first");
        await model.PostAsync();
        Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Name), Is.EqualTo(new[] { "First", "Valentino", "Vallers" }));
    }
    private static EditableGridModel<Product> NewOrderedModel(List<Product> store)
    {
        EditableGridModel<Product>? model = null;
        model = new(() => ValueTask.FromResult<IList<Product>>(model!.GetData().ToList())) { ItemKeyFn = p => p.Id };
        model.GetData().Set(store);
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        return model;
    }

    private static string[] Names(IGridModel<Product> model) => [.. Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Name)];

    /// <summary>
    /// The ordered editable model has the same edit lifecycle: a posted insert stays where it was inserted, in the order the
    /// user arranged, and a cancelled one leaves the rows as they were.
    /// </summary>
    [Test]
    public async Task OrderedModelInsertsAndCancelsTest()
    {
        var model = NewOrderedModel([new(1, "A", 1), new(2, "B", 2), new(3, "C", 3)]);
        model.SetCurrentRow(0);
        await model.InsertAsync(new Product(4, "NEW", 4), afterCurrentRow: true);
        Assert.That(model.IsCurrentRowFrozen(), Is.True, "frozen while the insert is pending");
        await model.PostAsync();
        Assert.Multiple(() =>
        {
            Assert.That(model.IsInsertPending(), Is.False);
            Assert.That(Names(model), Is.EqualTo(new[] { "A", "NEW", "B", "C" }));
            Assert.That(model.GetCurrentItem()!.Name, Is.EqualTo("NEW"));
        });

        model.SetCurrentRow(3);
        await model.InsertAsync(new Product(5, "GONE", 5), afterCurrentRow: false);
        await model.CancelAsync();
        Assert.Multiple(() =>
        {
            Assert.That(model.IsInsertPending(), Is.False);
            Assert.That(Names(model), Is.EqualTo(new[] { "A", "NEW", "B", "C" }));
        });
    }

    /// <summary>
    /// An edit on the ordered model freezes the current row until it is posted or cancelled, and the row keeps its place.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task OrderedModelEditEndsWithPostOrCancelTest(bool post)
    {
        var model = NewOrderedModel([new(1, "A", 1), new(2, "B", 2)]);
        model.SetCurrentRow(1);
        Product edited = await model.EditAsync();
        Assert.Multiple(() =>
        {
            Assert.That(edited.Name, Is.EqualTo("B"));
            Assert.That(model.IsEditPending(), Is.True);
            Assert.That(model.IsCurrentRowFrozen(), Is.True);
        });
        if (post)
            await model.PostAsync();
        else
            await model.CancelAsync();
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False);
            Assert.That(model.IsCurrentRowFrozen(), Is.False);
            Assert.That(Names(model), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A pending insert moves the rows after it down by one, once: read by value, they must be the rows read by item.
    /// </summary>
    [Test]
    public async Task ValuesDuringAPendingInsertAreTheRowsValuesTest()
    {
        List<Product> store = [new(1, "A", 1), new(2, "B", 2), new(3, "C", 3), new(4, "D", 4)];
        EditableGridModel<Product>? ordered = null;
        ordered = new(() => ValueTask.FromResult<IList<Product>>(ordered!.GetData().ToList())) { ItemKeyFn = p => p.Id };
        ordered.GetData().Set(store);
        ordered.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        foreach (IEditableGridModel<Product> model in new IEditableGridModel<Product>[] { NewModel(store), ordered })
        {
            model.SetCurrentRow(0);
            await model.InsertAsync(new Product(5, "NEW", 5), afterCurrentRow: true);
            IEnumerable<int> rows = Enumerable.Range(0, model.GetRowCount());
            Assert.That(rows.Select(i => model.GetValueAt(i, 0)), Is.EqualTo(rows.Select(i => model.GetItem(i).Name)), model.GetType().Name);
        }
    }
    /// <summary>
    /// A pending insert is shown but not stored: the view holds the stored rows and leaves it out, while the grid's indexes,
    /// which count it, still fetch without error.
    /// </summary>
    [Test]
    public async Task PendingInsertIsNotInTheViewButItsIndexesFetchTest()
    {
        var model = NewModel([new(1, "A", 1), new(2, "B", 2), new(3, "C", 3)]);
        model.SetCurrentRow(2);
        Product proto = await model.InsertAsync(new Product(4, "NEW", 4), afterCurrentRow: true);
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRowCount(), Is.EqualTo(4));
            Assert.That(model.GetDataView(), Has.Count.EqualTo(3).And.No.Member(proto), "the stored rows only");
            Assert.That(model.GetItem(model.GetCurrentRow()), Is.SameAs(proto), "read by position through the model");
            Assert.That(() => model.FetchRowsAsync(0, model.GetRowCount()).AsTask(), Throws.Nothing);
        });
    }

    private sealed class Row
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public Row Copy() => new() { Code = Code, Name = Name };
    }

    /// <summary>
    /// A cancelled edit that changed the key goes back to the row it started on, found by the key it had then, whether the
    /// model reloads everything or one row: the typed key names another row, or none.
    /// </summary>
    [TestCase(false, "C05")]
    [TestCase(false, "C99")]
    [TestCase(true, "C05")]
    [TestCase(true, "C99")]
    public async Task CancellingAnEditOfTheKeyGoesBackToTheSameRowTest(bool perRowReload, string typedKey)
    {
        List<Row> store = Enumerable.Range(0, 8).Select(i => new Row { Code = $"C{i:D2}", Name = $"N{i}" }).ToList();
        Func<ValueTask<IList<Row>>> reloadAll = () => ValueTask.FromResult<IList<Row>>(store.Select(r => r.Copy()).ToList());
        // The host's single-row reload reads the row the item names
        Func<Row, ValueTask<Row?>> reloadRow = item => ValueTask.FromResult(store.FirstOrDefault(r => r.Code == item.Code)?.Copy());
        EditableDataGridModel<Row> model = perRowReload
            ? new EditableDataGridModel<Row>(reloadAll, reloadRow) { ItemKeyFn = r => r.Code }
            : new EditableDataGridModel<Row>(reloadAll) { ItemKeyFn = r => r.Code };
        model.GetData().Set(store.Select(r => r.Copy()).ToList());
        model.AddColumn("Code", new Column<Row, string>("Code"), r => r.Code);
        model.SetCurrentRow(2);
        Row edited = await model.EditAsync();
        edited.Code = typedKey;
        edited.Name = "changed";
        await model.CancelAsync();
        Assert.Multiple(() =>
        {
            Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Code), Is.EqualTo(store.Select(r => r.Code)), "every row, once");
            Assert.That(model.GetCurrentItem()?.Code, Is.EqualTo("C02"));
            Assert.That(model.GetCurrentItem()?.Name, Is.EqualTo("N2"));
        });
    }

    private sealed class LockingModel : EditableDataGridModel<Product>
    {
        public LockingModel() : base(() => ValueTask.FromResult<IList<Product>>([])) { }

        public override bool IsCurrentRowEditable() => GetCurrentItem()?.Id != 2;
    }

    /// <summary>
    /// A row the model locks refuses an edit from host code as from the grid: <c>EditAsync</c> throws and nothing is left
    /// pending, while the rows not locked still edit.
    /// </summary>
    [Test]
    public async Task EditOfALockedRowIsRefusedTest()
    {
        LockingModel model = new() { ItemKeyFn = p => p.Id };
        model.GetData().Set([new(1, "Alpha", 900), new(2, "Beta", 1_000)]);
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        model.SetCurrentRow(1);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await model.EditAsync());
        Assert.That(model.IsEditPending(), Is.False, "nothing pending");
        model.SetCurrentRow(0);
        Assert.That((await model.EditAsync()).Name, Is.EqualTo("Alpha"));
        Assert.That(model.IsEditPending(), Is.True);
    }

    /// <summary>
    /// A post or a cancel whose reload fails still ends the edit or the insert: the exception reaches the caller, nothing is
    /// left pending, and the next edit starts as usual. No state of unknown validity is kept to retry with.
    /// </summary>
    [TestCase("post an edit")]
    [TestCase("cancel an edit")]
    [TestCase("post an insert")]
    [TestCase("cancel an insert")]
    public async Task AFailedReloadStillEndsTheEditTest(string action)
    {
        bool fail = false;
        EditableDataGridModel<Product>? model = null;
        model = new(
            () => fail ? throw new InvalidOperationException("storage down") : ValueTask.FromResult<IList<Product>>([.. model!.GetData()]),
            item => fail ? throw new InvalidOperationException("storage down") : ValueTask.FromResult<Product?>(item))
        {
            ItemKeyFn = p => p.Id,
        };
        model.GetData().Set([new(1, "Alpha", 900), new(2, "Beta", 1_000)]);
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        model.SetCurrentRow(0);
        if (action.EndsWith("an insert"))
            await model.InsertAsync(new Product(3, "Gamma", 1_200), afterCurrentRow: true);
        else
            await model.EditAsync();
        fail = true;
        if (action.StartsWith("post"))
            Assert.ThrowsAsync<InvalidOperationException>(async () => await model.PostAsync());
        else if (action == "cancel an edit")
            Assert.ThrowsAsync<InvalidOperationException>(async () => await model.CancelAsync());
        else
            await model.CancelAsync();   // dropping an insert reloads nothing
        fail = false;
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False, "no edit pending");
            Assert.That(model.IsInsertPending(), Is.False, "no insert pending");
        });
        Assert.DoesNotThrowAsync(async () => await model.EditAsync(), "the next edit starts");
    }

    /// <summary>
    /// A cancelled token stops a post or a cancel before the host's reload, which takes no token, as a reload that fails does:
    /// the edit ends.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ACancelledTokenStopsTheReloadTest(bool post)
    {
        int reloads = 0;
        EditableDataGridModel<Product>? model = null;
        model = new(() => { reloads++; return ValueTask.FromResult<IList<Product>>([.. model!.GetData()]); })
        {
            ItemKeyFn = p => p.Id,
        };
        model.GetData().Set([new(1, "Alpha", 900), new(2, "Beta", 1_000)]);
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        model.SetCurrentRow(0);
        await model.EditAsync();
        var cancelled = new CancellationToken(true);
        if (post)
            Assert.That(async () => await model.PostAsync(cancelled), Throws.InstanceOf<OperationCanceledException>());
        else
            Assert.That(async () => await model.CancelAsync(cancelled), Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(reloads, Is.Zero, "the host's reload did not run");
            Assert.That(model.IsEditPending(), Is.False, "no edit pending");
        });
    }

    /// <summary>
    /// While an edit or insert is pending the model is frozen: moving to another row, filtering and sorting throw, and the
    /// current row itself may still be set.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task FrozenModelThrowsTest(bool insert)
    {
        var model = NewModel([new(1, "Valentino", 900), new(2, "Vallers", 1_000), new(3, "Ben", 2_000)]);
        model.SetCurrentRow(0);
        if (insert)
            await model.InsertAsync(new Product(4, "Otta", 1_200), afterCurrentRow: true);
        else
            await model.EditAsync();
        int current = model.GetCurrentRow();
        Assert.Multiple(() =>
        {
            Assert.That(model.IsCurrentRowFrozen(), Is.True);
            Assert.That(() => model.SetCurrentRow(current == 0 ? 2 : 0), Throws.InvalidOperationException, "moving");
            Assert.That(() => model.ApplyFilters([new("Name", new DefaultDef() { Value = "Val" })]), Throws.InvalidOperationException, "filtering");
            Assert.That(() => model.SetSortedColumn("Name", true), Throws.InvalidOperationException, "sorting");
            Assert.That(() => model.SetCurrentRow(current), Throws.Nothing, "staying");
        });
        await model.CancelAsync();
        Assert.That(() => model.SetSortedColumn("Name", true), Throws.Nothing, "thawed");
    }

    /// <summary>
    /// Removing a sorted or filtered column drops its sort or filter, which would move the rows around the pending one: while
    /// frozen it throws and leaves the model as it was.
    /// </summary>
    [Test]
    public async Task RemovingASortedOrFilteredColumnWhileFrozenThrowsTest()
    {
        var model = NewModel([new(1, "a", 900), new(2, "b", 1_000), new(3, "c", 2_000)]);
        model.SetSortedColumn("Name", false);
        model.ApplyFilters([new("Price", new NumberDef<decimal> { From = 950 })]);
        model.SetCurrentRow(0);
        await model.InsertAsync(new Product(4, "new", 1_200), afterCurrentRow: true);
        Assert.Multiple(() =>
        {
            Assert.That(() => model.RemoveColumn("Name"), Throws.InvalidOperationException, "sorted");
            Assert.That(() => model.RemoveColumn("Price"), Throws.InvalidOperationException, "filtered");
            Assert.That(model.GetSortedColumn(), Is.EqualTo("Name"));
            Assert.That(model.GetFilters().Keys, Is.EquivalentTo(new[] { "Price" }));
            Assert.That(Enumerable.Range(0, model.GetRowCount()).Select(i => model.GetItem(i).Name), Is.EqualTo(new[] { "c", "new", "b" }));
        });
        await model.CancelAsync();
        Assert.That(model.RemoveColumn("Name"), Is.Not.Null, "thawed");
    }
}
