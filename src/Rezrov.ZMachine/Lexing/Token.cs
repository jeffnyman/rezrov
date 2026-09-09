namespace Rezrov.ZMachine.Lexing;

/// <summary>
/// One word found by lexical analysis: where it is in the text, how long
/// it is, and the dictionary entry it matched, or 0 if none did.
/// </summary>
/// <remarks>
/// [zm 13.6.3] These are what the parse table records. Writing that table
/// into memory in the layout the read opcode describes is the opcode's
/// job; this is the information it needs.
/// </remarks>
/// <param name="Start">
/// Offset of the word's first character in the text.
/// </param>
/// <param name="Length">The word's length in characters.</param>
/// <param name="DictionaryAddress">
/// The byte address of the matching dictionary entry, or 0.
/// </param>
public readonly record struct Token(int Start, int Length, int DictionaryAddress);
