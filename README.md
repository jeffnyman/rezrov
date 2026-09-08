# Rezrov

_An Interactive Fiction Interpreter_

Rezrov is an interpreter for interactive fiction written in C#. The most common and obvious formats there are the Z-Machine and Glulx. The plan is for the interpreter core to be a UI-agnostic library, and the command line, terminal, and graphical frontends are going to be thin shells over that one engine.

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

Note that NativeAOT does not cross-compile. Each target runtime identifier has to be published on its own platform: `win-x64` on Windows, `linux-x64` on Linux, `osx-arm64` on macOS.

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
