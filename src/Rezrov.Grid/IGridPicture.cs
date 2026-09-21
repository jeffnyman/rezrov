using Rezrov.ZMachine.Screen;

namespace Rezrov.Grid;

/// <summary>
/// What a frontend paints: a grid of cells the size of the screen
/// and a cursor, kept by whichever machine is running.
/// </summary>
/// <remarks>
/// The Z-machine's screen buffer and the Glk screen both come down to
/// this, so one view draws either. The view reads the cells under
/// <see cref="Sync"/>, since the interpreter fills them on its own
/// thread, and calls <see cref="Repaint"/> first, for a picture that
/// composes itself on demand.
/// </remarks>
public interface IGridPicture
{
    /// <summary>The lock the cells are read and written under.</summary>
    object Sync { get; }

    int Width { get; }

    int Height { get; }

    /// <summary>The cell at a row and column, both from 0.</summary>
    Cell this[int row, int column] { get; }

    /// <summary>
    /// Where the cursor goes, or null to hide it.
    /// </summary>
    (int Row, int Column)? Cursor { get; }

    /// <summary>
    /// Brings the cells up to date before they are read.
    /// </summary>
    void Repaint();

    /// <summary>The screen changed size.</summary>
    void Resize(int width, int height);
}
