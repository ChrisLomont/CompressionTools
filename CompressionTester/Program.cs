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
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using Lomont.Compression.Codec;

/* 
TODO:
1. Options
   - pathname, filename pattern
   - output to code or files
   - testing options
   - reflection to get options for each codec
   - reflection to get codecs
   - stress testing of a codec

2. options and codecs are tagged with attributes to get info, make generic
3. generic way to include filters in pipelines
4. on group testing output final nice table of all results: top is codecs, left is files, cols are entries, CSV
5. Finish simple C versions

*/

namespace Lomont.Compression
{
    class Program
    {
        class Options
        {
            public string Inputfile { get; set; } = "";
            public string Outputfile { get; set; } = "";
            public bool RecurseDirectories { get; set; }
            public int OutputFormatIndex { get; set; } = -1;
            public List<int> CodecIndices { get; } = new List<int>();
            public List<string> CodecParameters { get; } = new List<string>();
            public bool Verbose { get; set; }
            public bool Decompress { get; set; }
            public bool Testing { get; set; }
        }

        static void ShowParameters()
        {
            Console.WriteLine("    The following codec have parameters that can be selected.");
            Console.WriteLine("    Select as: -c LZ77:param1=value1:param2=value2");

            var props = Utility.GetPropertiesWith<CodecParameterAttribute>().ToList();

            foreach (var property in props)
            {
                var attrs = property.GetCustomAttributes(typeof(CodecParameterAttribute),true);
                foreach (var a in attrs.Select(a1 => a1 as CodecParameterAttribute))
                {
                    var containingTypeName = property.DeclaringType.Name;
                    containingTypeName = containingTypeName.Replace("Codec", "");
                    Console.WriteLine($"           {containingTypeName,7}: {a.SymbolName,8} - {a.Description}");
                }
            }
        }


        static void Usage()
        {
            
            Func<string[],string> writeArray = arr =>
            {
                return arr.Aggregate((a, b) => a+" " + b);
            };
            Console.WriteLine("Usage:");
            Console.WriteLine(" -i inputfile (can be path to do directory with recurse if no output file)");
            Console.WriteLine(" -o outputfile (if none, no output files)");
            Console.WriteLine(" -r recurse directories. No outputs");
            Console.WriteLine($" -f output formats : {writeArray(OutputFormats)}");
            Console.WriteLine($" -c codecs         : {writeArray(CodecNames)}");
            ShowParameters();
            // todo - allow parameters like LZ77:minLen=a,b,c:maxLen=a,b,c...
            // where a,b,c is min, max, increment step as +1 or *2 or such
            Console.WriteLine(" -v verbose");
            //Console.WriteLine($" // -b best(optimize) ");
            Console.WriteLine(" -t test - run current test set");
            Console.WriteLine(" -d decompress, else compress");
        }

        private static readonly string[] OutputFormats = {"C#", "C", "Binary"};
        private static string[] codecNamesBacking;
        private static string [] CodecNames {
            get
            {
                return codecNamesBacking ?? (codecNamesBacking = CodecTypes.
                           Select(t => t.Name.Replace("Codec", "")).
                           ToArray());
            }
        }
        private static Type[] codecTypes;
        private static Type[] CodecTypes => codecTypes ?? (codecTypes = Utility.GetTypesWith<CodecAttribute>(true).ToArray());

        static Options ParseOptions(string[] args)
        {
            var opts = new Options();
            var i = 0;
            while (i < args.Length)
            {
                switch (args[i++].ToLower())
                {
                    case "-i":
                        opts.Inputfile = args[i++];
                        break;
                    case "-o":
                        opts.Outputfile = args[i++];
                        break;
                    case "-r":
                        opts.RecurseDirectories = true;
                        break;
                    case "-f":
                        opts.OutputFormatIndex = Array.IndexOf(OutputFormats,args[i++]);
                        if (opts.OutputFormatIndex < 0)
                            Console.Error.WriteLine($"Invalid output format {args[i-1]}");
                        break;
                    case "-c":
                        var codecText = args[i++];
                        var splitIndex = codecText.IndexOf(":");
                        string codecName, codecParam = "";
                        if (splitIndex < 0)
                            codecName = codecText;
                        else
                        {
                            codecName = codecText.Substring(0, splitIndex);
                            codecParam = codecText.Substring(splitIndex+1);
                        }
                        
                        var codecIndex = Array.IndexOf(CodecNames, codecName);
                        if (codecIndex < 0)
                            Console.Error.WriteLine($"Invalid codec {args[i - 1]}");
                        else
                            opts.CodecIndices.Add(codecIndex);
                        opts.CodecParameters.Add(codecParam);
                        break;
                    case "-v":
                        opts.Verbose = true;
                        break;
                    case "-d":
                        opts.Decompress = true;
                        break;
                    case "-t":
                        opts.Testing = true;
                        break;
                }
            }
            if (opts.OutputFormatIndex == -1)
                opts.OutputFormatIndex = Array.IndexOf(OutputFormats,"Binary");
            return opts;
        }

        static void OutputData(string filename, byte[] data, string format, string headerMessage)
        {
            if (format == "Binary")
            {
                File.WriteAllBytes(filename, data);
            }
            else
            {
                var itemName = String.IsNullOrEmpty(filename) ? "data" : Path.GetFileNameWithoutExtension(filename);
                var output = Utility.FormatCode(data, itemName, format, headerMessage);
                if (!string.IsNullOrEmpty(filename))
                    File.WriteAllText(filename, output);
                else
                    Console.WriteLine(output);
            }


        }


        static int PerformCommand(Options opts)
        {
            if (opts.Testing)
            {
                DoTesting();
                return 1;
            }
            // create list of codecs
            var codecs = new List<CodecBase>();
            for (var i = 0;  i < opts.CodecIndices.Count; ++i)
            {
                var ci = opts.CodecIndices[i];
                CodecBase codec = null;
                switch (CodecNames[ci])
                {
                    case "Arithmetic":
                        codec = new ArithmeticCodec();
                        break;
                    case "Huffman":
                        codec = new HuffmanCodec();
                        break;
                    case "Lz77":
                        codec = new Lz77Codec();
                        break;
                    case "Lzcl":
                        codec = new LzclCodec();
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown codec {CodecNames[ci]}");
                        break;
                }
                ApplyParameters(codec,opts.CodecParameters[i]);
                if (codec != null)
                    codecs.Add(codec);
            }
            if (!codecs.Any())
            {
                Console.Error.WriteLine("No codec(s) specified");
                return -2;
            }
            if (opts.Verbose)
            {
                foreach (var c in codecs)
                {
                    c.OutputWriter = Console.Out;
                    // todo - flags?
                }
            }


            // if has input file and output file then do that
            if (!String.IsNullOrEmpty(opts.Inputfile) && !String.IsNullOrEmpty(opts.Outputfile))
            {
                if (!File.Exists(opts.Inputfile))
                {
                    Console.Error.WriteLine($"File {opts.Inputfile} does not exist");
                    Environment.Exit(-3);
                }
                // do first codec only
                var data = File.ReadAllBytes(opts.Inputfile);
                var output = opts.Decompress ? codecs[0].Decompress(data) : codecs[0].Compress(data);
                var headerMsg = $"File {opts.Inputfile} compressed from {data.Length} to {output.Length} for {(double)output.Length/data.Length:F3} ratio";
                if (codecs[0] is Lz77Codec)
                {
                    var lz77 = codecs[0] as Lz77Codec;
                    var bufSize = Math.Max(lz77.MaximumDistance,lz77.MaximumLength)+1;
                    headerMsg += $" LZ77 bufsize {bufSize}";
                }
                if (codecs[0] is LzclCodec)
                {
                    var lzcl = codecs[0] as LzclCodec;
                    var bufSize = Math.Max(lzcl.MaximumDistance, lzcl.MaximumLength)+1;
                    headerMsg += $" LZCL bufsize {bufSize}";
                }
                OutputData(opts.Outputfile,output,OutputFormats[opts.OutputFormatIndex], headerMsg);

                var actionText = opts.Decompress ? "decompressed" : "compressed";
                Console.WriteLine(
                    $"{Path.GetFileName(opts.Inputfile)} ({data.Length} bytes) {actionText} to {opts.Outputfile} ({output.Length} bytes).");
                if (!opts.Decompress)
                    Console.WriteLine($"Compression ratio {100.0 * output.Length / data.Length:F1}%");

                return 1;
            }
            // if has only input file - do some testing
            if (!String.IsNullOrEmpty(opts.Inputfile))
            {
                //public string Inputfile { get; set; } = "";
                //public bool RecurseDirectories { get; set; } = false;
                //public int OutputFormatIndex { get; set; } = -1;
                //public bool Decompress { get; set; } = false;

                var test = new Testing();
                var request = new Testing.Request
                {
                    Codecs = codecs,
                    Filename = "*.*",
                    Path = opts.Inputfile,
                    RecurseFiles = opts.RecurseDirectories,

                    // Decompress = false,
                    // UseExternalDecompressor = true,
                    ShowOnlyErrors = false,
                    TrapErrors = true
                };
                test.DoTest(request);
                return 1;
            }
            Console.Error.WriteLine("Nothing to do");
            return -4;
        }

        static void ApplyParameters(CodecBase codec, string parameterText)
        {
            var type = codec.GetType();
            var props = Utility.GetPropertiesWith<CodecParameterAttribute>(type).ToList();
            parameterText = parameterText.Trim(new char[] {':'});
            var phrases = parameterText.Split(new[] {':'}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var phrase in phrases)
            {
                var tokens = phrase.Split(new[] {'='});
                uint value;
                if (tokens.Length != 2 || !UInt32.TryParse(tokens[1],out value))
                {
                    Console.WriteLine($"Error! unknown parameter {phrase}");
                    continue;
                }
                var property = props.FirstOrDefault(p =>
                    {
                        var attr = p.GetCustomAttribute(typeof(CodecParameterAttribute)) as CodecParameterAttribute;
                        return attr != null && attr.SymbolName == tokens[0];
                    }
                );
                if (property == null)
                {
                    Console.WriteLine($"Error! unknown parameter {phrase}");
                    continue;
                }
                property.SetValue(codec,value);
                Console.WriteLine($"Codec {codec.GetType().Name.Replace("Codec","")} parameter {property.Name} set to {value}");
            }
        }

        static void Main(string[] args)
        {
            int exitCode;
            if (args.Length == 0)
            {
                Usage();
                exitCode = -1;
            }
            else
            {
                var options = ParseOptions(args);
                exitCode = PerformCommand(options);
            }
            Console.WriteLine("Chris Lomont 2016, www.lomont.org");
            Environment.Exit(exitCode);
        }

        static void DoTesting()
        {

            var test = new Testing
            {
                OutputWriter = Console.Out
            };

            //TestGolomb();
            //return;
            //DumpCode();
            //return;


            //test.TestUniversalCodeBattery();
            //test.TestUniversalCode(1, 10000);
            //test.TestUniversalCode(1, 1U<<20,n=>5*n/3);
            //test.TestUniversalCode(8, 1U << 28, n => 9*n/8);
            //test.TestUniversalOnFiles(Testing.Paths[0]);
            //return;

            //var bs = new Bitstream();
            ////bs.OutputWriter = Console.Out;
            //var ls1 = new List<uint> {1,4,4,9,1,34,4,0,0,76,1,34,10,34};
            //ls1 = new List<uint> { 0x00, 0x2B, 0x4F, 0x81, 0x50, 0x2B, 0x56, 0x2B, 0x2C, 0x56, 0x56, 0x24, 0x24, 0x4F, 0x56, 0x25, 0x2B, 0x7B, 0x2B, 0x2C, 0x01, 0x2B, 0x81, 0x32, 0x32, 0x07, 0x07, 0x2B, 0x56, 0x06, 0x31, 0x00, 0xA5, 0x24, 0xAC, 0xAC, 0x50, 0x81, 0x81, 0x57, 0x01, 0x81, 0x81, 0x07, 0x5D, 0x00, 0x5C, 0x4F, 0x48, 0x81, 0xD7, 0x7A, 0x7A, 0xAC, 0x7B, 0x50, 0x75, 0x56, 0x81, 0x2D, 0x02, 0x81, 0x0E, 0x32, 0x81, 0x5C, 0x0C, 0x81, 0x73, 0x2B, 0xA5, 0x75, 0xA6, 0xD7, 0xA6, 0x75, 0x7B, 0x2D, 0x82, 0xAC, 0xAD, 0xAC, 0x88, 0x5D, 0x39, 0x15, 0x5D, 0xAC, 0x37, 0x0C, 0x6C, 0x9E, 0x56, 0x9F, 0xD1, 0x81, 0x82, 0x03, 0x2D, 0xD7, 0xAC, 0x03, 0x88, 0x15, 0x39, 0x88, 0x37, 0x12, 0x37, 0x87, 0x73, 0xA5, 0x9F, 0x9A, 0xA0, 0x58, 0x58, 0x2D, 0x2E, 0x2E, 0x04, 0xAC, 0x40, 0xB3, 0xD7, 0x62, 0x12, 0xAC, 0xD7, 0x81, 0x9E, 0x90, 0x97, 0x81, 0x81, 0xCA, 0xCB, 0xD1, 0x83, 0x04, 0x2E, 0x58, 0xAD, 0xD7, 0x40, 0x1C, 0x40, 0xAC, 0x81, 0x88, 0x1C, 0x88, 0x18, 0x62, 0x87, 0x3D, 0xAC, 0x97, 0xC9, 0x56, 0xA5, 0xD1, 0xBF, 0xBF, 0xC5, 0xD7, 0xCB, 0xD7, 0x59, 0x83, 0x6B, 0x23, 0x47, 0x6B, 0x3D, 0xB2, 0xD7, 0x2B, 0x9E, 0xB4, 0xBB, 0xBB, 0xD7, 0xD1, 0xCB, 0xCB, 0xC5, 0xC5, 0xD1, 0xD1, 0x59, 0x05, 0x05, 0x59, 0x56, 0x8F, 0x47, 0x88, 0x88, 0x8D, 0x43, 0xB2, 0xAC, 0x8D, 0x1E, 0x43, 0xBB, 0xD0, 0xCB, 0xAC, 0xAD, 0x2F, 0x83, 0xB3, 0x47, 0xB2, 0x43, 0x68, 0xBB, 0xC2, 0xC2, 0xD0, 0xD7, 0x83, 0x82, 0x8F, 0x8D, 0x68, 0x2B, 0xA5, 0xC9, 0xD1, 0xAD, 0x57, 0x2B, 0xB3, 0x8F, 0xB2, 0x8D, 0xAD, 0x83, 0xD7, 0x8F, 0xB3, 0x88, 0x56, 0xA5, 0xC9, 0xB3, 0xAC, 0x2B, 0xAD, 0xB3, 0x2B, 0x2B, 0x32, 0x56, 0x4F, 0x24, 0x50, 0x25, 0x57, 0x01, 0x56, 0x2C, 0x07, 0x06, 0x31, 0x56, 0x24, 0x48, 0x25, 0x7B, 0xAC, 0x2C, 0x07, 0x32, 0x30, 0x2A, 0x5C, 0x31, 0x55, 0x73, 0x48, 0x2B, 0x7B, 0x7B, 0x56, 0x2D, 0x02, 0x2C, 0x57, 0x57, 0x5D, 0x5D, 0x32, 0x0E, 0x0E, 0x5D, 0x0E, 0x30, 0x80, 0x5C, 0x55, 0x6C, 0xA5, 0xA6, 0xA6, 0x2D, 0x82, 0x88, 0x39, 0x80, 0x30, 0x5B, 0x5B, 0xAC, 0x9E, 0x97, 0x6C, 0x9E, 0x56, 0x7B, 0x7B, 0x75, 0x03, 0x2D, 0x15, 0xD7, 0x5B, 0x36, 0x86, 0x87, 0xB2, 0xA5, 0x90, 0x56, 0xA0, 0x75, 0xA0, 0x58, 0x82, 0x82, 0x58, 0xAD, 0x39, 0x56, 0x64, 0x15, 0x88, 0xD6, 0x86, 0xAB, 0x86, 0x5A, 0xA6, 0x9A, 0xA0, 0xD1, 0xAC, 0x58, 0x04, 0x2E, 0x1C, 0x88, 0xAC, 0xAB, 0x60, 0xC2, 0xBB, 0xC2, 0x56, 0xCB, 0xCB, 0xD7, 0x82, 0x04, 0x58, 0x2E, 0x57, 0x88, 0x40, 0x8C, 0x87, 0xA5, 0xBB, 0xC9, 0x56, 0xC9, 0xD7, 0xC5, 0xCB, 0x00, 0x82, 0x2F, 0xAD, 0x83, 0x59, 0x47, 0x23, 0x47, 0x88, 0x8C, 0x8B, 0xB1, 0xC2, 0x56, 0x7A, 0xC9, 0xD0, 0x59, 0x83, 0x83, 0x2B, 0x6B, 0x56, 0x8C, 0xC2, 0xC9, 0xC9, 0xAD, 0x59, 0x83, 0x5D, 0x6B, 0x6B, 0x6B, 0x8F, 0x8F, 0x2B, 0xD7, 0xD0, 0xD0, 0xD1, 0xCB, 0xB3, 0xB3, 0x81, 0x8C, 0xAC, 0xD0, 0xD0, 0xD7, 0x89, 0x81, 0x8F, 0x2B, 0x81, 0xB1, 0xB2, 0x81, 0xD1, 0xAD, 0xB3, 0xB3, 0x88, 0x87, 0xD6, 0xB2, 0xD6, 0xD7, 0xD6, 0x00, 0xFF, 0x00};
            //UniversalCodec.BinaryAdaptiveSequentialEncode(bs,ls1);
            //bs.Position = 0;
            //Console.WriteLine();
            //var ls2 = UniversalCodec.BinaryAdaptiveSequentialDecode(bs);
            //Console.WriteLine();
            //Console.WriteLine(CodecBase.CompareInts(ls1.ToArray(),ls2.ToArray()));
            //
            //return;

            //DumpCode();
            //return;
            var codec77 = new Lz77Codec
            {
                //OutputWriter = Console.Out,
                //MaximumDistance = 85,
                //MaximumLength = 12,
                //MinimumLength = 2
            };
            //codec77.Options |= Lz77Codec.OptionFlags.DumpHeader;

            var codecCLbase = new LzclCodec {CodecName = "LZCL"};
            codecCLbase.CompressionOptions &= ~CompressionChecker.OptionFlags.UseHuffman;
            codecCLbase.CompressionOptions &= ~CompressionChecker.OptionFlags.UseArithmetic;
            codecCLbase.CompressionOptions &= ~CompressionChecker.OptionFlags.UseGolomb;

            var codecCl = new LzclCodec
            {
                CodecName = "LZCL",
                //MaximumDistance = 85,
                //MaximumLength = 12,
                //MinimumLength = 2
            };
            //codecCl.OutputWriter = Console.Out;
            //codecCl.CompressionOptions &= ~CompressionChecker.OptionFlags.UseArithmetic;
            //codecCl.CompressionOptions &= ~CompressionChecker.OptionFlags.UseGolomb;
            //codecCl.CompressionOptions &= ~CompressionChecker.OptionFlags.UseHuffman;
            //codecCl.CompressionOptions &= ~CompressionChecker.OptionFlags.UseFixed;
            //codecCl.CompressionOptions |= CompressionChecker.OptionFlags.UseStout;
            //codecCl.CompressionOptions |= CompressionChecker.OptionFlags.UseBASC;
            //codecCl.Options |= LZCLCodec.OptionFlags.ShowSavings;
            //codecCl.Options |= LZCLCodec.OptionFlags.DumpOptimize;
            codecCl.Options |= LzclCodec.OptionFlags.DumpDebug;
            //codecCl.Options |= LZCLCodec.OptionFlags.ShowTallies;
            codecCl.Options  |= LzclCodec.OptionFlags.DumpCompressorSelections;

            //var ds = DataSets.HypnoLightLogo;
            //ds = File.ReadAllBytes(@"..\Corpus\Cantebury\asyoulik.txt");
            //codecCL.Optimize(ds);
            //return;

            var codecH = new HuffmanCodec();
            //codecH.OutputWriter = Console.Out;
            //codecH.Options |= HuffmanCodec.OptionFlags.DumpDecoding;
            //codecH.Options |= HuffmanCodec.OptionFlags.DumpEncoding;
            //codecH.Options |= HuffmanCodec.OptionFlags.DumpHeader;
            //codecH.Options |= HuffmanCodec.OptionFlags.DumpDictionary;
            //codecH.Options &= ~HuffmanCodec.OptionFlags.UseLowMemoryDecoding;

            var codecA = new ArithmeticCodec();
            //codecA.OutputWriter = Console.Out;
            //codecA.Options |= ArithmeticCodec.OptionFlags.DumpHeader;
            //codecA.Options |= ArithmeticCodec.OptionFlags.DumpTable;
            //codecA.Options |= ArithmeticCodec.OptionFlags.DumpState;
            //codecA.Options |= ArithmeticCodec.OptionFlags.UseLowMemoryDecoding;

            var codecF = new FixedSizeCodec();

            var codecGolomb = new GolombCodec();

            byte[] smallTest = {1,2,5,5,7,7,7,7,8,8,5};

            var request = new Testing.Request
            {
                Codecs = new List<CodecBase>
                {
                    codecH,
                    codecA,
                    codec77,
                    codecCl,
                    //codecGolomb,
                    //codecCLbase,
                    //codecF
                },
                // Codec = codec,

                //TestData = smallTest,
                //TestData = DataSets.HypnoLightLogo,
                Filename = "*.*",
                Path = Testing.Paths[0],
                RecurseFiles = true,

                //Decompress = false,
                UseExternalDecompressor = true,
                ShowOnlyErrors = false,
                TrapErrors = true
            };
            test.DoTest(request);

            //OutLogo();
            // test.Optimize(DataSets.HypnoLightLogo);
        }
    }
}
