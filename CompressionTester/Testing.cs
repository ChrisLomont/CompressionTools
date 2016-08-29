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
using System.IO;
using System.Linq;
using Lomont.Compression.Codec;

namespace Lomont.Compression
{
    public class Testing : Output
    {
        public class Request
        {
            /// <summary>
            /// Codecs to test
            /// </summary>
            public List<CodecBase> Codecs { get; set;  } = new List<CodecBase>();
            /// <summary>
            /// A single codec to test
            /// </summary>
            public CodecBase Codec { get; set; }
            /// <summary>
            /// Single data array to test
            /// </summary>
            public byte[] TestData { get; set; }
            /// <summary>
            /// A filename to test, or a file pattern when used with path
            /// </summary>
            public string Filename { get; set;  }
            /// <summary>
            /// Path for recursive file
            /// </summary>
            public string Path { get; set; }
            /// <summary>
            /// Recurse directories?
            /// </summary>
            public bool RecurseFiles { get; set; } = true;
            /// <summary>
            /// Also decompress the compressed data
            /// </summary>
            public bool Decompress { get; set; } = true;
            /// <summary>
            /// Use the C decompressor
            /// </summary>
            public bool UseExternalDecompressor { get; set; } = false;
            /// <summary>
            /// Show only errors, else show one line per test
            /// </summary>
            public bool ShowOnlyErrors { get; set; } = false;
            /// <summary>
            /// Trap exceptions, otherwise let fall through
            /// </summary>
            public bool TrapErrors { get; set; } = true;
        }

        /// <summary>
        /// per test item result
        /// </summary>
        public class Result
        {
            public string DataFilename { get; set; }
            public CodecBase Codec { get; set; }
            public bool Success { get; set; }
            public long UncompressedLength { get; set; }
            public long CompressedLength { get; set; }
            public long CompressionTicks { get; set; }
            public long DecompressionTicks { get; set; }
            public double Ratio => (double)CompressedLength / (UncompressedLength > 0 ? UncompressedLength : 1);
        }

        /// <summary>
        /// Store stats for decompression items
        /// </summary>
        public class Stats
        {
            public ulong Failed { get; set; }
            public ulong Succeeded { get; set; }
            public ulong TotalBytesData { get; set; }
            public ulong TotalCompressed { get; set; }
        }

        void DumpStats(Stats stats)
        {
            var ratio1 = (double)stats.TotalCompressed / stats.TotalBytesData;
            WriteLine(
                $"Succeeded {stats.Succeeded}, failed {stats.Failed}, data bytes {stats.TotalBytesData}, compressed bytes {stats.TotalCompressed}, ratio {ratio1:F3}");
        }

        // items to show (when relevant):
        // codec, success, uncompressed size, compressed size, compression ratio, compression ticks, decompression ticks, filesize, filename
        private void DumpResult(Result result, CodecBase codec, string filename)
        {
            var length = 0L;
            if (File.Exists(filename))
            {
                var fi = new FileInfo(filename);
                length = fi.Length;
            }
            var codecName = codec.CodecName;//GetType().Name;
            Write($"{codecName,10}, {result.Success, 5}, {result.UncompressedLength,8}, {result.CompressedLength,8}, {result.Ratio:F3}, ");
            Write($"{result.CompressionTicks,10}, {result.DecompressionTicks,10}, ");
            Write($"{length,10}, {filename}, ");
            WriteLine();
        }

        public void DoTest(Request request)
        {
            WriteLine("codec, success, uncompressed size, compressed size, compression ratio, compression ticks, decompression ticks, filesize, filename,");

            var stats = new Stats();
            var results = new List<Result>();

            foreach (var dataSet in GetItems(request))
            {
                var codec = dataSet.Item1;
                var data = dataSet.Item2;
                var filename = dataSet.Item3;

                try
                {
                    var result = TestDataRoundtrip(request, codec, data);
                    result.Codec = codec;
                    if (String.IsNullOrEmpty(codec.CodecName))
                        codec.CodecName = codec.GetType().Name;
                    codec.CodecName = codec.CodecName.Replace("Codec", "");

                    result.DataFilename = filename;
                    results.Add(result);

                    if (!result.Success)
                    {
                        DumpResult(result, codec, filename);
                        stats.Failed++;
                    }
                    else
                    {
                        stats.Succeeded++;
                        stats.TotalBytesData += (ulong)result.UncompressedLength;
                        stats.TotalCompressed += (ulong)result.CompressedLength;
                    }
                    if (!request.ShowOnlyErrors && result.Success)
                        DumpResult(result, codec, filename);

                }
                // public bool TrapErrors { get; set; }
                catch (Exception ex)
                {
                    stats.Failed++;
                    var length = 0L;
                    if (!String.IsNullOrEmpty(filename) && File.Exists(filename))
                        length = new FileInfo(filename).Length;
                    WriteLine($"EXCEPTION: {filename,30} ({length,10}): " + ex);
                    if (!request.TrapErrors)
                        throw;
                }

                if (Console.KeyAvailable)
                {
                    while (Console.KeyAvailable)
                        Console.ReadKey(true);
                    DumpStats(stats);
                }

            }
            // final summary
            DumpStats(stats);

            // final table
            DumpTable(results,stats);

            // anything logged
            StatRecorder.DumpLog(OutputWriter);
        }

        public void DumpTable(List<Result> results, Stats stats)
        {
            var m = new MarkdownTable();

            // on left are files, on top are codecs, show file sizes, compression ratios and file sizes, and timing
            var codecNames = results.Select(r => r.Codec.CodecName).OrderBy(s => s).Distinct().ToList();
            var filenames = results.Select(r => r.DataFilename).OrderBy(s => s).Distinct().ToList();

            m.AddColumn("Filename");
            var fileSizeColumns = new List<int>();
            var fileRatioColumns = new List<int>();

            fileSizeColumns.Add(m.AddColumn("File size"));
            foreach (var name in codecNames)
            {
                fileRatioColumns.Add(m.AddColumn(name + " ratio"));
                fileSizeColumns.Add(m.AddColumn(name + " size"));
            }
            foreach (var filename in filenames)
                m.AddRow(filename);

            m.AddRow("Summary");

            // file sizes
            for (var j = 0; j < filenames.Count; ++j)
                m.SetCell(j + 1,
                    fileSizeColumns[0], results.First(r => r.DataFilename == filenames[j]).UncompressedLength.ToString());

            // place results
            foreach (var r in results)
            {
                var i = codecNames.IndexOf(r.Codec.CodecName);
                var j = filenames.IndexOf(r.DataFilename);
                m.SetCell(j + 1, fileRatioColumns[i], $"{(double)r.CompressedLength / r.UncompressedLength:F3}");
                m.SetCell(j + 1, fileSizeColumns[i + 1], $"{r.CompressedLength}");
            }

            // sum each column
            foreach (var col in fileSizeColumns)
                m.SumCol(1, m.Height - 2, col);

            // bottom ratios
            var total = m.ToInt(m.Height - 1, fileSizeColumns[0]);
            for (var i = 1; i < fileSizeColumns.Count; ++i)
            {
                var s = m.ToInt(m.Height - 1, fileSizeColumns[i]);
                m.SetCell(m.Height - 1, fileRatioColumns[i - 1], $"{(double)s / total:F3}");
            }

            // mark best in each row
            for (var row = 1; row < m.Height; ++row)
            {
                var vals = fileSizeColumns.Select(col => m.ToInt(row, col)).ToArray();
                var min = vals.Min();
                var start = 0;
                while (true)
                {
                    var index = Array.IndexOf(vals, min, start);
                    start = index + 1;
                    if (index < 0) break;
                    m.MarkCell(row, fileSizeColumns[index], true);
                    if (index != 0)
                        m.MarkCell(row, fileRatioColumns[index - 1], true);
                }
            }
            WriteLine("Final table");
            WriteLine();
            m.Write(OutputWriter);
        }

        // get codecs, data[] items, and filenames
        public static IEnumerable<Tuple<CodecBase,byte[],string>> GetItems(Request request)
        {
            List<CodecBase> codecs = new List<CodecBase>();
            codecs.AddRange(request.Codecs);
            if (request.Codec != null)
                codecs.Add(request.Codec);

            // process any test data
            if (request.TestData != null)
            {
                var data = request.TestData;
                foreach (var codec in codecs)
                    yield return new Tuple<CodecBase, byte[],string>(codec,data,"");
            }

            int fileMaxSize = 1000000;

            // process any files
            if (!String.IsNullOrEmpty(request.Path))
            { // do files in path

                var pathsToParse = new Queue<string>();
                var filePattern = String.IsNullOrEmpty(request.Filename) ? "*.*" : request.Filename;
                pathsToParse.Enqueue(request.Path); // initial path

                while (pathsToParse.Count > 0)
                {
                    var path = pathsToParse.Dequeue();
                    foreach (var filename in Directory.GetFiles(path, filePattern))
                    {
                        var fi = new FileInfo(filename);
                        if (fi.Length > fileMaxSize || 0 == fi.Length)
                            continue;

                        var data = File.ReadAllBytes(filename);

                        foreach (var codec in codecs)
                            yield return new Tuple<CodecBase, byte[], string>(codec, data, filename);
                    }
                    if (request.RecurseFiles)
                    {
                        foreach (var dir in Directory.GetDirectories(path))
                            pathsToParse.Enqueue(dir);
                    }
                }
            }
            else if (!String.IsNullOrEmpty(request.Filename))
            { // do single file

                var filename = request.Filename;
                var fi = new FileInfo(filename);
                if (fi.Length <= fileMaxSize && 0 != fi.Length)
                {
                    var data = File.ReadAllBytes(filename);
                    foreach (var codec in codecs)
                        yield return new Tuple<CodecBase, byte[], string>(codec, data, filename);
                }
            }
        }

        /// <summary>
        /// Test data round-trip.
        /// Gather info about it and return in result
        /// </summary>
        /// <param name="request"></param>
        /// <param name="codec"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        Result TestDataRoundtrip(Request request, CodecBase codec, byte [] data)
        {
            var result = new Result();
            var timer = new Stopwatch();
            timer.Start();
            var compressed = codec.Compress(data);
            timer.Stop();
            result.CompressionTicks = timer.ElapsedTicks;
            result.CompressedLength = compressed?.Length ?? 0;
            result.UncompressedLength = data.Length;

            result.Success = true; // assume succeeded
            if (request.Decompress)
            {
                timer.Reset();

                byte[] decompressed;
                if (request.UseExternalDecompressor)
                {
                    byte canary = (byte)(data.Length^0x5A); // check survives
                    var decompressedCanary = new byte[data.Length+1];
                    decompressedCanary[decompressedCanary.Length - 1] = canary;
                    if (codec.GetType() == typeof(HuffmanCodec) && compressed != null)
                    {
                        timer.Start();
                        NativeMethods.DecompressHuffman(compressed, compressed.Length, decompressedCanary,
                            decompressedCanary.Length - 1);
                        timer.Stop();
                    }
                    else if (codec.GetType() == typeof(Lz77Codec) && compressed != null)
                    {
                        timer.Start();
                        NativeMethods.DecompressLZ77(compressed, compressed.Length, decompressedCanary,
                            decompressedCanary.Length - 1);
                        timer.Stop();
                    }
                    else if (codec.GetType() == typeof(ArithmeticCodec) && compressed != null)
                    {
                        timer.Start();
                        NativeMethods.DecompressArithmetic(compressed, compressed.Length, decompressedCanary,
                            decompressedCanary.Length - 1);
                        timer.Stop();
                    }
                    else if (codec.GetType() == typeof(LzclCodec) && compressed != null)
                    {
                        timer.Start();
                        NativeMethods.DecompressLZCL(compressed, compressed.Length, decompressedCanary, 
                            decompressedCanary.Length - 1);
                        timer.Stop();
                    }
                    if (decompressedCanary[decompressedCanary.Length - 1] != canary)
                        throw new Exception("Decompression canary overwritten!");
                    // must shrink decompressed to proper size for compare
                    decompressed = new byte[data.Length];
                    Array.Copy(decompressedCanary, decompressed,decompressed.Length);
                }
                else
                {
                    timer.Start();
                    decompressed = codec.Decompress(compressed);
                    timer.Stop();
                }
                result.DecompressionTicks = timer.ElapsedTicks;
                result.Success = CodecBase.CompareBytes(data, decompressed);
            }
            return result;
        }

        public static void Optimize(byte [] data)
        {
            var entropy = CodecBase.ComputeShannonEntropy(data);
            var ratio = entropy / 8.0;
            Console.WriteLine($"Shannon entropy {entropy:F3} -> ratio {ratio:F3}");

            //var codec = new LZCLCodec();
            //codec.Options |= LZCLCodec.OptionFlags.DumpOptimize;
            //codec.OutputWriter = Console.Out;
            //codec.Optimize(data);
        }

#region Universal Codes testing

        /// <summary>
        /// Form of a universal encode function
        /// </summary>
        delegate void UniversalEncoder(Bitstream bitstream, uint value);
        /// <summary>
        /// Form of a universal encode function
        /// </summary>
        delegate uint UniversalDecoder(Bitstream bitstream);

    
        class UniversalData
        {
            public UniversalData(string name, UniversalEncoder encoder, UniversalDecoder decoder)
            {
                Name = name;
                Encode = encoder;
                Decode = decoder;
            }
            public string Name { get; }
            public UniversalEncoder Encode { get; }
            public UniversalDecoder Decode { get; }
        }

        static readonly UniversalData[] UniversalCodes = {
#if true
            new UniversalData("Binary",(b,v)=>b.Write(v), null),

            new UniversalData("Elias Gamma",UniversalCodec.Elias.EncodeGamma, UniversalCodec.Elias.DecodeGamma),
            new UniversalData("Elias Delta",UniversalCodec.Elias.EncodeDelta, UniversalCodec.Elias.DecodeDelta),
            new UniversalData("Elias Omega",UniversalCodec.Elias.EncodeOmega, UniversalCodec.Elias.DecodeOmega),

            new UniversalData("Even-Rodeh",UniversalCodec.EvenRodeh.Encode, UniversalCodec.EvenRodeh.Decode),

            new UniversalData("Stout(3)",(b,v)=>UniversalCodec.Stout.Encode(b,v,3),b=>UniversalCodec.Stout.Decode(b,3)),
            new UniversalData("Stout(4)",(b,v)=>UniversalCodec.Stout.Encode(b,v,4),b=>UniversalCodec.Stout.Decode(b,4)),
            new UniversalData("Stout(5)",(b,v)=>UniversalCodec.Stout.Encode(b,v,5),b=>UniversalCodec.Stout.Decode(b,5)),
            new UniversalData("Stout(6)",(b,v)=>UniversalCodec.Stout.Encode(b,v,6),b=>UniversalCodec.Stout.Decode(b,6)),
            new UniversalData("Stout(7)",(b,v)=>UniversalCodec.Stout.Encode(b,v,7),b=>UniversalCodec.Stout.Decode(b,7)),
            new UniversalData("Stout(8)",(b,v)=>UniversalCodec.Stout.Encode(b,v,8),b=>UniversalCodec.Stout.Decode(b,8)),
            new UniversalData("Stout(9)",(b,v)=>UniversalCodec.Stout.Encode(b,v,9),b=>UniversalCodec.Stout.Decode(b,9)),
            new UniversalData("Stout(10)",(b,v)=>UniversalCodec.Stout.Encode(b,v,10),b=>UniversalCodec.Stout.Decode(b,10)),
            new UniversalData("Stout(11)",(b,v)=>UniversalCodec.Stout.Encode(b,v,11),b=>UniversalCodec.Stout.Decode(b,11)),
            new UniversalData("Stout(12)",(b,v)=>UniversalCodec.Stout.Encode(b,v,12),b=>UniversalCodec.Stout.Decode(b,12)),

            new UniversalData("Lomont1(3)", (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,3,0), b=>UniversalCodec.Lomont.DecodeLomont1(b,3,0)),
            new UniversalData("Lomont1(4)", (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,4,0), b=>UniversalCodec.Lomont.DecodeLomont1(b,4,0)),
            new UniversalData("Lomont1(5)", (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,5,0), b=>UniversalCodec.Lomont.DecodeLomont1(b,5,0)),
            new UniversalData("Lomont1(6)", (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,6,0), b=>UniversalCodec.Lomont.DecodeLomont1(b,6,0)),
            new UniversalData("Lomont1(7)", (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,7,0), b=>UniversalCodec.Lomont.DecodeLomont1(b,7,0)),
            new UniversalData("Lomont1(8)", (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,8,0), b=>UniversalCodec.Lomont.DecodeLomont1(b,8,0)),
            new UniversalData("Lomont1(9)", (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,9,0), b=>UniversalCodec.Lomont.DecodeLomont1(b,9,0)),
            new UniversalData("Lomont1(10)",(b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,10,0),b=>UniversalCodec.Lomont.DecodeLomont1(b,10,0)),
            new UniversalData("Lomont1(11)",(b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,11,0),b=>UniversalCodec.Lomont.DecodeLomont1(b,11,0)),
            new UniversalData("Lomont1(12)",(b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,12,0),b=>UniversalCodec.Lomont.DecodeLomont1(b,12,0)),
            new UniversalData("Lomont1(13)",(b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,13,0),b=>UniversalCodec.Lomont.DecodeLomont1(b,13,0))

//            new UniversalData("GolumbExp(0)",(b,v)=>UniversalCodec.Golomb.EncodeExp(b,v,0),b=>UniversalCodec.Golomb.DecodeExp(b,0)),
//            new UniversalData("GolumbExp(3)",(b,v)=>UniversalCodec.Golomb.EncodeExp(b,v,3),b=>UniversalCodec.Golomb.DecodeExp(b,3)),
//            new UniversalData("GolumbExp(4)",(b,v)=>UniversalCodec.Golomb.EncodeExp(b,v,4),b=>UniversalCodec.Golomb.DecodeExp(b,4)),
//            new UniversalData("GolumbExp(5)",(b,v)=>UniversalCodec.Golomb.EncodeExp(b,v,5),b=>UniversalCodec.Golomb.DecodeExp(b,5)),
//            new UniversalData("GolumbExp(6)",(b,v)=>UniversalCodec.Golomb.EncodeExp(b,v,6),b=>UniversalCodec.Golomb.DecodeExp(b,6)),
//            new UniversalData("GolumbExp(7)",(b,v)=>UniversalCodec.Golomb.EncodeExp(b,v,7),b=>UniversalCodec.Golomb.DecodeExp(b,7)),
//            new UniversalData("GolumbExp(8)",(b,v)=>UniversalCodec.Golomb.EncodeExp(b,v,8),b=>UniversalCodec.Golomb.DecodeExp(b,8)),
//
//            new UniversalData("Golumb(7)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,7),b=>UniversalCodec.Golomb.Decode(b,7)),
//            new UniversalData("Golumb(8)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,8),b=>UniversalCodec.Golomb.Decode(b,8)),
//            new UniversalData("Golumb(9)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,9),b=>UniversalCodec.Golomb.Decode(b,9)),
//            new UniversalData("Golumb(10)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,10),b=>UniversalCodec.Golomb.Decode(b,10)),
//            new UniversalData("Golumb(11)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,11),b=>UniversalCodec.Golomb.Decode(b,11)),
//            new UniversalData("Golumb(12)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,12),b=>UniversalCodec.Golomb.Decode(b,12)),
//            new UniversalData("Golumb(13)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,13),b=>UniversalCodec.Golomb.Decode(b,13)),
//            new UniversalData("Golumb(14)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,14),b=>UniversalCodec.Golomb.Decode(b,14)),
//            new UniversalData("Golumb(15)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,15),b=>UniversalCodec.Golomb.Decode(b,15)),
//            new UniversalData("Golumb(16)",(b,v)=>UniversalCodec.Golomb.Encode(b,v,16),b=>UniversalCodec.Golomb.Decode(b,16)),


#endif
//            new UniversalData("Truncated(3)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,3),b=>UniversalCodec.Truncated.Decode(b,3)),
//            new UniversalData("Truncated(4)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,4),b=>UniversalCodec.Truncated.Decode(b,4)),
//            new UniversalData("Truncated(5)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,5),b=>UniversalCodec.Truncated.Decode(b,5)),
//            new UniversalData("Truncated(6)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,6),b=>UniversalCodec.Truncated.Decode(b,6)),
//            new UniversalData("Truncated(7)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,7),b=>UniversalCodec.Truncated.Decode(b,7)),
//            new UniversalData("Truncated(8)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,8),b=>UniversalCodec.Truncated.Decode(b,8)),
//            new UniversalData("Truncated(9)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,9),b=>UniversalCodec.Truncated.Decode(b,9)),
//            new UniversalData("Truncated(10)",(b,v)=>UniversalCodec.Truncated.Encode(b,v,10),b=>UniversalCodec.Truncated.Decode(b,10)),

        };

        void TestUniversalCodes(Bitstream [] bitstreams, uint value)
        {
            for (var i = 0; i < bitstreams.Length; ++i)
            {
                var code = UniversalCodes[i];
                var bitstream = bitstreams[i];
                var prePosition = bitstream.Position;
                code.Encode(bitstreams[i], value);
                var postPostion = bitstream.Position;
                if (code.Decode != null)
                { // try to decode, check results
                    bitstream.Position = prePosition;
                    var decodedValue = code.Decode(bitstream);
                    if (decodedValue != value)
                        WriteLine($"ERROR: Universal code {UniversalCodes[i].Name} converted {value} to {decodedValue}");
                    Trace.Assert(decodedValue == value);
                    Trace.Assert(postPostion == bitstream.Position);
                }
            }
        }

        public Bitstream[] TestUniversalCode(uint min, uint max, Func<uint,uint> increment = null, bool dumpResults = true)
        {
            if (increment == null)
                increment = n => n + 1;
            var bitstreams = new Bitstream[UniversalCodes.Length];
            for (var i = 0; i < bitstreams.Length; i++)
                bitstreams[i] = new Bitstream();
            for (uint value = min; value <= max; value = increment(value))
                TestUniversalCodes(bitstreams, value);
            // output results
            if (dumpResults)
                DumpCodeResults(bitstreams);
            return bitstreams;
        }

        void DumpCodeResults(Bitstream [] bitstreams)
        {
            //WriteLine($"[{min},{max}]: gamma {eg.Length} delta {ed.Length} omega {eo.Length} base {minB}");
            WriteLine("Universal code results: name, length, decoded");
            for (var i = 0; i < bitstreams.Length; ++i)
            {
                var decoded = UniversalCodes[i].Decode != null;
                var ratio = (double) bitstreams[i].Length/bitstreams[0].Length;
                WriteLine($"   {UniversalCodes[i].Name,-20}, {bitstreams[i].Length,10}, {ratio:F3}, {decoded},");
            }
        }

        void ShowBestCode(uint min, uint max, Func<uint, uint> increment, string name)
        {
            var bs = TestUniversalCode(min, max, increment,false);
            var results = bs.OrderBy(v => v.Length).ToList();
#if false
            var bestIndex = Array.IndexOf(bs, results[1]); // result 1, 0 is the default binary encoding
            var best = universalCodes[bestIndex];
            var ratio = (double) bs[bestIndex].Length/bs[0].Length;
            WriteLine($"Best {name} is {best.Name} with ratio {ratio:F3}");
#endif
            var best5 = results.Select(r => 
            new {
                ratio = (double) r.Length/results[0].Length,
                name = UniversalCodes[Array.IndexOf(bs,r)].Name
            }).ToList();

            WriteLine($"Best {name}:  ");
            for (var i =0; i < 5; ++i)
                WriteLine($"   {best5[i].name,-15} = {best5[i].ratio:F3}");
        }

        public void TestUniversalCodeBattery()
        {
            ShowBestCode(1, 12, n => n + 1, "1-12 by +1");
            ShowBestCode(1, 256,n=>n+1,"1-256 by +1");
            ShowBestCode(1, 1024, n => n + 1, "1-1024 by +1");
            ShowBestCode(8, 1<<28, n => 9*n/8, "1-1<<28 by *9/8");
            ShowBestCode(10, 1000000000, n => 10*n, "1-1000000000 by *10");
            ShowBestCode(30000, 35000, n => n+7, "30000-35000 by +7");
        }


        public void TestUniversalOnFiles(string path, int depth = 0, Bitstream[] bitstreams = null)
        {
            if (bitstreams == null)
            {
                bitstreams = new Bitstream[UniversalCodes.Length];
                for (var i = 0; i < bitstreams.Length; ++i)
                    bitstreams[i] = new Bitstream();
            }

            foreach (var file in Directory.GetFiles(path, "*.*"))
            {
                var fi = new FileInfo(file);
                if (fi.Length == 0 || fi.Length > 15000000)
                    continue;
                var size = (uint) fi.Length;
                TestUniversalCodes(bitstreams,size);
                if (Console.KeyAvailable)
                {
                    while (Console.KeyAvailable)
                        Console.ReadKey(true);
                    DumpCodeResults(bitstreams);
                }
            }

            foreach (var newPath in Directory.GetDirectories(path))
                TestUniversalOnFiles(newPath, depth+1,bitstreams);

            if (depth == 0)
                DumpCodeResults(bitstreams);
        }

        #endregion

        #region Miscellaneous testing

        public void DumpCode()
        {   // checked: Elias delta, gamma, omega, golomb, truncated
            for (var n = 2U; n < 31; ++n)
                for (var i = 0U; i <= 1000; ++i)
                {
                    var index = i;
                    var bs = new Bitstream();
                    UniversalCodec.Stout.Encode(bs, index, n);
                    // Console.WriteLine($"{index}->{bs}");
                    bs.Position = 0;
                    var ans = UniversalCodec.Stout.Decode(bs, n);
                    //Console.WriteLine($"{index}->{bs}->{ans}");
                    Trace.Assert(ans == index);
                }
            WriteLine("Code check done");
        }

        public void OutLogo()
        {
            //var codec = new HuffmanCodec();
            var codec = new Lz77Codec
            {
                MaximumDistance = 85,
                MaximumLength = 12,
                MinimumLength = 2
            };

            var data = DataSets.HypnoLightLogo;
            var compressed = codec.Compress(data);

            var ratio = (double)compressed.Length/data.Length;
            WriteLine($"// codec {codec.GetType().Name}, {data.Length} bytes to {compressed.Length}, ratio {ratio:F3}");
            Utility.FormatCode(compressed, "logo","C","");
        }

        public void TestGolomb()
        {
            var data = new Datastream();
            for (var i = 0U; i < 1000; ++i)
                data.Add(i);
            var last = UInt32.MaxValue;
            for (var m = 1U; m < 1000; ++m)
            {
                var g = new GolombCodec();
                g.Options &= ~GolombCodec.OptionFlags.Optimize;
                g.Parameter = m;
                var bs = g.CompressToStream(data, Header.HeaderFlags.None);
                var len = bs.Length;
                var dir = len < last ? -1 : len == last ? 0 : 1;
                WriteLine($"{m,3}: {bs.Length} {dir}");
                last = len;
            }
        }


        #endregion



        // some paths useful for testing recursive file scans
        public static string[] Paths =
        {
            @"..\Corpus",
            @"..\Corpus\Calgary",
            @"..\Corpus\Cantebury",
            @"..\..\",
            @"D:\programming\cstuff2"
        };
    }
}
