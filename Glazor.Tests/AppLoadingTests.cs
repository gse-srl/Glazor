using Bunit;
using Glazor.Misc;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// AppLoading shows the wheel until the layout's Spinner is in the page: glazor.css hides it under the dialog the Spinner
/// renders, closed too.
/// </summary>
public class AppLoadingTests
{
    [Test]
    public void AppLoadingGoesOnceTheSpinnerIsInThePageTest()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLogging();
        string css = File.ReadAllText(Path.Combine(RepoRoot.Dir, "Glazor", "wwwroot", "glazor.css"));
        Assert.Multiple(() =>
        {
            Assert.That(ctx.Render<AppLoading>().Find(".glazor.app-loading div.spinner").GetAttribute("role"), Is.EqualTo("status"));
            Assert.That(ctx.Render<Spinner>().FindAll("dialog.glazor.spinner-container"), Has.Count.EqualTo(1), "the dialog the rule waits for");
            Assert.That(css, Does.Match(@"body:has\(dialog\.glazor\.spinner-container\)\s+\.glazor\.app-loading\s*\{\s*display:\s*none;"));
        });
    }
}
