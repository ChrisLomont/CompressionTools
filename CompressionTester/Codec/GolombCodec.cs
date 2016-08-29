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
using System.Diagnostics;
using System.Linq;

namespace Lomont.Compression.Codec
{
    public class GolombCodec : CodecBase
    {

        /// <summary>
        /// recommended not to use on data over this size
        /// </summary>
        public static uint GolombThreshold { get; } = 2048; 
        [Flags]
        public enum OptionFlags
        {
            None = 0x00000000,
            Optimize = 0x00000001,
            DumpDebug = 0x00000002

        }
        public OptionFlags Options { get; set; } = OptionFlags.Optimize;
        /// <summary>
        /// Parameter for Golomb encoding
        /// </summary>
        public uint Parameter { get; set; } = 5;

        uint Optimize(Datastream data, Header.HeaderFlags headerFlags)
        {
            // Golomb bitlength  seems to be a convex down function of the parameter:
            // Let non-negative integers a1,a2,a3,..aN, parameter M. b=#bits to encode M, = Floor[log_2 M]+1
            // golomb code then qi=Floor[ai/M],ri=ai-Mqi, unary encode qi in qi+1 bits, encode ri in b or b-1 bits using
            // adaptive code. Then each of these is convex (true?) in M, so sum is convex, so length is convex.
            // Want best parameter between M=1 (total unary) and M=max{ai} = total fixed encoding
            // for large ai, unary uses lots of bits, so start at high end, 2^k=M >= max{ai}, divide by 2 until stops decreasing,
            // then binary search on final range.
            // todo - writeup optimal selection as blog post

            var g = new GolombCodec();
            g.Options &= ~OptionFlags.Optimize; // disable auto optimizer

            // function to compute length given the parameter
            Func<uint, uint> f = m1 =>
             {
                 g.Parameter = m1;
                 var bs = g.CompressToStream(data, headerFlags);
                 var len1 = bs.Length;
                 return len1;
            };

            Trace.Assert(data.Max() < 0x80000000); // needs to be true to work
            // start parameters
            var m = 1U<<(int)BitsRequired(data.Max());
            var length = f(m);
            uint oldLength;
            do
            {
                oldLength = length;
                m /= 2;
                length = f(m);
            } while (length < oldLength && m > 1);
            // now best between length and oldLength, binary search
            // todo - search
            Trace.Assert(m > 0);
            var left = m;
            var right = 2 * m;
            var mid = 0U;
            var a = f(left);
            while (left <= right)
            {
                mid = (left + right) / 2;
                var c = f(mid);
                if (c < a)
                    left = mid+1;
                else
                    right = mid-1;
            }
            if (mid == 1)
                mid = 2;
            // check mid, mid+1, mid-1
            uint best;
            if (f(mid)<f(mid+1))
            {
                if (f(mid - 1) < f(mid))
                    best = mid - 1;
                else
                    best= mid;
            }
            else
            {
                best = mid + 1;
            }
            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine($"Golomb opt {mid} {f(mid-2)} {f(mid-1)} {f(mid)} {f(mid+1)} {f(mid+2)} {f(mid+3)}");
            return best;
        }

        const int Lomont1Parameter = 6;

        public override void WriteHeader(Bitstream bitstream, Datastream data, Header.HeaderFlags headerFlags)
        {
            Header.WriteUniversalHeader(bitstream,data,headerFlags);
            if (Options.HasFlag(OptionFlags.Optimize))
                Parameter = Optimize(data, headerFlags);
            if (Options.HasFlag(OptionFlags.DumpDebug))
                WriteLine($"Golomb encode parameter {Parameter}");
            //Console.WriteLine("Parameter " + Parameter);
            UniversalCodec.Lomont.EncodeLomont1(bitstream,Parameter, Lomont1Parameter, 0);
        }

        public override void CompressSymbol(Bitstream bitstream, uint symbol)
        {
            UniversalCodec.Golomb.Encode(bitstream,symbol,Parameter);
        }

        public override uint ReadHeader(Bitstream bitstream, Header.HeaderFlags headerFlags)
        {
            var header = Header.ReadUniversalHeader(bitstream, headerFlags);
            Parameter = UniversalCodec.Lomont.DecodeLomont1(bitstream, Lomont1Parameter, 0);
            return header.Item1; // total items
        }

        public override uint DecompressSymbol(Bitstream bitstream)
        {
            return UniversalCodec.Golomb.Decode(bitstream,Parameter);
        }
    }
}
