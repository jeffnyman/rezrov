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
    /// Every file that may carry resources, across both machines: the
    /// Blorb files beside the Z-code stories and the packaged Glulx
    /// games, sorted, or empty if the submodule is not populated.
    /// </summary>
    /// <remarks>
    /// Most of the corpus's pictures and sounds are in the Glulx games
    /// rather than the Z-code ones, so a test about resources wants
    /// this rather than <see cref="BlorbFiles"/>.
    /// </remarks>
    public static List<string> ResourceFiles()
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            return [];
        }

        var files = new List<string>();

        foreach (var directory in StoryDirectories.Concat(GlulxDirectories))
        {
            var path = Path.Combine(root, "entharion", directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(path)
                .Where(f => Path.GetExtension(f) is ".blb" or ".blorb" or ".zblorb" or ".gblorb"));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// The directories that hold Glulx files, relative to the submodule
    /// root: the games, and the conformance tests.
    /// </summary>
    public static readonly string[] GlulxDirectories =
    [
        "glulx-code",
        "glulx-checkers",
    ];

    /// <summary>
    /// Every Glulx file across the corpus, bare .ulx and packaged
    /// .gblorb alike, sorted, or empty if the submodule is not
    /// populated.
    /// </summary>
    public static List<string> GlulxFiles()
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            return [];
        }

        var files = new List<string>();

        foreach (var directory in GlulxDirectories)
        {
            var path = Path.Combine(root, "entharion", directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(path).Where(f => Path.GetExtension(f) is ".ulx" or ".gblorb"));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// The Glulx game in a corpus file: the file itself for a .ulx, and
    /// [blorb 5] the GLUL executable chunk of a .gblorb.
    /// </summary>
    public static byte[] GlulxImage(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (Path.GetExtension(file) != ".gblorb")
        {
            return bytes;
        }

        var executable = Rezrov.Core.Blorb.BlorbFile.Read(bytes).Executable;
        Assert.NotNull(executable);
        Assert.Equal("GLUL", executable.ChunkType);
        return executable.Data.ToArray();
    }

    /// <summary>
    /// Every Arcturus resource file in the corpus, sorted, or empty if
    /// the submodule is not populated. These carry the arc_image
    /// pictures, either beside a story or wrapped around one.
    /// </summary>
    public static List<string> ArcturusPacks()
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "entharion", "arcturus-code");

        return path is not null && Directory.Exists(path)
            ? Directory.EnumerateFiles(path)
                .Where(f => Path.GetExtension(f) is ".blorb" or ".zblorb")
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
    }

    /// <summary>
    /// Every Infocom graphics file in the corpus, sorted, or empty if
    /// the submodule is not populated. These are the artwork the DOS
    /// releases carried beside a Version 6 story, kept by the graphics
    /// standard they were drawn for.
    /// </summary>
    public static List<string> InfocomGraphics()
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "entharion", "infocom-graphics");

        return path is not null && Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToUpperInvariant() is ".CG1" or ".EG1" or ".EG2" or ".MG1")
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
    }

    /// <summary>
    /// One named Infocom graphics file, or null if the submodule is not
    /// populated.
    /// </summary>
    public static string? InfocomGraphics(string name) =>
        InfocomGraphics().FirstOrDefault(f => Path.GetFileName(f) == name);

    /// <summary>
    /// One named Arcturus resource file, or null if the submodule is
    /// not populated.
    /// </summary>
    public static string? ArcturusPack(string name) =>
        ArcturusPacks().FirstOrDefault(f => Path.GetFileName(f) == name);

    /// <summary>
    /// One named Arcturus story, or null if the submodule is not
    /// populated. These are ordinary Z-machine files: the pictures ride
    /// in a resource file beside them.
    /// </summary>
    public static string? ArcturusStory(string name)
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "entharion", "arcturus-code", name);

        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Every Aa-machine story in the corpus, which is what Dialog
    /// compiles to when it is not compiling to the Z-Machine, sorted,
    /// or empty if the submodule is not populated.
    /// </summary>
    public static List<string> AaStoryFiles()
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "entharion", "dialog-code");

        return path is not null && Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.aastory").Order(StringComparer.Ordinal).ToList()
            : [];
    }

    /// <summary>
    /// One named Aa-machine story, or null if the submodule is not
    /// populated.
    /// </summary>
    public static string? AaStoryFile(string name) =>
        AaStoryFiles().FirstOrDefault(f => Path.GetFileName(f) == name);

    /// <summary>
    /// One story from the Aa-machine's own conformance suite, or null
    /// if the submodule is not populated.
    /// </summary>
    /// <remarks>
    /// These are written to be read wrongly: they ask for things no
    /// frontend has, in units nothing measures in, so that an
    /// interpreter can be caught answering rather than declining. The
    /// published games ask for far less.
    /// </remarks>
    public static string? AaConformanceFile(string name)
    {
        var root = FindRepositoryRoot();
        var path = root is null
            ? null
            : Path.Combine(root, "entharion", "vendor", "aamachine", "test", name, $"{name}.aastory");

        return path is not null && File.Exists(path) ? path : null;
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
    public static string? FindRepositoryRoot()
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
