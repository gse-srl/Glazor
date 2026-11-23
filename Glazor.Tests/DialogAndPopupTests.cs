using Bunit;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace Glazor.Tests;

/// <summary>
/// The dialog and popup components through their public surface: the style attribute they render, the declarative
/// Open parameter, what happens to IsVisible when the JS show call fails, and a spinner asked to show before it has rendered.
/// </summary>
public class DialogAndPopupTests
{
    private static BunitContext NewContext()
    {
        BunitContext ctx = new();
        ctx.Services.AddLogging();
        return ctx;
    }

    [Test]
    public void DialogBoxKeepsTheCallersStyleTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<DialogBox>(ps => ps.Add(d => d.Style, "width: 50%").AddChildContent("x"));
        Assert.That(cut.Find("dialog").GetAttribute("style"), Does.EndWith("width: 50%"));
    }

    /// <summary>ClosedBy reaches the dialog's closedby attribute: a dialog closes on a close request by default, a popup on any.</summary>
    [Test]
    public void ClosedByReachesTheAttributeTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        Assert.Multiple(() =>
        {
            Assert.That(ctx.Render<DialogBox>(ps => ps.AddChildContent("x")).Find("dialog").GetAttribute("closedby"), Is.EqualTo("closerequest"));
            Assert.That(ctx.Render<DialogBox>(ps => ps.Add(d => d.ClosedBy, ClosedBy.None).AddChildContent("x")).Find("dialog")
                .GetAttribute("closedby"), Is.EqualTo("none"));
            Assert.That(ctx.Render<Popup>(ps => ps.AddChildContent("x")).Find("dialog").GetAttribute("closedby"), Is.EqualTo("any"));
        });
    }

    [Test]
    public void PopupOpenParameterOpensAndClosesTest()
    {
        using BunitContext ctx = NewContext();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/Popup.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<Popup>(ps => ps.Add(p => p.Open, true).AddChildContent("x"));
        cut.WaitForAssertion(() => Assert.That(module.Invocations["show"], Has.Count.EqualTo(1)));
        Assert.That(cut.Instance.IsVisible, Is.True);
        cut.Render(ps => ps.Add(p => p.Open, false).AddChildContent("x"));
        cut.WaitForAssertion(() => Assert.That(module.Invocations["close"], Has.Count.EqualTo(1)));
        Assert.That(cut.Instance.IsVisible, Is.False);
    }

    [Test]
    public async Task FailedShowDoesNotLeaveTheDialogVisibleTest()
    {
        using BunitContext ctx = NewContext();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/DialogBox.razor.js");
        module.SetupVoid("show", _ => true).SetException(new JSException("boom"));
        var cut = ctx.Render<DialogBox>(ps => ps.AddChildContent("x"));
        Assert.ThrowsAsync<JSException>(async () => await cut.InvokeAsync(() => cut.Instance.ShowAsync()));
        Assert.That(cut.Instance.IsVisible, Is.False, "the show did not happen");
        // A second attempt must try the interop again instead of returning false because the dialog thinks it is open.
        Assert.ThrowsAsync<JSException>(async () => await cut.InvokeAsync(() => cut.Instance.ShowAsync()));
        Assert.That(module.Invocations["show"], Has.Count.EqualTo(2));
        // After a successful show, a close coming from the browser (ESC) must still be recognised as such: the rollback has to
        // reset all three counters, or the dialog stays "visible" for the rest of the circuit.
        module.SetupVoid("show", _ => true).SetVoidResult();
        Assert.That(await cut.InvokeAsync(() => cut.Instance.ShowAsync()), Is.True);
        Assert.That(cut.Instance.IsVisible, Is.True);
        await cut.InvokeAsync(() => cut.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty));
        Assert.That(cut.Instance.IsVisible, Is.False, "the browser closed it");
    }

    /// <summary>
    /// OnClose is raised for every close the browser reports, from CloseAsync or from Esc. A close that arrives after the
    /// dialog was opened again belongs to the earlier showing: it is reported, and the dialog stays open.
    /// </summary>
    [Test]
    public async Task OnCloseOnEveryClosePathTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.SetupModule("./_content/Glazor/Misc/DialogBox.razor.js").Mode = JSRuntimeMode.Loose;
        int closes = 0;
        var cut = ctx.Render<DialogBox>(ps => ps.AddChildContent("x").Add(d => d.OnClose, () => closes++));
        Task BrowserClosesAsync() => cut.InvokeAsync(() => cut.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty));

        await cut.InvokeAsync(() => cut.Instance.ShowModalAsync());
        await cut.InvokeAsync(() => cut.Instance.CloseAsync());
        await BrowserClosesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(closes, Is.EqualTo(1), "CloseAsync");
            Assert.That(cut.Instance.IsVisible, Is.False);
        });

        await cut.InvokeAsync(() => cut.Instance.ShowModalAsync());
        await BrowserClosesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(closes, Is.EqualTo(2), "Esc");
            Assert.That(cut.Instance.IsVisible, Is.False);
        });

        await cut.InvokeAsync(() => cut.Instance.ShowModalAsync());
        await cut.InvokeAsync(() => cut.Instance.CloseAsync());
        await cut.InvokeAsync(() => cut.Instance.ShowModalAsync());
        await BrowserClosesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(closes, Is.EqualTo(3), "the earlier showing's close");
            Assert.That(cut.Instance.IsVisible, Is.True, "the dialog opened again stays open");
        });

        await BrowserClosesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(closes, Is.EqualTo(4), "Esc on the second showing");
            Assert.That(cut.Instance.IsVisible, Is.False);
        });
    }

    /// <summary>A dialog disposed while open is no longer visible, as a popup is not.</summary>
    [Test]
    public async Task DialogDisposedWhileOpenIsNotVisibleTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.SetupModule("./_content/Glazor/Misc/DialogBox.razor.js").Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<DialogBox>(ps => ps.AddChildContent("x"));
        await cut.InvokeAsync(() => cut.Instance.ShowModalAsync());
        Assert.That(cut.Instance.IsVisible, Is.True);
        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        Assert.That(cut.Instance.IsVisible, Is.False);
    }

    [Test]
    public void SpinnerShowBeforeItsFirstRenderTest()
    {
        Spinner spinner = new();
        Assert.That(spinner.Show(), Is.False, "nothing to show yet");
        Assert.DoesNotThrow(spinner.Hide);
    }

    /// <summary>
    /// A rendered spinner opens its dialog modally on Show, once while it is shown, and closes it on Hide; the grids call both
    /// from the fetches they cover.
    /// </summary>
    [Test]
    public async Task SpinnerShowOpensTheDialogOnceAndHideClosesItTest()
    {
        using BunitContext ctx = NewContext();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/DialogBox.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<Spinner>();
        bool first = false, second = true;
        await cut.InvokeAsync(() => { first = cut.Instance.Show(); second = cut.Instance.Show(); });
        cut.WaitForAssertion(() => Assert.That(module.Invocations["showModal"], Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False, "already shown");
            Assert.That(cut.FindComponent<DialogBox>().Instance.IsVisible, Is.True);
        });
        await cut.InvokeAsync(() => cut.Instance.Hide());
        cut.WaitForAssertion(() => Assert.That(module.Invocations["close"], Has.Count.EqualTo(1)));
        Assert.That(cut.FindComponent<DialogBox>().Instance.IsVisible, Is.False);
    }

    /// <summary>
    /// The stuck case: the popup is closed while Open is still true (the user pressed ESC or clicked outside, or CloseAsync
    /// ran), the parent sets Open false and then true again. The popup renders only while visible, so the change must be
    /// detected at parameter time or the second show never happens.
    /// </summary>
    [Test]
    public async Task PopupReopensThroughOpenAfterItWasClosedUnderneathTest()
    {
        using BunitContext ctx = NewContext();
        var module = ctx.JSInterop.SetupModule("./_content/Glazor/Misc/Popup.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<Popup>(ps => ps.Add(p => p.Open, true).AddChildContent("x"));
        cut.WaitForAssertion(() => Assert.That(module.Invocations["show"], Has.Count.EqualTo(1)));
        await cut.InvokeAsync(() => cut.Instance.CloseAsync());   // closed underneath the parent, which still holds Open = true
        Assert.That(cut.Instance.IsVisible, Is.False);
        cut.Render(ps => ps.Add(p => p.Open, false).AddChildContent("x"));
        cut.Render(ps => ps.Add(p => p.Open, true).AddChildContent("x"));
        cut.WaitForAssertion(() => Assert.That(module.Invocations["show"], Has.Count.EqualTo(2), "Open must be able to reopen it"));
        Assert.That(cut.Instance.IsVisible, Is.True);
    }

    /// <summary>
    /// ShowAsync(x, y) leaves the placing to the script, which measures the popup as the browser has it. A host fills the popup
    /// and opens it in the same click handler, where a render waits for the handler to yield: the content of this opening must
    /// reach the browser before the script runs. Measured with the content of the opening before, a popup that has grown is
    /// placed as if it were shorter, and crosses the bottom edge of the window instead of opening above the point.
    /// </summary>
    [Test]
    public void PopupIsMeasuredWithTheContentOfThisOpeningTest()
    {
        using BunitContext ctx = NewContext();
        IRenderedComponent<FillAndShow>? cut = null;
        string? measured = null;
        ctx.Services.AddSingleton<IJSRuntime>(new ShowProbeJSRuntime(() => measured = cut!.Markup));
        cut = ctx.Render<FillAndShow>();

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.That(measured, Does.Contain("three roles"), "what the script measures holds the content set by the click"));
    }

    /// <summary>A host whose click fills the popup and shows it at a point, without yielding in between.</summary>
    private sealed class FillAndShow : ComponentBase
    {
        private Popup _popup = null!;
        private string _content = "empty";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, ShowAsync));
            builder.CloseElement();
            builder.OpenComponent<Popup>(2);
            builder.AddAttribute(3, nameof(Popup.ChildContent), (RenderFragment)(b => b.AddContent(0, _content)));
            builder.AddComponentReferenceCapture(4, popup => _popup = (Popup)popup);
            builder.CloseComponent();
        }

        private async Task ShowAsync(MouseEventArgs e)
        {
            _content = "three roles";
            await _popup.ShowAsync(10, 10);
        }
    }

    /// <summary>
    /// showModal focuses what the dialog holds when it opens. A host opens a dialog from a click handler, where a render waits
    /// for the handler to yield: the content of this opening must reach the browser first, or the render replaces the content
    /// the browser focused, keyed on the previous opening, and the focus falls to the body.
    /// </summary>
    [Test]
    public void DialogOpensOnTheContentOfThisOpeningTest()
    {
        using BunitContext ctx = NewContext();
        IRenderedComponent<FillAndShowDialog>? cut = null;
        string? shown = null;
        ctx.Services.AddSingleton<IJSRuntime>(new ShowProbeJSRuntime(() => shown = cut!.Markup));
        cut = ctx.Render<FillAndShowDialog>();

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.That(shown, Does.Contain("three roles"), "what the browser opens holds the content set by the click"));
    }

    /// <summary>A host whose click fills the dialog and opens it modally, without yielding in between.</summary>
    private sealed class FillAndShowDialog : ComponentBase
    {
        private DialogBox _dialog = null!;
        private string _content = "empty";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, ShowAsync));
            builder.CloseElement();
            builder.OpenComponent<DialogBox>(2);
            builder.AddAttribute(3, nameof(DialogBox.ChildContent), (RenderFragment)(b => b.AddContent(0, _content)));
            builder.AddComponentReferenceCapture(4, dialog => _dialog = (DialogBox)dialog);
            builder.CloseComponent();
        }

        private async Task ShowAsync(MouseEventArgs e)
        {
            _content = "three roles";
            await _dialog.ShowModalAsync();
        }
    }

    /// <summary>A JS runtime whose popup and dialog modules tell the test when the script is asked to show them.</summary>
    private sealed class ShowProbeJSRuntime(Action onShow) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(identifier == "import" ? (TValue)(object)new Module(onShow) : default!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        private sealed class Module(Action onShow) : IJSObjectReference
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            {
                if (identifier is "show" or "showModal")
                    onShow();
                return ValueTask.FromResult<TValue>(default!);
            }

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
                => InvokeAsync<TValue>(identifier, args);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // ─────────────────────────── m11: no module import into a disposed component ───────────────────────────

    /// <summary>
    /// Clamper hid its popup by removing it from the tree, which disposes it, and then called CloseAsync on the disposed
    /// instance: the popup imported its module again into a dead component, a reference nothing disposes. The close must come
    /// before the render that removes the popup, and a disposed popup must not import.
    /// </summary>
    [Test]
    public async Task ClamperClosingItsPopupDoesNotResurrectItTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<Clamper>(ps => ps.Add(c => c.LinesToClamp, 1).AddChildContent("a long text"));
        await cut.InvokeAsync(() => cut.Instance.ProcessClick(0, 0));
        Assert.That(cut.FindAll("dialog"), Has.Count.EqualTo(1), "the first click opens the popup");
        await cut.InvokeAsync(() => cut.Instance.ProcessClick(0, 0));
        Assert.That(cut.FindAll("dialog"), Is.Empty, "the second click removes it");
        Assert.That(PopupImports(ctx), Is.EqualTo(1), "the disposed popup must not import its module again");
        await cut.InvokeAsync(() => cut.Instance.ProcessClick(0, 0));
        Assert.That(cut.FindAll("dialog"), Has.Count.EqualTo(1), "the third click opens a new one");
    }

    /// <summary>
    /// Esc in the Clamper's popup closes it and goes no further: inside the layout or filter dialog, whose Escape action cancels
    /// the dialog, the Esc meant for the popup must not reach it.
    /// </summary>
    [Test]
    public async Task EscClosesTheClamperPopupTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<Clamper>(ps => ps.Add(c => c.LinesToClamp, 1).AddChildContent("a long text"));
        await cut.InvokeAsync(() => cut.Instance.ProcessOver(true, 0, 0));
        var popup = cut.FindComponent<Popup>();
        Assert.That(popup.Instance.IsVisible, Is.True);
        var esc = popup.FindComponents<KeyAction>().Where(k => k.Instance.Key == "Escape").ToList();
        Assert.That(esc, Has.Count.EqualTo(1), "the popup takes Esc");
        Assert.That(esc[0].Instance.BindAncestorWithTag, Is.EqualTo("dialog"), "on the popup, before the key leaves it");
        await cut.InvokeAsync(() => esc[0].Instance.ProcessAsync(null, null, null));
        Assert.That(popup.Instance.IsVisible, Is.False);
    }

    /// <summary>
    /// The Clamper's popup shows its content as it is: under an inherited white-space: pre-wrap, any text the popup adds around
    /// it shows as a blank line.
    /// </summary>
    [Test]
    public async Task ClamperPopupAddsNoTextTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<Clamper>(ps => ps.Add(c => c.LinesToClamp, 1).AddChildContent("a long text"));
        await cut.InvokeAsync(() => cut.Instance.ProcessOver(true, 0, 0));
        Assert.That(cut.Find("dialog.clamper-popup").TextContent, Is.EqualTo("a long text"));
    }

    private static int PopupImports(BunitContext ctx) =>
        ctx.JSInterop.Invocations["import"].Count(i => Equals(i.Arguments[0], "./_content/Glazor/Misc/Popup.razor.js"));

    /// <summary>
    /// The other way to resurrect a module: the component is disposed while its first-render import is still in flight.
    /// DisposeAsync finds no module to dispose, then the import completes into the dead component.
    /// </summary>
    [Test]
    public async Task PopupDisposedDuringTheImportDisposesTheModuleTest()
    {
        using BunitContext ctx = NewContext();
        PendingImportJSRuntime js = new();
        ctx.Services.AddSingleton<IJSRuntime>(js);
        var cut = ctx.Render<Popup>(ps => ps.AddChildContent("x"));   // OnAfterRenderAsync is now awaiting the import
        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        js.Import.SetResult(js.Module);
        cut.WaitForAssertion(() => Assert.That(js.Module.Disposed, Is.EqualTo(1), "the reference imported into a disposed component must be released"));
    }

    /// <summary>
    /// A JS runtime whose module import completes only when the test says so, and whose module counts its disposals.
    /// </summary>

    /// <summary>
    /// Opening the Clamper's popup makes two callers load its module before either import has returned, the popup's first
    /// render and the show. The import in flight must be shared, or the second reference is never disposed.
    /// </summary>
    [Test]
    public async Task OverlappingModuleLoadsImportOnceTest()
    {
        using BunitContext ctx = NewContext();
        PendingImportJSRuntime js = new();
        ctx.Services.AddSingleton<IJSRuntime>(js);
        var cut = ctx.Render<Clamper>(ps => ps.Add(c => c.LinesToClamp, 1).AddChildContent("a long text"));
        js.CompleteAll();   // the Clamper's own module
        Task click = cut.InvokeAsync(() => cut.Instance.ProcessClick(0, 0));
        Assert.That(SpinWait.SpinUntil(() => js.ImportsOf("Popup.razor.js") >= 1, TimeSpan.FromSeconds(5)), Is.True, "the popup asks for its module");
        js.CompleteAll();
        await click;
        Assert.That(js.ImportsOf("Popup.razor.js"), Is.EqualTo(1), "one import, shared by both callers");
        await ctx.DisposeComponentsAsync();
        Assert.That(js.Modules.Count(m => m.Disposed == 0), Is.Zero, "every module handed out is disposed");
    }

    /// <summary>
    /// A clamper bound to a setting that starts at 0 lines must load its module when the lines become positive, once however
    /// many renders arrive before the import returns.
    /// </summary>
    [Test]
    public void ClamperStartingAtZeroLinesImportsWhenTheyBecomePositiveTest()
    {
        using BunitContext ctx = NewContext();
        PendingImportJSRuntime js = new();
        ctx.Services.AddSingleton<IJSRuntime>(js);
        var cut = ctx.Render<Clamper>(ps => ps.Add(c => c.LinesToClamp, 0).AddChildContent("a long text"));
        Assert.That(js.Imports, Is.Empty, "nothing to clamp yet");
        cut.Render(ps => ps.Add(c => c.LinesToClamp, 2).AddChildContent("a long text"));
        cut.Render(ps => ps.Add(c => c.LinesToClamp, 3).AddChildContent("a long text"));
        Assert.That(js.ImportsOf("Clamper.razor.js"), Is.EqualTo(1));
    }

    /// <summary>
    /// The global stylesheet styles only elements that carry the library's own class, so a host's .popup, .dialog or .disabled
    /// rules and the library's never meet: every rule in glazor.css requires it, and the components emit it.
    /// </summary>
    [Test]
    public void StylesheetRulesAndComponentsShareTheLibraryClassTest()
    {
        string css = File.ReadAllText(Path.Combine(RepoRoot.Dir, "Glazor", "wwwroot", "glazor.css"));
        var selectors = css.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.EndsWith('{') && !l.StartsWith(' ')).Select(l => l[..^1].Trim()).ToList();
        Assert.That(selectors, Is.Not.Empty);
        Assert.That(selectors, Has.All.Matches<string>(s =>
            s.Contains(".glazor") || s == ":root"),
            "a selector a host page could match by accident");
        Assert.That(Regex.Matches(css, @":root \{(.*?)\}", RegexOptions.Singleline).SelectMany(m => Regex.Matches(m.Groups[1].Value, @"^\s*([a-zA-Z-]+)\s*:", RegexOptions.Multiline).Select(d => d.Groups[1].Value)),
            Has.All.StartWith("--glazor-"), ":root holds the theme properties and nothing else");
        Assert.That(css, Does.Not.Contain("a.disabled *"), "children inherit the anchor's opacity; a rule of their own compounded it");

        using BunitContext ctx = NewContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        Assert.Multiple(() =>
        {
            Assert.That(ctx.Render<DialogBox>(ps => ps.AddChildContent("x")).Find("dialog").ClassList, Does.Contain("glazor").And.Contain("dialog"));
            Assert.That(ctx.Render<Popup>(ps => ps.AddChildContent("x")).Find("dialog").ClassList, Does.Contain("glazor").And.Contain("popup"));
            Assert.That(ctx.Render<Clamper>(ps => ps.Add(c => c.LinesToClamp, 2).AddChildContent("x")).Find("div").ClassList, Does.Contain("glazor").And.Contain("clamper"));
            Assert.That(ctx.Render<Anchor>(ps => ps.Add(a => a.Disabled, true).AddChildContent("x")).Find("a").ClassList, Does.Contain("glazor").And.Contain("disabled"));
        });
    }

    /// <summary>
    /// The attributes a host passes go on the element without replacing the component's own: an Anchor adds a host's class to
    /// its own and keeps its href and role, a popup keeps its class.
    /// </summary>
    [Test]
    public void HostAttributesKeepTheComponentsOwnTest()
    {
        using BunitContext ctx = NewContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var enabled = ctx.Render<Anchor>(ps => ps.Add(a => a.OnClick, () => { }).AddUnmatched("class", "mine").AddUnmatched("href", "elsewhere").AddChildContent("x")).Find("a");
        var disabled = ctx.Render<Anchor>(ps => ps.Add(a => a.Disabled, true).AddUnmatched("class", "mine").AddUnmatched("role", "button").AddChildContent("x")).Find("a");
        var popup = ctx.Render<Popup>(ps => ps.Add(p => p.AdditionalAttributes, new Dictionary<string, object> { ["class"] = "mine", ["data-x"] = "1" }).AddChildContent("x")).Find("dialog");
        Assert.Multiple(() =>
        {
            Assert.That(enabled.ClassList, Does.Contain("glazor").And.Contain("enabled").And.Contain("mine"));
            Assert.That(enabled.GetAttribute("href"), Is.EqualTo("#"));
            Assert.That(disabled.ClassList, Does.Contain("glazor").And.Contain("disabled").And.Contain("mine"));
            Assert.That(disabled.GetAttribute("role"), Is.EqualTo("link"));
            Assert.That(popup.ClassList, Does.Contain("glazor").And.Contain("popup"));
            Assert.That(popup.GetAttribute("data-x"), Is.EqualTo("1"), "the other attributes still go on");
        });
    }
}
