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
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Lomont.Compression.Codec
{
    /// <summary>
    /// Read or write values to a stream of bits
    /// </summary>
    public sealed  class Bitstream : Output
    {
        public Bitstream(byte[] data)
        {
            foreach (var b in data)
                Write(b,8);
        }

        public Bitstream()
        {
            
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{Length}: ");
            for (var i = 0U; i < Length; ++i)
            {
                uint index = i;
                sb.Append(ReadFrom(ref index, 1));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Position of next read/write
        /// </summary>
        public uint Position { get; set; }

        /// <summary>
        /// Number of bits in stream
        /// </summary>
        public uint Length => (uint)bits.Count;

        /// <summary>
        /// Clear stream
        /// </summary>
        public void Clear()
        {
            bits.Clear();
            Position = 0;
        }

        /// <summary>
        /// Write bits, MSB first
        /// </summary>
        /// <param name="value"></param>
        /// <param name="bitLength"></param>
        public void Write(uint value, uint bitLength)
        {
            Trace.Assert(bitLength <= 32);
            for (var i = 0U; i < bitLength; ++i)
            {
                var bit = (byte) ((value>>((int)bitLength-1)) & 1);
                Write(bit.ToString());

                if (Position == Length)
                {
                    bits.Add(bit);
                    Position = Length;
                }
                else
                {
                    bits[(int)i] = bit;
                    Position = i+1;
                }
                value <<= 1;
            }
        }

        /// <summary>
        /// Copy in the given BitStream
        /// </summary>
        /// <param name="bitstream"></param>
        public void WriteStream(Bitstream bitstream)
        {
            var temp = bitstream.Position;
            bitstream.Position = 0;
            while (bitstream.Position < bitstream.Length)
                Write(bitstream.Read(1),1);
            bitstream.Position = temp;
        }

        /// <summary>
        /// Read values from given position, update it, MSB first
        /// </summary>
        /// <param name="position"></param>
        /// <param name="bitLength"></param>
        /// <returns></returns>
        public uint ReadFrom(ref uint position, uint bitLength)
        {
            uint temp = Position; // save internal state
            Position = position;
            uint value = Read(bitLength);
            position = Position; // update external value
            Position = temp; // restore internal
            return value;
        }

        /// <summary>
        /// Write the value to the bitstream in the minimum number of bits.
        /// Writes 0 using 1 bit.
        /// </summary>
        /// <param name="value"></param>
        public void Write(uint value)
        {
            if (value != 0)
                Write(value,CodecBase.BitsRequired(value));
            else
                Write(0,1);
        }


        /// <summary>
        /// Read values from current position, MSB first
        /// </summary>
        /// <param name="bitLength"></param>
        /// <returns></returns>
        public uint Read(uint bitLength)
        {
            var value = 0U;
            for (var i = 0; i < bitLength; ++i)
            {
                var b = (uint)(bits[(int)Position++]&1);
                Write(b.ToString());
                value <<= 1;
                value |= b;
                //value |= b<<i;
            }
            return value;
        }


        /// <summary>
        /// Convert bitstream to bytes, padding with 0 as needed to fill last byte
        /// </summary>
        /// <returns></returns>
        public byte[] GetBytes()
        {
            var len = (bits.Count + 7)/8;
            var data = new byte[len];
            for (var i = 0; i < len*8; i += 8)
            {
                var val = 0;
                for (var j = 0; j < 8; ++j)
                {
#if false
                    // MSB
                    var t = (i + j < bits.Count) ? bits[i + j] : 0;
                    val |= t << j;
#else
                    // LSB - needed for range coding to work cleanly
                    var t = (i + j < bits.Count) ? bits[i + j] : 0;
                    val <<= 1;
                    val |= t & 1;
#endif
                }
                data[i/8] = (byte)val;
            }
            return data;
        }

        public void InsertStream(uint insertPosition, Bitstream bitstream)
        {
            bits.InsertRange((int)insertPosition,bitstream.bits);
            // todo - position update?
        }


        // todo - make optimized later, one byte per bit ok for now
        readonly List<byte> bits = new List<byte>();

    }
}
