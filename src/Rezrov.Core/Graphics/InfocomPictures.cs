using System.Buffers.Binary;

namespace Rezrov.Core.Graphics;

/// <summary>
/// [infocom pictures] One picture out of an Infocom graphics file: its
/// number, its size, and where its pixels and colors are kept.
/// </summary>
/// <param name="Number">
/// The number a game asks for the picture by, which is the resource
/// slot and not this entry's place in the directory.
/// </param>
/// <param name="Width">Width in the file's own pixels.</param>
/// <param name="Height">Height in the file's own pixels.</param>
/// <param name="Transparent">
/// The color index the picture is see-through at, or null where it is
/// opaque.
/// </param>
public sealed record InfocomPicture(int Number, int Width, int Height, int? Transparent)
{
    /// <summary>
    /// Whether the file keeps any pixels for this picture. A game may
    /// ask a pictureless entry for its size and never draw it, which is
    /// how the Version 6 games measure space.
    /// </summary>
    public bool HasPixels => Data != 0;

    internal int Data { get; init; }

    internal int ColorMap { get; init; }
}

/// <summary>
/// [infocom pictures] The artwork Infocom's DOS releases carried beside
/// a Version 6 story, in the three graphics standards a PC of the time
/// might have: <c>.CG1</c> for CGA, <c>.EG1</c> and <c>.EG2</c> for
/// EGA, and <c>.MG1</c> for MCGA and the Amiga.
/// </summary>
/// <remarks>
/// A game names a picture by number and knows nothing of which file it
/// came from, so this stands in exactly the place a Blorb's pictures
/// stand. The Blorb releases carry the same artwork converted, and are
/// what most players have; this is for the original files.
///
/// The format has no specification. What is written here was read off
/// the two tools that have always decoded it, Mark Howell's
/// <c>pix2gif</c> and Frotz's <c>x_oldpic.c</c>, and checked against
/// the pictures <c>pix2gif</c> produces. Neither tool's code is used:
/// one states no license at all and the other is under the GPL, and
/// this program is under the MIT license.
/// </remarks>
public sealed class InfocomPictures
{
    // The clear and stop codes sit just above the 256 single byte
    // values, so the first code a picture defines for itself is 258.
    private const int CodeSize = 8;
    private const int ClearCode = 1 << CodeSize;
    private const int StopCode = ClearCode + 1;
    private const int TableSize = 4096;

    // [infocom pictures] The palette an EGA screen had, which is the
    // one a picture that carries no colors of its own is drawn in.
    private static readonly byte[] EgaPalette =
    [
        0, 0, 0,        0, 0, 170,      0, 170, 0,      0, 170, 170,
        170, 0, 0,      170, 0, 170,    170, 170, 0,    170, 170, 170,
        85, 85, 85,     85, 85, 255,    85, 255, 85,    85, 255, 255,
        255, 85, 85,    255, 85, 255,   255, 255, 85,   255, 255, 255,
    ];

    private readonly byte[] _file;
    private readonly Dictionary<int, InfocomPicture> _pictures = [];

    private InfocomPictures(byte[] file, int part, int version, List<InfocomPicture> pictures)
    {
        _file = file;
        Part = part;
        Version = version;
        Pictures = pictures;

        foreach (var picture in pictures)
        {
            _pictures.TryAdd(picture.Number, picture);
        }
    }

    /// <summary>
    /// Which file of a set this is. A picture set too large for one
    /// file is split, and then <c>.EG2</c> is part 2 of <c>.EG1</c>.
    /// </summary>
    public int Part { get; }

    /// <summary>The release of the artwork, as the file states it.</summary>
    public int Version { get; }

    /// <summary>Every picture the directory lists, in file order.</summary>
    public IReadOnlyList<InfocomPicture> Pictures { get; }

    /// <summary>How many pictures the file holds.</summary>
    public int Count => Pictures.Count;

    /// <summary>Whether the file has a picture of that number.</summary>
    public bool Contains(int number) => _pictures.ContainsKey(number);

    /// <summary>The picture of that number, or null for no such one.</summary>
    public InfocomPicture? Find(int number) => _pictures.GetValueOrDefault(number);

    /// <summary>
    /// Reads the directory of an Infocom graphics file.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is too short to hold the directory it claims, or an
    /// entry points outside it.
    /// </exception>
    public static InfocomPictures Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        // [infocom pictures] Sixteen bytes: which file of the set this
        // is, flags, a word nothing has ever explained, the count, a
        // link word, how wide a directory entry is, padding, a
        // checksum, another unexplained word, and the version.
        if (file.Length < 16)
        {
            throw new InvalidDataException("The file is too short to be an Infocom graphics file.");
        }

        var part = file[0];
        var count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4));
        var entry = file[8];
        var version = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(14));

        // Twelve bytes an entry, or fourteen where each picture brings
        // its own colors, which is what the MCGA files do.
        if (entry is not (12 or 14))
        {
            throw new InvalidDataException($"A directory entry of {entry} bytes is not one this reads.");
        }

        if (16 + (count * entry) > file.Length)
        {
            throw new InvalidDataException("The directory runs past the end of the file.");
        }

        var pictures = new List<InfocomPicture>(count);

        for (var i = 0; i < count; i++)
        {
            var at = 16 + (i * entry);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 6));

            // The addresses are three bytes and, unlike everything else
            // here, are written most significant byte first.
            var data = Address(file, at + 8);
            var colors = entry == 14 ? Address(file, at + 11) : 0;

            if (data >= file.Length || colors >= file.Length)
            {
                throw new InvalidDataException($"Picture {i + 1} points past the end of the file.");
            }

            pictures.Add(new InfocomPicture(
                BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at)),
                BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 4)),

                // [infocom pictures] The low flag bit says the picture
                // has a see-through color, and the top four bits say
                // which index it is.
                (flags & 1) != 0 ? flags >> 12 : null)
            {
                Data = data,
                ColorMap = colors,
            });
        }

        return new InfocomPictures(file, part, version, pictures);
    }

    /// <summary>
    /// A picture decoded to pixels, or null for one the file lists but
    /// keeps no pixels for.
    /// </summary>
    /// <remarks>
    /// [infocom pictures] A directory entry with no data address is a
    /// picture the game may ask the size of but never draw, which the
    /// Version 6 games use to measure space with. A picture whose
    /// pixels do not decode comes back null too, and for the same
    /// reason: [zm 8.8.6] a picture an interpreter cannot show is one
    /// it does not draw, and never an error a story is told about.
    /// </remarks>
    public Pixels? Decode(int number)
    {
        if (Find(number) is not { } picture || picture.Data == 0)
        {
            return null;
        }

        if (Unpack(picture) is not { } indices)
        {
            return null;
        }

        var palette = Palette(picture);
        var rgba = new byte[picture.Width * picture.Height * 4];

        for (var i = 0; i < indices.Length; i++)
        {
            var color = indices[i] * 3;

            rgba[(i * 4) + 0] = palette[color];
            rgba[(i * 4) + 1] = palette[color + 1];
            rgba[(i * 4) + 2] = palette[color + 2];
            rgba[(i * 4) + 3] = (byte)(picture.Transparent == indices[i] ? 0 : 255);
        }

        return new Pixels(picture.Width, picture.Height, rgba);
    }


    private static int Address(byte[] file, int at) =>
        (file[at] << 16) | (file[at + 1] << 8) | file[at + 2];

    /// <summary>
    /// The colors a picture is drawn in: the EGA palette, with the
    /// picture's own colors laid over it where it carries any.
    /// </summary>
    /// <remarks>
    /// [infocom pictures] A picture's own map begins with a count and
    /// then three bytes a color, and it is laid in from index 2, so the
    /// first two EGA colors always show through. Some of Arthur's
    /// pictures claim more colors than the fourteen there is room for,
    /// which both reference tools clamp rather than refuse, so this
    /// does too.
    /// </remarks>
    private byte[] Palette(InfocomPicture picture)
    {
        var palette = EgaPalette.ToArray();

        if (picture.ColorMap != 0 && picture.ColorMap < _file.Length)
        {
            var count = Math.Min((int)_file[picture.ColorMap], 14);
            var from = picture.ColorMap + 1;

            if (from + (count * 3) <= _file.Length)
            {
                Array.Copy(_file, from, palette, 2 * 3, count * 3);
            }
        }

        // A see-through color is black underneath, so that anything
        // which ignores the transparency still gets what the original
        // interpreters drew.
        if (picture.Transparent is { } clear && clear * 3 < palette.Length)
        {
            palette[clear * 3] = 0;
            palette[(clear * 3) + 1] = 0;
            palette[(clear * 3) + 2] = 0;
        }

        return palette;
    }

    /// <summary>
    /// The pixels of a picture, one palette index each.
    /// </summary>
    /// <remarks>
    /// [infocom pictures] The compression is a variable width code
    /// scheme of the LZW family, read a bit at a time from the low end
    /// of each byte upward. Codes begin nine bits wide and grow by one
    /// whenever the next code to be defined reaches the width's mask,
    /// which is one sooner than the same scheme in a GIF grows. Code
    /// 256 starts the table over and 257 ends the picture.
    /// </remarks>
    private byte[]? Unpack(InfocomPicture picture)
    {
        var pixels = new byte[picture.Width * picture.Height];
        var prefix = new short[TableSize];
        var pixel = new byte[TableSize];
        var run = new byte[TableSize];

        for (var i = 0; i < TableSize; i++)
        {
            prefix[i] = TableSize;
            pixel[i] = (byte)i;
        }

        var next = ClearCode + 2;
        var width = CodeSize + 1;
        var at = picture.Data * 8;
        var written = 0;
        var previous = 0;

        while (written < pixels.Length)
        {
            var code = ReadCode(ref at, ref width, next);

            if (code == StopCode)
            {
                break;
            }

            if (code == ClearCode)
            {
                width = CodeSize + 1;
                next = ClearCode + 2;
                code = ReadCode(ref at, ref width, next);

                if (code == StopCode)
                {
                    break;
                }
            }
            else if (code > next)
            {
                // [infocom pictures] A code above the highest one
                // defined is one no encoder can have written. Reading
                // on would walk prefix chains that lead nowhere, slowly,
                // so the picture is declined here instead.
                return null;
            }
            else if (next < TableSize)
            {
                // A code that has not been defined yet stands for the
                // run just emitted plus its own first pixel, which is
                // the one case a decoder has to work out for itself.
                var first = code == next ? previous : code;

                while (prefix[first] != TableSize)
                {
                    first = prefix[first];
                }

                prefix[next] = (short)previous;
                pixel[next] = pixel[first];
                next++;
            }

            previous = code;

            // A code unwinds backwards through its prefixes, so the
            // run comes out reversed and is written out in reverse.
            var length = 0;
            var walk = code;

            do
            {
                run[length++] = pixel[walk];
                walk = prefix[walk];
            }
            while (walk != TableSize && length < TableSize);

            // A chain that never reaches the end is a table this
            // stream did not build, so the picture is not one this can
            // read.
            if (walk != TableSize)
            {
                return null;
            }

            while (length > 0 && written < pixels.Length)
            {
                pixels[written++] = run[--length];
            }
        }

        // [infocom pictures] A stream that stops early, or that names
        // a color the palette has no room for, is not a picture this
        // understands. Some of the earlier CGA files are like that, and
        // no tool reads them: pix2gif writes a sixteen color GIF and
        // then fills it with values above fifteen, which C never
        // notices and no picture reader will open. Declining is better
        // than drawing noise.
        if (written != pixels.Length)
        {
            return null;
        }

        foreach (var index in pixels)
        {
            if (index * 3 >= EgaPalette.Length)
            {
                return null;
            }
        }

        return pixels;
    }

    /// <summary>
    /// The next code, taking the width's worth of bits and then
    /// widening the code where the table has grown far enough.
    /// </summary>
    /// <remarks>
    /// [infocom pictures] The widening is checked after every code is
    /// read, the clear code included, and the test is against the
    /// width's mask rather than one past it. Both of those differ from
    /// the same compression in a GIF, and getting either wrong decodes
    /// the first few hundred pixels correctly and then falls apart.
    /// </remarks>
    private int ReadCode(ref int at, ref int width, int next)
    {
        var code = 0;

        for (var taken = 0; taken < width; taken++)
        {
            var index = (at + taken) >> 3;

            if (index >= _file.Length)
            {
                break;
            }

            code |= ((_file[index] >> ((at + taken) & 7)) & 1) << taken;
        }

        at += width;

        if (next == (1 << width) - 1 && width < 12)
        {
            width++;
        }

        return code;
    }
}
