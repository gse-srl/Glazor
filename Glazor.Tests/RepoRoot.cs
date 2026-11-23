namespace Glazor.Tests;

/// <summary>
/// The directory holding the solution, wherever the build put the test assembly (an artifacts output too): the tests that
/// read a source file or run a Node probe start from it.
/// </summary>
internal static class RepoRoot
{
    public static readonly string Dir = Find();

    private static string Find()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Glazor.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException($"Glazor.slnx not found above {AppContext.BaseDirectory}");
    }
}
