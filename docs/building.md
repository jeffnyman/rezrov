# Building, testing, and releasing

## Building and Testing

From the repository root:

```sh
dotnet build
dotnet test
```

Neither needs the [`entharion` submodule](reference-material.md), so a plain clone builds and tests without pulling several hundred megabytes of reference material.

The three interpreter libraries are built optimized even in the Debug configuration, because the test suite replays whole games through them and an unoptimized interpreter makes that run take minutes rather than seconds. The programs and the tests aren't, so debugging them is as usual. To step through the interpreter code itself with every local in view, build with optimization off for that session:

```sh
dotnet build -p:Optimize=false
```

Tests are xUnit v3 on Microsoft Testing Platform, which means a test project is a real executable rather than a library loaded by a separate runner. You can run one directly, and it reports more detail than `dotnet test` does:

```sh
dotnet run --project tests/Rezrov.Tests
```

One thing worth knowing in advance, because the failure is misleading. In this mode `dotnet test` forwards any option it doesn't recognize to the test executable, which then rejects it. So `dotnet test -nologo` fails with "Zero tests ran" and exit code 5 rather than with a complaint about the flag. Other options from the VSTest era behave the same way. Plain `dotnet test` is the safe form.

### Speed

Fast enough that it has never been worth optimizing, which is worth writing down so that nobody wonders. On an AMD Ryzen 7 9800X3D, a release build on .NET 10 plays Zork Zero's whole recorded walkthrough, 1853 commands, in about 1.4 seconds, which is well under a millisecond a turn. On the Glulx side, Anchorhead's 722 command walkthrough executes 680 million virtual machine instructions in about 16 seconds, which is roughly 41 million instructions a second; a debug build measures the same within the noise.

The interesting part of that is the ratio rather than the rate. Anchorhead spends about 940,000 instructions on a single turn, some twenty milliseconds, where Advent, running on the same Glulx engine, spends about 18,000: five and a half million instructions over its whole 298 command walkthrough against Anchorhead's 680 million over 722. Fifty times the work for a turn of the same game, and the difference isn't the interpreter but the game. Anchorhead is Inform 7 with deep rulebooks and Advent is Inform 6.

The same gap shows on the Z-Machine, which is the better demonstration because both games run on the older and simpler of the two machines. Zork Zero, which is about as much as Infocom ever asked of it, takes well under a millisecond a turn. Bronze, which is Inform 7 compiled to the same machine, takes about thirty-four. A player notices none of this. What it does mean is that two games, Bronze and Anchorhead, account for most of the time the test suite takes, so if that ever becomes annoying, that's where the time is.

## Prerequisites

You will need the .NET SDK. The version is pinned in `global.json`, so you need **10.0.302 or newer within the 10.0.x band**. An older SDK will refuse to build rather than silently doing the wrong thing, and a future .NET 11 won't be picked up until that pin is raised deliberately.

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

That's everything needed to build, run, and test.

## Producing a Native Binary (optional)

Rezrov targets NativeAOT, which compiles to a self-contained native executable with no .NET runtime dependency for the end user. That path needs a platform linker in addition to the SDK, so it's only worth installing if you intend to run `dotnet publish -p:PublishAot=true`. Ordinary `dotnet build` and `dotnet test` need nothing beyond the SDK.

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

Note that NativeAOT doesn't cross-compile between operating systems. Each target runtime identifier has to be published on its own platform: `win-x64` on Windows, `linux-x64` on Linux, `osx-arm64` or `osx-x64` on macOS, where either kind of Mac can build both and `lipo` can join them.

Both program projects have `PublishAot` set, so the command is only `dotnet publish src/Rezrov.Cli -c Release -r win-x64`, with the runtime identifier of the machine you're on, and likewise for `src/Rezrov.Tui`. The executable lands under the project's `bin/Release` directory.

## Releasing

A release is a version tag. The release workflow builds all four programs with NativeAOT on Windows, Linux for x64 and arm64, and macOS for both Intel and Apple silicon joined into a universal binary with `lipo`. It makes two archives per platform, one holding the three single-file programs and one holding the graphical program with the native libraries that must sit beside it, each with its own readme and the license, and publishes a GitHub Release with the archives and their checksums attached. The macOS graphical archive is shaped by `.github/scripts/bundle-mac-gui.sh` into a `rezrov.app` bundle, with the icon built from `release/rezrov.png` at the sizes macOS asks for; that script runs on every pull request as well, over a real pair of publishes, so that a tag is never the first thing to run it. The steps:

1. Set `Version` in `Directory.Build.props` to the new number and merge that change through a pull request as usual. The programs report this version through `--version`.
2. Tag the merge on `main` and push the tag, for example `git tag v0.1.0 && git push origin v0.1.0`.

The workflow refuses a tag that disagrees with the version in `Directory.Build.props`, so the two can't drift apart. The release notes are generated from the pull requests merged since the previous tag, which is one more reason the pull request titles are kept to Conventional Commits.

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
