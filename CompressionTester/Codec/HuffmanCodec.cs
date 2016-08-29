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
using System.Text;

namespace Lomont.Compression.Codec
{
    /// <summary>
    /// Huffman compression - format is TODO - polish
    /// Header:
    ///     - decoded size stored in Lomont1(6) universal code:
    ///       - chunks of size 6 bits, prefixed with 1 if not last chunk, else 0 bit, least significant first
    /// Table: canonical Huffman coded
    /// Data stream:
    ///     Bits read from LSB of each byte first
    /// 0 padded to byte boundary
    /// 
    /// Alphabet consists of symbols, codewords have a value and (bit)length, table maps codewords to symbols
    /// </summary>
    [Codec("Huffman")]
    public class HuffmanCodec : CodecBase
    {

        #region Settings

        [Flags]
        public enum OptionFlags
        {
            None = 0,

            /// <summary>
            /// Decodes from tables stored in the compressed data itself
            /// Otherwise allocates memory
            /// </summary>
            UseLowMemoryDecoding = 0x00000001,

            /// <summary>
            /// Some debugging flags
            /// </summary>
            DumpDictionary = 0x00000100,
            DumpHeader     = 0x00000200,
            DumpEncoding   = 0x00000400,
            DumpDecoding   = 0x00000800,

            // stats to save
            LogCodewordLengths = 0x00010000
        }

        /// <summary>
        /// Codec options
        /// </summary>
        public OptionFlags Options { get; set; } = OptionFlags.UseLowMemoryDecoding;
        #endregion

        #region Compression functions

        public override void WriteHeader(Bitstream bitstream, Datastream data, Header.HeaderFlags headerFlags)
        {

            Header.WriteUniversalHeader(bitstream, data, headerFlags);
            if (data.Count == 0)
                return;

            // make code tree
            var tree = MakeTree(data);

            // walk tree assigning codewords
            AssignBitStrings(tree);

            // get leaf nodes, which are the symbols 
            // after this the rest of the tree is not needed
            leaves.Clear();
            GetLeaves(tree);
            MakeCanonical(leaves); // relabel codewords into canonical ordering


            // write symbol table
            // must have canonical labeled leaves
            bitstream.WriteStream(MakeTable(leaves));

            // prepare to add rest of data
            if (Options.HasFlag(OptionFlags.DumpEncoding))
                WriteLine("Huff encode: [");
        }

        public override void CompressSymbol(Bitstream bitstream, uint symbol)
        {
            var node = leaves.Find(n => n.Symbol == symbol);
            // write MSB first
            for (var i = (int)node.Codeword.BitLength - 1; i >= 0; --i)
                bitstream.Write(node.Codeword.GetBit((uint)i), 1);

            if (Options.HasFlag(OptionFlags.DumpEncoding))
                Write($"{symbol:X2},");

        }

        public override void WriteFooter(Bitstream bitstream)
        {
            if (Options.HasFlag(OptionFlags.DumpEncoding))
                WriteLine("]");
        }

        #endregion

        #region Decompression functions

        /// <summary>
        /// State needed for decompression to make porting to C easier
        /// </summary>
        public class DecompresorState
        {
            // current byte size for low memory decompressor: (4+4)+4+4+4*1 = 20 bytes

            /// <summary>
            /// Stream to decode from
            /// </summary>
            public Bitstream Bitstream { get; set; }
            /// <summary>
            /// bit stream position where codeword table is stored
            /// </summary>
            public uint TablePosition { get; set; }
            /// <summary>
            /// Symbols to decode
            /// </summary>
            public uint SymbolLength { get; set; }
            /// <summary>
            /// Bits per symbol in symbol table
            /// </summary>
            public byte BitsPerSymbol { get; set; }
            /// <summary>
            /// Minimum codeword length present
            /// </summary>
            public byte MinCodewordLength { get; set; }
            /// <summary>
            /// Maximum codeword length present
            /// </summary>
            public byte MaxCodewordLength { get; set; }
            /// <summary>
            /// Bits to store a count of codewords of the same length
            /// </summary>
            public byte BitsPerCodelengthCount { get; set; }
            /// <summary>
            /// Optional position for decoding table if decoding from memory instead of from the stream
            /// </summary>
            public List<Tuple<uint, Datastream>> Table { get; set; }
        }


        DecompresorState state;

        public override uint ReadHeader(Bitstream bitstream, Header.HeaderFlags headerFlags)
        {
            state = new DecompresorState();

            bool useLowMemoryDecoding = Options.HasFlag(OptionFlags.UseLowMemoryDecoding);

            // save stream for internal use
            state.Bitstream = bitstream;

            var header = Header.ReadUniversalHeader(bitstream, headerFlags);
            state.SymbolLength = header.Item1;

            ParseTable(state, useLowMemoryDecoding);

            if (!useLowMemoryDecoding)
            {
                // dump table for debugging
                WriteLine("Decode tree: ");
                var entry = 0;
                foreach (var row in state.Table)
                {
                    ++entry;
                    Write($"  {entry,3}: {row.Item1,3} -> ");
                    foreach (var s in row.Item2)
                        Write($"{s,3}, ");
                    WriteLine();
                }
            }

            if (Options.HasFlag(OptionFlags.DumpDecoding))
                WriteLine("Huff decode [");

            return state.SymbolLength;
        }

        void ParseTable(DecompresorState state1, bool useLowMemoryDecoding)
        {


            // want to save the minimum codeword length and the delta to the max codeword length
            // size of codeword min and delta to max

            // all header values
            state1.BitsPerSymbol = (byte)(1+UniversalCodec.Lomont.DecodeLomont1(state1.Bitstream, 3, 0));              // 1-32, usually 8, subtracting 1 gives 7, fits in 3 bits
            state1.BitsPerCodelengthCount=(byte)(1+UniversalCodec.Lomont.DecodeLomont1(state1.Bitstream, 3, 0));  // usually 4,5,6
            state1.MinCodewordLength=(byte)(1+UniversalCodec.Lomont.DecodeLomont1(state1.Bitstream,  2, 0));       // quite often 1,2,3,4, usually small
            uint deltaCodewordLength = 1+ UniversalCodec.Lomont.DecodeLomont1(state1.Bitstream, 4, -1);  // 9-12, up to 16,17

            state1.MaxCodewordLength = (byte)(state1.MinCodewordLength + deltaCodewordLength);

            if (Options.HasFlag(OptionFlags.DumpHeader))
            {
                WriteLine("Huffman encode header:");
                WriteLine($"   bits per symbol {state1.BitsPerSymbol} bits per code length count {state1.BitsPerCodelengthCount}");
                WriteLine($"   min len code {state1.MinCodewordLength} delta code len {deltaCodewordLength}");
            }


            // table entry i is # of this length i+1, then list of symbols of that length
            // walk counts, getting total table length
            // save this index as start of decoding table
            state1.TablePosition = state1.Bitstream.Position;

            if (!useLowMemoryDecoding)
                state1.Table = new List<Tuple<uint, Datastream>>();

            for (uint length = state1.MinCodewordLength; length <= state1.MaxCodewordLength; ++length)
            {
                uint count = state1.Bitstream.Read(state1.BitsPerCodelengthCount);
                if (!useLowMemoryDecoding)
                    state1.Table.Add(new Tuple<uint, Datastream>(count, new Datastream()));
                // read symbols
                for (uint symbolIndex = 0; symbolIndex < count; ++symbolIndex)
                {
                    byte symbol = (byte)state1.Bitstream.Read(state1.BitsPerSymbol);
                    if (!useLowMemoryDecoding)
                        state1.Table.Last().Item2.Add(symbol);
                }
            }
            if (!useLowMemoryDecoding && Options.HasFlag(OptionFlags.DumpDictionary))
            {
                WriteLine($"Decode min, max codeword lengths {state1.MinCodewordLength} {state1.MaxCodewordLength}");
                WriteLine("Decode huffman tree");
                for (var i = state1.MinCodewordLength; i <= state1.MaxCodewordLength; i++)
                {
                    var entry = state1.Table[i - state1.MinCodewordLength];
                    Write($"{i}: {entry.Item1,4} -> ");
                    foreach (var symbol in entry.Item2)
                        Write($"x{symbol:X2}, ");
                    WriteLine();
                }
            }
        }

        public override uint DecompressSymbol(Bitstream bitstream)
        {
            // items for walking the table
            uint accumulator = 0; // store bits read in until matches a codeword
            uint firstCodewordOnRow = 0; // first codeword on the current table entry

            // read min number of bits
            for (uint i = 0; i < state.MinCodewordLength; ++i)
            {
                Trace.Assert(accumulator < 0x80000000); // else will overflow!
                accumulator = 2*accumulator + state.Bitstream.Read(1); // accumulate bits
                firstCodewordOnRow <<= 1;
            }

            uint symbol = 0;
            bool symbolFound = false;
            if (Options.HasFlag(OptionFlags.UseLowMemoryDecoding))
            {
                uint tableIndex = state.TablePosition; // decoding table starts here
                while (!symbolFound)
                {

                    uint numberOfCodes = state.Bitstream.ReadFrom(ref tableIndex, state.BitsPerCodelengthCount);

                    if (numberOfCodes > 0 && accumulator - firstCodewordOnRow < numberOfCodes)
                    {
                        uint itemIndex = accumulator - firstCodewordOnRow;
                        tableIndex += itemIndex*state.BitsPerSymbol;
                        symbol = state.Bitstream.ReadFrom(ref tableIndex, state.BitsPerSymbol);
                        symbolFound = true;
                    }
                    else
                    {
                        firstCodewordOnRow += numberOfCodes;

                        Trace.Assert(accumulator < 0x80000000); // else will overflow!
                        accumulator = 2*accumulator + state.Bitstream.Read(1); // accumulate bits
                        firstCodewordOnRow <<= 1;

                        // next entry
                        tableIndex += numberOfCodes*state.BitsPerSymbol;
                    }
                }

            }
            else
            {
                int tblRow = 0; // only needed for low memory decoding
                while (!symbolFound)
                {
                    uint numberOfCodes = state.Table[tblRow].Item1;

                    if (numberOfCodes > 0 && accumulator - firstCodewordOnRow < numberOfCodes)
                    {
                        uint itemIndex = accumulator - firstCodewordOnRow;
                        symbol = state.Table[tblRow].Item2[(int) itemIndex];
                        symbolFound = true;
                    }
                    else
                    {
                        firstCodewordOnRow += numberOfCodes;

                        Trace.Assert(accumulator < 0x80000000); // else will overflow!
                        accumulator = 2*accumulator + state.Bitstream.Read(1); // accumulate bits
                        firstCodewordOnRow <<= 1;

                        // next entry
                        ++tblRow;
                    }
                }
            }

            if (Options.HasFlag(OptionFlags.DumpDecoding))
                Write($"{symbol:X2},");
            return symbol;
        }

        public override void ReadFooter(Bitstream bitstream)
        {
            if (Options.HasFlag(OptionFlags.DumpDecoding))
                WriteLine();
        }



#endregion

#region Implementation

        readonly List<Node> leaves = new List<Node>();

        /// <summary>
        /// Make probability tree
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        Node MakeTree(Datastream data)
        {
            // create a node for each symbol with the frequency count
            var nodes = new List<Node>();
            foreach (var symbol in data)
                AddNode(symbol, nodes);

            // create a parent for the lowest two freq nodes until single root node left
            while (nodes.Count > 1)
            {
                // get lowest freq node
                var minFreqL = nodes.Min(n => n.FrequencyCount);
                var left = nodes.First(n => n.FrequencyCount == minFreqL);
                nodes.Remove(left);

                // get second lowest freq node
                var minFreqR = nodes.Min(n => n.FrequencyCount);
                var right = nodes.First(n => n.FrequencyCount == minFreqR);
                nodes.Remove(right);

                // combine and reinsert
                nodes.Add(new Node
                {
                    LeftChild = left,
                    RightChild = right,
                    FrequencyCount = left.FrequencyCount + right.FrequencyCount
                    
                });
            }
            return nodes[0];
        }

        // turn the codewords into canonical ordered ones
        // needed for make table later
        void MakeCanonical(List<Node> leaves1)
        {
            // see https://pineight.com/mw/index.php?title=Canonical_Huffman_code
            // http://www.compressconsult.com/huffman
            // https://en.wikipedia.org/wiki/Canonical_Huffman_code

            // sort by bit lengths to order symbol values.
            // on ties, sort by symbol
            leaves1.Sort((a, b) =>
            {
                int result = a.Codeword.BitLength.CompareTo(b.Codeword.BitLength);
                if (result == 0)
                    return a.Symbol.CompareTo(b.Symbol);
                return result;
            });

            // rewrite symbols in increasing order, maintaining code lengths
            if (Options.HasFlag(OptionFlags.DumpDictionary))
                WriteLine("Huffman dictionary: ");
            var codeValue = new Codeword();
            codeValue.AppendBit(0); // start at the 1-bit code of 0
            foreach (var leaf in leaves1)
            {
                // lengthen code
                while (codeValue.BitLength < leaf.Codeword.BitLength)
                    codeValue.AppendBit(0);
                leaf.Codeword = Codeword.Clone(codeValue);
                if (Options.HasFlag(OptionFlags.DumpDictionary))
                    WriteLine($"  x{leaf.Symbol:X2}->{leaf.Codeword}");
                codeValue.Increment();
            }
        }


        /// <summary>
        /// Given the leaf nodes, create a canonical Huffman compression table
        /// Format is
        ///    Elias delta code bitsPerSymbol
        ///    Elias delta code maxCodeWordLength
        /// Then maxCodeWordLength counts of each codeword length, 
        /// Then sum of those lengths of symbols, each of the given length
        /// </summary>
        /// <param name="leaves1"></param>
        /// <returns></returns>
        Bitstream MakeTable(List<Node> leaves1)
        {
            Trace.Assert(leaves1.Count > 0);

            // longest codeword
            uint maxCodewordLength = leaves1.Max(n => n.Codeword.BitLength);
            uint minCodewordLength = leaves1.Min(n => n.Codeword.BitLength);
            WriteLine($"Min, max codeword lengths {minCodewordLength} {maxCodewordLength}");

            // get counts of each codeword length
            var codewordLengthCounts = new List<int>();
            for (var codewordLength = minCodewordLength; codewordLength <= maxCodewordLength; ++codewordLength)
                codewordLengthCounts.Add(leaves1.Count(n=> n.Codeword.BitLength == codewordLength));

            if (Options.HasFlag(OptionFlags.LogCodewordLengths))
            {
                for (var codewordLength = minCodewordLength; codewordLength <= maxCodewordLength; ++codewordLength)
                {
                    var count = codewordLengthCounts[(int)(codewordLength - minCodewordLength)];
                    StatRecorder.AddStat($"Huffman_Codeword_{codewordLength}", (uint)count);
                }
            }

            Trace.Assert(codewordLengthCounts.Sum() == leaves1.Count);

            // bits for each item to store
            uint bitsPerSymbol = BitsRequired(leaves1.Max(n=>n.Symbol));

            // codeword length is < alphabet size (proof: look at tree to make codewords)

            // the largest count of codewords of a given length is ceiling (log_2(alphabet size))
            // look at construction tree to see this
            var bitsPerCodelengthCount = BitsRequired((uint)codewordLengthCounts.Max());

            if (Options.HasFlag(OptionFlags.DumpDictionary))
            {
                // write table for debugging
                WriteLine("Make huffman tree:");
                for (var length = minCodewordLength; length <= maxCodewordLength; ++length)
                {
                    Write($"  {length,3}: {codewordLengthCounts[(int)(length - minCodewordLength)],3} -> ");
                    var length1 = length; // avoid modified closure
                    foreach (var s in leaves1.Where(n => n.Codeword.BitLength == length1))
                        Write($"x{s.Symbol:X2}, ");
                    WriteLine();
                }
            }

            // now write the bit sizes of each entry type, then counts of distinct lengths, then the symbols
            var bs = new Bitstream();


            // want to save the minimum codeword length and the delta to the max codeword length
            // size of codeword min and delta to max
            uint deltaCodewordLength = maxCodewordLength - minCodewordLength;

            // all header values
            UniversalCodec.Lomont.EncodeLomont1(bs, bitsPerSymbol-1,3,0);              // 1-32, usually 8, subtracting 1 gives 7, fits in 3 bits
            UniversalCodec.Lomont.EncodeLomont1(bs, bitsPerCodelengthCount - 1, 3,0);  // usually 4,5,6
            UniversalCodec.Lomont.EncodeLomont1(bs, minCodewordLength -1 , 2, 0);       // quite often 1,2,3,4, usually small
            UniversalCodec.Lomont.EncodeLomont1(bs, deltaCodewordLength - 1, 4,-1);  // 9-12, up to 16,17

            if (Options.HasFlag(OptionFlags.DumpHeader))
            {
                WriteLine("Huffman encode header:");
                WriteLine($"   bits per symbol {bitsPerSymbol} bits per code length count {bitsPerCodelengthCount}");
                WriteLine($"   min len code {minCodewordLength} delta code len {deltaCodewordLength}");
            }

            // write table - one entry for each codeword length present, entry is count then symbols
            int symbolIndex = 0;
            for (uint length = minCodewordLength; length <= maxCodewordLength; ++length)
            {
                int count = codewordLengthCounts[(int)(length - minCodewordLength)];
                bs.Write((uint)count,bitsPerCodelengthCount);
                // write 'count' symbols
                for (int j = 0; j < count; ++j)
                    bs.Write(leaves1[symbolIndex++].Symbol, bitsPerSymbol);
            }
            return bs;
        }

        class Codeword
        {
            /// <summary>
            /// The codeword, low values first, low bits first
            /// For a byte size alphabet, codewords can be at most 256 bits, 
            /// so need at most eight 32-bit words
            /// However, needing more than 32 bits is rare (if impossible), so 
            /// for simplicity we use 32 bits and throw exception on overflow
            /// </summary>
            uint value;

            /// <summary>
            /// Number of bits in code, 0 if no codeword
            /// </summary>
            public uint BitLength { get; private set; }

            public void AppendBit(uint bit)
            {
                value = (value<<1)+(bit&1);
                ++BitLength;
                Trace.Assert(BitLength<33);
            }

            /// <summary>
            /// Get codeword bit 0 to BitLength-1, where 0 is LSB
            /// </summary>
            /// <param name="bitIndex"></param>
            /// <returns></returns>
            public uint GetBit(uint bitIndex)
            {
                if (BitLength <= bitIndex)
                    throw new ArgumentException("Invalid bit index");
                return GetBitUnchecked(bitIndex);
            }

            /// <summary>
            /// Same as GetBit, but not bounds checked
            /// </summary>
            /// <param name="bitIndex"></param>
            /// <returns></returns>
            uint GetBitUnchecked(uint bitIndex)
            {
                return (value >> (int)bitIndex) & 1;
            }

            /// <summary>
            /// Deep clone the codeword
            /// </summary>
            /// <param name="codeword"></param>
            /// <returns></returns>
            public static Codeword Clone(Codeword codeword)
            {
                return new Codeword
                {
                    BitLength = codeword.BitLength,
                    value = codeword.value
                };
            }

            /// <summary>
            /// Add one to codeword value
            /// </summary>
            public void Increment()
            {
                value += 1;
                if (GetBitUnchecked(BitLength) == 1)
                    BitLength++; // possible overflow to higher bitlength
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                for (var i = (int)BitLength - 1; i >= 0; --i)
                    sb.Append(GetBit((uint)i));
                return sb.ToString();
            }
        }
        /// <summary>
        /// Store a node in the symbol tree
        /// </summary>
        class Node
        {
            /// <summary>
            /// value for symbol
            /// </summary>
            public uint Symbol;
            /// <summary>
            /// sum of frequencies of all nodes under and including this one
            /// </summary>
            public int FrequencyCount;
            /// <summary>
            /// Left child
            /// </summary>
            public Node LeftChild;
            /// <summary>
            /// Right child
            /// </summary>
            public Node RightChild;

            /// <summary>
            /// code to get here is a tuple of codeword and bitlength.
            /// </summary>
            public Codeword Codeword { get; set; } = new Codeword();

            /// <summary>
            /// Is this node a leaf? true if no children
            /// </summary>
            public bool IsLeaf => RightChild == null && LeftChild == null;
        }

        void AddNode(uint symbol, List<Node> nodes)
        {
            var node = nodes.FirstOrDefault(n => n.Symbol == symbol);
            if (node == null)
                nodes.Add(new Node {Symbol = symbol, FrequencyCount = 1});
            else
                node.FrequencyCount++;
        }

        Codeword NextCodeword(Codeword codeword, uint suffixBit)
        {
            var c = Codeword.Clone(codeword);
            c.AppendBit(suffixBit);
            return c;
        }

        /// <summary>
        /// Assign nodes their bit values
        /// </summary>
        /// <param name="node"></param>
        void AssignBitStrings(Node node)
        {
            if (node == null) return;
            if (node.LeftChild != null)
                node.LeftChild.Codeword = NextCodeword(node.Codeword, 0);
            if (node.RightChild != null)
                node.RightChild.Codeword = NextCodeword(node.Codeword, 1);
            AssignBitStrings(node.LeftChild);
            AssignBitStrings(node.RightChild);
        }
        
        /// <summary>
        /// Walk tree, getting all leaf nodes
        /// </summary>
        /// <param name="node"></param>
        void GetLeaves(Node node)
        {
            if (node == null) return;
            if (node.IsLeaf)
                leaves.Add(node);
            GetLeaves(node.LeftChild);
            GetLeaves(node.RightChild);
        }
#endregion

    }
}