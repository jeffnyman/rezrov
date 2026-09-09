namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// Every opcode the Z-machine has, by its Inform name.
/// </summary>
/// <remarks>
/// [zm 14] The table lists 119 opcodes. An opcode is identified by its
/// operand count and number, and a few numbers mean different opcodes in
/// different versions, which is why <see cref="OpcodeTable"/> takes the
/// version when it resolves one. Where the same name appears in two
/// tables, as save and restore do as 0OP and EXT opcodes, it is one entry
/// here.
///
/// The names follow the standard exactly, including the British spelling
/// in set_colour and set_true_colour, so that a reader can find each one
/// in section 15 without translation.
///
/// <see cref="Unknown"/> is for [zm 14.2.1] extended opcodes from EXT:30
/// upward, which an interpreter is to ignore rather than reject.
/// </remarks>
public enum Opcode
{
    Unknown,

    // 2OP
    Je,
    Jl,
    Jg,
    DecChk,
    IncChk,
    Jin,
    Test,
    Or,
    And,
    TestAttr,
    SetAttr,
    ClearAttr,
    Store,
    InsertObj,
    Loadw,
    Loadb,
    GetProp,
    GetPropAddr,
    GetNextProp,
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Call2s,
    Call2n,
    SetColour,
    Throw,

    // 1OP
    Jz,
    GetSibling,
    GetChild,
    GetParent,
    GetPropLen,
    Inc,
    Dec,
    PrintAddr,
    Call1s,
    RemoveObj,
    PrintObj,
    Ret,
    Jump,
    PrintPaddr,
    Load,
    Not,
    Call1n,

    // 0OP
    Rtrue,
    Rfalse,
    Print,
    PrintRet,
    Nop,
    Save,
    Restore,
    Restart,
    RetPopped,
    Pop,
    Catch,
    Quit,
    NewLine,
    ShowStatus,
    Verify,
    Piracy,

    // VAR
    Call,
    CallVs,
    Storew,
    Storeb,
    PutProp,
    Sread,
    Aread,
    PrintChar,
    PrintNum,
    Random,
    Push,
    Pull,
    SplitWindow,
    SetWindow,
    CallVs2,
    EraseWindow,
    EraseLine,
    SetCursor,
    GetCursor,
    SetTextStyle,
    BufferMode,
    OutputStream,
    InputStream,
    SoundEffect,
    ReadChar,
    ScanTable,
    CallVn,
    CallVn2,
    Tokenise,
    EncodeText,
    CopyTable,
    PrintTable,
    CheckArgCount,

    // EXT
    LogShift,
    ArtShift,
    SetFont,
    DrawPicture,
    PictureData,
    ErasePicture,
    SetMargins,
    SaveUndo,
    RestoreUndo,
    PrintUnicode,
    CheckUnicode,
    SetTrueColour,
    MoveWindow,
    WindowSize,
    WindowStyle,
    GetWindProp,
    ScrollWindow,
    PopStack,
    ReadMouse,
    MouseWindow,
    PushStack,
    PutWindProp,
    PrintForm,
    MakeMenu,
    PictureTable,
    BufferScreen,
}
