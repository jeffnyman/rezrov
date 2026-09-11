namespace Rezrov.Glulx.Glk;

/// <summary>
/// A Glk window: a panel of the display, of one of the types the
/// specification defines, with the stream that prints to it.
/// </summary>
/// <remarks>
/// [glk #window] Windows are the leaves and pair windows the forks of a
/// binary tree, and every window but the root has a pair window as its
/// parent. [glk #window_streams] Every window has a stream, made with
/// it and closed with it, and [glk #echo_streams] may have an echo
/// stream that receives a copy of everything printed to it.
/// </remarks>
public abstract class GlkWindow : GlkObject
{
    protected GlkWindow(uint rock, WindowType type)
        : base(rock)
    {
        Type = type;
        Stream = new WindowStream(this);
    }

    public WindowType Type { get; }

    /// <summary>
    /// [glk #window_pair] The pair window this is a child of.
    /// </summary>
    public PairWindow? Parent { get; internal set; }

    /// <summary>
    /// [glk #window_streams] The stream that prints here.
    /// </summary>
    public WindowStream Stream { get; }

    /// <summary>
    /// [glk #echo_streams] Where output is copied, if anywhere.
    /// </summary>
    public GlkStream? EchoStream { get; set; }

    /// <summary>
    /// [glk #window_changing] The width the layout gave the window, in
    /// its own units, which are character cells for text windows and
    /// nothing at all for blank and pair windows.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>The height the layout gave the window.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// Whether this window can show a size: blank and pair windows have
    /// none.
    /// </summary>
    public bool HasSize => Type is WindowType.TextBuffer or WindowType.TextGrid;

    /// <summary>
    /// [glk #line_events] The pending line input request, or null.
    /// </summary>
    public LineRequest? LineRequest { get; internal set; }

    /// <summary>
    /// [glk #char_events] The pending character input request, if any.
    /// </summary>
    public CharRequest CharRequest { get; internal set; }

    /// <summary>
    /// [glk op:set_echo_line_event] Whether a completed line is shown in
    /// the window, which it is unless the game says otherwise.
    /// </summary>
    public bool EchoLineInput { get; set; } = true;

    /// <summary>
    /// [glk op:set_terminators_line_event] The special keys that end
    /// line input besides enter.
    /// </summary>
    public IReadOnlyList<uint> LineTerminators { get; internal set; } = [];

    /// <summary>
    /// [glk #window_textbuf] Prints one character to the window, in a
    /// style, as the window's stream does.
    /// </summary>
    internal abstract void Put(uint character, GlkStyle style);

    /// <summary>[glk op:window_clear] Erases the window.</summary>
    public abstract void Clear();

    /// <summary>The layout's new size for the window.</summary>
    internal void Resize(int width, int height)
    {
        if (!HasSize)
        {
            return;
        }

        var oldWidth = Width;
        var oldHeight = Height;
        Width = Math.Max(width, 0);
        Height = Math.Max(height, 0);
        Resized(oldWidth, oldHeight);
    }

    /// <summary>What a window does with a new size.</summary>
    protected virtual void Resized(int oldWidth, int oldHeight)
    {
    }
}

/// <summary>
/// [glk #window_pair] A pair window: the fork that holds two windows and
/// the constraint that divides its space between them.
/// </summary>
/// <remarks>
/// [glk #window_opening] The constraint belongs to the pair: a
/// direction naming the child that is sized, a division that is fixed
/// or proportional, the size, and a key window whose units a fixed
/// size is measured in. <see cref="First"/> and <see cref="Second"/>
/// are the children in display order, top then bottom or left then
/// right, and the direction says which of them is the sized one.
/// </remarks>
public sealed class PairWindow : GlkWindow
{
    internal PairWindow(GlkWindow first, GlkWindow second, WindowMethod method, uint size, GlkWindow? key)
        : base(0, WindowType.Pair)
    {
        First = first;
        Second = second;
        Direction = method & WindowMethod.DirectionMask;
        Division = method & WindowMethod.DivisionMask;
        Border = (method & WindowMethod.NoBorder) == 0;
        Size = size;
        Key = key;
    }

    /// <summary>The child shown above or to the left.</summary>
    public GlkWindow First { get; internal set; }

    /// <summary>The child shown below or to the right.</summary>
    public GlkWindow Second { get; internal set; }

    /// <summary>
    /// [glk #window_opening] Which side the sized child is on: Above or
    /// Left for the first child, Below or Right for the second.
    /// </summary>
    public WindowMethod Direction { get; internal set; }

    /// <summary>Fixed or Proportional.</summary>
    public WindowMethod Division { get; internal set; }

    /// <summary>
    /// The size of the sized child: cells of the key window for a fixed
    /// division, a percentage for a proportional one.
    /// </summary>
    public uint Size { get; internal set; }

    /// <summary>
    /// [glk #window_opening] The window whose units a fixed size is
    /// measured in, or null after it was closed, which leaves the sized
    /// child with nothing.
    /// </summary>
    public GlkWindow? Key { get; internal set; }

    /// <summary>[glk #window_opening] The border hint.</summary>
    public bool Border { get; internal set; }

    /// <summary>Whether the children are stacked top and bottom.</summary>
    public bool IsVertical => Direction is WindowMethod.Above or WindowMethod.Below;

    /// <summary>The child the size applies to.</summary>
    public GlkWindow SizedChild => Direction is WindowMethod.Above or WindowMethod.Left ? First : Second;

    /// <summary>
    /// The method as a game would pass it, all three parts.
    /// </summary>
    public WindowMethod Method => Direction | Division | (Border ? 0 : WindowMethod.NoBorder);

    internal override void Put(uint character, GlkStyle style)
    {
    }

    public override void Clear()
    {
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> where a child was.
    /// </summary>
    internal void Replace(GlkWindow child, GlkWindow replacement)
    {
        if (ReferenceEquals(First, child))
        {
            First = replacement;
        }
        else
        {
            Second = replacement;
        }

        replacement.Parent = this;
    }
}

/// <summary>
/// [glk #window_types] A blank window, which shows nothing.
/// </summary>
public sealed class BlankWindow : GlkWindow
{
    internal BlankWindow(uint rock)
        : base(rock, WindowType.Blank)
    {
    }

    internal override void Put(uint character, GlkStyle style)
    {
    }

    public override void Clear()
    {
    }
}

/// <summary>
/// [glk #window_textbuf] A text buffer window: a stream of text that
/// the display shows as it is printed.
/// </summary>
public sealed class TextBufferWindow : GlkWindow
{
    private readonly IGlkDisplay _display;

    internal TextBufferWindow(uint rock, IGlkDisplay display)
        : base(rock, WindowType.TextBuffer)
    {
        _display = display;
    }

    internal override void Put(uint character, GlkStyle style) => _display.Print(this, character, style);

    public override void Clear() => _display.Clear(this);
}

/// <summary>
/// [glk #window_textgrid] A text grid window: a rectangle of characters
/// in a fixed-width font, with a cursor that output moves through.
/// </summary>
public sealed class TextGridWindow : GlkWindow
{
    private uint[] _characters = [];
    private GlkStyle[] _styles = [];

    internal TextGridWindow(uint rock)
        : base(rock, WindowType.TextGrid)
    {
    }

    /// <summary>The column the next character goes in.</summary>
    public int CursorX { get; private set; }

    /// <summary>The row the next character goes in.</summary>
    public int CursorY { get; private set; }

    /// <summary>
    /// The character at a cell, a space where nothing was put.
    /// </summary>
    public uint CharacterAt(int x, int y) => _characters[(y * Width) + x];

    /// <summary>The style at a cell.</summary>
    public GlkStyle StyleAt(int x, int y) => _styles[(y * Width) + x];

    /// <summary>One row of the grid as text.</summary>
    public string Row(int y)
    {
        var text = new System.Text.StringBuilder(Width);
        for (var x = 0; x < Width; x++)
        {
            text.Append(GlkText.ToString(CharacterAt(x, y)));
        }

        return text.ToString();
    }

    /// <summary>
    /// [glk op:window_move_cursor] Puts the cursor at a cell. A column
    /// past the end wraps to the next row when something is printed,
    /// and a row past the bottom is off the window.
    /// </summary>
    public void MoveCursor(uint x, uint y)
    {
        CursorX = (int)Math.Min(x, int.MaxValue);
        CursorY = (int)Math.Min(y, int.MaxValue);
    }

    internal override void Put(uint character, GlkStyle style)
    {
        // [glk #window_textgrid] Characters are laid into the array left
        // to right and top to bottom; a newline or the end of a row
        // moves to the start of the next row, and off the last row
        // nothing more happens until the cursor is moved.
        if (character == '\n')
        {
            CursorX = 0;
            CursorY++;
            return;
        }

        if (CursorX >= Width)
        {
            CursorX = 0;
            CursorY++;
        }

        if (CursorY >= Height || Width == 0)
        {
            return;
        }

        _characters[(CursorY * Width) + CursorX] = character;
        _styles[(CursorY * Width) + CursorX] = style;
        CursorX++;
    }

    public override void Clear()
    {
        // [glk op:window_clear] Blanks in the normal style, and the
        // cursor at the top left.
        Array.Fill(_characters, (uint)' ');
        Array.Fill(_styles, GlkStyle.Normal);
        CursorX = 0;
        CursorY = 0;
    }

    protected override void Resized(int oldWidth, int oldHeight)
    {
        // [glk #window_textgrid] Smaller keeps the top left area; larger
        // fills the new area with blanks.
        var characters = new uint[Width * Height];
        var styles = new GlkStyle[Width * Height];
        Array.Fill(characters, (uint)' ');

        for (var y = 0; y < Math.Min(oldHeight, Height); y++)
        {
            for (var x = 0; x < Math.Min(oldWidth, Width); x++)
            {
                characters[(y * Width) + x] = _characters[(y * oldWidth) + x];
                styles[(y * Width) + x] = _styles[(y * oldWidth) + x];
            }
        }

        _characters = characters;
        _styles = styles;
    }
}
