namespace Rezrov.Tests;

/// <summary>
/// Finds the real story files in the entharion submodule, for tests that
/// want to run against them.
/// </summary>
/// <remarks>
/// The submodule is optional and CI does not fetch it, so every method
/// here returns an empty list rather than failing when it is absent.
/// Tests that use it skip on an empty list.
/// </remarks>
internal static class Corpus
{
    /// <summary>
    /// The directories that hold Z-code story files, relative to the
    /// submodule root.
    /// </summary>
    public static readonly string[] StoryDirectories =
    [
        "zcode-infocom",
        "zcode-inform",
        "zcode-checkers",
    ];

    /// <summary>
    /// Every Z-code story file across the corpus, sorted, or empty if the
    /// submodule is not populated.
    /// </summary>
    public static List<string> StoryFiles() => StoryFiles(StoryDirectories);

    /// <summary>
    /// The Z-code story files in the given submodule directories, sorted.
    /// </summary>
    public static List<string> StoryFiles(params string[] directories)
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            return [];
        }

        var files = new List<string>();

        foreach (var directory in directories)
        {
            var path = Path.Combine(root, "entharion", directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(path).Where(IsStoryFile));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Every Blorb file beside the Z-code stories, sorted, or empty if
    /// the submodule is not populated.
    /// </summary>
    public static List<string> BlorbFiles()
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            return [];
        }

        var files = new List<string>();

        foreach (var directory in StoryDirectories)
        {
            var path = Path.Combine(root, "entharion", directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(path).Where(f => Path.GetExtension(f) is ".blb" or ".blorb" or ".zblorb"));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// The simple-test fixtures: one tiny Inform program compiled once
    /// for each version, which prints one known sentence and quits.
    /// </summary>
    public static List<string> SimpleTestFixtures() =>
        StoryFiles("zcode-inform")
            .Where(f => Path.GetFileName(f).StartsWith("simple-test-", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// The Z-machine version a corpus file was compiled for, taken from
    /// its extension, which is how the corpus names them.
    /// </summary>
    public static int VersionFromExtension(string file) => file[^1] - '0';

    private static bool IsStoryFile(string file) =>
        Path.GetExtension(file) is ['.', 'z', >= '1' and <= '8'];

    /// <summary>
    /// Walks up from the test binary until it finds the directory holding
    /// the solution file. Tests run from deep inside bin/, and the corpus
    /// is relative to the repository root, not to the binary.
    /// </summary>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rezrov.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
