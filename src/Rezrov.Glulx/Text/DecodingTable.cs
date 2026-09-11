namespace Rezrov.Glulx.Text;

/// <summary>
/// [glulx #string_table] The kinds of node a string decoding table
/// holds, by the type byte each begins with.
/// </summary>
public enum DecodingNodeType : byte
{
    /// <summary>
    /// 00: a branch, with a node for a 0 bit and one for a 1 bit.
    /// </summary>
    Branch = 0x00,

    /// <summary>01: the end of the string.</summary>
    Terminator = 0x01,

    /// <summary>02: one character, in one byte.</summary>
    Character = 0x02,

    /// <summary>03: a run of characters ended by a zero byte.</summary>
    CString = 0x03,

    /// <summary>04: one Unicode character, in four bytes.</summary>
    UnicodeCharacter = 0x04,

    /// <summary>
    /// 05: a run of Unicode characters ended by a zero word.
    /// </summary>
    UnicodeString = 0x05,

    /// <summary>
    /// 08: the address of a string to print or function to call.
    /// </summary>
    Indirect = 0x08,

    /// <summary>
    /// 09: the address of a word holding such an address.
    /// </summary>
    DoubleIndirect = 0x09,

    /// <summary>
    /// 0A: an indirect reference with arguments for a function.
    /// </summary>
    IndirectWithArguments = 0x0A,

    /// <summary>0B: a double indirect reference with arguments.</summary>
    DoubleIndirectWithArguments = 0x0B,
}

/// <summary>
/// One node of a decoding table, read out of memory.
/// </summary>
/// <param name="Type">Which kind of node it is.</param>
/// <param name="Address">Where its type byte is.</param>
/// <param name="Zero">For a branch, the node a 0 bit leads to.</param>
/// <param name="One">For a branch, the node a 1 bit leads to.</param>
/// <param name="Value">
/// The character for the character nodes, the address of the text for
/// the string nodes, and the address field for the references.
/// </param>
/// <param name="ArgumentCount">
/// For the references with arguments, how many follow the address.
/// </param>
public sealed record DecodingNode(
    DecodingNodeType Type,
    uint Address,
    DecodingNode? Zero,
    DecodingNode? One,
    uint Value,
    uint ArgumentCount)
{
    /// <summary>
    /// For the references with arguments, where the arguments begin.
    /// </summary>
    public uint ArgumentsAddress => Address + 9;
}

/// <summary>
/// A string decoding table read out of memory into a tree, so that a
/// compressed string is decoded by following object references rather
/// than by reading memory at every bit.
/// </summary>
/// <remarks>
/// [glulx #string_table] The table is a length, a node count, the
/// address of the root, and then the nodes, in any order and at
/// absolute addresses. The specification suggests caching the tree,
/// with the warning that a table in RAM may change under the cache,
/// which the machine handles by rebuilding the tree when memory has
/// been written since. The node count is only reported, never relied
/// on: Inform wrote it too large until 2014.
/// </remarks>
public sealed class DecodingTable
{
    private DecodingTable(uint address, uint length, uint declaredNodeCount, DecodingNode root, int nodesRead, int leaves)
    {
        Address = address;
        Length = length;
        DeclaredNodeCount = declaredNodeCount;
        Root = root;
        NodesRead = nodesRead;
        Leaves = leaves;
    }

    /// <summary>Where the table begins.</summary>
    public uint Address { get; }

    /// <summary>
    /// [glulx #string_table] The table's length in bytes.
    /// </summary>
    public uint Length { get; }

    /// <summary>
    /// [glulx #string_table] The node count the table declares, which
    /// may be too large in older Inform files.
    /// </summary>
    public uint DeclaredNodeCount { get; }

    /// <summary>The root of the tree.</summary>
    public DecodingNode Root { get; }

    /// <summary>How many nodes the tree turned out to have.</summary>
    public int NodesRead { get; }

    /// <summary>How many of those are leaves.</summary>
    public int Leaves { get; }

    /// <summary>
    /// Reads the table at <paramref name="address"/> into a tree.
    /// </summary>
    /// <exception cref="GlulxException">
    /// A node has an unknown type, or the tree refers back to a node
    /// already in it.
    /// </exception>
    public static DecodingTable Read(GlulxMemory memory, uint address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var length = memory.ReadWord(address);
        var declaredCount = memory.ReadWord(address + 4);
        var rootAddress = memory.ReadWord(address + 8);

        var nodes = new Dictionary<uint, DecodingNode>();
        var leaves = 0;
        var root = ReadNode(memory, rootAddress, nodes, ref leaves);

        return new DecodingTable(address, length, declaredCount, root, nodes.Count, leaves);
    }

    private static DecodingNode ReadNode(GlulxMemory memory, uint address, Dictionary<uint, DecodingNode> nodes, ref int leaves)
    {
        // A Huffman tree has no cycles, so a node met twice means the
        // table is not a tree and following it would never end.
        if (nodes.ContainsKey(address))
        {
            throw new GlulxException($"The string decoding table refers to the node at {address:X8} twice.");
        }

        var type = memory.ReadByte(address);
        DecodingNode node;

        switch ((DecodingNodeType)type)
        {
            case DecodingNodeType.Branch:
            {
                // Reserve the address before reading the children, so a
                // child that points back here is caught.
                nodes[address] = new DecodingNode(DecodingNodeType.Branch, address, null, null, 0, 0);
                var zero = ReadNode(memory, memory.ReadWord(address + 1), nodes, ref leaves);
                var one = ReadNode(memory, memory.ReadWord(address + 5), nodes, ref leaves);
                node = new DecodingNode(DecodingNodeType.Branch, address, zero, one, 0, 0);
                break;
            }
            case DecodingNodeType.Terminator:
                node = new DecodingNode(DecodingNodeType.Terminator, address, null, null, 0, 0);
                break;
            case DecodingNodeType.Character:
                node = new DecodingNode(DecodingNodeType.Character, address, null, null, memory.ReadByte(address + 1), 0);
                break;
            case DecodingNodeType.CString:
            case DecodingNodeType.UnicodeString:
                node = new DecodingNode((DecodingNodeType)type, address, null, null, address + 1, 0);
                break;
            case DecodingNodeType.UnicodeCharacter:
            case DecodingNodeType.Indirect:
            case DecodingNodeType.DoubleIndirect:
                node = new DecodingNode((DecodingNodeType)type, address, null, null, memory.ReadWord(address + 1), 0);
                break;
            case DecodingNodeType.IndirectWithArguments:
            case DecodingNodeType.DoubleIndirectWithArguments:
                node = new DecodingNode((DecodingNodeType)type, address, null, null, memory.ReadWord(address + 1), memory.ReadWord(address + 5));
                break;
            default:
                throw new GlulxException($"Unknown entity in the string decoding table: type {type:X2} at {address:X8}.");
        }

        if (node.Type != DecodingNodeType.Branch)
        {
            leaves++;
        }

        nodes[address] = node;
        return node;
    }
}
