using System.Text;
using Rezrov.Core.Blorb;
using Rezrov.Glulx.Execution;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// The Glk library: windows, streams, and the functions a game calls
/// through the glk opcode.
/// </summary>
/// <remarks>
/// [glk #intro] Glk is the portable interface the game talks to for
/// everything to do with the player: windows, text, and input. This is
/// the library half of that; the frontend half is whatever
/// <see cref="IGlkDisplay"/> it was given, and the files are whatever
/// <see cref="IGlkFileSystem"/> it was given. What is built so far is
/// the dispatch layer with every function's prototype, the window tree
/// and its layout, window, memory, and file streams, file references,
/// text output in Latin-1 and Unicode, styles and style hints, the
/// gestalt answers, the case functions, and line, character, and timer
/// events, and resource streams over the <see cref="Resources"/> it
/// was given. Date and time, graphics, sound, and hyperlinks throw
/// <see cref="NotSupportedException"/> naming the function, so a game
/// stops at the first one it needs.
///
/// Glk's rule for a program that breaks the rules, such as printing to
/// a closed stream, is that the library's behavior is undefined; the
/// reference libraries print a warning and carry on, and so does this
/// one, into <see cref="Warnings"/>.
/// </remarks>
public sealed class GlkLibrary
{
    /// <summary>
    /// [glk #version] The version of the Glk specification this library
    /// follows.
    /// </summary>
    public const uint SpecificationVersion = 0x00000706;

    private readonly IGlkDisplay _display;
    private readonly Dictionary<uint, GlkPrototype> _prototypes = [];
    private readonly Dictionary<(WindowType Type, GlkStyle Style, StyleHint Hint), int> _styleHints = [];

    // [glk #timer_events] The interval, zero for no timer, and when the
    // last timer event was given out, on the clock's timestamps.
    private readonly TimeProvider _clock;
    private uint _timerInterval;
    private long _timerStarted;

    /// <param name="display">The frontend's display.</param>
    /// <param name="files">
    /// The frontend's files, or none for files kept in memory for the
    /// session.
    /// </param>
    /// <param name="clock">
    /// The clock the timer runs on, or none for the system's; a test
    /// gives one it can move by hand.
    /// </param>
    public GlkLibrary(IGlkDisplay display, IGlkFileSystem? files = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        _clock = clock ?? TimeProvider.System;
        Files = files ?? new MemoryGlkFileSystem();
    }

    /// <summary>[glk #fileref] Where the files are.</summary>
    public IGlkFileSystem Files { get; }

    /// <summary>
    /// [glk #resource_streams] The resource file the game came with,
    /// whose data chunks resource streams read, or null for none.
    /// </summary>
    public BlorbFile? Resources { get; set; }

    /// <summary>
    /// [glk #window] Every window, pair windows included.
    /// </summary>
    public GlkRegistry<GlkWindow> Windows { get; } = new();

    /// <summary>
    /// [glk #stream] Every stream, window streams included.
    /// </summary>
    public GlkRegistry<GlkStream> Streams { get; } = new();

    /// <summary>[glk #fileref] Every file reference.</summary>
    public GlkRegistry<GlkFileReference> FileReferences { get; } = new();

    /// <summary>
    /// [glk op:window_get_root] The root window, or null for none.
    /// </summary>
    public GlkWindow? Root { get; private set; }

    /// <summary>[glk #stream] The current output stream, or null.</summary>
    public GlkStream? CurrentStream { get; private set; }

    /// <summary>[glk op:exit] Whether the game asked to end.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>
    /// [glk op:request_timer_events] The timer interval in milliseconds,
    /// zero for none.
    /// </summary>
    public uint TimerInterval => _timerInterval;

    /// <summary>
    /// What the game did that the specification calls illegal.
    /// </summary>
    public List<string> Warnings { get; } = [];

    // ----- The dispatch layer -----

    /// <summary>
    /// [glulx op:glk] Calls the function with a selector, with the
    /// arguments the machine popped for it, and returns its result.
    /// </summary>
    /// <exception cref="GlulxException">There is no such
    /// function.</exception>
    /// <exception cref="NotSupportedException">The function is not built
    /// yet.</exception>
    public uint Call(uint selector, uint[] arguments, GlulxMemory memory, GlulxStackSpace stack)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var function = GlkSelectors.Find(selector)
            ?? throw new GlulxException($"Unknown Glk function {selector:X4}.");

        if (!_prototypes.TryGetValue(selector, out var prototype))
        {
            prototype = GlkPrototype.Parse(function.Prototype);
            _prototypes[selector] = prototype;
        }

        var call = new GlkCall(function, prototype, arguments, memory, stack);
        Invoke(call);
        call.WriteOutputs();
        return call.Result;
    }

    private void Invoke(GlkCall call)
    {
        switch (call.Function.Selector)
        {
            case 0x0001: // exit
                ExitRequested = true;
                break;
            case 0x0002: // set_interrupt_handler
            case 0x0003: // tick
                break;
            case 0x0004: // gestalt
                call.Result = Gestalt(call.Arg(0), call.Arg(1), null);
                break;
            case 0x0005: // gestalt_ext
            {
                var extra = call.ArrayAddress(2) == 0 ? null : new uint[call.ArrayLength(2)];
                call.Result = Gestalt(call.Arg(0), call.Arg(1), extra);
                for (var i = 0; extra is not null && i < extra.Length; i++)
                {
                    call.Memory.WriteWord(call.ArrayAddress(2) + (uint)(4 * i), extra[i]);
                }

                break;
            }

            case 0x0020: // window_iterate
            {
                var next = Windows.Next(call.ObjectId(0) == 0 ? null : Window(call, 0));
                call.Out(1, next?.Rock ?? 0);
                call.Result = next?.Id ?? 0;
                break;
            }
            case 0x0021: // window_get_rock
                call.Result = Window(call, 0)?.Rock ?? 0;
                break;
            case 0x0022: // window_get_root
                call.Result = Root?.Id ?? 0;
                break;
            case 0x0023: // window_open
            {
                var split = call.ObjectId(0) == 0 ? null : Window(call, 0);
                if (call.ObjectId(0) != 0 && split is null)
                {
                    break;
                }

                call.Result = OpenWindow(split, (WindowMethod)call.Arg(1), call.Arg(2), (WindowType)call.Arg(3), call.Arg(4))?.Id ?? 0;
                break;
            }
            case 0x0024: // window_close
                if (Window(call, 0) is { } closing)
                {
                    var (read, written) = CloseWindow(closing);
                    call.Out(1, read, written);
                }

                break;
            case 0x0025: // window_get_size
                if (Window(call, 0) is { } measured)
                {
                    call.Out(1, (uint)measured.Width);
                    call.Out(2, (uint)measured.Height);
                }

                break;
            case 0x0026: // window_set_arrangement
                if (Window(call, 0) is { } arranged)
                {
                    var key = call.ObjectId(3) == 0 ? null : Window(call, 3);
                    SetArrangement(arranged, (WindowMethod)call.Arg(1), call.Arg(2), key);
                }

                break;
            case 0x0027: // window_get_arrangement
                if (Window(call, 0) is PairWindow pair)
                {
                    call.Out(1, (uint)pair.Method);
                    call.Out(2, pair.Size);
                    call.Out(3, pair.Key?.Id ?? 0);
                }
                else if (Window(call, 0) is not null)
                {
                    Warn("window_get_arrangement: not a pair window.");
                }

                break;
            case 0x0028: // window_get_type
                call.Result = (uint)(Window(call, 0)?.Type ?? 0);
                break;
            case 0x0029: // window_get_parent
                call.Result = Window(call, 0)?.Parent?.Id ?? 0;
                break;
            case 0x002A: // window_clear
                Window(call, 0)?.Clear();
                break;
            case 0x002B: // window_move_cursor
                if (Window(call, 0) is TextGridWindow grid)
                {
                    grid.MoveCursor(call.Arg(1), call.Arg(2));
                }
                else if (Window(call, 0) is not null)
                {
                    Warn("window_move_cursor: not a text grid window.");
                }

                break;
            case 0x002C: // window_get_stream
                call.Result = Window(call, 0)?.Stream.Id ?? 0;
                break;
            case 0x002D: // window_set_echo_stream
                if (Window(call, 0) is { } echoing)
                {
                    var echo = call.ObjectId(1) == 0 ? null : Stream(call, 1);
                    SetEchoStream(echoing, echo);
                }

                break;
            case 0x002E: // window_get_echo_stream
                call.Result = Window(call, 0)?.EchoStream?.Id ?? 0;
                break;
            case 0x002F: // set_window
                // [glk op:set_window] The window's stream becomes current,
                // or none for no window.
                CurrentStream = call.ObjectId(0) == 0 ? null : Window(call, 0)?.Stream;
                break;
            case 0x0030: // window_get_sibling
                if (Window(call, 0) is { Parent: { } parent } child)
                {
                    call.Result = (ReferenceEquals(parent.First, child) ? parent.Second : parent.First).Id;
                }

                break;

            case 0x0040: // stream_iterate
            {
                var next = Streams.Next(call.ObjectId(0) == 0 ? null : Stream(call, 0));
                call.Out(1, next?.Rock ?? 0);
                call.Result = next?.Id ?? 0;
                break;
            }
            case 0x0041: // stream_get_rock
                call.Result = Stream(call, 0)?.Rock ?? 0;
                break;
            case 0x0043: // stream_open_memory
            case 0x0139: // stream_open_memory_uni
                call.Result = OpenMemoryStream(call.Memory, call.ArrayAddress(0), call.ArrayLength(0), call.Function.Selector == 0x0139, (FileMode)call.Arg(1), call.Arg(2))?.Id ?? 0;
                break;
            case 0x0044: // stream_close
                if (Stream(call, 0) is { } closingStream)
                {
                    var (read, written) = CloseStream(closingStream);
                    call.Out(1, read, written);
                }

                break;
            case 0x0045: // stream_set_position
                Stream(call, 0)?.SetPosition((int)call.Arg(1), (SeekMode)call.Arg(2));
                break;
            case 0x0046: // stream_get_position
                call.Result = Stream(call, 0)?.Position ?? 0;
                break;
            case 0x0047: // stream_set_current
                CurrentStream = call.ObjectId(0) == 0 ? null : Stream(call, 0);
                break;
            case 0x0048: // stream_get_current
                call.Result = CurrentStream?.Id ?? 0;
                break;

            case 0x0042: // stream_open_file
            case 0x0138: // stream_open_file_uni
                call.Result = OpenFileStream(FileReference(call, 0), call.Function.Selector == 0x0138, (FileMode)call.Arg(1), call.Arg(2))?.Id ?? 0;
                break;

            case 0x0049: // stream_open_resource
            case 0x013A: // stream_open_resource_uni
                call.Result = OpenResourceStream(call.Arg(0), call.Function.Selector == 0x013A, call.Arg(1))?.Id ?? 0;
                break;

            case 0x0060: // fileref_create_temp
                call.Result = CreateTemporaryFileReference(call.Arg(0), call.Arg(1)).Id;
                break;
            case 0x0061: // fileref_create_by_name
                call.Result = CreateFileReference(call.Arg(0), call.Text(1), call.Arg(2)).Id;
                break;
            case 0x0062: // fileref_create_by_prompt
                call.Result = PromptForFileReference(call.Arg(0), (FileMode)call.Arg(1), call.Arg(2))?.Id ?? 0;
                break;
            case 0x0068: // fileref_create_from_fileref
                call.Result = CopyFileReference(call.Arg(0), FileReference(call, 1), call.Arg(2))?.Id ?? 0;
                break;
            case 0x0066: // fileref_delete_file
                DeleteFile(FileReference(call, 0));
                break;
            case 0x0067: // fileref_does_file_exist
                call.Result = FileExists(FileReference(call, 0)) ? 1u : 0u;
                break;
            case 0x0063: // fileref_destroy
                if (FileReferences.Find(call.ObjectId(0)) is { } destroyed)
                {
                    FileReferences.Remove(destroyed);
                }
                else
                {
                    Warn($"fileref_destroy: invalid fileref id {call.ObjectId(0)}.");
                }

                break;
            case 0x0064: // fileref_iterate
            {
                var next = FileReferences.Next(call.ObjectId(0) == 0 ? null : FileReferences.Find(call.ObjectId(0)));
                call.Out(1, next?.Rock ?? 0);
                call.Result = next?.Id ?? 0;
                break;
            }
            case 0x0065: // fileref_get_rock
                call.Result = FileReferences.Find(call.ObjectId(0))?.Rock ?? 0;
                break;
            case 0x00F0: // schannel_iterate
                // [glk #sound_testing] There are no sound channels, so
                // there is nothing to iterate.
                call.Out(1, 0);
                call.Result = 0;
                break;

            case 0x0080: // put_char
                PutChar(call.Arg(0) & 0xFF);
                break;
            case 0x0081: // put_char_stream
                PutChar(Stream(call, 0), call.Arg(1) & 0xFF);
                break;
            case 0x0082: // put_string
                PutString(CurrentStream, call.Text(0));
                break;
            case 0x0083: // put_string_stream
                PutString(Stream(call, 0), call.Text(1));
                break;
            case 0x0084: // put_buffer
                PutBuffer(CurrentStream, call, 0);
                break;
            case 0x0085: // put_buffer_stream
                PutBuffer(Stream(call, 0), call, 1);
                break;
            case 0x0086: // set_style
                SetStyle(CurrentStream, (GlkStyle)call.Arg(0));
                break;
            case 0x0087: // set_style_stream
                SetStyle(Stream(call, 0), (GlkStyle)call.Arg(1));
                break;

            case 0x0090: // get_char_stream
                call.Result = (uint)GetChar(Stream(call, 0), false);
                break;
            case 0x0091: // get_line_stream
                call.Result = GetLine(Stream(call, 0), call, 1, false);
                break;
            case 0x0092: // get_buffer_stream
                call.Result = GetBuffer(Stream(call, 0), call, 1, false);
                break;

            case 0x00A0: // char_to_lower
                call.Result = CharToLower((byte)call.Arg(0));
                break;
            case 0x00A1: // char_to_upper
                call.Result = CharToUpper((byte)call.Arg(0));
                break;

            case 0x00B0: // stylehint_set
                SetStyleHint((WindowType)call.Arg(0), (GlkStyle)call.Arg(1), (StyleHint)call.Arg(2), (int)call.Arg(3));
                break;
            case 0x00B1: // stylehint_clear
                ClearStyleHint((WindowType)call.Arg(0), (GlkStyle)call.Arg(1), (StyleHint)call.Arg(2));
                break;
            case 0x00B2: // style_distinguish
            case 0x00B3: // style_measure
                // [glk #stream_style_check] The display shows no styles
                // apart, and can measure none, so both answer no.
                call.Result = 0;
                break;

            // [glk #event] Waiting for the player, and the requests that
            // say what to wait for.
            case 0x00C0: // select
                Report(call, 0, Select());
                break;
            case 0x00C1: // select_poll
                Report(call, 0, SelectPoll());
                break;
            case 0x00D0: // request_line_event
            case 0x0141: // request_line_event_uni
                if (Window(call, 0) is { } lineWindow)
                {
                    RequestLineEvent(lineWindow, call.Memory, call.ArrayAddress(1), call.ArrayLength(1), call.Arg(2), call.Function.Selector == 0x0141);
                }

                break;
            case 0x00D1: // cancel_line_event
                if (Window(call, 0) is { } cancelled)
                {
                    Report(call, 1, CancelLineEvent(cancelled));
                }

                break;
            case 0x00D2: // request_char_event
            case 0x0140: // request_char_event_uni
                if (Window(call, 0) is { } charWindow)
                {
                    RequestCharEvent(charWindow, call.Function.Selector == 0x0140);
                }

                break;
            case 0x00D3: // cancel_char_event
                if (Window(call, 0) is { } uncharred)
                {
                    uncharred.CharRequest = CharRequest.None;
                }

                break;
            case 0x00D4: // request_mouse_event
            case 0x00D5: // cancel_mouse_event
            case 0x0102: // request_hyperlink_event
            case 0x0103: // cancel_hyperlink_event
                // [glk #mouse_events] and [glk #link_events] Legal to ask
                // for, as gestalt has said, but nothing ever comes of it.
                Window(call, 0);
                break;
            case 0x00D6: // request_timer_events
                RequestTimerEvents(call.Arg(0));
                break;
            case 0x0150: // set_echo_line_event
                if (Window(call, 0) is { } echoed)
                {
                    echoed.EchoLineInput = call.Arg(1) != 0;
                }

                break;
            case 0x0151: // set_terminators_line_event
                if (Window(call, 0) is { } terminated)
                {
                    var keys = new uint[call.ArrayAddress(1) == 0 ? 0 : call.ArrayLength(1)];
                    for (var i = 0; i < keys.Length; i++)
                    {
                        keys[i] = call.Memory.ReadWord(call.ArrayAddress(1) + (uint)(4 * i));
                    }

                    SetLineTerminators(terminated, keys);
                }

                break;

            case 0x00E8: // window_flow_break
            case 0x0100: // set_hyperlink
            case 0x0101: // set_hyperlink_stream
                // [glk #graphics_textbuf] and [glk #link_creating] Both
                // are hints a plain text display can do nothing with.
                break;

            case 0x0120: // buffer_to_lower_case_uni
            case 0x0121: // buffer_to_upper_case_uni
            case 0x0122: // buffer_to_title_case_uni
                call.Result = ChangeCase(call);
                break;

            case 0x0128: // put_char_uni
                PutChar(call.Arg(0));
                break;
            case 0x0129: // put_string_uni
                PutUnicodeString(CurrentStream, call.UnicodeText(0));
                break;
            case 0x012A: // put_buffer_uni
                PutUnicodeBuffer(CurrentStream, call, 0);
                break;
            case 0x012B: // put_char_stream_uni
                PutChar(Stream(call, 0), call.Arg(1));
                break;
            case 0x012C: // put_string_stream_uni
                PutUnicodeString(Stream(call, 0), call.UnicodeText(1));
                break;
            case 0x012D: // put_buffer_stream_uni
                PutUnicodeBuffer(Stream(call, 0), call, 1);
                break;
            case 0x0130: // get_char_stream_uni
                call.Result = (uint)GetChar(Stream(call, 0), true);
                break;
            case 0x0131: // get_buffer_stream_uni
                call.Result = GetBuffer(Stream(call, 0), call, 1, true);
                break;
            case 0x0132: // get_line_stream_uni
                call.Result = GetLine(Stream(call, 0), call, 1, true);
                break;

            default:
                throw new NotSupportedException($"glk_{call.Function.Name} is not built yet.");
        }
    }

    // ----- Gestalt -----

    /// <summary>
    /// [glk #gestalt] What the library can do, by selector, with the
    /// extra answer of some selectors written into
    /// <paramref name="extra"/> when there is room.
    /// </summary>
    public static uint Gestalt(uint selector, uint value, uint[]? extra)
    {
        switch ((GestaltSelector)selector)
        {
            case GestaltSelector.Version:
                return SpecificationVersion;

            case GestaltSelector.CharOutput:
            {
                // [glk #encoding_out] Printable Latin-1 and the newline
                // print exactly, and so does everything above Latin-1,
                // as far as a Unicode display can say; the control
                // characters cannot be printed at all.
                var printable = value == '\n' || value is >= 32 and <= 126 || value >= 160;
                if (extra is { Length: > 0 })
                {
                    extra[0] = printable ? 1u : 0u;
                }

                return (uint)(printable ? CharOutput.ExactPrint : CharOutput.CannotPrint);
            }

            case GestaltSelector.LineInput:
                // [glk #encoding_inline] Printable characters can be
                // typed; nonprintable Latin-1 never can.
                return value is >= 32 and <= 126 || value >= 160 ? 1u : 0u;

            case GestaltSelector.CharInput:
                // [glk #encoding_inchar] Printable characters and the
                // enter key; a display of lines of text has no arrows or
                // function keys to offer, so it does not promise them.
                return value is >= 32 and <= 126 || value >= 160 && !GlkKeyCode.IsSpecial(value) || value == GlkKeyCode.Return ? 1u : 0u;

            case GestaltSelector.Unicode:
            case GestaltSelector.Timer:
            case GestaltSelector.LineInputEcho:
            case GestaltSelector.LineTerminators:
            case GestaltSelector.ResourceStream:
                return 1;

            case GestaltSelector.LineTerminatorKey:
                // [glk #line_events] Terminators are recorded, but no key
                // the display has can be one, so none is promised.
                return 0;

            default:
                // [glk #gestalt] An unknown selector, and every feature
                // not built yet, answers zero.
                return 0;
        }
    }

    // ----- Windows -----

    /// <summary>
    /// [glk op:window_open] Opens the first window, or splits an existing
    /// one, and returns the new window, or null if it cannot be made.
    /// </summary>
    public GlkWindow? OpenWindow(GlkWindow? split, WindowMethod method, uint size, WindowType type, uint rock)
    {
        // [glk #window_opening] The first window is the whole display and
        // the split arguments are ignored; every later window splits an
        // existing one.
        if (Root is null && split is not null)
        {
            Warn("window_open: there are no windows, so the split window must be zero.");
            return null;
        }

        if (Root is not null && split is null)
        {
            Warn("window_open: a window to split is required once windows exist.");
            return null;
        }

        var direction = method & WindowMethod.DirectionMask;
        var division = method & WindowMethod.DivisionMask;
        if (split is not null && ((uint)direction > 3 || division is not (WindowMethod.Fixed or WindowMethod.Proportional)))
        {
            Warn($"window_open: bad method {(uint)method:X}.");
            return null;
        }

        var window = CreateWindow(type, rock);
        if (window is null)
        {
            return null;
        }

        Windows.Add(window);
        Streams.Add(window.Stream);

        if (split is null)
        {
            Root = window;
        }
        else
        {
            // [glk #window_arrangement] The split window is replaced by a
            // pair holding it and the new window, the new window on the
            // side the direction says, and the new window is the key.
            var newFirst = direction is WindowMethod.Above or WindowMethod.Left;
            var pair = new PairWindow(newFirst ? window : split, newFirst ? split : window, method, size, window);
            Windows.Add(pair);
            Streams.Add(pair.Stream);

            if (split.Parent is { } grandparent)
            {
                grandparent.Replace(split, pair);
            }
            else
            {
                Root = pair;
                pair.Parent = null;
            }

            split.Parent = pair;
            window.Parent = pair;
        }

        Arrange();
        return window;
    }

    /// <summary>
    /// [glk op:window_close] Closes a window and everything under it,
    /// returning the character counts of its stream.
    /// </summary>
    public (uint Read, uint Written) CloseWindow(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var counts = (window.Stream.ReadCount, window.Stream.WriteCount);

        if (window.Parent is { } parent)
        {
            // [glk #window_opening] The sibling takes over the pair's
            // place and space.
            var sibling = ReferenceEquals(parent.First, window) ? parent.Second : parent.First;
            if (parent.Parent is { } grandparent)
            {
                grandparent.Replace(parent, sibling);
            }
            else
            {
                Root = sibling;
                sibling.Parent = null;
            }

            Forget(parent, false);
        }
        else
        {
            Root = null;
        }

        Forget(window, true);

        // [glk #window_opening] A pair whose key window has gone has no
        // key, and its sized child collapses to nothing.
        foreach (var remaining in Windows.All)
        {
            if (remaining is PairWindow pair && pair.Key is { } key && Windows.Find(key.Id) is null)
            {
                pair.Key = null;
            }
        }

        Arrange();
        return counts;
    }

    /// <summary>
    /// [glk op:window_set_arrangement] Changes the constraint of a pair.
    /// </summary>
    public void SetArrangement(GlkWindow window, WindowMethod method, uint size, GlkWindow? key)
    {
        if (window is not PairWindow pair)
        {
            Warn("window_set_arrangement: not a pair window.");
            return;
        }

        var direction = method & WindowMethod.DirectionMask;
        var division = method & WindowMethod.DivisionMask;
        var vertical = direction is WindowMethod.Above or WindowMethod.Below;

        // [glk #window_changing] The split can be resized but not
        // rotated: a top-and-bottom pair stays that way.
        if ((uint)direction > 3 || vertical != pair.IsVertical)
        {
            Warn("window_set_arrangement: a split cannot change between vertical and horizontal.");
            return;
        }

        if (division is not (WindowMethod.Fixed or WindowMethod.Proportional))
        {
            Warn($"window_set_arrangement: bad method {(uint)method:X}.");
            return;
        }

        // [glk #window_changing] A new key must be a leaf below the pair;
        // null keeps the old one.
        if (key is not null)
        {
            if (key is PairWindow || !IsDescendant(key, pair))
            {
                Warn("window_set_arrangement: the key window must be a non-pair window below the pair.");
                return;
            }

            pair.Key = key;
        }

        pair.Direction = direction;
        pair.Division = division;
        pair.Border = (method & WindowMethod.NoBorder) == 0;
        pair.Size = size;
        Arrange();
    }

    /// <summary>
    /// [glk op:window_set_echo_stream] Sets or clears a window's echo
    /// stream.
    /// </summary>
    public void SetEchoStream(GlkWindow window, GlkStream? stream)
    {
        ArgumentNullException.ThrowIfNull(window);

        // [glk #echo_streams] A window may not echo to itself, or to a
        // chain that leads back to it.
        for (var check = stream; check is WindowStream ws; check = ws.Window.EchoStream)
        {
            if (ReferenceEquals(ws.Window, window))
            {
                Warn("window_set_echo_stream: the echo stream would loop back to the window.");
                return;
            }
        }

        window.EchoStream = stream;
    }

    /// <summary>
    /// [glk #window_arrangement] Lays the tree out again over the
    /// display, from the root down, and tells the display.
    /// </summary>
    public void Arrange()
    {
        if (Root is not null)
        {
            Layout(Root, _display.Width, _display.Height);
        }

        _display.Arranged(Root);
    }

    private static bool IsDescendant(GlkWindow window, PairWindow ancestor)
    {
        for (var at = window.Parent; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private GlkWindow? CreateWindow(WindowType type, uint rock)
    {
        switch (type)
        {
            case WindowType.Blank:
                return new BlankWindow(rock);
            case WindowType.TextBuffer:
                return new TextBufferWindow(rock, _display);
            case WindowType.TextGrid:
                return new TextGridWindow(rock);
            case WindowType.Graphics:
                // [glk #graphics_testing] Not supported, as gestalt says,
                // so the open fails as the specification allows.
                Warn("window_open: graphics windows are not supported.");
                return null;
            default:
                Warn($"window_open: unknown window type {(uint)type}.");
                return null;
        }
    }

    // Removes a window, and with it its subtree, from the registries,
    // and detaches whatever pointed at it.
    private void Forget(GlkWindow window, bool subtree)
    {
        if (subtree && window is PairWindow pair)
        {
            Forget(pair.First, true);
            Forget(pair.Second, true);
        }

        if (ReferenceEquals(CurrentStream, window.Stream))
        {
            CurrentStream = null;
        }

        foreach (var other in Windows.All)
        {
            if (ReferenceEquals(other.EchoStream, window.Stream))
            {
                other.EchoStream = null;
            }
        }

        Streams.Remove(window.Stream);
        Windows.Remove(window);
    }

    private static void Layout(GlkWindow window, int width, int height)
    {
        if (window is not PairWindow pair)
        {
            window.Resize(width, height);
            return;
        }

        // [glk #window_opening] The sized child gets its fixed size in
        // the key window's units, cells for a text window and nothing
        // for a blank one or no key, or its percentage of the space,
        // and the other child gets the rest. Control flows down: a pair
        // only ever divides what it was given.
        var extent = pair.IsVertical ? height : width;
        int sized;
        if (pair.Division == WindowMethod.Fixed)
        {
            sized = pair.Key is { HasSize: true } ? (int)Math.Min(pair.Size, int.MaxValue) : 0;
        }
        else
        {
            sized = (int)(extent * Math.Min(pair.Size, 100) / 100);
        }

        sized = Math.Clamp(sized, 0, extent);
        var first = ReferenceEquals(pair.SizedChild, pair.First) ? sized : extent - sized;

        if (pair.IsVertical)
        {
            Layout(pair.First, width, first);
            Layout(pair.Second, width, height - first);
        }
        else
        {
            Layout(pair.First, first, height);
            Layout(pair.Second, width - first, height);
        }
    }

    // ----- Events -----

    /// <summary>
    /// [glk op:request_line_event] Asks for a line in a window, into a
    /// buffer of bytes or of words, with any initial text already there.
    /// </summary>
    public void RequestLineEvent(GlkWindow window, GlulxMemory memory, uint address, uint maxLength, uint initialLength, bool unicode)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(memory);

        // [glk #line_events] Only text windows take lines, and a window
        // can have one input request at a time.
        if (window.Type is not (WindowType.TextBuffer or WindowType.TextGrid))
        {
            Warn("request_line_event: the window does not take line input.");
            return;
        }

        if (window.LineRequest is not null || window.CharRequest != CharRequest.None)
        {
            Warn("request_line_event: the window already has an input request.");
            return;
        }

        var initial = new StringBuilder();
        for (uint i = 0; i < Math.Min(initialLength, maxLength); i++)
        {
            initial.Append(GlkText.ToString(unicode ? memory.ReadWord(address + (4 * i)) : memory.ReadByte(address + i)));
        }

        window.LineRequest = new LineRequest(memory, address, maxLength, unicode, initial.ToString());
    }

    /// <summary>
    /// [glk op:cancel_line_event] Withdraws a line request, giving the
    /// event the player would have produced, with nothing typed, or no
    /// event if there was no request.
    /// </summary>
    public static GlkEvent CancelLineEvent(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // The display has whatever the player typed so far and no way to
        // hand it over, so the line composed so far is taken as empty.
        return window.LineRequest is null ? GlkEvent.None : CompleteLine(window, "", 0);
    }

    /// <summary>
    /// [glk op:request_char_event] Asks for a key in a window.
    /// </summary>
    public void RequestCharEvent(GlkWindow window, bool unicode)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Type is not (WindowType.TextBuffer or WindowType.TextGrid))
        {
            Warn("request_char_event: the window does not take character input.");
            return;
        }

        if (window.LineRequest is not null || window.CharRequest != CharRequest.None)
        {
            Warn("request_char_event: the window already has an input request.");
            return;
        }

        window.CharRequest = unicode ? CharRequest.Unicode : CharRequest.Latin1;
    }

    /// <summary>
    /// [glk op:set_terminators_line_event] Records the special keys that
    /// end later line input in a window, keeping only special keycodes.
    /// </summary>
    public static void SetLineTerminators(GlkWindow window, IReadOnlyList<uint> keycodes)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(keycodes);
        window.LineTerminators = keycodes.Where(GlkKeyCode.IsSpecial).ToArray();
    }

    /// <summary>
    /// [glk op:request_timer_events] Starts timer events every so many
    /// milliseconds, or stops them for zero.
    /// </summary>
    public void RequestTimerEvents(uint milliseconds)
    {
        _timerInterval = milliseconds;
        _timerStarted = _clock.GetTimestamp();
    }

    /// <summary>
    /// [glk op:select] Waits for the next event the game asked for: the
    /// timer if its interval has passed, else the player's line or key.
    /// </summary>
    /// <exception cref="EndOfStreamException">
    /// The display has no more input to give.
    /// </exception>
    public GlkEvent Select()
    {
        while (true)
        {
            // [glk #timer_events] A timer that is due goes first, but
            // never stacks up: the next is an interval from now.
            if (TimerDue())
            {
                _timerStarted = _clock.GetTimestamp();
                return new GlkEvent(EventType.Timer, null, 0, 0);
            }

            var lines = Windows.All.Where(w => w.LineRequest is not null).ToList();
            var chars = Windows.All.Where(w => w.CharRequest != CharRequest.None).ToList();
            var timeout = _timerInterval == 0
                ? (TimeSpan?)null
                : TimeSpan.FromMilliseconds(_timerInterval) - _clock.GetElapsedTime(_timerStarted);

            var input = _display.WaitForInput(lines, chars, timeout is { } t && t < TimeSpan.Zero ? TimeSpan.Zero : timeout);

            switch (input.Kind)
            {
                case GlkInputKind.Line when input.Window is { LineRequest: not null } window:
                    return CompleteLine(window, input.Text ?? "", input.Terminator);

                case GlkInputKind.Key when input.Window is { CharRequest: not CharRequest.None } window:
                {
                    // [glk #char_events] Through the Latin-1 request a
                    // character is 0 to 255 or a special key; anything
                    // else is a question mark.
                    var key = input.Key;
                    if (window.CharRequest == CharRequest.Latin1 && key > 0xFF && !GlkKeyCode.IsSpecial(key))
                    {
                        key = '?';
                    }

                    window.CharRequest = CharRequest.None;
                    return new GlkEvent(EventType.CharInput, window, key, 0);
                }

                case GlkInputKind.Ended:
                    throw new EndOfStreamException("The input ended while the game was waiting for the player.");

                default:
                    // A timer tick, or input for a window that no longer
                    // asks: back to the top, where the timer is checked.
                    break;
            }
        }
    }

    /// <summary>
    /// [glk op:select_poll] The timer event if it is due, else no event.
    /// Player input is never polled for.
    /// </summary>
    public GlkEvent SelectPoll()
    {
        if (TimerDue())
        {
            _timerStarted = _clock.GetTimestamp();
            return new GlkEvent(EventType.Timer, null, 0, 0);
        }

        return GlkEvent.None;
    }

    private bool TimerDue() =>
        _timerInterval != 0 && _clock.GetElapsedTime(_timerStarted) >= TimeSpan.FromMilliseconds(_timerInterval);

    // [glk #line_events] The line goes into the buffer, as many
    // characters as fit, and the event carries the count and the
    // terminator. A text grid keeps the line where it was typed and
    // moves its cursor to the next row; a text buffer's echo stream
    // gets the line if echoing is on, the display having shown it.
    private static GlkEvent CompleteLine(GlkWindow window, string text, uint terminator)
    {
        var request = window.LineRequest!;
        window.LineRequest = null;

        uint count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count >= request.MaxLength)
            {
                break;
            }

            var character = (uint)rune.Value;
            if (request.Unicode)
            {
                request.Memory.WriteWord(request.Address + (4 * count), character);
            }
            else
            {
                request.Memory.WriteByte(request.Address + count, character > 0xFF ? (byte)'?' : (byte)character);
            }

            count++;
        }

        if (window is TextGridWindow grid)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                grid.Stream.PutChar((uint)rune.Value);
            }

            grid.Stream.PutChar('\n');
        }
        else if (window.EchoLineInput && window.EchoStream is { } echo)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                echo.PutChar((uint)rune.Value);
            }

            echo.PutChar('\n');
        }

        return new GlkEvent(EventType.LineInput, window, count, terminator);
    }

    // [glk #event] Writes an event into the structure argument.
    private static void Report(GlkCall call, int index, GlkEvent glkEvent) =>
        call.Out(index, (uint)glkEvent.Type, glkEvent.Window?.Id ?? 0, glkEvent.Value1, glkEvent.Value2);

    // ----- Streams -----

    /// <summary>
    /// [glk op:stream_open_memory] Opens a stream over the game's memory.
    /// </summary>
    public GlkMemoryStream? OpenMemoryStream(GlulxMemory memory, uint address, uint length, bool unicode, FileMode mode, uint rock)
    {
        ArgumentNullException.ThrowIfNull(memory);

        if (mode is not (FileMode.Read or FileMode.Write or FileMode.ReadWrite))
        {
            Warn($"stream_open_memory: bad file mode {(uint)mode}.");
            return null;
        }

        var stream = new GlkMemoryStream(memory, address, length, unicode, mode, rock);
        Streams.Add(stream);
        return stream;
    }

    /// <summary>
    /// [glk op:stream_close] Closes a stream, returning its counts. A
    /// window stream cannot be closed this way and is left alone.
    /// </summary>
    public (uint Read, uint Written) CloseStream(GlkStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream is WindowStream)
        {
            Warn("stream_close: a window stream is closed with its window.");
            return (0, 0);
        }

        var counts = (stream.ReadCount, stream.WriteCount);

        // [glk #stream_close] If it was current, there is no current
        // stream; [glk #echo_streams] windows echoing to it stop.
        if (ReferenceEquals(CurrentStream, stream))
        {
            CurrentStream = null;
        }

        foreach (var window in Windows.All)
        {
            if (ReferenceEquals(window.EchoStream, stream))
            {
                window.EchoStream = null;
            }
        }

        Streams.Remove(stream);
        stream.Close();
        return counts;
    }

    /// <summary>
    /// Closes every file stream still open, so that what was written
    /// reaches the file, for a frontend that is shutting down.
    /// </summary>
    /// <remarks>
    /// [glk op:exit] A program may exit with streams open, and what it
    /// wrote should not be lost for that; the streams are closed as
    /// [glk #stream_close] a close would, echoes and all.
    /// </remarks>
    public void CloseFiles()
    {
        foreach (var stream in Streams.All.OfType<GlkFileStream>().ToList())
        {
            CloseStream(stream);
        }
    }

    // ----- Files -----

    /// <summary>
    /// [glk op:stream_open_file] Opens a stream on a file reference's
    /// file, of bytes or of Unicode characters, in a mode, or returns
    /// null for no reference, a bad mode, or a file that cannot be
    /// opened so.
    /// </summary>
    public GlkFileStream? OpenFileStream(GlkFileReference? fileref, bool unicode, FileMode mode, uint rock)
    {
        if (fileref is null)
        {
            return null;
        }

        if (mode is not (FileMode.Read or FileMode.Write or FileMode.ReadWrite or FileMode.WriteAppend))
        {
            Warn($"stream_open_file: bad file mode {(uint)mode}.");
            return null;
        }

        var file = Files.Open(fileref.Name, mode);
        if (file is null)
        {
            // [glk #file_streams] A file that must exist and does not is
            // simply null; anything else that fails is worth a word.
            if (mode != FileMode.Read)
            {
                Warn($"stream_open_file: unable to open {fileref.Name}.");
            }

            return null;
        }

        var stream = new GlkFileStream(file, fileref.Name, unicode, fileref.IsText, mode, rock);
        Streams.Add(stream);
        return stream;
    }

    /// <summary>
    /// [glk op:stream_open_resource] Opens a stream that reads a data
    /// resource, or returns null for no resource file, no resource of
    /// the number, or a chunk of a kind that is not data.
    /// </summary>
    public GlkResourceStream? OpenResourceStream(uint number, bool unicode, uint rock)
    {
        if (Resources?.Find(ResourceUsage.Data, (int)number) is not { } resource)
        {
            return null;
        }

        // [glk #resource_streams] TEXT is text, BINA is binary, and a
        // FORM, which the resource file reader keeps whole with its
        // header, is binary too; anything else is not a data chunk.
        bool binary;
        switch (resource.ChunkType)
        {
            case "TEXT":
                binary = false;
                break;
            case "BINA":
                binary = true;
                break;
            default:
                if (resource.Data.Length < 4 || !resource.Data.Span[..4].SequenceEqual("FORM"u8))
                {
                    return null;
                }

                binary = true;
                break;
        }

        var stream = new GlkResourceStream(resource.Data, number, unicode, !binary, rock);
        Streams.Add(stream);
        return stream;
    }

    /// <summary>
    /// [glk op:fileref_create_by_name] A reference to a file of the
    /// name the game gave, made safe and suffixed as the specification
    /// recommends.
    /// </summary>
    public GlkFileReference CreateFileReference(uint usage, string name, uint rock)
    {
        var fileref = new GlkFileReference(rock, usage, GlkFileReference.SafeName(name, usage));
        FileReferences.Add(fileref);
        return fileref;
    }

    /// <summary>
    /// [glk op:fileref_create_temp] A reference to a new temporary file.
    /// </summary>
    public GlkFileReference CreateTemporaryFileReference(uint usage, uint rock)
    {
        var fileref = new GlkFileReference(rock, usage, Files.TemporaryName());
        FileReferences.Add(fileref);
        return fileref;
    }

    /// <summary>
    /// [glk op:fileref_create_by_prompt] A reference to a file the
    /// player chose for a usage and mode, or null if the player
    /// declined or, for reading, chose a file that does not exist.
    /// </summary>
    public GlkFileReference? PromptForFileReference(uint usage, FileMode mode, uint rock)
    {
        if (mode is not (FileMode.Read or FileMode.Write or FileMode.ReadWrite or FileMode.WriteAppend))
        {
            Warn($"fileref_create_by_prompt: bad file mode {(uint)mode}.");
            return null;
        }

        var chosen = Files.AskForFile((FileUsage)(usage & GlkFileReference.TypeMask), mode);
        if (chosen is null)
        {
            return null;
        }

        // [glk op:fileref_create_by_prompt] The recommended suffix, for
        // a name without one; and for reading, the player is choosing
        // among existing files, so a name that is not one is a
        // cancellation, as the reference library has it.
        var name = GlkFileReference.WithSuffix(chosen, usage);
        if (mode == FileMode.Read && !Files.Exists(name))
        {
            return null;
        }

        var fileref = new GlkFileReference(rock, usage, name);
        FileReferences.Add(fileref);
        return fileref;
    }

    /// <summary>
    /// [glk op:fileref_create_from_fileref] A reference to the same
    /// file with another usage, or null for no reference to copy.
    /// </summary>
    /// <remarks>
    /// [glk op:fileref_create_from_fileref] Whether a changed type
    /// still points to the same file is the library's choice; here the
    /// name is kept, suffix and all, as the reference library keeps it.
    /// </remarks>
    public GlkFileReference? CopyFileReference(uint usage, GlkFileReference? source, uint rock)
    {
        if (source is null)
        {
            return null;
        }

        var fileref = new GlkFileReference(rock, usage, source.Name);
        FileReferences.Add(fileref);
        return fileref;
    }

    /// <summary>
    /// [glk op:fileref_does_file_exist] Whether the reference's file
    /// exists; false for no reference.
    /// </summary>
    public bool FileExists(GlkFileReference? fileref) => fileref is not null && Files.Exists(fileref.Name);

    /// <summary>
    /// [glk op:fileref_delete_file] Deletes the reference's file, and
    /// not the reference.
    /// </summary>
    public void DeleteFile(GlkFileReference? fileref)
    {
        if (fileref is not null)
        {
            Files.Delete(fileref.Name);
        }
    }

    /// <summary>
    /// [glk op:stream_set_current] Makes a stream current, or none.
    /// </summary>
    public void SetCurrentStream(GlkStream? stream) => CurrentStream = stream;

    /// <summary>[glk op:put_char] Prints to the current stream.</summary>
    public void PutChar(uint character) => PutChar(CurrentStream, character);

    /// <summary>[glk op:put_char_stream] Prints to a stream.</summary>
    public void PutChar(GlkStream? stream, uint character)
    {
        if (stream is null)
        {
            Warn("put_char: no current output stream.");
            return;
        }

        if (!stream.Writable)
        {
            Warn("put_char: the stream is not an output stream.");
            return;
        }

        stream.PutChar(character);
    }

    /// <summary>[glk op:set_style] Changes a stream's style.</summary>
    public void SetStyle(GlkStream? stream, GlkStyle style)
    {
        if (stream is null)
        {
            Warn("set_style: no current output stream.");
            return;
        }

        // [glk op:set_style] An unknown style counts as normal.
        stream.SetStyle((uint)style <= (uint)GlkStyle.User2 ? style : GlkStyle.Normal);
    }

    private void PutString(GlkStream? stream, string text)
    {
        foreach (var character in text)
        {
            PutChar(stream, character);
        }
    }

    private void PutUnicodeString(GlkStream? stream, uint[] characters)
    {
        foreach (var character in characters)
        {
            PutChar(stream, character);
        }
    }

    private void PutBuffer(GlkStream? stream, GlkCall call, int index)
    {
        var address = call.ArrayAddress(index);
        var length = call.ArrayLength(index);
        for (uint i = 0; i < length; i++)
        {
            PutChar(stream, call.Memory.ReadByte(address + i));
        }
    }

    private void PutUnicodeBuffer(GlkStream? stream, GlkCall call, int index)
    {
        var address = call.ArrayAddress(index);
        var length = call.ArrayLength(index);
        for (uint i = 0; i < length; i++)
        {
            PutChar(stream, call.Memory.ReadWord(address + (4 * i)));
        }
    }

    /// <summary>
    /// [glk op:get_char_stream_uni] Reads one character from a stream,
    /// or -1 at its end, with a warning if it is not one to read from.
    /// </summary>
    public int GetChar(GlkStream? stream) => GetChar(stream, true);

    private int GetChar(GlkStream? stream, bool unicode)
    {
        if (stream is null)
        {
            Warn("get_char_stream: no stream.");
            return -1;
        }

        if (!stream.Readable)
        {
            Warn("get_char_stream: the stream is not an input stream.");
            return -1;
        }

        var character = stream.GetChar();

        // [glk op:get_char_stream] A character beyond 255 comes back as a
        // question mark through the Latin-1 call.
        return !unicode && character > 0xFF ? '?' : character;
    }

    private uint GetBuffer(GlkStream? stream, GlkCall call, int index, bool unicode)
    {
        var address = call.ArrayAddress(index);
        var length = call.ArrayLength(index);
        uint count = 0;
        while (count < length)
        {
            var character = GetChar(stream, unicode);
            if (character < 0)
            {
                break;
            }

            Store(call.Memory, address, count, (uint)character, unicode);
            count++;
        }

        return count;
    }

    private uint GetLine(GlkStream? stream, GlkCall call, int index, bool unicode)
    {
        // [glk op:get_line_stream] Up to length less one characters or a
        // newline, then a terminating zero, returning the count without
        // the zero.
        var address = call.ArrayAddress(index);
        var length = call.ArrayLength(index);
        uint count = 0;
        if (length == 0)
        {
            return 0;
        }

        while (count < length - 1)
        {
            var character = GetChar(stream, unicode);
            if (character < 0)
            {
                break;
            }

            Store(call.Memory, address, count, (uint)character, unicode);
            count++;
            if (character == '\n')
            {
                break;
            }
        }

        Store(call.Memory, address, count, 0, unicode);
        return count;
    }

    private static void Store(GlulxMemory memory, uint address, uint index, uint value, bool unicode)
    {
        if (unicode)
        {
            memory.WriteWord(address + (4 * index), value);
        }
        else
        {
            memory.WriteByte(address + index, (byte)value);
        }
    }

    // ----- Styles -----

    /// <summary>
    /// [glk op:stylehint_set] Records a hint for windows opened later.
    /// </summary>
    public void SetStyleHint(WindowType type, GlkStyle style, StyleHint hint, int value) =>
        _styleHints[(type, style, hint)] = value;

    /// <summary>[glk op:stylehint_clear] Removes a hint.</summary>
    public void ClearStyleHint(WindowType type, GlkStyle style, StyleHint hint) =>
        _styleHints.Remove((type, style, hint));

    /// <summary>
    /// The hint set for a style in a type of window, or for all types,
    /// or null if none was set: having no hint is not the same as a
    /// hint of zero.
    /// </summary>
    public int? StyleHintFor(WindowType type, GlkStyle style, StyleHint hint) =>
        _styleHints.TryGetValue((type, style, hint), out var value) || _styleHints.TryGetValue((WindowType.AllTypes, style, hint), out value)
            ? value
            : null;

    // ----- Characters -----

    /// <summary>
    /// [glk #encoding_hilo] Lower-cases a Latin-1 character: the ranges
    /// 41 to 5A, C0 to D6, and D8 to DE move down by 20.
    /// </summary>
    public static byte CharToLower(byte character) =>
        character is >= 0x41 and <= 0x5A or >= 0xC0 and <= 0xD6 or >= 0xD8 and <= 0xDE
            ? (byte)(character + 0x20)
            : character;

    /// <summary>
    /// [glk #encoding_hilo] Upper-cases a Latin-1 character: the ranges
    /// 61 to 7A, E0 to F6, and F8 to FE move up by 20.
    /// </summary>
    public static byte CharToUpper(byte character) =>
        character is >= 0x61 and <= 0x7A or >= 0xE0 and <= 0xF6 or >= 0xF8 and <= 0xFE
            ? (byte)(character - 0x20)
            : character;

    private static uint ChangeCase(GlkCall call)
    {
        // [glk #encoding_hilo] The buffer holds numchars characters in
        // len words; the converted characters go back in place, and the
        // count after conversion is returned. Case changes here never
        // change the count, since one code point maps to one.
        var address = call.ArrayAddress(0);
        var length = call.ArrayLength(0);
        var count = Math.Min(call.Arg(1), length);
        var lowerRest = call.Function.Selector == 0x0122 && call.Arg(2) != 0;

        for (uint i = 0; i < count; i++)
        {
            var at = address + (4 * i);
            var character = call.Memory.ReadWord(at);
            if (!Rune.IsValid(character))
            {
                continue;
            }

            var rune = new Rune(character);
            var changed = call.Function.Selector switch
            {
                0x0120 => Rune.ToLowerInvariant(rune),
                0x0121 => Rune.ToUpperInvariant(rune),
                _ => i == 0 ? Rune.ToUpperInvariant(rune) : lowerRest ? Rune.ToLowerInvariant(rune) : rune,
            };

            call.Memory.WriteWord(at, (uint)changed.Value);
        }

        return call.Arg(1);
    }

    // ----- Helpers -----

    private GlkWindow? Window(GlkCall call, int index)
    {
        var id = call.ObjectId(index);
        var window = Windows.Find(id);
        if (window is null)
        {
            Warn($"{call.Function.Name}: invalid window id {id}.");
        }

        return window;
    }

    private GlkFileReference? FileReference(GlkCall call, int index)
    {
        var id = call.ObjectId(index);
        var fileref = FileReferences.Find(id);
        if (fileref is null)
        {
            Warn($"{call.Function.Name}: invalid fileref id {id}.");
        }

        return fileref;
    }

    private GlkStream? Stream(GlkCall call, int index)
    {
        var id = call.ObjectId(index);
        var stream = Streams.Find(id);
        if (stream is null)
        {
            Warn($"{call.Function.Name}: invalid stream id {id}.");
        }

        return stream;
    }

    private void Warn(string message) => Warnings.Add(message);
}
