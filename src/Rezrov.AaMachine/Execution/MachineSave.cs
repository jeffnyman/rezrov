using System.Buffers.Binary;

namespace Rezrov.AaMachine.Execution;

/// <summary>
/// Saving, restoring and undoing.
/// </summary>
/// <remarks>
/// [aam savefile] A saved game is an IFF form of type AASV holding
/// three chunks: the story's own header, so that a save cannot be
/// loaded into the wrong story; the registers and the divs the story
/// was inside; and the memory.
///
/// The memory is not stored as it stands. It is exclusive-ored with
/// the story's initial state and then run-length encoded, so that
/// everything the game never touched falls out to nothing and the file
/// is roughly the size of what has actually happened.
/// </remarks>
public sealed partial class Machine
{
    // [aam savefile] Sixty-four registers, two long ones, eight short,
    // the two bytes of output state, and the count of the divs that
    // follow them.
    private const int RegisterBytes = (64 * 2) + 8 + 16 + 2 + 2;

    private bool SaveState(bool toUndo, int resumeAt)
    {
        // [aam opcode] Saving from inside a span or a status area
        // would save a state that cannot be returned to.
        if (_inStatus || _spans > 0)
        {
            throw new RuntimeError(7);
        }

        var state = Capture(resumeAt);

        if (toUndo)
        {
            _undo = state;
            return true;
        }

        if (SaveFileName?.Invoke(true) is not { } path)
        {
            return false;
        }

        try
        {
            File.WriteAllBytes(path, state);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Undo()
    {
        if (_undo is { } state && Apply(state))
        {
            Resumed();
        }
    }

    private void Restore()
    {
        if (SaveFileName?.Invoke(false) is not { } path)
        {
            return;
        }

        byte[] state;

        try
        {
            state = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (Apply(state))
        {
            Resumed();
        }
    }

    // [aam opcode] After a restore the story is somewhere else
    // entirely, so the frontend is wound back to nothing and the divs
    // the story was inside are entered again.
    private void Resumed()
    {
        _output.LeaveAll();

        _inStatus = false;
        _spans = 0;
        _links = 0;

        foreach (var styleClass in _divs)
        {
            _output.EnterDiv(styleClass);
        }
    }

    private byte[] Capture(int resumeAt)
    {
        var state = new ushort[3 + _ram.Length + _aux.Length + _heap.Length];
        var at = 0;

        state[at++] = _objectCount;
        state[at++] = _longTermBottom;
        state[at++] = _longTermTop;

        // Only the parts of each memory that are in use are worth
        // keeping. The rest is written as unused, which is what the
        // story started with, so it costs nothing in the file.
        for (var i = 0; i < _ram.Length; i++)
        {
            state[at++] = i < _longTermTop ? _ram[i] : AaValue.Unused;
        }

        for (var i = 0; i < _aux.Length; i++)
        {
            state[at++] = i < _auxTop || i >= _trail ? _aux[i] : AaValue.Unused;
        }

        for (var i = 0; i < _heap.Length; i++)
        {
            state[at++] = i < _top || i >= _env || i >= _cho ? _heap[i] : AaValue.Unused;
        }

        var registers = new byte[RegisterBytes + (_divs.Count * 2)];
        var into = registers.AsSpan();

        for (var i = 0; i < 64; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(into[(i * 2)..], _reg[i]);
        }

        into = into[128..];

        BinaryPrimitives.WriteUInt32BigEndian(into, (uint)resumeAt);
        BinaryPrimitives.WriteUInt32BigEndian(into[4..], (uint)_cont);

        int[] pointers = [_top, _env, _cho, _sim, _auxTop, _trail, _stoppableAux, _stoppableChoice];

        for (var i = 0; i < pointers.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(into[(8 + (i * 2))..], (ushort)pointers[i]);
        }

        into[24] = (byte)_cwl;
        into[25] = (byte)_spc;

        BinaryPrimitives.WriteUInt16BigEndian(into[26..], (ushort)_divs.Count);

        for (var i = 0; i < _divs.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(into[(28 + (i * 2))..], (ushort)_divs[i]);
        }

        return Form(_story.Contents("HEAD").ToArray(), Compress(Difference(state)), registers);
    }

    private bool Apply(byte[] file)
    {
        var chunks = Chunks(file);

        if (!chunks.TryGetValue("DATA", out var data)
            || !chunks.TryGetValue("REGS", out var registers)
            || !chunks.TryGetValue("HEAD", out var head)
            || registers.Length < RegisterBytes)
        {
            return false;
        }

        // [aam savefile] The header has to match exactly, which is how
        // a save from one story is kept out of another.
        if (!head.AsSpan().SequenceEqual(_story.Contents("HEAD")))
        {
            return false;
        }

        var state = Difference(Expand(data, 3 + _ram.Length + _aux.Length + _heap.Length));
        var at = 0;

        _objectCount = state[at++];
        _longTermBottom = state[at++];
        _longTermTop = state[at++];

        for (var i = 0; i < _ram.Length; i++)
        {
            _ram[i] = state[at++];
        }

        for (var i = 0; i < _aux.Length; i++)
        {
            _aux[i] = state[at++];
        }

        for (var i = 0; i < _heap.Length; i++)
        {
            _heap[i] = state[at++];
        }

        var from = registers.AsSpan();

        for (var i = 0; i < 64; i++)
        {
            _reg[i] = BinaryPrimitives.ReadUInt16BigEndian(from[(i * 2)..]);
        }

        from = from[128..];

        _inst = (int)BinaryPrimitives.ReadUInt32BigEndian(from);
        _cont = (int)BinaryPrimitives.ReadUInt32BigEndian(from[4..]);
        _top = BinaryPrimitives.ReadUInt16BigEndian(from[8..]);
        _env = BinaryPrimitives.ReadUInt16BigEndian(from[10..]);
        _cho = BinaryPrimitives.ReadUInt16BigEndian(from[12..]);
        _sim = BinaryPrimitives.ReadUInt16BigEndian(from[14..]);
        _auxTop = BinaryPrimitives.ReadUInt16BigEndian(from[16..]);
        _trail = BinaryPrimitives.ReadUInt16BigEndian(from[18..]);
        _stoppableAux = BinaryPrimitives.ReadUInt16BigEndian(from[20..]);
        _stoppableChoice = BinaryPrimitives.ReadUInt16BigEndian(from[22..]);
        _cwl = from[24];
        _spc = (Spacing)from[25];

        _divs.Clear();

        var divs = BinaryPrimitives.ReadUInt16BigEndian(from[26..]);

        for (var i = 0; i < divs && 28 + (i * 2) + 1 < from.Length; i++)
        {
            _divs.Add(BinaryPrimitives.ReadUInt16BigEndian(from[(28 + (i * 2))..]));
        }

        return true;
    }

    // [aam savefile] The state against the story's own initial state,
    // which turns everything untouched into nothing at all. Doing it
    // again reverses it, which is how a save is read back.
    private ushort[] Difference(ushort[] state)
    {
        var init = _story.Contents("INIT");
        var result = new ushort[state.Length];

        for (var i = 0; i < state.Length; i++)
        {
            var initial = (i * 2) + 1 < init.Length
                ? (ushort)((init[i * 2] << 8) | init[(i * 2) + 1])
                : AaValue.Unused;

            result[i] = (ushort)(state[i] ^ initial);
        }

        return result;
    }

    // [aam savefile] A byte that is not null stands for itself, and a
    // run of up to 256 null bytes is a null and a count one less.
    private static byte[] Compress(ushort[] words)
    {
        var bytes = new List<byte>(words.Length);
        var nulls = 0;

        void Run()
        {
            while (nulls > 0)
            {
                var length = Math.Min(nulls, 256);

                bytes.Add(0);
                bytes.Add((byte)(length - 1));
                nulls -= length;
            }
        }

        foreach (var word in words)
        {
            foreach (var value in (ReadOnlySpan<byte>)[(byte)(word >> 8), (byte)(word & 0xff)])
            {
                if (value == 0)
                {
                    nulls++;
                    continue;
                }

                Run();
                bytes.Add(value);
            }
        }

        Run();

        return [.. bytes];
    }

    private static ushort[] Expand(byte[] compressed, int words)
    {
        var bytes = new byte[words * 2];
        var at = 0;

        for (var i = 0; i < compressed.Length && at < bytes.Length; i++)
        {
            if (compressed[i] != 0)
            {
                bytes[at++] = compressed[i];
                continue;
            }

            var length = i + 1 < compressed.Length ? compressed[++i] + 1 : 1;

            at += Math.Min(length, bytes.Length - at);
        }

        var result = new ushort[words];

        for (var i = 0; i < words; i++)
        {
            result[i] = (ushort)((bytes[i * 2] << 8) | bytes[(i * 2) + 1]);
        }

        return result;
    }

    private static byte[] Form(byte[] head, byte[] data, byte[] registers)
    {
        var file = new List<byte>();

        file.AddRange("FORM"u8);
        file.AddRange([0, 0, 0, 0]);
        file.AddRange("AASV"u8);

        Chunk(file, "HEAD"u8, head);
        Chunk(file, "DATA"u8, data);
        Chunk(file, "REGS"u8, registers);

        var bytes = file.ToArray();

        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));

        return bytes;
    }

    private static void Chunk(List<byte> file, ReadOnlySpan<byte> name, byte[] contents)
    {
        Span<byte> length = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)contents.Length);

        file.AddRange(name);
        file.AddRange(length);
        file.AddRange(contents);

        // Chunks are padded to an even length, as they are in a story.
        if ((contents.Length & 1) != 0)
        {
            file.Add(0);
        }
    }

    private static Dictionary<string, byte[]> Chunks(byte[] file)
    {
        var chunks = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (file.Length < 12
            || !file.AsSpan(0, 4).SequenceEqual("FORM"u8)
            || !file.AsSpan(8, 4).SequenceEqual("AASV"u8))
        {
            return chunks;
        }

        var at = 12;

        while (at + 8 <= file.Length)
        {
            var name = System.Text.Encoding.ASCII.GetString(file, at, 4);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 4));

            if (length < 0 || at + 8 + length > file.Length)
            {
                break;
            }

            chunks.TryAdd(name, file[(at + 8)..(at + 8 + length)]);
            at += 8 + length + (length & 1);
        }

        return chunks;
    }
}
