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
using System.Linq;

namespace Lomont.Compression.Codec
{
    /// <summary>
    /// Class that searches compression methods looking for
    /// the best one for given data
    /// </summary>
    public class CompressionChecker : Output
    {
        [Flags]
        public enum OptionFlags
        {
            None           = 0x00000000,

            UseFixed       = 0x00000001,
            UseArithmetic  = 0x00000002,
            UseHuffman     = 0x00000004,
            UseGolomb      = 0x00000008,
            UseEliasGamma  = 0x00000010,
            UseEliasDelta  = 0x00000020,
            UseEliasOmega  = 0x00000040,
            UseBasc        = 0x00000080,
            UseStout       = 0x00000100,

            StoreStats = 0x01000000,

            UseAll         = 0x7FFFFFFF
        }

        public class Result
        {
            public Result(string name, uint bitLength, Type codecType, params uint [] parameters)
            {
                CompressorName = name;
                CompressedBitLength = bitLength;
                CompressorType = codecType;
                Parameters.AddRange(parameters);
            }
            public string CompressorName { get; set;  }
            public uint CompressedBitLength { get; set; }
            public Type CompressorType { get; set; }
            public List<uint> Parameters { get; set; } = new List<uint>();
        }

        public OptionFlags Options { get; set; } = OptionFlags.UseAll;

        /// <summary>
        /// try various compression on the data, 
        /// return list of compression results (bitLength, type, optional parameters)
        /// </summary>
        /// <param name="statPrefix"></param>
        /// <param name="data"></param>
        /// <param name="headerFlags"></param>
        /// <returns></returns>
        public List<Result> TestAll(string statPrefix, Datastream data, Header.HeaderFlags headerFlags)
        {
            var results = new List<Result>();

            // perform compression algorithm
            Action<string, CodecBase, Type> tryCodec = (label, codec, codecType) =>
            {
                var bitstream = codec.CompressToStream(data, headerFlags);
                var result = new Result(label, bitstream.Length, codecType);
                if (codec.GetType() == typeof(GolombCodec))
                {
                    var g = codec as GolombCodec;
                    if (g != null)
                    {
                        result.Parameters.Add(g.Parameter);
                        result.CompressorName += $"({g.Parameter})";
                    }
                }
                results.Add(result);
            };

            // try compression algorithms
            if (Options.HasFlag(OptionFlags.UseFixed))
                tryCodec("Fixed size", new FixedSizeCodec(), typeof(FixedSizeCodec));
            if (Options.HasFlag(OptionFlags.UseArithmetic))
                tryCodec("Arithmetic", new ArithmeticCodec(), typeof(ArithmeticCodec));
            if (Options.HasFlag(OptionFlags.UseHuffman))
                tryCodec("Huffman", new HuffmanCodec(),typeof(HuffmanCodec));
            if (Options.HasFlag(OptionFlags.UseGolomb) && data.Max() < GolombCodec.GolombThreshold)
                tryCodec("Golomb", new GolombCodec(), typeof(GolombCodec));

/*            // try Golomb encoding
            if (Options.HasFlag(OptionFlags.UseGolomb))
            {
                var bestg = UniversalCodec.Optimize(UniversalCodec.Golomb.Encode, data, 1, Math.Min(data.Max(), 256));
                var bitstream = bestg.Item1;
                var gname = $"Golomb({bestg.Item2,2})";
                results.Add(new Result(gname, bitstream.Length, typeof(GolombCodec), bestg.Item2));
                //results.Add(new Result(gname,bitstream.Length, typeof(UniversalCodec.Golomb), bestg.Item2));
            } */

            Action<string, UniversalCodec.UniversalCodeDelegate, Type> tryEncoder = (label, codec, codecType) =>
            {
                var bitstream = UniversalCodec.CompressStream(codec, data.Select(v => v + 1).ToList());
                results.Add(new Result(label,bitstream.Length, codecType));
            };

            // try Elias codes - all perform poorly - todo - need way to pass this back as type?
            if (Options.HasFlag(OptionFlags.UseEliasDelta))
                tryEncoder("EliasDelta",UniversalCodec.Elias.EncodeDelta, typeof(UniversalCodec.Elias));
            if (Options.HasFlag(OptionFlags.UseEliasGamma))
                tryEncoder("EliasGamma",UniversalCodec.Elias.EncodeGamma, typeof(UniversalCodec.Elias));
            if (Options.HasFlag(OptionFlags.UseEliasOmega))
                tryEncoder("EliasOmega", UniversalCodec.Elias.EncodeOmega, typeof(UniversalCodec.Elias));

            // Stout
            if (Options.HasFlag(OptionFlags.UseStout))
                tryEncoder("Stout", (b,v)=>UniversalCodec.Stout.Encode(b,v,3), typeof(UniversalCodec.Stout));

            // BinaryAdaptiveSequentialEncode
            if (Options.HasFlag(OptionFlags.UseBasc))
            {

                var bitstream = new Bitstream();
                UniversalCodec.BinaryAdaptiveSequentialEncode(bitstream, data, UniversalCodec.Elias.EncodeDelta);
                var label = "BASC";
                results.Add(new Result(label,bitstream.Length, typeof(UniversalCodec)));
            }

            // save stats
            foreach (var result in results)
                StatRecorder.AddStat(statPrefix + "_" + result.CompressorName, result.CompressedBitLength);

            return results;
        }
    }
}
