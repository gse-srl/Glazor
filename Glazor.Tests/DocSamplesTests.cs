using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Glazor.Tests;

/// <summary>
/// The code samples of the README, the docs and the skill against the library as it is: a member, an override or a named
/// argument a C# sample takes from a Glazor type, and a parameter a Razor sample sets on a Glazor component, must exist.
/// A sample's own names (its rows, its context, its fields) are not checked, as they are declared elsewhere or not at all.
/// </summary>
public class DocSamplesTests
{
    private static readonly string[] GlazorAssemblies = ["Glazor", "Glazor.EntityFrameworkCore"];

    // A member, an override or an argument that is not there: the drift a renamed or removed API leaves in a sample
    private static readonly HashSet<string> DriftCodes = ["CS0117", "CS1061", "CS0115", "CS1739", "CS1501", "CS7036", "CS0234", "CS0122", "CS0305", "CS0308", "CS0534", "CS0535", "CS0506", "CS0507", "CS0508", "CS0200"];

    private const string Usings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Linq.Expressions;
        global using System.Text.Json;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.AspNetCore.Components;
        global using Microsoft.EntityFrameworkCore;
        global using Microsoft.Extensions.Logging;
        global using Glazor.Grds;
        global using Glazor.Grds.Decs;
        global using Glazor.Grds.Filters;
        global using Glazor.Grds.Mdls;
        global using Glazor.Grds.Props;
        global using Glazor.Misc;
        global using Glazor.Tabs;
        global using Glazor.EntityFrameworkCore;
        """;

    private static readonly Lazy<List<MetadataReference>> References = new(() =>
    {
        _ = typeof(EFDataGridModel<,>);   // both packages loaded, so their files are in the output
        IEnumerable<string> platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<string> output = Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        return platform.Concat(output)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Where(IsAssembly)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    });

    private static bool IsAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public static IEnumerable<string> Documents()
    {
        string root = RepoRoot.Dir;
        return new[] { Path.Combine(root, "README.md"), Path.Combine(root, "Glazor.EntityFrameworkCore", "README.md") }
            .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "*.md"))
            .Concat(Directory.GetFiles(Path.Combine(root, ".claude", "skills", "glazor"), "*.md", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order();
    }

    private sealed record Fence(string Language, string Code, int Line);

    private static List<Fence> Fences(string document)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot.Dir, document));
        List<Fence> fences = [];
        for (int i = 0; i < lines.Length; i++)
        {
            // a fence may sit inside a blockquote, its lines prefixed with '>'
            Match open = Regex.Match(lines[i], @"^\s*(?:>\s?)?```(\w+)\s*$");
            if (!open.Success)
                continue;
            int start = i + 1;
            while (++i < lines.Length && !Regex.IsMatch(lines[i], @"^\s*(?:>\s?)?```\s*$"))
            {
            }
            fences.Add(new Fence(open.Groups[1].Value, string.Join("\n", lines[start..Math.Min(i, lines.Length)]
                .Select(l => Regex.Replace(l, @"^\s*>\s?", ""))), start + 1));
        }
        return fences;
    }

    [TestCaseSource(nameof(Documents))]
    public void CSharpSamplesUseExistingMembersTest(string document)
    {
        List<(SyntaxTree Tree, int Line)> samples = [];
        foreach (Fence fence in Fences(document))
        {
            if (fence.Language == "csharp")
                samples.Add((Wrap(fence.Code, samples.Count, component: false), fence.Line));
            else if (fence.Language == "razor")
                foreach ((string code, int offset) in CodeBlocks(fence.Code))
                    samples.Add((Wrap(code, samples.Count, component: true), fence.Line + offset));
        }
        CSharpCompilation compilation = CSharpCompilation.Create("DocSamples",
            samples.Select(s => s.Tree).Append(CSharpSyntaxTree.ParseText(Usings)), References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        List<string> drift = [];
        foreach ((SyntaxTree tree, int line) in samples)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (Diagnostic d in model.GetDiagnostics().Where(d => DriftCodes.Contains(d.Id) && OnGlazor(d, model)))
                drift.Add($"{document}:{line + d.Location.GetLineSpan().StartLinePosition.Line - 3} {d.Id} {d.GetMessage()}");
        }
        Assert.That(drift, Is.Empty);
    }

    // Three lines precede the sample in each wrapping, so a diagnostic's line maps back to the document
    private static SyntaxTree Wrap(string code, int n, bool component)
    {
        List<string> usings = [];
        StringBuilder body = new();
        foreach (string line in code.Split('\n'))
        {
            if (Regex.IsMatch(line, @"^\s*using\s+[\w.]+\s*;") || Regex.IsMatch(line, @"^\s*namespace\s+[\w.]+\s*;"))
            {
                if (line.TrimStart().StartsWith("using"))
                    usings.Add(line.Trim());
                body.AppendLine();   // keeps the line count
            }
            else
                body.AppendLine(line);
        }
        string head = $"namespace Sample{n} {{ {string.Join(" ", usings)}\n";
        string[] variants = component
            ? [$"{head}partial class S : ComponentBase {{\n\n{body}}} }}"]
            : [$"{head}\n\n{body} }}", $"{head}partial class S {{\n\n{body}}} }}", $"{head}partial class S {{\nasync Task M() {{\n{body}}} }} }}"];
        return variants
            .Select(v => CSharpSyntaxTree.ParseText(v))
            .MinBy(t => t.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error))!;
    }

    // The @code blocks of a Razor sample, each with the line it starts on
    private static IEnumerable<(string Code, int Offset)> CodeBlocks(string razor)
    {
        foreach (Match m in Regex.Matches(razor, @"@code\s*\{"))
        {
            int depth = 1, i = m.Index + m.Length;
            for (; i < razor.Length && depth > 0; i++)
                depth += razor[i] switch { '{' => 1, '}' => -1, _ => 0 };
            string code = razor[(m.Index + m.Length)..Math.Max(m.Index + m.Length, i - 1)];
            yield return (code, razor[..(m.Index + m.Length)].Count(c => c == '\n'));
        }
    }

    // Whether the diagnostic is about a member of a Glazor type, not about a type or a name of the sample's own
    private static bool OnGlazor(Diagnostic d, SemanticModel model)
    {
        if (d.Id == "CS0234")
            return d.GetMessage().Contains("'Glazor");
        SyntaxNode? node = model.SyntaxTree.GetRoot().FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
        if (node == null)
            return false;
        if (d.Id is "CS0305" or "CS0308")   // a type given the wrong number of type arguments
            return model.GetSymbolInfo(node).CandidateSymbols.Append(model.GetSymbolInfo(node).Symbol).OfType<ITypeSymbol>().Any(IsGlazor);
        if (d.Id is "CS0534" or "CS0535")   // a sample's class missing a member that a Glazor base or interface added
            return node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault() is { } declaration
                && model.GetDeclaredSymbol(declaration) is INamedTypeSymbol declared
                && (BaseTypes(declared).Skip(1).Any(IsGlazor) || declared.AllInterfaces.Any(IsGlazor));
        if (d.Id is "CS0115" or "CS0506" or "CS0507" or "CS0508")   // an override that no longer matches the Glazor member
            return model.GetDeclaredSymbol(node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().First()) is { } member
                && BaseTypes(member.ContainingType).Any(IsGlazor);
        if (d.Id == "CS0200")   // a Glazor property that became read-only
            return model.GetSymbolInfo(node).CandidateSymbols.Append(model.GetSymbolInfo(node).Symbol)
                .OfType<IPropertySymbol>().Any(p => IsGlazor(p.ContainingType));
        if (d.Id is "CS1739" or "CS1501" or "CS7036")
        {
            SyntaxNode? call = node.AncestorsAndSelf().FirstOrDefault(n => n is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax
                or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax);
            if (call == null)
                return false;
            SymbolInfo info = model.GetSymbolInfo(call);
            return info.CandidateSymbols.Append(info.Symbol).Any(s => s != null && IsGlazor(s.ContainingType));
        }
        // CS0117, CS1061: the type the member was looked up in
        ITypeSymbol? owner = node.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == node || access.Name.Span.Contains(node.Span) =>
                model.GetTypeInfo(access.Expression).Type ?? model.GetSymbolInfo(access.Expression).Symbol as ITypeSymbol,
            AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax { Parent: { } created } } =>
                model.GetTypeInfo(created).Type,
            _ => null,
        };
        if (owner == null && node is MemberAccessExpressionSyntax self)
            owner = model.GetTypeInfo(self.Expression).Type ?? model.GetSymbolInfo(self.Expression).Symbol as ITypeSymbol;
        return owner != null && BaseTypes(owner).Any(IsGlazor);
    }

    private static IEnumerable<ITypeSymbol> BaseTypes(ITypeSymbol type)
    {
        for (ITypeSymbol? t = type; t != null; t = t.BaseType)
            yield return t;
    }

    private static bool IsGlazor(ITypeSymbol? type) =>
        type != null && type.TypeKind != TypeKind.Error && GlazorAssemblies.Contains(type.ContainingAssembly?.Name);

    private static readonly Lazy<Dictionary<string, Type>> Components = new(() =>
        new[] { typeof(Grigliaria<>).Assembly, typeof(EFDataGridModel<,>).Assembly }
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsNested)
            .ToDictionary(t => t.Name.Split('`')[0]));

    [TestCaseSource(nameof(Documents))]
    public void RazorSamplesSetExistingParametersTest(string document)
    {
        List<string> drift = [];
        foreach (Fence fence in Fences(document).Where(f => f.Language == "razor"))
        {
            string code = Regex.Replace(fence.Code, @"@\*.*?\*@", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
            foreach (Match tag in Regex.Matches(code, @"<([A-Z]\w*)\b"))
            {
                if (!Components.Value.TryGetValue(tag.Groups[1].Value, out Type? component))
                    continue;
                HashSet<string> accepted = new(StringComparer.OrdinalIgnoreCase) { "Context" };
                bool captureUnmatched = false;
                foreach (PropertyInfo p in component.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetCustomAttribute<ParameterAttribute>() is not { } parameter)
                        continue;
                    accepted.Add(p.Name);
                    captureUnmatched |= parameter.CaptureUnmatchedValues;
                }
                accepted.UnionWith(component.GetGenericArguments().Select(a => a.Name));
                int line = fence.Line + code[..tag.Index].Count(c => c == '\n');
                drift.AddRange(Attributes(code, tag.Index + tag.Length)
                    .Where(name => !name.StartsWith('@') && name.Trim('.', '…').Length > 0 && !accepted.Contains(name)
                        && (!captureUnmatched || char.IsUpper(name[0])))   // the unmatched values are HTML attributes, lower case
                    .Select(name => $"{document}:{line} <{tag.Groups[1].Value}> has no parameter {name}"));
            }
        }
        Assert.That(drift, Is.Empty);
    }

    // The attribute names of the tag whose name ends at start; a quoted value may hold quotes inside parentheses
    private static IEnumerable<string> Attributes(string razor, int start)
    {
        int i = start;
        while (i < razor.Length)
        {
            while (i < razor.Length && char.IsWhiteSpace(razor[i]))
                i++;
            if (i >= razor.Length || razor[i] == '>' || razor[i] == '/')
                yield break;
            int nameStart = i;
            while (i < razor.Length && !char.IsWhiteSpace(razor[i]) && razor[i] is not ('=' or '>' or '/'))
                i++;
            yield return razor[nameStart..i];
            if (i < razor.Length && razor[i] == '=')
            {
                i++;
                if (i < razor.Length && razor[i] is '"' or '\'')
                {
                    char quote = razor[i++];
                    int depth = 0;
                    for (; i < razor.Length && (razor[i] != quote || depth > 0); i++)
                        depth += razor[i] switch { '(' => 1, ')' => -1, _ => 0 };
                    i++;
                }
                else
                    while (i < razor.Length && !char.IsWhiteSpace(razor[i]) && razor[i] != '>')
                        i++;
            }
        }
    }
}
