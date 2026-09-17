using System.Text;
using Rezrov.Core.Graphics;
using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// A piece of a line, ready to be drawn: some text in one style, at a
/// place along the line.
/// </summary>
/// <param name="Text">The characters.</param>
/// <param name="Style">The style they are in.</param>
/// <param name="Link">
/// [glk #link_creating] The link they belong to, or zero for none.
/// </param>
/// <param name="Left">How far along the line the piece starts.</param>
/// <param name="Width">How wide it is.</param>
public sealed record Piece(string Text, GlkStyle Style, uint Link, double Left, double Width);

/// <summary>
/// [glk #graphics_textbuf] A picture laid out in a line, at the size
/// the image rules worked out for the width the text was laid out at.
/// </summary>
/// <param name="Picture">The pixels to draw.</param>
/// <param name="Left">How far along the line it starts.</param>
/// <param name="Top">
/// How far below the top of the line it starts, which is negative for
/// a margin picture that began on an earlier line.
/// </param>
/// <param name="Width">How wide to draw it.</param>
/// <param name="Height">How tall to draw it.</param>
public sealed record Inset(Pixels Picture, double Left, double Top, double Width, double Height);

/// <summary>
/// One laid out line of a text buffer: the pieces across it, the
/// pictures in it, how tall it is, and where its baseline sits.
/// </summary>
public sealed record Line(IReadOnlyList<Piece> Pieces, IReadOnlyList<Inset> Images, double Height, double Baseline);

/// <summary>
/// The text of one Glk text buffer window: what the game has printed,
/// and the lines it comes out as at a given width.
/// </summary>
/// <remarks>
/// [glk #window_textbuf] A text buffer is a stream of text, handed over
/// a character at a time as it is printed, that the library expects the
/// display to wrap, scroll, and keep. The text is kept here as runs of
/// one style, which is how it arrives and how it is drawn; the lines
/// are worked out from them whenever the width changes, since a window
/// that is made narrower has to wrap again from the top.
///
/// Wrapping breaks between words, and a word too long for a whole line
/// is broken where it reaches the edge rather than left to run off it.
///
/// [glk #graphics_textbuf] Pictures are part of the same stream, and
/// are laid out with the text rather than drawn over it: an inline one
/// takes its place in the run of the words, and a margin one stands the
/// text aside for as many lines as it takes to pass. Their sizes are
/// worked out here rather than when the game asked, since a width given
/// as a fraction of the window is a different number in a window of a
/// different size.
/// </remarks>
public sealed class BufferText(IGlyphs glyphs)
{
    private readonly List<Item> _items = [];
    private readonly IGlyphs _glyphs = glyphs;
    private List<Line> _lines = [];
    private double _width = -1;

    /// <summary>
    /// How many characters have been printed, which is where a mark can
    /// be put to find what arrived after it.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// [glk #graphics_textbuf] Whether nothing has been printed since
    /// the last line ending, which is the one place a margin picture
    /// may go.
    /// </summary>
    public bool AtLineStart { get; private set; } = true;

    /// <summary>
    /// [glk #line_events] The line the player is typing, which the
    /// display shows because the library does not: it is drawn after
    /// everything printed so far and is not part of the text.
    /// </summary>
    public string Pending
    {
        get;
        set
        {
            field = value;
            _width = -1;
            Scroll = 0;
        }
    }

    = "";

    /// <summary>
    /// How far back the player has scrolled, in pixels, with zero
    /// meaning the end of the text is in view.
    /// </summary>
    public double Scroll { get; private set; }

    /// <summary>
    /// [glk #window_textbuf] Adds a character in a style, as part of a
    /// link or of none.
    /// </summary>
    public void Put(uint character, GlkStyle style, uint link)
    {
        var text = GlkText.ToString(character);
        if (text.Length == 0)
        {
            return;
        }

        // A run holds as much as it can: the text arrives one character
        // at a time, and a run for each would be thousands of them.
        if (_items.Count > 0 && _items[^1] is Run last && last.Style == style && last.Link == link)
        {
            last.Text.Append(text);
        }
        else
        {
            _items.Add(new Run(new StringBuilder(text), style, link));
        }

        Length += text.Length;
        AtLineStart = text == "\n";
        Changed();
    }

    /// <summary>
    /// [glk #graphics_textbuf] Puts a picture in the run of the text,
    /// and says whether it was placed.
    /// </summary>
    /// <remarks>
    /// The size is kept as the rules that work it out rather than as a
    /// number, since the specification says a picture measured against
    /// the window's width resizes when the window does.
    ///
    /// A margin picture may only go at the start of a line, and one
    /// asked for anywhere else is refused outright, which is what the
    /// specification says becomes of it. An inline picture counts as
    /// text for that rule, while a margin one does not, since two of
    /// them may share a line.
    /// </remarks>
    public bool Draw(Pixels picture, ImageAlign align, ImageSizing sizing)
    {
        ArgumentNullException.ThrowIfNull(picture);

        var margin = align is ImageAlign.MarginLeft or ImageAlign.MarginRight;
        if (margin && !AtLineStart)
        {
            return false;
        }

        _items.Add(new Drawn(picture, align, sizing));
        AtLineStart = AtLineStart && margin;
        Changed();
        return true;
    }

    /// <summary>
    /// [glk op:window_flow_break] Puts a mark in the text which, where
    /// the text beside it is standing aside for a margin picture, moves
    /// it down below every such picture, and where it is not, does
    /// nothing.
    /// </summary>
    /// <remarks>
    /// [glk #graphics_textbuf] Which of the two it turns out to be is
    /// not known when the game asks, since it depends on where the
    /// pictures fall once the text has been wrapped. So the mark is
    /// kept as a mark and works itself out every time the text is laid
    /// out, which is what the specification describes.
    /// </remarks>
    public void FlowBreak()
    {
        _items.Add(new Parted());
        Changed();
    }

    /// <summary>[glk op:window_clear] Throws all of it away.</summary>
    public void Clear()
    {
        _items.Clear();
        _lines = [];
        Length = 0;
        AtLineStart = true;
        _width = -1;
        Scroll = 0;
    }

    /// <summary>
    /// The lines the text comes out as at the given width, worked out
    /// again only when the width or the text has changed.
    /// </summary>
    public IReadOnlyList<Line> Lines(double width)
    {
        if (Math.Abs(width - _width) < 0.01)
        {
            return _lines;
        }

        _width = width;
        _lines = Wrap(width);
        return _lines;
    }

    /// <summary>How tall all of it is at the given width.</summary>
    public double Height(double width)
    {
        var total = 0.0;
        foreach (var line in Lines(width))
        {
            total += line.Height;
        }

        return total;
    }

    /// <summary>
    /// The furthest back it is worth scrolling: enough to bring the
    /// first line into view and no further.
    /// </summary>
    public double Furthest(double width, double height) => Math.Max(Height(width) - height, 0);

    /// <summary>
    /// Moves the view back by so many pixels, or forward for a negative
    /// number, as far as there is text to see.
    /// </summary>
    public void ScrollBy(double pixels, double width, double height) =>
        Scroll = Math.Clamp(Scroll + pixels, 0, Furthest(width, height));

    /// <summary>Brings the end of the text back into view.</summary>
    public void ScrollToEnd() => Scroll = 0;

    /// <summary>
    /// Where the bottom of the last line sits, measured down from the
    /// top of a window of the given height.
    /// </summary>
    /// <remarks>
    /// [glk #window_textbuf] While the text fits, it starts at the top
    /// of the window and the space below it is empty, as a page of text
    /// does. Once there is more of it than fits, the last line sits
    /// against the bottom edge and the earlier part runs off the top,
    /// so that what the game printed last is always the part in view.
    ///
    /// Scrolling back moves the whole of it down, which brings the
    /// earlier part into view and takes the last line off the bottom.
    /// The amount is clamped here as well as where it is set, since a
    /// window made taller can leave a scroll that was reasonable at the
    /// old size further back than there is now text to show.
    /// </remarks>
    public double Bottom(double width, double height) =>
        Math.Min(height, Height(width)) + Math.Clamp(Scroll, 0, Furthest(width, height));

    /// <summary>
    /// Something new to read means the player wants to see it, so
    /// anything they had scrolled back to is left behind, and the
    /// lines have to be worked out again.
    /// </summary>
    private void Changed()
    {
        _width = -1;
        Scroll = 0;
    }

    private List<Line> Wrap(double width)
    {
        var layout = new Layout(_glyphs, width);

        foreach (var item in Showing())
        {
            switch (item)
            {
                case Run run:
                    layout.Write(run);
                    break;
                case Drawn picture:
                    layout.Place(picture);
                    break;
                default:
                    layout.Part();
                    break;
            }
        }

        return layout.Finish();
    }

    // [glk #line_events] The line being typed goes on the end, in the
    // style Glk keeps for it, so it wraps with the text before it.
    private IEnumerable<Item> Showing() => Pending.Length == 0
        ? _items
        : _items.Append(new Run(new StringBuilder(Pending), GlkStyle.Input, 0));

    /// <summary>Something in the stream of a text buffer.</summary>
    private abstract record Item;

    private sealed record Run(StringBuilder Text, GlkStyle Style, uint Link) : Item;

    private sealed record Drawn(Pixels Picture, ImageAlign Align, ImageSizing Sizing) : Item;

    private sealed record Parted : Item;

    /// <summary>
    /// The wrapping itself: a pen moving along a line, a line box
    /// growing around whatever is put in it, and the margin pictures
    /// the lines have to make room for.
    /// </summary>
    /// <remarks>
    /// A line box is kept as how far it reaches above its baseline and
    /// how far below, rather than as a height. That is what lets a
    /// heading and the prose beside it sit on the same line, and it is
    /// what [glk #graphics_textbuf] the inline alignments are defined
    /// against: one picture stands on the baseline, another hangs from
    /// the top of the line, and a third is centered between the two.
    /// </remarks>
    private sealed class Layout
    {
        private readonly List<Line> _lines = [];
        private readonly List<Piece> _pieces = [];
        private readonly List<Waiting> _inline = [];
        private readonly List<Margin> _margins = [];
        private readonly IGlyphs _glyphs;
        private readonly double _width;
        private double _top;
        private double _start;
        private double _limit;
        private double _left;
        private double _above;
        private double _below;

        public Layout(IGlyphs glyphs, double width)
        {
            _glyphs = glyphs;
            _width = width;
            Open();
        }

        /// <summary>How wide the current line may be.</summary>
        private double Room => Math.Max(_limit - _start, 0);

        /// <summary>Lays out a run of text in one style.</summary>
        public void Write(Run run)
        {
            foreach (var part in Split(run.Text.ToString()))
            {
                if (part == "\n")
                {
                    Grow(run.Style);
                    Close();
                    continue;
                }

                var measured = _glyphs.Width(part, run.Style);

                // [glk #window_textbuf] A word that does not fit goes on
                // the next line, unless the line is empty, in which case
                // it is broken where it reaches the edge: something has
                // to give, and dropping characters is worse.
                if (_left + measured > _limit && _left > _start && part != " ")
                {
                    Close();
                }

                if (measured > Room && Room > 0)
                {
                    var room = Room;
                    foreach (var (text, size) in Broken(part, run.Style, room))
                    {
                        if (_left + size > _limit && _left > _start)
                        {
                            Close();
                        }

                        Add(text, run, size);
                    }

                    continue;
                }

                // A space that falls at the edge is kept where it is
                // rather than pushed to the next line, where it would
                // show as an indent.
                Add(part, run, measured);
            }
        }

        /// <summary>
        /// [glk #graphics_textbuf] Puts a picture where the stream said
        /// it goes, at the size the rules work out for this width.
        /// </summary>
        public void Place(Drawn picture)
        {
            // [glk #graphics_textbuf] A width given as a fraction is a
            // fraction of the whole window, not of what is left of the
            // line beside a margin picture.
            var (across, down) = picture.Sizing.For(
                (int)Math.Round(_width),
                picture.Picture.Width,
                picture.Picture.Height);

            if (across <= 0 || down <= 0)
            {
                return;
            }

            if (picture.Align is ImageAlign.MarginLeft or ImageAlign.MarginRight)
            {
                Aside(picture.Picture, picture.Align == ImageAlign.MarginLeft, across, down);
            }
            else
            {
                Among(picture.Picture, picture.Align, across, down);
            }
        }

        /// <summary>
        /// [glk op:window_flow_break] Moves the text down below every
        /// margin picture it is standing aside for, and does nothing
        /// where it is standing aside for none.
        /// </summary>
        public void Part()
        {
            if (_pieces.Count > 0 || _inline.Count > 0)
            {
                Close();
            }

            if (Past() is { } below)
            {
                // The space is an empty line as tall as the gap, so that
                // everything measured in lines goes on being measured in
                // lines: how tall the text is, where the bottom of it
                // sits, and how far back it scrolls.
                Emit([], 0, below - _top);
            }
        }

        /// <summary>The lines, once there is no more to come.</summary>
        public List<Line> Finish()
        {
            if (_pieces.Count > 0 || _inline.Count > 0 || _lines.Count == 0)
            {
                if (_above + _below <= 0)
                {
                    Grow(GlkStyle.Normal);
                }

                Close();
            }

            // [glk #graphics_textbuf] A margin picture taller than the
            // text beside it reaches below the last line, and that
            // space is part of how tall the text is, or the picture
            // would hang below the window where it cannot be seen.
            if (Past() is { } below)
            {
                Emit([], 0, below - _top);
            }

            return _lines;
        }

        /// <summary>
        /// The text as words, the spaces between them, and the line
        /// breaks, each of which the wrapper treats differently.
        /// </summary>
        private static IEnumerable<string> Split(string text)
        {
            var word = new StringBuilder();

            foreach (var character in text)
            {
                if (character == '\n' || character == ' ')
                {
                    if (word.Length > 0)
                    {
                        yield return word.ToString();
                        word.Clear();
                    }

                    yield return character == '\n' ? "\n" : " ";
                    continue;
                }

                word.Append(character);
            }

            if (word.Length > 0)
            {
                yield return word.ToString();
            }
        }

        /// <summary>
        /// [glk #graphics_textbuf] Stands the text aside for a picture
        /// in one of the margins.
        /// </summary>
        /// <remarks>
        /// A margin picture is only ever kept at the start of a line, so
        /// the line it heads is still empty and there is nothing to move
        /// out of its way. A second picture in the same margin goes
        /// beside the first, which is the stacking the specification
        /// warns about and asks for a flow break to avoid.
        /// </remarks>
        private void Aside(Pixels picture, bool left, double across, double down)
        {
            var from = left ? _start : _limit - across;
            _margins.Add(new Margin(picture, left, _top, _top + down, from, from + across));

            // The line the picture heads has to make room for it too.
            Open();
        }

        /// <summary>
        /// [glk #graphics_textbuf] Puts a picture in the run of the
        /// words, standing on the baseline, hanging from the top of the
        /// line, or centered between the two.
        /// </summary>
        /// <remarks>
        /// Glk gives a picture no style of its own, and the
        /// specification speaks of the top and the baseline of "the line
        /// of text", so the line it is placed against is a line of
        /// ordinary prose.
        /// </remarks>
        private void Among(Pixels picture, ImageAlign align, double across, double down)
        {
            if (_left + across > _limit && _left > _start)
            {
                Close();
            }

            var ascent = _glyphs.Baseline(GlkStyle.Normal);
            var above = align switch
            {
                ImageAlign.InlineUp => down,
                ImageAlign.InlineDown => ascent,
                _ => (ascent + down) / 2,
            };

            _inline.Add(new Waiting(picture, _left, above, across, down));
            _left += across;
            _above = Math.Max(_above, above);
            _below = Math.Max(_below, down - above);
        }

        private void Add(string text, Run run, double measured)
        {
            _pieces.Add(new Piece(text, run.Style, run.Link, _left, measured));
            _left += measured;
            Grow(run.Style);
        }

        /// <summary>
        /// Makes the line box at least as tall as a line of the given
        /// style, above and below the baseline both.
        /// </summary>
        private void Grow(GlkStyle style)
        {
            var baseline = _glyphs.Baseline(style);
            _above = Math.Max(_above, baseline);
            _below = Math.Max(_below, _glyphs.LineHeight(style) - baseline);
        }

        private void Close()
        {
            Emit([.. _pieces], _above, _above + _below);
            _pieces.Clear();
            _inline.Clear();
            _above = 0;
            _below = 0;
        }

        /// <summary>
        /// Finishes a line and starts the next one below it, gathering
        /// the pictures that fall in it.
        /// </summary>
        /// <remarks>
        /// [glk #graphics_textbuf] A margin picture is put in every line
        /// it reaches down through, at an offset that is negative for
        /// all but the first. Putting it only in the line it began at
        /// would lose it as soon as that line had scrolled off the top,
        /// since painting starts at the bottom of the window and works
        /// upwards.
        /// </remarks>
        private void Emit(IReadOnlyList<Piece> pieces, double baseline, double height)
        {
            var images = new List<Inset>();

            // The pictures the line stands aside for come first, and
            // the ones carried along in its run after them, which is
            // also the order they are painted in.
            foreach (var margin in _margins)
            {
                if (margin.Top < _top + height && margin.Bottom > _top)
                {
                    images.Add(new Inset(
                        margin.Picture,
                        margin.From,
                        margin.Top - _top,
                        margin.To - margin.From,
                        margin.Bottom - margin.Top));
                }
            }

            foreach (var waiting in _inline)
            {
                images.Add(new Inset(
                    waiting.Picture,
                    waiting.Left,
                    baseline - waiting.Above,
                    waiting.Width,
                    waiting.Height));
            }

            _lines.Add(new Line(pieces, images, height, baseline));
            _top += height;
            Open();
        }

        /// <summary>
        /// Starts a line, between whatever margin pictures reach into
        /// it.
        /// </summary>
        private void Open()
        {
            var left = 0.0;
            var right = _width;

            foreach (var margin in _margins)
            {
                if (_top < margin.Top || _top >= margin.Bottom)
                {
                    continue;
                }

                if (margin.Left)
                {
                    left = Math.Max(left, margin.To);
                }
                else
                {
                    right = Math.Min(right, margin.From);
                }
            }

            _start = left;
            _limit = right;
            _left = left;
        }

        /// <summary>
        /// How far down the text would have to go to clear every margin
        /// picture it is beside, or null where it is beside none.
        /// </summary>
        private double? Past()
        {
            var below = _top;

            foreach (var margin in _margins)
            {
                below = Math.Max(below, margin.Bottom);
            }

            return below > _top ? below : null;
        }

        /// <summary>
        /// Breaks a word too long for any line into pieces that fit, one
        /// character at a time so that a proportional font is measured
        /// rather than guessed at.
        /// </summary>
        private IEnumerable<(string Text, double Width)> Broken(string word, GlkStyle style, double room)
        {
            var piece = new StringBuilder();
            var measured = 0.0;

            foreach (var character in word)
            {
                var one = _glyphs.Width(character.ToString(), style);
                if (piece.Length > 0 && measured + one > room)
                {
                    yield return (piece.ToString(), measured);
                    piece.Clear();
                    measured = 0;
                }

                piece.Append(character);
                measured += one;
            }

            if (piece.Length > 0)
            {
                yield return (piece.ToString(), measured);
            }
        }

        /// <summary>
        /// [glk #graphics_textbuf] A picture in one of the margins, and
        /// the band of the window it stands the text out of.
        /// </summary>
        private readonly record struct Margin(Pixels Picture, bool Left, double Top, double Bottom, double From, double To);

        /// <summary>
        /// An inline picture placed in the line being laid out, kept
        /// until the line's baseline is settled and it can be given a
        /// place to sit.
        /// </summary>
        private readonly record struct Waiting(Pixels Picture, double Left, double Above, double Width, double Height);
    }
}
