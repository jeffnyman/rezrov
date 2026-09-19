using System.Runtime.InteropServices;

namespace Rezrov.Gtui;

/// <summary>
/// A window on macOS, opened by talking to the Objective-C runtime.
/// </summary>
/// <remarks>
/// The third system and the strangest of the three. Windows and X11
/// both offer plain C functions to call; macOS offers objects, and the
/// only way in without a toolkit is the runtime underneath them: ask it
/// for a class by name, ask it for a method by name, and send the one
/// to the other. Every line here that looks like a method call is that.
///
/// Two decisions were made for the sake of a program that cannot be
/// tried on the machine it was written on.
///
/// The window does not draw through a view of its own. Drawing normally
/// means subclassing NSView and overriding its drawing method, which
/// means building a class at runtime and hanging function pointers off
/// it. Instead the pixels are handed to the layer behind the content
/// view as a picture, which needs no new class at all and no callback
/// into managed code.
///
/// The window cannot be resized. Asking a view how large it is returns
/// a rectangle of four numbers, and how such a thing comes back differs
/// between Apple's two processors, which is exactly the sort of detail
/// that cannot be got right without running it. The window opens at the
/// size the program asks for and stays there, which every Z-machine
/// game is content with. Resizing is worth adding once someone has this
/// running in front of them.
/// </remarks>
internal sealed partial class CocoaWindow : IGridWindow
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    // [cocoa] A titled window with a close button and a minimize
    // button, and deliberately without the resize corner.
    private const ulong Titled = 1;
    private const ulong Closable = 2;
    private const ulong Miniaturizable = 4;

    private const ulong Buffered = 2;
    private const ulong KeyDown = 10;

    // [cocoa] The command key, which marks a keystroke as the menus'
    // business rather than the game's.
    private const long CommandKey = 1 << 20;
    private const ulong EveryEvent = ulong.MaxValue;

    // [cocoa] Skip the first byte of each pixel rather than read it as
    // transparency, and take the four bytes in the order this machine
    // writes them, which is the order a surface already has them in.
    private const uint SkipFirst = 6;
    private const uint LittleEndian = 2 << 12;

    private nint _application;
    private nint _window;
    private nint _layer;
    private nint _colors;
    private int[] _copy = [];
    private volatile bool _dirty;
    private volatile bool _running;

    public CocoaWindow() => Surface = new Surface(0, 0);

    public Surface Surface { get; private set; }

    public Action<ushort>? Key { get; set; }

    public Action? Resized { get; set; }

    public Action<Surface>? Painting { get; set; }

    public void Open(string title, int width, int height)
    {
        // The classes live in AppKit, which nothing has loaded yet,
        // since this program links against no framework at all.
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

        Surface = new Surface(Math.Max(width, 1), Math.Max(height, 1));
        _copy = new int[Surface.Width * Surface.Height];

        _application = Send(Class("NSApplication"), Selector("sharedApplication"));
        Check(_application, "NSApplication could not be started.");

        // [cocoa] A regular application, which is what gives the window
        // a place in the dock and lets it come to the front.
        SendLong(_application, Selector("setActivationPolicy:"), 0);

        var window = Send(Class("NSWindow"), Selector("alloc"));
        Check(window, "No window could be allocated.");

        _window = SendWindow(
            window,
            Selector("initWithContentRect:styleMask:backing:defer:"),
            new CocoaRect { Width = Surface.Width, Height = Surface.Height },
            Titled | Closable | Miniaturizable,
            Buffered,
            false);
        Check(_window, "The window could not be opened.");

        SendPointer(_window, Selector("setTitle:"), Text(title));
        Send(_window, Selector("center"));

        var view = Send(_window, Selector("contentView"));
        Check(view, "The window has no content view.");

        // [cocoa] A view backed by a layer is one whose pixels can be
        // handed over as a picture rather than drawn by a method.
        SendBool(view, Selector("setWantsLayer:"), true);
        _layer = Send(view, Selector("layer"));
        Check(_layer, "The content view has no layer to draw on.");

        // A bitmap font wants whole pixels. On a display of twice the
        // density this makes each one a square of four rather than a
        // smudge, which is what the font was drawn for.
        SendPointer(_layer, Selector("setMagnificationFilter:"), Text("nearest"));

        _colors = CGColorSpaceCreateDeviceRGB();
        Check(_colors, "No color space to draw in.");

        SendPointer(_window, Selector("makeKeyAndOrderFront:"), 0);
        SendBool(_application, Selector("activateIgnoringOtherApps:"), true);
        Send(_application, Selector("finishLaunching"));
    }

    public void Redraw() => _dirty = true;

    public void Run()
    {
        _running = true;

        var nextEvent = Selector("nextEventMatchingMask:untilDate:inMode:dequeue:");
        var sendEvent = Selector("sendEvent:");
        var isVisible = Selector("isVisible");
        var mode = Text("kCFRunLoopDefaultMode");

        while (_running)
        {
            if (_dirty)
            {
                _dirty = false;
                Draw();
            }

            // [cocoa] Waiting a moment rather than forever, so that a
            // repaint asked for by the thread running the game is
            // noticed without that thread having to touch any of this.
            // Everything here belongs to the first thread and nothing
            // else may call it.
            var until = SendDouble(Class("NSDate"), Selector("dateWithTimeIntervalSinceNow:"), 0.02);
            var next = SendEvent(_application, nextEvent, EveryEvent, until, mode, true);

            if (next == 0)
            {
                if (Send(_window, isVisible) == 0)
                {
                    _running = false;
                }

                continue;
            }

            // [cocoa] A key this program has taken is NOT passed on.
            // Nothing in the responder chain handles typing, since the
            // content view is an ordinary one with no drawing method of
            // its own, so AppKit would reach the end of the chain and
            // sound the system beep at every keystroke.
            //
            // Keys held with the command key are passed on regardless,
            // because those belong to the menus rather than to the
            // game, and a player who presses command and Q means it.
            var typing = SendForLong(next, Selector("type")) == (long)KeyDown
                && (SendForLong(next, Selector("modifierFlags")) & CommandKey) == 0;

            if (typing)
            {
                Pressed(next);
            }
            else
            {
                SendPointer(_application, sendEvent, next);
            }

            if (Send(_window, isVisible) == 0)
            {
                _running = false;
            }
        }
    }

    public void Close() => _running = false;

    public void Dispose()
    {
        if (_colors != 0)
        {
            CGColorSpaceRelease(_colors);
            _colors = 0;
        }

        if (_window != 0)
        {
            SendPointer(_window, Selector("close"), 0);
            _window = 0;
        }
    }

    /// <summary>
    /// [zm 3.8] The key the player pressed. An event carries the
    /// characters that key means with the keyboard layout already
    /// taken into account, and macOS puts the arrows and the function
    /// keys in there too, so the characters settle every case.
    /// </summary>
    private void Pressed(nint next)
    {
        var characters = Send(next, Selector("characters"));
        var text = characters == 0 ? 0 : Send(characters, Selector("UTF8String"));
        var first = text == 0 ? '\0' : (char)Marshal.ReadByte(text);

        var zscii = first is >= ' ' and <= '~'
            ? GridKeys.FromCharacter(first)
            : GridKeys.FromCocoa(first);

        if (zscii != 0)
        {
            Key?.Invoke(zscii);
        }
    }

    /// <summary>
    /// Fills the surface and hands it to the layer as a picture.
    /// </summary>
    private void Draw()
    {
        if (_layer == 0 || Surface.Width == 0 || Surface.Height == 0)
        {
            return;
        }

        Painting?.Invoke(Surface);

        for (var at = 0; at < _copy.Length; at++)
        {
            _copy[at] = unchecked((int)Surface.Pixels[at]);
        }

        var bytes = _copy.Length * 4;
        var pixels = Marshal.AllocHGlobal(bytes);

        try
        {
            Marshal.Copy(_copy, 0, pixels, _copy.Length);

            var provider = CGDataProviderCreateWithData(0, pixels, (nuint)bytes, 0);
            Check(provider, "The pixels could not be handed over.");

            var picture = CGImageCreate(
                (nuint)Surface.Width,
                (nuint)Surface.Height,
                8,
                32,
                (nuint)(Surface.Width * 4),
                _colors,
                SkipFirst | LittleEndian,
                provider,
                0,
                false,
                0);

            if (picture != 0)
            {
                SendPointer(_layer, Selector("setContents:"), picture);
                CGImageRelease(picture);
            }

            CGDataProviderRelease(provider);
        }
        finally
        {
            Marshal.FreeHGlobal(pixels);
        }
    }

    private static void Check(nint handle, string what)
    {
        if (handle == 0)
        {
            throw new InvalidOperationException($"rezrov-gtui: {what}");
        }
    }

    private static nint Class(string name)
    {
        var found = objc_getClass(name);
        Check(found, $"The class {name} is not there, which means AppKit did not load.");
        return found;
    }

    private static nint Selector(string name)
    {
        var found = sel_registerName(name);
        Check(found, $"The method {name} has no selector.");
        return found;
    }

    /// <summary>An Objective-C string, which many calls want.</summary>
    private static nint Text(string value)
    {
        var utf8 = Marshal.StringToHGlobalAnsi(value);

        try
        {
            return SendPointer(Class("NSString"), Selector("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeHGlobal(utf8);
        }
    }

    // [cocoa] One entry point, sending a message to an object, declared
    // once for each shape of arguments it is used with. The runtime has
    // a single function for this and its arguments differ every time,
    // so there is no way to say it once.
    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial long SendForLong(nint receiver, nint selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nint SendPointer(nint receiver, nint selector, nint argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nint SendLong(nint receiver, nint selector, long argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nint SendBool(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.U1)] bool argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nint SendDouble(nint receiver, nint selector, double argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nint SendWindow(
        nint receiver,
        nint selector,
        CocoaRect frame,
        ulong style,
        ulong backing,
        [MarshalAs(UnmanagedType.U1)] bool defer);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial nint SendEvent(
        nint receiver,
        nint selector,
        ulong mask,
        nint until,
        nint mode,
        [MarshalAs(UnmanagedType.U1)] bool dequeue);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGColorSpaceCreateDeviceRGB();

    [LibraryImport(CoreGraphics)]
    private static partial void CGColorSpaceRelease(nint space);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGDataProviderCreateWithData(nint info, nint data, nuint size, nint release);

    [LibraryImport(CoreGraphics)]
    private static partial void CGDataProviderRelease(nint provider);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGImageCreate(
        nuint width,
        nuint height,
        nuint bitsPerComponent,
        nuint bitsPerPixel,
        nuint bytesPerRow,
        nint colorSpace,
        uint bitmapInfo,
        nint provider,
        nint decode,
        [MarshalAs(UnmanagedType.U1)] bool interpolate,
        int intent);

    [LibraryImport(CoreGraphics)]
    private static partial void CGImageRelease(nint image);

    // [cocoa] A rectangle is four numbers: where it sits and how large
    // it is. The window is placed by the system, so the first two are
    // left at nothing.
    [StructLayout(LayoutKind.Sequential)]
    private struct CocoaRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }
}
