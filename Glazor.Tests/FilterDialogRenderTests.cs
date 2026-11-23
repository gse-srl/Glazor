using Bunit;
using Glazor.Grds.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glazor.Tests;

/// <summary>
/// The filter dialogs through their renderers. The set dialogs put a MultiSelGrid over the definition's values model, so
/// what the user sees depends on that model registering a column: the wrapper contributes the selection column only.
/// </summary>
public class FilterDialogRenderTests
{
    /// <summary>
    /// SetDef's values model registered no column, so its dialog was a list of unlabelled checkboxes.
    /// </summary>
    [Test]
    public void SetDefDialogShowsTheValuesTest()
    {
        using BunitContext ctx = NewContext();
        SetDef<string> def = new(true) { AllValues = ["alpha", "beta", "gamma"] };
        var cut = ctx.Render<DynaSetDefRenderer<SetDef<string>, string, string>>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 4);   // include-null, select-all, one per value
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("alpha").And.Contain("beta").And.Contain("gamma"), "each row must show its value");
            Assert.That(cut.FindAll("input[type=checkbox]"), Has.Count.EqualTo(5));
        });
    }

    /// <summary>
    /// A set over a collection column lists the candidates its definition reads, every one checked when it starts full, and
    /// binds "Include null or empty" to the definition.
    /// </summary>
    [Test]
    public async Task DynaValuesInSetDefDialogShowsTheCandidatesTest()
    {
        using BunitContext ctx = NewContext();
        DynaValuesInSetDefSubclassTests.ByCompanyRolesFDef def = new(startFull: true);
        var cut = ctx.Render<DynaValuesInSetDefRenderer<DynaValuesInSetDefSubclassTests.ByCompanyRolesFDef, DynaValuesInSetDefSubclassTests.CompanyRole, int>>(
            ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 5);   // include-null, select-all, one per candidate
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("admin").And.Contain("user").And.Contain("guest"));
            Assert.That(cut.FindAll("input[type=checkbox]").Select(i => i.HasAttribute("checked")), Is.All.True, "started full");
            Assert.That(def.Selected, Is.EquivalentTo(new[] { 1, 2, 3 }));
        });
        await cut.Find("input[id$=_include]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = false });
        Assert.That(def.IncludeNullOrEmpty, Is.False);
    }

    private enum Light { Red, Amber, Green }

    /// <summary>
    /// The editor of an enum column, every one's by default, lists one box per member, every one checked when the definition
    /// starts full, and binds the members and "Include null" to the definition.
    /// </summary>
    [Test]
    public async Task EnumDefDialogListsTheMembersTest()
    {
        using BunitContext ctx = NewContext();
        EnumDef def = new(typeof(Light), true);
        var cut = ctx.Render<EnumDefRenderer>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 5);   // include-null, select-all, one per member
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Red").And.Contain("Amber").And.Contain("Green"));
            Assert.That(cut.FindAll("input[type=checkbox]"), Has.Count.EqualTo(5));
        });
        await cut.FindAll("tr").Single(tr => tr.TextContent.Contains("Amber") && tr.QuerySelector("tr") == null).QuerySelector("input[type=checkbox]")!
            .ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = false });
        await cut.Find("input[id$=_include]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = false });
        Assert.Multiple(() =>
        {
            Assert.That(def.Selected, Is.EquivalentTo(new Enum[] { Light.Red, Light.Green }));
            Assert.That(def.IncludeNull, Is.False);
        });
    }

    /// <summary>A set over a collection whose dialog has rows of their own lists those rows, with the columns its definition gives.</summary>
    [Test]
    public void ADynaValuesInSetDefDialogShowsItsOwnRowsTest()
    {
        using BunitContext ctx = NewContext();
        DynaValuesInSetDefSubclassTests.TagsFDef def = new(startFull: true);
        var cut = ctx.Render<DynaValuesInSetDefSubclassTests.TagsFDefRenderer>(ps => ps.Add(r => r.FilterDef, def));
        cut.WaitForState(() => cut.FindAll("input[type=checkbox]").Count >= 5);   // include-null, select-all, one per row
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("red").And.Contain("green").And.Contain("blue"));
            Assert.That(cut.Markup, Does.Contain("Tag"), "the column the definition gives");
            Assert.That(def.Selected, Is.EquivalentTo(new[] { 1, 2, 3 }));
        });
    }

    private static IEnumerable<string> DateInputTypes<VT>(BunitContext ctx, DateDef<VT> def) where VT : struct, IComparable<VT>
    {
        var cut = ctx.Render<DateDefRenderer<VT>>(ps => ps.Add(r => r.FilterDef, def));
        return cut.FindAll("input").Select(i => i.GetAttribute("type")!).Where(t => t != "checkbox").ToList();
    }

    /// <summary>
    /// A range over a value type that carries a time is edited to the instant, so the bound the user picks is the bound applied;
    /// a date-only or time-only type keeps its own picker.
    /// </summary>
    [Test]
    public void DateEditorMatchesTheValueTypeTest()
    {
        using BunitContext ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(DateInputTypes(ctx, new DateDef<DateTime>()), Is.EqualTo(new[] { "datetime-local", "datetime-local" }));
            Assert.That(DateInputTypes(ctx, new DateDef<DateTimeOffset>()), Is.EqualTo(new[] { "datetime-local", "datetime-local" }));
            Assert.That(DateInputTypes(ctx, new DateDef<DateOnly>()), Is.EqualTo(new[] { "date", "date" }));
            Assert.That(DateInputTypes(ctx, new DateDef<TimeOnly>()), Is.EqualTo(new[] { "time", "time" }));
        });
    }

    private sealed class DayDef : DateDef<DateTime>
    {
        protected internal override Microsoft.AspNetCore.Components.Forms.InputDateType GetInputDateType()
            => Microsoft.AspNetCore.Components.Forms.InputDateType.Date;
    }

    private sealed class PercentRenderer : AFilterDefRenderer<NumberDef<decimal>>
    {
        protected internal override ValueTask ShownAsync() => ValueTask.CompletedTask;
    }

    private sealed class PercentDef : NumberDef<decimal>
    {
        public override FilterDefRendererType Renderer() => new FilterDefRendererType<PercentRenderer>();
    }

    /// <summary>
    /// A subclass of a built-in definition changes its editor: a DateTime column holding dates only gets the date picker, and a
    /// definition can name an editor of its own.
    /// </summary>
    [Test]
    public void ASubclassOfABuiltInDefinitionChoosesItsEditorTest()
    {
        using BunitContext ctx = NewContext();
        Assert.Multiple(() =>
        {
            Assert.That(DateInputTypes(ctx, new DayDef()), Is.EqualTo(new[] { "date", "date" }));
            Assert.That(new PercentDef().Renderer().RendererType, Is.EqualTo(typeof(PercentRenderer)));
        });
    }
}
