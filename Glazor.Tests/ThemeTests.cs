using System.Text.RegularExpressions;

namespace Glazor.Tests;

/// <summary>
/// A theme is a set of --glazor-* custom properties: glazor.css declares every one with its light default, or each use falls back to one, the stylesheets
/// use nothing but them for colours and icons, and glazor-dark.css redefines a subset. No image is left for a theme to miss.
/// </summary>
public class ThemeTests
{
    private static readonly string Lib = Path.Combine(RepoRoot.Dir, "Glazor");

    private static bool InBuildOutput(string f) =>
        f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar);

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Lib, pattern, SearchOption.AllDirectories).Where(f => !InBuildOutput(f));

    private static string Css(string name) => File.ReadAllText(Path.Combine(Lib, "wwwroot", name));

    private static HashSet<string> Declared(string css) =>
        Regex.Matches(css, @"^\s*(--glazor-[a-z0-9-]+)\s*:", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet();

    /// <summary>The stylesheet without its :root blocks and its comments: the part that must use the properties.</summary>
    private static string Rules(string css) =>
        Regex.Replace(Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline), @":root\s*\{.*?\}", "", RegexOptions.Singleline);

    [Test]
    public void EveryPropertyUsedIsDeclaredWithItsDefaultTest()
    {
        var declared = Declared(Css("glazor.css"));
        var uses = Files("*.css").Concat(Files("*.razor")).SelectMany(f => Regex.Matches(File.ReadAllText(f), @"var\((--glazor-[a-z0-9-]+)\s*(,?)")).ToList();
        var used = uses.Select(m => m.Groups[1].Value).ToHashSet();
        // a use with a fallback carries its default, resolved where it is used: the editors' properties follow an ancestor's theme so
        var usedWithoutFallback = uses.Where(m => m.Groups[2].Value == "").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Multiple(() =>
        {
            Assert.That(declared, Is.Not.Empty);
            Assert.That(usedWithoutFallback.Except(declared), Is.Empty, "a property used without a fallback and declared nowhere");
            Assert.That(declared.Except(used).Where(d => !d.StartsWith("--glazor-icon-")), Is.Empty, "a property declared and used nowhere");
        });
    }

    [TestCase("glazor-dark.css")]
    [TestCase("glazor-aurora.css")]
    [TestCase("glazor-paper.css")]
    public void AThemeRedefinesDeclaredPropertiesOnlyTest(string theme)
    {
        var declared = Declared(Css("glazor.css"));
        var redefined = Declared(Css(theme));
        Assert.Multiple(() =>
        {
            Assert.That(redefined, Is.Not.Empty);
            Assert.That(redefined.Except(declared), Is.Empty, "a value for a property the light theme does not declare");
        });
    }

    // Aurora and Paper add a few rules of their own to the header
    [Test]
    public void TheDarkThemeIsPropertiesOnlyTest()
    {
        Assert.That(Rules(Css("glazor-dark.css")).Trim(), Is.Empty);
    }

    [Test]
    public void NoColourOrImageOutsideThePropertiesTest()
    {
        Regex colour = new(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|(?<![-\w])(white|black|red|yellow|azure|orange|blue|green|gray|grey|darkgray|lightgray)(?![-\w])", RegexOptions.IgnoreCase);
        var offenders = Files("*.css")
            .SelectMany(f => Rules(File.ReadAllText(f)).Split('\n').Select((line, i) => (line, i + 1)).Where(x => colour.IsMatch(x.line) || x.line.Contains("url(")).Select(x => $"{Path.GetFileName(f)}: {x.line.Trim()}"))
            .ToList();
        Assert.That(offenders, Is.Empty, "a colour or an image a theme cannot reach");
    }

    [Test]
    public void NoImageInTheMarkupAndEveryIconHasItsRuleTest()
    {
        var razor = Files("*.razor").ToList();
        var images = razor.Where(f => File.ReadAllText(f).Contains("<img")).Select(Path.GetFileName).ToList();
        // the class attribute's own words, plus the quoted ones inside its razor expressions
        const string expression = @"@\((?:[^()]|\([^()]*\))*\)";
        var iconClasses = razor.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"class=""glazor icon ((?:[^""@]|" + expression + @")*)""").Select(m => m.Groups[1].Value))
            .SelectMany(v => Regex.Matches(v, @"""([a-z-]+)""").Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(Regex.Replace(v, expression, " "), @"\b[a-z]+(?:-[a-z]+)*\b").Select(m => m.Value)))
            .Where(c => c.Length > 0 && c != "accent").ToHashSet();
        var rules = Regex.Matches(Css("glazor.css"), @"\.glazor\.icon\.([a-z-]+)\s*\{").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Multiple(() =>
        {
            Assert.That(images, Is.Empty, "an image a theme cannot recolour");
            Assert.That(iconClasses, Is.Not.Empty);
            Assert.That(iconClasses.Except(rules), Is.Empty, "an icon class with no mask rule");
            Assert.That(Directory.Exists(Path.Combine(Lib, "wwwroot", "images")), Is.False, "the image folder is gone with the images");
        });
    }
}
