using System.Text;

using Rezrov.Core.Graphics;
using Rezrov.Glulx.Glk;

namespace Rezrov.Cli;

/// <summary>
/// Follows a Glulx game by watching what it prints, so that the rooms it
/// goes through can be mapped.
/// </summary>
/// <remarks>
/// A Glulx story keeps nothing an interpreter can ask. There is no
/// status line holding the room, no global the compiler agrees to put it
/// in, and no object tree to look the answer up in: from out here a
/// Glulx game is a stream of styled text and a request for a line. So
/// the room is taken from the one place it does appear, which is the
/// heading Inform prints as the player walks into it, in the style
/// [glk #stream_styles] sets aside for a subheader.
///
/// Being a heading is not enough on its own, because front matter is
/// styled the same way. What tells them apart is the shape of the page
/// rather than the words on it: Inform follows a room's heading with the
/// description underneath it and then stops for a command, while a title
/// or a content warning stands on its own. So a heading counts only once
/// ordinary prose has followed it and the game has asked for a line.
///
/// This sits around the display rather than inside the library, because
/// none of it is the interpreter's business. Everything is passed
/// straight through.
/// </remarks>
public sealed class HeadingWatcher : IGlkDisplay
{
    private readonly IGlkDisplay _inner;
    private readonly StringBuilder _line = new();

    private bool _heading = true;
    private string? _room;
    private bool _prose;

    public HeadingWatcher(IGlkDisplay inner, RoomWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(watcher);

        _inner = inner;
        Watcher = watcher;
    }

    /// <summary>The map being built from what the game prints.</summary>
    public RoomWatcher Watcher { get; }

    public void Print(GlkWindow window, uint character, GlkStyle style, uint link)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Type == WindowType.TextBuffer)
        {
            Read(character, style);
        }

        _inner.Print(window, character, style, link);
    }

    /// <summary>
    /// Takes one character of the story's text, keeping track of the
    /// last line that was a heading and whether anything has been said
    /// since.
    /// </summary>
    private void Read(uint character, GlkStyle style)
    {
        if (character == '\n')
        {
            Finish();
            return;
        }

        // A heading owns its whole line. One bold word at the start of a
        // sentence is a sentence, which is how a game that prints every
        // object's name in the heading style would otherwise fill the
        // map with rooms called "ear" and "lamp".
        if (!char.IsWhiteSpace((char)character))
        {
            _heading &= style is GlkStyle.Subheader or GlkStyle.Header;
        }

        _line.Append(GlkText.ToString(character));
    }

    private void Finish()
    {
        var text = _line.ToString().Trim();
        _line.Clear();

        if (text.Length == 0)
        {
            _heading = true;
            return;
        }

        if (_heading)
        {
            _room = text;
            _prose = false;
        }
        else if (_room is not null)
        {
            _prose = true;
        }

        _heading = true;
    }

    public GlkInput WaitForInput(
        IReadOnlyList<GlkWindow> lineRequests,
        IReadOnlyList<GlkWindow> charRequests,
        TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(lineRequests);

        // Asking for a command is the end of a turn, and the only moment
        // the map is sure the page is finished. A request for a single
        // key is not: that is a menu or a [MORE] prompt, and the story
        // has not necessarily said where the player is yet.
        if (lineRequests.Count > 0)
        {
            // The prompt itself is prose, and prints before this is
            // reached, so a heading with nothing after it has genuinely
            // had nothing after it.
            if (_room is not null && _prose)
            {
                Watcher.StandingIn(_room);
            }

            _room = null;
            _prose = false;

            // Whatever is on the line when the story stops for a command
            // is the prompt, and belongs to the turn that is ending
            // rather than the one about to start. Left there it would be
            // the first thing on the next turn's opening line, and a
            // prompt is not written in the heading style, so every
            // heading after the first would stop being one.
            _line.Clear();
            _heading = true;
        }

        var input = _inner.WaitForInput(lineRequests, charRequests, timeout);

        if (input.Kind == GlkInputKind.Line && input.Text is { } typed)
        {
            Watcher.Typed(typed);
        }

        return input;
    }

    public int Width => _inner.Width;

    public int Height => _inner.Height;

    public int CellWidth => _inner.CellWidth;

    public int CellHeight => _inner.CellHeight;

    public void Clear(GlkWindow window)
    {
        _line.Clear();
        _heading = true;
        _inner.Clear(window);
    }

    public void Arranged(GlkWindow? root) => _inner.Arranged(root);

    public void Wake() => _inner.Wake();

    public void FlowBreak(GlkWindow window) => _inner.FlowBreak(window);

    public void Drawn(GlkWindow window) => _inner.Drawn(window);

    public bool CanReportMouse(WindowType type) => _inner.CanReportMouse(type);

    public bool CanReportHyperlinks(WindowType type) => _inner.CanReportHyperlinks(type);

    public bool CanDrawImages(WindowType type) => _inner.CanDrawImages(type);

    public GlkAppearance? Appearance(GlkWindow window, GlkStyle style) =>
        _inner.Appearance(window, style);

    public bool DrawImage(
        GlkWindow window,
        uint image,
        Pixels picture,
        ImageAlign align,
        ImageSizing sizing,
        uint link) =>
        _inner.DrawImage(window, image, picture, align, sizing, link);
}
