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
#ifndef _CL_DECOMPRESSOR
#define _CL_DECOMPRESSOR
#include <inttypes.h>

#ifdef __cplusplus
extern "C" {
#endif

/** TODO
* DONE 1. Needs read header - generic gets decoded byte length = bits/symbol rounded up to byte * symbol count
* 2. Needs callback versions that callback a function with a byte and an index
*/


// for using as a DLL in windows for testing
#ifdef WIN32
#define EXPORT_WIN32 __declspec(dllexport)
#else
#define EXPORT_WIN32
#endif

// Define which of these to include    
#define DECOMPRESSOR_USE_HUFFMAN
#define DECOMPRESSOR_USE_ARITHMETIC
#define DECOMPRESSOR_USE_LZ77
#define DECOMPRESSOR_USE_LZCL


// LZCL requires these decompressors
#ifdef DECOMPRESSOR_USE_LZCL
#ifndef DECOMPRESSOR_USE_HUFFMAN
#define DECOMPRESSOR_USE_HUFFMAN
#endif
#ifndef DECOMPRESSOR_USE_ARITHMETIC
#define DECOMPRESSOR_USE_ARITHMETIC
#endif
#ifndef DECOMPRESSOR_USE_LZ77
#define DECOMPRESSOR_USE_LZ77
#endif
#endif

    
// some incremental decompressors return this when done    
#define CL_COMPRESSOR_END_TOKEN 0xFFFFFFFF
    
typedef struct
{
	// bit position for next read or write
	uint32_t position;
	// base of data
	const void * data;
} Bitstream_t;

// get size of a decompressed stream
EXPORT_WIN32 uint32_t GetDecompressedSize(const uint8_t * source);

#ifdef DECOMPRESSOR_USE_HUFFMAN
// State needed for Huffman decompression
typedef struct
{
	// current byte size for low memory decompressor: (4+4)+4+4+4*1 = 20 bytes

	// Stream to decode from
	Bitstream_t bitstream;
	// bit stream position where codeword table is stored
	uint32_t tablePosition;
	// Bytes left to decode (or 0xFFFFFFFF to mark unknown)
	uint32_t byteLength;
	// Bits per symbol in symbol table
	uint8_t bitsPerSymbol;
	// Minimum codeword length present
	uint8_t minCodewordLength;
	// Maximum codeword length present
	uint8_t maxCodewordLength;
	// Bits to store a count of codewords of the same length
	uint8_t bitsPerCodelengthCount;
} HuffmanState_t;

// Single call decompression:
// decompress, return bytes decoded
EXPORT_WIN32 int32_t DecompressHuffman(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength);

// Partial call decompression
// start decompression, follow up with DecompressHuffmanSymbol until done
EXPORT_WIN32 void DecompressHuffmanStart(HuffmanState_t * state, const uint8_t * source, int32_t sourceLength);

// Call after starting decompression with DecompressHuffmanStart to get individual symbols
// decompress a symbol 0-255. Returns CL_COMPRESSOR_END_TOKEN when no more
EXPORT_WIN32 uint32_t DecompressHuffmanSymbol(HuffmanState_t * state);

#endif // DECOMPRESSOR_USE_HUFFMAN

#ifdef DECOMPRESSOR_USE_ARITHMETIC

typedef struct
{
	Bitstream_t bitstream;

	// items for arithmetic part
	uint32_t lowValue, highValue, total;
	uint32_t scaling;

	// items for table decoding
	uint32_t symbolMin;
	uint32_t tableStartBitPosition;

	// lookahead buffer
	uint32_t buffer;

	// track bits read to end stream
	uint32_t bitLength; // bits in compressed region
	uint32_t bitsRead;  // bits read from compressed region

} ArithmeticState_t;

// decompress, return bytes decoded
EXPORT_WIN32 int32_t DecompressArithmetic(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength);

// Partial call decompression
// start decompression, follow up with DecompressArithmeticSymbol until done
// Return number of symbols in stream if known, else 0 if not present
EXPORT_WIN32 uint32_t DecompressArithmeticStart(ArithmeticState_t * state, const uint8_t * source, int32_t sourceLength);

// Decompress a symbol in the compression algorithm, or CL_COMPRESSOR_END_TOKEN when done
EXPORT_WIN32 uint32_t DecompressArithmeticSymbol(ArithmeticState_t * state);

#endif // DECOMPRESSOR_USE_ARITHMETIC

#ifdef DECOMPRESSOR_USE_LZ77

// State needed for LZ77 decompression
typedef struct
{
	// stream to decode from
	Bitstream_t bitstream;
	// bytes decoded
	uint32_t byteIndex;
	// total bytes to decode
	uint32_t byteLength;

	// destination buffer and size
	uint8_t * dest;
	uint32_t destLength;
	// defines how tokens are stored
	uint32_t actualMaxToken;
	uint32_t actualMaxDistance;
	uint8_t  actualMinLength;
	// define the bit sizes of items
	uint8_t  actualBitsPerSymbol;
	uint8_t  actualBitsPerToken;
} LZ77State_t;

// decompress, return bytes decoded
// needs enough to decode entire item
EXPORT_WIN32 int32_t DecompressLZ77(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength);

// Partial call decompression
// start decompression, follow up with DecompressLZ77Block until done
// requires buffer dest of length enough to handle the max look-back plus the max length used when compressing the block
EXPORT_WIN32 void DecompressLZ77Start(LZ77State_t * state, const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength);

// Call after starting decompression with DecompressLZ77Start to get block of symbols
// symbols are written into the dest buffer passed in, they are written in a cyclical manner
// returns number of items decompressed, or CL_COMPRESSOR_END_TOKEN when no more.
EXPORT_WIN32 uint32_t DecompressLZ77Block(LZ77State_t * state);

#endif // DECOMPRESSOR_USE_LZ77

#ifdef DECOMPRESSOR_USE_LZCL
// two additional small codecs
typedef struct
{
	Bitstream_t bitstream;
	uint32_t bitsPerSymbol;
} FixedState_t;

typedef struct
{
	Bitstream_t bitstream;
	uint32_t parameter;
} GolombState_t;

typedef struct
{
	union {
		FixedState_t fixedState;
		ArithmeticState_t arithmeticState;
		HuffmanState_t huffmanState;
		GolombState_t golombState;
	};
	uint8_t codecType; // codec type 0-3
	uint32_t bitLength; // 
}  LZCLSubCodec_t;

typedef struct {
	uint32_t actualMinLength;
	uint32_t actualMaxDistance;
	uint32_t byteLength, byteIndex;
	Bitstream_t bitstream;
	uint8_t useDecisionRuns; // how to decode decisions
	union {
		LZCLSubCodec_t decisionCodec;
		LZCLSubCodec_t decisionRunCodec;
	};
	LZCLSubCodec_t literalCodec;
	uint8_t useTokens; // how to decode distance/length pairs
	union {
		struct {
			LZCLSubCodec_t tokenCodec;
		};
		struct {
			LZCLSubCodec_t distanceCodec;
			LZCLSubCodec_t lengthCodec;
		};
	};
	uint32_t initialValue;
	uint8_t * dest;
	uint32_t destLength;

	// stuff for decoding runs
	int32_t curRun;    // 0 or 1, -1 if none decoded yet
	uint32_t runsLeft; // runs of current type left
} LZCLState_t;

// decompress, return bytes decoded
EXPORT_WIN32 int32_t DecompressLZCL(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength);

// Read the header for the compression algorithm
// Return number of symbols in stream if known, else 0 if not present
EXPORT_WIN32 uint32_t DecompressLZCLStart(LZCLState_t * state, const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength);

// Call after starting decompression with DecompressLZCLStart to get block of symbols
// symbols are written into the dest buffer passed in, they are written in a cyclical manner
// returns number of items decompressed, or CL_COMPRESSOR_END_TOKEN when no more.
EXPORT_WIN32 uint32_t DecompressLZCLBlock(LZCLState_t * state);


#endif // DECOMPRESSOR_USE_LZCL

#ifdef __cplusplus
}
#endif

#endif // _CL_DECOMPRESSOR
