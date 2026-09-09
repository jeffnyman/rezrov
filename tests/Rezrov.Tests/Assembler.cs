using Rezrov.ZMachine.Instructions;

namespace Rezrov.Tests;

/// <summary>
/// Builds Z-machine code by hand for interpreter tests, one instruction
/// at a time, in the forms [zm 4.3] describes. Just enough of an
/// assembler that a test reads like the program it runs.
/// </summary>
internal sealed class Assembler
{
    private readonly List<byte> _bytes = [];

    /// <summary>The number of bytes emitted so far.</summary>
    public int Position => _bytes.Count;

    public byte[] ToArray() => _bytes.ToArray();

    public static Operand Small(int value) => new(OperandType.SmallConstant, (ushort)value);

    public static Operand Large(int value) => new(OperandType.LargeConstant, (ushort)value);

    public static Operand Var(int number) => new(OperandType.Variable, (ushort)number);

    public Assembler Bytes(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    /// <summary>
    /// [zm 4.3.2] Long form: a 2OP with small constant or variable
    /// operands.
    /// </summary>
    public Assembler Long(int opcode, Operand a, Operand b)
    {
        var first = opcode & 0x1F;
        if (a.Type == OperandType.Variable)
        {
            first |= 0x40;
        }

        if (b.Type == OperandType.Variable)
        {
            first |= 0x20;
        }

        _bytes.Add((byte)first);
        Emit(a);
        Emit(b);
        return this;
    }

    /// <summary>[zm 4.3.1] Short form with one operand.</summary>
    public Assembler Short1(int opcode, Operand a)
    {
        _bytes.Add((byte)(0x80 | ((int)a.Type << 4) | (opcode & 0x0F)));
        Emit(a);
        return this;
    }

    /// <summary>[zm 4.3.1] Short form with no operands.</summary>
    public Assembler Short0(int opcode)
    {
        _bytes.Add((byte)(0xB0 | (opcode & 0x0F)));
        return this;
    }

    /// <summary>
    /// [zm op:quit] The usual last instruction of a test program.
    /// </summary>
    public Assembler Quit() => Short0(Op.Quit);

    /// <summary>
    /// [zm 4.3.3] Variable form, as a 2OP or a VAR opcode, with a types
    /// byte, and [zm 4.4.3.1] a second one for call_vs2 and call_vn2.
    /// </summary>
    public Assembler Variable(int opcode, bool isVar, params Operand[] operands)
    {
        _bytes.Add((byte)(0xC0 | (isVar ? 0x20 : 0) | (opcode & 0x1F)));
        EmitTypes(operands, 0);

        if (isVar && opcode is 12 or 26)
        {
            EmitTypes(operands, 4);
        }

        foreach (var operand in operands)
        {
            Emit(operand);
        }

        return this;
    }

    /// <summary>[zm 4.3.4] Extended form.</summary>
    public Assembler Ext(int opcode, params Operand[] operands)
    {
        _bytes.Add(0xBE);
        _bytes.Add((byte)opcode);
        EmitTypes(operands, 0);

        foreach (var operand in operands)
        {
            Emit(operand);
        }

        return this;
    }

    /// <summary>[zm 4.6] The store variable byte.</summary>
    public Assembler Store(int variable)
    {
        _bytes.Add((byte)variable);
        return this;
    }

    /// <summary>
    /// [zm 4.7] Branch data, in the one-byte form when the offset fits.
    /// </summary>
    public Assembler Branch(bool onTrue, int offset)
    {
        var sense = onTrue ? 0x80 : 0x00;

        if (offset is >= 0 and <= 63)
        {
            _bytes.Add((byte)(sense | 0x40 | offset));
        }
        else
        {
            var value = offset & 0x3FFF;
            _bytes.Add((byte)(sense | (value >> 8)));
            _bytes.Add((byte)value);
        }

        return this;
    }

    /// <summary>
    /// [zm 4.8] Inline text, lower case letters and spaces only.
    /// </summary>
    public Assembler Text(string text)
    {
        _bytes.AddRange(ZChars.Text(text));
        return this;
    }

    private void EmitTypes(Operand[] operands, int from)
    {
        var types = 0xFF;
        for (var i = 0; i < 4 && from + i < operands.Length; i++)
        {
            var shift = 6 - (2 * i);
            types &= ~(0x03 << shift);
            types |= (int)operands[from + i].Type << shift;
        }

        _bytes.Add((byte)types);
    }

    private void Emit(Operand operand)
    {
        switch (operand.Type)
        {
            case OperandType.LargeConstant:
                _bytes.Add((byte)(operand.Value >> 8));
                _bytes.Add((byte)operand.Value);
                break;
            case OperandType.SmallConstant:
            case OperandType.Variable:
                _bytes.Add((byte)operand.Value);
                break;
        }
    }
}

/// <summary>
/// The opcode numbers the interpreter tests assemble, from [zm 14].
/// </summary>
internal static class Op
{
    // 2OP
    public const int Je = 1;
    public const int Jl = 2;
    public const int Jg = 3;
    public const int DecChk = 4;
    public const int IncChk = 5;
    public const int Jin = 6;
    public const int Test = 7;
    public const int Or = 8;
    public const int And = 9;
    public const int TestAttr = 10;
    public const int SetAttr = 11;
    public const int ClearAttr = 12;
    public const int Store = 13;
    public const int InsertObj = 14;
    public const int Loadw = 15;
    public const int Loadb = 16;
    public const int GetProp = 17;
    public const int GetPropAddr = 18;
    public const int GetNextProp = 19;
    public const int Add = 20;
    public const int Sub = 21;
    public const int Mul = 22;
    public const int Div = 23;
    public const int Mod = 24;
    public const int Call2s = 25;
    public const int SetColour = 27;
    public const int Throw = 28;

    // 1OP
    public const int Jz = 0;
    public const int GetSibling = 1;
    public const int GetChild = 2;
    public const int GetParent = 3;
    public const int GetPropLen = 4;
    public const int Inc = 5;
    public const int Dec = 6;
    public const int PrintAddr = 7;
    public const int RemoveObj = 9;
    public const int PrintObj = 10;
    public const int Ret = 11;
    public const int Jump = 12;
    public const int PrintPaddr = 13;
    public const int Load = 14;
    public const int Not = 15;
    public const int Call1n = 15;

    // 0OP
    public const int Rtrue = 0;
    public const int Rfalse = 1;
    public const int Print = 2;
    public const int PrintRet = 3;
    public const int Nop = 4;
    public const int Save = 5;
    public const int Restore = 6;
    public const int Restart = 7;
    public const int RetPopped = 8;
    public const int Catch = 9;
    public const int Quit = 10;
    public const int NewLine = 11;
    public const int ShowStatus = 12;
    public const int Verify = 13;
    public const int Piracy = 15;

    // VAR
    public const int Call = 0;
    public const int Storew = 1;
    public const int Storeb = 2;
    public const int PutProp = 3;
    public const int Sread = 4;
    public const int Aread = 4;
    public const int PrintChar = 5;
    public const int PrintNum = 6;
    public const int Random = 7;
    public const int Push = 8;
    public const int Pull = 9;
    public const int SplitWindow = 10;
    public const int SetWindow = 11;
    public const int CallVs2 = 12;
    public const int EraseWindow = 13;
    public const int EraseLine = 14;
    public const int SetCursor = 15;
    public const int GetCursor = 16;
    public const int SetTextStyle = 17;
    public const int BufferMode = 18;
    public const int OutputStream = 19;
    public const int InputStream = 20;
    public const int SoundEffect = 21;
    public const int ReadChar = 22;
    public const int ScanTable = 23;
    public const int CallVn = 25;
    public const int Tokenise = 27;
    public const int EncodeText = 28;
    public const int CopyTable = 29;
    public const int PrintTable = 30;
    public const int CheckArgCount = 31;

    // EXT
    public const int LogShift = 2;
    public const int ArtShift = 3;
}
