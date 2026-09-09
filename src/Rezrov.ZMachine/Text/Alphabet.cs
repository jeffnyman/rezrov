namespace Rezrov.ZMachine.Text;

/// <summary>
/// The three alphabets a Z-character can be read against.
/// </summary>
/// <remarks>
/// [zm 3.2.1] A0 is lower case, A1 upper case, and A2 punctuation, and
/// one of them is current at any moment during decoding. The numeric
/// values matter: [zm 3.2.2] the Version 1 and 2 shift rules move between
/// alphabets by adding 1 or 2 and wrapping around, which only works if
/// these are 0, 1, and 2.
/// </remarks>
public enum Alphabet
{
    A0 = 0,
    A1 = 1,
    A2 = 2,
}
