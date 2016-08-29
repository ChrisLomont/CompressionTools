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
    // todo - reimplement sparse tables, useful to compress LZ77 style distances which are not sequential
    // or just apply a unique filter to them first.

    /// <summary>
    /// Simple and small arithmetic compression
    /// </summary>
    [Codec("Arithmetic")]
    public class ArithmeticCodec : CodecBase
    {
        // see http://www.drdobbs.com/cpp/data-compression-with-arithmetic-encodin/240169251?pgno=2    
        // http://www.sable.mcgill.ca/~ebodde/pubs/sable-tr-2007-5.pdf

        #region Constants

        // constants defining the range
        const uint Range25Percent = 0x20000000U;
        const uint Range50Percent = 0x40000000U;
        const uint Range75Percent = 0x60000000U;
        const uint Range100Percent = 0x80000000U;

        #endregion

        #region Settings

        [Flags]
        public enum OptionFlags
        {
            None = 0x00000000,

            /// <summary>
            /// Decodes from tables stored in the compressed data itself
            /// Otherwise allocates memory
            /// </summary>
            UseLowMemoryDecoding = 0x00000001,

            DumpState  = 0x00000100,
            DumpTable  = 0x00000200,
            DumpHeader = 0x00000400
        }

        public OptionFlags Options { get; set; } = OptionFlags.None;

        #endregion

        #region Compression Functions

        // end encoding  notes http://www3.sympatico.ca/mt0000/biacode/biacode.html
        // and http://bijective.dogma.net/compres10.htm

        /// <summary>
        /// Write the header for the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="data"></param>
        /// <param name="headerFlags">Flags telling what to put in the header. Useful when embedding in other streams.</param>
        /// <returns></returns>
        public override void WriteHeader(Bitstream bitstream, Datastream data, Header.HeaderFlags headerFlags)
        {
            // count occurrences to get probabilities
            counts = new uint[data.Max() + 1];
            foreach (var b in data)
                counts[b]++;
            total = (uint) data.Count; // total frequency
            MakeSums();

            ResetCoder();

            // arithmetic gets total probability from the header, so ensure it gets saved
            headerFlags |= Header.HeaderFlags.SymbolCount;
            Header.WriteUniversalHeader(bitstream, data, headerFlags);
            
            // we'll insert the bitlength of what follows at this spot during the footer
            whereToInsertBitlength = bitstream.Position;

            // write freq tables
            var tables = MakeFrequencyTable();
            bitstream.WriteStream(tables);

            if (Options.HasFlag(OptionFlags.DumpState))
                Write("Enc: ");
        }

        /// <summary>
        /// Compress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="symbol"></param>
        public override void CompressSymbol(Bitstream bitstream, uint symbol)
        {
            if (Options.HasFlag(OptionFlags.DumpState))
                Write($"[{symbol:X2},{lowValue:X8},{highValue:X8}] ");

            uint lowCount, highCount;
            GetCounts(symbol, out lowCount, out highCount);
            Trace.Assert(total < (1 << 29));

            // update bounds
            uint step = (highValue - lowValue + 1)/total; // interval open at top gives + 1
            highValue = lowValue + step*highCount - 1; // interval open at top gives -1
            lowValue = lowValue + step*lowCount;

            // apply e1/e2 scaling to keep ranges in bounds
            while ((highValue < Range50Percent) || (lowValue >= Range50Percent))
            {
                if (highValue < Range50Percent)
                {
                    bitstream.Write(0);
                    lowValue = 2*lowValue;
                    highValue = 2*highValue + 1;
                    // e3 scaling
                    for (; scaling > 0; scaling--)
                        bitstream.Write(1);
                }
                else if (lowValue >= Range50Percent)
                {
                    bitstream.Write(1);
                    lowValue = 2*(lowValue - Range50Percent);
                    highValue = 2*(highValue - Range50Percent) + 1;

                    // e3 scaling
                    for (; scaling > 0; scaling--)
                        bitstream.Write(0);
                }
            }

            // get e3 scaling value
            while ((Range25Percent <= lowValue) && (highValue < Range75Percent))
            {
                scaling++;
                lowValue = 2*(lowValue - Range25Percent);
                highValue = 2*(highValue - Range25Percent) + 1;
            }
        }

        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public override void WriteFooter(Bitstream bitstream)
        {
            if (Options.HasFlag(OptionFlags.DumpState))
                WriteLine();
            Finish(bitstream);

            // insert the bitlength of the compressed portion
            bitLength = bitstream.Length - whereToInsertBitlength;
            var lengthBitstream = new Bitstream();
            UniversalCodec.Lomont.EncodeLomont1(lengthBitstream,bitLength,8,-1);
            bitstream.InsertStream(whereToInsertBitlength,lengthBitstream);
            //Console.WriteLine($"Arith encode bitsize {bitLength} at {whereToInsertBitlength} final {bitstream.Length}");
        }

        #endregion

        #region Decompression functions

        /// <summary>
        /// Read the header for the compression algorithm
        /// Return number of symbols in stream if known, else 0 if not present
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="headerFlags">Flags telling what to put in the header. Useful when embedding in other streams.</param>
        /// <returns></returns>
        public override uint ReadHeader(Bitstream bitstream, Header.HeaderFlags headerFlags)
        {
            ResetCoder();

            // arithmetic gets total probability from the header, so...
            headerFlags |= Header.HeaderFlags.SymbolCount;
            var header = Header.ReadUniversalHeader(bitstream, headerFlags);
            total = header.Item1;

            bitLength = UniversalCodec.Lomont.DecodeLomont1(bitstream, 8, -1);
            bitsRead = 0;
            //Console.WriteLine($"Arith decode bitsize {bitLength}");


            var tempPos = bitstream.Position;
            DecodeTable(bitstream);
            bitsRead = bitstream.Position - tempPos;

            // start buffer with 31 bits
            buffer = 0;
            for (var i = 0; i < 31; ++i)
                buffer = (buffer << 1) | ReadBit(bitstream);

            if (Options.HasFlag(OptionFlags.DumpState))
                Write("Dec: ");


            return total;
        }

        /// <summary>
        /// Decompress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        public override uint DecompressSymbol(Bitstream bitstream)
        {
            if (Options.HasFlag(OptionFlags.DumpState))
                Write($"[{lowValue:X8},{highValue:X8}] ");

            Trace.Assert(total < (1 << 29)); // todo - this sufficient when 8 bit symbols, but what when larger?
            Trace.Assert(lowValue <= buffer && buffer <= highValue);

            //split range into single steps
            uint step = (highValue - lowValue + 1)/total; // interval open at top gives +1

            uint lowCount, highCount;

            var symbol = Options.HasFlag(OptionFlags.UseLowMemoryDecoding) ? 
                LookupLowMemoryCount(bitstream, (buffer - lowValue) / step, out lowCount, out highCount) : 
                LookupCount((buffer - lowValue) / step, out lowCount, out highCount);

            // upper bound
            highValue = lowValue + step*highCount - 1; // interval open top gives -1
            // lower bound
            lowValue = lowValue + step*lowCount;

            // e1/e2 scaling
            while ((highValue < Range50Percent) || (lowValue >= Range50Percent))
            {
                if (highValue < Range50Percent)
                {
                    lowValue = 2*lowValue;
                    highValue = 2*highValue + 1;
                    Trace.Assert(buffer <= 0x7FFFFFFF);
                    buffer = 2*buffer + ReadBit(bitstream);
                }
                else if (lowValue >= Range50Percent)
                {
                    lowValue = 2*(lowValue - Range50Percent);
                    highValue = 2*(highValue - Range50Percent) + 1;
                    Trace.Assert(buffer >= Range50Percent);
                    buffer = 2*(buffer - Range50Percent) + ReadBit(bitstream);
                }
            }

            // e3 scaling
            while ((Range25Percent <= lowValue) && (highValue < Range75Percent))
            {
                lowValue = 2*(lowValue - Range25Percent);
                highValue = 2*(highValue - Range25Percent) + 1;
                Trace.Assert(buffer >= Range25Percent);
                buffer = 2*(buffer - Range25Percent) + ReadBit(bitstream);
            }
            return symbol;
        }

        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public override void ReadFooter(Bitstream bitstream)
        {
        }

        #endregion

        #region Implementation

        void MakeSums()
        {
            sums = new uint[counts.Length];
            sums[0] = counts[0];
            for (var i = 1; i < counts.Length; ++i)
                sums[i] = sums[i - 1] + counts[i];
        }

        private uint whereToInsertBitlength;
        private uint bitLength; // number of bits in compressed region
        private uint bitsRead; // track number read of compressed region

        private uint total;

        // both encoder and decoder state
        uint highValue, lowValue, scaling;
        // decoder only state
        uint buffer;

        // low memory decoding state
        private uint symbolMin;
        private uint symbolMax;
        private uint tableStartBitPosition;

        // symbol counts
        private uint[] counts;
        private uint[] sums;


        void GetCounts(uint symbol, out uint lowCount, out uint highCount)
        {
            lowCount = symbol == 0 ? 0 : sums[symbol - 1];
            highCount = sums[symbol];
        }

        void Finish(Bitstream bitstream)
        {
            // two possible lowValue and highValue distributions, so
            // two bits enough to distinguish
            // todo - need enough to decode last item, else bitstream must return 0 when empty
            if (lowValue < Range25Percent)
            {
                bitstream.Write(0);
                bitstream.Write(1);
                for (var i = 0; i < scaling + 1; ++i) //final e3 scaling
                    bitstream.Write(1);
                //Console.WriteLine($"A finish 0 - {output.symbolsToWrite-output.symbolsWritten} to go");
            }
            else
            {
                bitstream.Write(1);
                bitstream.Write(0);
                // no need to write more final scaling 0 values since decoder returns all 0s after end of stream

                //for (var i = 0; i < scaling + 1; ++i) //final e3 scaling
                //    bitstream.Write(0);
                //Console.WriteLine($"A finish 1 - {output.symbolsToWrite - output.symbolsWritten} to go");
            }
        }

        void ResetCoder()
        {
            // initialize coder state
            lowValue = 0; // lower bound, inclusive
            highValue = Range100Percent - 1; // upper bound, inclusive
            scaling = 0;
        }

        // read one bit, return 0 when out of bits
        uint ReadBit(Bitstream bitstream)
        {
            ++bitsRead;
            if (bitsRead < bitLength)
                return bitstream.Read(1);
            return 0;
        }

        // lookup symbol and probability range using table decoding
        uint LookupLowMemoryCount(Bitstream bitstream, uint cumCount, out uint lowCount, out uint highCount)
        {
            // BASC encoded, decode with same process
            // todo - merge with BASC Codec version, make cleaner

            // swap bit positions to access table
            uint tempPosition = bitstream.Position; // save this
            bitstream.Position = tableStartBitPosition;

            lowCount = highCount = 0;
            uint symbol = 0;

            uint length = UniversalCodec.Lomont.DecodeLomont1(bitstream,6,0);
            if (length != 0)
            {
                uint b1 = UniversalCodec.Lomont.DecodeLomont1(bitstream,6,0);
                uint xi = bitstream.Read(b1);

                lowCount = 0;
                highCount = xi;
                symbol = symbolMin;
                uint i = symbolMin;

                while (highCount <= cumCount)
                {
                    var decision = bitstream.Read(1);
                    if (decision == 0)
                    {
                        // bi is <= b(i-1), so enough bits
                        xi = bitstream.Read(b1);
                    }
                    else
                    {
                        // bi is bigger than b(i-1), must increase it
                        uint delta = 0;
                        do
                        {
                            decision = bitstream.Read(1);
                            delta++;
                        } while (decision != 0);
                        b1 += delta;
                        xi = bitstream.Read(b1-1); // xi has implied leading 1
                        xi |= 1U << (int)(b1 - 1);
                    }
                    b1 = BitsRequired(xi);

                    lowCount = highCount;
                    highCount += xi;
                    ++i;
                    if (xi != 0)
                        symbol = i;
                }
            }

            // restore bit position
            bitstream.Position = tempPosition; 
            return symbol;
        }

        // find the symbol value that has this cumulativeCount
        uint LookupCount(uint cumulativeCount, out uint lowCount, out uint highCount)
        {

            lowCount = highCount = 0;
            uint symbol = 0, i = 0;

            while (i < counts.Length && highCount <= cumulativeCount)
            {
                if (counts[i] != 0)
                    symbol = i;
                lowCount = highCount;
                highCount += counts[i];
                i++;
            }

            return symbol;
        }

        // todo 
        void DecodeTable(Bitstream bs)
        {
            // Table thus only full type. Format is
            //   - symbol min index used, max index used, Lomont1 universal coded.
            //   - Number of bits in table, lomont1 universal coded (allows jumping past)
            //   - Full table. Counts are BASC encoded, maxIndex - minIndex+1 entries
            symbolMin = UniversalCodec.Lomont.DecodeLomont1(bs,6,0);
            symbolMax = UniversalCodec.Lomont.DecodeLomont1(bs,6,0);
            uint tableBitLength = UniversalCodec.Lomont.DecodeLomont1(bs,6,0);

            if (Options.HasFlag(OptionFlags.DumpHeader))
            {
                WriteLine($"Arith decode: min symb index {symbolMin} max symb index {symbolMax} tbl bits {tableBitLength}");
            }

            if (Options.HasFlag(OptionFlags.UseLowMemoryDecoding))
            {
                tableStartBitPosition = bs.Position;
                bs.Position += tableBitLength; // skip table
                return;
            }
            // else decode table and use memory based decoding
            var counts1 = UniversalCodec.BinaryAdaptiveSequentialDecode(bs,b=>UniversalCodec.Lomont.DecodeLomont1(bs,6,0));
            counts = new uint[symbolMax+1];
            for (var i = symbolMin; i <= symbolMax; ++i)
                counts[i] = counts1[(int)(i-symbolMin)];
            MakeSums();
        }


        /// <summary>
        /// Create the frequency table as a bitsteam for ease of use/testing
        /// </summary>
        /// <returns></returns>
        Bitstream MakeFrequencyTable()
        {
            var bs = new Bitstream();
            // write freq tables
            uint maxCount = counts.Max();
            uint minCount = counts.Where(c => c > 0).Min();
#if true
            // have determined the following:
            // Of all three Elias, Golomb optimized, BASC, that BASC is slightly best for storing counts
            // Also using BASC for counts present is good.
            // Also determined the sparse table type is much bigger in every file we tested!
            // so, check two types: 
            //    1) BASC on all counts, versus
            //    2) BASC on those present for both count and symbol
            // Table thus only full type. Format is
            //   - symbol min index used, max index used, Lomont1 universal coded.
            //   - Number of bits in table, Lomont1 universal coded (allows jumping past)
            //   - Full table. Counts are BASC encoded, maxIndex - minIndex+1 entries

            uint minSymbolIndex = UInt32.MaxValue;
            uint maxSymbolIndex = 0;
            for (var i = 0U; i < counts.Length; ++i)
            {
                if (counts[i] != 0)
                {
                    maxSymbolIndex = i;
                    if (minSymbolIndex == UInt32.MaxValue)
                        minSymbolIndex = i;
                }
            }

            UniversalCodec.Lomont.EncodeLomont1(bs, minSymbolIndex, 6, 0);
            UniversalCodec.Lomont.EncodeLomont1(bs, maxSymbolIndex, 6, 0);

            var fullTableBs = new Bitstream();
            UniversalCodec.BinaryAdaptiveSequentialEncode(fullTableBs, new Datastream(
                counts.Skip((int)minSymbolIndex).Take((int)(maxSymbolIndex - minSymbolIndex + 1)).ToArray()),
                (b,v)=>UniversalCodec.Lomont.EncodeLomont1(b,v,6,0)
                );
            UniversalCodec.Lomont.EncodeLomont1(bs, fullTableBs.Length,6,0);
            bs.WriteStream(fullTableBs);

            if (Options.HasFlag(OptionFlags.DumpHeader))
            {
                WriteLine($"Arith encode: min symb index {minSymbolIndex} max symb index {maxSymbolIndex} tbl bits {fullTableBs.Length}");
            }

#else

            // have determined the following:
            // Of all three Elias, Golomb optimized, BASC, that BASC is slightly best for storing counts
            // Also using BASC for counts present is good.
            // Also determined the sparse table type is much bigger in every file we tested!
            // so, check two types: 
            //    1) BASC on all counts, versus
            //    2) BASC on those present for both count and symbol
            // Table thus 
            //   - symbol min index used + 1, max index used + 1, EliasDelta coded.
            //   - bit denoting table type 0 (full) or 1 (sparse)
            //   - Number of bits in table + 1, elias delta coded (allows jumping past)
            //     0 = Full table. Counts are BASC encoded, maxIndex - minIndex+1 entries
            //     1 = sparse table. 
            //         Elias delta for number of counts in table + 1 (same as number of symbols)
            //         Elias delta for bitlength of counts + 1, 
            //         BASC counts, 
            //         BASC symbols present
            //   - table



            // compute two table lengths:
            uint minSymbolIndex = UInt32.MaxValue;
            uint maxSymbolIndex = 0;
            for (var i = 0U; i < counts.Length; ++i)
            {
                if (counts[i] != 0)
                {
                    maxSymbolIndex = i;
                    if (minSymbolIndex == UInt32.MaxValue)
                        minSymbolIndex = i;
                }
            }
            // common header
            UniversalCodec.Elias.EncodeDelta(bs,minSymbolIndex+1);
            UniversalCodec.Elias.EncodeDelta(bs, maxSymbolIndex + 1);


            var fullTableBs = new Bitstream();
            var sparseTableBs = new Bitstream();

            UniversalCodec.BinaryAdaptiveSequentialEncode(fullTableBs,new Datastream(
                counts.Skip((int)minSymbolIndex).Take((int)(maxSymbolIndex-minSymbolIndex+1)).ToArray()
                ));

            var nonzeroCountIndices =
                counts.Select((c, n) => new {val = c, pos = n})
                    .Where(p => p.val > 0)
                    .Select(p => (uint) p.pos)
                    .ToArray();
            var nonzeroCounts = counts.Where(c => c > 0).ToArray();

            UniversalCodec.Elias.EncodeDelta(sparseTableBs, (uint)(nonzeroCounts.Length+1));
            UniversalCodec.Elias.EncodeDelta(sparseTableBs, (uint)(nonzeroCounts.Length + 1));

            var tempBs = new Bitstream();
            UniversalCodec.BinaryAdaptiveSequentialEncode(tempBs, new Datastream(nonzeroCounts));
            uint sparseMidPos = tempBs.Position;

            UniversalCodec.Elias.EncodeDelta(sparseTableBs, sparseMidPos + 1);
            sparseTableBs.WriteStream(tempBs);

            UniversalCodec.BinaryAdaptiveSequentialEncode(sparseTableBs, new Datastream(nonzeroCountIndices));

            Console.WriteLine($"Arith full table {fullTableBs.Length} sparse table {sparseTableBs.Length}");


            // now finish table
            if (fullTableBs.Length < sparseTableBs.Length)
            {
                bs.Write(0); // full table
                UniversalCodec.Elias.EncodeDelta(bs,fullTableBs.Length+1);

                bs.WriteStream(fullTableBs);
                
            }
            else
            {
                bs.Write(1); // sparse table
                UniversalCodec.Elias.EncodeDelta(bs, sparseTableBs.Length + 1);

                bs.WriteStream(sparseTableBs);
            }





            // var cc = new CompressionChecker();
            // cc.TestAll("arith",new Datastream(counts)); // all
            // cc.TestAll("arith",new Datastream(counts.Where(c=>c>0).ToArray())); // nonzero
            // BASC wins these tests
            // 

#if false
            var allDs = new Datastream();
            var nonzeroDs = new Datastream();
            for (var i = 0U; i < counts.Length; ++i)
            {
                var index = i;//(uint)(counts.Length - 1 - i);
                allDs.Add(index);
                if (counts[i] != 0)
                    nonzeroDs.Add(index);
            }

            var allBs = new Bitstream();
            var nonzeroBs = new Bitstream();
            UniversalCodec.BinaryAdaptiveSequentialEncode(allBs,allDs);
            UniversalCodec.BinaryAdaptiveSequentialEncode(nonzeroBs, nonzeroDs);
            Console.WriteLine($"Arith all {allBs.Length} in ");
            Console.WriteLine($"Arith nonzero {nonzeroBs.Length} in ");

            //foreach (var c in counts)
            //    UniversalCodec.OneParameterCodeDelegate(
            //var ans = UniversalCodec.Optimize(UniversalCodec.Golomb.Encode,counts.ToList(),1,256);
            //bs = ans.Item1;
            // 912 gamma
            // 918 elias delta
            // 988 Omega
            // 1152 bits UniversalCodec.BinaryAdaptiveSequentialEncode(bs,new Datastream(counts));
            // 1265 best Golomb 
#endif

#endif
            if (Options.HasFlag(OptionFlags.DumpTable))
            {
                WriteLine($"Arith table bitsize {bs.Length}, min symbol ? max symbol ? min count {minCount} max count {maxCount}");
                for (var i = 0; i < counts.Length; ++i)
                {
                    if (counts[i] != 0)
                        Write($"[{i},{counts[i]}] ");
                }
                WriteLine();
            }
            return bs;
        }


        #endregion
    }
}
