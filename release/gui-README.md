# Rezrov

Rezrov plays interactive fiction: Infocom's games and everything since
written for the Z-Machine, and the Glulx games that Inform 7 produces.
This archive holds `rezrov-gui`, the graphical program, which plays a
game in a window of its own and so can show the pictures a game carries.

The two files beside it, `libSkiaSharp` and `libHarfBuzzSharp`, are the
drawing and text shaping libraries the program needs. They have to stay
in the same directory as the program, which is why this archive holds a
directory rather than a single executable. Nothing else has to be
installed, not even .NET.

The command line and terminal programs, `rezrov` and `rezrov-tui`, are
in an archive of their own on the same release page.

## Running a game

Run the program from this directory, or move the whole directory
somewhere and run it from there.

```sh
rezrov-gui zork1.z3
rezrov-gui anchorhead.gblorb
```

Story files end in `.z3` through `.z8` for the Z-Machine, `.ulx` for
Glulx, and `.zblorb` or `.gblorb` for either packaged with its sounds
and pictures. A game that keeps its pictures in a separate resource file
finds it beside the story file, or you can name it with `--blorb`.

## Settling how the text looks

`rezrov-gui --help` lists every option. Four of them settle the type:

```sh
rezrov-gui game.gblorb --font "Iowan Old Style, Charter, Georgia, serif" --size 18
```

- `--font <family>` sets the family the prose is in, and `--fixed
  <family>` the one that status lines and preformatted text are in. A
  family may be a list rather than one name, in which case the first of
  them your machine actually has is the one used.
- `--size <pixels>` is the size of ordinary text, from 6 to 72.
- `--smoothing <mode>` is `subpixel`, `grayscale`, or `none`. Subpixel
  is the default and is what the rest of your desktop uses; grayscale
  is worth trying if text looks heavy on your display.

`rezrov-gui --probe` takes the same four options and prints what they
measure without opening a window, which is a quick way to see whether a
family you have named is one your machine actually has.

## Platform notes

- macOS: the program is not signed. Unpacked with `tar` it runs as it
  is. If an archive unpacked through the Finder is refused as being from
  an unidentified developer, run
  `xattr -dr com.apple.quarantine .` in this directory, or allow it in
  System Settings under Privacy and Security.
- Linux: `tar` keeps the executable permission. If a download loses it,
  `chmod +x rezrov-gui` restores it.

## More

The full documentation, the source, and the issue tracker are at
https://github.com/jeffnyman/rezrov. `rezrov-gui --version` says which
release this is. The license is in the LICENSE file beside this one.
