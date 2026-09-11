namespace Rezrov.Glulx.Instructions;

/// <summary>
/// Every opcode Glulx has, by its name in the specification, with the
/// opcode number as its value.
/// </summary>
/// <remarks>
/// [glulx #dictionary-of-opcodes] The table of opcodes lists 150 of
/// them, and unlike the Z-Machine's, a Glulx opcode is identified by
/// one number alone, so the number is the enum value and the two are
/// interchangeable. The groups follow the dictionary's own sections,
/// which is why Miscellaneous, Branches, Functions, and Output each
/// appear more than once: the numbers were assigned in blocks as the
/// machine grew, and the dictionary keeps them in numeric order.
///
/// [glulx #instruction] There are 2^28 possible opcode numbers, and
/// the ranges 0x1000 to 0x14FF and 0x7900 to 0x79FF are reserved for
/// other interpreters' extensions, so a number outside this list is
/// not an error in the file so much as a feature this interpreter does
/// not have, and the decoder refuses it by number.
///
/// The names are the specification's, with capitals at the word
/// boundaries the abbreviations hide: aloadb is ALoadB (array load
/// byte), sexs is SexS (sign extend short), jfeq is JFeq (jump if
/// floats equal), and dmodr is DModR (double modulo remainder).
/// </remarks>
public enum Opcode
{
    // Miscellaneous
    Nop = 0x0,

    // Integer Math
    Add = 0x10,
    Sub = 0x11,
    Mul = 0x12,
    Div = 0x13,
    Mod = 0x14,
    Neg = 0x15,
    BitAnd = 0x18,
    BitOr = 0x19,
    BitXor = 0x1A,
    BitNot = 0x1B,
    ShiftL = 0x1C,
    SShiftR = 0x1D,
    UShiftR = 0x1E,

    // Branches
    Jump = 0x20,
    Jz = 0x22,
    Jnz = 0x23,
    Jeq = 0x24,
    Jne = 0x25,
    Jlt = 0x26,
    Jge = 0x27,
    Jgt = 0x28,
    Jle = 0x29,
    Jltu = 0x2A,
    Jgeu = 0x2B,
    Jgtu = 0x2C,
    Jleu = 0x2D,

    // Functions
    Call = 0x30,
    Return = 0x31,
    Catch = 0x32,
    Throw = 0x33,
    TailCall = 0x34,

    // Moving Data
    Copy = 0x40,
    CopyS = 0x41,
    CopyB = 0x42,
    SexS = 0x44,
    SexB = 0x45,

    // Array Data
    ALoad = 0x48,
    ALoadS = 0x49,
    ALoadB = 0x4A,
    ALoadBit = 0x4B,
    AStore = 0x4C,
    AStoreS = 0x4D,
    AStoreB = 0x4E,
    AStoreBit = 0x4F,

    // The Stack
    StkCount = 0x50,
    StkPeek = 0x51,
    StkSwap = 0x52,
    StkRoll = 0x53,
    StkCopy = 0x54,

    // Output
    StreamChar = 0x70,
    StreamNum = 0x71,
    StreamStr = 0x72,
    StreamUniChar = 0x73,

    // Miscellaneous
    Gestalt = 0x100,
    DebugTrap = 0x101,

    // Memory Map
    GetMemSize = 0x102,
    SetMemSize = 0x103,

    // Branches
    JumpAbs = 0x104,

    // Random Number Generator
    Random = 0x110,
    SetRandom = 0x111,

    // Game State
    Quit = 0x120,
    Verify = 0x121,
    Restart = 0x122,
    Save = 0x123,
    Restore = 0x124,
    SaveUndo = 0x125,
    RestoreUndo = 0x126,
    Protect = 0x127,
    HasUndo = 0x128,
    DiscardUndo = 0x129,

    // Miscellaneous
    Glk = 0x130,

    // Output
    GetStringTbl = 0x140,
    SetStringTbl = 0x141,
    GetIOSys = 0x148,
    SetIOSys = 0x149,

    // Searching
    LinearSearch = 0x150,
    BinarySearch = 0x151,
    LinkedSearch = 0x152,

    // Functions
    CallF = 0x160,
    CallFI = 0x161,
    CallFII = 0x162,
    CallFIII = 0x163,

    // Block Copy and Clear
    MZero = 0x170,
    MCopy = 0x171,

    // Memory Allocation Heap
    MAlloc = 0x178,
    MFree = 0x179,

    // Accelerated Functions
    AccelFunc = 0x180,
    AccelParam = 0x181,

    // Floating-Point Math
    NumToF = 0x190,
    FToNumZ = 0x191,
    FToNumN = 0x192,
    Ceil = 0x198,
    Floor = 0x199,
    FAdd = 0x1A0,
    FSub = 0x1A1,
    FMul = 0x1A2,
    FDiv = 0x1A3,
    FMod = 0x1A4,
    Sqrt = 0x1A8,
    Exp = 0x1A9,
    Log = 0x1AA,
    Pow = 0x1AB,
    Sin = 0x1B0,
    Cos = 0x1B1,
    Tan = 0x1B2,
    ASin = 0x1B3,
    ACos = 0x1B4,
    ATan = 0x1B5,
    ATan2 = 0x1B6,

    // Floating-Point Comparisons
    JFeq = 0x1C0,
    JFne = 0x1C1,
    JFlt = 0x1C2,
    JFle = 0x1C3,
    JFgt = 0x1C4,
    JFge = 0x1C5,
    JIsNaN = 0x1C8,
    JIsInf = 0x1C9,

    // Double-Precision Math
    NumToD = 0x200,
    DToNumZ = 0x201,
    DToNumN = 0x202,
    FToD = 0x203,
    DToF = 0x204,
    DCeil = 0x208,
    DFloor = 0x209,
    DAdd = 0x210,
    DSub = 0x211,
    DMul = 0x212,
    DDiv = 0x213,
    DModR = 0x214,
    DModQ = 0x215,
    DSqrt = 0x218,
    DExp = 0x219,
    DLog = 0x21A,
    DPow = 0x21B,
    DSin = 0x220,
    DCos = 0x221,
    DTan = 0x222,
    DASin = 0x223,
    DACos = 0x224,
    DATan = 0x225,
    DATan2 = 0x226,

    // Double-Precision Comparisons
    JDeq = 0x230,
    JDne = 0x231,
    JDlt = 0x232,
    JDle = 0x233,
    JDgt = 0x234,
    JDge = 0x235,
    JDIsNaN = 0x238,
    JDIsInf = 0x239,
}
