namespace Rezrov.AaMachine.Instructions;

/// <summary>
/// Every operation the Aa-machine has, by its name in the
/// specification.
/// </summary>
/// <remarks>
/// [aam opcode] An opcode is always one byte, and the high bit usually
/// picks between two variants of the same operation: one that takes an
/// operand and one that has it implicitly, or one that reads a word
/// where the other reads a byte. Often the two variants are the same
/// operation and share a name here, so the byte is not the value of
/// this enum; the table holds the byte.
///
/// The names are the specification's own, with capitals at the word
/// boundaries: JMPL_MULTI is JmplMulti, PRINT_A_STR_N is PrintAStrN,
/// and the IFN_ family, which branches when its test fails, is Ifn.
/// </remarks>
public enum Opcode
{
    // Execution flow.
    Nop,
    Fail,
    SetCont,
    Proceed,
    Jmp,
    JmpMulti,
    JmplMulti,
    JmpSimple,
    JmplSimple,
    JmpTail,
    Tail,
    PushEnv,
    PopEnv,
    PopEnvProceed,
    PushChoice,
    PopChoice,
    PopPushChoice,
    CutChoice,
    GetCho,
    SetCho,

    // Live data, and the environments a stoppable region needs.
    Assign,
    MakeVar,
    MakePair,
    AuxPushVal,
    AuxPushRaw,
    AuxPopVal,
    AuxPopList,
    AuxPopListChk,
    AuxPopListMatch,
    SplitList,
    Stop,
    PushStop,
    PopStop,
    SplitWord,
    JoinWords,

    // The random access area, where the game's own state lives.
    LoadWord,
    LoadByte,
    LoadVal,
    StoreWord,
    StoreByte,
    StoreVal,
    SetFlag,
    ResetFlag,
    Unlink,
    SetParent,

    // Conditional branches, taken when the test holds.
    IfRawEq,
    IfBound,
    IfEmpty,
    IfNum,
    IfPair,
    IfObj,
    IfWord,
    IfUword,
    IfUnify,
    IfGt,
    IfEq,
    IfMemEq,
    IfFlag,
    IfCwl,

    // The same tests, taken when they do not hold.
    IfnRawEq,
    IfnBound,
    IfnEmpty,
    IfnNum,
    IfnPair,
    IfnObj,
    IfnWord,
    IfnUword,
    IfnUnify,
    IfnGt,
    IfnEq,
    IfnMemEq,
    IfnFlag,
    IfnCwl,

    // Arithmetic, on plain numbers and on tagged ones.
    AddRaw,
    IncRaw,
    SubRaw,
    DecRaw,
    RandRaw,
    AddNum,
    IncNum,
    SubNum,
    DecNum,
    RandNum,
    MulNum,
    DivNum,
    ModNum,

    // Output.
    PrintAStrA,
    PrintNStrA,
    PrintAStrN,
    PrintNStrN,
    NoSpace,
    Space,
    Line,
    Par,
    SpaceN,
    PrintVal,
    EnterDiv,
    LeaveDiv,
    EnterStatus,
    LeaveStatus,
    SetBody,
    EnterLinkRes,
    LeaveLinkRes,
    EnterLink,
    LeaveLink,
    EnterSelfLink,
    LeaveSelfLink,
    SetStyle,
    ResetStyle,
    EmbedRes,
    CanEmbedRes,
    Progress,
    EnterSpan,
    LeaveSpan,

    // Input, the machine itself, and the rest.
    Ext0,
    Save,
    SaveUndo,
    GetInput,
    GetKey,
    VmInfo,
    SetIdx,
    CheckEq,
    CheckGtEq,
    CheckGt,
    CheckWordmap,
    CheckEq2,
    Tracepoint,
}
