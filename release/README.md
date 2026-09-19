# Rezrov

Rezrov plays interactive fiction: Infocom's games and everything since
written for the Z-Machine, and the Glulx games that Inform 7 produces.
This archive holds three programs for your platform, as native executables
that need nothing installed beside them, not even .NET.

- `rezrov` is the command line program. Given a story file it describes
  the file; with `--run` it plays the game on the console as a plain
  stream of text.
- `rezrov-tui` is the terminal program. It plays the game full screen,
  with the status line, windows, styles, and colors the game asks for.
  Ctrl+Q leaves at any time.
- `rezrov-gtui` is the grid program. It plays a Z-Machine game in a
  window of its own, drawn as a grid of characters from a font it
  carries, including the character graphics font that Beyond Zork draws
  its map with. Close the window to leave. It does not play Glulx.

## Running a game

Put the programs somewhere on your path, or run them from here.

```sh
rezrov-tui zork1.z3
rezrov-gtui zork1.z3
rezrov zork1.z3 --run
```

Story files end in `.z3` through `.z8` for the Z-Machine, `.ulx` for
Glulx, and `.zblorb` or `.gblorb` for either packaged with its sounds and
pictures. `rezrov --help` and the others list the options: a
commands file to play from, a seed for repeatable play, and the files to
use for saves, transcripts, and command recording.

Saved games, transcripts, and the files a game keeps go where the game
asks or where the program asks for a name: the terminal program with a
dialog, the command line program on standard error, and the grid program
in its own window, offering the story's name so that saving is one
keystroke.

## Platform notes

- macOS: the executables are not signed. Unpacked with `tar` they run as
  they are. If an archive unpacked through the Finder is refused as being
  from an unidentified developer, run
  `xattr -d com.apple.quarantine rezrov rezrov-tui rezrov-gtui`, or
  allow it in System Settings under Privacy and Security.
- Linux: `tar` keeps the executable permission. If a download loses it,
  `chmod +x rezrov rezrov-tui rezrov-gtui` restores it.
- Windows: the two text programs run in Windows Terminal, PowerShell,
  or the command prompt, and `rezrov-tui` looks best in Windows
  Terminal. `rezrov-gtui` opens a window of its own instead.

## More

The full documentation, the source, and the issue tracker are at
https://github.com/jeffnyman/rezrov. `rezrov --version` says which
release this is. The license is in the LICENSE file beside this one.
