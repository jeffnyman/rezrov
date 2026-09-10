using Rezrov.ZMachine;
using Rezrov.ZMachine.Saves;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// The save, restore, and undo opcodes in the interpreter: what they
/// answer, where a restored game resumes, and the auxiliary file forms.
/// </summary>
public partial class InterpreterTests
{
    [Fact]
    public void SaveStoresOneAndRestoreResumesAtTheSaveWithTwo()
    {
        // [zm op:save] and [zm op:restore] in Version 5: the program
        // saves, counts a turn, restores, and the save "returns" 2 into
        // the same variable, so the turn count starts over.
        var files = new ScriptedFiles { SaveFile = new MemoryStream() };
        var run = Execute(
            new Assembler()
                .Ext(0).Store(G0)
                .Short1(Op.Inc, Small(G1))
                .Long(Op.Je, Var(G0), Small(2)).Branch(true, 8)
                .Ext(1).Store(G2)
                .Quit()
                .Quit()
                .Variable(Op.Storeb, true, Large(0x300), Small(0), Var(G0))
                .Quit(),
            files: files);

        // Pass one: save gives 1, G1 becomes 1, no branch, restore. Pass
        // two: memory is back as saved, so G1 is 0 again and becomes 1,
        // G0 is 2, and the branch past the restore records the 2 in
        // memory and quits.
        Assert.Equal(2, run.Global(G0));
        Assert.Equal(1, run.Global(G1));
        Assert.Equal(2, run.Story.Bytes[0x300]);
        Assert.Equal(1, files.SaveRequests);
        Assert.Equal(1, files.RestoreRequests);
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void InVersion3SaveBranchesAndRestoreResumesAtThatBranch()
    {
        // [zm op:save] branches on success, and [quetzal 5.8.1] a restore
        // resumes at that branch data, taking it again. G0 counts how
        // often the branch target ran.
        var files = new ScriptedFiles { SaveFile = new MemoryStream() };
        var run = Execute(
            new Assembler()
                .Short0(Op.Save).Branch(true, 4)
                .Quit()
                .Quit()
                .Variable(Op.Loadb, false, Large(0x10), Small(1)).Store(0)
                .Short1(Op.Jz, Var(0)).Branch(false, 14)
                .Long(Op.Store, Small(G0), Small(1))
                .Variable(Op.Storeb, true, Large(0x10), Small(1), Small(2))
                .Short0(Op.Restore).Branch(true, 3)
                .Quit()
                .Long(Op.Store, Small(G1), Small(1))
                .Quit(),
            version: ZMachineVersion.V3,
            files: files);

        // Memory comes back as saved, so G0 is 0 again, but [zm 6.1.2]
        // Flags 2 keeps the bit set after the save, which is how the
        // second pass knows to quit.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, run.Global(G1));
        Assert.True(run.Interpreter.Header.Flags2.HasFlag(Flags2.ForceFixedPitch));
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void ADeclinedSaveFailsAndADeclinedRestoreReturnsZero()
    {
        // [zm op:save] 0 for failure; [zm op:restore] 0 for failure.
        var run = Execute(
            new Assembler()
                .Ext(0).Store(G0)
                .Ext(1).Store(G1)
                .Quit());

        Assert.Equal(0, run.Global(G0));
        Assert.Equal(0, run.Global(G1));
    }

    [Fact]
    public void InVersion3AFailedSaveOrRestoreNeverBranches()
    {
        // [zm op:restore] "the branch is never actually made".
        var run = Execute(
            new Assembler()
                .Short0(Op.Save).Branch(true, 3)
                .Short0(Op.Restore).Branch(true, 3)
                .Long(Op.Store, Small(G0), Small(1))
                .Quit(),
            version: ZMachineVersion.V3);

        Assert.Equal(1, run.Global(G0));
    }

    [Fact]
    public void AFileFromAnotherStoryIsReportedAndFails()
    {
        var files = new ScriptedFiles { RestoreFile = [70, 79, 82, 77, 0, 0, 0, 4, 73, 70, 90, 83] };
        var run = Execute(
            new Assembler()
                .Ext(1).Store(G0)
                .Quit(),
            files: files);

        // [zm 6.1.2.1] and [zm 7.6.4]
        Assert.Equal(0, run.Global(G0));
        Assert.Contains("restore", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void UndoSavesAndRestoresTheStateOfPlay()
    {
        // [zm op:save_undo] 1 now, [zm op:restore_undo] never returns
        // but resumes at the save_undo with 2. G1 was changed after the
        // save and comes back.
        var run = Execute(
            new Assembler()
                .Ext(9).Store(G0)
                .Long(Op.Je, Var(G0), Small(2)).Branch(true, 11)
                .Long(Op.Store, Small(G1), Small(7))
                .Ext(10).Store(G2)
                .Quit()
                .Quit()
                .Variable(Op.Storeb, true, Large(0x300), Small(0), Var(G1))
                .Quit());

        Assert.Equal(2, run.Global(G0));
        Assert.Equal(0, run.Global(G1));
        Assert.Equal(0, run.Story.Bytes[0x300]);
        Assert.Equal(0, run.Interpreter.Undo.Count);
    }

    [Fact]
    public void UndoGoesBackSeveralTurnsThenRunsOut()
    {
        // Two save_undos, two restore_undos, and a third that has nothing
        // left and returns 0.
        var run = Execute(
            new Assembler()
                .Ext(9).Store(G0)
                .Long(Op.Je, Var(G0), Small(2)).Branch(true, 22)
                .Ext(9).Store(G1)
                .Long(Op.Je, Var(G1), Small(2)).Branch(true, 8)
                .Ext(10).Store(G2)
                .Quit()
                .Quit()
                .Ext(10).Store(G2)
                .Quit()
                .Quit()
                .Ext(10).Store(G2)
                .Quit());

        // Pass one: both saves give 1, the first restore_undo goes to the
        // second save (G1 = 2), whose branch leads to another restore
        // going to the first save (G0 = 2), whose branch leads to a
        // restore_undo with nothing left.
        Assert.Equal(2, run.Global(G0));
        Assert.Equal(0, run.Global(G2));
        Assert.Equal(0, run.Interpreter.Undo.Count);
    }

    [Fact]
    public void SavingInsideAnInterruptRoutineIsRefused()
    {
        // [zm 6.1.1.3]
        var files = new ScriptedFiles { SaveFile = new MemoryStream() };
        var input = new ScriptedInput("look") { InterruptsBeforeAnswering = 1 };
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer), Small(10), Large(RoutineA / 4)).Store(G0)
                .Quit(),
            story =>
            {
                story.Routine(RoutineA, 0, new Assembler()
                    .Ext(9).Store(G1)
                    .Ext(0).Store(G2)
                    .Short0(Op.Rfalse)
                    .ToArray());
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: input,
            files: files);

        Assert.Equal(0, run.Global(G1));
        Assert.Equal(0, run.Global(G2));
        Assert.Equal(0, files.SaveRequests);
        Assert.Equal(2, run.Interpreter.RuntimeErrors.Count);
    }

    [Fact]
    public void RestoreSetsTheInterpretersHeaderFieldsAgain()
    {
        // [zm 6.1.2.2] The screen width the game scribbled on after the
        // save is the interpreter's again after the restore.
        var files = new ScriptedFiles { SaveFile = new MemoryStream() };
        var run = Execute(
            new Assembler()
                .Ext(0).Store(G0)
                .Long(Op.Je, Var(G0), Small(2)).Branch(true, 13)
                .Variable(Op.Storeb, true, Large(0x21), Small(0), Small(9))
                .Ext(1).Store(G1)
                .Quit()
                .Long(Op.Store, Small(G2), Small(1))
                .Quit(),
            screen: new RecordingScreen(50, 12),
            files: files);

        Assert.Equal(2, run.Global(G0));
        Assert.Equal(1, run.Global(G2));
        Assert.Equal(50, run.Story.Bytes[0x21]);
    }

    [Fact]
    public void RestoreKeepsTheTranscriptAndCollapsesTheUpperWindowInVersion3()
    {
        // [zm 6.1.2] The transcript turned on after the save is still on
        // after the restore, since Flags 2 survives, and [zm 8.6.1.3] the
        // upper window split before the save is collapsed.
        var files = new ScriptedFiles { SaveFile = new MemoryStream(), Transcript = new StringWriter() };
        var run = Execute(
            new Assembler()
                .Variable(Op.SplitWindow, true, Small(3))
                .Short0(Op.Save).Branch(true, 4)
                .Quit()
                .Quit()
                .Variable(Op.Loadb, false, Large(0x10), Small(1)).Store(0)
                .Short1(Op.Jz, Var(0)).Branch(false, 8)
                .Variable(Op.OutputStream, true, Small(2))
                .Short0(Op.Restore).Branch(true, 3)
                .Quit()
                .Long(Op.Store, Small(G1), Small(1))
                .Quit(),
            version: ZMachineVersion.V3,
            screen: new RecordingScreen(50, 12),
            files: files);

        Assert.Equal(1, run.Global(G1));
        Assert.True(run.Interpreter.Streams.TranscriptSelected);
        Assert.Equal(0, run.Interpreter.Screen!.UpperWindow.Lines);
    }

    [Fact]
    public void AuxiliaryFilesSaveAndRestoreARegionOfMemory()
    {
        // [zm op:save] table bytes name prompt and [zm op:restore] the
        // same, returning the bytes loaded.
        var files = new ScriptedFiles();
        files.AuxiliaryReadable["NOTES"] = [0xAA, 0xBB, 0xCC];
        var run = Execute(
            new Assembler()
                .Ext(0, Large(0x300), Small(4), Large(0x310), Small(0)).Store(G0)
                .Ext(1, Large(0x320), Small(8), Large(0x310), Small(1)).Store(G1)
                .Quit(),
            story =>
            {
                "abcd"u8.CopyTo(story.Bytes.AsSpan(0x300));
                story.Bytes[0x310] = 9;
                "notes.txt"u8.CopyTo(story.Bytes.AsSpan(0x311));
            },
            files: files);

        Assert.Equal(1, run.Global(G0));
        Assert.Equal("abcd"u8.ToArray(), files.AuxiliaryWritten["NOTES"].ToArray());
        Assert.Equal(3, run.Global(G1));
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0 }, run.Story.Bytes.AsSpan(0x320, 4).ToArray());
        Assert.Equal([false, true], files.AuxiliaryPrompts.ToArray());
    }

    [Fact]
    public void SuggestedNamesAreMadeSafe()
    {
        // [zm 7.6.1.4] and [zm 7.6.1.1]
        Assert.Equal("NOTES", AuxiliaryFileName.Sanitize("notes.txt"));
        Assert.Equal("MYGAME", AuxiliaryFileName.Sanitize("my/ga:me?*"));
        Assert.Equal("NULL", AuxiliaryFileName.Sanitize("..."));
        Assert.Equal("NULL", AuxiliaryFileName.Sanitize(""));
        Assert.Equal("A B", AuxiliaryFileName.Sanitize(" a b "));
    }
}
