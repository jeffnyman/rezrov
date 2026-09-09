using Rezrov.ZMachine;

namespace Rezrov.Tests;

/// <summary>
/// Runs the header code over every real story file in the entharion
/// submodule. These are the checks that hand-built headers cannot give:
/// that the validation in the constructor accepts every file Infocom and
/// Inform ever shipped, and that the checksum arithmetic agrees with what
/// their compilers wrote.
/// </summary>
/// <remarks>
/// The submodule is optional, and CI does not fetch it, so every test here
/// skips rather than fails when it is absent. Locally, with the submodule
/// populated, they run against the full corpus.
/// </remarks>
public class StoryCorpusTests
{
    private static readonly string[] CorpusDirectories =
    [
        "zcode-infocom",
        "zcode-inform",
        "zcode-checkers",
    ];

    [Fact]
    public void EveryStoryFileHasAValidHeader()
    {
        var files = StoryFiles();
        Assert.SkipUnless(files.Count > 0, "The entharion submodule is not populated.");

        var failures = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var header = new StoryHeader(new ZMemory(File.ReadAllBytes(file)));

                // The extension on these files is the version, so the two
                // had better agree.
                var expected = file[^1] - '0';
                if ((int)header.Version != expected)
                {
                    failures.Add($"{Path.GetFileName(file)}: header says Version {(int)header.Version}");
                }
            }
            catch (InvalidDataException e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryStoryFileWithAChecksumVerifies()
    {
        var files = StoryFiles();
        Assert.SkipUnless(files.Count > 0, "The entharion submodule is not populated.");

        var failures = new List<string>();
        var verified = 0;

        foreach (var file in files)
        {
            var header = new StoryHeader(new ZMemory(File.ReadAllBytes(file)));

            // [zm 11.1] Early Version 3 files have nothing to verify.
            if (!header.HasFileLength)
            {
                continue;
            }

            // A declared length with a zero checksum means the compiler
            // wrote the one and not the other. Inform 1 did this in 1993,
            // and dejavu-r1-s930921.z3 is the example in this corpus. Such
            // a file fails verification honestly, so it is not a failure
            // of the checksum code and is left out of the count.
            if (header.Checksum == 0)
            {
                continue;
            }

            if (header.VerifyChecksum())
            {
                verified++;
            }
            else
            {
                failures.Add(
                    $"{Path.GetFileName(file)}: header {header.Checksum:X4}, computed {header.ComputeChecksum():X4}");
            }
        }

        Assert.Empty(failures);
        Assert.True(verified > 0, "No file in the corpus carried a checksum, which cannot be right.");
    }

    /// <summary>
    /// Finds every Z-code story file in the corpus, or returns an empty
    /// list if the submodule is not there.
    /// </summary>
    private static List<string> StoryFiles()
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            return [];
        }

        var files = new List<string>();

        foreach (var directory in CorpusDirectories)
        {
            var path = Path.Combine(root, "entharion", directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            files.AddRange(
                Directory.EnumerateFiles(path)
                    .Where(f => Path.GetExtension(f) is [ '.', 'z', >= '1' and <= '8' ]));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

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
