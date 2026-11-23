using Bunit;
using Glazor.Grds;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The default cell tolerates a getter failing on the row being inserted, whose prototype may still have null members, and on
/// no other row: a host's getter that fails elsewhere is a bug, and must not hide behind an insert in progress.
/// </summary>
public class DefaultCellFailureTests
{
    private sealed class Inner
    {
        public string Name { get; set; } = "";
    }

    private sealed class Product
    {
        public string Code { get; set; } = "";
        public Inner? Inner { get; set; }
    }

    private static EditableDataGridModel<Product> ModelOf(params Product[] items)
    {
        EditableDataGridModel<Product> model = new(() => ValueTask.FromResult<IList<Product>>(items.ToList())) { ItemKeyFn = p => p.Code };
        model.GetData().Set(items.ToList());
        model.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        model.AddColumn("Inner", new Column<Product, string>("Inner"), p => p.Inner!.Name);   // throws on a null Inner
        return model;
    }

    [Test]
    public async Task InsertedRowWithNullMembersRendersPlaceholdersTest()
    {
        using BunitContext ctx = NewContext();
        var model = ModelOf(new Product { Code = "C01", Inner = new Inner { Name = "one" } });
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => model.InsertAsync(new Product { Code = "new" }, afterCurrentRow: true).AsTask());
        Assert.DoesNotThrow(() => cut.Render());
        Assert.That(cut.Markup, Does.Contain("one"), "the stored row renders");
        Assert.That(cut.FindAll("td").Any(td => td.TextContent == "..."), Is.True, "the inserted row's failing cell shows a placeholder");
    }

    [Test]
    public void StoredRowWhoseGetterFailsThrowsEvenDuringAnInsertTest()
    {
        using BunitContext ctx = NewContext();
        var model = ModelOf(new Product { Code = "C01", Inner = new Inner { Name = "one" } }, new Product { Code = "C02", Inner = null });
        Assert.Throws<NullReferenceException>(() => ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner())),
            "with no insert pending the host's bug surfaces");

        var ok = ModelOf(new Product { Code = "C01", Inner = new Inner { Name = "one" } });
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, ok).Add(g => g.Spinner, new NoSpinner()));
        cut.InvokeAsync(() => ok.InsertAsync(new Product { Code = "new", Inner = new Inner { Name = "new" } }, afterCurrentRow: true).AsTask()).GetAwaiter().GetResult();
        ok.GetData().Add(new Product { Code = "C02", Inner = null });   // a stored row that fails, while the insert is pending
        Assert.Throws<NullReferenceException>(() => cut.Render(), "the insert in progress does not hide it");
    }

    /// <summary>
    /// MultiSelGrid hands its cells a wrapper of the model, which must not hide the insert in progress: the ordered editable
    /// model is only accepted by MultiSelGrid.
    /// </summary>
    [Test]
    public async Task InsertedRowWithNullMembersRendersPlaceholdersInMultiSelGridTest()
    {
        using BunitContext ctx = NewContext();
        Product stored = new() { Code = "C01", Inner = new Inner { Name = "one" } };
        EditableGridModel<Product> ordered = new(() => ValueTask.FromResult<IList<Product>>([stored])) { ItemKeyFn = p => p.Code };
        ordered.GetData().Set([stored]);
        ordered.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        ordered.AddColumn("Inner", new Column<Product, string>("Inner"), p => p.Inner!.Name);
        foreach (IEditableGridModel<Product> model in new IEditableGridModel<Product>[] { ordered, ModelOf(stored) })
        {
            var cut = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, model)
                .Add(g => g.SelectionModel, new SetSelectionModel<Product>()).Add(g => g.Spinner, new NoSpinner()));
            await cut.InvokeAsync(() => model.InsertAsync(new Product { Code = "new" }, afterCurrentRow: true).AsTask());
            Assert.DoesNotThrow(() => cut.Render(), model.GetType().Name);
            Assert.That(cut.FindAll("td").Any(td => td.TextContent == "..."), Is.True, "the inserted row's failing cell shows a placeholder");
        }
    }

    /// <summary>The row heading tells an insert from an edit in MultiSelGrid too, whose wrapper hides the editable model.</summary>
    [Test]
    public async Task RowHeadingShowsTheInsertInMultiSelGridTest()
    {
        using BunitContext ctx = NewContext();
        Product stored = new() { Code = "C01", Inner = new Inner { Name = "one" } };
        var plain = ModelOf(stored);
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, plain).Add(g => g.Spinner, new NoSpinner()));
        await grid.InvokeAsync(() => plain.InsertAsync(new Product { Code = "new", Inner = new Inner() }, afterCurrentRow: true).AsTask());
        grid.Render();

        var wrapped = ModelOf(stored);
        var multi = ctx.Render<MultiSelGrid<Product>>(ps => ps.Add(g => g.Model, wrapped)
            .Add(g => g.SelectionModel, new SetSelectionModel<Product>()).Add(g => g.Spinner, new NoSpinner()));
        await multi.InvokeAsync(() => wrapped.InsertAsync(new Product { Code = "new", Inner = new Inner() }, afterCurrentRow: true).AsTask());
        multi.Render();
        Assert.Multiple(() =>
        {
            Assert.That(grid.FindAll(".icon.inserting"), Has.Count.EqualTo(1), "Grigliaria");
            Assert.That(multi.FindAll(".icon.inserting"), Has.Count.EqualTo(1), "MultiSelGrid");
            Assert.That(multi.FindAll(".icon.editing"), Is.Empty, "MultiSelGrid");
        });
    }

    /// <summary>A host's own wrapper that adds inserting to a plain model: the prototype is shown after the stored rows.</summary>
    private sealed class HostEditableWrapper(DataGridModel<Product> inner) : DataGridModelWrapper<Product>(inner), IEditableGridModel<Product>
    {
        private Product? _proto;

        public override int GetRowCount() => base.GetRowCount() + (_proto != null ? 1 : 0);
        public override Product GetItem(int rowIndex) => _proto != null && rowIndex == base.GetRowCount() ? _proto : base.GetItem(rowIndex);
        public override int GetCurrentRow() => _proto != null ? base.GetRowCount() : base.GetCurrentRow();
        public ValueTask<Product> InsertAsync(Product newItemProto, bool afterCurrentRow) { _proto = newItemProto; return ValueTask.FromResult(newItemProto); }
        public bool IsInsertPending() => _proto != null;
        public bool IsEditPending() => false;
        public bool IsCurrentRowEditable() => true;
        public ValueTask<Product> EditAsync() => throw new NotSupportedException();
        public ValueTask PostAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask CancelAsync(CancellationToken ct = default) { _proto = null; return ValueTask.CompletedTask; }
    }

    /// <summary>
    /// A host's own wrapper may be the editable model while the model it wraps is not: the cell finds the insert in progress on
    /// the wrapper itself.
    /// </summary>
    [Test]
    public async Task InsertedRowOfAHostEditableWrapperRendersPlaceholdersTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> plain = new([new Product { Code = "C01", Inner = new Inner { Name = "one" } }]) { ItemKeyFn = p => p.Code };
        plain.AddColumn("Code", new Column<Product, string>("Code"), p => p.Code);
        plain.AddColumn("Inner", new Column<Product, string>("Inner"), p => p.Inner!.Name);   // throws on a null Inner
        HostEditableWrapper model = new(plain);
        var cut = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, model).Add(g => g.Spinner, new NoSpinner()));
        await cut.InvokeAsync(() => model.InsertAsync(new Product { Code = "new" }, afterCurrentRow: true).AsTask());
        Assert.DoesNotThrow(() => cut.Render());
        Assert.That(cut.FindAll("td").Any(td => td.TextContent == "..."), Is.True, "the inserted row's failing cell shows a placeholder");
    }
}
