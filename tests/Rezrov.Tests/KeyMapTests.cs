using Rezrov.Glulx.Glk;
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
    public void PastedTextIsTypedForThePlayer()
    {
        var keys = new KeyMap(UnicodeTranslationTable.Default);

        // [zm 10.7] Each character as its ZSCII code, and a line ending
        // of any of the three shapes as the return key, so a command
        // pasted from somewhere else runs when it arrives.
        Assert.Equal(
            [(ushort)'h', (ushort)'i', 13, (ushort)'y', 13, (ushort)'o', 13],
            keys.ToZscii("hi\ny\r\no\r").ToArray());

        // A character the story has no code for is dropped rather than
        // typed as something else.
        Assert.Equal([(ushort)'a', (ushort)'b'], keys.ToZscii("a\u4e2db").ToArray());
        Assert.Empty(keys.ToZscii(""));
    }

    [Fact]
    public void PastedTextIsTypedForAGlulxGameToo()
    {
        // [glk #encoding_inchar] A character is its own code point, and
        // one beyond the Basic Multilingual Plane is one character, not
        // the two halves it is written with.
        Assert.Equal(
            [(uint)'h', 'i', GlkKeyCode.Return, 0x4E2D, GlkKeyCode.Return, 0x1F600],
            GlkKeyMap.ToGlk("hi\r\n\u4e2d\n\U0001F600").ToArray());

        // A control character is not a key a game can read.
        Assert.Equal([(uint)'a'], GlkKeyMap.ToGlk("a\u0007").ToArray());
    }

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
