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
    public static class Filters
    {
        /// <summary>
        /// Remap to unique values, return unique values (sorted) and the remapped data
        /// </summary>
        /// <param name="data"></param>
        /// <param name="unique"></param>
        /// <returns></returns>
        public static byte[] UniqueBytes(byte[] data, out byte [] unique)
        {
            var present = new bool[256];
            foreach (var b in data)
                present[b] = true;
            var count = present.Count(b=>b);
            unique = new byte[count];
            var j = 0;
            for (var i = 0; i < 256; ++i)
                if (present[i])
                    unique[j++] = (byte)i;
            // remap
            var result = new byte[data.Length];
            for (var i = 0; i < data.Length; ++i)
                result[i] = (byte)Array.IndexOf(unique,data[i]);

            return result;
        }

        /// <summary>
        /// replace each entry with the diff from the prev one
        /// first one is left alone. Return deltas
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static Datastream DeltaCode(Datastream data)
        {
            var d = new Datastream(data);
            for (var i = data.Count - 1; i >= 1; --i)
                d[i] = (byte)(data[i] - data[i - 1]);
            d[0] = data[0];
            return d;
        }
    }
}
