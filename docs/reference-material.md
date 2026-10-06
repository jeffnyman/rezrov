# Reference material

The `entharion` submodule holds the specifications, story files, and third-party tooling this project is developed against. None of it is required to build, test, or run Rezrov, but it's what the interpreter is checked against during development.

It's also large, roughly 230 MB fully populated, so it's worth choosing how much of it you want:

```sh
# The specifications, story files, and test suites only.
git submodule update --init

# The above, plus the fourteen third-party tool repositories under vendor/.
git submodule update --init --recursive
```

The second form is only necessary if you intend to build the reference tools described below.

## What Entharion Provides

- `specs/` holds the Z-Machine Standard 1.1, Blorb, Quetzal, and Treaty of Babel specifications, along with the earlier Infocom ZIP/EZIP/XZIP/YZIP documents.
- `zcode-infocom/`, `zcode-inform/`, and `glulx-code/` hold story files, many with source alongside them in the matching `-source` directories.
- `dialog-code/` and `arcturus-code/` hold the games of the other two languages, each with its source beside it. Dialog's are both Å-machine stories and Z-Machine ones, since one compiler emits either; Arcturus's are ordinary Version 5 files with their arc_image pictures in a Blorb beside them or wrapped around them. Both directories have a build script in Entharion's `scripts/` that rebuilds them from source.
- `zcode-checkers/` and `glulx-checkers/` hold the interpreter conformance suites. These matter more than the games early on: `czech`, `praxix.z5`, `strictz.z5`, and `etude.z5` exercise Z-Machine opcode behavior systematically, and `glulxercise-r13-s241202.ulx` does the same for Glulx.
- `vendor/` holds third-party interpreters, compilers, and Glk libraries as nested submodules.

Entharion's own README documents the story file collection in detail, including which Infocom release each binary came from. It's the authoritative source for that, and this page only covers getting the tools running.

## Building the Reference Tools

All of these need a C compiler, `make`, and a Unix-like environment. They're useful for comparing Rezrov's behavior against known-good implementations.

### Toolchain prerequisites

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

### Z-Machine tools

From the repository root, in WSL, macOS Terminal, or a Linux shell:

```sh
make -C entharion/vendor/ztools
make -C entharion/vendor/reform6
make -C entharion/vendor/frotz dumb
```

- `frotz` is the reference Z-Machine interpreter. The `dumb` target builds `dfrotz`, which runs in a plain terminal with no display dependencies, making it the right choice for diffing transcripts against Rezrov's own output.
- `ztools` provides the inspection utilities, notably `infodump` for header, object, and dictionary dumps and `txd` for disassembly.
- `reform6` is an Inform 6 based compiler for producing story files.

### Glulx tools

Glulx does no input or output of its own, delegating all of that to Glk. So a Glulx interpreter has to be linked against a Glk library, which means building the library first:

```sh
make -C entharion/vendor/cheapglk
make -C entharion/vendor/glulxe
```

Glulxe's Makefile already defaults to `../cheapglk`, and because the submodules are siblings under `vendor/`, that pairing builds without editing any paths. CheapGlk is deliberately minimal, with one text buffer window and no status line, which makes it the cleanest baseline for comparing output.

For multiple windows and a real status line, build GlkTerm instead and uncomment the `../glkterm` block near the top of `entharion/vendor/glulxe/Makefile`. Either way, set the appropriate `-DOS_UNIX`, `-DOS_MAC`, or `-DOS_WINDOWS` option in that same file.

Each vendored repository ignores its own build artifacts, so nothing shows up as untracked in Git after building.

## Running the Reference Tools

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
