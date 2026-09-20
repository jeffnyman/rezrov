using Arg = Rezrov.AaMachine.Instructions.OperandKind;

namespace Rezrov.AaMachine.Instructions;

/// <summary>
/// The opcode table as code: which operation a byte names, and what it
/// reads after itself.
/// </summary>
/// <remarks>
/// [aam opcode] Every row of the specification's table is here in its
/// order, with its operand list copied from the same row, so reading
/// this against the table is the check that it is right. Where the
/// table writes an operand as 0 it is implied rather than written, and
/// is left out here, which is why PUSH_ENV appears twice with and
/// without its byte and LOAD_WORD appears twice with and without its
/// value.
///
/// Three rows depend on the file format version rather than on the
/// byte alone, and <see cref="Resolve(byte, int, int)"/> is where that
/// is settled.
/// </remarks>
public static class OpcodeTable
{
    private static readonly OpcodeInfo[] Entries =
    [
        // Execution flow.
        new(0x00, Opcode.Nop, "NOP", []),
        new(0x01, Opcode.Fail, "FAIL", []),
        new(0x02, Opcode.SetCont, "SET_CONT", [Arg.Code]),
        new(0x03, Opcode.Proceed, "PROCEED", []),

        new(0x04, Opcode.Jmp, "JMP", [Arg.Code]),
        new(0x05, Opcode.JmpMulti, "JMP_MULTI", [Arg.Code]),
        new(0x85, Opcode.JmplMulti, "JMPL_MULTI", [Arg.Code]),
        new(0x06, Opcode.JmpSimple, "JMP_SIMPLE", [Arg.Code]),
        new(0x86, Opcode.JmplSimple, "JMPL_SIMPLE", [Arg.Code]),
        new(0x07, Opcode.JmpTail, "JMP_TAIL", [Arg.Code]),
        new(0x87, Opcode.Tail, "TAIL", []),

        new(0x08, Opcode.PushEnv, "PUSH_ENV", [Arg.Byte]),
        new(0x88, Opcode.PushEnv, "PUSH_ENV", []),
        new(0x09, Opcode.PopEnv, "POP_ENV", []),
        new(0x89, Opcode.PopEnvProceed, "POP_ENV_PROCEED", []),

        new(0x0a, Opcode.PushChoice, "PUSH_CHOICE", [Arg.Byte, Arg.Code]),
        new(0x8a, Opcode.PushChoice, "PUSH_CHOICE", [Arg.Code]),
        new(0x0b, Opcode.PopChoice, "POP_CHOICE", [Arg.Byte]),
        new(0x8b, Opcode.PopChoice, "POP_CHOICE", []),
        new(0x0c, Opcode.PopPushChoice, "POP_PUSH_CHOICE", [Arg.Byte, Arg.Code]),
        new(0x8c, Opcode.PopPushChoice, "POP_PUSH_CHOICE", [Arg.Code]),

        new(0x0d, Opcode.CutChoice, "CUT_CHOICE", []),
        new(0x0e, Opcode.GetCho, "GET_CHO", [Arg.Dest]),
        new(0x0f, Opcode.SetCho, "SET_CHO", [Arg.Value]),

        // Live data, and the environments a stoppable region needs.
        new(0x10, Opcode.Assign, "ASSIGN", [Arg.Value, Arg.Dest]),
        new(0x90, Opcode.Assign, "ASSIGN", [Arg.VByte, Arg.Dest]),

        new(0x11, Opcode.MakeVar, "MAKE_VAR", [Arg.Dest]),

        new(0x12, Opcode.MakePair, "MAKE_PAIR", [Arg.Dest, Arg.Dest, Arg.Dest]),
        new(0x13, Opcode.MakePair, "MAKE_PAIR", [Arg.VWord, Arg.Dest, Arg.Dest]),
        new(0x93, Opcode.MakePair, "MAKE_PAIR", [Arg.VByte, Arg.Dest, Arg.Dest]),

        new(0x14, Opcode.AuxPushVal, "AUX_PUSH_VAL", [Arg.Value]),
        new(0x94, Opcode.AuxPushRaw, "AUX_PUSH_RAW", []),
        new(0x15, Opcode.AuxPushRaw, "AUX_PUSH_RAW", [Arg.VWord]),
        new(0x95, Opcode.AuxPushRaw, "AUX_PUSH_RAW", [Arg.VByte]),
        new(0x16, Opcode.AuxPopVal, "AUX_POP_VAL", [Arg.Dest]),
        new(0x17, Opcode.AuxPopList, "AUX_POP_LIST", [Arg.Dest]),
        new(0x18, Opcode.AuxPopListChk, "AUX_POP_LIST_CHK", [Arg.Value]),
        new(0x19, Opcode.AuxPopListMatch, "AUX_POP_LIST_MATCH", [Arg.Value]),

        new(0x1b, Opcode.SplitList, "SPLIT_LIST", [Arg.Value, Arg.Value, Arg.Dest]),

        new(0x1c, Opcode.Stop, "STOP", []),
        new(0x1d, Opcode.PushStop, "PUSH_STOP", [Arg.Code]),
        new(0x1e, Opcode.PopStop, "POP_STOP", []),

        new(0x1f, Opcode.SplitWord, "SPLIT_WORD", [Arg.Value, Arg.Dest]),
        new(0x9f, Opcode.JoinWords, "JOIN_WORDS", [Arg.Value, Arg.Dest]),

        // The random access area, where the game's own state lives.
        new(0x20, Opcode.LoadWord, "LOAD_WORD", [Arg.Value, Arg.Index, Arg.Dest]),
        new(0xa0, Opcode.LoadWord, "LOAD_WORD", [Arg.Index, Arg.Dest]),
        new(0x21, Opcode.LoadByte, "LOAD_BYTE", [Arg.Value, Arg.Index, Arg.Dest]),
        new(0xa1, Opcode.LoadByte, "LOAD_BYTE", [Arg.Index, Arg.Dest]),
        new(0x22, Opcode.LoadVal, "LOAD_VAL", [Arg.Value, Arg.Index, Arg.Dest]),
        new(0xa2, Opcode.LoadVal, "LOAD_VAL", [Arg.Index, Arg.Dest]),

        new(0x24, Opcode.StoreWord, "STORE_WORD", [Arg.Value, Arg.Index, Arg.Value]),
        new(0xa4, Opcode.StoreWord, "STORE_WORD", [Arg.Index, Arg.Value]),
        new(0x25, Opcode.StoreByte, "STORE_BYTE", [Arg.Value, Arg.Index, Arg.Value]),
        new(0xa5, Opcode.StoreByte, "STORE_BYTE", [Arg.Index, Arg.Value]),
        new(0x26, Opcode.StoreVal, "STORE_VAL", [Arg.Value, Arg.Index, Arg.Value]),
        new(0xa6, Opcode.StoreVal, "STORE_VAL", [Arg.Index, Arg.Value]),

        new(0x28, Opcode.SetFlag, "SET_FLAG", [Arg.Value, Arg.Index]),
        new(0xa8, Opcode.SetFlag, "SET_FLAG", [Arg.Index]),
        new(0x29, Opcode.ResetFlag, "RESET_FLAG", [Arg.Value, Arg.Index]),
        new(0xa9, Opcode.ResetFlag, "RESET_FLAG", [Arg.Index]),

        new(0x2d, Opcode.Unlink, "UNLINK", [Arg.Value, Arg.Index, Arg.Index, Arg.Value]),
        new(0xad, Opcode.Unlink, "UNLINK", [Arg.Index, Arg.Index, Arg.Value]),

        new(0x2e, Opcode.SetParent, "SET_PARENT", [Arg.Value, Arg.Value]),
        new(0xae, Opcode.SetParent, "SET_PARENT", [Arg.VByte, Arg.Value]),
        new(0x2f, Opcode.SetParent, "SET_PARENT", [Arg.Value, Arg.VByte]),
        new(0xaf, Opcode.SetParent, "SET_PARENT", [Arg.VByte, Arg.VByte]),

        // Conditional branches, taken when the test holds.
        new(0x30, Opcode.IfRawEq, "IF_RAW_EQ", [Arg.VWord, Arg.Raw, Arg.Code]),
        new(0xb0, Opcode.IfRawEq, "IF_RAW_EQ", [Arg.Raw, Arg.Code]),
        new(0x31, Opcode.IfBound, "IF_BOUND", [Arg.Value, Arg.Code]),
        new(0x32, Opcode.IfEmpty, "IF_EMPTY", [Arg.Value, Arg.Code]),
        new(0x33, Opcode.IfNum, "IF_NUM", [Arg.Value, Arg.Code]),
        new(0x34, Opcode.IfPair, "IF_PAIR", [Arg.Value, Arg.Code]),
        new(0x35, Opcode.IfObj, "IF_OBJ", [Arg.Value, Arg.Code]),
        new(0x36, Opcode.IfWord, "IF_WORD", [Arg.Value, Arg.Code]),
        new(0xb6, Opcode.IfUword, "IF_UWORD", [Arg.Value, Arg.Code]),
        new(0x37, Opcode.IfUnify, "IF_UNIFY", [Arg.Value, Arg.Value, Arg.Code]),
        new(0x38, Opcode.IfGt, "IF_GT", [Arg.Value, Arg.Value, Arg.Code]),
        new(0x39, Opcode.IfEq, "IF_EQ", [Arg.VWord, Arg.Value, Arg.Code]),
        new(0xb9, Opcode.IfEq, "IF_EQ", [Arg.VByte, Arg.Value, Arg.Code]),
        new(0x3a, Opcode.IfMemEq, "IF_MEM_EQ", [Arg.Value, Arg.Index, Arg.Raw, Arg.Code]),
        new(0xba, Opcode.IfMemEq, "IF_MEM_EQ", [Arg.Index, Arg.Raw, Arg.Code]),
        new(0x3b, Opcode.IfFlag, "IF_FLAG", [Arg.Value, Arg.Index, Arg.Code]),
        new(0xbb, Opcode.IfFlag, "IF_FLAG", [Arg.Index, Arg.Code]),
        new(0x3c, Opcode.IfCwl, "IF_CWL", [Arg.Code]),
        new(0x3d, Opcode.IfMemEq, "IF_MEM_EQ", [Arg.Value, Arg.Index, Arg.VByte, Arg.Code]),
        new(0xbd, Opcode.IfMemEq, "IF_MEM_EQ", [Arg.Index, Arg.VByte, Arg.Code]),

        // The same tests, taken when they do not hold.
        new(0x40, Opcode.IfnRawEq, "IFN_RAW_EQ", [Arg.VWord, Arg.Raw, Arg.Code]),
        new(0xc0, Opcode.IfnRawEq, "IFN_RAW_EQ", [Arg.Raw, Arg.Code]),
        new(0x41, Opcode.IfnBound, "IFN_BOUND", [Arg.Value, Arg.Code]),
        new(0x42, Opcode.IfnEmpty, "IFN_EMPTY", [Arg.Value, Arg.Code]),
        new(0x43, Opcode.IfnNum, "IFN_NUM", [Arg.Value, Arg.Code]),
        new(0x44, Opcode.IfnPair, "IFN_PAIR", [Arg.Value, Arg.Code]),
        new(0x45, Opcode.IfnObj, "IFN_OBJ", [Arg.Value, Arg.Code]),
        new(0x46, Opcode.IfnWord, "IFN_WORD", [Arg.Value, Arg.Code]),
        new(0xc6, Opcode.IfnUword, "IFN_UWORD", [Arg.Value, Arg.Code]),
        new(0x47, Opcode.IfnUnify, "IFN_UNIFY", [Arg.Value, Arg.Value, Arg.Code]),
        new(0x48, Opcode.IfnGt, "IFN_GT", [Arg.Value, Arg.Value, Arg.Code]),
        new(0x49, Opcode.IfnEq, "IFN_EQ", [Arg.VWord, Arg.Value, Arg.Code]),
        new(0xc9, Opcode.IfnEq, "IFN_EQ", [Arg.VByte, Arg.Value, Arg.Code]),
        new(0x4a, Opcode.IfnMemEq, "IFN_MEM_EQ", [Arg.Value, Arg.Index, Arg.Raw, Arg.Code]),
        new(0xca, Opcode.IfnMemEq, "IFN_MEM_EQ", [Arg.Index, Arg.Raw, Arg.Code]),
        new(0x4b, Opcode.IfnFlag, "IFN_FLAG", [Arg.Value, Arg.Index, Arg.Code]),
        new(0xcb, Opcode.IfnFlag, "IFN_FLAG", [Arg.Index, Arg.Code]),
        new(0x4c, Opcode.IfnCwl, "IFN_CWL", [Arg.Code]),
        new(0x4d, Opcode.IfnMemEq, "IFN_MEM_EQ", [Arg.Value, Arg.Index, Arg.VByte, Arg.Code]),
        new(0xcd, Opcode.IfnMemEq, "IFN_MEM_EQ", [Arg.Index, Arg.VByte, Arg.Code]),

        // Arithmetic, on plain numbers and on tagged ones.
        new(0x50, Opcode.AddRaw, "ADD_RAW", [Arg.Raw, Arg.Raw, Arg.Dest]),
        new(0xd0, Opcode.IncRaw, "INC_RAW", [Arg.Raw, Arg.Dest]),
        new(0x51, Opcode.SubRaw, "SUB_RAW", [Arg.Raw, Arg.Raw, Arg.Dest]),
        new(0xd1, Opcode.DecRaw, "DEC_RAW", [Arg.Raw, Arg.Dest]),
        new(0x52, Opcode.RandRaw, "RAND_RAW", [Arg.Byte, Arg.Dest]),

        new(0x58, Opcode.AddNum, "ADD_NUM", [Arg.Value, Arg.Value, Arg.Dest]),
        new(0xd8, Opcode.IncNum, "INC_NUM", [Arg.Value, Arg.Dest]),
        new(0x59, Opcode.SubNum, "SUB_NUM", [Arg.Value, Arg.Value, Arg.Dest]),
        new(0xd9, Opcode.DecNum, "DEC_NUM", [Arg.Value, Arg.Dest]),
        new(0x5a, Opcode.RandNum, "RAND_NUM", [Arg.Value, Arg.Value, Arg.Dest]),
        new(0x5b, Opcode.MulNum, "MUL_NUM", [Arg.Value, Arg.Value, Arg.Dest]),
        new(0x5c, Opcode.DivNum, "DIV_NUM", [Arg.Value, Arg.Value, Arg.Dest]),
        new(0x5d, Opcode.ModNum, "MOD_NUM", [Arg.Value, Arg.Value, Arg.Dest]),

        // Output. The four print opcodes differ in whether a space is
        // wanted before the string and after it.
        new(0x60, Opcode.PrintAStrA, "PRINT_A_STR_A", [Arg.Text]),
        new(0xe0, Opcode.PrintNStrA, "PRINT_N_STR_A", [Arg.Text]),
        new(0x61, Opcode.PrintAStrN, "PRINT_A_STR_N", [Arg.Text]),
        new(0xe1, Opcode.PrintNStrN, "PRINT_N_STR_N", [Arg.Text]),

        new(0x62, Opcode.NoSpace, "NOSPACE", []),
        new(0xe2, Opcode.Space, "SPACE", []),
        new(0x63, Opcode.Line, "LINE", []),
        new(0xe3, Opcode.Par, "PAR", []),

        new(0x64, Opcode.SpaceN, "SPACE_N", [Arg.Value]),
        new(0x65, Opcode.PrintVal, "PRINT_VAL", [Arg.Value]),

        new(0x66, Opcode.EnterDiv, "ENTER_DIV", [Arg.Index]),
        new(0xe6, Opcode.LeaveDiv, "LEAVE_DIV", []),

        // [aam opcode] $67 was ENTER_STATUS with an implied 0 until
        // version 1.0 made it SET_BODY, and $e7 was the LEAVE_STATUS
        // that went with it. Resolve settles both by version.
        new(0x67, Opcode.SetBody, "SET_BODY", [Arg.Index]),
        new(0xe7, Opcode.LeaveStatus, "LEAVE_STATUS", []),

        new(0x68, Opcode.EnterLinkRes, "ENTER_LINK_RES", [Arg.Value]),
        new(0xe8, Opcode.LeaveLinkRes, "LEAVE_LINK_RES", []),

        new(0x69, Opcode.EnterLink, "ENTER_LINK", [Arg.Value]),
        new(0xe9, Opcode.LeaveLink, "LEAVE_LINK", []),

        new(0x6a, Opcode.EnterSelfLink, "ENTER_SELF_LINK", []),
        new(0xea, Opcode.LeaveSelfLink, "LEAVE_SELF_LINK", []),

        new(0x6b, Opcode.SetStyle, "SET_STYLE", [Arg.Byte]),
        new(0xeb, Opcode.ResetStyle, "RESET_STYLE", [Arg.Byte]),

        new(0x6c, Opcode.EmbedRes, "EMBED_RES", [Arg.Value]),
        new(0xec, Opcode.CanEmbedRes, "CAN_EMBED_RES", [Arg.Value, Arg.Dest]),

        new(0x6d, Opcode.Progress, "PROGRESS", [Arg.Value, Arg.Value]),

        new(0x6e, Opcode.EnterSpan, "ENTER_SPAN", [Arg.Index]),
        new(0xee, Opcode.LeaveSpan, "LEAVE_SPAN", []),

        new(0x6f, Opcode.EnterStatus, "ENTER_STATUS", [Arg.Byte, Arg.Index]),
        new(0xef, Opcode.LeaveStatus, "LEAVE_STATUS", []),

        // Input, the machine itself, and the rest.
        new(0x70, Opcode.Ext0, "EXT0", [Arg.Byte]),

        new(0x72, Opcode.Save, "SAVE", [Arg.Code]),
        new(0xf2, Opcode.SaveUndo, "SAVE_UNDO", [Arg.Code]),

        new(0x73, Opcode.GetInput, "GET_INPUT", [Arg.Dest]),
        new(0xf3, Opcode.GetKey, "GET_KEY", [Arg.Dest]),

        new(0x74, Opcode.VmInfo, "VM_INFO", [Arg.Byte, Arg.Dest]),

        new(0x78, Opcode.SetIdx, "SET_IDX", [Arg.Value]),
        new(0x79, Opcode.CheckEq, "CHECK_EQ", [Arg.VWord, Arg.Code]),
        new(0xf9, Opcode.CheckEq, "CHECK_EQ", [Arg.VByte, Arg.Code]),
        new(0x7a, Opcode.CheckGtEq, "CHECK_GT_EQ", [Arg.VWord, Arg.Code, Arg.Code]),
        new(0xfa, Opcode.CheckGtEq, "CHECK_GT_EQ", [Arg.VByte, Arg.Code, Arg.Code]),
        new(0x7b, Opcode.CheckGt, "CHECK_GT", [Arg.Raw, Arg.Code]),
        new(0xfb, Opcode.CheckGt, "CHECK_GT", [Arg.Byte, Arg.Code]),
        new(0x7c, Opcode.CheckWordmap, "CHECK_WORDMAP", [Arg.Index, Arg.Code]),

        new(0x7d, Opcode.CheckEq2, "CHECK_EQ_2", [Arg.VWord, Arg.VWord, Arg.Code]),
        new(0xfd, Opcode.CheckEq2, "CHECK_EQ_2", [Arg.VByte, Arg.VByte, Arg.Code]),

        new(0x7f, Opcode.Tracepoint, "TRACEPOINT", [Arg.Text, Arg.Text, Arg.Text, Arg.Word]),
    ];

    private static readonly OpcodeInfo?[] ByCode = BuildIndex();

    // [aam opcode] $67 held ENTER_STATUS, with the 0 that picked the
    // top status area implied, until version 1.0 gave the byte to
    // SET_BODY and moved every status area onto $6f.
    private static readonly OpcodeInfo OldEnterStatus =
        new(0x67, Opcode.EnterStatus, "ENTER_STATUS", [Arg.Index]);

    /// <summary>Every opcode, in the specification's order.</summary>
    public static IReadOnlyList<OpcodeInfo> All => Entries;

    /// <summary>
    /// The opcode a byte names in a story of the given file format
    /// version, or null if that story has no such opcode.
    /// </summary>
    public static OpcodeInfo? Resolve(byte code, int major, int minor)
    {
        var entry = ByCode[code];

        if (major > 0)
        {
            // [aam opcode] AUX_POP_VAL was taken out in version 1.0,
            // having been deprecated before that.
            return code == 0x16 ? null : entry;
        }

        // [aam opcode] The two status opcodes that moved. $e7 keeps
        // its name either way, so only $67 changes hands.
        return code == 0x67 ? OldEnterStatus : entry;
    }

    private static OpcodeInfo?[] BuildIndex()
    {
        var byCode = new OpcodeInfo?[256];

        foreach (var entry in Entries)
        {
            byCode[entry.Code] = entry;
        }

        return byCode;
    }
}
