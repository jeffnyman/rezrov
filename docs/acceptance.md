# Acceptance scripts

A seed and a command file together make a game play the same way every time, and an acceptance script is those two things in one small file, with the play it produced kept beside it so that later runs can be checked against it. The scripts in the `acceptance` directory are the interpreter's own; this is one of them:

```text
! SEED=20
! GAME=../entharion/zcode-infocom/zork1-r2-sAS000C.z1

n. n. u
get egg
```

A line beginning with `!` is a directive. `GAME` names the story file and `SEED` gives the session seed, as `--seed` does, and both are required. `BLORB` names a resource file when the one beside the story isn't the right one, and `INTERPRETER` names the machine, as `--interpreter` does, for a game that plays differently by it. `TANDY` set to `yes` sets the Tandy bit, as `--tandy` does. `UPPER` set to `yes` puts the upper window into the play: whenever the game pauses for input and has changed that window since it was last shown, its rows are written out, which is how a game like Custard, which draws all of its text there, can be scripted at all. `PICTURES` set to `yes` tells a Version 6 game the screen can show pictures, which sends Infocom's four down their graphical paths instead of their text-only ones; a stream of text can't show a picture, so what the play records is which pictures are on the screen and where, written out whenever that changes. That's what catches a picture wrongly erased or left behind when the text scrolls, which is the way a Version 6 game goes wrong. An Å-machine game is scripted the same way, with the seed and the commands; the directives that belong to the other two machines, which are `INTERPRETER`, `TANDY`, `UPPER`, `PICTURES`, and `BLORB`, mean nothing to it and a script that carries one is told so. Paths are relative to the script, not to wherever you run it from. A line beginning with `#` is a comment, a blank line is ignored, and every other line is a command in the same format as a command file, so a game that reads single keys can be given them as bracketed codes, or by name for the keys a menu is worked with: `<up>`, `<down>`, `<left>`, `<right>`, `<escape>`, and `<space>`, one press per line. Two more of that shape are for a Glulx game that watches the pointer: `<click column,row>` and `<link value>`. A command may be written with the prompt in front, as `> look`, which reads like a transcript and is also how to give a command that itself begins with `#`, `!`, or `<`.

A stretch of the script can be fenced off so that it isn't played. A line of three backticks opens the fence, a bare line of three backticks closes it, and a fence left open runs to the end of the file. A label after the opening backticks is allowed, so a script can keep an alternative path through the game beside the one it plays:

````text
open trapdoor

```PATH 1
kill troll
take axe
```

save troll
````

Here "save troll" is played and the two commands under PATH 1 aren't. Swapping the fence to the other path later is a matter of moving the backticks.

A long script can change its seed partway. A `SEED` line after some commands takes effect when the play reaches it, before the next command, and the seed before it governs everything above. That means each stretch of a game can have the seed that makes it go the way it should, and once a fight with the troll is settled, hunting for a seed that gets you past the thief never disturbs it. A `SEED` line after the last command applies as the script ends, which matters with `--resume`.

While a script is being written, `--resume` plays it and then leaves the game running at the console, with a note on standard error marking where the script ended, so the next commands can be tried before they go into the file. Nothing is checked or recorded in that mode; the game runs until it quits or the console runs out, and the saved game, if there's one, is still the one held in memory from the script.

```sh
rezrov --accept acceptance/zork1-r2-sAS000C.accept
rezrov --accept acceptance/zork1-r2-sAS000C.accept --resume
```

The play is shown as it happens, so that when you're building a script up you can see where a command went wrong, and the verdict comes at the end. The first run records what came out as `zork1-r2-sAS000C.expected` beside the script. Every run after plays it again and compares, saying which line of the recording differs and which line of the script was being played at that point, and exiting with 1 when something has changed. When the change is one you meant, `--update` after the script records it afresh. A script that has grown since it was recorded is told apart from one whose play has changed: the run says that the recording ends before the script does, and `--update` records the rest. The recorded text is everything the game printed, commands included, and after it a line for each thing the interpreter noticed: a runtime error the game made, or the point at which the game reached something not implemented yet. That way a game that starts misbehaving, or stops, changes the text and shows up. Saving and restoring work within a run, with the saved game kept in memory, so a script can test both.

A script may name a Glulx game as easily as a Z-Machine one. It plays through Glk on the console's text display, its commands answering whatever the game asks for, a line or a key, and any file it saves is kept in memory for the length of the run, so a script can save and restore without leaving anything behind. A script can also point at a game that expects a pointer: `<click 3,1>` touches the character at a column and row of whichever window asked to be touched, and `<link 5>` selects that link in whichever window asked about links. A console has no pointer of its own, so the window waiting is the window touched; where none is waiting, the line is typed into the game as it stands, which makes a misplaced click plain in the recording. The test suite plays every script in the directory whose game it can find, so the scripts double as regression tests for the interpreter. The games themselves live in the submodule and aren't part of the repository, which is why a script whose game is absent is skipped rather than failed.
