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
// reference decompressor for Chris Lomont tiny decompression library
#include <inttypes.h>
#include <string.h> // for memset
#include "Decompressor.h"

/************************* utility functions **********************************/

// Count number of ones set in value
static uint32_t OnesCount(uint32_t value)
{
	// trick: map 2 bit values into sum of two 1-bit values in sneaky way
	value -= (value >> 1) & 0x55555555;
	value = ((value >> 2) & 0x33333333) + (value & 0x33333333); // each 2 bits is sum
	value = ((value >> 4) + value) & 0x0f0f0f0f; // each 4 bits is sum
	value += value >> 8; // each 8 bits is sum
	value += value >> 16; // each 16 bits is sum
	return value & 0x0000003f; // mask out answer
}

// Compute Floor of Log2 of value
// 0 goes to 0
static uint32_t FloorLog2(uint32_t value)
{
	// flow bits downwards
	value |= value >> 1;
	value |= value >> 2;
	value |= value >> 4;
	value |= value >> 8;
	value |= value >> 16;
	// return (OnesCount(value) - 1); // if log 0 undefined, returns -1 for 0
	return (OnesCount(value >> 1)); // returns 0 for 0
}

// Bits required to store value. 
// Value 0 returns 1.
// Is 1+Floor[Log_2 n] = Ceiling[Log_2 (n+1)]
static uint32_t BitsRequired(uint32_t value)
{
	return 1 + FloorLog2(value);
}

/************************* bitstream implementation ***************************/

// Initialize a bitstream to read from the given position
static void InitializeBitstream(Bitstream_t * bitstream, const void * data)
{
	bitstream->data = data;
	bitstream->position = 0;
}

// Read values from current position, MSB first
static uint32_t ReadBitstream(Bitstream_t * bitstream, uint32_t bitLength)
{
	uint32_t value = 0, i;
	const uint8_t * bits = bitstream->data;
	for (i = 0; i < bitLength; ++i)
	{
		uint32_t pos = bitstream->position;
		uint32_t b1 = bits[pos / 8];
		uint32_t b = ((b1 >> (7 - (pos & 7))) & 1);
		bitstream->position++;
		value <<= 1;
		value |= b;
	}
	return value;
}

// Read values from given position, update it, MSB first
static uint32_t ReadFromBitstreamPosition(Bitstream_t * bitstream, uint32_t * position, uint32_t bitLength)
{
	uint32_t temp = bitstream->position; // save internal state
	bitstream->position = *position;
	uint32_t value = ReadBitstream(bitstream, bitLength);
	*position = bitstream->position; // update external value
	bitstream->position = temp; // restore internal
	return value;
}


/************************* universal coding implementation ********************/

// Decode Lomont method 1 
static uint32_t DecodeUniversalLomont1(Bitstream_t * bitstream, int chunkSize, int deltaChunk)
{
	uint32_t value = 0, b;
	int shift = 0;
	do
	{
		b = ReadBitstream(bitstream, 1); // decision
		uint32_t chunk = ReadBitstream(bitstream, (uint32_t)chunkSize);
		value += chunk << shift;
		shift += chunkSize;

		if (deltaChunk != 0)
		{
			chunkSize += deltaChunk;
			if (chunkSize <= 0)
				chunkSize = 1;
		}

	} while (b != 0);
	return value;
}


/************************* Universal compression header ***********************/

// get size of decompressed stream
uint32_t GetDecompressedSize(const uint8_t * source)
{
	Bitstream_t bitstream;
	InitializeBitstream(&bitstream, source);
	return DecodeUniversalLomont1(&bitstream, 6, 0); // number of bytes to decompress
}


/************************* Huffman coding implementation **********************/

#ifdef DECOMPRESSOR_USE_HUFFMAN

static void ParseHuffmanTable(HuffmanState_t * state)
{
	// table entry i is # of this length i+1, then list of symbols of that length
	// walk counts, getting total table length
	// save this index as start of decoding table
	state->tablePosition = state->bitstream.position;

	uint32_t length;
	for (length = state->minCodewordLength; length <= state->maxCodewordLength; ++length)
	{
		uint32_t count = ReadBitstream(&state->bitstream, state->bitsPerCodelengthCount);
		// read symbols - todo - simply add to position
		uint32_t symbolIndex;
		for (symbolIndex = 0; symbolIndex < count; ++symbolIndex)
			ReadBitstream(&state->bitstream, state->bitsPerSymbol);
	}
}

// read from header, skipping byte length, assumes state bitstream is set
static void ReadHuffmanHeaderNoLength(HuffmanState_t * state)
{
	// want to save the minimum codeword length and the delta to the max codeword length
	// size of codeword min and delta to max

	// general header
	// all header values
	state->bitsPerSymbol = (uint8_t)(1 + DecodeUniversalLomont1(&state->bitstream, 3, 0));  // 1-32, usually 8, subtracting 1 gives 7, fits in 3 bits
	state->bitsPerCodelengthCount = (uint8_t)(1 + DecodeUniversalLomont1(&state->bitstream, 3, 0));  // usually 4,5,6
	state->minCodewordLength = (uint8_t)(1 + DecodeUniversalLomont1(&state->bitstream, 2, 0));  // quite often 1,2,3,4, usually small
	uint32_t deltaCodewordLength = 1 + DecodeUniversalLomont1(&state->bitstream, 4, -1);  // 9-12, up to 16,17

	state->maxCodewordLength = (uint8_t)(state->minCodewordLength + deltaCodewordLength);
	ParseHuffmanTable(state);
}

// Call after starting decompression with DecompressHuffmanStart to get individual symbols
// decompress a symbol 0-255. Returns CL_COMPRESSOR_END_TOKEN when no more
EXPORT_WIN32 uint32_t DecompressHuffmanSymbol(HuffmanState_t * state)
{
	if (state->byteLength == 0)
		return CL_COMPRESSOR_END_TOKEN;
	if (state->byteLength != 0xFFFFFFFF)
		state->byteLength--;
	// items for walking the table
	uint32_t accumulator = 0;        // store bits read in until matches a codeword
	uint32_t firstCodewordOnRow = 0; // first codeword on the current table entry

									 // read min number of bits
	uint32_t i;
	for (i = 0; i < state->minCodewordLength; ++i)
	{
		accumulator = 2 * accumulator + ReadBitstream(&state->bitstream, 1); // accumulate bits
		firstCodewordOnRow <<= 1;
	}

	uint32_t tableIndex = state->tablePosition; // decoding table starts here
	while (1)
	{

		uint32_t numberOfCodes = ReadFromBitstreamPosition(&state->bitstream, &tableIndex, state->bitsPerCodelengthCount);

		if (numberOfCodes > 0 && accumulator - firstCodewordOnRow < numberOfCodes)
		{
			uint32_t itemIndex = accumulator - firstCodewordOnRow;
			tableIndex += itemIndex * state->bitsPerSymbol;
			return ReadFromBitstreamPosition(&state->bitstream, &tableIndex, state->bitsPerSymbol);
		}
		firstCodewordOnRow += numberOfCodes;

		accumulator = 2 * accumulator + ReadBitstream(&state->bitstream, 1); // accumulate bits
		firstCodewordOnRow <<= 1;

		// next entry
		tableIndex += numberOfCodes * state->bitsPerSymbol;
	}
}

// Partial call decompression
// start decompression, follow up with DecompressHuffmanSymbol until done
EXPORT_WIN32 void DecompressHuffmanStart(HuffmanState_t * state, const uint8_t * source, int32_t sourceLength)
{
	InitializeBitstream(&state->bitstream, source);
	// size header
	state->byteLength = DecodeUniversalLomont1(&state->bitstream, 6, 0); // number of bytes to decompress
	ReadHuffmanHeaderNoLength(state);
}


// decompress, return bytes decoded
EXPORT_WIN32 int32_t DecompressHuffman(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength)
{
	HuffmanState_t state;
	DecompressHuffmanStart(&state, source, sourceLength);

	// now decode bytes
	uint32_t byteIndex = 0;

	while (state.byteLength != 0 && byteIndex < (uint32_t)destLength)
	{
		uint32_t symbol = DecompressHuffmanSymbol(&state);
		if (symbol == CL_COMPRESSOR_END_TOKEN)
			break;
		dest[byteIndex] = symbol;
		++byteIndex;
	}

	return (int32_t)byteIndex;
}

#endif // DECOMPRESSOR_USE_HUFFMAN

/************************* Arithmetic coding implementation *******************/

#ifdef DECOMPRESSOR_USE_ARITHMETIC

// constants defining the range
#define range25Percent  0x20000000U
#define range50Percent  0x40000000U
#define range75Percent  0x60000000U
#define range100Percent 0x80000000U

static uint32_t ReadArithmeticBistream(ArithmeticState_t * state, uint32_t bitCount)
{
	state->bitsRead++;
	if (state->bitsRead < state->bitLength)
		return ReadBitstream(&state->bitstream, bitCount);
	return 0;
}

static void DecodeArithmeticTable(ArithmeticState_t * state)
{
	Bitstream_t * bs = &state->bitstream;
	// Table thus only full type. Format is
	//   - symbol min index used, max index used, Lomont1 universal coded.
	//   - Number of bits in table, Lomont1 universal coded (allows jumping past)
	//   - Full table. Counts are BASC encoded, maxIndex - minIndex+1 entries

	state->symbolMin = DecodeUniversalLomont1(bs,6,0);
	uint32_t symbolMax = DecodeUniversalLomont1(bs, 6, 0);
	uint32_t tableBitLength = DecodeUniversalLomont1(bs, 6, 0);
	state->tableStartBitPosition = bs->position;
	bs->position += tableBitLength;
}

// read from header, assumes state bitstream is set
// return number of symbols
static uint32_t ReadArithmeticHeaderNoLength(ArithmeticState_t * state)
{
	state->lowValue = 0; // lower bound, inclusive
	state->highValue = range100Percent - 1; // upper bound, inclusive

	state->total = DecodeUniversalLomont1(&state->bitstream, 6, 0);
	state->bitLength = DecodeUniversalLomont1(&state->bitstream, 8, -1);
	state->bitsRead = 0; // start tracking bits read to handle short decodable streams

	uint32_t tempPos = state->bitstream.position;
	DecodeArithmeticTable(state);
	state->bitsRead = state->bitstream.position - tempPos;

	// start buffer with 31 bits
	state->buffer = 0;
	uint32_t i;
	for (i = 0; i < 31; ++i)
		state->buffer = (state->buffer << 1) | ReadArithmeticBistream(state, 1);

	return state->total;
}

// Read the header for the compression algorithm
// Return number of symbols in stream if known, else 0 if not present
EXPORT_WIN32 uint32_t DecompressArithmeticStart(ArithmeticState_t * state, const uint8_t * source, int32_t sourceLength)
{
	// init coder state
	InitializeBitstream(&state->bitstream, source);

	return ReadArithmeticHeaderNoLength(state);
}

// lookup symbol and probability range using table decoding
static uint32_t LookupArithmeticLowMemoryCount(ArithmeticState_t * state, uint32_t cumCount, uint32_t * lowCount, uint32_t * highCount)
{
	// BASC encoded, decode with same process

	uint32_t tempPosition = state->bitstream.position; // save this
	state->bitstream.position = state->tableStartBitPosition;

	*lowCount = *highCount = 0;
	uint32_t symbol = 0;

	
	uint32_t length = DecodeUniversalLomont1(&state->bitstream, 6, 0);
	if (length != 0)
	{
		uint32_t b1 = DecodeUniversalLomont1(&state->bitstream, 6, 0);
		uint32_t xi = ReadBitstream(&state->bitstream, b1);

		*lowCount = 0;
		*highCount = xi;
		symbol = state->symbolMin;
		uint32_t i = state->symbolMin;

		while (*highCount <= cumCount)
		{
			uint32_t decision = ReadBitstream(&state->bitstream, 1);
			if (decision == 0)
			{
				// bi is <= b(i-1), so enough bits
				xi = ReadBitstream(&state->bitstream, b1);
			}
			else
			{
				// bi is bigger than b(i-1), must increase it
				uint32_t delta = 0;
				do
				{
					decision = ReadBitstream(&state->bitstream, 1);
					delta++;
				} while (decision != 0);
				b1 += delta;
				xi = ReadBitstream(&state->bitstream, b1 - 1); // xi has implied leading 1
				xi |= 1U << (int)(b1 - 1);
			}
			b1 = BitsRequired(xi);

			*lowCount = *highCount;
			*highCount += xi;
			++i;
			if (xi != 0)
				symbol = i;
		}
	}

	state->bitstream.position = tempPosition; // restore this
	return symbol;
}


// Decompress a symbol in the compression algorithm
EXPORT_WIN32 uint32_t DecompressArithmeticSymbol(ArithmeticState_t * state)
{
	//	Trace.Assert(total < (1 << 29)); // todo - this sufficient when 8 bit symbols, but what when larger?
	//	Trace.Assert(lowValue <= buffer && buffer <= highValue);

	//split range into single steps
	uint32_t step = (state->highValue - state->lowValue + 1) / state->total; // interval open at top gives +1

	uint32_t lowCount, highCount;
	uint32_t symbol = LookupArithmeticLowMemoryCount(state, (state->buffer - state->lowValue) / step, &lowCount, &highCount);

	// upper bound
	state->highValue = state->lowValue + step*highCount - 1; // interval open top gives -1
															 // lower bound											   
	state->lowValue = state->lowValue + step*lowCount;

	// e1/e2 scaling
	while ((state->highValue < range50Percent) || (state->lowValue >= range50Percent))
	{
		if (state->highValue < range50Percent)
		{
			state->lowValue = 2 * state->lowValue;
			state->highValue = 2 * state->highValue + 1;
			//Trace.Assert(buffer <= 0x7FFFFFFF);
			state->buffer = 2 * state->buffer + ReadArithmeticBistream(state, 1);
		}
		else if (state->lowValue >= range50Percent)
		{
			state->lowValue = 2 * (state->lowValue - range50Percent);
			state->highValue = 2 * (state->highValue - range50Percent) + 1;
			//Trace.Assert(buffer >= range50Percent);
			state->buffer = 2 * (state->buffer - range50Percent) + ReadArithmeticBistream(state, 1);
		}
	}

	// e3 scaling
	while ((range25Percent <= state->lowValue) && (state->highValue < range75Percent))
	{
		state->lowValue = 2 * (state->lowValue - range25Percent);
		state->highValue = 2 * (state->highValue - range25Percent) + 1;
		//Trace.Assert(buffer >= range25Percent);
		state->buffer = 2 * (state->buffer - range25Percent) + ReadArithmeticBistream(state, 1);
	}
	return symbol;
}

EXPORT_WIN32 int32_t DecompressArithmetic(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength)
{
	ArithmeticState_t state;
	uint32_t symbolCount = DecompressArithmeticStart(&state, source, sourceLength);

	uint32_t i;
	for (i = 0; i < symbolCount; ++i)
		dest[i] = DecompressArithmeticSymbol(&state);
	return symbolCount;
}

// cleanup
#undef range25Percent 
#undef range50Percent 
#undef range75Percent 
#undef range100Percent

#endif // DECOMPRESSOR_USE_ARITHMETIC

/************************* LZ77 coding implementation *************************/
#ifdef DECOMPRESSOR_USE_LZ77

// Partial call decompression
// start decompression, follow up with DecompressLZ77Block until done
// requires buffer dest of length enough to handle the max look-back used when compressing the block
EXPORT_WIN32 void DecompressLZ77Start(LZ77State_t * state, const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength)
{
	InitializeBitstream(&state->bitstream, source);

	// header values
	state->byteLength = DecodeUniversalLomont1(&state->bitstream, 6, 0);
	state->actualBitsPerSymbol = DecodeUniversalLomont1(&state->bitstream, 3, 0) + 1;  // usually 8, -1 => 7, fits in 3 bits
	state->actualBitsPerToken = DecodeUniversalLomont1(&state->bitstream, 5, 0) + 1;   // around 20 or so, depends on parameters
	state->actualMinLength = DecodeUniversalLomont1(&state->bitstream, 2, 0);          // usually 2
	state->actualMaxToken = DecodeUniversalLomont1(&state->bitstream, 25, -10);        // 
	state->actualMaxDistance = DecodeUniversalLomont1(&state->bitstream, 14, -7);      // 
	state->byteIndex = 0;
	state->dest = dest;
	state->destLength = destLength;
}

// Call after starting decompression with DecompressLZ77Start to get block of symbols
// symbols are written into the dest buffer passed in, they are written in a cyclical manner
// returns number of items decompressed, or CL_COMPRESSOR_END_TOKEN when no more.
EXPORT_WIN32 uint32_t DecompressLZ77Block(LZ77State_t * state)
{

	if (state->byteIndex >= state->byteLength)
		return CL_COMPRESSOR_END_TOKEN;

	if (ReadBitstream(&state->bitstream, 1) == 0)
	{
		// literal
		uint32_t lit = ReadBitstream(&state->bitstream, state->actualBitsPerSymbol);
		state->dest[state->byteIndex % state->destLength] = (uint8_t)lit;
		state->byteIndex++;
		return 1;
	}
	else
	{
		// run
		uint32_t token = ReadBitstream(&state->bitstream, state->actualBitsPerToken);
		// decode token
		uint32_t length = token / (state->actualMaxDistance + 1) + state->actualMinLength;
		uint32_t distance = token % (state->actualMaxDistance + 1);

		// copy run
		uint32_t i;
		// positive delta that looks back when taken mod destLength
		uint32_t delta = state->destLength - distance - 1;
		for (i = 0; i < length; ++i)
		{
			state->dest[state->byteIndex % state->destLength] =
				state->dest[(state->byteIndex + delta) % state->destLength];
			state->byteIndex++;
		}
		return length;
	}
}

// decompress, return bytes decoded
EXPORT_WIN32 int32_t DecompressLZ77(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength)
{
	LZ77State_t state;
	DecompressLZ77Start(&state, source, sourceLength, dest, destLength);
	uint32_t symbolCount = 0;
	while (symbolCount != CL_COMPRESSOR_END_TOKEN)
		symbolCount = DecompressLZ77Block(&state);

	return (int32_t)state.byteIndex;
}
#endif // DECOMPRESSOR_USE_LZ77


#ifdef DECOMPRESSOR_USE_LZCL // uses the following few decoders

/************************* Fixed length coding implementation *****************/
// read from header, assumes state bitstream is set
// return number of symbols
static void ReadFixedHeaderNoLength(FixedState_t * state)
{
	state->bitsPerSymbol = DecodeUniversalLomont1(&state->bitstream,3,0) + 1;
}

// Call after starting decompression with ReadFixedHeaderNoLength to get individual symbols
// decompress a symbol 0-255. 
static uint32_t DecompressFixedSymbol(FixedState_t * state)
{
	return ReadBitstream(&state->bitstream, state->bitsPerSymbol);
}

/************************* Golomb coding implementation ***********************/

// read from header, assumes state bitstream is set
static void ReadGolombHeaderNoLength(GolombState_t * state)
{
	state->parameter = DecodeUniversalLomont1(&state->bitstream, 6, 0);
}

// Decodes truncated code item
static uint32_t DecodeTruncated(Bitstream_t * bitstream, uint32_t n)
{
	uint32_t k = BitsRequired(n);
	uint32_t u = (1U << (int)k) - n; // u = number of unused codewords
	uint32_t x = ReadBitstream(bitstream,k - 1);
	if (x >= u)
	{ // need another bit
		x = 2 * x + ReadBitstream(bitstream,1);
		x -= u;
	}
	return x;
}


// Call after starting decompression with ReadGolombHeaderNoLength to get individual symbols
// decompress a symbol 0-255. 
static uint32_t DecompressGolombSymbol(GolombState_t * state)
{
	uint32_t q = 0U;
	while (ReadBitstream(&state->bitstream, 1) == 1)
		++q;
	uint32_t r = DecodeTruncated(&state->bitstream, state->parameter);
	return q *(state->parameter) + r;
}


/************************* LZCL coding implementation *************************/

static uint32_t DecodeLZCLSymbol(LZCLSubCodec_t * codec)
{
	if (codec->codecType == 0)
		return DecompressFixedSymbol(&codec->fixedState);
	else if (codec->codecType == 1)
		return DecompressArithmeticSymbol(&codec->arithmeticState);
	else if (codec->codecType == 2)
		return DecompressHuffmanSymbol(&codec->huffmanState);
	else if (codec->codecType == 3)
		return DecompressGolombSymbol(&codec->golombState);
	return 0xBADC0DE;
}

// Get codec type, a bitstream for it, read the header, advance the bitstream
// the codec, the bit length, and the bitstream
static void ReadLZCLItem(LZCLSubCodec_t * decoder, Bitstream_t * bitstream)
{
	// get type
	decoder->codecType = ReadBitstream(bitstream, 2);
	
	// get encoded bit length
	decoder->bitLength = DecodeUniversalLomont1(bitstream, 6, 0);

	// prepare a bitstream for the codec, parse header
	if (decoder->codecType == 0)
	{
		decoder->fixedState.bitstream.data = bitstream->data;
		decoder->fixedState.bitstream.position = bitstream->position;
		ReadFixedHeaderNoLength(&decoder->fixedState);
	}
	else if (decoder->codecType == 1)
	{
		decoder->arithmeticState.bitstream.data = bitstream->data;
		decoder->arithmeticState.bitstream.position = bitstream->position;
		ReadArithmeticHeaderNoLength(&decoder->arithmeticState);
	}
	else if (decoder->codecType == 2)
	{
		decoder->huffmanState.bitstream.data = bitstream->data;
		decoder->huffmanState.bitstream.position = bitstream->position;
		ReadHuffmanHeaderNoLength(&decoder->huffmanState);
		decoder->huffmanState.byteLength = 0xFFFFFFFF; // mark continual run
	}
	else if (decoder->codecType == 3)
	{
		decoder->golombState.bitstream.data = bitstream->data;
		decoder->golombState.bitstream.position = bitstream->position;
		ReadGolombHeaderNoLength(&decoder->golombState);
	}
	//else // todo - error?
	//	throw new NotImplementedException("Unknown compressor type");

	// skip general bitstream ahead
	bitstream->position += decoder->bitLength;
}

static uint32_t GetLZCLDecision(LZCLState_t * state)
{
	if (state->useDecisionRuns == 0)
		return DecodeLZCLSymbol(&state->decisionCodec);
	if (state->curRun == -1)
	{
		state->curRun = (int)state->initialValue;
		state->runsLeft = DecodeLZCLSymbol(&state->decisionRunCodec);
	}
	if (state->runsLeft == 0)
	{
		state->curRun ^= 1; // toggle direction
		state->runsLeft = DecodeLZCLSymbol(&state->decisionRunCodec);
	}
	--state->runsLeft;
	return (uint32_t)state->curRun;
}

static void GetLZCLDecodedToken(LZCLState_t * state, uint32_t * distance1, uint32_t * length1)
{
	if (state->useTokens == 0)
	{
		*distance1 = DecodeLZCLSymbol(&state->distanceCodec);
		*length1 = DecodeLZCLSymbol(&state->lengthCodec) + state->actualMinLength;
	}
	else
	{
		uint32_t token = DecodeLZCLSymbol(&state->tokenCodec);
		*length1 = token / (state->actualMaxDistance + 1) + state->actualMinLength;
		*distance1 = token % (state->actualMaxDistance + 1);
	}
}


// Read the header for the compression algorithm
// Return number of symbols in stream if known, else 0 if not present
EXPORT_WIN32 uint32_t DecompressLZCLStart(LZCLState_t * state, const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength)
{
	// initalize state
	memset(state, 0, sizeof(LZCLState_t));
	state->curRun = -1;

	// save info about buffer we can write into
	state->dest = dest;
	state->destLength = destLength;

	// prepare bitstream
	InitializeBitstream(&state->bitstream, source);

	// read header values
	state->byteLength = DecodeUniversalLomont1(&state->bitstream, 6, 0);  // number of bytes to decompress
	state->actualMaxDistance = DecodeUniversalLomont1(&state->bitstream, 10, 0); // max distance occurring
	state->actualMinLength = DecodeUniversalLomont1(&state->bitstream, 2, 0);  // min length occurring

	// see if decisions or decision runs
	if (ReadBitstream(&state->bitstream, 1) == 0)
	{
		state->useDecisionRuns = 0;
		ReadLZCLItem(&state->decisionCodec, &state->bitstream);
	}
	else
	{
		state->useDecisionRuns = 1;
		// read initial value
		state->initialValue = ReadBitstream(&state->bitstream, 1);
		// read item
		ReadLZCLItem(&state->decisionRunCodec, &state->bitstream);
	}

	// literals
	ReadLZCLItem(&state->literalCodec, &state->bitstream);

	// tokens or separate distance, length pairs
	if (ReadBitstream(&state->bitstream, 1) == 0)
		 ReadLZCLItem(&state->tokenCodec, &state->bitstream);
	else
	{
		 ReadLZCLItem(&state->distanceCodec, &state->bitstream);
		 ReadLZCLItem(&state->lengthCodec, &state->bitstream);
	}
	return state->byteLength;
}

// Call after starting decompression with DecompressLZCLStart to get block of symbols
// symbols are written into the dest buffer passed in, they are written in a cyclical manner
// returns number of items decompressed, or CL_COMPRESSOR_END_TOKEN when no more.
EXPORT_WIN32 uint32_t DecompressLZCLBlock(LZCLState_t * state)
{
	if (state->byteIndex >= state->byteLength)
		return CL_COMPRESSOR_END_TOKEN;

		if (GetLZCLDecision(state) == 0)
		{
			// literal
			uint32_t symbol = DecodeLZCLSymbol(&state->literalCodec);
			state->dest[state->byteIndex % state->destLength] = (uint8_t)symbol;
			state->byteIndex++;
			return 1;
		}
		else
		{
			// token - either a single token or a token pair
			uint32_t distance, length;
			GetLZCLDecodedToken(state,&distance,&length);

			// copy run
			uint32_t i;
			// positive delta that looks back when taken mod destLength
			uint32_t delta = state->destLength - distance - 1;
			for (i = 0; i < length; ++i)
			{
				state->dest[state->byteIndex % state->destLength] =
					state->dest[(state->byteIndex + delta) % state->destLength];
				state->byteIndex++;
			}
			return length;
		}
}

 // decompress, return bytes decoded
 EXPORT_WIN32 int32_t DecompressLZCL(const uint8_t * source, int32_t sourceLength, uint8_t * dest, int32_t destLength)
 {
	 LZCLState_t state;
	 DecompressLZCLStart(&state, source, sourceLength, dest, destLength);
	 uint32_t symbolCount = 0;
	 while (symbolCount != CL_COMPRESSOR_END_TOKEN)
		 symbolCount = DecompressLZCLBlock(&state);

	 return (int32_t)state.byteIndex;
 }

#endif // DECOMPRESSOR_USE_LZCL

/************************* END OF CODE ****************************************/