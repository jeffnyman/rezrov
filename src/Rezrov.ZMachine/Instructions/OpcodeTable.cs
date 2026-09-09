namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// Section 14's table of opcodes as code: which opcode an operand count
/// and number name in a given version, and what follows its operands.
/// </summary>
/// <remarks>
/// [zm 14] Every row of the table is here, including the version column.
/// [zm 14.2] An opcode that does not exist in the story's version is
/// illegal, and resolves to null so the decoder can refuse it; the
/// interpreter is meant to halt with a message rather than guess.
///
/// Where the table gives a version as "5/3", the first number is the
/// version whose specification the opcode belongs to and the second the
/// earliest version any Infocom game used it in. sound_effect is the one
/// case that matters: a Version 5 opcode that The Lurking Horror, a
/// Version 3 game, uses, so it is accepted from Version 3.
/// </remarks>
public static class OpcodeTable
{
    /// <summary>
    /// The two VAR opcodes that carry a second byte of operand types.
    /// </summary>
    /// <remarks>
    /// [zm 4.4.3.1] call_vs2 and call_vn2, numbers 12 and 26, are the
    /// only instructions that can have more than four operands.
    /// </remarks>
    public static bool HasSecondTypesByte(OperandCount count, int number) =>
        count == OperandCount.Var && number is 12 or 26;

    /// <summary>
    /// Finds the opcode for an operand count and number, or null if there
    /// is no such opcode in this version.
    /// </summary>
    public static OpcodeInfo? Resolve(InstructionForm form, OperandCount count, int number, ZMachineVersion version)
    {
        if (form == InstructionForm.ExtendedForm)
        {
            return Extended(number, version);
        }

        return count switch
        {
            OperandCount.TwoOp => TwoOp(number, version),
            OperandCount.OneOp => OneOp(number, version),
            OperandCount.ZeroOp => ZeroOp(number, version),
            OperandCount.Var => Var(number, version),
            _ => null,
        };
    }

    private static OpcodeInfo? TwoOp(int number, ZMachineVersion v) => number switch
    {
        // [zm 14] The remarks say 2OP:0 was possibly meant for breakpoints
        // and was never nop, so it stays illegal.
        1 => new(Opcode.Je, "je", Branch: true),
        2 => new(Opcode.Jl, "jl", Branch: true),
        3 => new(Opcode.Jg, "jg", Branch: true),
        4 => new(Opcode.DecChk, "dec_chk", Branch: true),
        5 => new(Opcode.IncChk, "inc_chk", Branch: true),
        6 => new(Opcode.Jin, "jin", Branch: true),
        7 => new(Opcode.Test, "test", Branch: true),
        8 => new(Opcode.Or, "or", Store: true),
        9 => new(Opcode.And, "and", Store: true),
        10 => new(Opcode.TestAttr, "test_attr", Branch: true),
        11 => new(Opcode.SetAttr, "set_attr"),
        12 => new(Opcode.ClearAttr, "clear_attr"),
        13 => new(Opcode.Store, "store"),
        14 => new(Opcode.InsertObj, "insert_obj"),
        15 => new(Opcode.Loadw, "loadw", Store: true),
        16 => new(Opcode.Loadb, "loadb", Store: true),
        17 => new(Opcode.GetProp, "get_prop", Store: true),
        18 => new(Opcode.GetPropAddr, "get_prop_addr", Store: true),
        19 => new(Opcode.GetNextProp, "get_next_prop", Store: true),
        20 => new(Opcode.Add, "add", Store: true),
        21 => new(Opcode.Sub, "sub", Store: true),
        22 => new(Opcode.Mul, "mul", Store: true),
        23 => new(Opcode.Div, "div", Store: true),
        24 => new(Opcode.Mod, "mod", Store: true),
        25 => From(v, ZMachineVersion.V4, new(Opcode.Call2s, "call_2s", Store: true)),
        26 => From(v, ZMachineVersion.V5, new(Opcode.Call2n, "call_2n")),
        27 => From(v, ZMachineVersion.V5, new(Opcode.SetColour, "set_colour")),
        28 => From(v, ZMachineVersion.V5, new(Opcode.Throw, "throw")),
        _ => null,
    };

    private static OpcodeInfo? OneOp(int number, ZMachineVersion v) => number switch
    {
        0 => new(Opcode.Jz, "jz", Branch: true),
        1 => new(Opcode.GetSibling, "get_sibling", Store: true, Branch: true),
        2 => new(Opcode.GetChild, "get_child", Store: true, Branch: true),
        3 => new(Opcode.GetParent, "get_parent", Store: true),
        4 => new(Opcode.GetPropLen, "get_prop_len", Store: true),
        5 => new(Opcode.Inc, "inc"),
        6 => new(Opcode.Dec, "dec"),
        7 => new(Opcode.PrintAddr, "print_addr"),
        8 => From(v, ZMachineVersion.V4, new(Opcode.Call1s, "call_1s", Store: true)),
        9 => new(Opcode.RemoveObj, "remove_obj"),
        10 => new(Opcode.PrintObj, "print_obj"),
        11 => new(Opcode.Ret, "ret"),

        // [zm op:jump] Not a branch instruction: its operand is a signed
        // offset, so there is no branch data to read.
        12 => new(Opcode.Jump, "jump"),
        13 => new(Opcode.PrintPaddr, "print_paddr"),
        14 => new(Opcode.Load, "load", Store: true),

        // [zm 14] 1OP:15 is not, which stores, through Version 4, and
        // call_1n, which does not, from Version 5.
        15 => v <= ZMachineVersion.V4
            ? new(Opcode.Not, "not", Store: true)
            : new(Opcode.Call1n, "call_1n"),
        _ => null,
    };

    private static OpcodeInfo? ZeroOp(int number, ZMachineVersion v) => number switch
    {
        0 => new(Opcode.Rtrue, "rtrue"),
        1 => new(Opcode.Rfalse, "rfalse"),
        2 => new(Opcode.Print, "print", Text: true),
        3 => new(Opcode.PrintRet, "print_ret", Text: true),
        4 => new(Opcode.Nop, "nop"),

        // [zm 14] save and restore branch through Version 3, store in
        // Version 4, and are illegal from Version 5, where EXT:0 and
        // EXT:1 replace them.
        5 => v switch
        {
            <= ZMachineVersion.V3 => new(Opcode.Save, "save", Branch: true),
            ZMachineVersion.V4 => new(Opcode.Save, "save", Store: true),
            _ => null,
        },
        6 => v switch
        {
            <= ZMachineVersion.V3 => new(Opcode.Restore, "restore", Branch: true),
            ZMachineVersion.V4 => new(Opcode.Restore, "restore", Store: true),
            _ => null,
        },
        7 => new(Opcode.Restart, "restart"),
        8 => new(Opcode.RetPopped, "ret_popped"),

        // [zm 14] 0OP:9 is pop through Version 4 and catch, which
        // stores, from Version 5.
        9 => v <= ZMachineVersion.V4
            ? new(Opcode.Pop, "pop")
            : new(Opcode.Catch, "catch", Store: true),
        10 => new(Opcode.Quit, "quit"),
        11 => new(Opcode.NewLine, "new_line"),

        // [zm 14] show_status exists in Version 3 only.
        // [zm op:show_status] Version 3 only in theory, but Version 5
        // Release 23 of Wishbringer contains it by accident and the
        // standard asks for a nop there, so it decodes from Version 3 on.
        12 => From(v, ZMachineVersion.V3, new(Opcode.ShowStatus, "show_status")),
        13 => From(v, ZMachineVersion.V3, new(Opcode.Verify, "verify", Branch: true)),

        // [zm 14] 0OP:14 is the first byte of an extended opcode from
        // Version 5, which the decoder handles by form before it gets
        // here, and nothing at all before that.
        15 => From(v, ZMachineVersion.V5, new(Opcode.Piracy, "piracy", Branch: true)),
        _ => null,
    };

    private static OpcodeInfo? Var(int number, ZMachineVersion v) => number switch
    {
        // [zm 14] The same opcode, named call through Version 3 and
        // call_vs from Version 4.
        0 => v <= ZMachineVersion.V3
            ? new(Opcode.Call, "call", Store: true)
            : new(Opcode.CallVs, "call_vs", Store: true),
        1 => new(Opcode.Storew, "storew"),
        2 => new(Opcode.Storeb, "storeb"),
        3 => new(Opcode.PutProp, "put_prop"),

        // [zm 14] sread through Version 4, and aread, which stores the
        // terminating character, from Version 5.
        4 => v <= ZMachineVersion.V4
            ? new(Opcode.Sread, "sread")
            : new(Opcode.Aread, "aread", Store: true),
        5 => new(Opcode.PrintChar, "print_char"),
        6 => new(Opcode.PrintNum, "print_num"),
        7 => new(Opcode.Random, "random", Store: true),
        8 => new(Opcode.Push, "push"),

        // [zm 14] pull names its variable as an operand through Version
        // 5, and stores in Version 6.
        9 => v == ZMachineVersion.V6
            ? new(Opcode.Pull, "pull", Store: true)
            : new(Opcode.Pull, "pull"),
        10 => From(v, ZMachineVersion.V3, new(Opcode.SplitWindow, "split_window")),
        11 => From(v, ZMachineVersion.V3, new(Opcode.SetWindow, "set_window")),
        12 => From(v, ZMachineVersion.V4, new(Opcode.CallVs2, "call_vs2", Store: true)),
        13 => From(v, ZMachineVersion.V4, new(Opcode.EraseWindow, "erase_window")),
        14 => From(v, ZMachineVersion.V4, new(Opcode.EraseLine, "erase_line")),
        15 => From(v, ZMachineVersion.V4, new(Opcode.SetCursor, "set_cursor")),
        16 => From(v, ZMachineVersion.V4, new(Opcode.GetCursor, "get_cursor")),
        17 => From(v, ZMachineVersion.V4, new(Opcode.SetTextStyle, "set_text_style")),
        18 => From(v, ZMachineVersion.V4, new(Opcode.BufferMode, "buffer_mode")),
        19 => From(v, ZMachineVersion.V3, new(Opcode.OutputStream, "output_stream")),
        20 => From(v, ZMachineVersion.V3, new(Opcode.InputStream, "input_stream")),
        21 => From(v, ZMachineVersion.V3, new(Opcode.SoundEffect, "sound_effect")),
        22 => From(v, ZMachineVersion.V4, new(Opcode.ReadChar, "read_char", Store: true)),
        23 => From(v, ZMachineVersion.V4, new(Opcode.ScanTable, "scan_table", Store: true, Branch: true)),
        24 => From(v, ZMachineVersion.V5, new(Opcode.Not, "not", Store: true)),
        25 => From(v, ZMachineVersion.V5, new(Opcode.CallVn, "call_vn")),
        26 => From(v, ZMachineVersion.V5, new(Opcode.CallVn2, "call_vn2")),
        27 => From(v, ZMachineVersion.V5, new(Opcode.Tokenise, "tokenise")),
        28 => From(v, ZMachineVersion.V5, new(Opcode.EncodeText, "encode_text")),
        29 => From(v, ZMachineVersion.V5, new(Opcode.CopyTable, "copy_table")),
        30 => From(v, ZMachineVersion.V5, new(Opcode.PrintTable, "print_table")),
        31 => From(v, ZMachineVersion.V5, new(Opcode.CheckArgCount, "check_arg_count", Branch: true)),
        _ => null,
    };

    // Extended opcodes exist only from Version 5, which the decoder has
    // already established by the time the form is extended.
    private static OpcodeInfo? Extended(int number, ZMachineVersion v) => number switch
    {
        0 => new(Opcode.Save, "save", Store: true),
        1 => new(Opcode.Restore, "restore", Store: true),
        2 => new(Opcode.LogShift, "log_shift", Store: true),
        3 => new(Opcode.ArtShift, "art_shift", Store: true),
        4 => new(Opcode.SetFont, "set_font", Store: true),
        5 => Only(v, ZMachineVersion.V6, new(Opcode.DrawPicture, "draw_picture")),
        6 => Only(v, ZMachineVersion.V6, new(Opcode.PictureData, "picture_data", Branch: true)),
        7 => Only(v, ZMachineVersion.V6, new(Opcode.ErasePicture, "erase_picture")),
        8 => Only(v, ZMachineVersion.V6, new(Opcode.SetMargins, "set_margins")),
        9 => new(Opcode.SaveUndo, "save_undo", Store: true),
        10 => new(Opcode.RestoreUndo, "restore_undo", Store: true),
        11 => new(Opcode.PrintUnicode, "print_unicode"),
        12 => new(Opcode.CheckUnicode, "check_unicode", Store: true),
        13 => new(Opcode.SetTrueColour, "set_true_colour"),

        // [zm 14.2.2] EXT:14 and EXT:15 are reserved.
        16 => Only(v, ZMachineVersion.V6, new(Opcode.MoveWindow, "move_window")),
        17 => Only(v, ZMachineVersion.V6, new(Opcode.WindowSize, "window_size")),
        18 => Only(v, ZMachineVersion.V6, new(Opcode.WindowStyle, "window_style")),
        19 => Only(v, ZMachineVersion.V6, new(Opcode.GetWindProp, "get_wind_prop", Store: true)),
        20 => Only(v, ZMachineVersion.V6, new(Opcode.ScrollWindow, "scroll_window")),
        21 => Only(v, ZMachineVersion.V6, new(Opcode.PopStack, "pop_stack")),
        22 => Only(v, ZMachineVersion.V6, new(Opcode.ReadMouse, "read_mouse")),
        23 => Only(v, ZMachineVersion.V6, new(Opcode.MouseWindow, "mouse_window")),
        24 => Only(v, ZMachineVersion.V6, new(Opcode.PushStack, "push_stack", Branch: true)),
        25 => Only(v, ZMachineVersion.V6, new(Opcode.PutWindProp, "put_wind_prop")),
        26 => Only(v, ZMachineVersion.V6, new(Opcode.PrintForm, "print_form")),
        27 => Only(v, ZMachineVersion.V6, new(Opcode.MakeMenu, "make_menu", Branch: true)),
        28 => Only(v, ZMachineVersion.V6, new(Opcode.PictureTable, "picture_table")),
        29 => Only(v, ZMachineVersion.V6, new(Opcode.BufferScreen, "buffer_screen", Store: true)),

        // [zm 14.2.1] EXT:30 and up are to be ignored rather than treated
        // as errors. Nothing says whether they store or branch, so they
        // are decoded as if they do neither.
        >= 30 and <= 255 => new(Opcode.Unknown, $"ext_{number}"),
        _ => null,
    };

    private static OpcodeInfo? From(ZMachineVersion version, ZMachineVersion earliest, OpcodeInfo info) =>
        version >= earliest ? info : null;

    private static OpcodeInfo? Only(ZMachineVersion version, ZMachineVersion only, OpcodeInfo info) =>
        version == only ? info : null;
}
