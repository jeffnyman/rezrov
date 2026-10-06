# Rezrov

_An Interactive Fiction Interpreter_

Rezrov is an interpreter for interactive fiction written in C#. The most common and obvious formats there are the Z-Machine and Glulx, and beside them is the Å-machine, which is what Dialog compiles to when it's not compiling to the Z-Machine. The plan is for the interpreter core to be a UI-agnostic library, and the command line, terminal, and graphical frontends are going to be thin shells over that one engine.

## Installing

Each release on the [releases page](https://github.com/jeffnyman/rezrov/releases) carries one archive per platform, named for the version and the platform: `rezrov-0.1.0-win-x64.zip`, `rezrov-0.1.0-linux-x64.tar.gz`, `rezrov-0.1.0-linux-arm64.tar.gz`, and `rezrov-0.1.0-osx-universal.tar.gz`, the last a universal binary that runs natively on both Intel and Apple silicon Macs. Inside are the three single-file programs, `rezrov`, `rezrov-tui`, and `rezrov-gtui`, as native executables that need nothing installed beside them, not even .NET. The graphical program comes in an archive of its own beside them, named the same way with `rezrov-gui` in front: `rezrov-gui-0.1.0-win-x64.zip` and so on. It holds a directory rather than a single file, because the drawing and text shaping libraries have to sit beside the program, and it's separate so that someone who wants only the three single-file programs is not left holding those libraries with nothing to say what they're sitting there for. On macOS that directory holds `rezrov.app` instead, a bundle, because a bundle is the only shape the Dock and the application switcher read a program's name and icon out of, and a program shipped loose there shows up under the toolkit's name with no icon at all; a `rezrov-gui` beside the bundle names a story from a shell the way it does everywhere else. Unpack the archive somewhere on your path and they are ready; `rezrov --version` says which release you have. A `SHA256SUMS` file beside the archives lets you check a download.

Two platform notes. The macOS executables are not signed. Unpacked with `tar` they run as they are, on Intel and Apple silicon alike; if an archive unpacked through the Finder is refused as being from an unidentified developer, clear the quarantine mark with `xattr -d com.apple.quarantine rezrov rezrov-tui`, or `xattr -dr com.apple.quarantine .` inside the graphical program's directory, or allow it in System Settings under Privacy and Security. On Linux, `tar` keeps the executable permission, but if a download loses it, `chmod +x rezrov rezrov-tui` restores it.

Each archive carries a short readme of its own, kept in the repository's `release` directory, with just what someone who has downloaded that archive needs.

## Using

Rezrov comes as four programs over one interpreter.

- `rezrov` is the command line program: give it a story file and it tells you about the file; add `--run` and it plays the game on the console as a plain stream of text.
- `rezrov-tui` is the terminal program: give it a story file and it plays the game full screen, with the status line, the upper window, styles, and colors that the console can't draw, and for a Glulx game with its windows laid out as the game splits them.
- `rezrov-gui` is the graphical program: the same games in a window of their own, where the pictures they carry can finally be drawn.
- `rezrov-gtui` is the grid program: a Z-machine game in a window this program opens for itself, with no package underneath it at all, which is the same thing the graphical program does with every part of it written out rather than handed to a toolkit. Dialog games run on all four of them.

## License

MIT. See [LICENSE](LICENSE).
