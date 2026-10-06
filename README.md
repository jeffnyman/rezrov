# Rezrov

_An Interactive Fiction Interpreter_

Rezrov is an interpreter for interactive fiction written in C#. The most common and obvious formats there are the Z-Machine and Glulx, and beside them is the Å-machine, which is what Dialog compiles to when it's not compiling to the Z-Machine. The plan is for the interpreter core to be a UI-agnostic library, and the command line, terminal, and graphical frontends are going to be thin shells over that one engine.

## What it plays

Z-Machine story files, Versions 1 through 8, which is Infocom's whole catalog and everything Inform 6 has produced since. Glulx story files, which is what Inform 7 produces. Å-machine stories, which is what Dialog compiles to. A game packaged in a Blorb file with its sounds and pictures can be handed over as it stands, and saved games are written in Quetzal. All four programs play the Z-Machine and the Å-machine; Glulx runs on all of them but the grid program.

[What Rezrov plays](docs/formats.md) takes each machine in turn, down to Version 6's eight-window screen model and the arc_image picture band that Arcturus games carry.

## Installing

Each release on the [releases page](https://github.com/jeffnyman/rezrov/releases) carries one archive per platform, named for the version and the platform: `rezrov-0.1.0-win-x64.zip`, `rezrov-0.1.0-linux-x64.tar.gz`, `rezrov-0.1.0-linux-arm64.tar.gz`, and `rezrov-0.1.0-osx-universal.tar.gz`, the last a universal binary that runs natively on both Intel and Apple silicon Macs. Inside are the three single-file programs, `rezrov`, `rezrov-tui`, and `rezrov-gtui`, as native executables that need nothing installed beside them, not even .NET.

The graphical program comes in an archive of its own beside them, named the same way with `rezrov-gui` in front: `rezrov-gui-0.1.0-win-x64.zip` and so on. It holds a directory rather than a single file, because the drawing and text shaping libraries have to sit beside the program, and it's separate so that someone who wants only the three single-file programs isn't left holding those libraries with nothing to say what they're sitting there for. On macOS that directory holds `rezrov.app` instead, a bundle, because a bundle is the only shape the Dock and the application switcher read a program's name and icon out of, and a program shipped loose there shows up under the toolkit's name with no icon at all; a `rezrov-gui` beside the bundle names a story from a shell the way it does everywhere else.

Unpack the archive somewhere on your path and they're ready; `rezrov --version` says which release you have. A `SHA256SUMS` file beside the archives lets you check a download.

Two platform notes. The macOS executables aren't signed. Unpacked with `tar` they run as they are, on Intel and Apple silicon alike; if an archive unpacked through the Finder is refused as being from an unidentified developer, clear the quarantine mark with `xattr -d com.apple.quarantine rezrov rezrov-tui`, or `xattr -dr com.apple.quarantine .` inside the graphical program's directory, or allow it in System Settings under Privacy and Security. On Linux, `tar` keeps the executable permission, but if a download loses it, `chmod +x rezrov rezrov-tui` restores it.

Each archive carries a short readme of its own, kept in the repository's `release` directory, with just what someone who has downloaded that archive needs.

## Running a game

Give any of the programs a story file:

```sh
rezrov zork1.z3 --run
rezrov-tui zork1.z3
rezrov-gui anchorhead.gblorb
rezrov-gtui zork1.z3
```

Story files end in `.z1` through `.z8` for the Z-Machine, `.ulx` for Glulx, and `.aastory` for the Å-machine. A `.zblorb`, `.gblorb`, or `.blb` is a game packaged with its sounds and pictures, and can be given just as directly. Every program answers `--help` with the options it takes and `--version` with the release it is.

Those commands are written for the released binaries, and so is everything under `docs/`. **From a clone, put `dotnet run --project <project> --` in front of any of them**, with the project that goes with the program: `src/Rezrov.Cli` for `rezrov`, and `src/Rezrov.Tui`, `src/Rezrov.Gui`, or `src/Rezrov.Gtui` for the other three. The `--` is what separates arguments meant for `dotnet run` from arguments meant for Rezrov.

```sh
dotnet run --project src/Rezrov.Tui -- entharion/zcode-infocom/zork1-r88-s840726.z3
```

## The four programs

Rezrov comes as four programs over one interpreter. Dialog games run on all four of them.

- `rezrov` is the command line program: give it a story file and it tells you about the file; add `--run` and it plays the game on the console as a plain stream of text.
- `rezrov-tui` is the terminal program: give it a story file and it plays the game full screen, with the status line, the upper window, styles, and colors that the console can't draw, and for a Glulx game with its windows laid out as the game splits them.
- `rezrov-gui` is the graphical program: the same games in a window of their own, where the pictures they carry can finally be drawn.
- `rezrov-gtui` is the grid program: a Z-machine game in a window this program opens for itself, with no package underneath it at all, which is the same thing the graphical program does with every part of it written out rather than handed to a toolkit.

That order is deliberate rather than historical: each program takes over a little more of the work the one before it left to something else, until the grid program is deciding every pixel for itself. [The four programs](docs/programs.md) takes each one in turn, with every option it accepts and what it can and can't draw.

## Documentation

| Page | What is in it |
| --- | --- |
| [The four programs](docs/programs.md) | each frontend in full, with the options it takes |
| [What Rezrov plays](docs/formats.md) | the Z-Machine, Glulx, and the Å-machine, feature by feature |
| [Mapping a game](docs/mapping.md) | the map drawn from the rooms a game was played through |
| [Reading and debugging a story file](docs/debugging.md) | the disassembler and the debugger |
| [Acceptance scripts](docs/acceptance.md) | playing a game the same way every time, and checking that it still plays that way |
| [Building, testing, and releasing](docs/building.md) | the SDK, the test suite, native binaries, commit messages, and cutting a release |
| [Reference material](docs/reference-material.md) | the Entharion submodule and the third-party tools Rezrov is checked against |

## Building from source

```sh
dotnet build
dotnet test
```

The .NET SDK is all either of those needs: 10.0.302 or newer within the 10.0.x band, which `global.json` pins. Neither needs the `entharion` submodule, so a plain clone builds and tests without pulling several hundred megabytes of reference material. [Building, testing, and releasing](docs/building.md) has the rest, including installing the SDK on each platform and producing a native binary.

## License

MIT. See [LICENSE](LICENSE).
