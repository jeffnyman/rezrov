using Rezrov.ZMachine.Screen;

namespace Rezrov.Tui;

/// <summary>
/// What the game view paints: a grid of cells the size of the terminal
/// and a cursor, kept by whichever machine is running.
/// </summary>
/// <remarks>
/// The Z-machine's screen buffer and the Glk screen both come down to
/// this, so one view draws either. The view reads the cells under
/// <see cref="Sync"/>, since the interpreter fills them on its own
/// thread, and calls <see cref="Repaint"/> first, for a picture that
/// composes itself on demand.
/// </remarks>
public interface ITerminalPicture
{
    /// <summary>The lock the cells are read and written under.</summary>
    object Sync { get; }

    int Width { get; }

    int Height { get; }

    /// <summary>The cell at a row and column, both from 0.</summary>
    Cell this[int row, int column] { get; }

    /// <summary>
    /// Where the terminal's cursor goes, or null to hide it.
    /// </summary>
    (int Row, int Column)? Cursor { get; }

    /// <summary>
    /// Brings the cells up to date before they are read.
    /// </summary>
    void Repaint();

    /// <summary>The terminal changed size.</summary>
    void Resize(int width, int height);
}
