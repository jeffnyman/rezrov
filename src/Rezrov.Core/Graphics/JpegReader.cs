using System.Buffers.Binary;

namespace Rezrov.Core.Graphics;

/// <summary>
/// Reads a JPEG file into pixels a frontend can draw.
/// </summary>
/// <remarks>
/// [blorb 2.2] JPEG is the other picture format of a resource file, and
/// the games in the corpus use two of its modes: the baseline sequential
/// one that everything can read, and the progressive one, which about
/// half of them are written in. Both are here, along with the chroma
/// subsampling, restart intervals, and grayscale frames the corpus also
/// carries.
///
/// The shape of the work is set by [jpeg G.2]: a progressive file sends
/// the same coefficients over several scans, each adding either a band
/// of frequencies or another bit of precision, so nothing can be turned
/// into pixels until every scan has been read. The coefficients of the
/// whole picture are therefore collected first, and the dequantizing,
/// the inverse transform, and the color conversion all happen at the
/// end. A baseline file is the same path with one scan.
///
/// What is not here is what no picture in the corpus uses and nothing
/// could be tested against: arithmetic coding, the lossless and
/// hierarchical modes, twelve bit samples, four component frames, and a
/// frame whose height arrives in a DNL marker. Each is refused rather
/// than guessed at.
/// </remarks>
public static class JpegReader
{
    // [jpeg A.3.6] Where each coefficient of the zig-zag sequence
    // belongs in the 8 by 8 block, so that a coefficient can be put in
    // its place as it is read.
    private static ReadOnlySpan<int> ZigZag =>
    [
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // [jpeg A.3.3] The cosines of the inverse transform, which are the
    // same for every block and so are worked out once.
    private static readonly float[] Cosines = BuildCosines();

    /// <summary>
    /// The most pixels a picture may claim. A resource file's pictures
    /// are drawn to fit a screen, so a header asking for more than this
    /// is malformed, and refusing it keeps a bad two byte field from
    /// asking for gigabytes of coefficients.
    /// </summary>
    private const int PixelLimit = 32 * 1024 * 1024;

    /// <summary>
    /// Decodes a JPEG, or returns null if the bytes are not one, or are
    /// one this cannot decode.
    /// </summary>
    public static Pixels? Read(ReadOnlySpan<byte> file)
    {
        // [jpeg B.2.1] Every JPEG opens with SOI.
        if (file.Length < 4 || file[0] != 0xFF || file[1] != 0xD8)
        {
            return null;
        }

        var quantization = new int[4][];
        var dcTables = new HuffmanTable?[4];
        var acTables = new HuffmanTable?[4];
        var restartInterval = 0;
        var colorTransform = -1;
        Frame? frame = null;

        for (var at = 2; at + 1 < file.Length;)
        {
            // [jpeg B.1.1.2] A marker is X'FF' then a byte that is
            // neither zero nor another X'FF', and any number of X'FF'
            // fill bytes may come first.
            if (file[at] != 0xFF)
            {
                at++;
                continue;
            }

            var marker = file[at + 1];
            if (marker is 0xFF or 0x00)
            {
                at++;
                continue;
            }

            // [jpeg B.1.1.3] The markers that stand alone: TEM, the
            // restart markers, and EOI, which ends the picture.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                at += 2;
                continue;
            }

            if (marker == 0xD9)
            {
                break;
            }

            // [jpeg B.1.1.4] Everything else is a marker segment, whose
            // first parameter is its own length in bytes, the length
            // itself included and the marker excluded.
            if (at + 4 > file.Length)
            {
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(file[(at + 2)..]);
            if (length < 2 || at + 2 + length > file.Length)
            {
                break;
            }

            var segment = file.Slice(at + 4, length - 2);

            switch (marker)
            {
                case 0xDB: // DQT
                    if (!ReadQuantization(segment, quantization))
                    {
                        return null;
                    }

                    break;

                case 0xC4: // DHT
                    if (!ReadHuffman(segment, dcTables, acTables))
                    {
                        return null;
                    }

                    break;

                case 0xDD: // DRI
                    // [jpeg B.2.4.4] How many MCUs lie between restart
                    // markers, or zero for a scan with none.
                    restartInterval = segment.Length >= 2
                        ? BinaryPrimitives.ReadUInt16BigEndian(segment)
                        : 0;
                    break;

                case 0xC0: // SOF0, baseline sequential
                case 0xC1: // SOF1, extended sequential
                case 0xC2: // SOF2, progressive
                    if (frame is not null)
                    {
                        // [jpeg B.3] A second frame means the
                        // hierarchical mode, which is not supported.
                        return null;
                    }

                    frame = ReadFrame(segment, marker == 0xC2);
                    if (frame is null)
                    {
                        return null;
                    }

                    break;

                case 0xEE: // APP14
                    // Adobe's segment, whose last byte says whether the
                    // three components are a color difference of a
                    // color or the colors themselves.
                    if (segment.Length >= 12 && segment[..5].SequenceEqual("Adobe"u8))
                    {
                        colorTransform = segment[^1];
                    }

                    break;

                case 0xDA: // SOS
                    if (frame is null)
                    {
                        return null;
                    }

                    var scan = ReadScan(segment, frame, dcTables, acTables);
                    if (scan is null)
                    {
                        return null;
                    }

                    at = DecodeScan(file, at + 2 + length, frame, scan, restartInterval);
                    continue;

                default:
                    // [jpeg B.1.1.3] The modes with no decoder here.
                    // Everything else, such as a comment or another
                    // application segment, is skipped by its length.
                    if (marker is 0xC3 or 0xCC or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCF))
                    {
                        return null;
                    }

                    break;
            }

            at += 2 + length;
        }

        return frame is null ? null : Compose(frame, quantization, colorTransform);
    }

    // [jpeg B.2.4.1] One or more quantization tables, each with its
    // precision and destination in one byte and then its 64 elements in
    // zig-zag order. They are put back into block order here, so that
    // dequantizing is an element for an element.
    private static bool ReadQuantization(ReadOnlySpan<byte> segment, int[][] tables)
    {
        for (var at = 0; at < segment.Length;)
        {
            var precision = segment[at] >> 4;
            var destination = segment[at] & 15;
            at++;

            if (destination > 3)
            {
                return false;
            }

            var table = new int[64];
            for (var k = 0; k < 64; k++)
            {
                if (precision == 0)
                {
                    if (at >= segment.Length)
                    {
                        return false;
                    }

                    table[ZigZag[k]] = segment[at++];
                }
                else
                {
                    if (at + 1 >= segment.Length)
                    {
                        return false;
                    }

                    table[ZigZag[k]] = BinaryPrimitives.ReadUInt16BigEndian(segment[at..]);
                    at += 2;
                }
            }

            tables[destination] = table;
        }

        return true;
    }

    // [jpeg B.2.4.2] One or more Huffman tables, each a class and a
    // destination, then the count of codes of each of the sixteen
    // lengths, then the values those codes stand for.
    private static bool ReadHuffman(ReadOnlySpan<byte> segment, HuffmanTable?[] dc, HuffmanTable?[] ac)
    {
        for (var at = 0; at + 17 <= segment.Length;)
        {
            var kind = segment[at] >> 4;
            var destination = segment[at] & 15;
            at++;

            if (kind > 1 || destination > 3)
            {
                return false;
            }

            var counts = segment.Slice(at, 16);
            at += 16;

            var total = 0;
            for (var i = 0; i < 16; i++)
            {
                total += counts[i];
            }

            if (at + total > segment.Length)
            {
                return false;
            }

            var table = new HuffmanTable(counts, segment.Slice(at, total));
            at += total;

            if (kind == 0)
            {
                dc[destination] = table;
            }
            else
            {
                ac[destination] = table;
            }
        }

        return true;
    }

    // [jpeg B.2.2] The frame header: the sample precision, the size of
    // the picture, and for each component its identifier, its sampling
    // factors, and which quantization table it uses.
    private static Frame? ReadFrame(ReadOnlySpan<byte> segment, bool progressive)
    {
        if (segment.Length < 6 || segment[0] != 8)
        {
            // [jpeg B.2.2] Twelve bit samples are allowed in the
            // extended and progressive modes; no picture in the corpus
            // uses them and nothing here could be tested against one.
            return null;
        }

        var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        var count = segment[5];

        // [jpeg B.2.2] A height of zero means the DNL marker at the end
        // of the first scan carries it, which nothing writes.
        if (width == 0 || height == 0 || (long)width * height > PixelLimit)
        {
            return null;
        }

        // A picture is either a gray one or a color one. Four component
        // frames hold ink rather than light and none is in the corpus.
        if ((count != 1 && count != 3) || segment.Length < 6 + (count * 3))
        {
            return null;
        }

        var components = new Component[count];
        var maxHorizontal = 1;
        var maxVertical = 1;

        for (var i = 0; i < count; i++)
        {
            var horizontal = segment[7 + (i * 3)] >> 4;
            var vertical = segment[7 + (i * 3)] & 15;
            var table = segment[8 + (i * 3)];

            if (horizontal is < 1 or > 4 || vertical is < 1 or > 4 || table > 3)
            {
                return null;
            }

            components[i] = new Component(segment[6 + (i * 3)], horizontal, vertical, table);
            maxHorizontal = Math.Max(maxHorizontal, horizontal);
            maxVertical = Math.Max(maxVertical, vertical);
        }

        var frame = new Frame(width, height, progressive, components)
        {
            MaxHorizontal = maxHorizontal,
            MaxVertical = maxVertical,
            McusPerLine = Spread(width, 8 * maxHorizontal),
            McusPerColumn = Spread(height, 8 * maxVertical),
        };

        foreach (var component in components)
        {
            // [jpeg A.1.1] A component is as many samples across as the
            // picture is, scaled by its share of the sampling factors,
            // and it is coded in whole blocks of eight.
            component.SampleWidth = Spread(width * component.Horizontal, maxHorizontal);
            component.SampleHeight = Spread(height * component.Vertical, maxVertical);
            component.BlocksPerLine = Spread(component.SampleWidth, 8);
            component.BlocksPerColumn = Spread(component.SampleHeight, 8);

            // [jpeg A.2.3] An interleaved scan sends whole MCUs, so the
            // last one can reach past the picture. The blocks it writes
            // are kept and then ignored, which is what [jpeg A.3.2]
            // asks of a decoder.
            component.BlocksPerLineForMcu = frame.McusPerLine * component.Horizontal;
            component.BlocksPerColumnForMcu = frame.McusPerColumn * component.Vertical;
            component.Coefficients =
                new short[component.BlocksPerLineForMcu * component.BlocksPerColumnForMcu * 64];
        }

        return frame;
    }

    // [jpeg B.2.3] The scan header: which components are in this scan,
    // which entropy tables each one uses, and for a progressive file the
    // band of coefficients and the bit of precision it carries.
    private static Scan? ReadScan(ReadOnlySpan<byte> segment, Frame frame, HuffmanTable?[] dc, HuffmanTable?[] ac)
    {
        if (segment.Length < 1)
        {
            return null;
        }

        var count = segment[0];
        if (count is < 1 or > 4 || segment.Length < 4 + (count * 2))
        {
            return null;
        }

        var components = new Component[count];

        for (var j = 0; j < count; j++)
        {
            var selector = segment[1 + (j * 2)];
            var tables = segment[2 + (j * 2)];

            var component = Array.Find(frame.Components, c => c.Id == selector);
            if (component is null)
            {
                return null;
            }

            component.DcTable = dc[tables >> 4];
            component.AcTable = ac[tables & 15];
            components[j] = component;
        }

        var start = segment[1 + (count * 2)];
        var end = segment[2 + (count * 2)];
        var approximation = segment[3 + (count * 2)];

        if (start > 63 || end > 63 || start > end)
        {
            return null;
        }

        return new Scan(components, start, end, approximation >> 4, approximation & 15)
        {
            Progressive = frame.Progressive,
        };
    }

    /// <summary>
    /// Reads one scan's entropy-coded data and returns where the next
    /// marker begins.
    /// </summary>
    private static int DecodeScan(ReadOnlySpan<byte> file, int at, Frame frame, Scan scan, int restartInterval)
    {
        // [jpeg A.2] One component in a scan is non-interleaved and is
        // sent block by block; more than one is interleaved and is sent
        // as minimum coded units, each holding a block from every
        // component in its sampling factors.
        var single = scan.Components.Length == 1;
        var first = scan.Components[0];
        var total = single
            ? first.BlocksPerLine * first.BlocksPerColumn
            : frame.McusPerLine * frame.McusPerColumn;

        // [jpeg B.2.2] A restart interval of zero means the scan is one
        // entropy-coded segment from end to end.
        var interval = restartInterval > 0 ? restartInterval : total;
        var bits = new BitReader(file, at);
        var done = 0;

        while (done < total)
        {
            // [jpeg F.2.1.3.1] The difference a DC coefficient is coded
            // as is against the block before it, and [jpeg G.1.2.2] an
            // end-of-band run may span blocks. A restart interval
            // starts both afresh.
            foreach (var component in scan.Components)
            {
                component.Prediction = 0;
            }

            scan.EobRun = 0;
            var last = Math.Min(done + interval, total);

            for (; done < last; done++)
            {
                if (single)
                {
                    var row = done / first.BlocksPerLine;
                    var column = done % first.BlocksPerLine;
                    DecodeBlock(ref bits, scan, first, Offset(first, row, column));
                }
                else
                {
                    var mcuRow = done / frame.McusPerLine;
                    var mcuColumn = done % frame.McusPerLine;

                    foreach (var component in scan.Components)
                    {
                        for (var v = 0; v < component.Vertical; v++)
                        {
                            for (var h = 0; h < component.Horizontal; h++)
                            {
                                var row = (mcuRow * component.Vertical) + v;
                                var column = (mcuColumn * component.Horizontal) + h;
                                DecodeBlock(ref bits, scan, component, Offset(component, row, column));
                            }
                        }
                    }
                }
            }

            // [jpeg B.1.1.5] Each entropy-coded segment but the last is
            // followed by a restart marker, on a byte boundary.
            if (done < total && !bits.SkipRestart())
            {
                break;
            }
        }

        // Whether the scan ended where it should or the data ran out
        // part way, reading goes on from the next marker.
        return NextMarker(file, bits.Position);
    }

    private static int NextMarker(ReadOnlySpan<byte> file, int at)
    {
        for (var i = Math.Max(at, 0); i + 1 < file.Length; i++)
        {
            if (file[i] == 0xFF && file[i + 1] != 0x00 && file[i + 1] != 0xFF)
            {
                return i;
            }
        }

        return file.Length;
    }

    private static int Offset(Component component, int row, int column) =>
        ((row * component.BlocksPerLineForMcu) + column) * 64;

    private static void DecodeBlock(ref BitReader bits, Scan scan, Component component, int offset)
    {
        if (offset + 64 > component.Coefficients.Length)
        {
            return;
        }

        if (!scan.Progressive)
        {
            DecodeSequential(ref bits, component, offset);
            return;
        }

        // [jpeg G.1.1.1.1] A progressive scan carries either the DC
        // coefficient or a band of AC coefficients, and [jpeg G.1.1.1.2]
        // is either the first scan of that band or one that adds a bit
        // to what an earlier scan sent.
        if (scan.Start == 0)
        {
            if (scan.High == 0)
            {
                DecodeDcFirst(ref bits, scan, component, offset);
            }
            else
            {
                DecodeDcRefine(ref bits, scan, component, offset);
            }
        }
        else if (scan.High == 0)
        {
            DecodeAcFirst(ref bits, scan, component, offset);
        }
        else
        {
            DecodeAcRefine(ref bits, scan, component, offset);
        }
    }

    // [jpeg F.2.2.1] The DC coefficient, then [jpeg F.2.2.2] the AC
    // coefficients as runs of zeros each ending in a value, up to the
    // end of the block or an end-of-block code.
    private static void DecodeSequential(ref BitReader bits, Component component, int offset)
    {
        var coefficients = component.Coefficients;
        var size = Decode(ref bits, component.DcTable);
        if (size < 0)
        {
            return;
        }

        component.Prediction += size == 0 ? 0 : Extend(bits.Receive(size), size);
        coefficients[offset] = (short)component.Prediction;

        for (var k = 1; k <= 63;)
        {
            var symbol = Decode(ref bits, component.AcTable);
            if (symbol < 0)
            {
                return;
            }

            var magnitude = symbol & 15;
            var run = symbol >> 4;

            if (magnitude == 0)
            {
                if (run < 15)
                {
                    return;
                }

                k += 16;
                continue;
            }

            k += run;
            if (k > 63)
            {
                return;
            }

            coefficients[offset + ZigZag[k]] = (short)Extend(bits.Receive(magnitude), magnitude);
            k++;
        }
    }

    // [jpeg G.1.2.1] The first DC scan is the sequential DC procedure
    // over coefficients the point transform has already shifted down,
    // so what it decodes is shifted back up into place.
    private static void DecodeDcFirst(ref BitReader bits, Scan scan, Component component, int offset)
    {
        var size = Decode(ref bits, component.DcTable);
        if (size < 0)
        {
            return;
        }

        component.Prediction += size == 0 ? 0 : Extend(bits.Receive(size), size);
        component.Coefficients[offset] = (short)(component.Prediction << scan.Low);
    }

    // [jpeg G.1.2.1] Later DC scans append the next bit of each
    // coefficient with no coding of any kind around it.
    private static void DecodeDcRefine(ref BitReader bits, Scan scan, Component component, int offset)
    {
        if (bits.NextBit() == 1)
        {
            component.Coefficients[offset] |= (short)(1 << scan.Low);
        }
    }

    // [jpeg G.1.2.2] The first scan of a band of AC coefficients is the
    // sequential procedure with two changes: it runs over the band
    // rather than the whole block, and an end-of-block code may stand
    // for a run of blocks that are empty from here on.
    private static void DecodeAcFirst(ref BitReader bits, Scan scan, Component component, int offset)
    {
        if (scan.EobRun > 0)
        {
            scan.EobRun--;
            return;
        }

        var coefficients = component.Coefficients;

        for (var k = scan.Start; k <= scan.End;)
        {
            var symbol = Decode(ref bits, component.AcTable);
            if (symbol < 0)
            {
                return;
            }

            var magnitude = symbol & 15;
            var run = symbol >> 4;

            if (magnitude == 0)
            {
                if (run < 15)
                {
                    // [jpeg G.1 Table G.1] EOBn stands for a run of
                    // between 2^n and 2^(n+1) - 1 blocks, the rest of
                    // the length following in n bits. This block is the
                    // first of them.
                    scan.EobRun = (1 << run) - 1;
                    if (run > 0)
                    {
                        scan.EobRun += bits.Receive(run);
                    }

                    return;
                }

                k += 16;
                continue;
            }

            k += run;
            if (k > scan.End)
            {
                return;
            }

            coefficients[offset + ZigZag[k]] =
                (short)(Extend(bits.Receive(magnitude), magnitude) << scan.Low);
            k++;
        }
    }

    // [jpeg G.1.2.3] A later scan of a band adds one bit to what is
    // already there. A coefficient an earlier scan sent gets a
    // correction bit; a coefficient that was zero until now arrives as a
    // run of zeros and a sign. Only the coefficients that are still zero
    // count toward that run.
    private static void DecodeAcRefine(ref BitReader bits, Scan scan, Component component, int offset)
    {
        var coefficients = component.Coefficients;
        var positive = (short)(1 << scan.Low);
        var negative = (short)(-1 << scan.Low);
        var k = scan.Start;

        if (scan.EobRun > 0)
        {
            scan.EobRun--;
            CorrectBand(ref bits, coefficients, offset, k, scan.End, positive, negative);
            return;
        }

        while (k <= scan.End)
        {
            var symbol = Decode(ref bits, component.AcTable);
            if (symbol < 0)
            {
                return;
            }

            var magnitude = symbol & 15;
            var run = symbol >> 4;
            short arriving = 0;

            if (magnitude == 0)
            {
                if (run < 15)
                {
                    scan.EobRun = (1 << run) - 1;
                    if (run > 0)
                    {
                        scan.EobRun += bits.Receive(run);
                    }

                    // The rest of the band holds nothing new, but the
                    // coefficients already in it still take their
                    // correction bits.
                    CorrectBand(ref bits, coefficients, offset, k, scan.End, positive, negative);
                    return;
                }
            }
            else
            {
                // [jpeg G.1.2.3] In a refining scan the magnitude can
                // only be one, and a single bit gives its sign.
                arriving = bits.NextBit() == 1 ? positive : negative;
            }

            while (k <= scan.End)
            {
                var place = offset + ZigZag[k];

                if (coefficients[place] != 0)
                {
                    Correct(ref bits, coefficients, place, positive, negative);
                }
                else if (run == 0)
                {
                    if (arriving != 0)
                    {
                        coefficients[place] = arriving;
                    }

                    k++;
                    break;
                }
                else
                {
                    run--;
                }

                k++;
            }
        }
    }

    // [jpeg G.1.2.3] Every coefficient an earlier scan already sent
    // carries one bit here: a one adds the bit being refined to its
    // magnitude, a zero leaves it alone.
    private static void Correct(ref BitReader bits, short[] coefficients, int place, short positive, short negative)
    {
        if (bits.NextBit() == 1 && (coefficients[place] & positive) == 0)
        {
            coefficients[place] += coefficients[place] >= 0 ? positive : negative;
        }
    }

    private static void CorrectBand(ref BitReader bits, short[] coefficients, int offset, int from, int to, short positive, short negative)
    {
        for (var k = from; k <= to; k++)
        {
            var place = offset + ZigZag[k];
            if (coefficients[place] != 0)
            {
                Correct(ref bits, coefficients, place, positive, negative);
            }
        }
    }

    // [jpeg F.2.2.3] Reading a Huffman code one bit at a time, widening
    // the code until it falls inside the range of codes of that length.
    private static int Decode(ref BitReader bits, HuffmanTable? table)
    {
        if (table is null)
        {
            return -1;
        }

        var code = 0;

        for (var length = 1; length <= 16; length++)
        {
            code = (code << 1) | bits.NextBit();

            if (code <= table.MaxCode[length])
            {
                return table.Values[table.ValuePointer[length] + code - table.MinCode[length]];
            }
        }

        return -1;
    }

    // [jpeg F.2.2.1 Figure F.12] A value of the given number of bits is
    // negative when its top bit is clear, and the whole range below that
    // point stands for the negative half.
    private static int Extend(int value, int size) =>
        value < 1 << (size - 1) ? value - (1 << size) + 1 : value;

    /// <summary>
    /// Turns the coefficients every scan has left behind into pixels.
    /// </summary>
    private static Pixels? Compose(Frame frame, int[][] quantization, int colorTransform)
    {
        foreach (var component in frame.Components)
        {
            if (quantization[component.QuantizationTable] is not { } table)
            {
                // [jpeg B.2.4.1] A component whose table was never
                // defined cannot be reconstructed.
                return null;
            }

            component.Samples = Reconstruct(component, table);
        }

        var pixels = new Pixels(frame.Width, frame.Height, new byte[frame.Width * frame.Height * 4]);
        var rgba = pixels.Rgba;

        if (frame.Components.Length == 1)
        {
            Gray(frame, rgba);
        }
        else
        {
            Color(frame, rgba, Transformed(frame, colorTransform));
        }

        return pixels;
    }

    /// <summary>
    /// Whether a three component picture is a color difference that has
    /// to be turned back into colors.
    /// </summary>
    private static bool Transformed(Frame frame, int colorTransform)
    {
        // Adobe's segment says outright, and is believed when present.
        if (colorTransform >= 0)
        {
            return colorTransform != 0;
        }

        // Otherwise the component identifiers tell: a file that labels
        // them 'R', 'G', and 'B' means them literally, and anything else
        // is the usual color difference.
        return !(frame.Components[0].Id == 'R'
            && frame.Components[1].Id == 'G'
            && frame.Components[2].Id == 'B');
    }

    // [jpeg A.3.4] Dequantizing, then [jpeg A.3.3] the inverse
    // transform, then [jpeg A.3.1] the shift back to unsigned samples,
    // block by block into one plane for the whole component.
    private static byte[] Reconstruct(Component component, int[] quantization)
    {
        var width = component.BlocksPerLineForMcu * 8;
        var samples = new byte[width * component.BlocksPerColumnForMcu * 8];
        var block = new float[64];
        var pass = new float[64];

        for (var blockRow = 0; blockRow < component.BlocksPerColumnForMcu; blockRow++)
        {
            for (var blockColumn = 0; blockColumn < component.BlocksPerLineForMcu; blockColumn++)
            {
                var offset = Offset(component, blockRow, blockColumn);

                for (var i = 0; i < 64; i++)
                {
                    block[i] = component.Coefficients[offset + i] * quantization[i];
                }

                Inverse(block, pass);

                for (var y = 0; y < 8; y++)
                {
                    var at = (((blockRow * 8) + y) * width) + (blockColumn * 8);
                    for (var x = 0; x < 8; x++)
                    {
                        samples[at + x] = Clamp(block[(y * 8) + x] + 128f);
                    }
                }
            }
        }

        return samples;
    }

    // [jpeg A.3.3] The inverse transform, taken a row at a time and then
    // a column at a time, which is the same sum as the definition since
    // the cosines separate.
    private static void Inverse(float[] block, float[] pass)
    {
        // Most rows and columns of most blocks hold nothing but their
        // first coefficient, and such a row is flat, so the sum is worth
        // stepping around rather than working out eight times over.
        const float Flat = 0.35355339f;

        for (var y = 0; y < 8; y++)
        {
            var row = y * 8;

            if (Empty(block, row, 1))
            {
                var value = block[row] * Flat;
                for (var x = 0; x < 8; x++)
                {
                    pass[row + x] = value;
                }

                continue;
            }

            for (var x = 0; x < 8; x++)
            {
                var sum = 0f;
                for (var u = 0; u < 8; u++)
                {
                    sum += Cosines[(u * 8) + x] * block[row + u];
                }

                pass[row + x] = sum * 0.5f;
            }
        }

        for (var x = 0; x < 8; x++)
        {
            if (Empty(pass, x, 8))
            {
                var value = pass[x] * Flat;
                for (var y = 0; y < 8; y++)
                {
                    block[(y * 8) + x] = value;
                }

                continue;
            }

            for (var y = 0; y < 8; y++)
            {
                var sum = 0f;
                for (var v = 0; v < 8; v++)
                {
                    sum += Cosines[(v * 8) + y] * pass[(v * 8) + x];
                }

                block[(y * 8) + x] = sum * 0.5f;
            }
        }
    }

    /// <summary>
    /// Whether the seven values after the first of a row or a column are
    /// all zero, which makes what the transform does to it flat.
    /// </summary>
    private static bool Empty(float[] values, int at, int step)
    {
        for (var i = 1; i < 8; i++)
        {
            if (values[at + (i * step)] != 0f)
            {
                return false;
            }
        }

        return true;
    }

    private static float[] BuildCosines()
    {
        var cosines = new float[64];

        for (var u = 0; u < 8; u++)
        {
            var scale = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
            for (var x = 0; x < 8; x++)
            {
                cosines[(u * 8) + x] =
                    (float)(scale * Math.Cos(((2 * x) + 1) * u * Math.PI / 16.0));
            }
        }

        return cosines;
    }

    private static void Gray(Frame frame, byte[] rgba)
    {
        var component = frame.Components[0];
        var width = component.BlocksPerLineForMcu * 8;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var value = component.Samples![(y * width) + x];
                var at = ((y * frame.Width) + x) * 4;
                rgba[at] = value;
                rgba[at + 1] = value;
                rgba[at + 2] = value;
                rgba[at + 3] = 255;
            }
        }
    }

    private static void Color(Frame frame, byte[] rgba, bool transformed)
    {
        var first = frame.Components[0];
        var second = frame.Components[1];
        var third = frame.Components[2];

        var columns = new Steps[3];
        var rows = new Steps[3];

        for (var i = 0; i < 3; i++)
        {
            var component = frame.Components[i];
            columns[i] = new Steps(
                frame.Width, component.Horizontal, frame.MaxHorizontal, component.SampleWidth, 1);
            rows[i] = new Steps(
                frame.Height, component.Vertical, frame.MaxVertical, component.SampleHeight,
                component.BlocksPerLineForMcu * 8);
        }

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var a = Sample(first.Samples!, columns[0], rows[0], x, y);
                var b = Sample(second.Samples!, columns[1], rows[1], x, y);
                var c = Sample(third.Samples!, columns[2], rows[2], x, y);

                var at = ((y * frame.Width) + x) * 4;

                if (transformed)
                {
                    // [jfif 7] The color difference a JPEG is usually
                    // written in, turned back into red, green, and blue.
                    rgba[at] = Clamp(a + (1.402f * (c - 128f)));
                    rgba[at + 1] = Clamp(a - (0.344136f * (b - 128f)) - (0.714136f * (c - 128f)));
                    rgba[at + 2] = Clamp(a + (1.772f * (b - 128f)));
                }
                else
                {
                    rgba[at] = Clamp(a);
                    rgba[at + 1] = Clamp(b);
                    rgba[at + 2] = Clamp(c);
                }

                rgba[at + 3] = 255;
            }
        }
    }

    /// <summary>
    /// One sample of a component at a pixel of the picture, drawn on a
    /// straight line between the samples on either side of it.
    /// </summary>
    private static float Sample(byte[] samples, Steps columns, Steps rows, int x, int y)
    {
        var left = columns.Near[x];
        var right = columns.Far[x];
        var top = rows.Near[y];
        var bottom = rows.Far[y];
        var across = columns.Blend[x];
        var down = rows.Blend[y];

        var upper = samples[top + left] + ((samples[top + right] - samples[top + left]) * across);
        var lower = samples[bottom + left] + ((samples[bottom + right] - samples[bottom + left]) * across);

        return upper + ((lower - upper) * down);
    }

    // [jpeg A.3.1] A sample outside the range the precision allows is
    // brought back to its nearest end.
    private static byte Clamp(float value) =>
        value <= 0f ? (byte)0 : value >= 255f ? (byte)255 : (byte)(value + 0.5f);

    private static int Spread(int size, int step) => (size + step - 1) / step;

    /// <summary>
    /// Where along one axis of a component each pixel of the picture
    /// falls, as the two samples on either side of it and how far it
    /// lies between them.
    /// </summary>
    /// <remarks>
    /// [jpeg A.1.1] A component sent at less than the picture's
    /// resolution has a sample for every few pixels, and a sample stands
    /// for the middle of the run of pixels it covers, not for the first
    /// of them. Reading between the two nearest samples on that
    /// understanding is what keeps the color of a subsampled picture
    /// from stepping in blocks at an edge. The specification says
    /// nothing about how to do this, so the plainest reading is used.
    /// </remarks>
    private readonly struct Steps
    {
        public Steps(int pixels, int factor, int most, int samples, int stride)
        {
            Near = new int[pixels];
            Far = new int[pixels];
            Blend = new float[pixels];

            for (var i = 0; i < pixels; i++)
            {
                var middle = (((i + 0.5f) * factor) / most) - 0.5f;
                var before = (int)Math.Floor(middle);

                Near[i] = Math.Clamp(before, 0, samples - 1) * stride;
                Far[i] = Math.Clamp(before + 1, 0, samples - 1) * stride;
                Blend[i] = middle - before;
            }
        }

        /// <summary>The sample at or before each pixel.</summary>
        public int[] Near { get; }

        /// <summary>The sample after it.</summary>
        public int[] Far { get; }

        /// <summary>How far the pixel lies between the two.</summary>
        public float[] Blend { get; }
    }

    /// <summary>
    /// [jpeg B.2.2] A picture as its frame header describes it.
    /// </summary>
    private sealed class Frame(int width, int height, bool progressive, Component[] components)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public bool Progressive { get; } = progressive;

        public Component[] Components { get; } = components;

        public int MaxHorizontal { get; init; }

        public int MaxVertical { get; init; }

        public int McusPerLine { get; init; }

        public int McusPerColumn { get; init; }
    }

    /// <summary>
    /// One component of a picture, its coefficients, and the tables and
    /// running state the scan being read uses.
    /// </summary>
    private sealed class Component(int id, int horizontal, int vertical, int quantizationTable)
    {
        public int Id { get; } = id;

        public int Horizontal { get; } = horizontal;

        public int Vertical { get; } = vertical;

        public int QuantizationTable { get; } = quantizationTable;

        /// <summary>
        /// [jpeg A.1.1] How many samples across and down this component
        /// really has, which is fewer than the padded plane its blocks
        /// are decoded into.
        /// </summary>
        public int SampleWidth { get; set; }

        public int SampleHeight { get; set; }

        public int BlocksPerLine { get; set; }

        public int BlocksPerColumn { get; set; }

        public int BlocksPerLineForMcu { get; set; }

        public int BlocksPerColumnForMcu { get; set; }

        public short[] Coefficients { get; set; } = [];

        public byte[]? Samples { get; set; }

        public HuffmanTable? DcTable { get; set; }

        public HuffmanTable? AcTable { get; set; }

        /// <summary>
        /// [jpeg A.3.5] The DC coefficient of the block before this one,
        /// which the next difference is added to.
        /// </summary>
        public int Prediction { get; set; }
    }

    /// <summary>
    /// [jpeg B.2.3] A scan as its header describes it.
    /// </summary>
    private sealed class Scan(Component[] components, int start, int end, int high, int low)
    {
        public Component[] Components { get; } = components;

        /// <summary>The first coefficient of the band, Ss.</summary>
        public int Start { get; } = start;

        /// <summary>The last coefficient of the band, Se.</summary>
        public int End { get; } = end;

        /// <summary>The bit an earlier scan of the band sent, Ah.</summary>
        public int High { get; } = high;

        /// <summary>The bit this scan sends, Al.</summary>
        public int Low { get; } = low;

        public bool Progressive { get; init; }

        /// <summary>
        /// [jpeg G.1.2.2] How many more blocks the end-of-band run that
        /// is under way covers.
        /// </summary>
        public int EobRun { get; set; }
    }

    /// <summary>
    /// [jpeg F.2.2.3] A Huffman table in the three arrays the decoding
    /// procedure reads: the smallest and largest code of each length,
    /// and where in the values the codes of that length begin.
    /// </summary>
    private sealed class HuffmanTable
    {
        public HuffmanTable(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            Values = values.ToArray();

            // [jpeg C.2] The codes are handed out shortest first, each
            // one after the one before it, and the whole run shifted
            // left at every step up in length.
            var code = 0;
            var at = 0;

            for (var length = 1; length <= 16; length++)
            {
                if (counts[length - 1] == 0)
                {
                    // [jpeg F.2.2.3 Figure F.15] A length with no codes
                    // is marked so that no code can ever match it.
                    MaxCode[length] = -1;
                }
                else
                {
                    ValuePointer[length] = at;
                    MinCode[length] = code;
                    at += counts[length - 1];
                    code += counts[length - 1];
                    MaxCode[length] = code - 1;
                }

                code <<= 1;
            }
        }

        public byte[] Values { get; }

        public int[] MinCode { get; } = new int[17];

        public int[] MaxCode { get; } = new int[17];

        public int[] ValuePointer { get; } = new int[17];
    }

    /// <summary>
    /// [jpeg F.2.2.5] The bits of an entropy-coded segment, with the
    /// zero bytes that keep an X'FF' from looking like a marker taken
    /// back out, and a stop at the marker that ends the segment.
    /// </summary>
    private ref struct BitReader(ReadOnlySpan<byte> file, int at)
    {
        private readonly ReadOnlySpan<byte> _file = file;
        private int _at = at;
        private int _byte;
        private int _count;
        private bool _stopped;

        /// <summary>Where the next byte would be read from.</summary>
        public readonly int Position => _at;

        public int NextBit()
        {
            if (_count == 0 && !Fill())
            {
                // Past the end of the segment there are no more bits.
                // Reading zeros lets the blocks that are left finish as
                // empty ones, so a truncated picture comes out partly
                // drawn rather than not at all.
                return 0;
            }

            _count--;
            return (_byte >> _count) & 1;
        }

        /// <summary>
        /// [jpeg F.2.2.4] The next few bits as a number, most
        /// significant first.
        /// </summary>
        public int Receive(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | NextBit();
            }

            return value;
        }

        /// <summary>
        /// Steps over the restart marker between two entropy-coded
        /// segments, and says whether one was there.
        /// </summary>
        public bool SkipRestart()
        {
            _count = 0;
            _stopped = false;

            // [jpeg B.1.1.5] The padding that fills out the last byte of
            // a segment has already been passed over by clearing the
            // count; the marker follows it.
            while (_at + 1 < _file.Length && _file[_at] == 0xFF && _file[_at + 1] == 0xFF)
            {
                _at++;
            }

            if (_at + 1 < _file.Length && _file[_at] == 0xFF
                && _file[_at + 1] >= 0xD0 && _file[_at + 1] <= 0xD7)
            {
                _at += 2;
                return true;
            }

            return false;
        }

        private bool Fill()
        {
            if (_stopped || _at >= _file.Length)
            {
                _stopped = true;
                return false;
            }

            var value = _file[_at++];

            if (value == 0xFF)
            {
                // [jpeg B.1.1.5] An X'FF' in the data is written with a
                // zero byte after it. Anything else after it is a real
                // marker, which ends the segment and is left where it
                // is for the reader that comes next.
                if (_at < _file.Length && _file[_at] == 0x00)
                {
                    _at++;
                }
                else
                {
                    _at--;
                    _stopped = true;
                    return false;
                }
            }

            _byte = value;
            _count = 8;
            return true;
        }
    }
}
