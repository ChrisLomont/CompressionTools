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
using System.Linq;

namespace Lomont.Compression.Codec
{
    public class FixedSizeCodec :CodecBase
    {
        #region Compression functions
        /// <summary>
        /// Write the header for the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="data"></param>
        /// <param name="headerFlags">Flags telling what to put in the header. Useful when embedding in other streams.</param>
        /// <returns></returns>
        public override void WriteHeader(Bitstream bitstream, Datastream data, Header.HeaderFlags headerFlags)
        {
            // always store bps also
            Header.WriteUniversalHeader(bitstream, data, headerFlags | Header.HeaderFlags.BitsPerSymbol);
            var max = data.Any() ? data.Max() : 0;
            BitsPerSymbol = BitsRequired(max);
        }

        /// <summary>
        /// Compress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        /// <param name="symbol"></param>
        public override void CompressSymbol(Bitstream bitstream, uint symbol)
        {
            bitstream.Write(symbol,BitsPerSymbol);
        }

        /// <summary>
        /// Finish the stream
        /// </summary>
        /// <param name="bitstream"></param>
        public override void WriteFooter(Bitstream bitstream)
        {
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
            var header = Header.ReadUniversalHeader(bitstream, headerFlags | Header.HeaderFlags.BitsPerSymbol);
            uint symbolCount = header.Item1;
            BitsPerSymbol = header.Item2;
            return symbolCount;
        }

        /// <summary>
        /// Decompress a symbol in the compression algorithm
        /// </summary>
        /// <param name="bitstream"></param>
        public override uint DecompressSymbol(Bitstream bitstream)
        {
            return bitstream.Read(BitsPerSymbol);
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
        uint BitsPerSymbol { get; set; } = 8;
        #endregion


    }
}
