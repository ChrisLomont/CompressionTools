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
    // todo - Add: fibonacci
    // Improved Prefix Encodings of the Natural Numbers , QUENTIN F.STOUT, http://web.eecs.umich.edu/~qstout/pap/IEEEIT80.pdf
    // See UNIVERSAL CODES OF THE NATURAL NUMBERS, YUVAL FILMUS, https://arxiv.org/pdf/1308.1600.pdf, for recent results
    // also Variable-length Splittable Codes with Multiple Delimiters, http://arxiv.org/abs/1508.01360, Anatoly V.Anisimov
    // 
    // needs testing of values to check is correct, Wikipedia has some values to test
    // 
    // Add modes for list of value, such as RRUC (Recursive Bottom-Up coding), 
    //    BASC Binary Adaptive Sequential Coding
    //    See Salomon data compression handbook for details
    // 
    // test all

    /// <summary>
    /// Universal codes are prefix codes mapping positive integers onto binary codewords
    /// </summary>
    public static class UniversalCodec
    {
        /// <summary>
        /// Elias codes: Delta, Gamma, Omega
        /// </summary>
        public static class Elias
        {

            /// <summary>
            /// L = bits needed to store value
            /// Write L-1 zero bits, then the value in binary (which necessarily starts with one)
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="value32"></param>
            public static void EncodeGamma(Bitstream bitstream, uint value32)
            {
                Trace.Assert(value32 >= 1);
                uint n = CodecBase.BitsRequired(value32);
                for (var i = 0; i < n-1; ++i)
                    bitstream.Write(0);
                bitstream.Write(value32, n);
            }

            public static uint DecodeGamma(Bitstream bitstream)
            {
                uint n = 0, b;
                do
                {
                    b = bitstream.Read(1);
                    n++;
                } while (b != 1);
                n--;
                var t = bitstream.Read(n);
                return (1U << (int)n) + t;
            }


            /// <summary>
            /// Encode a 32 bit value into the bitstream using Elias Delta coding
            /// To encode a number X greater than 0:
            /// 1. N = number of bits needed to store X. N >=1 
            /// 2. L = number of bits needed to store N. L >= 1
            /// 3. Write L-1 zeroes.
            /// 4. Write the L bit representation of N (which starts with a 1)
            /// 5. Write all but the leading 1 bit of X (i.e., the last N-1 bits)
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="value32"></param>
            public static void EncodeDelta(Bitstream bitstream, uint value32)
            {
                Trace.Assert(value32 >= 1);
                uint n = CodecBase.BitsRequired(value32);
                uint l = CodecBase.BitsRequired(n);
                for (var i = 1; i <= l-1; ++i)
                    bitstream.Write(0, 1);
                bitstream.Write(n, l);
                bitstream.Write(value32, n-1);
            }

            public static uint DecodeDelta(Bitstream bitstream)
            {
                uint l = 0, b;
                do
                {
                    b = bitstream.Read(1);
                    l++;
                } while (b == 0);
                // get remaining l-1 digits of n, add back initial bit read in loop while determining L
                uint n = bitstream.Read(l-1) + (1U << (int) (l-1));
                // read last n-1 bits of x, add back leading bit
                uint x = bitstream.Read(n-1) + (1U<<(int)(n-1));
                return x;
            }


            public static void EncodeOmega(Bitstream bitstream, uint value32)
            {
                Trace.Assert(value32 >= 1);
                var stack = new Stack<uint>();
                while (value32 != 1)
                {
                    stack.Push(value32);
                    value32 = CodecBase.BitsRequired(value32) - 1;
                }
                while (stack.Any())
                    bitstream.Write(stack.Pop());
                bitstream.Write(0);
            }

            public static uint DecodeOmega(Bitstream bitstream)
            {
                uint n = 1,b;
                do
                {
                    b = bitstream.Read(1);
                    if (b != 0)
                    {
                        uint t = bitstream.Read(n);
                        n = (1U << (int) n) + t;
                    }
                } while (b != 0);
                return n;
            }

        }

        /// <summary>
        /// A few simple codes designed by Chris Lomont
        /// Outperform Elias for some things
        /// </summary>
        public static class Lomont
        {
            /// <summary>
            /// Encode a 32 bit value into the bitstream using Lomont method 1
            /// value is broken into 'chunckSize' bit chunks, then 0 or 1 written before 
            /// each chunk, where 1 means not last chunk, 0 means last chunk. Chunks are 
            /// written least significant first. 'chunkSize' 6 is default and works best for
            /// common file sizes.
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="value32"></param>
            /// <param name="chunkSize">A good value is 6</param>
            /// <param name="deltaChunk">A good starting value is 0</param>
            public static void EncodeLomont1(Bitstream bitstream, uint value32, int chunkSize, int deltaChunk)
            {
                uint mask = (1U << chunkSize) - 1;
                while (value32 >= (1 << chunkSize))
                {
                    bitstream.Write(1, 1); // another chunk
                    bitstream.Write(value32 & mask, (uint)chunkSize); // write chunkSize bits
                    value32 >>= chunkSize;

                    if (deltaChunk != 0)
                    {
                        chunkSize += deltaChunk;
                        if (chunkSize <= 0)
                            chunkSize = 1;
                        mask = (1U << chunkSize) - 1;
                    }

                    //Console.Write('#');
                }
                bitstream.Write(0, 1); // last chunk
                bitstream.Write(value32 & mask, (uint)chunkSize);
            }

            /// <summary>
            /// Decode lomont method 1 with the given chunksize
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="chunkSize"></param>
            /// <param name="deltaChunk"></param>
            public static uint DecodeLomont1(Bitstream bitstream, int chunkSize, int deltaChunk)
            {
                uint value = 0, b;
                int shift = 0;
                do
                {
                    b = bitstream.Read(1); // decision
                    uint chunk = bitstream.Read((uint)chunkSize);
                    value += chunk << shift;
                    shift += chunkSize;

                    if (deltaChunk != 0)
                    {
                        chunkSize += deltaChunk;
                        if (chunkSize <= 0)
                            chunkSize = 1;
                    }

                } while (b != 0);
                return value;
            }


            /// <summary>
            /// Encode a 32 bit value into the bitstream using Lomont method 2
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="value32"></param>
            public static void EncodeLomont2(Bitstream bitstream, uint value32)
            {
                while (value32 > 255)
                {
                    bitstream.Write(1, 1); // another byte
                    bitstream.Write(value32 & 255, 8); // write 8 bits
                    value32 >>= 8;
                }
                Trace.Assert(value32 > 0);
                bitstream.Write(0, 1); // last byte
                bitstream.Write(value32 & 255, 8);
            }

            /// <summary>
            /// Encode a 32 bit value into the bitstream using Lomont method 3
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="value32"></param>
            public static void EncodeLomont3(Bitstream bitstream, uint value32)
            {
                Trace.Assert(value32 > 0);
                uint n = CodecBase.BitsRequired(value32);
                for (var i = 0; i < n - 1; ++i)
                    bitstream.Write(0, 1); // n-1 of these
                bitstream.Write(1, 1); // end of n count
                bitstream.Write(value32, n - 1); // remove leading 1
            }
        }

        public static class Truncated
        {
            /// <summary>
            /// Useful for encoding a value in [0,N). 
            /// k = bit length N, k > 0 
            /// If N is a power of 2, uses k bits. 
            /// If N is not a power of two, encodes some choices in k-1 bits, others in k bits
            /// </summary>
            public static void Encode(Bitstream bitstream, uint value, uint n)
            {
                Trace.Assert(value < n);
                uint k = CodecBase.BitsRequired(n);
                uint u = (1U << (int)k) - n; // u = number of unused codewords
                if (value < u)
                    bitstream.Write(value, k-1);
                else
                    bitstream.Write(value+u, k);
            }

            /// <summary>
            /// Decodes truncated code item
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="n"></param>
            /// <returns></returns>
            public static uint Decode(Bitstream bitstream, uint n)
            {
                uint k = CodecBase.BitsRequired(n);
                uint u = (1U << (int)k) - n; // u = number of unused codewords
                uint x = bitstream.Read(k-1);
                if (x >= u) 
                { // need another bit
                    x = 2*x + bitstream.Read(1);
                    x -= u;
                }
                return x;
            }
        }

        public static class Stout
        {
            static void Recurse(Bitstream bitstream, uint n, uint k)
            {
                Trace.Assert(k > 1);
                if (n < (1U << (int) k))
                    bitstream.Write(n, k);
                else
                {
                    uint m = CodecBase.FloorLog2(n);
                    Recurse(bitstream, m - k, k);
                    bitstream.Write(n, m + 1);
                }

            }

            /// <summary>
            /// Encode integer N via Sk(N) 0 B(N,floor(log_2 N)+1)
            /// 
            /// Recursively define: for fixed integer k > 1
            /// Sk(n) = B(n,l) if n in [0,2^k-1], else = Sk(Floor[Log_2 n]-k) B(n,Floor[Log_2 n]+1)
            /// where B(n,l) writes n in k bits
            /// Note the final B value is 0 prefixed, which lets the decoder know this is the last block.
            /// </summary>
            public static void Encode(Bitstream bitstream, uint value, uint k)
            {
                uint bitLength = CodecBase.BitsRequired(value);
                Recurse(bitstream,bitLength,k);
                bitstream.Write(0);
                bitstream.Write(value);
            }

            /// <summary>
            /// </summary>
            /// <returns></returns>
            public static uint Decode(Bitstream bitstream, uint k)
            {
                Trace.Assert(k > 1);
                uint v = bitstream.Read(k);
                while (true)
                {
                    uint b = bitstream.Read(1);
                    if (b == 0) // end of chain
                        return bitstream.Read(v);
                    v += k;
                    v = (1U<<(int)v) + bitstream.Read(v);
                }

            }
        }


        /// <summary>
        /// Golomb k-code and Exp code
        /// See http://www.ual.es/~vruiz/Docencia/Apuntes/Coding/Text/03-symbol_encoding/09-Golomb_coding/index.html
        /// </summary>
        public static class Golomb
        {
            /// <summary>
            /// Golomb code, useful for geometric distributions
            /// 
            /// encode values using int parameter m
            /// value N is encoded via: q=Floor(N/M),r=N%M, 
            /// q 1's, then one 0, then Log2M bits for r
            /// good for geometric distribution
            /// 
            /// </summary>
            /// <returns></returns>
            public static void Encode(Bitstream bitstream, uint value, uint m)
            {
                Trace.Assert(m > 0);
                var n = value;
                var q = n/m;
                var r = n%m;
                for (var i = 1; i <= q; ++i)
                    bitstream.Write(1);
                bitstream.Write(0);
                Truncated.Encode(bitstream,r,m);
            }

            public static uint Decode(Bitstream bitstream, uint m)
            {
                Trace.Assert(m > 0);
                var q = 0U;
                while (bitstream.Read(1) == 1)
                    ++q;
                uint r = Truncated.Decode(bitstream, m);
                return q *m + r;
            }

            /// <summary>
            /// Exponential Golumb code for x geq 0
            /// 1. Write (x+1) in binary in n bits
            /// 2. Prepend n-1 zero bits
            /// 
            /// Order k > 0, do above to Floor[x/2^k]
            /// Then x mod 2^k in binary
            /// </summary>
            /// <param name="bitstream"></param>
            /// <param name="value"></param>
            /// <param name="k"></param>
            public static void EncodeExp(Bitstream bitstream, uint value, uint k)
            {
                if (k > 0)
                {
                    EncodeExp(bitstream, value >> (int)k, 0);
                    uint mask = (1U << (int) k) - 1;
                    bitstream.Write(value&mask,k);
                }
                else
                {
                    uint n = CodecBase.BitsRequired(value + 1);
                    bitstream.Write(0,n-1);
                    bitstream.Write(value+1,n);
                }
            }

            public static uint DecodeExp(Bitstream bitstream, uint k)
            {
                if (k > 0)
                {
                    // read value shifted by k
                    uint high = DecodeExp(bitstream, 0);
                    //read last k bits
                    uint low = bitstream.Read(k);
                    return (high << (int)k) | low;
                }
                uint n = 0, b;
                do
                {
                    b = bitstream.Read(1);
                    ++n;
                } while (b == 0);
                // b is high bit of value + 1
                uint value = bitstream.Read(n-1);
                value |= b << (int)(n-1);
                return value - 1;
            }



        }

        public static class EvenRodeh
        {
            /// <summary>
            /// Even-Rodeh code
            /// 
            ///  Encode a non-negative integer N : 
            ///   1. If N is less than 4 then output N in 3 bits and stop.
            ///   2. If N is less than 8 then prepend the coded value with 3 bits containing the value of N and stop.
            ///   3. Prepend the coded value with the binary representation of N.
            ///   4. Store the number of bits prepended in step 3 as the new value of N.
            ///   5. Go back to step 2
            ///   6. Output a single 0 bit.
            /// 
            /// </summary>
            /// <returns></returns>
            public static void Encode(Bitstream bitstream, uint value)
            {
                if (value < 4)
                {
                    bitstream.Write(value,3);
                    return;
                }

                var stack = new Stack<uint>();
                uint n = value;
                while (true)
                {
                    if (n < 8)
                    {
                        stack.Push(n);
                        break;
                    }
                    stack.Push(n);
                    n = CodecBase.BitsRequired(n);
                }
                while (stack.Any())
                {
                    uint val = stack.Pop();
                    if (val < 8)
                        bitstream.Write(val,3);
                    else
                        bitstream.Write(val);
                }
                bitstream.Write(0,1);
            }

            /// <summary>
            /// To decode an Even-Rodeh-coded integer:
            ///   1.Read 3 bits and store the value into N.If the first bit read was 0 then stop.The decoded number is N.
            ///     If the first bit read was 1 then continue to step 2.
            ///   2.Examine the next bit. If the bit is 0 then read 1 bit and stop.The decoded number is N.
            ///     If the bit is 1 then read N bits, store the value as the new value of N, and go back to step 2.
            /// </summary>
            /// <param name="bitstream"></param>
            /// <returns></returns>
            public static uint Decode(Bitstream bitstream)
            {
                uint n = bitstream.Read(3);
                if (n < 4) return n;
                while (true)
                {
                    uint nextBit = bitstream.Read(1);
                    if (nextBit == 0) return n;
                    uint m = bitstream.Read(n-1);
                    n = m | (1U << (int)(n - 1));
                }
            }
        }

        /// <summary>
        /// Compress stream of numbers:
        /// Store length+1 using EliasDelta (to avoid 0 case)
        /// First number x0 requires b0 bits. Write b0 in EliasDelta. Write x0 in b0 bits.
        /// Each subsequent number xi requires bi bits. If bi leq b(i-1) then
        /// write a 0 bit, then xi in b(i-1) bits. Else write (bi-b(i-1)) 1 bits, then a 0,
        /// then the least sig bi - 1 bits of xi (the leading 1 in xi is implied, xi>0).
        /// TODO - alternatives - allow the bi to change slowly, removes some hiccups for odd data points, set b to avg of some prev values
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="data"></param>
        /// <param name="universalCoder">How to encode/decode a data length and first bitlength items. Elias.EncodeDelta is useful</param>
        public static void BinaryAdaptiveSequentialEncode(Bitstream  bitstream, Datastream data,Action<Bitstream,uint> universalCoder)
        {
            universalCoder(bitstream,(uint)data.Count+1);
            if (data.Count == 0) return;
            uint b1 = CodecBase.BitsRequired(data[0]);
            universalCoder(bitstream,b1);
            bitstream.Write(data[0]);
            for (var i = 1; i < data.Count; ++i)
            {
                uint d = data[i];
                uint b2 = CodecBase.BitsRequired(d);

                if (b2 <= b1)
                { // b1 is enough bits
                    bitstream.Write(0);
                    bitstream.Write(d,b1);
                }
                else
                { // b2 requires more bits, tell how many
                    Trace.Assert(d>0);
                    for (var ik = 0; ik < b2-b1; ++ik)
                        bitstream.Write(1);
                    bitstream.Write(0,1); // end of bit count
                    bitstream.Write(d, b2-1); // strip off leading '1'
                }
                b1 = CodecBase.BitsRequired(d); // for next pass
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="universalDecoder">The same encoder/decoder pair used to encode this</param>
        /// <returns></returns>
        public static List<uint> BinaryAdaptiveSequentialDecode(Bitstream bitstream,Func<Bitstream, uint> universalDecoder)
        {
            var list = new List<uint>();
            uint length = universalDecoder(bitstream) - 1;
            if (length == 0) return list;
            uint b1 = universalDecoder(bitstream);
            uint xi = bitstream.Read(b1);
            list.Add(xi);
            while (list.Count < length)
            {
                var decision = bitstream.Read(1);
                if (decision == 0)
                { // bi is <= b(i-1), so enough bits
                    xi = bitstream.Read(b1);
                }
                else
                { // bi is bigger than b(i-1), must increase it
                    uint delta = 0;
                    do
                    {
                        decision = bitstream.Read(1);
                        delta++;
                    } while (decision != 0);
                    b1 += delta;
                    xi = bitstream.Read(b1 - 1); // xi has implied leading 1
                    xi |= 1U << (int) (b1-1);
                }
                list.Add(xi);
                b1 = CodecBase.BitsRequired(xi);
            }
            return list;
        }


#region Helper functions
        public delegate void UniversalCodeDelegate(Bitstream bitstream, uint value);

        public static Bitstream CompressStream(UniversalCodeDelegate encoder, List<uint> data)
        {
            var bs = new Bitstream();
            foreach (var d in data)
                encoder(bs, d);
            return bs;
        }


        public delegate void OneParameterCodeDelegate(Bitstream bitstream, uint value, uint parameter);

        /// <summary>
        /// Optimize a code over a non-negative parameter
        /// return smallest stream and best parameter
        /// </summary>
        /// <param name="coder"></param>
        /// <param name="data"></param>
        /// <param name="minParameter"></param>
        /// <param name="maxParameter"></param>
        /// <returns></returns>
        public static Tuple<Bitstream,uint> Optimize(OneParameterCodeDelegate coder, List<uint> data, uint minParameter, uint maxParameter)
        {
            var bestLength = UInt32.MaxValue;
            uint bestParameter = 0;
            Bitstream bestBitstream = null;
            for (uint parameter = minParameter; parameter <= maxParameter; ++parameter)
            {
                var bs = new Bitstream();
                foreach (var d in data)
                    coder(bs, d, parameter);
                if (bs.Length < bestLength)
                {
                    bestLength = bs.Length;
                    bestBitstream = bs;
                    bestParameter = parameter;
                }
            }
            return new Tuple<Bitstream,uint>(bestBitstream,bestParameter);
        }
        
#endregion

    }
}
