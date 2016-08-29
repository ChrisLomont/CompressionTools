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
using System.Linq;

namespace Lomont.Compression.Codec
{
    public static class Header
    {
        [Flags]
        public enum HeaderFlags
        {
            None                 = 0x0000,
            /// <summary>
            /// Number of symbols in datastream.
            /// Will be the same as the byte count if the datastream values are in [0,255]
            /// </summary>
            SymbolCount          = 0x0001,
            /// <summary>
            /// Stores B where datastream values are in [0, 2^B - 1]
            /// </summary>
            BitsPerSymbol       = 0x0002
        }

        /// <summary>
        /// Write a universal compression header
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="data"></param>
        /// <param name="headerFlags"></param>
        public static void WriteUniversalHeader(Bitstream bitstream, Datastream data, HeaderFlags headerFlags)
        {
            if (headerFlags.HasFlag(HeaderFlags.SymbolCount))
                UniversalCodec.Lomont.EncodeLomont1(bitstream, (uint)data.Count, 6, 0);
            if (headerFlags.HasFlag(HeaderFlags.BitsPerSymbol))
            {
                var max = data.Any() ? data.Max() : 0;
                var bitsPerSymbol = CodecBase.BitsRequired(max);
                UniversalCodec.Lomont.EncodeLomont1(bitstream, bitsPerSymbol-1, 3, 0);
            }
        }
        /// <summary>
        /// Read a universal compression header
        /// Returns (symbol count, bits per symbol)
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="headerFlags"></param>
        public static Tuple<uint,uint> ReadUniversalHeader(Bitstream bitstream, HeaderFlags headerFlags)
        {
            uint decompressedSymbolCount = 0;
            uint bitsPerSymbol = 0;
            if (headerFlags.HasFlag(HeaderFlags.SymbolCount))
                decompressedSymbolCount = UniversalCodec.Lomont.DecodeLomont1(bitstream, 6, 0);
            if (headerFlags.HasFlag(HeaderFlags.BitsPerSymbol))
                bitsPerSymbol = UniversalCodec.Lomont.DecodeLomont1(bitstream, 3, 0)+1;
            return new Tuple<uint, uint>(decompressedSymbolCount,bitsPerSymbol);
        }
    }
}
