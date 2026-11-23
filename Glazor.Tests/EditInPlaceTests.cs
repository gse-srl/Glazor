using Bunit;
using Glazor.Grds;
using Glazor.Grds.Decs;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace Glazor.Tests;

[TestFixture]
public class EditInPlaceTests
{
    public enum Color { Red, Green }

    public sealed class Item
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = "";
        [System.ComponentModel.DataAnnotations.Range(0, 100)] public int Qty { get; set; }
        public Color Color { get; set; }
        public bool Done { get; set; }
        public int Weight { get; set; }   // no column shows it
        public int Code { get; init; }
        public DateTime When { get; set; }
        public DateOnly Day { get; set; }
        public Color? Shade { get; set; }

        public Item Copy() => (Item)MemberwiseClone();
    }

    private List<Item> _store = null!;

    [SetUp]
    public void Setup() => _store = Enumerable.Range(1, 4).Select(i => new Item { Id = i, Name = $"item{i}", Qty = i }).ToList();

    private EditableDataGridModel<Item> NewModel()
    {
        EditableDataGridModel<Item> model = new(() => ValueTask.FromResult<IList<Item>>(_store.Select(i => i.Copy()).ToList()))
        {
            ItemKeyFn = i => i.Id,
        };
        model.GetData().Set(_store.Select(i => i.Copy()).ToList());
        return model;
    }

    private static void Column<T>(RenderTreeBuilder b, string name, Func<Item, T>? property = null,
        Expression<Func<Item, T>>? edit = null, bool editable = true)
    {
        b.OpenComponent<GridColumn<Item, T>>(0);
        b.AddAttribute(1, "Name", name);
        b.AddAttribute(2, "Label", name);
        if (property != null)
            b.AddAttribute(3, "Property", property);
        if (edit != null)
            b.AddAttribute(4, "EditProperty", edit);
        if (!editable)
            b.AddAttribute(5, "Editable", false);
        b.CloseComponent();
    }

    private static readonly RenderFragment Columns = b =>
    {
        Column(b, "Id", property: i => i.Id);
        Column(b, "Name", edit: i => i.Name);
        Column(b, "Quantity", edit: i => i.Qty);   // named apart from its property
        Column(b, "Color", edit: i => i.Color);
        Column(b, "Done", edit: i => i.Done);
    };

    private static async Task PressAsync(IRenderedComponent<Grigliaria<Item>> cut, string key, bool ctrl = false)
    {
        KeyAction action = cut.FindComponents<KeyAction>()
            .Single(k => k.Instance.Key == key && k.Instance.CtrlKey == ctrl && k.Instance.BindAncestorWithTag == "div" && k.Instance.Enabled).Instance;
        await cut.InvokeAsync(() => action.ProcessAsync(null, key, null));
    }

    private IRenderedComponent<Grigliaria<Item>> Render(BunitContext ctx, EditableDataGridModel<Item> model,
        Func<GridPost<Item>, Task>? onPost = null, EditMode mode = EditMode.Manual)
    {
        var cut = ctx.Render<Grigliaria<Item>>(ps =>
        {
            ps.Add(g => g.Model, model).Add(g => g.EditMode, mode).Add(g => g.ChildContent, Columns)
                .Add(g => g.NewItem, current => new Item { Id = 99, Name = "new" });
            if (onPost != null)
                ps.Add(g => g.OnPost, onPost);
        });
        cut.InvokeAsync(() => model.SetCurrentRow(1));
        return cut;
    }

    // Saves as a host does, then lets the model read the row back
    private Func<GridPost<Item>, Task> Save(EditableDataGridModel<Item> model) => async post =>
    {
        int at = _store.FindIndex(i => i.Id == post.Item.Id);   // in place: a row that moves stops a move to another
        if (at < 0)
            _store.Add(post.Item.Copy());
        else
            _store[at] = post.Item.Copy();
        await model.PostAsync();
    };

    [Test]
    public async Task F2EditsTheRowInPlaceAndEnterSavesItTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model));
        await PressAsync(cut, "F2");
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.True);
            Assert.That(cut.FindAll("td.editing"), Has.Count.EqualTo(4), "Name, Quantity, Color and Done; Id is only read");
            Assert.That(cut.FindAll("td.editing select"), Has.Count.EqualTo(1), "the enum");
            Assert.That(cut.FindAll("td.editing input[type=checkbox]"), Has.Count.EqualTo(1), "the bool");
            Assert.That(cut.FindAll("button.btnRowPost"), Has.Count.EqualTo(1));
        });

        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "bolt" }));
        Assert.That(cut.FindAll("td.editing input")[0].GetAttribute("value"), Is.EqualTo("bolt"), "the cell rendered again keeps what was typed");
        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[1].ChangeAsync(new ChangeEventArgs { Value = "7" }));
        await cut.InvokeAsync(() => cut.Instance.PostAsync());   // Enter; the grid's dialogs bind it too
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False);
            Assert.That(_store.Single(i => i.Id == 2).Name, Is.EqualTo("bolt"));
            Assert.That(_store.Single(i => i.Id == 2).Qty, Is.EqualTo(7));
            Assert.That(cut.FindAll("td.editing"), Is.Empty);
        });
    }

    [Test]
    public async Task TheEditorOfTheCurrentColumnTakesTheFocusTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model));
        await cut.InvokeAsync(() => cut.Instance.SetCurrentCell(1, model.GetColumnIndex("Quantity")));
        await PressAsync(cut, "F2");
        string focused = ctx.JSInterop.Invocations.Last(i => i.Identifier.EndsWith("focus")).Arguments.OfType<ElementReference>().Single().Id;
        Assert.That(cut.FindComponent<InputNumber<int>>().Instance.Element?.Id, Is.EqualTo(focused),
            "Quantity, where the cell cursor is, not Name, the first editor");
    }

    /// <summary>
    /// Under a column window the edited row has a stop on each edge it can move: Tab off the last column shown brings the next one
    /// and focuses it, Shift+Tab off the first one brings the one before; reached from elsewhere a stop only passes the focus on.
    /// </summary>
    [Test]
    public async Task TabOffAnEdgeOfTheColumnWindowShowsTheNextColumnTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, Columns).Add(g => g.HorzResizer, GridHorzResizers.Fixed(30)));
        await cut.InvokeAsync(() => cut.Instance.SetCurrentCell(1, model.GetColumnIndex("Name")));
        await PressAsync(cut, "F2");
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetShownColumns(), Is.EqualTo(new[] { "Id", "Name" }));
            Assert.That(cut.Find("tr.edited td.data").GetAttribute("tabindex"), Is.EqualTo("0"), "Id, read-only, is a Tab stop too");
            Assert.That(cut.FindAll("span.edit-edge"), Has.Count.EqualTo(1), "on the right only: nothing is hidden on the left");
        });

        await cut.FindAll("td.editing")[0].FocusInAsync(new());   // the Name field
        await cut.Find("span.edit-edge").FocusAsync(new());   // Tab from it
        string focused = ctx.JSInterop.Invocations.Last(i => i.Identifier.EndsWith("focus")).Arguments.OfType<ElementReference>().Single().Id;
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetShownColumns(), Does.Contain("Quantity"));
            Assert.That(cut.Instance.GetCurrentCol(), Is.EqualTo("Quantity"));
            Assert.That(cut.FindComponent<InputNumber<int>>().Instance.Element?.Id, Is.EqualTo(focused), "Quantity's field has the focus");
        });

        await cut.FindAll("td.editing").First(td => td.QuerySelector("input[type=number]") != null).FocusInAsync(new());   // Quantity
        await cut.FindAll("span.edit-edge").First().FocusAsync(new());   // the left stop, reached with Tab from elsewhere
        Assert.That(cut.Instance.GetCurrentCol(), Is.EqualTo("Name"), "passed on to the first column shown, no scroll");

        await cut.FindAll("td.editing")[0].FocusInAsync(new());   // Name
        await cut.FindAll("span.edit-edge").First().FocusAsync(new());   // Shift+Tab from it
        Assert.That(cut.Instance.GetShownColumns(), Does.Contain("Id").And.Contain("Name"), "back to the start");
    }

    /// <summary>
    /// An insert starts from the first editable column wherever the cell cursor was, the column window scrolled back to it.
    /// </summary>
    [Test]
    public async Task AnInsertFocusesTheFirstEditableColumnTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, Columns).Add(g => g.HorzResizer, GridHorzResizers.Fixed(30))
            .Add(g => g.NewItem, current => new Item { Id = 99, Name = "new" }));
        await cut.InvokeAsync(() => cut.Instance.SetCurrentCell(1, model.GetColumnIndex("Done")));
        Assert.That(cut.Instance.GetShownColumns(), Does.Not.Contain("Name"), "scrolled past it");

        await PressAsync(cut, "Insert", ctrl: true);
        string focused = ctx.JSInterop.Invocations.Last(i => i.Identifier.EndsWith("focus")).Arguments.OfType<ElementReference>().Single().Id;
        Assert.Multiple(() =>
        {
            Assert.That(model.IsInsertPending(), Is.True);
            Assert.That(cut.Instance.GetShownColumns(), Does.Contain("Name"));
            Assert.That(cut.Instance.GetCurrentCol(), Is.EqualTo("Name"), "Id, before it, is only read");
            Assert.That(cut.FindComponent<InputText>().Instance.Element?.Id, Is.EqualTo(focused));
        });
    }

    [Test]
    public async Task AnInvalidRowStaysInEditWithItsMessagesAndEscCancelsItTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model));
        await PressAsync(cut, "F2");
        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "" }));
        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[1].ChangeAsync(new ChangeEventArgs { Value = "500" }));
        await cut.InvokeAsync(() => cut.Instance.PostAsync());
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.True, "not posted");
            Assert.That(cut.FindAll("td.editing")[0].QuerySelectorAll("i.editor-message"), Has.Length.EqualTo(1), "Name is required");
            Assert.That(cut.FindAll("td.editing")[1].QuerySelectorAll("i.editor-message"), Has.Length.EqualTo(1),
                "out of range, on the Quantity column though its property is Qty");
            Assert.That(_store.Single(i => i.Id == 2).Name, Is.EqualTo("item2"));
        });

        await cut.InvokeAsync(() => cut.Instance.CancelAsync());   // Esc, as Enter above
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False);
            Assert.That(model.GetCurrentItem()!.Name, Is.EqualTo("item2"), "read back");
        });
    }

    /// <summary>
    /// An error of a column scrolled out of the column window has no editor to show it: the post button turns red and lists it.
    /// </summary>
    [Test]
    public async Task ThePostButtonShowsAnErrorOutsideTheColumnWindowTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, Columns).Add(g => g.HorzResizer, GridHorzResizers.Fixed(30)));
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        await PressAsync(cut, "F2");
        Assert.That(cut.Find("button.btnRowPost").ClassList, Does.Not.Contain("invalid"));

        model.GetCurrentItem()!.Qty = 500;
        await cut.InvokeAsync(() => cut.Instance.PostAsync());
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.GetShownColumns(), Does.Not.Contain("Quantity"));
            Assert.That(model.IsEditPending(), Is.True, "not posted");
            Assert.That(cut.Find("button.btnRowPost").ClassList, Does.Contain("invalid"));
            Assert.That(cut.Find("button.btnRowPost").GetAttribute("title"), Does.Contain("Qty"));
        });
    }

    [Test]
    public async Task ErrorsTheHostAddsShowOnTheirEditorOrOnTheRowHeadingTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, post =>
        {
            post.AddError(nameof(Item.Name), "taken");
            post.AddError(nameof(Item.Weight), "too heavy");
            return Task.CompletedTask;
        });
        await PressAsync(cut, "F2");
        await cut.Find("button.btnRowPost").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.True, "the handler did not post");
            Assert.That(cut.FindAll("td.editing")[0].QuerySelectorAll("i.editor-message"), Has.Length.EqualTo(1));
            Assert.That(cut.Find("i.row-errors").GetAttribute("title"), Is.EqualTo("too heavy"), "no editor shows Weight");
        });

        await cut.Find("button.btnRowCancel").ClickAsync(new());
        Assert.That(model.IsEditPending(), Is.False);
    }

    [Test]
    public async Task CtrlInsInsertsTheNewItemInPlaceTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model));
        await PressAsync(cut, "Insert", ctrl: true);
        Assert.Multiple(() =>
        {
            Assert.That(model.IsInsertPending(), Is.True);
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(99));
            Assert.That(cut.FindAll("td.editing"), Has.Count.EqualTo(4));
        });
        await cut.InvokeAsync(() => cut.Instance.PostAsync());
        Assert.That(_store.Select(i => i.Id), Does.Contain(99));
    }

    [Test]
    public async Task CtrlDelHandsTheCurrentRowToOnRemoveTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        Item? removed = null;
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, Columns).Add(g => g.OnRemove, item =>
            {
                removed = item;
                _store.RemoveAll(i => i.Id == item.Id);
                model.GetData().RemoveAt(model.GetCurrentRow());
            }));
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        await PressAsync(cut, "Delete", ctrl: true);
        Assert.Multiple(() =>
        {
            Assert.That(removed?.Id, Is.EqualTo(2));
            Assert.That(model.GetDataView().Select(i => i.Id), Is.EqualTo(new[] { 1, 3, 4 }));
        });

        await PressAsync(cut, "F2");
        Assert.That(cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == "Delete" && k.Instance.CtrlKey).Instance.Enabled,
            Is.False, "not while a row is edited");
    }

    /// <summary>
    /// While OnRemove deletes, the row is still current: a double click on Remove hands it over once, and the row after it
    /// stays.
    /// </summary>
    [Test]
    public async Task ASecondRemoveWhileOnRemoveDeletesIsIgnoredTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        TaskCompletionSource deleting = new();
        int calls = 0;
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, Columns).Add(g => g.OnRemove, async item =>
            {
                calls++;
                await deleting.Task;
                _store.RemoveAll(i => i.Id == item.Id);
                model.GetData().RemoveAt(model.GetCurrentRow());
            }));
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        Task first = cut.Find("button.btnRemoveRow").ClickAsync(new());
        Task second = cut.Find("button.btnRemoveRow").ClickAsync(new());
        deleting.SetResult();
        await first;
        await second;
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(model.GetDataView().Select(i => i.Id), Is.EqualTo(new[] { 1, 3, 4 }));
        });
    }

    [Test]
    public async Task OnClickPostsTheRowEditedAndEditsTheRowClickedTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model), EditMode.OnClick);
        await cut.InvokeAsync(() => cut.Instance.RowClickedAsync(1, null));
        Assert.That(model.IsEditPending(), Is.True, "a click edits the row");

        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "bolt" }));
        await cut.InvokeAsync(() => cut.Instance.RowClickedAsync(3, null));
        Assert.Multiple(() =>
        {
            Assert.That(_store.Single(i => i.Id == 2).Name, Is.EqualTo("bolt"), "posted on the way");
            Assert.That(model.GetCurrentRow(), Is.EqualTo(3));
            Assert.That(model.IsEditPending(), Is.True);
        });

        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "" }));
        await cut.InvokeAsync(() => cut.Instance.RowClickedAsync(0, null));
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(3), "a row with errors stays");
            Assert.That(cut.FindAll("td.editing")[0].QuerySelectorAll("i.editor-message"), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public async Task APostThatMovesTheRowStopsTheMoveTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, async post =>
        {
            _store.RemoveAll(i => i.Id == post.Item.Id);
            _store.Add(post.Item.Copy());   // read back last
            await model.PostAsync();
        }, EditMode.OnClick);
        await cut.InvokeAsync(() => cut.Instance.RowClickedAsync(1, null));
        await cut.InvokeAsync(() => cut.Instance.RowClickedAsync(0, null));
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False);
            Assert.That(model.GetCurrentItem()!.Id, Is.EqualTo(2), "on the row posted, where it went");
        });
    }

    [Test]
    public async Task APostThatFiltersTheRowOutStopsTheMoveTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model), EditMode.OnClick);
        await cut.InvokeAsync(() => cut.Instance.SetFilter("Name", new DefaultDef { Value = "item" }));
        await cut.InvokeAsync(() => cut.Instance.RowClickedAsync(1, null));
        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "gone" }));
        await cut.InvokeAsync(() => cut.Instance.RowClickedAsync(3, null));   // Id 4, before the post shifts it to row 2
        Assert.Multiple(() =>
        {
            Assert.That(_store.Single(i => i.Id == 2).Name, Is.EqualTo("gone"), "posted");
            Assert.That(model.GetRowCount(), Is.EqualTo(3), "out of the filter");
            Assert.That(model.IsEditPending(), Is.False, "no edit of a row the user did not click");
        });
    }

    [Test]
    public async Task OnMoveEditsTheRowTheArrowsReachTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model), EditMode.OnMove);
        await PressAsync(cut, "ArrowDown");
        Assert.Multiple(() =>
        {
            Assert.That(model.GetCurrentRow(), Is.EqualTo(2));
            Assert.That(model.IsEditPending(), Is.True);
        });

        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "nut" }));
        await PressAsync(cut, "ArrowUp");
        Assert.Multiple(() =>
        {
            Assert.That(_store.Single(i => i.Id == 3).Name, Is.EqualTo("nut"));
            Assert.That(model.GetCurrentRow(), Is.EqualTo(1));
            Assert.That(model.IsEditPending(), Is.True);
        });
    }

    [Test]
    public async Task OnMoveAtTheFirstRowPostsAndKeepsEditingTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, Save(model), EditMode.OnMove);
        await PressAsync(cut, "ArrowUp");
        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "nut" }));
        await PressAsync(cut, "ArrowUp");
        Assert.Multiple(() =>
        {
            Assert.That(_store.Single(i => i.Id == 1).Name, Is.EqualTo("nut"), "posted");
            Assert.That(model.GetCurrentRow(), Is.Zero);
            Assert.That(model.IsEditPending(), Is.True, "as PageUp, the row is edited again");
        });
    }

    [Test]
    public async Task TheFooterButtonsEditInsertAndRemoveTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, Columns));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("button.btnEditRow"), Has.Count.EqualTo(1));
            Assert.That(cut.FindAll("button.btnInsertRow, button.btnRemoveRow"), Is.Empty, "without NewItem and OnRemove");
        });

        bool removed = false;
        cut.Render(ps => ps.Add(g => g.NewItem, _ => new Item { Id = 99 }).Add(g => g.OnRemove, _ => removed = true));
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        await cut.Find("button.btnRemoveRow").ClickAsync(new());
        Assert.That(removed, Is.True);

        await cut.Find("button.btnEditRow").ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.True);
            Assert.That(cut.FindAll("button.btnEditRow, button.btnInsertRow, button.btnRemoveRow").All(b => b.HasAttribute("disabled")),
                Is.True, "none while a row is edited");
        });
        await cut.InvokeAsync(() => cut.Instance.CancelAsync());
        await cut.Find("button.btnInsertRow").ClickAsync(new());
        Assert.That(model.IsInsertPending(), Is.True);
    }

    [Test]
    public void WithEditModeDisabledTheGridEditsNothingTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model, mode: EditMode.Disabled);
        Assert.That(cut.FindComponents<KeyAction>().Where(k => k.Instance.Key == "F2"), Is.Empty);
    }

    [Test]
    public async Task AColumnNotEditableShowsItsValueTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        RenderFragment columns = b =>
        {
            Column(b, "Name", edit: i => i.Name, editable: false);
            Column(b, "Quantity", edit: i => i.Qty);
        };
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, columns));
        await PressAsync(cut, "F2");
        Assert.That(cut.FindAll("td.editing"), Has.Count.EqualTo(1));

        RenderFragment computed = b => Column(b, "Twice", edit: i => i.Qty * 2);
        Assert.Throws<ArgumentException>(() => ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, NewModel())
            .Add(g => g.ChildContent, computed)), "EditProperty must be a property with a setter");
        RenderFragment initOnly = b => Column(b, "Code", edit: i => i.Code);
        Assert.Throws<ArgumentException>(() => ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, NewModel())
            .Add(g => g.ChildContent, initOnly)), "an init property is set once, not edited");
    }

    /// <summary>
    /// Without OnPost the grid posts through the model itself: the edit ends, and the row is what the model's reload reads, here
    /// the store the grid never wrote.
    /// </summary>
    [Test]
    public async Task WithoutOnPostTheGridPostsThroughTheModelTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        var cut = Render(ctx, model);
        await PressAsync(cut, "F2");
        await cut.InvokeAsync(() => cut.FindAll("td.editing input")[0].ChangeAsync(new ChangeEventArgs { Value = "bolt" }));
        await cut.InvokeAsync(() => cut.Instance.PostAsync());
        Assert.Multiple(() =>
        {
            Assert.That(model.IsEditPending(), Is.False);
            Assert.That(cut.FindAll("td.editing"), Is.Empty);
            Assert.That(model.GetItem(1).Name, Is.EqualTo("item2"), "read back from the store");
        });
    }

    [Test]
    public async Task OnPostTellsANewRowFromAnEditedOneTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        Func<GridPost<Item>, Task> save = Save(model);
        List<bool> inserting = [];
        var cut = Render(ctx, model, post =>
        {
            inserting.Add(post.Inserting);
            return save(post);
        });
        await PressAsync(cut, "F2");
        await cut.InvokeAsync(() => cut.Instance.PostAsync());
        await PressAsync(cut, "Insert", ctrl: true);
        await cut.InvokeAsync(() => cut.Instance.PostAsync());
        Assert.That(inserting, Is.EqualTo(new[] { false, true }));
    }

    /// <summary>
    /// While OnPost saves, the row is still pending: a double click on Post saves it once, and a Cancel does not drop it.
    /// </summary>
    [Test]
    public async Task ASecondPostWhileOnPostSavesIsIgnoredTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        Func<GridPost<Item>, Task> save = Save(model);
        TaskCompletionSource saving = new();
        int calls = 0;
        var cut = Render(ctx, model, async post =>
        {
            calls++;
            await saving.Task;
            await save(post);
        });
        await PressAsync(cut, "Insert", ctrl: true);
        Task first = cut.Find("button.btnRowPost").ClickAsync(new());
        Task second = cut.Find("button.btnRowPost").ClickAsync(new());
        await cut.InvokeAsync(() => cut.Instance.CancelAsync());
        saving.SetResult();
        await first;
        await second;
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_store.Count(i => i.Id == 99), Is.EqualTo(1));
            Assert.That(model.IsInsertPending(), Is.False);
        });
    }

    /// <summary>
    /// A column's EditTemplate replaces its default editor in the row being edited, and gets the row and the column's value.
    /// </summary>
    [Test]
    public async Task AnEditTemplateTakesThePlaceOfTheEditorTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        RenderFragment columns = b =>
        {
            b.OpenComponent<GridColumn<Item, string>>(0);
            b.AddAttribute(1, "Name", "Name");
            b.AddAttribute(2, "EditProperty", (Expression<Func<Item, string>>)(i => i.Name));
            b.AddAttribute(3, "EditTemplate", (RenderFragment<DecoratorContext<Item, string>>)(c => t =>
            {
                t.OpenElement(0, "span");
                t.AddAttribute(1, "class", "own-editor");
                t.AddContent(2, $"{c.Item.Id}:{c.Value}");
                t.CloseElement();
            }));
            b.CloseComponent();
            Column(b, "Quantity", edit: i => i.Qty);
        };
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, columns));
        await cut.InvokeAsync(() => model.SetCurrentRow(1));
        await PressAsync(cut, "F2");
        var cells = cut.FindAll("td.editing");
        Assert.Multiple(() =>
        {
            Assert.That(cells, Has.Count.EqualTo(2));
            Assert.That(cells[0].QuerySelector("span.own-editor")?.TextContent, Is.EqualTo("2:item2"));
            Assert.That(cells[0].QuerySelector("input"), Is.Null, "no default editor beside it");
            Assert.That(cells[1].QuerySelector("input"), Is.Not.Null, "the other column keeps its own");
        });
    }

    /// <summary>
    /// A DateTime gets a date and time input, so an edit keeps the time, a DateOnly a date input, and a nullable enum a list
    /// whose first entry is empty, for null.
    /// </summary>
    [Test]
    public async Task ADateAndANullableEnumGetTheirEditorsTest()
    {
        using BunitContext ctx = NewContext();
        var model = NewModel();
        RenderFragment columns = b =>
        {
            Column(b, "When", edit: i => i.When);
            Column(b, "Day", edit: i => i.Day);
            Column(b, "Shade", edit: i => i.Shade);
            Column(b, "Color", edit: i => i.Color);
        };
        var cut = ctx.Render<Grigliaria<Item>>(ps => ps.Add(g => g.Model, model).Add(g => g.EditMode, EditMode.Manual)
            .Add(g => g.ChildContent, columns));
        await PressAsync(cut, "F2");
        var selects = cut.FindAll("td.editing select");
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("td.editing input[type=datetime-local]"), Has.Count.EqualTo(1));
            Assert.That(cut.FindAll("td.editing input[type=date]"), Has.Count.EqualTo(1), "the DateOnly");
            Assert.That(selects, Has.Count.EqualTo(2));
            Assert.That(selects[0].QuerySelectorAll("option").Select(o => o.GetAttribute("value")), Is.EqualTo(new[] { "", "Red", "Green" }));
            Assert.That(selects[1].QuerySelectorAll("option").Select(o => o.GetAttribute("value")), Is.EqualTo(new[] { "Red", "Green" }));
        });
        await cut.InvokeAsync(() => cut.Find("td.editing input[type=datetime-local]").ChangeAsync(new ChangeEventArgs { Value = "2026-11-11T14:35:00" }));
        Assert.That(model.GetCurrentItem()!.When, Is.EqualTo(new DateTime(2026, 11, 11, 14, 35, 0)));
        await cut.InvokeAsync(() => cut.Find("td.editing input[type=date]").ChangeAsync(new ChangeEventArgs { Value = "2026-11-12" }));
        Assert.That(model.GetCurrentItem()!.Day, Is.EqualTo(new DateOnly(2026, 11, 12)));
        await cut.InvokeAsync(() => cut.FindAll("td.editing select")[0].ChangeAsync(new ChangeEventArgs { Value = "" }));
        Assert.That(model.GetCurrentItem()!.Shade, Is.Null);
        await cut.InvokeAsync(() => cut.FindAll("td.editing select")[0].ChangeAsync(new ChangeEventArgs { Value = "Green" }));
        Assert.That(model.GetCurrentItem()!.Shade, Is.EqualTo(Color.Green));
    }
}
