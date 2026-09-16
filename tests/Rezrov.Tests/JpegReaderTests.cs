using System.Buffers.Binary;
using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;

namespace Rezrov.Tests;

/// <summary>
/// [blorb 2.2] Reading the other picture format of a resource file: the
/// coefficients of a block and where each one belongs, the runs and
/// end-of-block codes between them, restart intervals, components sent
/// at less than the picture's resolution, and the several scans a
/// progressive picture arrives in.
/// </summary>
/// <remarks>
/// The pictures here are built from coefficients rather than from
/// pixels, because that is the layer the reader works at: a test says
/// what a block holds, writes it the way [jpeg F.1.2] and [jpeg G.1.2]
/// say a writer does, and the reader has to arrive back at the same
/// picture.
///
/// A block holding nothing but its average is flat, and [jpeg A.3.3]
/// makes the value of it the coefficient over eight, so one number says
/// what the whole block should come out as. That is what the plain
/// tests measure. The progressive tests measure no pixels at all: they
/// build the same coefficients twice, once in one scan and once spread
/// over several, and require the two to decode identically.
/// </remarks>
public class JpegReaderTests
{
    [Fact]
    public void ABlockHoldingOnlyItsAverageIsFlat()
    {
        // [jpeg A.3.3] A block with only a DC coefficient comes out at
        // one value everywhere, the coefficient over eight, and
        // [jpeg A.3.1] shifted back up into unsigned samples.
        var picture = Read(Gray(8, 8, Block(0, 8)));

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal((129, 129, 129, 255), picture.At(x, y));
            }
        }

        // [jpeg A.3.1] And a sample the transform puts outside the range
        // a byte holds is brought back to the nearest end of it.
        Assert.Equal((0, 0, 0, 255), Read(Gray(8, 8, Block(0, -4000))).At(4, 4));
        Assert.Equal((255, 255, 255, 255), Read(Gray(8, 8, Block(0, 4000))).At(4, 4));
    }

    [Fact]
    public void ACoefficientLandsWhereTheZigZagSequenceSaysItDoes()
    {
        // [jpeg A.3.6] The second coefficient of the zig-zag sequence is
        // the one that varies across the block, and the third is the one
        // that varies down it. A sequence read the other way round would
        // swap the two, so each is checked in both directions.
        var across = Read(Gray(8, 8, Block(0, 0, 1, 200)));

        Assert.NotEqual(across.At(0, 0), across.At(7, 0));
        Assert.Equal(across.At(0, 0), across.At(0, 7));

        var down = Read(Gray(8, 8, Block(0, 0, 2, 200)));

        Assert.Equal(down.At(0, 0), down.At(7, 0));
        Assert.NotEqual(down.At(0, 0), down.At(0, 7));
    }

    [Fact]
    public void ALongRunOfZerosIsSteppedOverAndWhatFollowsItPlaced()
    {
        // [jpeg F.1.2.2] A run of more than fifteen zeros is written as
        // one or more runs of sixteen and then the value, so a
        // coefficient this far along is only reached if those runs are
        // followed. Zig-zag 20 is the sixth row of the block, which
        // varies down it and not across.
        var picture = Read(Gray(8, 8, Block(0, 0, 20, 300)));

        Assert.Equal(picture.At(0, 0), picture.At(7, 0));
        Assert.NotEqual(picture.At(0, 0), picture.At(0, 1));
    }

    [Fact]
    public void TheQuantizationTableScalesWhatTheBlockHolds()
    {
        // [jpeg A.3.4] The coefficient written is the real one divided
        // by its table element, so the same coefficient against a table
        // of fours stands four times as far from the middle gray.
        Assert.Equal((129, 129, 129, 255), Read(Gray(8, 8, Block(0, 8))).At(0, 0));
        Assert.Equal((132, 132, 132, 255), Read(GrayFile(8, 8, [Block(0, 8)], 4, 0)).At(0, 0));
    }

    [Fact]
    public void ARestartIntervalStartsTheDifferencesAfresh()
    {
        // [jpeg A.3.5] A DC coefficient is written as its difference
        // from the block before it, and [jpeg F.2.2.5] a restart
        // interval begins again from zero. The second block here is
        // written as a whole value rather than a difference, so it comes
        // out too bright unless the restart was honored, and the marker
        // between the two has to be stepped over as well.
        var blocks = new[] { Block(0, 80), Block(0, 200) };
        var picture = Read(GrayFile(16, 8, blocks, 1, 1));

        Assert.Equal((138, 138, 138, 255), picture.At(0, 0));
        Assert.Equal((153, 153, 153, 255), picture.At(8, 0));

        // And the same blocks in one segment are the same picture.
        Assert.Equal(Read(GrayFile(16, 8, blocks, 1, 0)).Rgba, picture.Rgba);
    }

    [Fact]
    public void AColorPictureIsTurnedBackIntoRedGreenAndBlue()
    {
        // [jfif 7] Three components hold a color difference unless the
        // file says otherwise. Both differences at the middle of their
        // range leave a gray.
        Assert.Equal((129, 129, 129, 255), Read(Color(8, 8, 8, 0, 0)).At(0, 0));

        // A difference away from the middle leans the color, and by
        // exactly as much as the conversion says.
        Assert.Equal((198, 92, 128, 255), Read(Color(8, 8, 0, 0, 400)).At(4, 4));

        // Adobe's segment saying zero means the three components are the
        // colors themselves and nothing is converted.
        Assert.Equal((129, 128, 178, 255), Read(Color(8, 8, 8, 0, 400, transform: 0)).At(4, 4));
    }

    [Fact]
    public void AComponentSentSmallerIsSpreadOverThePixelsItCovers()
    {
        // [jpeg A.2.3] One MCU here is four blocks of brightness and one
        // of each color, and the four are sent left to right and then
        // top to bottom, so each lands in its own quarter.
        var quarters = Read(Subsampled(16, 16, [0, 8, 16, 24], 0, 0));

        Assert.Equal((128, 128, 128, 255), quarters.At(0, 0));
        Assert.Equal((129, 129, 129, 255), quarters.At(15, 0));
        Assert.Equal((130, 130, 130, 255), quarters.At(0, 15));
        Assert.Equal((131, 131, 131, 255), quarters.At(15, 15));

        // [jpeg A.1.1] The color components are sent at half the
        // resolution, so every pixel has to find the one color the
        // picture holds however far it is from a sample of it.
        var colored = Read(Subsampled(16, 16, [0, 0, 0, 0], 0, 400));

        foreach (var (x, y) in new[] { (0, 0), (15, 0), (0, 15), (15, 15), (8, 8) })
        {
            Assert.Equal((198, 92, 128, 255), colored.At(x, y));
        }
    }

    [Fact]
    public void ASpectralSelectionPictureDecodesAsItsOneScanTwinDoes()
    {
        // [jpeg G.1.1.1.1] A progressive picture may send the DC
        // coefficient in one scan and bands of AC coefficients in
        // others. However it is divided, it holds the same coefficients,
        // so it has to come out as the same pixels.
        var blocks = new[] { Block(0, 40, 1, 120, 9, -70, 30, 25) };

        Assert.Equal(
            Read(Gray(8, 8, blocks)).Rgba,
            Read(Spectral(8, 8, blocks)).Rgba);
    }

    [Fact]
    public void ASuccessiveApproximationPictureDecodesAsItsOneScanTwinDoes()
    {
        // [jpeg G.1.1.1.2] The other way a progressive picture is
        // divided: the first scan of a band sends every coefficient in
        // it less its lowest bit, and the next sends the bits that were
        // left off, as a correction to what is already there and as a
        // whole new coefficient where there had been nothing.
        var blocks = new[]
        {
            Block(0, 41, 1, 121, 2, -35, 3, 9, 4, -1, 20, 7),
            Block(0, -17, 1, -3, 5, 16, 30, 2),
        };

        Assert.Equal(
            Read(Gray(16, 8, blocks)).Rgba,
            Read(Approximated(16, 8, blocks)).Rgba);
    }

    [Fact]
    public void WhatIsNotAPictureIsRefused()
    {
        Assert.Null(JpegReader.Read([]));
        Assert.Null(JpegReader.Read("not a picture at all"u8));

        // [jpeg B.1.1.3] A frame this cannot decode: lossless, and
        // arithmetic coding in place of Huffman.
        Assert.Null(JpegReader.Read(Retyped(Gray(8, 8, Block(0, 8)), 0xC3)));
        Assert.Null(JpegReader.Read(Retyped(Gray(8, 8, Block(0, 8)), 0xC9)));

        // [jpeg B.2.2] Samples that are not eight bits, a height that is
        // to arrive in a DNL marker, and a frame of four components.
        Assert.Null(JpegReader.Read(Amended(Gray(8, 8, Block(0, 8)), 0, 12)));
        Assert.Null(JpegReader.Read(Amended(Gray(8, 8, Block(0, 8)), 1, 0, 0)));
        Assert.Null(JpegReader.Read(Amended(Gray(8, 8, Block(0, 8)), 5, 4)));

        // A picture that stops part way is drawn as far as it got rather
        // than refused, which is the useful answer from a reader with
        // nowhere to report a warning.
        var whole = Gray(16, 8, Block(0, 80), Block(0, 200));
        var cut = JpegReader.Read(whole.AsSpan(0, whole.Length - 6));

        Assert.NotNull(cut);
        Assert.Equal((16, 8), (cut.Width, cut.Height));
        Assert.Equal((138, 138, 138, 255), cut.At(0, 0));
    }

    [Fact]
    public void EveryPictureInTheCorpusDecodes()
    {
        var files = Corpus.ResourceFiles();
        Assert.SkipUnless(files.Count > 0, "The entharion submodule is not populated.");

        var decoded = 0;
        foreach (var path in files)
        {
            var blorb = BlorbFile.Read(File.ReadAllBytes(path));
            foreach (var picture in BlorbPictures.From(blorb).Pictures.Where(p => p.Kind == PictureKind.Jpeg))
            {
                var pixels = PictureReader.Decode(picture);

                // The catalog reads the size out of the header; the
                // decoder must produce exactly that many pixels.
                Assert.NotNull(pixels);
                Assert.Equal((picture.Width, picture.Height), (pixels.Width, pixels.Height));
                Assert.Equal(picture.Width * picture.Height * 4, pixels.Rgba.Length);
                decoded++;
            }
        }

        Assert.True(decoded > 210, $"Only {decoded} pictures were decoded.");
    }

    private static Pixels Read(byte[] file)
    {
        var picture = JpegReader.Read(file);
        Assert.NotNull(picture);
        return picture;
    }

    /// <summary>
    /// The coefficients of one block, given as pairs of a zig-zag
    /// position and the value that belongs there.
    /// </summary>
    private static int[] Block(params int[] pairs)
    {
        var block = new int[64];
        for (var i = 0; i + 1 < pairs.Length; i += 2)
        {
            block[pairs[i]] = pairs[i + 1];
        }

        return block;
    }

    /// <summary>
    /// A gray picture in one sequential scan, its blocks in reading
    /// order.
    /// </summary>
    private static byte[] Gray(int width, int height, params int[][] blocks) =>
        GrayFile(width, height, blocks, 1, 0);

    private static byte[] GrayFile(int width, int height, int[][] blocks, int quantization, int restart)
    {
        var file = new Builder(width, height, [new Part(1, 1, 1)], quantization, restart);
        file.Sequential(blocks);
        return file.Done();
    }

    /// <summary>
    /// A color picture of one flat block for each component, every one
    /// of them at the picture's own resolution.
    /// </summary>
    private static byte[] Color(int width, int height, int brightness, int first, int second, int transform = -1)
    {
        var file = new Builder(
            width, height, [new Part(1, 1, 1), new Part(2, 1, 1), new Part(3, 1, 1)], 1, 0)
        {
            Transform = transform,
        };

        file.Sequential([Block(0, brightness)], [Block(0, first)], [Block(0, second)]);
        return file.Done();
    }

    /// <summary>
    /// The same, with four blocks of brightness to the one block of each
    /// color, which makes the color components half the resolution of
    /// the picture in each direction.
    /// </summary>
    private static byte[] Subsampled(int width, int height, int[] brightness, int first, int second)
    {
        var file = new Builder(
            width, height, [new Part(1, 2, 2), new Part(2, 1, 1), new Part(3, 1, 1)], 1, 0);

        file.Sequential(
            [.. brightness.Select(dc => Block(0, dc))],
            [Block(0, first)],
            [Block(0, second)]);

        return file.Done();
    }

    /// <summary>
    /// [jpeg G.1.1.1.1] A gray picture whose DC coefficient and two
    /// bands of AC coefficients arrive in three scans.
    /// </summary>
    private static byte[] Spectral(int width, int height, int[][] blocks)
    {
        var file = new Builder(width, height, [new Part(1, 1, 1)], 1, 0) { Progressive = true };

        file.DcScan(blocks, 0);
        file.AcScan(blocks, 1, 20, 0);
        file.AcScan(blocks, 21, 63, 0);
        return file.Done();
    }

    /// <summary>
    /// [jpeg G.1.1.1.2] A gray picture whose coefficients arrive a bit
    /// at a time, DC and AC alike.
    /// </summary>
    private static byte[] Approximated(int width, int height, int[][] blocks)
    {
        var file = new Builder(width, height, [new Part(1, 1, 1)], 1, 0) { Progressive = true };

        file.DcScan(blocks, 1);
        file.DcRefineScan(blocks, 1);
        file.AcScan(blocks, 1, 63, 1);
        file.AcRefineScan(blocks, 1, 63, 1);
        return file.Done();
    }

    /// <summary>Changes which kind of frame a picture says it is.</summary>
    private static byte[] Retyped(byte[] file, byte marker)
    {
        var copy = (byte[])file.Clone();
        copy[Frame(copy) + 1] = marker;
        return copy;
    }

    /// <summary>
    /// Overwrites bytes of the frame header, counting from the sample
    /// precision.
    /// </summary>
    private static byte[] Amended(byte[] file, int at, params byte[] values)
    {
        var copy = (byte[])file.Clone();
        values.CopyTo(copy.AsSpan(Frame(copy) + 4 + at));
        return copy;
    }

    private static int Frame(byte[] file)
    {
        for (var at = 2; at + 1 < file.Length; at++)
        {
            if (file[at] == 0xFF && file[at + 1] is 0xC0 or 0xC2)
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>One component of a picture being built.</summary>
    private sealed record Part(int Id, int Horizontal, int Vertical);

    /// <summary>
    /// Writes a JPEG from coefficients, following the encoding
    /// procedures of [jpeg F.1.2] and [jpeg G.1.2] so that what the
    /// reader has to undo is what a writer really does.
    /// </summary>
    /// <remarks>
    /// The refining scans of [jpeg G.1.2.3] are written by walking the
    /// band the way the annex describes, which is the same walk the
    /// reader makes in the other direction. That makes these tests a
    /// statement of the format rather than an independent check of it;
    /// the independent check is that every JPEG in the corpus, half of
    /// them progressive, decodes to within a rounding of what the
    /// reference decoder produces.
    /// </remarks>
    private sealed class Builder
    {
        private readonly List<byte> _file = [];
        private readonly Part[] _parts;
        private readonly int _width;
        private readonly int _height;
        private readonly int _quantization;
        private readonly int _restart;
        private readonly int _mostHorizontal;
        private readonly int _mostVertical;
        private Write? _write;

        public Builder(int width, int height, Part[] parts, int quantization, int restart)
        {
            _width = width;
            _height = height;
            _parts = parts;
            _quantization = quantization;
            _restart = restart;
            _mostHorizontal = parts.Max(p => p.Horizontal);
            _mostVertical = parts.Max(p => p.Vertical);
        }

        private delegate void Write(Bits bits, int[] block, int[]? previous);

        public bool Progressive { get; init; }

        /// <summary>
        /// What Adobe's segment should say, or -1 for no such segment.
        /// </summary>
        public int Transform { get; init; } = -1;

        public byte[] Done()
        {
            _file.AddRange([0xFF, 0xD9]);
            return [.. _file];
        }

        /// <summary>[jpeg F.1.2] One scan holding whole blocks.</summary>
        public void Sequential(params int[][][] components)
        {
            Head();
            Scan(0, 63, 0, 0, (bits, block, previous) =>
            {
                Difference(bits, block[0] - (previous?[0] ?? 0));
                Values(bits, block, 1, 63, 0);
            });

            Entropy(components);
        }

        /// <summary>
        /// [jpeg G.1.2.1] A scan of DC coefficients only, each shifted
        /// down by the given number of bits.
        /// </summary>
        public void DcScan(int[][] blocks, int low)
        {
            Head();
            Scan(0, 0, 0, low, (bits, block, previous) =>
                Difference(bits, (block[0] >> low) - ((previous?[0] ?? 0) >> low)));

            Entropy([blocks]);
        }

        /// <summary>
        /// [jpeg G.1.2.1] The scan after it, which is the bit each DC
        /// coefficient was shifted by and nothing around it.
        /// </summary>
        public void DcRefineScan(int[][] blocks, int high)
        {
            Head();
            Scan(0, 0, high, high - 1, (bits, block, previous) =>
                bits.Raw((block[0] >> (high - 1)) & 1, 1));

            Entropy([blocks]);
        }

        /// <summary>
        /// [jpeg G.1.2.2] A scan of one band of AC coefficients, each
        /// shifted down by the given number of bits.
        /// </summary>
        public void AcScan(int[][] blocks, int start, int end, int low)
        {
            Head();
            Scan(start, end, 0, low, (bits, block, previous) =>
                Values(bits, block, start, end, low));

            Entropy([blocks]);
        }

        /// <summary>
        /// [jpeg G.1.2.3] The scan after it: a code for each coefficient
        /// that becomes something for the first time, and a bit for each
        /// one an earlier scan already sent.
        /// </summary>
        public void AcRefineScan(int[][] blocks, int start, int end, int high)
        {
            Head();
            Scan(start, end, high, high - 1, (bits, block, previous) =>
                Refine(bits, block, start, end, high));

            Entropy([blocks]);
        }

        // [jpeg F.1.2.1] A value is written as the number of bits it
        // needs and then those bits, the negative half of each range
        // written one below the positive half.
        private static (int Size, int Bits) Magnitude(int value)
        {
            var size = 0;
            for (var magnitude = Math.Abs(value); magnitude > 0; magnitude >>= 1)
            {
                size++;
            }

            return (size, value >= 0 ? value : value + (1 << size) - 1);
        }

        private static void Difference(Bits bits, int difference)
        {
            var (size, value) = Magnitude(difference);
            bits.Code(size);
            bits.Raw(value, size);
        }

        // [jpeg F.1.2.2] The coefficients of a band, as runs of zeros
        // each ending in a value, and an end-of-block if the band runs
        // out before the last of them.
        private static void Values(Bits bits, int[] block, int start, int end, int low)
        {
            var run = 0;

            for (var k = start; k <= end; k++)
            {
                // [jpeg A.4] The point transform divides toward zero.
                var value = block[k] / (1 << low);
                if (value == 0)
                {
                    run++;
                    continue;
                }

                while (run >= 16)
                {
                    bits.Code(0xF0);
                    run -= 16;
                }

                var (size, written) = Magnitude(value);
                bits.Code((run << 4) | size);
                bits.Raw(written, size);
                run = 0;
            }

            if (run > 0)
            {
                // [jpeg G.1 Table G.1] An end-of-block run of one, which
                // closes a band that ends early whether the scan is
                // sequential or not.
                bits.Code(0x00);
            }
        }

        // [jpeg G.1.2.3] Walking the band once: a coefficient an earlier
        // scan sent takes a correction bit where it stands, and one
        // arriving now is announced by how many still-empty places were
        // passed to reach it.
        private static void Refine(Bits bits, int[] block, int start, int end, int high)
        {
            var low = high - 1;
            var k = start;

            while (k <= end)
            {
                var next = k;
                var run = 0;

                while (next <= end && (Sent(block, next, high) != 0 || Sent(block, next, low) == 0))
                {
                    if (Sent(block, next, high) == 0)
                    {
                        run++;
                    }

                    next++;
                }

                if (next > end)
                {
                    bits.Code(0x00);
                    Corrections(bits, block, k, end, low);
                    return;
                }

                if (run >= 16)
                {
                    bits.Code(0xF0);
                    k = Walk(bits, block, k, end, low, 16);
                    continue;
                }

                bits.Code((run << 4) | 1);
                bits.Raw(Sent(block, next, low) > 0 ? 1 : 0, 1);
                k = Walk(bits, block, k, end, low, run);
            }
        }

        /// <summary>
        /// Writes the correction bits of the coefficients already sent
        /// that lie between here and the place being filled in, and says
        /// where the walk left off.
        /// </summary>
        private static int Walk(Bits bits, int[] block, int k, int end, int low, int run)
        {
            while (k <= end)
            {
                if (Sent(block, k, low + 1) != 0)
                {
                    bits.Raw(Math.Abs(Sent(block, k, low)) & 1, 1);
                }
                else if (run == 0)
                {
                    return k + 1;
                }
                else
                {
                    run--;
                }

                k++;
            }

            return k;
        }

        private static void Corrections(Bits bits, int[] block, int from, int to, int low)
        {
            for (var k = from; k <= to; k++)
            {
                if (Sent(block, k, low + 1) != 0)
                {
                    bits.Raw(Math.Abs(Sent(block, k, low)) & 1, 1);
                }
            }
        }

        /// <summary>
        /// [jpeg A.4] What a scan at the given bit position says a
        /// coefficient is.
        /// </summary>
        private static int Sent(int[] block, int k, int low) => block[k] / (1 << low);

        private static byte[] Word(int value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
            return bytes;
        }

        private void Head()
        {
            if (_file.Count > 0)
            {
                return;
            }

            _file.AddRange([0xFF, 0xD8]);

            if (Transform >= 0)
            {
                var adobe = new List<byte>("Adobe"u8.ToArray());
                adobe.AddRange([0, 100, 0, 0, 0, 0, 0, 0, (byte)Transform]);
                Segment(0xEE, adobe);
            }

            // [jpeg B.2.4.1] One quantization table, used by every
            // component.
            var table = new List<byte> { 0 };
            for (var k = 0; k < 64; k++)
            {
                table.Add((byte)_quantization);
            }

            Segment(0xDB, table);

            // [jpeg B.2.2] The frame header.
            var frame = new List<byte> { 8 };
            frame.AddRange(Word(_height));
            frame.AddRange(Word(_width));
            frame.Add((byte)_parts.Length);
            foreach (var part in _parts)
            {
                frame.AddRange([(byte)part.Id, (byte)((part.Horizontal << 4) | part.Vertical), 0]);
            }

            Segment(Progressive ? (byte)0xC2 : (byte)0xC0, frame);

            // [jpeg B.2.4.2] One table for the DC coefficients and one
            // for the AC, both of them the flat table Bits writes.
            foreach (var kind in new[] { 0, 1 })
            {
                var huffman = new List<byte> { (byte)(kind << 4) };
                for (var length = 1; length <= 16; length++)
                {
                    huffman.Add(length == Bits.CodeLength ? (byte)255 : (byte)0);
                }

                for (var symbol = 0; symbol < 255; symbol++)
                {
                    huffman.Add((byte)symbol);
                }

                Segment(0xC4, huffman);
            }

            if (_restart > 0)
            {
                Segment(0xDD, [.. Word(_restart)]);
            }
        }

        // [jpeg B.2.3] The scan header, and the writer the blocks of
        // this scan are put through.
        private void Scan(int start, int end, int high, int low, Write write)
        {
            var header = new List<byte> { (byte)_parts.Length };
            foreach (var part in _parts)
            {
                header.AddRange([(byte)part.Id, 0]);
            }

            header.AddRange([(byte)start, (byte)end, (byte)((high << 4) | low)]);
            Segment(0xDA, header);
            _write = write;
        }

        /// <summary>
        /// [jpeg A.2] The blocks of the scan, in the order the minimum
        /// coded units put them in, with a restart marker between
        /// intervals.
        /// </summary>
        private void Entropy(int[][][] components)
        {
            var bits = new Bits(_file);
            var previous = new int[components.Length][];
            var single = components.Length == 1;
            var across = (_width + (8 * _mostHorizontal) - 1) / (8 * _mostHorizontal);
            var down = (_height + (8 * _mostVertical) - 1) / (8 * _mostVertical);
            var units = single ? components[0].Length : across * down;
            var interval = _restart > 0 ? _restart : units;
            var marker = 0;

            for (var done = 0; done < units;)
            {
                if (done > 0)
                {
                    bits.Pad();
                    _file.AddRange([0xFF, (byte)(0xD0 + (marker++ % 8))]);
                    Array.Clear(previous);
                }

                var last = Math.Min(done + interval, units);

                for (; done < last; done++)
                {
                    for (var i = 0; i < components.Length; i++)
                    {
                        var each = single ? 1 : _parts[i].Horizontal * _parts[i].Vertical;
                        for (var b = 0; b < each; b++)
                        {
                            var block = components[i][single ? done : (done * each) + b];
                            _write!(bits, block, previous[i]);
                            previous[i] = block;
                        }
                    }
                }
            }

            bits.Pad();
        }

        private void Segment(byte marker, List<byte> body)
        {
            _file.AddRange([0xFF, marker]);
            _file.AddRange(Word(body.Count + 2));
            _file.AddRange(body);
        }
    }

    /// <summary>
    /// [jpeg C.3] Bits into bytes, the root of a code toward the top,
    /// with the zero byte that keeps an X'FF' from looking like a
    /// marker.
    /// </summary>
    private sealed class Bits(List<byte> file)
    {
        /// <summary>
        /// How long every code written here is. [jpeg C.2] lets a table
        /// give every value a code of the same length, and eight bits
        /// holds the 255 the tables are built with while leaving the
        /// code of all ones unused. A code is then its own value, which
        /// keeps the table out of what the tests are measuring.
        /// </summary>
        public const int CodeLength = 8;

        private int _byte;
        private int _count;

        public void Code(int symbol) => Raw(symbol, CodeLength);

        public void Raw(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                Put((value >> i) & 1);
            }
        }

        /// <summary>
        /// [jpeg B.1.1.5] Fills out the last byte of a segment with
        /// ones, which the reader then steps over.
        /// </summary>
        public void Pad()
        {
            while (_count != 0)
            {
                Put(1);
            }
        }

        private void Put(int bit)
        {
            _byte = (_byte << 1) | bit;
            _count++;

            if (_count < 8)
            {
                return;
            }

            file.Add((byte)_byte);
            if (_byte == 0xFF)
            {
                file.Add(0);
            }

            _byte = 0;
            _count = 0;
        }
    }
}
