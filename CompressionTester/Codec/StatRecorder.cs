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
using System.IO;
using System.Linq;

namespace Lomont.Compression.Codec
{
    /// <summary>
    /// Class to log items by name
    /// </summary>
    public static class StatRecorder
    {
        private static readonly Dictionary<string,ulong> Log = new Dictionary<string, ulong>();
        public static void AddStat(string name, uint value)
        {
            if (!Log.ContainsKey(name))
                Log.Add(name,0);
            Log[name] += value;
        }

        public static void DumpLog(TextWriter output)
        {
            if (Log.Count == 0 || output == null) return;
            var maxNameLength = Log.Keys.Max(n => n.Length);
            var maxVal = Log.Values.Max();
            var maxValLength = (uint) Math.Ceiling(Math.Log10(maxVal + 1));
            var format = "{0,-"+(maxNameLength+1) + "} => {1,"+(maxValLength)+"}";
            foreach (var item in Log.OrderBy(d=>d.Key))
                output.WriteLine(format,item.Key,item.Value);
        }
    }
}
