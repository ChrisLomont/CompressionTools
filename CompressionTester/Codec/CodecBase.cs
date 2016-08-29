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
    public abstract class CodecBase : Output
    {

        public string CodecName { get; set; } = "";

        #region compression functions
        /// <summary>
        /// compress data, return compressed data
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] Compress(byte[] data)
        {
            return CompressToStream(new Datastream(data), Header.HeaderFlags.SymbolCount)?.GetBytes();
        }

        /// <summary>
        /// Stream version to keep from 0 padding last byte
        /// </summary>
        /// <param name="data"></param>
        /// <param name="headerFlags">Flags telling what to put in the header. Useful when embedding in other streams.</param>
        /// <returns></returns>
        public Bitstream CompressToStream(Datastream data,  Header.HeaderFlags headerFlags)
        {
            var bitstream = new Bitstream();
            WriteHeader(bitstream,data, headerFlags);
            foreach (var symbol in data)
                CompressSymbol(bitstream, symbol);
            WriteFooter(bitstream);
            return bitstream;
        }

        /// <summary>
        /// Write the header for the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="data"></param>
        /// <param name="headerFlags"></param>
        /// <returns></returns>
        public virtual void WriteHeader(Bitstream bitstream, Datastream data, Header.HeaderFlags headerFlags)
        {
        }

        /// <summary>
        /// Compress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="symbol"></param>
        public virtual void CompressSymbol(Bitstream bitstream, uint symbol)
        {
        }

        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public virtual void WriteFooter(Bitstream bitstream)
        {
        }
        #endregion

        #region decompression functions

        /// <summary>
        /// decompress data, return decompressed data
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte [] Decompress(byte [] data)
        {
            var bitstream = new Bitstream(data) {Position = 0};
            // reset position pointer
            var datastream = DecompressFromStream(bitstream, Header.HeaderFlags.SymbolCount);
            return datastream.ToByteArray();
        }

        /// <summary>
        /// Stream version 
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="headerFlags"></param>
        /// <returns></returns>
        public Datastream DecompressFromStream(Bitstream bitstream, Header.HeaderFlags headerFlags)
        {
            uint symbolCount = ReadHeader(bitstream, headerFlags);
            var datastream = new Datastream();
            for (var i = 0U; i < symbolCount; ++i)
                datastream.Add(DecompressSymbol(bitstream));
            ReadFooter(bitstream);
            return datastream;
        }

        /// <summary>
        /// Read the header for the compression algorithm
        /// Return number of symbols in stream if known, else 0 if not present
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="headerFlags">Flags telling what to put in the header. Useful when embedding in other streams.</param>
        /// <returns></returns>
        public virtual uint ReadHeader(Bitstream bitstream, Header.HeaderFlags headerFlags)
        {
            return 0;
        }

        /// <summary>
        /// Decompress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        public virtual uint DecompressSymbol(Bitstream bitstream)
        {
            return 0;
        }

        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public virtual void ReadFooter(Bitstream bitstream)
        {
        }


        #endregion


        #region Utility
        // count each value and how often it occurs
        public void Tally(List<uint> values)
        {
            var unique = new List<uint>();
            foreach (var v in values)
                if (!unique.Contains(v))
                    unique.Add(v);
            unique.Sort();
            foreach (var v in unique)
                Write($"[{v},{values.Count(s => s == v)}] ");

        }

        public void DumpArray(List<uint> data)
        {
            foreach (var d in data)
                Write($"0x{d:X2}, ");
        }

        /// <summary>
        /// Count number of ones set in value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint OnesCount(uint value)
        {
            // trick: map 2 bit values into sum of two 1-bit values in sneaky way
            value -= (value >> 1) & 0x55555555;
            value = ((value >> 2) & 0x33333333) + (value & 0x33333333); // each 2 bits is sum
            value = ((value >> 4) + value) & 0x0f0f0f0f; // each 4 bits is sum
            value += value >> 8; // each 8 bits is sum
            value += value >> 16; // each 16 bits is sum
            return value & 0x0000003f; // mask out answer
        }

        /// <summary>
        /// Compute Floor of Log2 of value
        /// 0 goes to 0
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint FloorLog2(uint value)
        {
            // flow bits downwards
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            // return (OnesCount(value) - 1); // if log 0 undefined, returns -1 for 0
            return (OnesCount(value >> 1)); // returns 0 for 0
        }

        // bits required to store values 0 through value-1
        /// <summary>
        /// Bits required to store value. 
        /// Value 0 returns 1.
        /// Is 1+Floor[Log_2 n] = Ceiling[Log_2 (n+1)]
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint BitsRequired(uint value)
        {
            return 1+FloorLog2(value);
        }

        public static bool CompareBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (var i = 0; i < a.Length; ++i)
                if (a[i] != b[i])
                    return false;
            return true;
        }
        public static bool CompareInts(uint [] a, uint [] b)
        {
            if (a.Length != b.Length)
                return false;
            for (var i = 0; i < a.Length; ++i)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        public static int BitCount(uint m)
        {
            var c = 0U;
            while (m != 0)
            {
                c += m & 1;
                m >>= 1;
            }
            return (int)c;
        }

        public static bool IsPowerOfTwo(uint m)
        {
            return BitCount(m) == 1;

        }


        public static double ComputeShannonEntropy(byte[] data)
        {
            // compute shannon entropy
            var counts = new int[256];
            foreach (var b in data)
                counts[b]++;
            var total = data.Length;
            var entropy = 0.0;
            foreach (var c in counts)
            {
                if (c != 0)
                {
                    var p = (double)c / total;
                    entropy += p * Math.Log(1 / p, 2);
                }
            }
            return entropy;
        }
        #endregion
    }
}
