# Rezrov

_An Interactive Fiction Interpreter_

Rezrov is an interpreter for interactive fiction written in C#. The most common and obvious formats there are the Z-Machine and Glulx. The plan is for the interpreter core to be a UI-agnostic library, and the command line, terminal, and graphical frontends are going to be thin shells over that one engine.

## Installing

Each release on the [releases page](https://github.com/jeffnyman/rezrov/releases) carries one archive per platform, named for the version and the platform: `rezrov-0.1.0-win-x64.zip`, `rezrov-0.1.0-linux-x64.tar.gz`, `rezrov-0.1.0-linux-arm64.tar.gz`, and `rezrov-0.1.0-osx-universal.tar.gz`, the last a universal binary that runs natively on both Intel and Apple silicon Macs. Inside are the two programs, `rezrov` and `rezrov-tui`, as native executables that need nothing installed beside them, not even .NET. Unpack the archive somewhere on your path and they are ready; `rezrov --version` says which release you have. A `SHA256SUMS` file beside the archives lets you check a download.

Two platform notes. On macOS the executables are not signed, so the first run is refused with a message about an unidentified developer; allow it in System Settings under Privacy and Security, or clear the quarantine mark with `xattr -d com.apple.quarantine rezrov rezrov-tui`. On Linux, `tar` keeps the executable permission, but if a download loses it, `chmod +x rezrov rezrov-tui` restores it.

## Using

Rezrov comes as two programs over one interpreter. `rezrov` is the command line program: give it a story file and it tells you about the file; add `--run` and it plays the game on the console as a plain stream of text. `rezrov-tui` is the terminal program: give it a story file and it plays the game full screen, with the status line, the upper window, styles, and colors that the console cannot draw.

```sh
dotnet run --project src/Rezrov.Cli -- entharion/zcode-infocom/zork1-r88-s840726.z3
dotnet run --project src/Rezrov.Cli -- entharion/zcode-infocom/zork1-r88-s840726.z3 --run
```

The `--` separates arguments meant for `dotnet run` from arguments meant for Rezrov. A native binary, described further down, drops all of that and is just `rezrov <story-file>`.

Without `--run`, Rezrov reads the file and describes it. For a Z-Machine file that is the version, release, and serial number, where dynamic and high memory begin, whether the checksum matches, how many objects and dictionary words there are, and the first instruction the machine would execute. For a Glulx file it is the specification version the file was written to, the Inform compiler version, release, and serial number when Inform made it, the memory map, and whether the checksum matches. Given a Blorb file it lists the resources inside, which game they belong to, and, when the file packages a game of its own, describes that game too.

With `--run`, the game's text goes to standard output and commands are read from the console. What the console can show is plain text, so the upper window and the status line of a Version 3 game are kept in the model but not drawn, styles and colors are not shown, timed input is not available, and a bleep is the terminal bell. The game is told all of this through the header, so a game that checks before using a feature behaves itself. Everything else the Z-Machine standard describes is there: the parser gets your commands, the transcript and command recording streams work, saved games are written in Quetzal format, undo works, and sound effects are found in a Blorb file and handed to the frontend, which on the console can only ring the bell for them. Version 6 games run too, on the eight-window screen model of the standard's section 8.8, but without pictures: the game is told there are none, and Infocom's Version 6 games answer by using their text-only modes. On the console their several windows come out as one stream of text, which reads roughly; the terminal program below shows them properly. Glulx files load and run as far as that machine goes so far, which is the whole instruction set apart from accelerated functions, floating point and the memory heap included, and the Glk library's windows, streams, and text output. On the console a Glulx game's text buffer windows come out as one stream of text, its text grid windows are kept but not shown, and commands are read from the console, or from a `--commands` file first. A request for a single key takes a line and uses its first character, or the enter key for an empty line. Timer events work when the game is waiting on nothing else. Undo works, and saved games, transcripts, and the data files a game keeps go to disk through Glk's file references: a file the game names lives beside the game file with the suffix the Glk specification recommends, a file the game asks the player for is asked about on standard error and answered on the console, or from the `--commands` file, and `--save`, `--transcript`, and `--record` name those files up front so a scripted run never asks. Saved games are in the Glulx variant of Quetzal. A game packaged in a Blorb file can read the data chunks packaged with it through resource streams, and a game that asks the time gets the system clock in universal or local time. Graphics and sound are not there yet, so a game that reaches for one of them stops with a message naming the Glk function it needed.

The options, all of which imply `--run`:

- `--commands <file>` plays commands from the file, one per line, before handing the game to the console. This is the same format the Z-Machine writes to its command recording stream and that Frotz records and replays, so a session recorded by either can be played back by the other. Once the file runs out, the console takes over.
- `--transcript <file>`, `--record <file>`, and `--save <file>` name the files to use when the game turns on a transcript, starts recording commands, or saves and restores, so that nothing has to be typed at a prompt. Without them Rezrov asks on standard error, which keeps the question out of anything you are capturing from standard output.
- `--blorb <file>` names the resource file that holds the game's sounds. Without it, a file beside the story with the same name and a `.blb`, `.blorb`, or `.zblorb` extension is used, which is how the Infocom sound files are distributed. A `.zblorb` that packages its own game can be given as the story file directly.
- `--seed <number>` seeds the game's random number generator, so the same commands produce the same play every time, which is what a test wants. The game still sees ordinary dice, just the same dice every session; this is not the predictable state the standard describes, which is a testing mode a game enters for itself with rolls of 1, 2, 3, and which is still there for games that use it. A game that asks to be reseeded at random partway through gets a fresh point on the seeded stream instead, so a seeded session stays repeatable to the end. The generator is Rezrov's own rather than the runtime's, so a seed produces the same session forever and no recording is invalidated by a .NET upgrade.
- `--interpreter <machine>` tells the game which of Infocom's machines it is running on, by name or by the number the standard's section 11.1.3 gives: `dec20`, `apple2e`, `macintosh`, `amiga`, `atarist`, `ibmpc`, `c128`, `c64`, `apple2c`, `apple2gs`, or `tandy`. Left alone, Rezrov says IBM PC, or DEC-20 for a Version 6 game on a screen without pictures, which is the pair that suits the games best. Some games behave differently by machine: Beyond Zork decides whether to draw its map with the character graphics font by it, and the Version 6 games lay their screens out by it. Under the Amiga number an Infocom Version 6 game gets the Amiga's behavior of one pair of colors for the whole screen, as the standard's section 8.3 requires.
- `--trace` writes every instruction to standard error before it runs, which is the quickest way to find out how a game got somewhere.

Standard input works too. When it is a pipe or a file rather than a terminal, each command is echoed to standard output as it is consumed, so the output still reads as a session:

```sh
rezrov entharion/zcode-infocom/zork1-r88-s840726.z3 --run < commands.txt
```

When a game does something the standard says it must not, such as treating object 0 as an object, Rezrov notes it and carries on, and prints the notes to standard error after the game ends. That is the middle setting of the four levels Appendix A of the standard recommends, and the one Frotz uses too.

Rezrov exits with 0 when the game quits or standard input runs out, 1 when a file cannot be read or is not a story, 2 when the command line is wrong, and 3 when the game reaches something not implemented yet.

### Acceptance scripts

A seed and a command file together make a game play the same way every time, and an acceptance script is those two things in one small file, with the play it produced kept beside it so that later runs can be checked against it. The scripts in the `acceptance` directory are the interpreter's own; this is one of them:

```text
! SEED=20
! GAME=../entharion/zcode-infocom/zork1-r2-sAS000C.z1

n. n. u
get egg
```

A line beginning with `!` is a directive. `GAME` names the story file and `SEED` gives the session seed, as `--seed` does, and both are required. `BLORB` names a resource file when the one beside the story is not the right one, and `INTERPRETER` names the machine, as `--interpreter` does, for a game that plays differently by it. Paths are relative to the script, not to wherever you run it from. A line beginning with `#` is a comment, a blank line is ignored, and every other line is a command in the same format as a command file, so a game that reads single keys can be given them as bracketed codes. A command may be written with the prompt in front, as `> look`, which reads like a transcript and is also how to give a command that itself begins with `#` or `!`.

A stretch of the script can be fenced off so that it is not played. A line of three backticks opens the fence, a bare line of three backticks closes it, and a fence left open runs to the end of the file. A label after the opening backticks is allowed, so a script can keep an alternative path through the game beside the one it plays:

````text
open trapdoor

```PATH 1
kill troll
take axe
```

save troll
````

Here "save troll" is played and the two commands under PATH 1 are not. Swapping the fence to the other path later is a matter of moving the backticks.

A long script can change its seed partway. A `SEED` line after some commands takes effect when the play reaches it, before the next command, and the seed before it governs everything above. That means each stretch of a game can have the seed that makes it go the way it should, and once a fight with the troll is settled, hunting for a seed that gets you past the thief never disturbs it. A `SEED` line after the last command applies as the script ends, which matters with `--resume`.

While a script is being written, `--resume` plays it and then leaves the game running at the console, with a note on standard error marking where the script ended, so the next commands can be tried before they go into the file. Nothing is checked or recorded in that mode; the game runs until it quits or the console runs out, and the saved game, if there is one, is still the one held in memory from the script.

```sh
rezrov --accept acceptance/zork1-r2-sAS000C.accept
rezrov --accept acceptance/zork1-r2-sAS000C.accept --resume
```

The play is shown as it happens, so that when you are building a script up you can see where a command went wrong, and the verdict comes at the end. The first run records what came out as `zork1-r2-sAS000C.expected` beside the script. Every run after plays it again and compares, saying which line of the recording differs and which line of the script was being played at that point, and exiting with 1 when something has changed. When the change is one you meant, `--update` after the script records it afresh. The recorded text is everything the game printed, commands included, and after it a line for each thing the interpreter noticed: a runtime error the game made, or the point at which the game reached something not implemented yet. That way a game that starts misbehaving, or stops, changes the text and shows up. Saving and restoring work within a run, with the saved game kept in memory, so a script can test both.

The test suite plays every script in the directory whose game it can find, so the scripts double as regression tests for the interpreter. The games themselves live in the submodule and are not part of the repository, which is why a script whose game is absent is skipped rather than failed.

### The terminal program

```sh
dotnet run --project src/Rezrov.Tui -- entharion/zcode-infocom/zork1-r88-s840726.z3
```

This takes over the whole terminal, as Infocom's own interpreters did. A Version 3 game gets its status line across the top, later games get their upper window, and text is shown in the styles and colors the game asks for, as far as the terminal has them. Input is edited in place at the game's own cursor, timed input works, and the [MORE] pause appears when a screenful of text has gone by. Mouse clicks are reported to games that ask for them, as the standard's section 10.3 describes, so Journey's and Zork Zero's menus can be clicked as well as typed. When the game turns on a transcript, saves, or restores, a file dialog asks where. Ctrl+Q leaves at any time, and when the game ends by itself the screen stays until a key is pressed, so the last of the text can be read.

The same `--blorb`, `--commands`, `--transcript`, `--record`, `--save`, `--seed`, and `--interpreter` options work here as on the command line, and a `.zblorb` can be given directly. Version 6 games get their windows laid out on the terminal's grid, with the game measuring in units that are a quarter of a cell across and a whole cell down, which is the shape Infocom's games assume when they have no pictures to draw. The character graphics font of the standard's section 16, which Journey borders its screens with and Beyond Zork draws its map and its runes in, is shown with the nearest Unicode box drawing, block, arrow, and runic characters, as far as the terminal's font has them. What the terminal program does not do yet is play sounds beyond a bleep, since a sound frontend needs a decoder for the Blorb formats, or show pictures, which wait for a graphical frontend.

## Building and Testing

From the repository root:

```sh
dotnet build
dotnet test
```

Neither needs the `entharion` submodule described further down, so a plain clone builds and tests without pulling several hundred megabytes of reference material.

Tests are xUnit v3 on Microsoft Testing Platform, which means a test project is a real executable rather than a library loaded by a separate runner. You can run one directly, and it reports more detail than `dotnet test` does:

```sh
dotnet run --project tests/Rezrov.Tests
```

One thing worth knowing in advance, because the failure is misleading. In this mode `dotnet test` forwards any option it does not recognize to the test executable, which then rejects it. So `dotnet test -nologo` fails with "Zero tests ran" and exit code 5 rather than with a complaint about the flag. Other options from the VSTest era behave the same way. Plain `dotnet test` is the safe form.

## Prerequisites

You will need the .NET SDK. The version is pinned in `global.json`, so you need **10.0.302 or newer within the 10.0.x band**. An older SDK will refuse to build rather than silently doing the wrong thing, and a future .NET 11 will not be picked up until that pin is raised deliberately.

Verify what you have with `dotnet --list-sdks`.

### Windows

```powershell
winget install -e --id Microsoft.DotNet.SDK.10 --source winget
```

- `-e` enforces an exact match for the package ID.
- `--id` pinpoints the explicit unique identifier for .NET 10.
- `--source winget` ensures Windows Package Manager pulls the manifest directly from the official WinGet repository instead of a third-party Store listing.

### macOS

```sh
brew install --cask dotnet-sdk
```

### Ubuntu / Debian-based systems

```sh
sudo apt update
sudo apt install -y dotnet-sdk-10.0
```

### Fedora / RHEL-based systems

```sh
sudo dnf install -y dotnet-sdk-10.0
```

### Arch Linux

```sh
sudo pacman -S dotnet-sdk
```

Arch ships whichever SDK is current, which may be ahead of the pinned band. Confirm with `dotnet --list-sdks` afterwards.

### Any platform

Distribution repositories often lag behind. If the package above is unavailable or too old, use the official installer script, which needs no root and drops the SDK in `~/.dotnet`:

```sh
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
```

That is everything needed to build, run, and test.

## Producing a Native Binary (optional)

Rezrov targets NativeAOT, which compiles to a self-contained native executable with no .NET runtime dependency for the end user. That path needs a platform linker in addition to the SDK, so it is only worth installing if you intend to run `dotnet publish -p:PublishAot=true`. Ordinary `dotnet build` and `dotnet test` need nothing beyond the SDK.

**Windows.** NativeAOT invokes the MSVC linker, so it needs the C++ build tools and the Windows SDK:

```powershell
winget install Microsoft.VisualStudio.2022.BuildTools --force --override "--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100"
```

The `--override` string matters. Installing the Build Tools package without specifying the `VCTools` workload leaves you without a linker, and the failure surfaces at publish time rather than at install time.

**macOS.** The Xcode command line tools, which `xcode-select --install` provides.

**Linux.** A Clang toolchain and zlib headers, for example on Debian/Ubuntu:

```sh
sudo apt update
sudo apt install -y clang zlib1g-dev
```

Note that NativeAOT does not cross-compile between operating systems. Each target runtime identifier has to be published on its own platform: `win-x64` on Windows, `linux-x64` on Linux, `osx-arm64` or `osx-x64` on macOS, where either kind of Mac can build both and `lipo` can join them.

Both program projects have `PublishAot` set, so the command is only `dotnet publish src/Rezrov.Cli -c Release -r win-x64`, with the runtime identifier of the machine you are on, and likewise for `src/Rezrov.Tui`. The executable lands under the project's `bin/Release` directory.

## Releasing

A release is a version tag. The release workflow builds both programs with NativeAOT on Windows, Linux for x64 and arm64, and macOS for both Intel and Apple silicon joined into a universal binary with `lipo`, packages each platform's pair with the README and license, and publishes a GitHub Release with the archives and their checksums attached. The steps:

1. Set `Version` in `Directory.Build.props` to the new number and merge that change through a pull request as usual. The programs report this version through `--version`.
2. Tag the merge on `main` and push the tag, for example `git tag v0.1.0 && git push origin v0.1.0`.

The workflow refuses a tag that disagrees with the version in `Directory.Build.props`, so the two cannot drift apart. The release notes are generated from the pull requests merged since the previous tag, which is one more reason the pull request titles are kept to Conventional Commits.

## Commit Messages

This project follows [Conventional Commits](https://www.conventionalcommits.org/), and a `commit-msg` hook enforces it. Activating the hook is a one-time step per clone:

```sh
git config core.hooksPath .githooks
```

Subjects take the shape `type(optional scope): description`, with an optional `!` before the colon to mark a breaking change. The accepted types are `build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`, `style`, and `test`, and the subject is capped at 72 characters so it reads cleanly in `git log --oneline`.

```
feat: decode variable form opcodes
fix(zmachine): correct branch offset sign extension
docs: explain the Glk and Glulx split
feat(glulx)!: replace the accelerated function table
```

Merge, revert, and autosquash subjects that Git generates on its own are left alone. If you ever need to sidestep the check for a single commit, `git commit --no-verify` does it.

## Reference Material (optional)

The `entharion` submodule holds the specifications, story files, and third-party tooling this project is developed against. None of it is required to build, test, or run Rezrov, but it is what the interpreter is checked against during development.

It is also large, roughly 230 MB fully populated, so it is worth choosing how much of it you want:

```sh
# The specifications, story files, and test suites only.
git submodule update --init

# The above, plus the fourteen third-party tool repositories under vendor/.
git submodule update --init --recursive
```

The second form is only necessary if you intend to build the reference tools described below.

### What Entharion Provides

- `specs/` holds the Z-Machine Standard 1.1, Blorb, Quetzal, and Treaty of Babel specifications, along with the earlier Infocom ZIP/EZIP/XZIP/YZIP documents.
- `zcode-infocom/`, `zcode-inform/`, and `glulx-code/` hold story files, many with source alongside them in the matching `-source` directories.
- `zcode-checkers/` and `glulx-checkers/` hold the interpreter conformance suites. These matter more than the games early on: `czech`, `praxix.z5`, `strictz.z5`, and `etude.z5` exercise Z-Machine opcode behavior systematically, and `glulxercise-r13-s241202.ulx` does the same for Glulx.
- `vendor/` holds third-party interpreters, compilers, and Glk libraries as nested submodules.

Entharion's own README documents the story file collection in detail, including which Infocom release each binary came from. It is the authoritative source for that, and this file only covers getting the tools running.

### Building the Reference Tools

All of these need a C compiler, `make`, and a Unix-like environment. They are useful for comparing Rezrov's behavior against known-good implementations.

#### Toolchain prerequisites

**Windows.** The tools assume a Unix environment, so use WSL. From an elevated PowerShell, rebooting if prompted, then creating a Unix user when the Ubuntu shell first opens:

```powershell
wsl --install
```

Then, inside the Ubuntu shell:

```sh
sudo apt update
sudo apt install build-essential groff libncurses-dev
```

`groff` is only needed to format the ztools man pages, and `libncurses-dev` only for GlkTerm.

Your Windows drives are visible in WSL under `/mnt`, so a checkout at `F:\Projects\rezrov` is reachable at `/mnt/f/Projects/rezrov`.

**macOS.** Install the command line developer tools:

```sh
xcode-select --install
```

**Linux.** Install a compiler toolchain, for example on Debian/Ubuntu:

```sh
sudo apt update
sudo apt install build-essential groff libncurses-dev
```

#### Z-Machine tools

From the repository root, in WSL, macOS Terminal, or a Linux shell:

```sh
make -C entharion/vendor/ztools
make -C entharion/vendor/reform6
make -C entharion/vendor/frotz dumb
```

- `frotz` is the reference Z-Machine interpreter. The `dumb` target builds `dfrotz`, which runs in a plain terminal with no display dependencies, making it the right choice for diffing transcripts against Rezrov's own output.
- `ztools` provides the inspection utilities, notably `infodump` for header, object, and dictionary dumps and `txd` for disassembly.
- `reform6` is an Inform 6 based compiler for producing story files.

#### Glulx tools

Glulx does no input or output of its own, delegating all of that to Glk. So a Glulx interpreter has to be linked against a Glk library, which means building the library first:

```sh
make -C entharion/vendor/cheapglk
make -C entharion/vendor/glulxe
```

Glulxe's Makefile already defaults to `../cheapglk`, and because the submodules are siblings under `vendor/`, that pairing builds without editing any paths. CheapGlk is deliberately minimal, with one text buffer window and no status line, which makes it the cleanest baseline for comparing output.

For multiple windows and a real status line, build GlkTerm instead and uncomment the `../glkterm` block near the top of `entharion/vendor/glulxe/Makefile`. Either way, set the appropriate `-DOS_UNIX`, `-DOS_MAC`, or `-DOS_WINDOWS` option in that same file.

Each vendored repository ignores its own build artifacts, so nothing shows up as untracked in Git after building.

### Running the Reference Tools

From a Unix shell:

```sh
./entharion/vendor/frotz/dfrotz entharion/zcode-infocom/ballyhoo-r97-s851218.z3
./entharion/vendor/ztools/infodump -i entharion/zcode-infocom/amfv-r77-s850814.z4
./entharion/vendor/glulxe/glulxe entharion/glulx-checkers/glulxercise-r13-s241202.ulx
```

On Windows the binaries are Linux executables, but they can be invoked directly from PowerShell by prefixing `wsl`:

```powershell
wsl ./entharion/vendor/frotz/dfrotz entharion/zcode-infocom/ballyhoo-r97-s851218.z3
```

## License

MIT. See [LICENSE](LICENSE).
