using Bunit;
using Glazor.Grds;
using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Microsoft.AspNetCore.Components;

namespace Glazor.Tests;

/// <summary>
/// The extension points a host subclasses, used as a host would: its own model wrapper and column wrapper under a grid with
/// markup columns, rather than only through the library's own subclasses.
/// </summary>
public class ExtensionPointsTests
{
    private sealed record Product(string Code, string Name);

    private sealed class UpperLabel(IColumn<Product> wrapped) : ColumnWrapper<Product>(wrapped)
    {
        public override string GetLabel() => base.GetLabel().ToUpperInvariant();

        protected override ColumnWrapper<Product> NewWrapper(IColumn<Product> wrapped) => new UpperLabel(wrapped);
    }

    private sealed class UpperLabels(IDataGridModel<Product> wrapped) : DataGridModelWrapper<Product>(wrapped)
    {
        public override IColumn<Product> GetColumn(int columnIndex) => new UpperLabel(base.GetColumn(columnIndex));
    }

    /// <summary>
    /// A host's model wrapper passes the markup columns and the filters on to the model it wraps, and the column wrapper it
    /// returns changes the header while the enrichment the wrapped column took still draws the cells.
    /// </summary>
    // Stands for a wrapper that filters its rows on a switch of the page: a change of its own, which the wrapped model never sees
    private sealed class NameFilter(IDataGridModel<Product> wrapped) : DataGridModelWrapper<Product>(wrapped)
    {
        public void Changed() => FireModelChanged(GridModelChangeType.AfterSync);
    }

    /// <summary>
    /// A wrapper raises a change of its own as the grid's model, beside the wrapped model's; a grid that let it go hears neither.
    /// </summary>
    [Test]
    public async Task AWrapperRaisesItsOwnChangesTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new([new("C00", "Name00"), new("C01", "Name01")]) { ItemKeyFn = p => p.Code };
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        NameFilter wrapper = new(model);
        List<GridModelChangeType> changes = [];
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, wrapper).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.OnModelChanged, changes.Add));

        changes.Clear();
        await grid.InvokeAsync(wrapper.Changed);
        Assert.That(changes, Is.EqualTo(new[] { GridModelChangeType.AfterSync }), "the wrapper's own change");
        changes.Clear();
        await grid.InvokeAsync(() => model.SetCurrentRow(1));
        Assert.That(changes, Is.EqualTo(new[] { GridModelChangeType.Other }), "the wrapped model's, once");

        grid.Render(ps => ps.Add(g => g.Model, new DataGridModel<Product>([])));
        changes.Clear();
        await grid.InvokeAsync(wrapper.Changed);
        await grid.InvokeAsync(() => model.SetCurrentRow(0));
        Assert.That(changes, Is.Empty, "the wrapper replaced");
    }

    [Test]
    public async Task HostWrappersWorkUnderAGridTest()
    {
        using BunitContext ctx = NewContext();
        DataGridModel<Product> model = new([new("C00", "Name00"), new("C01", "Name01")]) { ItemKeyFn = p => p.Code };
        model.AddColumn("Name", new Column<Product, string>("Name"), p => p.Name);
        UpperLabels wrapper = new(model);
        RenderFragment<DecoratorContext<Product, string>> enriched = c => b => b.AddContent(0, "ENRICHED:" + c.Value);
        RenderFragment columns = builder =>
        {
            builder.OpenComponent<GridColumn<Product, string>>(0);
            builder.AddAttribute(1, "Label", "Code");
            builder.AddAttribute(2, "Property", (Func<Product, string>)(p => p.Code));
            builder.CloseComponent();
            builder.OpenComponent<EnrichGridColumn<Product, string>>(3);
            builder.AddAttribute(4, "Name", "Name");
            builder.AddAttribute(5, "DisplayTemplate", enriched);
            builder.CloseComponent();
        };
        var grid = ctx.Render<Grigliaria<Product>>(ps => ps.Add(g => g.Model, wrapper).Add(g => g.Spinner, new NoSpinner())
            .Add(g => g.ChildContent, columns));
        Assert.Multiple(() =>
        {
            Assert.That(model.GetColumnIndex("Code"), Is.GreaterThanOrEqualTo(0), "the markup column reached the wrapped model");
            Assert.That(grid.Markup, Does.Contain("CODE").And.Contain("NAME"), "the column wrapper's labels");
            Assert.That(grid.Markup, Does.Contain("ENRICHED:Name00").And.Contain("ENRICHED:Name01"), "the enrichment");
        });

        await grid.InvokeAsync(() => grid.Instance.SetFilter("Name", new DefaultDef { Value = "Name01", Contains = false }));
        Assert.Multiple(() =>
        {
            Assert.That(model.IsFiltered(), Is.True, "the filter reached the wrapped model");
            Assert.That(grid.Markup, Does.Contain("ENRICHED:Name01").And.Not.Contain("ENRICHED:Name00"));
        });
    }
}
