using System.Runtime.InteropServices;

namespace Rezrov.Gtui;

/// <summary>
/// A window on Windows, opened by asking the operating system directly.
/// </summary>
/// <remarks>
/// This is the part a toolkit usually hides. There is no framework
/// underneath it: the program registers a window class, creates a
/// window, and runs the loop that takes messages off its queue and
/// answers them, which is the same thing every Windows program has done
/// since 1985. Drawing is one call, handing the operating system the
/// rectangle of pixels the drawing code filled in.
///
/// The window belongs to the thread that made it, which is the thread
/// that must run the message loop, so the game runs on a thread of its
/// own and only ever touches the surface and asks for a repaint.
/// </remarks>
internal sealed partial class Win32Window : IDisposable
{
    private const string ClassName = "RezrovGrid";

    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint SwShow = 5;

    private const uint WmDestroy = 0x0002;
    private const uint WmSize = 0x0005;
    private const uint WmPaint = 0x000F;
    private const uint WmClose = 0x0010;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmKeyDown = 0x0100;
    private const uint WmChar = 0x0102;

    private const int BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint SrcCopy = 0x00CC0020;

    private const int CwUseDefault = unchecked((int)0x80000000);

    // The callback the operating system holds a pointer to. It has to
    // outlive the window, and nothing else refers to it, so it is kept
    // here on purpose rather than by accident.
    private readonly WindowProcedure _procedure;

    private nint _window;
    private nint _className;
    private bool _closed;

    public Win32Window()
    {
        _procedure = Handle;
        Surface = new Surface(0, 0);
    }

    /// <summary>The pixels the window shows.</summary>
    public Surface Surface { get; private set; }

    /// <summary>
    /// A character the player typed, as a Unicode character.
    /// </summary>
    public Action<char>? Typed { get; set; }

    /// <summary>
    /// A key with no character of its own, as its virtual key code.
    /// </summary>
    public Action<int>? Pressed { get; set; }

    /// <summary>The window changed size, and the surface with it.</summary>
    public Action? Resized { get; set; }

    /// <summary>
    /// Fill the surface: called on the thread that owns the window,
    /// just before the pixels are handed over, so that nothing is drawn
    /// into a surface while the operating system is reading it.
    /// </summary>
    public Action<Surface>? Painting { get; set; }

    /// <summary>Whether the window has gone away.</summary>
    public bool Closed => _closed;

    /// <summary>
    /// Registers the window class and opens the window. The size asked
    /// for is the drawing area, and the frame is added around it, so a
    /// program that wants eighty columns gets eighty columns.
    /// </summary>
    public void Open(string title, int width, int height)
    {
        var self = GetModuleHandleW(null);

        _className = Marshal.StringToHGlobalUni(ClassName);

        var registration = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = 0x0003, // CS_HREDRAW | CS_VREDRAW: a resize repaints
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = self,
            Cursor = LoadCursorW(0, 32512), // IDC_ARROW
            ClassName = _className,
        };

        if (RegisterClassExW(ref registration) == 0)
        {
            throw new InvalidOperationException(
                $"The window class could not be registered: {Marshal.GetLastWin32Error()}.");
        }

        var wanted = new Rectangle { Right = width, Bottom = height };
        AdjustWindowRect(ref wanted, WsOverlappedWindow, false);

        _window = CreateWindowExW(
            0,
            ClassName,
            title,
            WsOverlappedWindow,
            CwUseDefault,
            CwUseDefault,
            wanted.Right - wanted.Left,
            wanted.Bottom - wanted.Top,
            0,
            0,
            self,
            0);

        if (_window == 0)
        {
            throw new InvalidOperationException(
                $"The window could not be opened: {Marshal.GetLastWin32Error()}.");
        }

        Resize();
        ShowWindow(_window, SwShow);
        UpdateWindow(_window);
    }

    /// <summary>
    /// Asks for the window to be painted again. Safe from the thread
    /// running the game, since this only marks the window and the
    /// painting itself happens on the thread that owns it.
    /// </summary>
    public void Redraw()
    {
        if (_window != 0)
        {
            InvalidateRect(_window, 0, false);
        }
    }

    /// <summary>
    /// Asks the window to close, from any thread, which is how the
    /// game says it has finished.
    /// </summary>
    public void Close()
    {
        if (_window != 0)
        {
            PostMessageW(_window, WmClose, 0, 0);
        }
    }

    /// <summary>
    /// Takes messages off the queue and answers them until the window
    /// closes. This is the program's main loop.
    /// </summary>
    public void Run()
    {
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        _closed = true;
    }

    public void Dispose()
    {
        if (_window != 0)
        {
            DestroyWindow(_window);
            _window = 0;
        }

        if (_className != 0)
        {
            Marshal.FreeHGlobal(_className);
            _className = 0;
        }
    }

    /// <summary>
    /// The window procedure: everything the operating system has to say
    /// about this window arrives here.
    /// </summary>
    private nint Handle(nint window, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WmPaint:
                Draw(window);
                return 0;

            // The background is never erased, because every pixel is
            // painted every time, and erasing first is what makes a
            // window of text flicker.
            case WmEraseBackground:
                return 1;

            case WmSize:
                Resize();
                Resized?.Invoke();
                return 0;

            case WmChar:
                Typed?.Invoke((char)wParam);
                return 0;

            case WmKeyDown:
                Pressed?.Invoke((int)wParam);
                return 0;

            case WmClose:
                _closed = true;
                DestroyWindow(window);
                return 0;

            case WmDestroy:
                _closed = true;
                PostQuitMessage(0);
                return 0;

            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    /// <summary>
    /// Hands the pixels to the operating system as they stand. The
    /// bitmap is described with a negative height, which is what says
    /// the first row in memory is the top row of the picture rather
    /// than the bottom.
    /// </summary>
    private void Draw(nint window)
    {
        var device = BeginPaint(window, out var paint);

        if (device != 0 && Surface.Width > 0 && Surface.Height > 0)
        {
            Painting?.Invoke(Surface);

            var info = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = Surface.Width,
                Height = -Surface.Height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
            };

            StretchDIBits(
                device,
                0,
                0,
                Surface.Width,
                Surface.Height,
                0,
                0,
                Surface.Width,
                Surface.Height,
                ref MemoryMarshal.GetArrayDataReference(Surface.Pixels),
                ref info,
                DibRgbColors,
                SrcCopy);
        }

        EndPaint(window, ref paint);
    }

    /// <summary>
    /// Makes a surface the size of the window's drawing area. A window
    /// dragged to nothing has no pixels, which is allowed, and the
    /// surface simply has none either.
    /// </summary>
    private void Resize()
    {
        if (_window == 0 || !GetClientRect(_window, out var area))
        {
            return;
        }

        var width = Math.Max(area.Right - area.Left, 0);
        var height = Math.Max(area.Bottom - area.Top, 0);

        if (width != Surface.Width || height != Surface.Height)
        {
            Surface = new Surface(width, height);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    // Every field is a number, which is what lets the operating system
    // be called without anything having to be converted on the way. The
    // class name is a pointer to characters the program keeps for as
    // long as the class is registered, rather than a string the runtime
    // might move.
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ExtraClassBytes;
        public int ExtraWindowBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint Device;
        public int Erase;
        public Rectangle Area;
        public int Restore;
        public int IncUpdate;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
        public long Reserved3;
    }

    [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WindowClass registration);

    [LibraryImport("user32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint exStyle,
        string className,
        string title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32")]
    private static partial nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32")]
    private static partial int GetMessageW(out Message message, nint window, uint first, uint last);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Message message);

    [LibraryImport("user32")]
    private static partial nint DispatchMessageW(ref Message message);

    [LibraryImport("user32")]
    private static partial void PostQuitMessage(int code);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, uint command);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint window);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint window, nint area, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out Rectangle area);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRect(
        ref Rectangle area,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool menu);

    [LibraryImport("user32", SetLastError = true)]
    private static partial nint LoadCursorW(nint instance, nint cursor);

    [LibraryImport("user32")]
    private static partial nint BeginPaint(nint window, out PaintStruct paint);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPaint(nint window, ref PaintStruct paint);

    [LibraryImport("gdi32")]
    private static partial int StretchDIBits(
        nint device,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        ref uint pixels,
        ref BitmapInfoHeader info,
        uint usage,
        uint operation);
}
