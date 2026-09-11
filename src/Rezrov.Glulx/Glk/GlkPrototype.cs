using System.Globalization;

namespace Rezrov.Glulx.Glk;

/// <summary>The basic type of a parameter.</summary>
public enum GlkParameterType
{
    /// <summary>Iu or Is: a 32-bit integer.</summary>
    Number,

    /// <summary>Cn, Cu, or Cs: a character in a byte.</summary>
    Character,

    /// <summary>
    /// S: a Latin-1 string, an E0 string object in Glulx.
    /// </summary>
    Text,

    /// <summary>
    /// U: a Unicode string, an E2 string object in Glulx.
    /// </summary>
    UnicodeText,

    /// <summary>
    /// F: a floating-point value, which Glk does not yet use.
    /// </summary>
    Real,

    /// <summary>Qa to Qd: an opaque object of a class.</summary>
    Opaque,
}

/// <summary>How a parameter is passed by reference, if it is.</summary>
public enum GlkReference
{
    /// <summary>By value.</summary>
    None,

    /// <summary>&amp;: passed in and out.</summary>
    InOut,

    /// <summary>&lt;: passed out only.</summary>
    Out,

    /// <summary>&gt;: passed in only.</summary>
    In,
}

/// <summary>
/// One parameter of a Glk function as its prototype describes it.
/// </summary>
/// <param name="Type">The basic type.</param>
/// <param name="AllowsNegative">Is, Cs: the value is signed.</param>
/// <param name="ObjectClass">For an object, its class, 0 for
/// windows.</param>
/// <param name="Reference">Whether and how it is passed by
/// reference.</param>
/// <param name="Mandatory">+: the reference may not be null.</param>
/// <param name="IsArray">#: a reference to an array with a length.</param>
/// <param name="Retained">!: the library keeps the array.</param>
/// <param name="Fields">[...]: the fields of a structure reference.</param>
public sealed record GlkParameter(
    GlkParameterType Type,
    bool AllowsNegative,
    int ObjectClass,
    GlkReference Reference,
    bool Mandatory,
    bool IsArray,
    bool Retained,
    IReadOnlyList<GlkParameter>? Fields)
{
    /// <summary>Whether this is a reference to a structure.</summary>
    public bool IsStructure => Fields is not null;

    /// <summary>Whether a value is read in through the reference.</summary>
    public bool PassesIn => Reference is GlkReference.InOut or GlkReference.In;

    /// <summary>
    /// Whether a value is written out through the reference.
    /// </summary>
    public bool PassesOut => Reference is GlkReference.InOut or GlkReference.Out;
}

/// <summary>
/// The argument list of a Glk function, parsed from the prototype
/// string the dispatch layer publishes for it.
/// </summary>
/// <remarks>
/// [glk #dispatching_func] The string begins with the count of
/// logical arguments, the return value counted among them, then one
/// code per argument, then a colon, then the return value's code if
/// there is one. A code is a type name, possibly behind a reference
/// prefix, and a reference may be to a structure, written as a
/// bracketed prototype of its own, or to an array. The grammar at the
/// end of that section is what this parser follows, and the count is
/// checked against what was read.
/// </remarks>
public sealed class GlkPrototype
{
    private GlkPrototype(IReadOnlyList<GlkParameter> parameters, GlkParameter? result)
    {
        Parameters = parameters;
        Result = result;
    }

    /// <summary>The logical arguments, in order.</summary>
    public IReadOnlyList<GlkParameter> Parameters { get; }

    /// <summary>The return value, or null for none.</summary>
    public GlkParameter? Result { get; }

    /// <summary>Parses a prototype string such as "3Qa&lt;Iu:Qa".</summary>
    /// <exception cref="FormatException">The string does not follow the
    /// grammar.</exception>
    public static GlkPrototype Parse(string prototype)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        var at = 0;
        var (count, parameters) = ParseList(prototype, ref at, ':');
        GlkParameter? result = null;
        if (at < prototype.Length)
        {
            result = ParseParameter(prototype, ref at);
        }

        if (at != prototype.Length)
        {
            throw new FormatException($"Trailing text in the prototype \"{prototype}\".");
        }

        if (count != parameters.Count + (result is null ? 0 : 1))
        {
            throw new FormatException($"The count {count} does not match the arguments in \"{prototype}\".");
        }

        return new GlkPrototype(parameters, result);
    }

    // A count, then parameters up to the closing character, which for
    // a structure is the count of its fields.
    private static (int Count, List<GlkParameter> Parameters) ParseList(string text, ref int at, char closing)
    {
        var start = at;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (start == at)
        {
            throw new FormatException($"Expected an argument count at {start} in \"{text}\".");
        }

        var count = int.Parse(text.AsSpan(start, at - start), CultureInfo.InvariantCulture);
        var parameters = new List<GlkParameter>(count);
        while (at < text.Length && text[at] != closing)
        {
            parameters.Add(ParseParameter(text, ref at));
        }

        if (at >= text.Length)
        {
            throw new FormatException($"Expected '{closing}' at {at} in \"{text}\".");
        }

        at++;
        return (count, parameters);
    }

    private static GlkParameter ParseParameter(string text, ref int at)
    {
        var reference = GlkReference.None;
        var mandatory = false;
        var isArray = false;
        var retained = false;

        if (at < text.Length && text[at] is '&' or '<' or '>')
        {
            reference = text[at] switch
            {
                '&' => GlkReference.InOut,
                '<' => GlkReference.Out,
                _ => GlkReference.In,
            };
            at++;

            if (at < text.Length && text[at] == '+')
            {
                mandatory = true;
                at++;
            }

            if (at < text.Length && text[at] == '[')
            {
                at++;
                var (count, fields) = ParseList(text, ref at, ']');
                if (count != fields.Count)
                {
                    throw new FormatException($"The field count {count} does not match the fields at {at} in \"{text}\".");
                }

                return new GlkParameter(GlkParameterType.Number, false, 0, reference, mandatory, false, false, fields);
            }

            if (at < text.Length && text[at] == '#')
            {
                isArray = true;
                at++;
                if (at < text.Length && text[at] == '!')
                {
                    retained = true;
                    at++;
                }
            }
        }

        if (at >= text.Length)
        {
            throw new FormatException($"Expected a type at the end of \"{text}\".");
        }

        var letter = text[at++];
        switch (letter)
        {
            case 'I':
            case 'C':
            {
                var qualifier = Qualifier(text, ref at);
                var type = letter == 'I' ? GlkParameterType.Number : GlkParameterType.Character;
                return new GlkParameter(type, qualifier == 's', 0, reference, mandatory, isArray, retained, null);
            }
            case 'Q':
            {
                var qualifier = Qualifier(text, ref at);
                return new GlkParameter(GlkParameterType.Opaque, false, qualifier - 'a', reference, mandatory, isArray, retained, null);
            }
            case 'S':
                return new GlkParameter(GlkParameterType.Text, false, 0, reference, mandatory, isArray, retained, null);
            case 'U':
                return new GlkParameter(GlkParameterType.UnicodeText, false, 0, reference, mandatory, isArray, retained, null);
            case 'F':
                return new GlkParameter(GlkParameterType.Real, false, 0, reference, mandatory, isArray, retained, null);
            default:
                throw new FormatException($"Unknown type '{letter}' at {at - 1} in \"{text}\".");
        }
    }

    private static char Qualifier(string text, ref int at)
    {
        if (at >= text.Length)
        {
            throw new FormatException($"Expected a type qualifier at the end of \"{text}\".");
        }

        return text[at++];
    }
}
