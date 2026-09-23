# Rezrov

_An Interactive Fiction Interpreter_

Rezrov is an interpreter for interactive fiction written in C#. The most common and obvious formats there are the Z-Machine and Glulx, and beside them is the Å-machine, which is what Dialog compiles to when it is not compiling to the Z-Machine. The plan is for the interpreter core to be a UI-agnostic library, and the command line, terminal, and graphical frontends are going to be thin shells over that one engine.

## Installing

Each release on the [releases page](https://github.com/jeffnyman/rezrov/releases) carries one archive per platform, named for the version and the platform: `rezrov-0.1.0-win-x64.zip`, `rezrov-0.1.0-linux-x64.tar.gz`, `rezrov-0.1.0-linux-arm64.tar.gz`, and `rezrov-0.1.0-osx-universal.tar.gz`, the last a universal binary that runs natively on both Intel and Apple silicon Macs. Inside are the three single-file programs, `rezrov`, `rezrov-tui`, and `rezrov-gtui`, as native executables that need nothing installed beside them, not even .NET. The graphical program comes in an archive of its own beside them, named the same way with `rezrov-gui` in front: `rezrov-gui-0.1.0-win-x64.zip` and so on. It holds a directory rather than a single file, because the drawing and text shaping libraries have to sit beside the program, and it is separate so that someone who wants only the three single-file programs is not left holding those libraries with nothing to say what they are for. Unpack the archive somewhere on your path and they are ready; `rezrov --version` says which release you have. A `SHA256SUMS` file beside the archives lets you check a download.

Two platform notes. The macOS executables are not signed. Unpacked with `tar` they run as they are, on Intel and Apple silicon alike; if an archive unpacked through the Finder is refused as being from an unidentified developer, clear the quarantine mark with `xattr -d com.apple.quarantine rezrov rezrov-tui`, or `xattr -dr com.apple.quarantine .` inside the graphical program's directory, or allow it in System Settings under Privacy and Security. On Linux, `tar` keeps the executable permission, but if a download loses it, `chmod +x rezrov rezrov-tui` restores it.

Each archive carries a short readme of its own, kept in the repository's `release` directory, with just what someone who has downloaded that archive needs.

## Using

Rezrov comes as four programs over one interpreter. `rezrov` is the command line program: give it a story file and it tells you about the file; add `--run` and it plays the game on the console as a plain stream of text. `rezrov-tui` is the terminal program: give it a story file and it plays the game full screen, with the status line, the upper window, styles, and colors that the console cannot draw, and for a Glulx game with its windows laid out as the game splits them. `rezrov-gui` is the graphical program: the same games in a window of their own, where the pictures they carry can finally be drawn. `rezrov-gtui` is the grid program: a Z-machine game in a window this program opens for itself, with no package underneath it at all, which is the same thing the graphical program does with every part of it written out rather than handed to a toolkit. Dialog games run on all four of them.

```sh
dotnet run --project src/Rezrov.Cli -- entharion/zcode-infocom/zork1-r88-s840726.z3
dotnet run --project src/Rezrov.Cli -- entharion/zcode-infocom/zork1-r88-s840726.z3 --run
```

The `--` separates arguments meant for `dotnet run` from arguments meant for Rezrov. A native binary, described further down, drops all of that and is just `rezrov <story-file>`.

Without `--run`, Rezrov reads the file and describes it. For a Z-Machine file that is the version, release, and serial number, where dynamic and high memory begin, whether the checksum matches, how many objects and dictionary words there are, and the first instruction the machine would execute. For a Glulx file it is the specification version the file was written to, the Inform compiler version, release, and serial number when Inform made it, the memory map, and whether the checksum matches. For an Å-machine story it is the file format version, release, and serial number, whether the checksum matches, how many characters the game adds to ASCII and how many words its dictionary holds, how many instructions its bytecode decodes to, and what the story says about itself: its title, its author, and what compiled it. Given a Blorb file it lists the resources inside, which game they belong to, and, when the file packages a game of its own, describes that game too.

With `--run`, the game's text goes to standard output and commands are read from the console. What the console can show is plain text, so the upper window and the status line of a Version 3 game are kept in the model but not drawn, styles and colors are not shown, timed input is not available, and a bleep is the terminal bell. The game is told all of this through the header, so a game that checks before using a feature behaves itself.

Everything else the Z-Machine standard describes is there: the parser gets your commands, the transcript and command recording streams work, saved games are written in Quetzal format, undo works, and sound effects are found in a Blorb file and handed to the frontend, which on the console can only ring the bell for them and in the terminal program plays them. Version 6 games run too, on the eight-window screen model of the standard's section 8.8. Whether they draw their pictures depends on where they are played: the two text programs and the grid program tell the game there are none, and Infocom's Version 6 games answer by using their text-only modes, while the graphical program draws them. On the console their several windows come out as one stream of text, which reads roughly; the terminal program below shows them properly.

One more kind of picture rides on the Z-Machine. Arcturus, Stefan Vogt's language and compiler, produces ordinary Version 5 and Version 8 story files, and its arc_image extension puts a band of artwork across the top of the screen with the whole text model, status line and upper window included, strictly below it. Rezrov draws that band in the graphical program. The band's height is the mode the game names on every call, nine text rows for the shallower Arthur shape or twelve for the deeper DAAD one, and it is taken from that operand alone rather than measured off the picture, so artwork of the wrong shape is fitted to the band instead of being allowed to set the layout. A picture is scaled as far as it will go with its own proportions kept, so a window that is not the band's shape gets an edge of background rather than artwork stretched out of true. Asking for picture zero takes the band down and gives its rows back to the text, which is what a room with no picture does and what the extension's own interpreter does with it; walk into such a room and the prose has the whole window, walk on and the band comes back.

Which games get any of this is settled by the resource file rather than guessed at. The Blorb an Arcturus game ships with carries a declaration chunk of its own saying that its pictures are arc_image scenes, and only a game whose resources carry it is told the band exists. That is not fussiness. The header bit the extension uses to advertise pictures is the same bit Inform's Version 4 and later games carry over from Version 3 to choose between a time and a score status line, which the standard's section 11.1 says an interpreter should leave alone, and several games here have it set for exactly that reason. Gating on the declaration keeps their status lines theirs. A game with no such resources, or played on a program with nowhere to put a band, is never told the extension is there, never issues its opcode, and plays as the perfectly ordinary Version 5 story it is.

Glulx files load and run on the whole instruction set, floating point, the memory heap, and accelerated functions included, and on the Glk library's windows, streams, and text output. On the console a Glulx game's text buffer windows come out as one stream of text, its text grid windows are kept but not shown, and commands are read from the console, or from a `--commands` file first. A request for a single key takes a line and uses its first character, or the enter key for an empty line. Timer events work when the game is waiting on nothing else. Undo works, and saved games, transcripts, and the data files a game keeps go to disk through Glk's file references: a file the game names lives beside the game file with the suffix the Glk specification recommends, a file the game asks the player for is asked about on standard error and answered on the console, or from the `--commands` file, and `--save`, `--transcript`, and `--record` name those files up front so a scripted run never asks. Saved games are in the Glulx variant of Quetzal.

A game packaged in a Blorb file can read the data chunks packaged with it through resource streams, a game that asks the time gets the system clock in universal or local time, and the Unicode case and normalization functions cover the whole of Unicode, including the letters that change length when their case does. Sound channels are there in the Glk library, with playing, pausing, volume, and the notification events, over whatever audio the frontend offers, and the gestalt answers say what that is; the terminal program plays them where the machine has audio, and the console program, which is a stream of text, says it has none. Hyperlinks are there too: text carries the link values a game gives it, a text grid remembers the link of every cell, and the terminal program shows link text in a color of its own and reports the link the player touches. Mouse input is reported for text grid windows in the terminal, as the column and row of the character touched. Graphics are there in the library: a graphics window is a canvas the library keeps for it, pictures are read out of the Blorb file as PNG or JPEG and drawn onto it at whatever size the image rules work out, with transparency honored, and rectangles are filled or erased back to the window's background color. The two text programs have nowhere to show any of it, so they answer that they have no graphics, no graphics window opens on them, and a game that draws anyway gets a false result and a note on standard error and plays on; the graphical program shows the lot. Pictures placed in the run of a text buffer's text are handed to the frontend to position, since only whatever lays the text out knows where they go and how wide the window they are measured against is; a script that asks for graphics gets each one written into the text as a note of which picture it was, where it was put, and what size the image rules worked out.

Å-machine stories run as well, from a `.aastory` file. The Å-machine is built for the way Dialog thinks rather than for the way a program runs: instead of calls and returns there are choice points, and when a query fails the machine goes back to the last one and tries the next answer, unbinding along the way everything it had decided since. All of that is here, with the unification, the trail, and the environment and choice frames, and so are the objects and their fields, the long-term heap where a value too big for a field is kept, the word maps the parser narrows a noun with, and the word endings decoder that lets a game told about "lantern" understand "lanterns". Text is read out of the game's own compressed bitstream and its own character set, so a story written in Swedish or French prints as its author wrote it. Saved games are written in the Å-machine's own AASV format, and undo works. On the console the output comes out as a stream of text wrapped to the terminal, which keeps the line breaks and the paragraph breaks and hears nothing a story writes into a status area, since there is nowhere to put one; divs, spans, links, and styles are read and kept but not drawn. The terminal program below shows them properly. The `--commands`, `--save`, and `--seed` options work here as elsewhere, and the options that belong to the other two machines are refused with a note rather than ignored.

The Å-machine comes with a conformance suite of its own, and Rezrov's tests play all of it: an opcode exercise that names every operation it checks, a character set and screen exercise, and two whole games played through their walkthroughs, each compared against the transcript the reference interpreter recorded. Beside that, Dialog games can be scripted the same way every other game here is, described next.

Rezrov can also draw a map of a game while it is played. It watches where the player is standing at each prompt and what they type there, and builds a grid of the rooms and the passages walked between them; `--map <file>` writes that out when the run ends, however it ended, since a map of where a game got to before it stopped is worth as much as one of a game played through. Nothing is read off the screen and nothing out of the game's own code, so only what the play actually showed goes on the map. A passage is drawn out of the room it was walked from and nowhere else, so a one-way drop wears an arrowhead at the end it points at and nothing brings you back, and a direction that left the player where they were is recorded as tried rather than as a wall, since a wall, a door that would not open, and a passage that loops straight back are the same thing from inside the room. That is the map as a file; the window program below draws the same map beside the game while it is being played.

A passage is drawn straight wherever it can be. Where it cannot, it is bent round whatever is in the way: the gaps between the boxes are free the whole distance across the picture, since no box ever sits in the columns beside one or the row beneath one, so a line has somewhere to run even when the two rooms have drifted well apart. Every straight line is drawn first, before any bent one is allowed to take up room, because a bent line has the whole picture to find its way through and a straight one has only the gap between two boxes. A bent line carries an arrowhead only where nothing is known to come back, and the arrowhead points into the room the passage leads to rather than the way the line happened to be travelling when it arrived, since a line that comes round the houses and arrives from the west is still a passage south.

Where the grid still cannot show something honestly it is named underneath rather than improvised. A passage in or out has nowhere to point on a flat map. A staircase gets a dotted line rather than the solid one a passage north gets, because the room above it was put north only to be readable and is not really north of anywhere. A passage the lattice is too crowded to get a line through is listed. And one bent line between two rooms serves both ways along it, so the way back is named rather than drawn a second time beside the first. One thing is both drawn and named: a diagonal that runs one way, which is a single character with nowhere to put an arrowhead, so the line is drawn and the direction said underneath.

Rooms are put down as they are found and never moved afterwards, which is what keeps a map steady to look at while it is still being made, and the price is that it drifts: making space for a new room shoves a whole half of the map a cell over, and every passage that straddled the line is a cell longer for it. Do that a few hundred times and rooms that are next door to each other are three cells and a column apart. So before the map is written it is tidied. Every passage wants its two rooms one cell apart in the direction it was walked, and the number that get their wish is a score the whole map can be judged by; rooms are offered the cells their own passages point at and take whichever scores best, trading places with whatever is sitting there when that is what helps. Nothing is accepted unless the score really rises, so the tidying always finishes and can never leave the map worse than it found it.

What none of it can do is make these maps flat. Zork's geometry is very nearly consistent, with only sixteen of its hundred and forty-six passages contradicting the arithmetic outright, and yet half its rooms want a cell another room has already taken: the map folds over itself, with the underground lying across the ground above it. Bending the lines around the fold is what answers that here, and across the games with walkthroughs it draws about seven passages in eight.

How the room is found depends on the version. Through Version 3 the Z-Machine keeps it in a global variable, because the interpreter draws the status line itself and has to know what to write on it, so those games are simply asked. From Version 4 the game draws its own bar and keeps the room wherever it likes, so the name is read off that bar and turned back into an object.

Turning it back into an object is the part that matters, because a name is not an identity. Zork has a dozen rooms called Forest and a maze of rooms called Maze, and one game here names the room the same thing on two turns out of three; a map keyed by the name folds every one of them into a single box. The way back to an object is the player. Whatever the game moves from room to room as the bar changes is the player, and once that is known the room is simply whatever that object is standing in, with no names involved at all. Nothing in a story file says which object that is, so it is learned while the game is played: every object in the room the bar names scores a point, and the one that goes on scoring is the player. In Zork's case it turns out to be called "cretin". A player who has climbed into something is still placed in the room around it rather than in the boat, and a game that spends its whole session in one room never learns at all, because nothing in it ever parts company with anything else; a room only one object in the story answers to is still enough on its own.

A Glulx story is read differently again, because it keeps nothing an interpreter can ask: no status line holding the room, no global the compiler agrees to put it in, and no object tree to look an answer up in. What it does do is print the room's name as a heading when the player walks in, in the style Glk sets aside for one, so that is what is read. Being a heading is not enough on its own, since a title page and a content warning are styled the same way. What tells them apart is the shape of the page rather than the words on it: Inform follows a room's heading with the description underneath it and then stops for a command, while front matter stands on its own, so a heading counts only once ordinary prose has followed it and the story has asked for a command rather than a keypress. A heading also has to own its line, or a story that prints every object's name in that style would fill the map with rooms called "lamp". None of this gives an object to key the room by, so two rooms a Glulx story calls the same thing are one room on the map.

Three things are beyond it. A Version 6 game paints its whole screen and has no status line to read, so those are not mapped. A game whose rooms have no objects behind them, which is what a Dialog-compiled story looks like from here, has nothing to find, and neither does a Glulx story that keeps its room on a status bar rather than printing a heading, as the Glulx build of Zork does. And a line holding several commands, which these parsers allow and which walkthroughs are full of, names no direction at all, because the parser walks every room on it and only the one the player finished in is ever seen; the same walkthrough written one command to a line maps far better than one written `n. n. u`. The direction words themselves are English, the nautical set the shipboard games use, and German, the last of those read out of Infocom's own unfinished German translation of Zork rather than guessed at, in both the spellings it takes. One word of its compass is deliberately left out: "no" is northeast in German and the answer to a question in English, and nothing here knows which language it is reading, so a German player is left with the two longer spellings for that one direction. A game played in a language none of this covers has its rooms found but not the passages between them.

The options, all of which imply `--run`, and which `rezrov --help` summarizes:

- `--commands <file>` plays commands from the file, one per line, before handing the game to the console. This is the same format the Z-Machine writes to its command recording stream and that Frotz records and replays, so a session recorded by either can be played back by the other. Once the file runs out, the console takes over.
- `--transcript <file>`, `--record <file>`, and `--save <file>` name the files to use when the game turns on a transcript, starts recording commands, or saves and restores, so that nothing has to be typed at a prompt. Without them Rezrov asks on standard error, which keeps the question out of anything you are capturing from standard output.
- `--blorb <file>` names the resource file that holds the game's sounds. Without it, a file beside the story with the same name and a `.blb`, `.blorb`, or `.zblorb` extension is used, which is how the Infocom sound files are distributed. A `.zblorb` that packages its own game can be given as the story file directly.
- `--map <file>` writes a map of the rooms the game was played through, drawn as text, with the passages the grid could not show named underneath it.
- `--seed <number>` seeds the game's random number generator, so the same commands produce the same play every time, which is what a test wants. The game still sees ordinary dice, just the same dice every session; this is not the predictable state the standard describes, which is a testing mode a game enters for itself with rolls of 1, 2, 3, and which is still there for games that use it. A game that asks to be reseeded at random partway through gets a fresh point on the seeded stream instead, so a seeded session stays repeatable to the end. The generator is Rezrov's own rather than the runtime's, so a seed produces the same session forever and no recording is invalidated by a .NET upgrade.
- `--interpreter <machine>` tells the game which of Infocom's machines it is running on, by name or by the number the standard's section 11.1.3 gives: `dec20`, `apple2e`, `macintosh`, `amiga`, `atarist`, `ibmpc`, `c128`, `c64`, `apple2c`, `apple2gs`, or `tandy`. Left alone, Rezrov says IBM PC, or DEC-20 for a Version 6 game on a screen without pictures, which is the pair that suits the games best.
- `--tandy` sets the Tandy bit in the header of a Version 1 to 3 game, which the standard's section 11.1 lists and which a few early Infocom games read: Zork I adds "Licensed to Tandy Corporation." to its banner and changes its closing words, and some games tone down their prose. Nothing else looks at it. Some games behave differently by machine: Beyond Zork decides whether to draw its map with the character graphics font by it, and the Version 6 games lay their screens out by it. Under the Amiga number an Infocom Version 6 game gets the Amiga's behavior of one pair of colors for the whole screen, as the standard's section 8.3 requires.
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

A line beginning with `!` is a directive. `GAME` names the story file and `SEED` gives the session seed, as `--seed` does, and both are required. `BLORB` names a resource file when the one beside the story is not the right one, and `INTERPRETER` names the machine, as `--interpreter` does, for a game that plays differently by it. `TANDY` set to `yes` sets the Tandy bit, as `--tandy` does. `UPPER` set to `yes` puts the upper window into the play: whenever the game pauses for input and has changed that window since it was last shown, its rows are written out, which is how a game like Custard, which draws all of its text there, can be scripted at all. `PICTURES` set to `yes` tells a Version 6 game the screen can show pictures, which sends Infocom's four down their graphical paths instead of their text-only ones; a stream of text cannot show a picture, so what the play records is which pictures are on the screen and where, written out whenever that changes. That is what catches a picture wrongly erased or left behind when the text scrolls, which is the way a Version 6 game goes wrong. An Å-machine game is scripted the same way, with the seed and the commands; the directives that belong to the other two machines, which are `INTERPRETER`, `TANDY`, `UPPER`, `PICTURES`, and `BLORB`, mean nothing to it and a script that carries one is told so. Paths are relative to the script, not to wherever you run it from. A line beginning with `#` is a comment, a blank line is ignored, and every other line is a command in the same format as a command file, so a game that reads single keys can be given them as bracketed codes, or by name for the keys a menu is worked with: `<up>`, `<down>`, `<left>`, `<right>`, `<escape>`, and `<space>`, one press per line. Two more of that shape are for a Glulx game that watches the pointer: `<click column,row>` and `<link value>`. A command may be written with the prompt in front, as `> look`, which reads like a transcript and is also how to give a command that itself begins with `#`, `!`, or `<`.

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

The play is shown as it happens, so that when you are building a script up you can see where a command went wrong, and the verdict comes at the end. The first run records what came out as `zork1-r2-sAS000C.expected` beside the script. Every run after plays it again and compares, saying which line of the recording differs and which line of the script was being played at that point, and exiting with 1 when something has changed. When the change is one you meant, `--update` after the script records it afresh. A script that has grown since it was recorded is told apart from one whose play has changed: the run says that the recording ends before the script does, and `--update` records the rest. The recorded text is everything the game printed, commands included, and after it a line for each thing the interpreter noticed: a runtime error the game made, or the point at which the game reached something not implemented yet. That way a game that starts misbehaving, or stops, changes the text and shows up. Saving and restoring work within a run, with the saved game kept in memory, so a script can test both.

A script may name a Glulx game as easily as a Z-Machine one. It plays through Glk on the console's text display, its commands answering whatever the game asks for, a line or a key, and any file it saves is kept in memory for the length of the run, so a script can save and restore without leaving anything behind. A script can also point at a game that expects a pointer: `<click 3,1>` touches the character at a column and row of whichever window asked to be touched, and `<link 5>` selects that link in whichever window asked about links. A console has no pointer of its own, so the window waiting is the window touched; where none is waiting, the line is typed into the game as it stands, which makes a misplaced click plain in the recording. The test suite plays every script in the directory whose game it can find, so the scripts double as regression tests for the interpreter. The games themselves live in the submodule and are not part of the repository, which is why a script whose game is absent is skipped rather than failed.

### The terminal program

```sh
dotnet run --project src/Rezrov.Tui -- entharion/zcode-infocom/zork1-r88-s840726.z3
```

This takes over the whole terminal, as Infocom's own interpreters did. A Version 3 game gets its status line across the top, later games get their upper window, and text is shown in the styles and colors the game asks for, as far as the terminal has them. Input is edited in place at the game's own cursor, timed input works, and the [MORE] pause appears when a screenful of text has gone by. Mouse clicks are reported to games that ask for them, as the standard's section 10.3 describes, so Journey's and Zork Zero's menus can be clicked as well as typed. When the game turns on a transcript, saves, or restores, a file dialog asks where. Ctrl+Q leaves at any time, and when the game ends by itself the screen stays until a key is pressed, so the last of the text can be read.

The same `--blorb`, `--commands`, `--transcript`, `--record`, `--save`, `--seed`, `--interpreter`, and `--tandy` options work here as on the command line, and a `.zblorb` can be given directly. Text pasted into the terminal is typed into the game a character at a time, line endings included, so a command copied from a walkthrough runs on arrival; where the terminal leaves the key to the program, Ctrl+V pastes from the clipboard instead. Version 6 games get their windows laid out on the terminal's grid, with the game measuring in units that are a quarter of a cell across and a whole cell down, which is the shape Infocom's games assume when they have no pictures to draw. The character graphics font of the standard's section 16, which Journey borders its screens with and Beyond Zork draws its map and its runes in, is shown with the nearest Unicode box drawing, block, arrow, and runic characters, as far as the terminal's font has them. Sounds play here, both the Z-Machine's sound effects and Glk's sound channels: the AIFF recordings a Blorb file carries are decoded, mixed, and resampled to the audio device, several at once, each with its own volume, and the game is told when one ends so that The Lurking Horror and Sherlock behave as they did. Each platform has its own way of making noise, and none of them needs a library the system does not already have, so the published program stays a single file: waveOut on Windows, the simple interface of PulseAudio on Linux, which the PipeWire systems that replaced it still answer, and an audio queue from AudioToolbox on macOS. On a machine with no device available the game is told there is no sound, as it is told when there are no pictures, and plays on without. What the terminal program does not do is show pictures, which are the graphical program's to draw.

Glulx games run here too, from a `.ulx` or a `.gblorb`. Their windows are laid out on the terminal as the game splits them: a status line or a menu in a text grid window stays where the game put it, text buffer windows wrap their text and scroll, and side by side windows share the rows. The mouse works: touching a text grid window that asked for it tells the game which character was touched, and touching a link tells the game which link, wherever the link is. Emphasis shows as italic and headings as bold, input is edited in the window that asked for it, the [MORE] pause works per window, and resizing the terminal lays the windows out again and tells the game, as the Glk specification's arrangement events do. File prompts go through the same dialogs. Graphics windows are not opened, since a terminal has nothing to draw them with, and a game that asks first is told so.

Dialog games run here too, and this is where they look the way their authors meant them to. The Å-machine's output model is closer to Glk's than to a Version 3 status line: text is divided into nested divs and spans, each carrying a style class out of the story's own style sheet, and a game may write into a status area across the top of the screen. All of that is honored as far as a grid of characters can honor it. A div's top and bottom margins become blank lines, its left and right margins and its width in characters become the width its text is wrapped to, and a class asking for centering, for capitals, or for heavier or leaning text gets them. The status area is as many rows tall as its style class asks for, is emptied each time the game enters it, as the specification says entering it means, and shows in reverse video; a box the style sheet floats to the right of it, which is where the games put a score, is set against the right of the row. What a terminal cannot do the game is told about before it tries: there are no pictures, so an embedded resource comes out as the words the story carries in its place, and there is nothing to click, so link text is ordinary text. The `--commands`, `--save`, and `--seed` options work here, and the options belonging to the other two machines are refused with a note.

### The graphical program

```sh
dotnet run --project src/Rezrov.Gui -- entharion/glulx-code/anchorhead-r1-s171017.gblorb
```

This opens a window and plays either machine in it, which is what finally lets a game show its pictures. A Glulx game gets its windows laid out as it splits them, its prose wrapped and scrolled with a proportional face, its text grids in a fixed one, and the eleven Glk styles drawn as the game's style hints ask: heavier or lighter, leaning, larger or smaller, indented, centered, in colors of the game's choosing, or reversed, which is how most games ask for the inverse status line every player expects. Pictures are drawn in graphics windows and in the run of a text buffer's text, inline with the words or in either margin with the text flowing around them, so Anchorhead's hundred and three illustrations appear where its author put them. Links are drawn in blue and underlined and can be clicked, pictures in links included, and a window that asked to be touched is told which character or which pixel the pointer landed on. The mouse wheel scrolls a text buffer back through what has gone by. Version 6 games get their pictures too, drawn at the exact unit rectangle the game asked for rather than rounded to whole characters, and a picture that takes its colors from whatever was drawn before it gets those colors, which is how Arthur and Zork Zero shade one set of drawings several ways. Beyond Zork, the one Infocom game outside Version 6 to carry any artwork, gets the title screen its Amiga and Macintosh releases put up while the game loaded: the game never draws it, so the interpreter does, filling the window until a key is pressed, at the start and again whenever the game restarts. An Arcturus game gets its arc_image band across the top, the text and the status line below it, and the band comes and goes as the game walks from a room with a picture to one without. Sounds play as they do in the terminal, file prompts open the system's own dialogs, and Ctrl+V pastes into the game a character at a time.

The window opens in the middle of the screen, large enough for a hundred and twenty characters by forty in whatever font is in use, or as much of that as the screen has room for. A Version 6 game says what shape of screen its pictures were drawn for, and the window is brought into that shape, so Shogun's title screen and the side panels of all four of them fill the window rather than stopping short in a band of background. Resizing it tells the game, so a Glk game lays its windows out again and a Z-machine game hears that its screen changed.

The map goes beside the game here rather than into a file. Control and M opens it and closes it again, `--map` has it open from the start, and the game keeps every column it had until it is asked for. What it draws is the map described above, freed of the character grid it had to be written into: a northeast passage is a line running northeast rather than a staircase of cells, and a way in or out, which has nowhere to point on a grid at all, is simply a line between the two rooms it joins. A way up, down, in or out is drawn broken and lettered at each end, because those rooms were put where there was space rather than where the passage points, and a passage walked only one way keeps its arrowhead. The map follows the player: while the whole game will go in the pane and still be read it shows the whole game, and past that it keeps the room the player is standing in in the middle, at a size their names can be read at. Dragging the map moves it and the wheel zooms about the pointer, after which it stays where it was put, since a view that jumped every turn would be worse than no view; `fit` puts the whole game on screen however small that makes it, and pointing at a room names it in full in the bar above, for the boxes too small to hold a name. `tidy` is the straightening the terminal does before it writes a file, and here it is asked for rather than done, because rooms keep the cells they were first placed in and a map that rearranged itself every time the player walked through a door would be no use for the one thing a map is for. It is built from the first turn whether or not the pane has ever been opened, so opening it halfway through a game shows the whole way there rather than the one room the player happens to be in. An Å-machine story says where the player is in no way any of this can read, and the pane says so rather than showing an empty map.

The `--blorb`, `--seed`, `--interpreter`, and `--tandy` options work here as elsewhere, the last two for the Z-machine games that read them, among them Beyond Zork, which picks how to draw its map by which machine it is told it is on. Four more settle how the text looks:

```sh
dotnet run --project src/Rezrov.Gui -- game.gblorb --font "Iowan Old Style, Charter, Georgia, serif" --size 18
```

| Option | What it does |
| --- | --- |
| `--font <family>` | the family the prose of a text buffer is set in |
| `--fixed <family>` | the family text grids and the preformatted style are set in |
| `--size <pixels>` | the size of ordinary text, from 6 to 72 |
| `--smoothing <mode>` | `subpixel`, `grayscale`, or `none` |

A family may be a list rather than one name, in which case the first of them the machine actually has is the one used, and a generic name at the end is always there. The defaults are `Georgia, Palatino, Times New Roman, serif` for the prose, `Consolas, Menlo, DejaVu Sans Mono, monospace` for the fixed text, a size of 16, and subpixel smoothing. Smoothing is worth knowing about: the program draws with the software renderer, so that the published folder need not carry a five megabyte graphics library it would hardly use, and what that renderer would choose on its own is gray antialiasing, which leaves a serif face at reading size looking thinner and paler than every other window on the machine. Asking for subpixel gets the same rendering the rest of the desktop uses; a display that cannot do it falls back to gray.

`--probe` takes the same four options and prints what they measure without opening a window:

```sh
dotnet run --project src/Rezrov.Gui -- --probe --font "Segoe UI" --size 18
```

It reports the size of a character cell and, for a few of the styles, how wide a space is, how tall a line is, where its baseline sits, and whether the parts of a phrase add up to the whole. It exits nonzero when they do not, which is the one thing about a window that a script can check: the whole layout is built on those numbers, and they can be wrong in ways that are perfectly quiet on the screen.

### The grid program

```sh
dotnet run --project src/Rezrov.Gtui -- entharion/zcode-infocom/zork1-r88-s840726.z3
```

This plays a Z-machine game in a window as a grid of characters, and it is the one program here that uses nothing at all: no package is referenced, the window comes from the operating system directly, and every pixel on the screen was decided by code in `src/Rezrov.Gtui`. It opens its own window on Windows through the Win32 API, on Linux and the BSDs through X11, and on macOS through the Objective-C runtime, in under five hundred lines each. The four programs are meant to be read in that order: the command line program shows what an interpreter needs at its barest, the terminal program adds the screen model and lets the terminal draw, the graphical program hands the window and the drawing to a toolkit, and this one shows what the toolkit was doing.

Because it owns its pixels it carries two fonts of its own. The character graphics font of the standard's section 16 is drawn from the bitmaps the standard prints, rather than from the nearest box-drawing characters an ordinary font happens to have, which is as close to Infocom's own shapes as a frontend gets. Ordinary text is set in an eight by sixteen font drawn for this program, where a letter occupies seven columns and the eighth is the gap to the next one, so bold can be the same letter drawn again a pixel to the right and italic a one pixel lean without either running into its neighbor. Both fonts can be read without playing anything:

```sh
dotnet run --project src/Rezrov.Gtui -- --glyphs
dotnet run --project src/Rezrov.Gtui -- --glyphs "The quick brown fox"
```

The `--seed`, `--interpreter`, and `--tandy` options work here as elsewhere. Saving and restoring ask for the file name in the window itself, the way Infocom's interpreters did, since a file dialog is a toolkit and there is none here; the name offered is the story's own, so saving is one keystroke. What it does not do is play Glulx, or make any sound, or draw a Version 6 game's pictures, which is why it tells such a game there are none and gets Infocom's four in their text-only modes; a Dialog game's pictures it does draw, since the Å-machine hands it a picture already decoded and a grid of cells is somewhere to put one. The window cannot be resized on macOS. The other three programs are the complete ones; this is the one that shows how a window is made.

## Building and Testing

From the repository root:

```sh
dotnet build
dotnet test
```

Neither needs the `entharion` submodule described further down, so a plain clone builds and tests without pulling several hundred megabytes of reference material.

The three interpreter libraries are built optimized even in the Debug configuration, because the test suite replays whole games through them and an unoptimized interpreter makes that run take minutes rather than seconds. The programs and the tests are not, so debugging them is as usual. To step through the interpreter code itself with every local in view, build with optimization off for that session:

```sh
dotnet build -p:Optimize=false
```

Tests are xUnit v3 on Microsoft Testing Platform, which means a test project is a real executable rather than a library loaded by a separate runner. You can run one directly, and it reports more detail than `dotnet test` does:

```sh
dotnet run --project tests/Rezrov.Tests
```

One thing worth knowing in advance, because the failure is misleading. In this mode `dotnet test` forwards any option it does not recognize to the test executable, which then rejects it. So `dotnet test -nologo` fails with "Zero tests ran" and exit code 5 rather than with a complaint about the flag. Other options from the VSTest era behave the same way. Plain `dotnet test` is the safe form.

### Speed

Fast enough that it has never been worth optimizing, which is worth writing down so that nobody wonders. On an AMD Ryzen 7 9800X3D, a release build on .NET 10 plays Zork Zero's whole recorded walkthrough, 1853 commands, in about 1.4 seconds, which is well under a millisecond a turn. On the Glulx side, Anchorhead's 722 command walkthrough executes 680 million virtual machine instructions in about 16 seconds, which is roughly 41 million instructions a second; a debug build measures the same within the noise.

The interesting part of that is the ratio rather than the rate. Anchorhead spends about 940,000 instructions on a single turn, some twenty milliseconds, where Advent, running on the same Glulx engine, spends about 18,000: five and a half million instructions over its whole 298 command walkthrough against Anchorhead's 680 million over 722. Fifty times the work for a turn of the same game, and the difference is not the interpreter but the game. Anchorhead is Inform 7 with deep rulebooks and Advent is Inform 6.

The same gap shows on the Z-Machine, which is the better demonstration because both games run on the older and simpler of the two machines. Zork Zero, which is about as much as Infocom ever asked of it, takes well under a millisecond a turn. Bronze, which is Inform 7 compiled to the same machine, takes about thirty-four. A player notices none of this. What it does mean is that two games, Bronze and Anchorhead, account for most of the time the test suite takes, so if that ever becomes annoying, that is where the time is.

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

A release is a version tag. The release workflow builds all four programs with NativeAOT on Windows, Linux for x64 and arm64, and macOS for both Intel and Apple silicon joined into a universal binary with `lipo`. It makes two archives per platform, one holding the three single-file programs and one holding the graphical program with the native libraries that must sit beside it, each with its own readme and the license, and publishes a GitHub Release with the archives and their checksums attached. The steps:

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
- `dialog-code/` and `arcturus-code/` hold the games of the other two languages, each with its source beside it. Dialog's are both Å-machine stories and Z-Machine ones, since one compiler emits either; Arcturus's are ordinary Version 5 files with their arc_image pictures in a Blorb beside them or wrapped around them. Both directories have a build script in Entharion's `scripts/` that rebuilds them from source.
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
