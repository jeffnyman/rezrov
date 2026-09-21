using Rezrov.Core.Graphics;
using System.Runtime.InteropServices;

namespace Rezrov.Gtui;

/// <summary>
/// A window on X11, opened by asking the display server directly.
/// </summary>
/// <remarks>
/// The same program as the Windows one underneath a different system,
/// and worth reading beside it: a connection is opened to the display,
/// a window is created on it, and a loop takes events off the
/// connection and answers them. What Windows calls messages X calls
/// events, and where Windows hands a rectangle of pixels to the
/// operating system this hands it to the server.
///
/// [x11] Xlib is not safe to call from two threads unless it is told at
/// the start that it will be, so the very first call this program makes
/// is the one that says so. That is what lets the thread running the
/// game ask for a repaint while the loop is waiting for an event.
/// </remarks>
internal sealed partial class X11Window : IGridWindow
{
    // The events this window asks to hear about.
    private const long KeyPressMask = 1L << 0;
    private const long ExposureMask = 1L << 15;
    private const long StructureNotifyMask = 1L << 17;

    // [x11] Replacing whatever a property held before, which is
    // the only one of the three ways of writing one that is wanted
    // here.
    private const int PropModeReplace = 0;

    private const int KeyPress = 2;
    private const int Expose = 12;
    private const int ConfigureNotify = 22;
    private const int ClientMessage = 33;

    // [x11] ZPixmap, which is the one where a pixel's bits are together
    // rather than split across planes.
    private const int ZPixmap = 2;

    private nint _display;
    private nint _window;
    private nint _context;
    private nint _image;
    private nint _pixels;

    // The surface, in the shape Marshal.Copy will take, kept from one
    // paint to the next rather than made afresh for each: a window of
    // any size repaints on every character the game prints.
    private int[] _copy = [];
    private nint _closeAtom;
    private nint _repaintAtom;
    private int _screen;
    private bool _running;

    public X11Window() => Surface = new Surface(0, 0);

    public Surface Surface { get; private set; }

    public Action<ushort>? Key { get; set; }

    public Action? Resized { get; set; }

    public Action<Surface>? Painting { get; set; }

    public void Open(string title, int width, int height)
    {
        // This has to come before anything else Xlib does, and it has
        // to work: the thread running the game asks for repaints, and
        // without this Xlib is not safe to call from two threads.
        if (XInitThreads() == 0)
        {
            throw new InvalidOperationException("This X11 library will not work across threads.");
        }

        _display = XOpenDisplay(null);

        if (_display == 0)
        {
            throw new InvalidOperationException(
                "No display to open a window on. X11 needs DISPLAY set to a server that will have it.");
        }

        _screen = XDefaultScreen(_display);

        _window = XCreateSimpleWindow(
            _display,
            XRootWindow(_display, _screen),
            0,
            0,
            (uint)Math.Max(width, 1),
            (uint)Math.Max(height, 1),
            0,
            XBlackPixel(_display, _screen),
            XBlackPixel(_display, _screen));

        XStoreName(_display, _window, title);
        XSelectInput(_display, _window, KeyPressMask | ExposureMask | StructureNotifyMask);

        // [x11] A window is told about its own closing by agreement
        // rather than by force: the window manager sends a message
        // naming this atom, and a program that did not ask for it is
        // simply killed instead.
        _closeAtom = XInternAtom(_display, "WM_DELETE_WINDOW", false);
        var protocols = _closeAtom;
        XSetWMProtocols(_display, _window, ref protocols, 1);

        // An atom of this program's own, for the repaint the game asks
        // for from its own thread.
        _repaintAtom = XInternAtom(_display, "REZROV_REPAINT", false);

        _context = XCreateGC(_display, _window, 0, 0);

        XMapWindow(_display, _window);
        XFlush(_display);

        Resize(width, height);
    }

    /// <summary>
    /// [x11] The mark the window wears, as the property the window
    /// managers agreed on.
    /// </summary>
    /// <remarks>
    /// The property is a list of pictures, each of them its width, its
    /// height, and then a pixel for every place in it, so that a panel
    /// showing a small mark and a switcher showing a large one can each
    /// take the size they want. Every value is a thirty-two bit
    /// quantity in the protocol and a machine word here, which is why
    /// the array is of the wider type on a machine where those differ.
    ///
    /// A window manager that does not read this property leaves the
    /// window as it was, which is no worse than never having asked.
    /// </remarks>
    public void SetIcon(byte[] icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        if (_display == 0 || _window == 0)
        {
            return;
        }

        var pictures = IcoReader.Read(icon);
        var values = new List<nint>();

        foreach (var picture in pictures)
        {
            values.Add(picture.Width);
            values.Add(picture.Height);

            for (var y = 0; y < picture.Height; y++)
            {
                for (var x = 0; x < picture.Width; x++)
                {
                    var (red, green, blue, alpha) = picture.At(x, y);

                    values.Add((alpha << 24) | (red << 16) | (green << 8) | blue);
                }
            }
        }

        if (values.Count == 0)
        {
            return;
        }

        var property = XInternAtom(_display, "_NET_WM_ICON", false);
        var cardinal = XInternAtom(_display, "CARDINAL", false);
        var data = values.ToArray();

        XChangeProperty(_display, _window, property, cardinal, 32, PropModeReplace, data, data.Length);
        XFlush(_display);
    }

    public void Redraw()
    {
        if (_display == 0 || _window == 0)
        {
            return;
        }

        // Asking from this thread would mean drawing from it, so a
        // message is sent instead and the loop does the drawing where
        // it belongs.
        var message = default(XEvent);
        message.Type = ClientMessage;
        message.Window = _window;
        message.MessageType = _repaintAtom;
        message.Format = 32;

        XSendEvent(_display, _window, false, 0, ref message);
        XFlush(_display);
    }

    public void Run()
    {
        _running = true;

        while (_running)
        {
            var next = default(XEvent);
            XNextEvent(_display, ref next);

            switch (next.Type)
            {
                case Expose:
                    Draw();
                    break;

                case ConfigureNotify:
                    if (next.ConfigureWidth != Surface.Width || next.ConfigureHeight != Surface.Height)
                    {
                        Resize(next.ConfigureWidth, next.ConfigureHeight);
                        Resized?.Invoke();
                        Draw();
                    }

                    break;

                case KeyPress:
                    Pressed(ref next);
                    break;

                case ClientMessage:
                    if (next.MessageType == _repaintAtom)
                    {
                        Draw();
                    }
                    else if (next.MessageData == _closeAtom)
                    {
                        _running = false;
                    }

                    break;

                default:
                    break;
            }
        }
    }

    public void Close()
    {
        if (_display == 0 || _window == 0)
        {
            return;
        }

        var message = default(XEvent);
        message.Type = ClientMessage;
        message.Window = _window;
        message.MessageType = XInternAtom(_display, "WM_PROTOCOLS", false);
        message.Format = 32;
        message.MessageData = _closeAtom;

        XSendEvent(_display, _window, false, 0, ref message);
        XFlush(_display);
    }

    public void Dispose()
    {
        Forget();

        if (_context != 0)
        {
            XFreeGC(_display, _context);
            _context = 0;
        }

        if (_window != 0)
        {
            XDestroyWindow(_display, _window);
            _window = 0;
        }

        if (_display != 0)
        {
            XCloseDisplay(_display);
            _display = 0;
        }
    }

    /// <summary>
    /// [zm 3.8] Turns an X11 key into the Z-machine's own code. The
    /// character comes from the server, which has the keyboard layout,
    /// and the keys with no character of their own come from the symbol
    /// the server names them by.
    /// </summary>
    private void Pressed(ref XEvent key)
    {
        var buffer = new byte[8];
        var written = XLookupString(ref key, buffer, buffer.Length, out var symbol, 0);

        var zscii = written > 0
            ? GridKeys.FromCharacter((char)buffer[0])
            : GridKeys.FromKeySym((ulong)symbol);

        if (zscii != 0)
        {
            Key?.Invoke(zscii);
        }
    }

    /// <summary>
    /// Fills the surface and hands the pixels to the server.
    /// </summary>
    private void Draw()
    {
        if (_image == 0 || Surface.Width == 0 || Surface.Height == 0)
        {
            return;
        }

        Painting?.Invoke(Surface);

        // [x11] The image was made over memory this program owns, so
        // the pixels are copied into it and the server reads them from
        // there. A surface is the same shape as an X image of depth 24
        // on a little endian machine, which is every machine this will
        // run on, so it is a copy and nothing more.
        for (var at = 0; at < _copy.Length; at++)
        {
            _copy[at] = unchecked((int)Surface.Pixels[at]);
        }

        Marshal.Copy(_copy, 0, _pixels, _copy.Length);

        XPutImage(_display, _window, _context, _image, 0, 0, 0, 0, (uint)Surface.Width, (uint)Surface.Height);
        XFlush(_display);
    }

    /// <summary>
    /// Makes a surface and an image of the size the window now is.
    /// </summary>
    private void Resize(int width, int height)
    {
        Forget();

        Surface = new Surface(Math.Max(width, 0), Math.Max(height, 0));

        if (Surface.Width == 0 || Surface.Height == 0)
        {
            return;
        }

        _copy = new int[Surface.Width * Surface.Height];
        _pixels = Marshal.AllocHGlobal(_copy.Length * 4);

        _image = XCreateImage(
            _display,
            XDefaultVisual(_display, _screen),
            (uint)XDefaultDepth(_display, _screen),
            ZPixmap,
            0,
            _pixels,
            (uint)Surface.Width,
            (uint)Surface.Height,
            32,
            0);
    }

    /// <summary>
    /// Lets go of the image and the memory under it. [x11] The image
    /// itself is freed rather than destroyed, because destroying one
    /// frees the memory with it and this program owns that memory.
    /// </summary>
    private void Forget()
    {
        if (_image != 0)
        {
            XFree(_image);
            _image = 0;
        }

        if (_pixels != 0)
        {
            Marshal.FreeHGlobal(_pixels);
            _pixels = 0;
        }
    }

    // [x11] The event union, which is as large as its largest member.
    // The fields here are read out of it by where they sit, which is
    // what a union means, so several of them share a place.
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)]
        public int Type;

        [FieldOffset(32)]
        public nint Window;

        [FieldOffset(40)]
        public nint MessageType;

        [FieldOffset(48)]
        public int Format;

        [FieldOffset(56)]
        public nint MessageData;

        [FieldOffset(56)]
        public int ConfigureWidth;

        [FieldOffset(60)]
        public int ConfigureHeight;
    }

    [LibraryImport("libX11.so.6")]
    private static partial int XInitThreads();

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string? name);

    [LibraryImport("libX11.so.6")]
    private static partial void XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XDefaultScreen(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial nint XRootWindow(nint display, int screen);

    [LibraryImport("libX11.so.6")]
    private static partial nint XBlackPixel(nint display, int screen);

    [LibraryImport("libX11.so.6")]
    private static partial nint XDefaultVisual(nint display, int screen);

    [LibraryImport("libX11.so.6")]
    private static partial int XDefaultDepth(nint display, int screen);

    [LibraryImport("libX11.so.6")]
    private static partial nint XCreateSimpleWindow(
        nint display,
        nint parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        nint border,
        nint background);

    [LibraryImport("libX11.so.6")]
    private static partial void XDestroyWindow(nint display, nint window);

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void XStoreName(nint display, nint window, string title);

    [LibraryImport("libX11.so.6")]
    private static partial void XChangeProperty(
        nint display,
        nint window,
        nint property,
        nint type,
        int format,
        int mode,
        nint[] data,
        int count);

    [LibraryImport("libX11.so.6")]
    private static partial void XSelectInput(nint display, nint window, long mask);

    [LibraryImport("libX11.so.6")]
    private static partial void XMapWindow(nint display, nint window);

    [LibraryImport("libX11.so.6")]
    private static partial void XFlush(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial nint XCreateGC(nint display, nint drawable, ulong mask, nint values);

    [LibraryImport("libX11.so.6")]
    private static partial void XFreeGC(nint display, nint context);

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XInternAtom(
        nint display,
        string name,
        [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport("libX11.so.6")]
    private static partial void XSetWMProtocols(nint display, nint window, ref nint protocols, int count);

    [LibraryImport("libX11.so.6")]
    private static partial void XNextEvent(nint display, ref XEvent next);

    [LibraryImport("libX11.so.6")]
    private static partial void XSendEvent(
        nint display,
        nint window,
        [MarshalAs(UnmanagedType.Bool)] bool propagate,
        long mask,
        ref XEvent message);

    [LibraryImport("libX11.so.6")]
    private static partial int XLookupString(
        ref XEvent key,
        byte[] buffer,
        int count,
        out nint symbol,
        nint status);

    [LibraryImport("libX11.so.6")]
    private static partial nint XCreateImage(
        nint display,
        nint visual,
        uint depth,
        int format,
        int offset,
        nint data,
        uint width,
        uint height,
        int pad,
        int bytesPerLine);

    [LibraryImport("libX11.so.6")]
    private static partial void XPutImage(
        nint display,
        nint drawable,
        nint context,
        nint image,
        int sourceX,
        int sourceY,
        int destinationX,
        int destinationY,
        uint width,
        uint height);

    [LibraryImport("libX11.so.6")]
    private static partial void XFree(nint data);
}
