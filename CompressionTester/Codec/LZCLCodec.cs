/*
MIT License

Copyright (c) 2016 Chris Lomont

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Lomont.Compression.Codec
{
    /// <summary>
    /// LZCL - Chris Lomont's LZ77 codec - a simple, small, low footprint lossless compression 
    /// algorithm. It's a form of LZ77 with multiple other compressors applied to literals, 
    /// tokens (separately or combined to distance and length), and decisions, along with 
    /// optional data filtering (not yet implemented).
    /// 
    /// Data is compressed by converting into a stream of decisions, literals, and tokens,
    /// where a token is a (distance,length) pair. A decision is a bit marking the next item 
    /// as a literal or token. A literal is a symbol to output. A token denotes a length back 
    /// into the symbol history and the length of a run to copy forwards. 
    /// 
    /// Format:
    ///    TODO - document cleanly :)
    /// </summary>
    [Codec("LZCL")]
    public class LzclCodec : CodecBase
    {
        #region Settings

        [Flags]
        public enum OptionFlags
        {
            None = 0,
            DumpOptimize = 0x00000001,
            DumpDebug    = 0x00000002,
            DumpStream   = 0x00000004,
            DumpCompressorSelections = 0x00000008,

            ShowTallies = 0x00000010,
            ShowSavings = 0x00000020
        }

        public CompressionChecker.OptionFlags CompressionOptions { get; set; } =
            CompressionChecker.OptionFlags.UseFixed |
            CompressionChecker.OptionFlags.UseArithmetic |
            CompressionChecker.OptionFlags.UseHuffman |
            CompressionChecker.OptionFlags.UseGolomb;

        public OptionFlags Options { get; set; } = OptionFlags.None;


        // parameters defining format
        /// <summary>
        /// Distances can be in the range 0 to MaximumDistance inclusive.
        /// Decoding requires this much in a history buffer, so uses RAM
        /// </summary>
        [CodecParameter("Maximum distance", "maxDist", "Max distance to look back in buffer")]
        public uint MaximumDistance { get; set; } = (1 << 12);

        /// <summary>
        /// The smallest allowable run length.
        /// Any shorter run is best done as literals.
        /// </summary>
        [CodecParameter("Minimum length", "minLen", "Minimum length of a run to be worthwhile")]
        public uint MinimumLength { get; set; } = 2;

        /// <summary>
        /// Run lengths can be in the range MinimumLength to MaximumLength inclusive
        /// </summary>
        [CodecParameter("Maximum length", "maxLen", "Maximum length of a run to be worthwhile")]
        public uint MaximumLength { get; set; } = (1 << 9);
        #endregion

        #region Compression functions

        /// <summary>
        /// Write the header for the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="data"></param>
        /// <param name="headerFlags">Flags telling what to put in the header. Useful when embedding in other streams.</param>
        /// <returns></returns>
        public override void WriteHeader(Bitstream bitstream, Datastream data, Header.HeaderFlags headerFlags)
        {
            // erase data streams
            decisions.Clear();
            decisionRuns.Clear();
            literals.Clear();
            distances.Clear();
            lengths.Clear();
            tokens.Clear();

            // fill in all the data streams
            uint actualMinLength, actualMaxDistance;
            ComputeStreams(data, out actualMinLength, out actualMaxDistance);

            // due to the vagaries of this format, we write the entire file in the header call, 
            // and unfortunately ignore the encode symbol and footer sections

            // dump info to help analyze
            if (Options.HasFlag(OptionFlags.DumpDebug))
            {
                WriteLine("LZCL compress:");
                WriteLine($"  Data length {data.Count} ");
            }

            if (Options.HasFlag(OptionFlags.ShowTallies))
            {
                // some info to help make analyze and make decisions
                Write("Length tally: ");
                Tally(lengths);
                WriteLine();

                Write("Distance tally: ");
                Tally(distances);
                WriteLine();
            }

            // get compressed streams so we can decide what to output
            var decisionChoice     = GetBestCompressor("decisions"    , decisions   );
            var decisionRunsChoice = GetBestCompressor("decision runs", decisionRuns);
            var literalsChoice     = GetBestCompressor("literals", literals);
            var tokensChoice       = GetBestCompressor("tokens", tokens);
            var distancesChoice    = GetBestCompressor("distances", distances);
            var lengthsChoice      = GetBestCompressor("lengths", lengths);

            // write header values
            Header.WriteUniversalHeader(bitstream, data, headerFlags);

            // save max distance occurring, used to encode tokens, very useful to users to know window needed size
            UniversalCodec.Lomont.EncodeLomont1(bitstream, actualMaxDistance, 10, 0);
            UniversalCodec.Lomont.EncodeLomont1(bitstream, actualMinLength, 2, 0);

            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine($"actual min length {actualMinLength}");
            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine($"Max distance {actualMaxDistance}");

            if (decisionChoice.Item2.Length < decisionRunsChoice.Item2.Length)
            {   
                // denote choice
                bitstream.Write(0); 
                // save item
                WriteItem(bitstream,decisionChoice);
                if (Options.HasFlag(OptionFlags.DumpDebug))
                    WriteLine("Decisions smaller than decision runs");
                StatRecorder.AddStat($"codec used: decisions {decisionChoice.Item1.Name}", 1);
            }
            else
            {
                // denote choice
                bitstream.Write(1);
                // save initial value
                bitstream.Write(decisions[0]);
                // save item
                WriteItem(bitstream, decisionRunsChoice);
                if (Options.HasFlag(OptionFlags.DumpDebug))
                    WriteLine("Decisions runs smaller than decisions");
                StatRecorder.AddStat($"codec used: decision runs {decisionRunsChoice.Item1.Name}", 1);
            }

            // literals
            WriteItem(bitstream, literalsChoice);
            StatRecorder.AddStat($"codec used: literals {literalsChoice.Item1.Name}", 1);


            // tokens or separate distance, length pairs
            if (tokensChoice.Item2.Length < distancesChoice.Item2.Length + lengthsChoice.Item2.Length)
            {
                // denote choice
                bitstream.Write(0);
                // save item
                WriteItem(bitstream, tokensChoice);
                if (Options.HasFlag(OptionFlags.DumpDebug))
                    WriteLine("Tokens smaller than distance,length pairs");
                StatRecorder.AddStat($"codec used: tokens {tokensChoice.Item1.Name}", 1);
            }
            else
            {
                // denote choice
                bitstream.Write(1);
                // save items
                WriteItem(bitstream, distancesChoice);
                WriteItem(bitstream, lengthsChoice);
                if (Options.HasFlag(OptionFlags.DumpDebug))
                    WriteLine("Distance,length pairs smaller than tokens");
                StatRecorder.AddStat($"codec used: distances {distancesChoice.Item1.Name}", 1);
                StatRecorder.AddStat($"codec used: lengths {lengthsChoice.Item1.Name}", 1);
            }
        }

        class Decoder
        {
            public CodecBase Codec { get; set;  }
            public uint BitLength { get; set;  }
            public Bitstream Bitstream { get; set; }

            public uint DecodeSymbol()
            {
                return Codec.DecompressSymbol(Bitstream);
            }
        }

        // Get codec type, a bitstream for it, read the header, advance the bitstream
        // the codec, the bit length, and the bitstream
        Decoder ReadItem(Bitstream bitstream)
        {
            var decoder = new Decoder();
            // save type
            uint type = bitstream.Read(2);
            if (type == 0)
                decoder.Codec = new FixedSizeCodec();
            else if (type == 1)
                decoder.Codec = new ArithmeticCodec();
            else if (type == 2)
                decoder.Codec = new HuffmanCodec();
            else if (type == 3)
                decoder.Codec = new GolombCodec();
            else
                throw new NotImplementedException("Unknown compressor type");
            decoder.BitLength = UniversalCodec.Lomont.DecodeLomont1(bitstream, 6, 0);
            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine($"Compressor index {type}, length {decoder.BitLength}");

            // prepare a bitstream for the codec
            decoder.Bitstream = new Bitstream();
            decoder.Bitstream.WriteStream(bitstream);
            decoder.Bitstream.Position = bitstream.Position;
            decoder.Codec.ReadHeader(decoder.Bitstream, internalFlags);
            bitstream.Position += decoder.BitLength;
            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine($"Post read item position {bitstream.Position}");
            return decoder;
        }

        Header.HeaderFlags internalFlags = Header.HeaderFlags.None; // todo - make None to save bits

        void WriteItem(Bitstream bitstream, Tuple<Type,Bitstream> item)
        {
            // save type
            var codecType = item.Item1;
            if (codecType == typeof(FixedSizeCodec))
                bitstream.Write(0, 2);
            else if (codecType == typeof(ArithmeticCodec))
                bitstream.Write(1, 2);
            else if (codecType == typeof(HuffmanCodec))
                bitstream.Write(2, 2);
            else if (codecType == typeof(GolombCodec))
                bitstream.Write(3, 2);
            else
                throw new NotImplementedException("Unknown compressor type");
            // save bit size
            UniversalCodec.Lomont.EncodeLomont1(bitstream, item.Item2.Length, 6, 0);
            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine($"Compressor type {codecType.Name}, length {item.Item2.Length}");
            // save stream
            bitstream.WriteStream(item.Item2);
        }

        /// <summary>
        /// Compress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="symbol"></param>
        public override void CompressSymbol(Bitstream bitstream, uint symbol)
        { // due to how this format is stored, all work was done in the header call
        }

        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public override void WriteFooter(Bitstream bitstream)
        {
            if (Options.HasFlag(OptionFlags.DumpStream))
                WriteLine();

            if (Options.HasFlag(OptionFlags.DumpStream))
                WriteLine($"Total bits {bitstream.Length}");
        }

        #endregion

        #region Decompression functions

        /// <summary>
        /// Read the header for the compression algorithm
        /// Return number of symbols in stream if known, else 0 if not present
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="headerFlags">Flags telling what to put in the header. Useful when embedding in other streams.</param>
        /// <returns></returns>
        public override uint ReadHeader(Bitstream bitstream, Header.HeaderFlags headerFlags)
        {
            decoderState = new DecoderState();

            // read header values
            var header = Header.ReadUniversalHeader(bitstream, headerFlags);
            decoderState.SymbolCount = header.Item1;

            // get max distance occurring, used to encode tokens, very useful to users to know window needed size
            decoderState.ActualMaxDistance = UniversalCodec.Lomont.DecodeLomont1(bitstream, 10, 0);
            decoderState.ActualMinLength = UniversalCodec.Lomont.DecodeLomont1(bitstream, 2, 0);

            // see if decisions or decision runs
            if (bitstream.Read(1) == 0)
            {
                decoderState.DecisionDecoder = ReadItem(bitstream);
            }
            else
            {
                // read initial value
                decoderState.InitialValue = bitstream.Read(1);
                // read item
                decoderState.DecisionRunDecoder = ReadItem(bitstream);
            }

            // literals
            decoderState.LiteralDecoder = ReadItem(bitstream);

            // tokens or separate distance, length pairs
            if (bitstream.Read(1) == 0)
            {
                decoderState.TokenDecoder = ReadItem(bitstream);
            }
            else
            {
                decoderState.DistanceDecoder = ReadItem(bitstream);
                decoderState.LengthDecoder = ReadItem(bitstream);
            }
            return decoderState.SymbolCount;
        }

        /// <summary>
        /// Decompress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        public override uint DecompressSymbol(Bitstream bitstream)
        {
            var output = decoderState.DecoderStream;

            // decode some if needed
            while (output.Count <= decoderState.DatumIndex)
            {
                if (decoderState.GetDecision() == 0)
                {
                    // literal
                    uint symbol = decoderState.LiteralDecoder.DecodeSymbol();
                    if (Options.HasFlag(OptionFlags.DumpDebug))
                        Write($"{output.Count}:[{symbol}] ");
                    output.Add(symbol);
                }
                else
                {
                    // token - either a single token or a token pair
                    uint distance, length;
                    decoderState.GetDecodedToken(out distance, out length);

                    if (Options.HasFlag(OptionFlags.DumpDebug))
                        Write($"[{distance},{length}] ");

                    // copy run
                    for (uint i = 0; i < length; ++i)
                        output.Add(output[(int)(output.Count - distance - 1)]);
                }
            }
            return output[decoderState.DatumIndex++];
        }

        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public override void ReadFooter(Bitstream bitstream)
        {
        }

        #endregion

        #region Implementation

        #region Compressor

        /// <summary>
        /// decisions 1 = (distance,length), 0 = literal symbol
        /// </summary>
        private readonly List<uint> decisions = new List<uint>();
        /// <summary>
        /// Decisions converted to an alternating list of runs of 0 or 1
        /// </summary>
        private readonly List<uint> decisionRuns = new List<uint>();
        /// <summary>
        /// distances for tokens
        /// </summary>
        private readonly List<uint> distances = new List<uint>();
        /// <summary>
        /// lengths for tokens
        /// </summary>
        private readonly List<uint> lengths = new List<uint>();
        /// <summary>
        /// literal values
        /// </summary>
        private readonly List<uint> literals = new List<uint>();
        /// <summary>
        /// token values, which are packed (distance,length) pairs, computed as
        /// (length - actualMinLength)*(actualMaxDistance+1)+distance
        /// </summary>
        private readonly List<uint> tokens = new List<uint>();

        /// <summary>
        /// Process the data to compress, creating various list of values to be compressed later
        /// </summary>
        /// <param name="data"></param>
        /// <param name="actualMinLength"></param>
        /// <param name="actualMaxDistance"></param>
        private void ComputeStreams(Datastream data, out uint actualMinLength, out uint actualMaxDistance)
        {
            uint index = 0; // index of symbol to encode
            while (index < data.Count)
            {
                // find best run
                uint bestDistance = 0;
                uint bestLength = 0;
                // walk backwards to end with smallest distance for a given run length
                for (int distance = (int)MaximumDistance; distance >= 0; --distance)
                {
                    uint length = 0;
                    while (
                        // bound check the values
                        0 <= index - 1 - distance + length &&
                        index - 1 - distance + length < data.Count &&
                        index + length < data.Count &&
                        // check the bytes match
                        data[(int)(index - 1 - distance + length)] == data[(int)(index + length)] &&
                        // don't get longer than this
                        length < MaximumLength
                        )
                    {
                        length++;
                    }
                    // keep best
                    if (length >= bestLength)
                    {
                        // break ties in this direction to have shorter distances
                        bestLength = length;
                        bestDistance = (uint)distance;
                    }
                }

                // now with best run, decide how to encode next part
                if (bestLength >= MinimumLength)
                {
                    // encode (distance,length) token
                    decisions.Add(1); // select (distance,length)
                    distances.Add(bestDistance);
                    lengths.Add(bestLength);
                    index += bestLength;
                }
                else
                {
                    // encode literal
                    decisions.Add(0); // select literal
                    literals.Add(data[(int)(index)]); // save value
                    index++;
                }
            }

            Trace.Assert(decisions.Max() < 2);

            // some actually occurring extremes
            actualMaxDistance = distances.Any()?distances.Max():0;
            actualMinLength = lengths.Any()?lengths.Min():0;

            // shift lengths
            for (var i = 0; i < lengths.Count; ++i)
                lengths[i] -= actualMinLength;

            // create tokens in case we need them
            for (var i = 0; i < lengths.Count; ++i)
                tokens.Add(lengths[i]*(actualMaxDistance+1)+distances[i]);

            // get runs of each type of decision in case we need them
            decisionRuns.AddRange(GetRuns(decisions));

            if (Options.HasFlag(OptionFlags.ShowTallies))
            {
                Write("Decision runs tally ");
                Tally(decisionRuns);
                WriteLine();
            }
        }


        /// <summary>
        /// try various compression on the data, 
        /// return the best codec and compute the bits saved by it
        /// </summary>
        /// <returns></returns>
        private Tuple<Type, Bitstream> GetBestCompressor(
            string label,
            List<uint> data
            )
        {
            // use this to check each stream
            var cc = new CompressionChecker {Options = CompressionOptions};
            var stream = new Datastream(data);
            var results = cc.TestAll(label, stream, internalFlags);
            results.Sort((a, b) => a.CompressedBitLength.CompareTo(b.CompressedBitLength));

            var best = results[0];
            CodecBase codec;
            if (best.CompressorType == typeof(FixedSizeCodec))
                codec = new FixedSizeCodec();
            else if (best.CompressorType == typeof(ArithmeticCodec))
                codec = new ArithmeticCodec();
            else if (best.CompressorType == typeof(HuffmanCodec))
                codec = new HuffmanCodec();
            else if (best.CompressorType == typeof(GolombCodec))
                codec = new GolombCodec {Parameter = best.Parameters[0]};
            else
                throw new NotImplementedException("Unknown codec type");

            var bitstream = codec.CompressToStream(stream, internalFlags);

            var codecName = codec.GetType().Name;
            StatRecorder.AddStat("codec win: " + label + " " + codecName,1);
            StatRecorder.AddStat($"codec win {codecName} saved high ", results.Last().CompressedBitLength- best.CompressedBitLength);
            if (results.Count > 1)
                StatRecorder.AddStat($"codec win {codecName} saved low  ", results[1].CompressedBitLength - best.CompressedBitLength);

            if (Options.HasFlag(OptionFlags.DumpCompressorSelections))
                WriteLine($"{label} using {codecName}");

            return new Tuple<Type,Bitstream>(codec.GetType(),bitstream);
        }


        // decisions are 0 for literal, 1 for token. 
        // get all the runs of each
        static List<uint> GetRuns(List<uint> data)
        {
            var runs = new List<uint>();
            if (!data.Any())
                return runs;
            uint runLength = 1;
            var i = 1;
            while (i < data.Count)
            {
                if (data[i] == data[i - 1])
                    runLength++;
                else
                {
                    runs.Add(runLength);
                    runLength = 1;
                }
                i++;
            }
            if (runLength > 0)
                runs.Add(runLength);
            return runs;
        }

        #endregion // Compression

        #region Decompressor
        class DecoderState
        {
            public uint ActualMinLength;
            public uint ActualMaxDistance;
            public uint SymbolCount;
            public int DatumIndex { get; set; }
            public Datastream DecoderStream { get; } = new Datastream();
            public Decoder DecisionDecoder { private get; set; }
            public Decoder DecisionRunDecoder { private get; set; }
            public Decoder LiteralDecoder { get; set; }
            public Decoder TokenDecoder { private get; set; }
            public Decoder DistanceDecoder { private get; set; }
            public Decoder LengthDecoder { private get; set; }
            public uint InitialValue { private get; set; }

            public void GetDecodedToken(out uint distance, out uint length)
            {
                if (TokenDecoder != null)
                {
                    uint token = TokenDecoder.DecodeSymbol();
                    length = token / (ActualMaxDistance + 1) + ActualMinLength;
                    distance = token % (ActualMaxDistance + 1);
                }
                else
                {
                    distance = DistanceDecoder.DecodeSymbol();
                    length = LengthDecoder.DecodeSymbol() + ActualMinLength;
                }
            }

            // stuff for decoding runs
            private int curRun = -1; // 0 or 1, -1 if none
            private uint runsLeft; // runs of current type left

            public uint GetDecision()
            {
                if (DecisionDecoder != null)
                    return DecisionDecoder.DecodeSymbol();
                if (curRun == -1)
                {
                    curRun = (int)InitialValue;
                    runsLeft = DecisionRunDecoder.DecodeSymbol();
                }
                if (runsLeft == 0)
                {
                    curRun ^= 1; // toggle direction
                    runsLeft = DecisionRunDecoder.DecodeSymbol();
                }
                --runsLeft;
                return (uint)curRun;
            }
        }

        private DecoderState decoderState;

        #endregion

        #endregion // Implementation

    }
}
