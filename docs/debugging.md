# Reading and Debugging a Story File

This is not something the casual user of Rezrov needs to read. This is more when you want to learn about the internals of a story file itself. This was stuff that ultimately helped me build Rezrov in the first place.

## Reading a Story File

Rezrov can read a Z-Machine story file rather than play it. `--disassemble <file>` writes an account of the whole thing: first a memory map saying what every byte of it is for, and then every routine in it, in address order, with the stretches between them. A `-` in place of the file name sends the listing to standard output instead, so it can be read through a pager without leaving a file behind. A `.zblorb` may be given as easily as a bare story file, since the Z-code is taken out of it first.

Nothing in a story file says where its routines are. Following calls from the entry point finds barely a tenth of them, because these games dispatch their verbs through action tables and call through a variable operand, so the address is never written down anywhere a reader can see it, and playing the game only ever finds what was played. What works is to try every address a routine could be packed to and keep the ones whose header and body decode. Zork I comes out with 411 routines, which is about the number it has.

That method keeps the occasional thing that isn't a routine, so it's worth saying how often, and the only authority on what is code is a game running. Every instruction a game really executes has to land exactly on an instruction boundary inside some routine the scan found. Over the fifty-nine walkthroughs in this repository, 101,230 instructions are executed at distinct addresses; twenty-seven of them land inside a routine at the wrong offset, and 942 land where no routine was found at all. So the listing reads 99.97% of executed code correctly and finds 99% of it, and where a listing and a running game disagree, the running game is right. A call to a constant address counts for more than an address merely decoding, which is used both ways: it puts back the single-instruction routines the scan is too cautious to believe on their own, and where a call lands among the locals of a routine already found, it says the scan began that routine too early, since nothing ever calls into a table of locals.

Reading Zork I:

```sh
rezrov entharion/zcode-infocom/zork1-r88-s840726.z3 --disassemble zork1.lst
```

```text
rezrov: listed 411 routines and 54 gaps to zork1.lst
```

The listing begins by saying what it read and what it found in it:

```text
; Version 3, release 88, serial 840726, checksum A129
; dynamic memory at 0000, static at 2E53, high at 4E37
; 84876 bytes in the file, execution begins at 4F05
; 411 routines, 54 gaps, 177 bytes of padding
```

The listing itself is written to be read. A branch or a jump that stays inside its routine is given a name rather than a number, and one that goes elsewhere is left as an address, so a label never points at nothing. A call to a constant address says which routine it reaches. Text an instruction carries is shown beside it, and text a print opcode names by address is shown as a comment. The few opcodes that take a variable by reference, where the operand is the number of a variable rather than a value, print as the variable they name, so `inc` says which local it's incrementing. A stretch of high memory that no routine claims says how many encoded strings exactly fill it, which is usually all of them, since high memory holds the text as well as the code; a stretch that doesn't read as text is reported as so many bytes and nothing is claimed about them. The bytes lost to packing routines onto their boundaries are counted separately rather than called gaps, because a byte that exists only so the next routine can begin on an even address isn't a hole in anything.

Zork I's restore and save are seven instructions and four, and between them they show most of that:

```text
gap 6E4B, 7 bytes, 2 strings

routine 6E52, 0 locals
       6E53  restore        ?~L1
       6E55  print         "Ok."
       6E5A  new_line
       6E5B  call          #4644 -> sp  ; routine 8C88
       6E60  ret_popped
L1:    6E61  print_ret     "Failed."

routine 6E68, 0 locals
       6E69  save           ?~L1
       6E6B  print_ret     "Ok."
L1:    6E70  print_ret     "Failed."
```

The seven bytes above them hold two strings and no code. Both routines branch on failure to a label with a name rather than an address, both carry their text beside them, and the call at 6E5B says which routine it reaches, so the listing can be walked from here without working a packed address out by hand.

The memory map is the same honesty applied to the rest of the file. Most of what the header names can be sized exactly: the abbreviations are a fixed count of pointers, the globals are 240 words, the dictionary states how many entries it has and how long each one is, and the object entries end where the first property table begins. Whatever is left over is listed as unaccounted rather than explained away, so the account always adds up to the length of the file. Regions are allowed to overlap, and where they do both are shown, which isn't a hypothetical: Zork Zero puts its object table twelve bytes inside the 240 words reserved for globals, because the game never uses the last few, and a map that quietly dropped one of the two would be hiding the interesting part.

Zork I adds up like this:

```text
address   bytes  region
0000         64  header
0040        432  abbreviation text
01F0        192  abbreviation pointers
02B0         62  property defaults
02EE       2250  object entries
0BB8       5817  property tables
2271        480  global variables
2451       5840  unaccounted
3B21       4886  dictionary
4E37      64853  code and text

84876 bytes in the file, 5840 unaccounted, 0 claimed twice
```

The 5840 bytes between the globals and the dictionary are named as unaccounted rather than explained away. Zork Zero is where the overlap shows, in two rows that run into each other and in the count beneath them:

```text
02A2        480  global variables
0476        126  property defaults

300032 bytes in the file, 10755 unaccounted, 12 claimed twice
```

Property defaults are the beginning of the object table, and they begin twelve bytes before the globals end.

## Debugging a Game

`--debug` plays a Z-Machine game with a debugger's prompt beside it. The game's text goes where it always goes and the debugger's own lines go to standard error, so the two can be told apart and a transcript of the play stays a transcript. The game and the debugger take turns at the same console: when the game stops for a command you're typing to the game, and when the debugger prompts you're typing to the debugger, which is the whole of how they share a terminal.

At that prompt, `break` sets a breakpoint, `step` carries out one instruction, `next` does the same but over a call rather than into it, `finish` runs until the routine returns, and `continue` runs on. `where` shows the routines the game is inside, newest first. `list` shows the instructions around wherever the game is, with an arrow at the one about to run, and `list <address>` shows them around somewhere else and stays there until the game moves. `locals`, `globals`, `stack`, and `read` show the current routine's variables, the globals the game has actually written to, what this routine has pushed, and sixteen bytes of memory. `watch G3C` keeps an eye on a global, or on the word at a plain address, and stops the game at the instruction that changes it; `unwatch` gives that up. Addresses are written in hexadecimal, because every address the listing shows is in hexadecimal and asking for them in decimal would mean converting by hand. Typing the address the listing gives for a routine sets the breakpoint at its first instruction rather than at its table of locals, since the program counter never holds the latter and nobody means it.

Most of that on Zork I, before the game has read anything, by breaking on a routine the opening calls:

```text
(rezrov) delete
; 3 breakpoints gone
(rezrov) break 5472
; stopping at 5479, where routine 5472 begins
(rezrov) continue
; stopped
       5479  call          #2A43, L00 -> L02  ; routine 5486
(rezrov) where
  2  5479  in routine 5472
  1  4F0E  in routine 4F04, from the listing
(rezrov) locals
;  L00 8010  L01 FFFF  L02 0000
(rezrov) next
       547F  storew        L02, #01, L01
(rezrov) next
       5484  ret           L02
(rezrov) finish
       4F0E  storew        sp, #00, #01
```

5472 is the address the listing gives for that routine, and the debugger says where it really put the breakpoint, which is 5479, past the three locals. The locals it then shows are the arguments the opening passed: the listing of routine 4F04 has `call #2A39, #8010, #FFFF`, and here they are as `L00 8010` and `L01 FFFF`.

Watching a value is how the most common question about a story gets answered. Which code puts the lamp out, moves the thief, or sets the score is a question about a value rather than about an address, and the way to answer it is to name the value and let the game say where. The watch is on the bytes rather than on the opcode, so a `store`, a `storew`, and a `storeb` that lands on either half of the word all count, and the run comes back at the instruction after the one that did it, with that instruction still on the screen above. Restoring a saved game replaces the whole of dynamic memory and isn't reported as the game changing anything, because it isn't. Nothing is watched until you ask, and while nothing is watched the cost is one test against null on each write: the longest walkthrough in the repository, three and a quarter million instructions, runs in the same time to within the noise either way.

Zork I gives ten points for getting into the house. Naming the global its status line scores from and then walking in says which instruction awarded them, and what the game was in the middle of at the time:

```text
(rezrov) delete
; 3 breakpoints gone
(rezrov) watch G01
; watching G01, which holds 0000
(rezrov) continue

West of House
You are standing in an open field west of a white house, with a boarded front door.
There is a small mailbox here.

>superbrief
Super-brief descriptions.
>n
North of House
>e
Behind House
>open window
With great effort, you open the window far enough to allow entry.
>w
; G01 changed from 0000 to 000A
       906D  je            G01, #015E ?~rtrue
(rezrov) where
  7  906D  in routine 9062
  6  90CD  in routine 90BA
  5  940C  in routine 92B6
  4  8B4A  in routine 8AA4
  3  5871  in routine 577C
  2  55AC  in routine 552A
  1  4F9E  in routine 4F04, from the listing
```

Ten in hexadecimal is 000A. The run comes back at 906D, one instruction past the `add G01, L00 -> G01` in routine 9062 that did it, and the seven frames are the whole way down from the opening routine to the one that scores.

A session begins by stopping wherever the game takes a command, which the disassembly finds for you, because a game is debugged a turn at a time and because a `continue` with nothing set would otherwise disappear into the game with no way back. `delete` with no address gives that up for a free run.

The window program takes the same option and lays itself out for the work rather than for the game: the listing and the variables across the top, the game with the call chain and whatever is being watched beside it below them, and the debugger's own prompt across the bottom, with whatever it has said kept above that. The game gets a panel and is told the size of it, exactly as it's told the size of a window that has been made smaller. There's no map in that layout, so the two never have to share a window; `rezrov-gui game.z3` is still the whole window of game it always was. Every panel scrolls on the wheel, the instruction about to be carried out is marked in the listing and kept in view as the game moves, and the up arrow at the prompt walks back through what has been typed. Wherever the debugger has written a routine down, the line can be clicked to go and read it: the comment beside a call, the head of a listing, and every frame of the call chain, which between them are enough to walk a game's call graph by pointing at it. The pointer says which lines lead somewhere by turning into a hand over them. A click is the `list` command and nothing more, so it's recorded in the log like anything else, and the listing stays where it was sent until the game is moved again or `list` is typed with no address, either of which brings it back to following the game. While a command is running the prompt says so and takes nothing, because the commonest reason a command takes any time at all is that the game has stopped to ask the player something, and then it's the game that wants the keyboard.

Both programs are the same debugger underneath. A typed line goes in and the text of its answer comes back, and the window simply asks for all the answers at once rather than one at a time, so the two can never disagree about what the game is doing. In the window that gathering happens on the thread the game runs on and is handed over as plain text, which is the same arrangement the map uses and for the same reason: nothing that draws ever reads a game that's moving.

The debugger is built on three things the interpreter now says about itself: the call chain, with each frame recording the routine it was called into; a set of addresses a run stops before reaching; and a run that comes back when the call chain unwinds to a given depth, which is all that stepping over a call and running to a return actually need. Checking a set of addresses at every instruction was measured before it was written and costs nothing that can be told from noise, so there's no fast path around it and a game being played simply has an empty set.
