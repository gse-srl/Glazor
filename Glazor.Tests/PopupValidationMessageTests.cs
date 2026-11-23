using Bunit;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The message is shown as an icon with a popup; the icon has to be reachable without a mouse, or a keyboard user gets no
/// validation text at all.
/// </summary>
public class PopupValidationMessageTests
{
    private sealed class Model
    {
        public string? Email { get; set; }
    }

    private static (BunitContext Ctx, EditContext EditContext, ValidationMessageStore Store, Model Model) Setup()
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLogging();
        Model model = new();
        EditContext editContext = new(model);
        ValidationMessageStore store = new(editContext);
        return (ctx, editContext, store, model);
    }

    private static IRenderedComponent<CascadingValue<EditContext>> Render(BunitContext ctx, EditContext editContext, Model model, bool showWhenMouseOver = false)
        => ctx.Render<CascadingValue<EditContext>>(ps => ps
            .Add(c => c.Value, editContext)
            .AddChildContent<PopupValidationMessage<string?>>(cp => cp.Add(p => p.For, () => model.Email).Add(p => p.ShowWhenMouseOver, showWhenMouseOver)));

    [Test]
    public void InvalidFieldRendersAFocusableIconTest()
    {
        var (ctx, editContext, store, model) = Setup();
        using (ctx)
        {
            store.Add(() => model.Email!, "Required");
            editContext.NotifyValidationStateChanged();
            var cut = Render(ctx, editContext, model);
            var icon = cut.Find("i.message-icon");
            Assert.Multiple(() =>
            {
                Assert.That(icon.GetAttribute("role"), Is.EqualTo("button"));
                Assert.That(icon.GetAttribute("tabindex"), Is.EqualTo("0"));
                Assert.That(icon.GetAttribute("aria-label"), Is.Not.Empty);
                Assert.That(icon.ClassList, Does.Contain("glazor").And.Contain("message-icon"), "the stylesheet styles only elements with the library's class");
                Assert.That(icon.QuerySelectorAll("span[hidden]"), Has.Length.EqualTo(2), "the key actions are the icon's own");
            });
        }
    }

    /// <summary>With <c>ShowWhenMouseOver</c> the icon is the same focusable control, and the mouse over it opens the message.</summary>
    [Test]
    public void MouseOverIconIsFocusableAndOpensTheMessageTest()
    {
        var (ctx, editContext, store, model) = Setup();
        using (ctx)
        {
            store.Add(() => model.Email!, "Required");
            editContext.NotifyValidationStateChanged();
            var cut = Render(ctx, editContext, model, showWhenMouseOver: true);
            var icon = cut.Find("i.message-icon");
            Assert.Multiple(() =>
            {
                Assert.That(icon.GetAttribute("role"), Is.EqualTo("button"));
                Assert.That(icon.GetAttribute("tabindex"), Is.EqualTo("0"));
                Assert.That(icon.GetAttribute("aria-label"), Is.Not.Empty);
                Assert.That(icon.QuerySelectorAll("span[hidden]"), Has.Length.EqualTo(2), "the key actions are the icon's own");
            });
            Popup popup = cut.FindComponent<Popup>().Instance;
            Assert.That(popup.IsVisible, Is.False);
            icon.MouseOver();
            Assert.That(popup.IsVisible, Is.True);
        }
    }

    /// <summary>
    /// The render that first shows the icon already anchors the popup to it: the icon names its anchor and the popup uses the
    /// same name, with no second render needed, as there is none when the field is found invalid outside a UI event.
    /// </summary>
    [Test]
    public void TheFirstRenderAnchorsThePopupToTheIconTest()
    {
        var (ctx, editContext, store, model) = Setup();
        using (ctx)
        {
            store.Add(() => model.Email!, "Required");
            editContext.NotifyValidationStateChanged();
            var cut = Render(ctx, editContext, model);
            string? anchor = System.Text.RegularExpressions.Regex.Match(cut.Find("i.message-icon").GetAttribute("style") ?? "", @"anchor-name:\s*(--[\w-]+)").Groups[1].Value;
            Assert.Multiple(() =>
            {
                Assert.That(anchor, Has.Length.GreaterThan(2), "a name after the two dashes");
                Assert.That(cut.Find("dialog.message-popup").GetAttribute("style"), Does.Contain($"position-anchor: {anchor};"));
            });
        }
    }

    [Test]
    public async Task SpaceAndEnterToggleTheMessageTest()
    {
        var (ctx, editContext, store, model) = Setup();
        using (ctx)
        {
            store.Add(() => model.Email!, "Required");
            editContext.NotifyValidationStateChanged();
            var cut = Render(ctx, editContext, model);
            var popup = cut.FindComponent<Popup>();
            var space = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == " ");
            var enter = cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == "Enter");
            Assert.That(popup.Instance.IsVisible, Is.False);
            await cut.InvokeAsync(() => space.Instance.ProcessAsync(null, null, null));
            Assert.That(popup.Instance.IsVisible, Is.True, "Space opens");
            await cut.InvokeAsync(() => space.Instance.ProcessAsync(null, null, null));
            Assert.That(popup.Instance.IsVisible, Is.False, "Space again closes");
            await cut.InvokeAsync(() => enter.Instance.ProcessAsync(null, null, null));
            Assert.That(popup.Instance.IsVisible, Is.True, "Enter opens");
        }
    }

    /// <summary>
    /// Esc in the message closes it and goes no further: in a row edited in place, the grid's Escape action cancels the edit,
    /// and the Esc meant for the message must not reach it.
    /// </summary>
    [Test]
    public async Task EscClosesTheMessageTest()
    {
        var (ctx, editContext, store, model) = Setup();
        using (ctx)
        {
            store.Add(() => model.Email!, "Required");
            editContext.NotifyValidationStateChanged();
            var cut = Render(ctx, editContext, model);
            var popup = cut.FindComponent<Popup>();
            await cut.InvokeAsync(() => cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == " ").Instance.ProcessAsync(null, null, null));
            Assert.That(popup.Instance.IsVisible, Is.True);
            var esc = popup.FindComponents<KeyAction>().Where(k => k.Instance.Key == "Escape").ToList();
            Assert.That(esc, Has.Count.EqualTo(1), "the popup takes Esc");
            Assert.That(esc[0].Instance.BindAncestorWithTag, Is.EqualTo("dialog"), "on the popup, before the key leaves it");
            await cut.InvokeAsync(() => esc[0].Instance.ProcessAsync(null, null, null));
            Assert.That(popup.Instance.IsVisible, Is.False);
        }
    }

    [Test]
    public void ValidFieldRendersNothingTest()
    {
        var (ctx, editContext, _, model) = Setup();
        using (ctx)
        {
            var cut = Render(ctx, editContext, model);
            Assert.That(cut.FindAll("i.message-icon"), Is.Empty);
        }
    }

    /// <summary>A field that turns valid while its message is open takes the icon and the popup away.</summary>
    [Test]
    public async Task TurningValidWhileOpenRemovesTheMessageTest()
    {
        var (ctx, editContext, store, model) = Setup();
        using (ctx)
        {
            store.Add(() => model.Email!, "Required");
            editContext.NotifyValidationStateChanged();
            var cut = Render(ctx, editContext, model);
            Popup popup = cut.FindComponent<Popup>().Instance;
            await cut.InvokeAsync(() => cut.FindComponents<KeyAction>().Single(k => k.Instance.Key == " ").Instance.ProcessAsync(null, null, null));
            Assert.That(popup.IsVisible, Is.True);

            store.Clear();
            await cut.InvokeAsync(editContext.NotifyValidationStateChanged);
            Assert.Multiple(() =>
            {
                Assert.That(cut.FindAll("i.message-icon"), Is.Empty);
                Assert.That(cut.FindAll("dialog.message-popup"), Is.Empty);
                Assert.That(popup.IsVisible, Is.False);
            });
        }
    }

    private sealed class CountingMessage : PopupValidationMessage<string?>
    {
        public int RenderRequests;

        protected override bool ShouldRender()
        {
            RenderRequests++;
            return base.ShouldRender();
        }
    }

    /// <summary>A message removed from the form stops listening to its edit context, which outlives it.</summary>
    [Test]
    public async Task ADisposedMessageNoLongerListensTest()
    {
        var (ctx, editContext, store, model) = Setup();
        using (ctx)
        {
            var cut = ctx.Render<CascadingValue<EditContext>>(ps => ps
                .Add(c => c.Value, editContext)
                .AddChildContent<CountingMessage>(cp => cp.Add(p => p.For, () => model.Email)));
            var message = cut.FindComponent<CountingMessage>().Instance;
            await ctx.DisposeComponentsAsync();
            message.RenderRequests = 0;

            store.Add(() => model.Email!, "Required");
            await cut.InvokeAsync(editContext.NotifyValidationStateChanged);
            Assert.That(message.RenderRequests, Is.Zero);
        }
    }
}
