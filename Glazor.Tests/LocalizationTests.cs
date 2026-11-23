using Bunit;
using Glazor.Grds;
using Glazor.Grds.Filters;
using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Glazor.Tests;

/// <summary>
/// The texts the components show come from the resources for the current UI culture, English by default and Italian, unless
/// the host registers its own <see cref="IGlazorTexts"/>; every key exists in both resource files and in <see cref="TextKeys"/>.
/// </summary>
public class LocalizationTests
{
    private enum Status { On, Off }

    private sealed record Product(string Code, string Name, Status State);

    private sealed class PrefixedTexts : IGlazorTexts
    {
        public string Get(string key) => "X-" + key;
    }

    private sealed class DialogGrid : Grigliaria<Product>
    {
        public Task OpenLayoutAsync() => OnLayoutAsync();
    }

    private sealed class Form
    {
        public string? Email { get; set; }
    }

    private static DataGridModel<Product> ModelOf()
    {
        DataGridModel<Product> model = new([new("C1", "N1", Status.On), new("C2", "N2", Status.Off)]);
        model.AddColumn("Code", new Column<Product, string>("Code") { PreferredSize = 10 }, p => p.Code);
        model.AddColumn("Name", new Column<Product, string>("Name") { PreferredSize = 10, FilterDef = new SetDef<string>(true) { AllValues = ["N1", "N2"] } }, p => p.Name);
        model.AddColumn("State", new Column<Product, Status>("State") { PreferredSize = 10 }, p => p.State);
        return model;
    }

    private static IRenderedComponent<DialogGrid> RenderGrid(BunitContext ctx)
        => ctx.Render<DialogGrid>(ps => ps.Add(g => g.Model, ModelOf()).Add(g => g.Spinner, new NoSpinner()).Add(g => g.ShowNavigationButtons, true).Add(g => g.Layoutable, true));

    /// <summary>Renders one of each localized component and collects the texts they show, keyed by what they are.</summary>
    private static async Task<Dictionary<string, string?>> CollectAsync(BunitContext ctx)
    {
        Dictionary<string, string?> texts = [];
        var grid = RenderGrid(ctx);
        texts["footer"] = grid.Find("button.btnFirstRow").GetAttribute("title");
        await grid.InvokeAsync(() => grid.Instance.ShowFilterDialogAsync("State", null));
        texts["enumHeader"] = grid.FindComponent<FilterDefDlg>().FindAll("th .clamper").Select(c => c.TextContent.Trim()).FirstOrDefault(t => t.Length > 0);
        await grid.InvokeAsync(() => grid.Instance.ShowFilterDialogAsync("Name", null));
        texts["setHeader"] = grid.FindComponent<FilterDefDlg>().FindAll("th .clamper").Select(c => c.TextContent.Trim()).FirstOrDefault(t => t.Length > 0);
        await grid.InvokeAsync(() => grid.Instance.OpenLayoutAsync());
        var layout = grid.FindComponent<LayoutDefDlg<Product>>();
        texts["layoutLists"] = string.Join("|", layout.FindAll("table[aria-label]").Select(t => t.GetAttribute("aria-label")));
        texts["layoutHeaders"] = string.Join("|", layout.FindAll("div.content > table > tbody > tr > th").Select(th => th.TextContent.Trim()).Where(t => t.Length > 0).Distinct());
        texts["defaultLabel"] = ctx.Render<DefaultDefRenderer>(ps => ps.Add(r => r.FilterDef, new DefaultDef())).Find("label[for$=_include]").TextContent.Trim();
        texts["spinner"] = ctx.Render<SpinnerWheel>().Find("div.spinner").GetAttribute("aria-label");
        Form form = new();
        EditContext editContext = new(form);
        ValidationMessageStore store = new(editContext);
        store.Add(() => form.Email!, "Required");
        editContext.NotifyValidationStateChanged();
        var icon = ctx.Render<CascadingValue<EditContext>>(ps => ps.Add(c => c.Value, editContext)
            .AddChildContent<PopupValidationMessage<string?>>(cp => cp.Add(p => p.For, () => form.Email)));
        texts["validationIcon"] = icon.Find("i.message-icon").GetAttribute("aria-label");
        return texts;
    }

    private static async Task<T> UnderCultureAsync<T>(string name, Func<Task<T>> fn)
    {
        CultureInfo ui = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(name);
        try { return await fn(); }
        finally { CultureInfo.CurrentUICulture = ui; }
    }

    private static T UnderCulture<T>(string name, Func<T> fn) => UnderCultureAsync(name, () => Task.FromResult(fn())).GetAwaiter().GetResult();

    private static HashSet<string> ResxKeys(string file) =>
        XDocument.Load(Path.Combine(RepoRoot.Dir, "Glazor", "Resources", file)).Root!.Elements("data").Select(d => (string)d.Attribute("name")!).ToHashSet();

    [Test]
    public void EveryKeyIsInBothResourceFilesAndInTextKeysTest()
    {
        var english = ResxKeys("Texts.resx");
        var italian = ResxKeys("Texts.it.resx");
        var constants = typeof(TextKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        Assert.Multiple(() =>
        {
            Assert.That(english, Is.Not.Empty);
            Assert.That(italian, Is.EquivalentTo(english), "a text added in one language and not the other");
            Assert.That(constants, Is.EquivalentTo(english), "TextKeys and Texts.resx name the same keys");
        });
    }

    /// <summary>
    /// A literal in a title, alt, aria-label or placeholder, in markup or in a column label or title built in code, is a text the
    /// resources do not know: every one goes through <see cref="TextKeys"/>. Plain text nodes are not scanned; the key-parity
    /// test above and a review cover them.
    /// </summary>
    [Test]
    public void NoLiteralUiTextInTheMarkupTest()
    {
        string dir = Path.Combine(RepoRoot.Dir, "Glazor");
        Regex attribute = new("""(?:^|\s)(title|alt|aria-label|placeholder|Title|AriaLabel)=["'][A-Za-z0-9]""");
        Regex code = new("""new Column<[^>]*>\("[A-Za-z]|\b(Title|AriaLabel)\s*=\s*"[A-Za-z]""");
        var offenders = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs")) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.EndsWith("GlazorTexts.cs"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (line, n: i + 1))
                .Where(x => (f.EndsWith(".razor") ? attribute : code).IsMatch(x.line))
                .Select(x => $"{Path.GetRelativePath(dir, f)}:{x.n}"))
            .ToList();
        Assert.That(offenders, Is.Empty, "a literal where a TextKeys entry belongs");
    }

    [Test]
    public void EnglishByDefaultAndItalianUnderTheItalianCultureTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UnderCulture("en-US", () => GlazorTexts.Default.Get(TextKeys.IncludeNull)), Is.EqualTo("Include null"));
            Assert.That(UnderCulture("it-IT", () => GlazorTexts.Default.Get(TextKeys.IncludeNull)), Is.EqualTo("Includi null"), "it-IT falls back to it");
            Assert.That(UnderCulture("fr-FR", () => GlazorTexts.Default.Get(TextKeys.IncludeNull)), Is.EqualTo("Include null"), "a language without resources gets English");
            Assert.That(GlazorTexts.Default.Get("NoSuchKey"), Is.EqualTo("NoSuchKey"), "an unknown key shows itself rather than throwing");
        });
    }

    [Test]
    public async Task ComponentsRenderTheCultureTextsTest()
    {
        using BunitContext ctx = NewContext();
        var italian = await UnderCultureAsync("it-IT", () => CollectAsync(ctx));
        var english = await UnderCultureAsync("en-US", () => CollectAsync(ctx));
        Assert.Multiple(() =>
        {
            Assert.That(italian["footer"], Is.EqualTo("Prima riga"));
            Assert.That(italian["defaultLabel"], Is.EqualTo("Includi null:"));
            Assert.That(italian["enumHeader"], Is.EqualTo("Valore"), "the enum picker's column, through EnumModel's texts");
            Assert.That(italian["setHeader"], Is.EqualTo("Valore"), "the set picker's column, through the definition's Texts");
            Assert.That(italian["layoutLists"], Is.EqualTo("Colonne nascoste|Colonne visibili"));
            Assert.That(italian["layoutHeaders"], Is.EqualTo("Colonne nascoste|Colonne visibili"));
            Assert.That(italian["spinner"], Is.EqualTo("Caricamento"));
            Assert.That(italian["validationIcon"], Is.EqualTo("Mostra l'errore di validazione"));
            Assert.That(english["footer"], Is.EqualTo("First row"));
            Assert.That(english["enumHeader"], Is.EqualTo("Value"));
            Assert.That(english["validationIcon"], Is.EqualTo("Show the validation error"));
        });
    }

    [Test]
    public async Task HostServiceReplacesTheBuiltInTextsTest()
    {
        using BunitContext ctx = NewContext();
        ctx.Services.AddSingleton<IGlazorTexts>(new PrefixedTexts());
        var texts = await CollectAsync(ctx);
        Assert.Multiple(() =>
        {
            Assert.That(texts["footer"], Is.EqualTo("X-FirstRow"));
            Assert.That(texts["defaultLabel"], Is.EqualTo("X-IncludeNull:"));
            Assert.That(texts["enumHeader"], Is.EqualTo("X-Value"));
            Assert.That(texts["setHeader"], Is.EqualTo("X-Value"));
            Assert.That(texts["layoutLists"], Is.EqualTo("X-HiddenColumns|X-VisibleColumns"));
            Assert.That(texts["layoutHeaders"], Is.EqualTo("X-HiddenColumns|X-VisibleColumns"));
            Assert.That(texts["spinner"], Is.EqualTo("X-Loading"));
            Assert.That(texts["validationIcon"], Is.EqualTo("X-ShowValidationError"));
        });
    }
}
