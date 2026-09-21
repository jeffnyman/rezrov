using System.Text;
using Rezrov.AaMachine;
using Rezrov.Core.Graphics;

namespace Rezrov.Gui;

/// <summary>
/// A piece of a line, ready to be drawn: some text set one way, at a
/// place along the line.
/// </summary>
/// <param name="Words">The characters.</param>
/// <param name="Look">How they are set.</param>
/// <param name="Link">
/// [aam output] The link they belong to, or zero for none.
/// </param>
/// <param name="Left">How far along the line the piece starts.</param>
/// <param name="Width">How wide it is.</param>
public sealed record AaPiece(string Words, AaLook Look, int Link, double Left, double Width);

/// <summary>
/// [aam output] A picture laid out in a line, at the size it comes out
/// at in the width the text was laid out at.
/// </summary>
/// <param name="Picture">The pixels to draw.</param>
/// <param name="Left">How far along the line it starts.</param>
/// <param name="Top">How far below the top of the line it starts.</param>
/// <param name="Width">How wide to draw it.</param>
/// <param name="Height">How tall to draw it.</param>
/// <param name="Link">The link it belongs to, or zero for none.</param>
public sealed record AaInset(Pixels Picture, double Left, double Top, double Width, double Height, int Link);

/// <summary>
/// One laid out line: the pieces across it, the pictures in it, where
/// it sits down the page, how tall it is, and where its baseline is.
/// </summary>
/// <param name="Pieces">The text across it.</param>
/// <param name="Pictures">The pictures in it.</param>
/// <param name="Top">How far down the page the line begins.</param>
/// <param name="Height">How tall it is.</param>
/// <param name="Baseline">
/// How far below its top the line the text stands on sits.
/// </param>
public sealed record AaLine(
    IReadOnlyList<AaPiece> Pieces,
    IReadOnlyList<AaInset> Pictures,
    double Top,
    double Height,
    double Baseline);

/// <summary>
/// [aam story] A rectangle painted behind the text: the background and
/// the border a style class asked for around a div.
/// </summary>
/// <param name="Left">Its left edge.</param>
/// <param name="Top">Its top edge.</param>
/// <param name="Width">How wide it is, border to border.</param>
/// <param name="Height">How tall.</param>
/// <param name="Background">What to fill it with, or nothing.</param>
/// <param name="Border">How thick a line to draw around it.</param>
/// <param name="BorderColor">What color that line is.</param>
/// <param name="Radius">How far the corners are rounded.</param>
public sealed record AaFrame(
    double Left,
    double Top,
    double Width,
    double Height,
    uint Background,
    double Border,
    uint BorderColor,
    double Radius);

/// <summary>
/// A page of a story laid out: what to paint behind, what to paint on
/// top, and how far down it all reaches.
/// </summary>
/// <param name="Frames">
/// The boxes, outermost first, which is the order to paint them in.
/// </param>
/// <param name="Lines">The lines, in the order they were written.</param>
/// <param name="Height">How far down the page the whole of it goes.</param>
public sealed record AaPage(IReadOnlyList<AaFrame> Frames, IReadOnlyList<AaLine> Lines, double Height);

/// <summary>
/// [aam output] What an Aa-machine story has printed, and the page it
/// comes out as at a given width.
/// </summary>
/// <remarks>
/// The machine's output is a stream of text inside nested divs and
/// spans, each carrying a style class. The spans are settled as the
/// text arrives, since a span only changes how its text is set, and
/// two runs set the same way are one run. The divs are kept as a tree,
/// because a div is a box: it has margins and a width and may have a
/// line drawn around it, and where its edges fall is not known until
/// there is a width to fall inside of.
///
/// So the tree is laid out afresh whenever the width changes, exactly
/// as a browser lays the reference interpreter's own document out
/// again when its window is resized. What comes back is a list of
/// boxes to paint and a list of lines to paint on them, both measured
/// from the top of the page, which is all a painter needs and nothing
/// it has to decide.
/// </remarks>
public sealed class AaText
{
    /// <summary>
    /// [aam output] How tall a line is as a multiple of the size of
    /// its text, which is what the reference interpreter's own style
    /// sheet asks its browser for. A frontend that has to answer how
    /// many lines fit in a window has to divide by the same number
    /// the layout laid them out with.
    /// </summary>
    public const double LineSpacing = 1.35;

    private readonly IAaGlyphs _glyphs;
    private readonly AaSheet _sheet;
    private readonly Group _root;
    private readonly List<Group> _open = [];
    private readonly List<AaLook> _looks = [];
    private readonly List<bool> _shouts = [];

    private AaPage _page = new([], [], 0);
    private double _width = -1;
    private int _link;

    /// <param name="glyphs">The faces to measure with.</param>
    /// <param name="sheet">The story's style sheet, in pixels.</param>
    public AaText(IAaGlyphs glyphs, AaSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(sheet);

        _glyphs = glyphs;
        _sheet = sheet;
        _root = new Group(NoClass, []) { Look = sheet.Plain };
        _open.Add(_root);
        _looks.Add(sheet.Plain);
        _shouts.Add(false);
    }

    /// <summary>
    /// How far back the player has scrolled, in pixels, with nought
    /// meaning the end of the text is in view.
    /// </summary>
    public double Scroll { get; private set; }

    /// <summary>How text is set at this moment.</summary>
    public AaLook Look => _looks[^1];

    /// <summary>
    /// The style class of the div the story is writing inside, which is
    /// how a caller finds out how tall a status area asked to be.
    /// </summary>
    public int Innermost => _open[^1].StyleClass;

    /// <summary>The class that is no class at all.</summary>
    private static int NoClass => -1;

    /// <summary>Adds text, set the way the open classes ask.</summary>
    public void Put(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        var words = _shouts[^1] ? text.ToUpperInvariant() : text;
        var look = Look;
        var children = _open[^1].Children;

        // A run holds as much as it can: the text arrives a character
        // at a time, and a run for each would be thousands of them.
        if (children.Count > 0 && children[^1] is Words last && last.Look == look && last.Link == _link)
        {
            last.Letters.Append(words);
        }
        else
        {
            children.Add(new Words(new StringBuilder(words), look, _link));
        }

        Changed();
    }

    /// <summary>
    /// Takes back the last character written, which is what a player
    /// who is typing and changes their mind wants.
    /// </summary>
    public void Backspace()
    {
        var children = _open[^1].Children;

        if (children.Count > 0 && children[^1] is Words last && last.Letters.Length > 0)
        {
            last.Letters.Length--;
            Changed();
        }
    }

    /// <summary>Ends the line without ending the paragraph.</summary>
    public void Newline() => Add(new Ending());

    /// <summary>
    /// Ends the paragraph, which leaves a gap before the next one.
    /// </summary>
    public void EndParagraph() => Add(new Parting());

    /// <summary>Opens a div, which is a box of its own.</summary>
    public void EnterDiv(int styleClass)
    {
        var look = _sheet.Inside(Look, styleClass, span: false);
        var group = new Group(styleClass, []) { Look = look };

        _open[^1].Children.Add(group);
        _open.Add(group);
        _looks.Add(look);
        _shouts.Add(_shouts[^1] || _sheet.Shouts(styleClass));
        Changed();
    }

    /// <summary>Closes the innermost div.</summary>
    public void LeaveDiv()
    {
        if (_open.Count > 1)
        {
            _open.RemoveAt(_open.Count - 1);
            _looks.RemoveAt(_looks.Count - 1);
            _shouts.RemoveAt(_shouts.Count - 1);
        }

        Changed();
    }

    /// <summary>
    /// Opens a span, which changes only how its text is set and so is
    /// settled here rather than kept for the layout.
    /// </summary>
    public void EnterSpan(int styleClass)
    {
        _looks.Add(_sheet.Inside(Look, styleClass, span: true));
        _shouts.Add(_shouts[^1] || _sheet.Shouts(styleClass));
    }

    /// <summary>Closes the innermost span.</summary>
    public void LeaveSpan()
    {
        if (_looks.Count > _open.Count)
        {
            _looks.RemoveAt(_looks.Count - 1);
            _shouts.RemoveAt(_shouts.Count - 1);
        }
    }

    /// <summary>
    /// [aam output] Marks the text that follows as part of a link, so
    /// that it can be drawn as one and found again when it is clicked.
    /// </summary>
    public void EnterLink(int link) => _link = link;

    /// <summary>Ends the link.</summary>
    public void LeaveLink() => _link = 0;

    /// <summary>
    /// [aam output] Puts a picture in the run of the text, or the words
    /// the story carries in its place where there is no picture to be
    /// had.
    /// </summary>
    public void Draw(Pixels? picture, string alt)
    {
        ArgumentNullException.ThrowIfNull(alt);

        Add(new Shown(picture, alt, Look, _link));
    }

    /// <summary>
    /// [aam opcode] Puts a bar showing how far along something is.
    /// </summary>
    public void ProgressBar(int amount, int total) => Add(new Bar(amount, total));

    /// <summary>
    /// [aam opcode] Sets how text outside every class is to be set,
    /// which a story asks for to change the look of the whole page.
    /// </summary>
    /// <remarks>
    /// It reaches the text that follows rather than the text already
    /// written, since what is written has already been settled. The
    /// Dialog manual says as much: whether a body style reaches
    /// existing text is the interpreter's own business, and a story
    /// that wants to be sure clears the screen after asking.
    /// </remarks>
    public void SetBody(AaLook plain)
    {
        _root.Look = plain;
        _looks[0] = plain;
        Changed();
    }

    /// <summary>
    /// [aam opcode] Throws away what is in the innermost div, leaving
    /// the div itself and everything outside it.
    /// </summary>
    public void ClearDiv()
    {
        _open[^1].Children.Clear();
        Changed();
    }

    /// <summary>Throws all of it away, but keeps the open divs.</summary>
    public void Clear()
    {
        foreach (var group in _open)
        {
            group.Children.Clear();
        }

        // The open divs were emptied along with everything else, so
        // they have to be put back inside one another.
        for (var i = 1; i < _open.Count; i++)
        {
            _open[i - 1].Children.Add(_open[i]);
        }

        Scroll = 0;
        Changed();
    }

    /// <summary>Closes every div at once, after a runtime error.</summary>
    public void LeaveAll()
    {
        while (_open.Count > 1)
        {
            LeaveDiv();
        }

        while (_looks.Count > 1)
        {
            LeaveSpan();
        }

        _link = 0;
    }

    /// <summary>
    /// The page the text comes out as at the given width, worked out
    /// again only when the width or the text has changed.
    /// </summary>
    public AaPage Lay(double width)
    {
        if (Math.Abs(width - _width) < 0.01)
        {
            return _page;
        }

        _width = width;
        _page = new Layout(_glyphs, _sheet, width).Of(_root);
        return _page;
    }

    /// <summary>How tall all of it is at the given width.</summary>
    public double Height(double width) => Lay(width).Height;

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
    /// Where the top of the page sits in the window: the text is shown
    /// from the bottom up, so a page shorter than the window starts at
    /// the top and a longer one hangs off it.
    /// </summary>
    public double Offset(double width, double height) =>
        Math.Min(height - Height(width) + Scroll, 0);

    /// <summary>
    /// [aam output] The link at a point in the window, or zero where
    /// there is none there.
    /// </summary>
    public int LinkAt(double x, double y, double width, double height)
    {
        var offset = Offset(width, height);

        foreach (var line in Lay(width).Lines)
        {
            var top = line.Top + offset;

            if (y < top || y >= top + line.Height)
            {
                continue;
            }

            foreach (var piece in line.Pieces)
            {
                if (piece.Link != 0 && x >= piece.Left && x < piece.Left + piece.Width)
                {
                    return piece.Link;
                }
            }

            foreach (var picture in line.Pictures)
            {
                if (picture.Link != 0
                    && x >= picture.Left && x < picture.Left + picture.Width
                    && y >= top + picture.Top && y < top + picture.Top + picture.Height)
                {
                    return picture.Link;
                }
            }
        }

        return 0;
    }

    private void Add(Node node)
    {
        _open[^1].Children.Add(node);
        Changed();
    }

    private void Changed()
    {
        _width = -1;
        Scroll = 0;
    }

    /// <summary>One thing the story printed.</summary>
    private abstract record Node;

    /// <summary>A run of text all set the same way.</summary>
    private sealed record Words(StringBuilder Letters, AaLook Look, int Link) : Node;

    /// <summary>A picture, or the words that stand in for one.</summary>
    private sealed record Shown(Pixels? Picture, string Alt, AaLook Look, int Link) : Node;

    /// <summary>[aam opcode] A bar showing how far along something is.</summary>
    private sealed record Bar(int Amount, int Total) : Node;

    /// <summary>The end of a line.</summary>
    private sealed record Ending : Node;

    /// <summary>The end of a paragraph.</summary>
    private sealed record Parting : Node;

    /// <summary>A div: a box with things inside it.</summary>
    /// <remarks>
    /// How its text is set is settled when it is opened rather than
    /// when it is laid out, since nothing about it depends on the
    /// width. It can still be set again afterwards, which is what
    /// [aam opcode] a story does when it changes the style of the
    /// whole body of the document.
    /// </remarks>
    private sealed record Group(int StyleClass, List<Node> Children) : Node
    {
        public AaLook Look { get; set; }
    }

    /// <summary>
    /// The layout itself: a pen moving along a line, a line box growing
    /// around whatever is put on it, boxes opening and closing around
    /// the lines, and the blocks set aside at the edges that the lines
    /// have to make room for.
    /// </summary>
    /// <remarks>
    /// A line box is kept as how far it reaches above the line the text
    /// stands on and how far below, rather than as a height, which is
    /// what lets a heading and the prose beside it stand on the same
    /// line instead of hanging from the same top.
    ///
    /// Vertical space is kept as a debt rather than paid at once: a
    /// margin below one box and a margin above the next are not two
    /// gaps but one as wide as the wider of them, which is what CSS
    /// does and what a reader expects. The debt is paid when a line is
    /// actually begun, so a box that turns out to hold nothing leaves
    /// no gap behind.
    /// </remarks>
    private sealed class Layout
    {
        private readonly List<AaLine> _lines = [];
        private readonly List<AaFrame> _frames = [];
        private readonly List<AaPiece> _pieces = [];
        private readonly List<Waiting> _inline = [];
        private readonly List<Band> _bands = [];
        private readonly IAaGlyphs _glyphs;
        private readonly AaSheet _sheet;
        private readonly double _width;

        private double _top;
        private double _left;
        private double _right;
        private double _pen;
        private double _start;
        private double _limit;
        private double _above;
        private double _below;
        private double _owed;
        private AaAlignment _align;
        private bool _begun;
        private bool _inParagraph;
        private bool _printed;

        public Layout(IAaGlyphs glyphs, AaSheet sheet, double width, AaAlignment align = AaAlignment.Start)
        {
            _glyphs = glyphs;
            _sheet = sheet;
            _width = Math.Max(width, 0);
            _right = _width;
            _align = align;
        }

        /// <summary>How far down the page the layout has reached.</summary>
        private double Reach => _top;

        /// <summary>The whole of a tree, laid out.</summary>
        public AaPage Of(Group root)
        {
            Walk(root);
            Close();

            // A block set aside at an edge may reach below the last
            // line beside it, and that room is part of how tall the
            // page is, or it would hang off the bottom of the window.
            foreach (var band in _bands)
            {
                _top = Math.Max(_top, band.Bottom);
            }

            return new AaPage(_frames, _lines, _top);
        }

        /// <summary>
        /// The widest line a tree comes out as at this width, which is
        /// what a block set aside at an edge is shrunk to when it was
        /// not told how wide to be. A block of prose comes out very
        /// near the width it was offered, and a word or two comes out
        /// as wide as the words, which is the distinction that
        /// matters.
        /// </summary>
        public double Widest(Group group)
        {
            Walk(group);
            Close();

            var widest = 0.0;

            foreach (var line in _lines)
            {
                foreach (var piece in line.Pieces)
                {
                    widest = Math.Max(widest, piece.Left + piece.Width);
                }

                foreach (var picture in line.Pictures)
                {
                    widest = Math.Max(widest, picture.Left + picture.Width);
                }
            }

            return widest;
        }

        private void Walk(Group group)
        {
            foreach (var node in group.Children)
            {
                switch (node)
                {
                    case Words words:
                        Write(words);
                        break;
                    case Shown shown:
                        Show(shown);
                        break;
                    case Bar bar:
                        Gauge(bar);
                        break;
                    case Ending:
                        // [aam output] A line break inside a paragraph
                        // ends the line. Outside one it does nothing,
                        // since there is no line for it to end.
                        if (_inParagraph)
                        {
                            Begin(_sheet.Plain);
                            Close();
                        }

                        break;
                    case Parting:
                        Close();
                        _inParagraph = false;
                        break;
                    case Group inner:
                        Block(inner);
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>Lays out one div as a box of its own.</summary>
        private void Block(Group group)
        {
            var box = _sheet.Box(group.StyleClass, group.Look, _right - _left, _align);

            if (box.Hidden)
            {
                return;
            }

            Close();
            _inParagraph = false;
            _printed = false;

            if (box.Floats != AaFloat.None)
            {
                Aside(group, box);
                return;
            }

            if (box.Clears)
            {
                foreach (var band in _bands)
                {
                    _top = Math.Max(_top, band.Bottom);
                }
            }

            var outerLeft = _left;
            var outerRight = _right;
            var outerAlign = _align;

            _owed = Math.Max(_owed, box.Margin.Top);

            // A line drawn around a box, or a strip of padding inside
            // it, stands between the margin above it and whatever is
            // within, so the two cannot run together.
            if (!box.Collapses)
            {
                Pay();
            }

            var frameTop = _top;
            var room = Math.Max(outerRight - outerLeft, 0);
            var outside = box.Margin.Across + (box.Border * 2) + box.Padding.Across;
            var content = Math.Max(box.Width ?? (room - outside), 0);
            var slack = Math.Max(room - outside - content, 0);
            var boxLeft = outerLeft + box.Margin.Left + (box.Centered ? slack / 2 : 0);

            _left = boxLeft + box.Border + box.Padding.Left;
            _right = _left + content;
            _align = box.Alignment;
            _top += box.Border + box.Padding.Top;

            Walk(group);

            Close();
            _inParagraph = false;
            _printed = false;
            _top += box.Padding.Bottom + box.Border;

            if (_top - frameTop < box.Height)
            {
                _top = frameTop + box.Height;
            }

            if (box.IsDrawn)
            {
                _frames.Add(new AaFrame(
                    boxLeft,
                    frameTop,
                    (box.Border * 2) + box.Padding.Across + content,
                    _top - frameTop,
                    box.Background,
                    box.Border,
                    box.BorderColor,
                    box.Radius));
            }

            _left = outerLeft;
            _right = outerRight;
            _align = outerAlign;
            _owed = Math.Max(_owed, box.Margin.Bottom);
        }

        /// <summary>
        /// [aam story] Sets a block aside at one edge, with the lines
        /// that follow running beside it rather than through it.
        /// </summary>
        /// <remarks>
        /// A block set aside is a page of its own: it is laid out at
        /// its own width, knowing nothing of the text it will sit
        /// beside, and what comes back is moved into place. A block
        /// that was not told how wide to be is made as wide as its
        /// widest line, which is what CSS shrinks one to, and no wider
        /// than the room it has.
        /// </remarks>
        private void Aside(Group group, AaBox box)
        {
            var room = Math.Max(_right - _left, 0);
            var outside = box.Margin.Across + (box.Border * 2) + box.Padding.Across;
            var content = box.Width
                ?? Math.Min(
                    new Layout(_glyphs, _sheet, Math.Max(room - outside, 0)).Widest(group),
                    Math.Max(room - outside, 0));

            var inner = new Layout(_glyphs, _sheet, content, box.Alignment);

            inner.Of(group);

            var across = content + outside;
            var down = inner.Reach + box.Margin.Down + (box.Border * 2) + box.Padding.Down;

            // It goes where the next line would have gone, which is
            // below whatever vertical space is still owed.
            var top = _top + _owed;
            var from = box.Floats == AaFloat.Left ? _left : Math.Max(_right - across, _left);

            _bands.Add(new Band(box.Floats == AaFloat.Left, top, top + down, from, from + across));

            var contentLeft = from + box.Margin.Left + box.Border + box.Padding.Left;
            var contentTop = top + box.Margin.Top + box.Border + box.Padding.Top;

            if (box.IsDrawn)
            {
                _frames.Add(new AaFrame(
                    from + box.Margin.Left,
                    top + box.Margin.Top,
                    (box.Border * 2) + box.Padding.Across + content,
                    down - box.Margin.Down,
                    box.Background,
                    box.Border,
                    box.BorderColor,
                    box.Radius));
            }

            foreach (var frame in inner._frames)
            {
                _frames.Add(frame with { Left = frame.Left + contentLeft, Top = frame.Top + contentTop });
            }

            foreach (var line in inner._lines)
            {
                _lines.Add(line with
                {
                    Top = line.Top + contentTop,
                    Pieces = [.. line.Pieces.Select(p => p with { Left = p.Left + contentLeft })],
                    Pictures = [.. line.Pictures.Select(p => p with { Left = p.Left + contentLeft })],
                });
            }
        }

        /// <summary>How much room the line being built has left.</summary>
        private double Room => Math.Max(_limit - _pen, 0);

        /// <summary>Lays out a run of text.</summary>
        private void Write(Words words)
        {
            foreach (var part in Split(words.Letters.ToString()))
            {
                Begin(words.Look);

                var measured = Measure(part, words.Look);

                // A word that does not fit goes on the next line,
                // unless the line is empty, in which case it is broken
                // where it reaches the edge: something has to give,
                // and dropping characters is worse.
                if (_pen + measured > _limit && _pen > _start && part != " ")
                {
                    Close();
                    Begin(words.Look);
                }

                if (measured > Room && Room > 0)
                {
                    foreach (var (text, size) in Broken(part, words.Look, Room))
                    {
                        if (_pen + size > _limit && _pen > _start)
                        {
                            Close();
                            Begin(words.Look);
                        }

                        Place(text, words, size);
                    }

                    continue;
                }

                Place(part, words, measured);
            }
        }

        /// <summary>
        /// [aam output] Puts a picture in the run of the text, standing
        /// on the line the text stands on, or the words the story
        /// carries in its place where there is no picture.
        /// </summary>
        /// <remarks>
        /// A picture wider than the box it is in is brought down to fit
        /// rather than allowed to run off the edge, which is what the
        /// reference interpreter's own style sheet asks of the browser.
        /// </remarks>
        private void Show(Shown shown)
        {
            if (shown.Picture is not { Width: > 0, Height: > 0 } picture)
            {
                Write(new Words(new StringBuilder($"[{shown.Alt}]"), shown.Look, shown.Link));
                return;
            }

            Begin(shown.Look);

            var room = Math.Max(_right - _left, 1);
            var across = (double)picture.Width;
            var down = (double)picture.Height;

            if (across > room)
            {
                down = down * room / across;
                across = room;
            }

            if (_pen + across > _limit && _pen > _start)
            {
                Close();
                Begin(shown.Look);
            }

            _inline.Add(new Waiting(picture, shown.Link, _pen, down, across, down));
            _pen += across;
            _above = Math.Max(_above, down);
            Grow(shown.Look);
            _printed = true;
        }

        /// <summary>
        /// [aam opcode] A bar showing how far along something is: a
        /// filled part inside a line drawn around the whole width, with
        /// a lighter stripe along the top of the fill.
        /// </summary>
        private void Gauge(Bar bar)
        {
            Close();

            var look = _sheet.Plain;
            var edge = Math.Max(Math.Round(look.Size / 10), 1);
            var thick = Math.Max(Math.Round(look.Size * 0.31), 2);
            var stripe = Math.Max(Math.Round(look.Size * 0.19), 1);

            _owed = Math.Max(_owed, look.Size * 0.2);
            Pay();

            var width = Math.Max(_right - _left, 0);
            var filled = bar.Total == 0
                ? 0
                : Math.Clamp(width - (edge * 2), 0, width) * Math.Clamp((double)bar.Amount / bar.Total, 0, 1);

            _frames.Add(new AaFrame(
                _left, _top, width, thick + (edge * 2), 0, edge, AaTheme.BarEdge, look.Size * 0.3));

            _frames.Add(new AaFrame(
                _left + edge, _top + edge, filled, thick, AaTheme.Bar, 0, 0, 0));

            _frames.Add(new AaFrame(
                _left + edge, _top + edge, filled, stripe, AaTheme.BarLit, 0, 0, 0));

            _top += thick + (edge * 2);
            _printed = true;
        }

        /// <summary>
        /// Starts a line if one is not already started: pays whatever
        /// vertical space is owed, opens a paragraph if none is open,
        /// and finds where the line may run between the blocks set
        /// aside at the edges.
        /// </summary>
        private void Begin(AaLook look)
        {
            if (!_inParagraph)
            {
                // [aam output] A paragraph that follows text is set off
                // from it by a line's worth of space. One that follows
                // the edge of a box is not, since the box has margins
                // of its own to do that with.
                if (_printed)
                {
                    _owed = Math.Max(_owed, look.Size);
                }

                _inParagraph = true;
                _printed = false;
            }

            if (_begun)
            {
                return;
            }

            Pay();
            Open();
            _begun = true;
        }

        /// <summary>Pays whatever vertical space is owed.</summary>
        private void Pay()
        {
            _top += _owed;
            _owed = 0;
        }

        /// <summary>
        /// Finds where a line may begin and end, between the blocks set
        /// aside at the edges that reach into it.
        /// </summary>
        private void Open()
        {
            var left = _left;
            var right = _right;

            foreach (var band in _bands)
            {
                if (_top < band.Top || _top >= band.Bottom)
                {
                    continue;
                }

                if (band.Left)
                {
                    left = Math.Max(left, band.To);
                }
                else
                {
                    right = Math.Min(right, band.From);
                }
            }

            _start = Math.Min(left, right);
            _limit = right;
            _pen = _start;
        }

        private void Place(string text, Words words, double measured)
        {
            _pieces.Add(new AaPiece(text, words.Look, words.Link, _pen, measured));
            _pen += measured;
            Grow(words.Look);
            _printed = true;
        }

        /// <summary>
        /// Makes the line at least as tall as a line of the given look,
        /// above and below the line the text stands on both.
        /// </summary>
        /// <remarks>
        /// The room a line takes is a multiple of the size of its text
        /// rather than the height of the face, and what is left over
        /// after the face has had its share is halved between the top
        /// and the bottom, which is how CSS sets a line.
        /// </remarks>
        private void Grow(AaLook look)
        {
            var ascent = _glyphs.Ascent(look);
            var descent = _glyphs.Descent(look);
            var leading = Math.Max(((look.Size * LineSpacing) - ascent - descent) / 2, 0);

            _above = Math.Max(_above, ascent + leading);
            _below = Math.Max(_below, descent + leading);
        }

        /// <summary>
        /// Finishes the line being built, if there is one, and moves
        /// down past it.
        /// </summary>
        private void Close()
        {
            if (!_begun)
            {
                return;
            }

            if (_above + _below <= 0)
            {
                Grow(_sheet.Plain);
            }

            var shift = Shift();

            var pieces = shift > 0
                ? _pieces.Select(piece => piece with { Left = piece.Left + shift }).ToList()
                : [.. _pieces];

            var pictures = new List<AaInset>();

            foreach (var waiting in _inline)
            {
                pictures.Add(new AaInset(
                    waiting.Picture,
                    waiting.Left + shift,
                    _above - waiting.Above,
                    waiting.Width,
                    waiting.Height,
                    waiting.Link));
            }

            _lines.Add(new AaLine(pieces, pictures, _top, _above + _below, _above));
            _top += _above + _below;

            _pieces.Clear();
            _inline.Clear();
            _above = 0;
            _below = 0;
            _begun = false;
        }

        /// <summary>
        /// How far the finished pieces of a line move along to sit the
        /// way the box asked.
        /// </summary>
        /// <remarks>
        /// Setting a line against both edges means stretching its
        /// spaces, which is not done here, so a box that asks for it is
        /// set against the left instead and the story is told as much
        /// rather than being told one thing and shown another.
        ///
        /// Trailing spaces are not part of what is moved, or a line
        /// that wrapped after a space would sit a space too far over.
        /// </remarks>
        private double Shift()
        {
            if (_align == AaAlignment.Start)
            {
                return 0;
            }

            var reach = 0.0;

            foreach (var piece in _pieces)
            {
                if (piece.Words != " ")
                {
                    reach = Math.Max(reach, piece.Left + piece.Width);
                }
            }

            foreach (var waiting in _inline)
            {
                reach = Math.Max(reach, waiting.Left + waiting.Width);
            }

            if (reach <= 0)
            {
                return 0;
            }

            var slack = Math.Max(_limit - reach, 0);

            return _align == AaAlignment.Center ? slack / 2 : slack;
        }

        /// <summary>
        /// How wide a piece of text comes out, with the room the class
        /// asked to be left after each of its letters.
        /// </summary>
        private double Measure(string text, AaLook look) =>
            _glyphs.Width(text, look) + (look.Spacing * text.Length);

        /// <summary>
        /// The text as words and the spaces between them, which the
        /// wrapper treats differently.
        /// </summary>
        private static IEnumerable<string> Split(string text)
        {
            var word = new StringBuilder();

            foreach (var character in text)
            {
                if (character == ' ')
                {
                    if (word.Length > 0)
                    {
                        yield return word.ToString();
                        word.Clear();
                    }

                    yield return " ";
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
        /// Breaks a word too long for any line into pieces that fit,
        /// one character at a time so that a proportional face is
        /// measured rather than guessed at.
        /// </summary>
        private IEnumerable<(string Text, double Width)> Broken(string word, AaLook look, double room)
        {
            var piece = new StringBuilder();
            var measured = 0.0;

            foreach (var character in word)
            {
                var one = Measure(character.ToString(), look);

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
        /// A block set aside at an edge, and the band of the page it
        /// stands the lines out of.
        /// </summary>
        private readonly record struct Band(bool Left, double Top, double Bottom, double From, double To);

        /// <summary>
        /// A picture placed in the line being built, kept until the
        /// line the text stands on is settled and it can be given a
        /// place to sit.
        /// </summary>
        private readonly record struct Waiting(
            Pixels Picture, int Link, double Left, double Above, double Width, double Height);
    }
}
