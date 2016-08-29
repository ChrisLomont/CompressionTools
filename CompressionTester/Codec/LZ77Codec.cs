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
    /// A basic LZ77 codec - a simple, small, low footprint lossless compression algorithm
    /// 
    /// Data is compressed by converting into a stream of decisions, literals, and tokens,
    /// where a token is a (distance,length) pair. A decision is a bit marking the next item 
    /// as a literal or token. A literal is a symbol to output. A token denotes a length back 
    /// into the symbol history and the length of a run to copy forwards. 
    /// 
    /// Format:
    ///    Header: TODO
    ///    Data: run of literal or (distance,length) pairs
    /// TODO
    /// 
    /// </summary>
    [Codec("LZ77")]
    public class Lz77Codec : CodecBase
    {
        #region Settings

        [Flags]
        public enum OptionFlags
        {
            DumpOptimize = 0x00000001,
            DumpDebug    = 0x00000002,
            DumpHeader   = 0x00000004,
            DumpEncode   = 0x00000008
        }

        public OptionFlags Options { get; set; }


        // parameters defining format
        /// <summary>
        /// Distances can be in the range 0 to MaximumDistance inclusive.
        /// Decoding requires this much in a history buffer, so uses RAM
        /// </summary>
        [CodecParameter("Maximum distance","maxDist","Max distance to look back in buffer")]
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
            literals.Clear();
            distances.Clear();
            lengths.Clear();

            // fill in various data streams
            ScanData(data);

            // put header into stream
            PackHeader(bitstream, data, headerFlags);

            // start indices here
            encoderState.Reset();

            if (Options.HasFlag(OptionFlags.DumpEncode))
                Write("LZ77 stream: ");
        }

        /// <summary>
        /// Compress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="symbol"></param>
        public override void CompressSymbol(Bitstream bitstream, uint symbol)
        {
            encoderState.SymbolCallIndex++;
            // process streams into compressed bitstream
            while (encoderState.SymbolCallIndex > encoderState.DatumIndex)
            {
                uint decision = decisions[encoderState.DecisionIndex++];
                bitstream.Write(decision, 1); // decision
                if (decision == 0)
                {
                    // literal
                    if (Options.HasFlag(OptionFlags.DumpEncode))
                        Write($"[{literals[encoderState.LiteralIndex]}] ");
                    bitstream.Write(literals[encoderState.LiteralIndex++], encoderState.ActualBitsPerSymbol);
                    ++encoderState.DatumIndex;
                }
                else
                {
                    // (distance, length) pair, encoded
                    uint distance = distances[encoderState.TokenIndex];
                    uint length = lengths[encoderState.TokenIndex];
                    if (Options.HasFlag(OptionFlags.DumpEncode))
                        Write($"[{distance},{length}] ");
                    uint token = (length - encoderState.ActualMinLength)*(encoderState.ActualMaxDistance + 1) + distance;
                    bitstream.Write(token, encoderState.ActualBitsPerToken);
                    ++encoderState.TokenIndex;

                    encoderState.DatumIndex += (int) length;

                }
            }
        }

        // call to try and optimize compression for the given data
        public Bitstream Optimize(byte[] data)
        {
            var bestRatio = double.PositiveInfinity;
            for (uint large = 1; large <= 1024; large++)
                for (uint m = 1; m <= 8; ++m)
                    for (uint run = 2; run <= 64; run++)
                    {
                        MaximumDistance = large;
                        MaximumLength = run;
                        MinimumLength = m;
                        var t = OutputWriter;
                        OutputWriter = null;
                        var enc = Compress(data);
                        OutputWriter = t;
                        var ratio = (double)enc.Length / data.Length;
                        if (ratio < bestRatio)
                        {
                            bestRatio = ratio;
                            if (Options.HasFlag(OptionFlags.DumpOptimize))
                            {
                                WriteLine("*-----------------------------------------*");
                                WriteLine($"Ratio {ratio} run max {MaximumLength} distance max {MaximumDistance} min length {MinimumLength}");
                            }
                            //Compress(data); // to get debug messages
                        }
                    }
            return null;
        }


        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public override void WriteFooter(Bitstream bitstream)
        {
            if (Options.HasFlag(OptionFlags.DumpEncode))
                WriteLine();
            if (Options.HasFlag(OptionFlags.DumpEncode))
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
            var bsd = bitstream; // alias

            // header values
            var header = Header.ReadUniversalHeader(bitstream, headerFlags);
            decoderState.ByteLength = header.Item1;

            decoderState.ActualBitsPerSymbol = UniversalCodec.Lomont.DecodeLomont1(bsd, 3 ,0)+1;  // usually 8, -1 => 7, fits in 3 bits
            decoderState.ActualBitsPerToken  = UniversalCodec.Lomont.DecodeLomont1(bsd, 5 ,0)+1;   // around 20 or so, depends on parameters
            decoderState.ActualMinLength     = UniversalCodec.Lomont.DecodeLomont1(bsd, 2 ,0);          // usually 2
            uint actualMaxToken              = UniversalCodec.Lomont.DecodeLomont1(bsd, 25, -10);                  // 
            decoderState.ActualMaxDistance   = UniversalCodec.Lomont.DecodeLomont1(bsd, 14, -7);   // 


            if (Options.HasFlag(OptionFlags.DumpDebug))
            {
                //some info to help analyze
                WriteLine("LZ77 decompress:");
                WriteLine($"  Data length {decoderState.ByteLength} ");
                WriteLine($"  Bits per symbol {decoderState.ActualBitsPerSymbol}");
                WriteLine($"  Bits per token {decoderState.ActualBitsPerToken}");
                WriteLine($"  Max token {actualMaxToken}");
                WriteLine($"  Max distance {decoderState.ActualMaxDistance}");
                WriteLine($"  Min length {decoderState.ActualMinLength}");
            }


            if (Options.HasFlag(OptionFlags.DumpDebug))
                Write("LZ77 decode stream: ");

            decoderState.Reset();

            return decoderState.ByteLength;
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
                if (bitstream.Read(1) == 0)
                {
                    // literal
                    uint symbol = bitstream.Read(decoderState.ActualBitsPerSymbol);
                    if (Options.HasFlag(OptionFlags.DumpDebug))
                        Write($"{output.Count}:[{symbol}] ");

                    output.Add(symbol);
                    ++decoderState.DatumIndex;
                    return symbol;
                }
                // run
                uint token = bitstream.Read(decoderState.ActualBitsPerToken);
                // decode token
                uint length = token / (decoderState.ActualMaxDistance + 1) + decoderState.ActualMinLength;
                uint distance = token % (decoderState.ActualMaxDistance + 1);

                if (Options.HasFlag(OptionFlags.DumpDebug))
                    Write($"[{distance},{length}] ");

                // copy run
                for (uint i = 0; i < length; ++i)
                {
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
            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine();
        }

        #endregion



        #region Implementation

        class State
        {
            public uint ByteLength; // number of symbols to encode/decode
            public uint ActualBitsPerSymbol,
                ActualMinLength,
                ActualMaxDistance,
                ActualBitsPerToken;

            public int DatumIndex;

            public int SymbolCallIndex;

            // data stream positions
            public int LiteralIndex;
            public int TokenIndex;
            public int DecisionIndex;

            /// <summary>
            /// Reset state for encoding/decoding. Does not change lengths, only indices
            /// </summary>
            public void Reset()
            {
                LiteralIndex = TokenIndex = DecisionIndex = DatumIndex = 0;
                SymbolCallIndex = 0;
                DecoderStream.Clear();
            }

            // stream used to buffer decoding
            public readonly Datastream DecoderStream = new Datastream();
        }

        private readonly State encoderState = new State();
        private readonly State decoderState = new State();



        #region Compressor

        /// <summary>
        /// decisions 1 = (length,distance), 0 = literal symbol
        /// </summary>
        private readonly List<uint> decisions = new List<uint>();

        /// <summary>
        /// distances for tokens
        /// </summary>
        private readonly List<uint> distances = new List<uint>();

        /// <summary>
        /// lengths for runs
        /// </summary>
        private readonly List<uint> lengths = new List<uint>();

        /// <summary>
        /// literal values
        /// </summary>
        private readonly List<uint> literals = new List<uint>();

        /// <summary>
        /// Process the data to compress, creating various list of values to be compressed later
        /// </summary>
        /// <param name="data"></param>
        private void ScanData(Datastream data)
        {
            uint index = 0; // index of symbol to encode
            while (index < data.Count)
            {
                // find best run
                uint bestDistance = 0;
                uint bestLength = 0;
                // walk backwards to end with smallest distance for a given run length
                for (int distance = (int) MaximumDistance; distance >= 0; --distance)
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
                        bestDistance = (uint) distance;
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
                    literals.Add(data[(int)index]); // save value
                    index++;
                }
            }
        }

        /// <summary>
        /// Compress the data in the streams.
        /// </summary>
        /// <returns></returns>
        private void PackHeader(Bitstream bitstream, Datastream data, Header.HeaderFlags headerFlags)
        {

            // actual value extremes occurring in data streams
            encoderState.ActualMaxDistance = distances.Any() ? distances.Max() : 0;
            encoderState.ActualMinLength = lengths.Any() ? lengths.Min() : 0;
            // token (distance,length) is encoded as
            //   (length - actualMinLength) * (actualMaxDistance+1) + distance
            // compute largest occurring token value
            uint actualMaxToken = 0;
            for (var i = 0; i < distances.Count; ++i)
            {
                uint length = lengths[i];
                uint distance = distances[i];
                uint token = (length - encoderState.ActualMinLength)*(encoderState.ActualMaxDistance + 1) + distance;
                actualMaxToken = Math.Max(actualMaxToken, token);
            }
            
            // bit sizes
            encoderState.ActualBitsPerSymbol = BitsRequired(literals.Max());
            encoderState.ActualBitsPerToken = BitsRequired(actualMaxToken);

            //some info to help analyze

            if (Options.HasFlag(OptionFlags.DumpHeader))
            {
                //WriteLine("LZ77 compress:");
                //WriteLine($"  Data length {data.Count} ");
                //WriteLine($"  Bits per symbol {encoderState.actualBitsPerSymbol} ");
                //WriteLine($"  Bits per token {encoderState.actualBitsPerToken}");
                //WriteLine($"  Max token {actualMaxToken}, {lengths.Count} tokens ");
                //WriteLine($"  Max distance {encoderState.actualMaxDistance} ");
            }

            // checks
            Trace.Assert(0 < encoderState.ActualBitsPerSymbol);

            // header values
            Header.WriteUniversalHeader(bitstream, data, headerFlags);
            UniversalCodec.Lomont.EncodeLomont1(bitstream, encoderState.ActualBitsPerSymbol - 1, 3,0);  // usually 8, -1 => 7, fits in 3 bits
            UniversalCodec.Lomont.EncodeLomont1(bitstream, encoderState.ActualBitsPerToken - 1, 5,0);   // around 20 or so, depends on parameters
            UniversalCodec.Lomont.EncodeLomont1(bitstream, encoderState.ActualMinLength, 2,0 );          // usually 2
            UniversalCodec.Lomont.EncodeLomont1(bitstream, actualMaxToken, 25, -10);                  // 
            UniversalCodec.Lomont.EncodeLomont1(bitstream, encoderState.ActualMaxDistance, 14, -7);   // 

            if (Options.HasFlag(OptionFlags.DumpHeader))
            {
                WriteLine("LZ77 header:");
                WriteLine($"   datalen {data.Count} bits/symbol {encoderState.ActualBitsPerSymbol}, bits/token {encoderState.ActualBitsPerToken}, minLen {encoderState.ActualMinLength}, max token {actualMaxToken}, max dist {encoderState.ActualMaxDistance}");
            }



        }


#endregion // Compression

#endregion // Implementation
    }
}
