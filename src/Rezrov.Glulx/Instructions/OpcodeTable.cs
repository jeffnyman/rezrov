namespace Rezrov.Glulx.Instructions;

/// <summary>
/// The dictionary of opcodes as code: which opcode a number names, and
/// the operands each one takes.
/// </summary>
/// <remarks>
/// [glulx #dictionary-of-opcodes] Every entry of the dictionary is
/// here in its order, with its operand list copied from the heading of
/// its entry, "add L1 L2 S1" becoming "LLS". That notation is the
/// specification's own, and reading the list against the dictionary
/// is the check that it is right. The reference interpreter's operand
/// lists were compared as well and agree on all 150.
///
/// [glulx #opcodes_branch] The branch opcodes are marked, since their
/// last operand is an offset with the two special values 0 and 1, and
/// jumpabs is not marked because it takes an absolute address.
/// [glulx #instruction] copys and copyb are marked as the two opcodes
/// whose indirect operands are 16-bit and 8-bit fields.
/// </remarks>
public static class OpcodeTable
{
    private static readonly OpcodeInfo[] Entries =
    [
        // Miscellaneous
        new(Opcode.Nop, "nop", ""),

        // Integer Math
        new(Opcode.Add, "add", "LLS"),
        new(Opcode.Sub, "sub", "LLS"),
        new(Opcode.Mul, "mul", "LLS"),
        new(Opcode.Div, "div", "LLS"),
        new(Opcode.Mod, "mod", "LLS"),
        new(Opcode.Neg, "neg", "LS"),
        new(Opcode.BitAnd, "bitand", "LLS"),
        new(Opcode.BitOr, "bitor", "LLS"),
        new(Opcode.BitXor, "bitxor", "LLS"),
        new(Opcode.BitNot, "bitnot", "LS"),
        new(Opcode.ShiftL, "shiftl", "LLS"),
        new(Opcode.SShiftR, "sshiftr", "LLS"),
        new(Opcode.UShiftR, "ushiftr", "LLS"),

        // Branches
        new(Opcode.Jump, "jump", "L", Branches: true),
        new(Opcode.Jz, "jz", "LL", Branches: true),
        new(Opcode.Jnz, "jnz", "LL", Branches: true),
        new(Opcode.Jeq, "jeq", "LLL", Branches: true),
        new(Opcode.Jne, "jne", "LLL", Branches: true),
        new(Opcode.Jlt, "jlt", "LLL", Branches: true),
        new(Opcode.Jge, "jge", "LLL", Branches: true),
        new(Opcode.Jgt, "jgt", "LLL", Branches: true),
        new(Opcode.Jle, "jle", "LLL", Branches: true),
        new(Opcode.Jltu, "jltu", "LLL", Branches: true),
        new(Opcode.Jgeu, "jgeu", "LLL", Branches: true),
        new(Opcode.Jgtu, "jgtu", "LLL", Branches: true),
        new(Opcode.Jleu, "jleu", "LLL", Branches: true),

        // Functions
        new(Opcode.Call, "call", "LLS"),
        new(Opcode.Return, "return", "L"),
        new(Opcode.Catch, "catch", "SL"),
        new(Opcode.Throw, "throw", "LL"),
        new(Opcode.TailCall, "tailcall", "LL"),

        // Moving Data
        new(Opcode.Copy, "copy", "LS"),
        new(Opcode.CopyS, "copys", "LS", OperandSize: 2),
        new(Opcode.CopyB, "copyb", "LS", OperandSize: 1),
        new(Opcode.SexS, "sexs", "LS"),
        new(Opcode.SexB, "sexb", "LS"),

        // Array Data
        new(Opcode.ALoad, "aload", "LLS"),
        new(Opcode.ALoadS, "aloads", "LLS"),
        new(Opcode.ALoadB, "aloadb", "LLS"),
        new(Opcode.ALoadBit, "aloadbit", "LLS"),
        new(Opcode.AStore, "astore", "LLL"),
        new(Opcode.AStoreS, "astores", "LLL"),
        new(Opcode.AStoreB, "astoreb", "LLL"),
        new(Opcode.AStoreBit, "astorebit", "LLL"),

        // The Stack
        new(Opcode.StkCount, "stkcount", "S"),
        new(Opcode.StkPeek, "stkpeek", "LS"),
        new(Opcode.StkSwap, "stkswap", ""),
        new(Opcode.StkRoll, "stkroll", "LL"),
        new(Opcode.StkCopy, "stkcopy", "L"),

        // Output
        new(Opcode.StreamChar, "streamchar", "L"),
        new(Opcode.StreamNum, "streamnum", "L"),
        new(Opcode.StreamStr, "streamstr", "L"),
        new(Opcode.StreamUniChar, "streamunichar", "L"),

        // Miscellaneous
        new(Opcode.Gestalt, "gestalt", "LLS"),
        new(Opcode.DebugTrap, "debugtrap", "L"),

        // Memory Map
        new(Opcode.GetMemSize, "getmemsize", "S"),
        new(Opcode.SetMemSize, "setmemsize", "LS"),

        // Branches
        new(Opcode.JumpAbs, "jumpabs", "L"),

        // Random Number Generator
        new(Opcode.Random, "random", "LS"),
        new(Opcode.SetRandom, "setrandom", "L"),

        // Game State
        new(Opcode.Quit, "quit", ""),
        new(Opcode.Verify, "verify", "S"),
        new(Opcode.Restart, "restart", ""),
        new(Opcode.Save, "save", "LS"),
        new(Opcode.Restore, "restore", "LS"),
        new(Opcode.SaveUndo, "saveundo", "S"),
        new(Opcode.RestoreUndo, "restoreundo", "S"),
        new(Opcode.Protect, "protect", "LL"),
        new(Opcode.HasUndo, "hasundo", "S"),
        new(Opcode.DiscardUndo, "discardundo", ""),

        // Miscellaneous
        new(Opcode.Glk, "glk", "LLS"),

        // Output
        new(Opcode.GetStringTbl, "getstringtbl", "S"),
        new(Opcode.SetStringTbl, "setstringtbl", "L"),
        new(Opcode.GetIOSys, "getiosys", "SS"),
        new(Opcode.SetIOSys, "setiosys", "LL"),

        // Searching
        new(Opcode.LinearSearch, "linearsearch", "LLLLLLLS"),
        new(Opcode.BinarySearch, "binarysearch", "LLLLLLLS"),
        new(Opcode.LinkedSearch, "linkedsearch", "LLLLLLS"),

        // Functions
        new(Opcode.CallF, "callf", "LS"),
        new(Opcode.CallFI, "callfi", "LLS"),
        new(Opcode.CallFII, "callfii", "LLLS"),
        new(Opcode.CallFIII, "callfiii", "LLLLS"),

        // Block Copy and Clear
        new(Opcode.MZero, "mzero", "LL"),
        new(Opcode.MCopy, "mcopy", "LLL"),

        // Memory Allocation Heap
        new(Opcode.MAlloc, "malloc", "LS"),
        new(Opcode.MFree, "mfree", "L"),

        // Accelerated Functions
        new(Opcode.AccelFunc, "accelfunc", "LL"),
        new(Opcode.AccelParam, "accelparam", "LL"),

        // Floating-Point Math
        new(Opcode.NumToF, "numtof", "LS"),
        new(Opcode.FToNumZ, "ftonumz", "LS"),
        new(Opcode.FToNumN, "ftonumn", "LS"),
        new(Opcode.Ceil, "ceil", "LS"),
        new(Opcode.Floor, "floor", "LS"),
        new(Opcode.FAdd, "fadd", "LLS"),
        new(Opcode.FSub, "fsub", "LLS"),
        new(Opcode.FMul, "fmul", "LLS"),
        new(Opcode.FDiv, "fdiv", "LLS"),
        new(Opcode.FMod, "fmod", "LLSS"),
        new(Opcode.Sqrt, "sqrt", "LS"),
        new(Opcode.Exp, "exp", "LS"),
        new(Opcode.Log, "log", "LS"),
        new(Opcode.Pow, "pow", "LLS"),
        new(Opcode.Sin, "sin", "LS"),
        new(Opcode.Cos, "cos", "LS"),
        new(Opcode.Tan, "tan", "LS"),
        new(Opcode.ASin, "asin", "LS"),
        new(Opcode.ACos, "acos", "LS"),
        new(Opcode.ATan, "atan", "LS"),
        new(Opcode.ATan2, "atan2", "LLS"),

        // Floating-Point Comparisons
        new(Opcode.JFeq, "jfeq", "LLLL", Branches: true),
        new(Opcode.JFne, "jfne", "LLLL", Branches: true),
        new(Opcode.JFlt, "jflt", "LLL", Branches: true),
        new(Opcode.JFle, "jfle", "LLL", Branches: true),
        new(Opcode.JFgt, "jfgt", "LLL", Branches: true),
        new(Opcode.JFge, "jfge", "LLL", Branches: true),
        new(Opcode.JIsNaN, "jisnan", "LL", Branches: true),
        new(Opcode.JIsInf, "jisinf", "LL", Branches: true),

        // Double-Precision Math
        new(Opcode.NumToD, "numtod", "LSS"),
        new(Opcode.DToNumZ, "dtonumz", "LLS"),
        new(Opcode.DToNumN, "dtonumn", "LLS"),
        new(Opcode.FToD, "ftod", "LSS"),
        new(Opcode.DToF, "dtof", "LLS"),
        new(Opcode.DCeil, "dceil", "LLSS"),
        new(Opcode.DFloor, "dfloor", "LLSS"),
        new(Opcode.DAdd, "dadd", "LLLLSS"),
        new(Opcode.DSub, "dsub", "LLLLSS"),
        new(Opcode.DMul, "dmul", "LLLLSS"),
        new(Opcode.DDiv, "ddiv", "LLLLSS"),
        new(Opcode.DModR, "dmodr", "LLLLSS"),
        new(Opcode.DModQ, "dmodq", "LLLLSS"),
        new(Opcode.DSqrt, "dsqrt", "LLSS"),
        new(Opcode.DExp, "dexp", "LLSS"),
        new(Opcode.DLog, "dlog", "LLSS"),
        new(Opcode.DPow, "dpow", "LLLLSS"),
        new(Opcode.DSin, "dsin", "LLSS"),
        new(Opcode.DCos, "dcos", "LLSS"),
        new(Opcode.DTan, "dtan", "LLSS"),
        new(Opcode.DASin, "dasin", "LLSS"),
        new(Opcode.DACos, "dacos", "LLSS"),
        new(Opcode.DATan, "datan", "LLSS"),
        new(Opcode.DATan2, "datan2", "LLLLSS"),

        // Double-Precision Comparisons
        new(Opcode.JDeq, "jdeq", "LLLLLLL", Branches: true),
        new(Opcode.JDne, "jdne", "LLLLLLL", Branches: true),
        new(Opcode.JDlt, "jdlt", "LLLLL", Branches: true),
        new(Opcode.JDle, "jdle", "LLLLL", Branches: true),
        new(Opcode.JDgt, "jdgt", "LLLLL", Branches: true),
        new(Opcode.JDge, "jdge", "LLLLL", Branches: true),
        new(Opcode.JDIsNaN, "jdisnan", "LLL", Branches: true),
        new(Opcode.JDIsInf, "jdisinf", "LLL", Branches: true),
    ];

    private static readonly Dictionary<uint, OpcodeInfo> ByNumber =
        Entries.ToDictionary(entry => (uint)entry.Opcode);

    /// <summary>Every opcode, in the dictionary's order.</summary>
    public static IReadOnlyList<OpcodeInfo> All => Entries;

    /// <summary>
    /// Finds the opcode with a number, or null if there is none.
    /// </summary>
    public static OpcodeInfo? Resolve(uint number) =>
        ByNumber.TryGetValue(number, out var info) ? info : null;

    /// <summary>Finds the entry for an opcode.</summary>
    public static OpcodeInfo Describe(Opcode opcode) => ByNumber[(uint)opcode];
}
