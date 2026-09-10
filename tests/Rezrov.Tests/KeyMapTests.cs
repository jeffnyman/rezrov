using Rezrov.Tui;
using Rezrov.ZMachine.Text;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Rezrov.Tests;

/// <summary>
/// Terminal keys to ZSCII input codes.
/// </summary>
public class KeyMapTests
{
    private static readonly KeyMap Map = new(UnicodeTranslationTable.Default);

    [Fact]
    public void SpecialKeysHaveTheirZsciiCodes()
    {
        // [zm 3.8.2] and [zm 3.8.4]
        Assert.Equal(Zscii.Newline, Map.ToZscii(Key.Enter));
        Assert.Equal(Zscii.Delete, Map.ToZscii(Key.Backspace));
        Assert.Equal(Zscii.Escape, Map.ToZscii(Key.Esc));
        Assert.Equal(Zscii.CursorUp, Map.ToZscii(Key.CursorUp));
        Assert.Equal(Zscii.CursorRight, Map.ToZscii(Key.CursorRight));
        Assert.Equal(Zscii.F1, Map.ToZscii(new Key(KeyCode.F1)));
        Assert.Equal(Zscii.F12, Map.ToZscii(new Key(KeyCode.F12)));
    }

    [Fact]
    public void PrintableKeysGoThroughTheTranslationTable()
    {
        // [zm 3.8.3] ASCII as itself; [zm 3.8.5] an accent by the table.
        Assert.Equal((ushort)'a', Map.ToZscii(new Key('a')));
        Assert.Equal((ushort)'A', Map.ToZscii(Key.A.WithShift));
        Assert.Equal((ushort)' ', Map.ToZscii(Key.Space));
        Assert.Equal((ushort)170, Map.ToZscii(new Key('é')));
    }

    [Fact]
    public void ModifiedKeysAndTheRestAreNotInput()
    {
        Assert.Null(Map.ToZscii(Key.Q.WithCtrl));
        Assert.Null(Map.ToZscii(Key.A.WithAlt));
        Assert.Null(Map.ToZscii(Key.Tab));
        Assert.Null(Map.ToZscii(new Key('€')));
    }
}
