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
using System.Linq;

namespace Lomont.Compression.Codec
{
    public class Datastream : List<uint>
    {
        public Datastream(IEnumerable<byte> data)
        {
            AddRange(data.Select(b=>(uint)b));
        }

        public Datastream(IEnumerable<uint> data)
        {
            AddRange(data);
        }

        public Datastream()
        {
        }

        public byte [] ToByteArray()
        {
            var list = new List<byte>();
            Trace.Assert(this.Max() < 256);  // for now, only convert actual byte valued items
            foreach (var symbol in this)
            {
                uint value = symbol;
                do
                {
                    list.Add((byte)value);
                    value >>= 8;
                } while (value > 0);
            }

            return list.ToArray();
        }

    }
}
