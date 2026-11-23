using Bunit;
using Glazor.Misc;
using Glazor.Tabs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// A tab set keeps one current tab: the one whose panel shows, and the one Tab reaches in the tablist.
/// </summary>
public class TabsTests
{
    private static void AddTab(RenderTreeBuilder builder, int seq, string name, bool hidden, bool disabled)
    {
        builder.OpenComponent<Tab>(seq);
        builder.AddAttribute(seq + 1, "Name", name);
        builder.AddAttribute(seq + 2, "Label", name);
        builder.AddAttribute(seq + 3, "Hidden", hidden);
        builder.AddAttribute(seq + 4, "Disabled", disabled);
        builder.AddAttribute(seq + 5, "ChildContent", (RenderFragment)(b => b.AddContent(0, $"PANEL-{name}")));
        builder.CloseComponent();
    }

    /// <summary>
    /// A current tab that turns hidden gives way to the first tab neither hidden nor disabled: its panel goes with its label,
    /// and Tab still reaches the tablist. One that turns disabled stays current.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CurrentTabTurningHiddenGivesWayTest(bool disabledInstead)
    {
        using BunitContext ctx = NewContext();
        bool changeB = false;
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: false, disabled: false);
            AddTab(builder, 10, "B", hidden: changeB && !disabledInstead, disabled: changeB && disabledInstead);
        };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, LabelsPosition.Top).Add(t => t.ChildContent, tabs));
        await cut.InvokeAsync(() => cut.Instance.SetCurrentTabAsync("B"));
        cut.Render();
        changeB = true;
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, disabledInstead ? Does.Contain("PANEL-B") : Does.Contain("PANEL-A").And.Not.Contain("PANEL-B"));
            Assert.That(cut.FindAll("button[tabindex='0']"), Has.Count.EqualTo(1), "Tab reaches the tablist");
            Assert.That(cut.Instance.CurrentTabName, Is.EqualTo(disabledInstead ? "B" : "A"), "the host can read the tab shown");
        });
    }

    /// <summary>
    /// Labels on a side make a vertical tablist, so a screen reader announces the arrow keys that move along it.
    /// </summary>
    [TestCase(LabelsPosition.Left)]
    [TestCase(LabelsPosition.LeftVert)]
    [TestCase(LabelsPosition.Right)]
    [TestCase(LabelsPosition.RightVert)]
    public void SideLabelsMakeAVerticalTablistTest(LabelsPosition position)
    {
        using BunitContext ctx = NewContext();
        RenderFragment tabs = builder => AddTab(builder, 0, "A", hidden: false, disabled: false);
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, position).Add(t => t.ChildContent, tabs));
        Assert.That(cut.Find("[role=tablist]").GetAttribute("aria-orientation"), Is.EqualTo("vertical"));
    }

    /// <summary>
    /// Two tabs with the same name are a configuration error: the render throws, as AddColumn does for a column name used
    /// twice, instead of showing one tab and dropping the other.
    /// </summary>
    [Test]
    public void DuplicateTabNameThrowsTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: false, disabled: false);
            AddTab(builder, 10, "A", hidden: false, disabled: false);
        };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, LabelsPosition.Top).Add(t => t.ChildContent, tabs)));
        Assert.That(ex!.Message, Does.Contain("\"A\""));
    }

    /// <summary>
    /// A tab re-created under the same name in one render, an <c>@if</c>/<c>else</c> pair, registers before the one it
    /// replaces leaves: that is no duplicate, and a current tab stays current under its name.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TabRecreatedUnderTheSameNameReplacesItTest(bool current)
    {
        using BunitContext ctx = NewContext();
        bool admin = true;
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: false, disabled: false);
            if (admin)
                AddTab(builder, 10, "S", hidden: false, disabled: false);
            else
            {
                builder.OpenComponent<Tab>(20);
                builder.AddAttribute(21, "Name", "S");
                builder.AddAttribute(22, "Label", "User");
                builder.AddAttribute(23, "ChildContent", (RenderFragment)(b => b.AddContent(0, "PANEL-User")));
                builder.CloseComponent();
            }
        };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, LabelsPosition.Top).Add(t => t.ChildContent, tabs));
        if (current)
            await cut.InvokeAsync(() => cut.Instance.SetCurrentTabAsync("S"));
        admin = false;
        cut.Render();
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("button[role=tab]").Select(b => b.TextContent.Trim()), Is.EqualTo(new[] { "A", "User" }));
            Assert.That(cut.Instance.CurrentTabName, Is.EqualTo(current ? "S" : "A"));
            Assert.That(cut.Markup, current ? Does.Contain("PANEL-User") : Does.Contain("PANEL-A"));
        });
    }

    /// <summary>
    /// A tab label is a plain button: inside a host form a click on it, or Enter in a field, must not submit the form.
    /// </summary>
    [Test]
    public void TabLabelsDoNotSubmitAFormTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: false, disabled: false);
            AddTab(builder, 10, "B", hidden: false, disabled: false);
        };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, LabelsPosition.Top).Add(t => t.ChildContent, tabs));
        var labels = cut.FindAll("button.tab-label");
        Assert.That(labels, Has.Count.EqualTo(2));
        Assert.That(labels.Select(l => ((AngleSharp.Html.Dom.IHtmlButtonElement)l).Type), Is.All.EqualTo("button"));
    }

    /// <summary>
    /// The tabs activate manually: Enter on the focused tab selects it through the tablist's own Enter action, so a host's
    /// Enter action around the tab set does not take it.
    /// </summary>
    [Test]
    public async Task EnterSelectsTheFocusedTabTest()
    {
        using BunitContext ctx = NewContext();
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: false, disabled: false);
            AddTab(builder, 10, "B", hidden: false, disabled: false);
        };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, LabelsPosition.Top).Add(t => t.ChildContent, tabs));
        var enter = cut.FindComponents<KeyAction>().Where(k => k.Instance.Key == "Enter").ToList();
        Assert.That(enter, Has.Count.EqualTo(1), "the tablist takes Enter");
        string idB = cut.FindAll("button.tab-label")[1].Id!;
        await cut.InvokeAsync(() => enter[0].Instance.ProcessAsync(null, null, idB));
        Assert.That(cut.Markup, Does.Contain("PANEL-B").And.Not.Contain("PANEL-A"));
    }

    /// <summary>
    /// A click on another tab selects it and raises OnTabChanged with the new and the previous tab's names.
    /// </summary>
    [Test]
    public async Task ClickRaisesOnTabChangedTest()
    {
        using BunitContext ctx = NewContext();
        List<TabChangedEvent> changes = [];
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: false, disabled: false);
            AddTab(builder, 10, "B", hidden: false, disabled: false);
        };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.ChildContent, tabs)
            .Add(t => t.OnTabChanged, EventCallback.Factory.Create<TabChangedEvent>(this, changes.Add)));
        await cut.FindAll("button.tab-label")[1].ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(new[] { new TabChangedEvent("B", "A") }));
            Assert.That(cut.Markup, Does.Contain("PANEL-B").And.Not.Contain("PANEL-A"));
        });
    }

    /// <summary>
    /// A tab that is disabled, hidden, unknown or already current is refused, and so is every tab while the current one is
    /// frozen: SetCurrentTabAsync returns false, a click does nothing, and OnTabChanged is not raised.
    /// </summary>
    [TestCase("disabled")]
    [TestCase("hidden")]
    [TestCase("unknown")]
    [TestCase("current")]
    [TestCase("frozen")]
    public async Task RefusedTabChangesNothingTest(string refusal)
    {
        using BunitContext ctx = NewContext();
        List<TabChangedEvent> changes = [];
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: false, disabled: false);
            AddTab(builder, 10, "B", hidden: refusal == "hidden", disabled: refusal == "disabled");
        };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.ChildContent, tabs).Add(t => t.CurrentTabFrozen, refusal == "frozen")
            .Add(t => t.OnTabChanged, EventCallback.Factory.Create<TabChangedEvent>(this, changes.Add)));
        string target = refusal switch { "unknown" => "C", "current" => "A", _ => "B" };
        Assert.That(await cut.InvokeAsync(() => cut.Instance.SetCurrentTabAsync(target)), Is.False);
        if (refusal is "disabled" or "frozen" or "current")
            await cut.FindAll("button.tab-label")[target == "A" ? 0 : 1].ClickAsync(new());
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(cut.Markup, Does.Contain("PANEL-A").And.Not.Contain("PANEL-B"));
        });
    }

    private static int FocusCalls(BunitContext ctx) => ctx.JSInterop.Invocations.Count(i => i.Identifier.EndsWith("focus"));

    // The element the last focus call went to; bUnit leaves a captured reference off the markup, so it is told apart by its id
    private static string LastFocused(BunitContext ctx) =>
        ((ElementReference)ctx.JSInterop.Invocations.Last(i => i.Identifier.EndsWith("focus")).Arguments[0]!).Id;

    /// <summary>
    /// The arrow keys move the focus to the next or previous tab, wrapping at the ends and skipping a hidden tab; Home and End go
    /// to the first and last tab shown. With every tab hidden they move nothing.
    /// </summary>
    [Test]
    public async Task ArrowsHomeAndEndMoveTheFocusOverTheTabsShownTest()
    {
        using BunitContext ctx = NewContext();
        bool cHidden = true, allHidden = false;
        RenderFragment tabs = builder =>
        {
            AddTab(builder, 0, "A", hidden: allHidden, disabled: false);
            AddTab(builder, 10, "B", hidden: true, disabled: false);
            AddTab(builder, 20, "C", hidden: cHidden || allHidden, disabled: false);
        };
        var cut = ctx.Render<Tabs.Tabs>(ps => ps.Add(t => t.LabelsPosition, LabelsPosition.Top).Add(t => t.ChildContent, tabs));
        KeyAction Key(string key) => cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == key).Instance;
        string Id(string name) => cut.FindAll("button.tab-label").Single(b => b.TextContent == name).Id!;
        string a = "";
        async Task<string> PressAsync(string key, string? on)
        {
            await cut.InvokeAsync(() => Key(key).ProcessAsync(null, null, on == null ? null : Id(on)));
            string id = LastFocused(ctx);
            return id == a ? "A" : string.IsNullOrEmpty(id) ? "a hidden tab" : "C";   // C is the only other button shown
        }

        await cut.InvokeAsync(() => Key("Home").ProcessAsync(null, null, null));
        a = LastFocused(ctx);   // A is the only tab shown, B and C hidden
        cHidden = false;
        cut.Render();

        string afterRight = await PressAsync("ArrowRight", "A");
        string wrappedRight = await PressAsync("ArrowRight", "C");
        string wrappedLeft = await PressAsync("ArrowLeft", "A");
        string home = await PressAsync("Home", "C");
        string end = await PressAsync("End", "A");
        Assert.Multiple(() =>
        {
            Assert.That(afterRight, Is.EqualTo("C"), "B is hidden");
            Assert.That(wrappedRight, Is.EqualTo("A"), "past the last tab");
            Assert.That(wrappedLeft, Is.EqualTo("C"), "before the first tab, B skipped");
            Assert.That(home, Is.EqualTo("A"));
            Assert.That(end, Is.EqualTo("C"));
        });

        allHidden = true;
        cut.Render();
        int focusCalls = FocusCalls(ctx);
        await cut.InvokeAsync(() => Key("Home").ProcessAsync(null, null, null));
        await cut.InvokeAsync(() => Key("End").ProcessAsync(null, null, null));
        Assert.That(FocusCalls(ctx), Is.EqualTo(focusCalls), "no tab to focus");
    }
}
