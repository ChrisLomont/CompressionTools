# Compression Tools - A low resource embedded compression library

This is a tool providing multiple forms of compression (Huffman, Arithmetic, a LZ77 variant, and my own LZ format, LZCL) suitable for low resource environments. A C# compression tool does the compression, provides decompression, and a single file decompressor written in C makes it easy to embed decompression in a C environment.

The usefulness of this is it allows very tunable decompression resource requirements for constrained environments.

The compression tools side provides a lot of primitives for developing compression algorithms with many pluggable and advanced pieces. 

Neither side is optimized for speed, as both were meant to be simple and clean to read/extend/implement.

## Usage

### Compression
From the help in CompressionTester.exe

    Usage:
     -i inputfile (can be path to do directory with recurse if no output file)
     -o outputfile (if none, no output files)
     -r recurse directories. No outputs
     -f output formats : C# C Binary
     -c codecs         : Arithmetic Huffman Lz77 Lzcl
        The following codec have parameters that can be selected.
        Select as: -c LZ77:param1=value1:param2=value2
                  Lz77:  maxDist - Max distance to look back in buffer
                  Lz77:   minLen - Minimum length of a run to be worthwhile
                  Lz77:   maxLen - Maximum length of a run to be worthwhile
                  Lzcl:  maxDist - Max distance to look back in buffer
                  Lzcl:   minLen - Minimum length of a run to be worthwhile
                  Lzcl:   maxLen - Maximum length of a run to be worthwhile
     -v verbose
     -t test - run current test set
     -d decompress, else compress

You can take a file, compress in one of 4 methods, output to binary or C code, and specify optional parameters if desired.

The compressors generally compress more in order Huffman, Arithmetic, LZ77, LZCL. For general use LZ77 is very good, but if you really need to fit more data in a small space LZCL will beat it handily.

LZ77 and LZCL allow setting the max lookback window and max length of a run. Lower generally lowers compression, but often the optimal value (exhaustively tested) is not large. The buffer size required for these two for incremental decompression is the max of maxDist and maxLen, plus 1. Setting minDist to other than default 2 has is not generally useful.  

### Decompression

To decompress in your project, add Decompressor.c and Decompressor.h. In Decompressor.h, select which compression routines you want by commenting out the ones you don't. LZCL needs all the others and will re-`#define` them.

    #define DECOMPRESSOR_USE_HUFFMAN
    #define DECOMPRESSOR_USE_ARITHMETIC
    #define DECOMPRESSOR_USE_LZ77
    #define DECOMPRESSOR_USE_LZCL

In your file needing decompression, `#include "Decompressor.h"`, then use code like

    uint8_t outputBuffer[DECOMPRESSED_SIZE]; 
    DecompressHuffman(compressedData, sizeof(compressedData), outputBuffer, DECOMPRESSED_SIZE);

to decompress in one large run. The same interface works with `Huffman` replaced by `Arithmetic`, `LZ77`, and `LZCL`. 

To decompress incrementally, use each like

    // incremental decompression
    uint32_t symbol, index;

    // Huffman
    HuffmanState_t huffmanState;
    DecompressHuffmanStart(&huffmanState, huffData, sizeof(huffData));
    index = 0;
    while (1)
    {
        symbol = DecompressHuffmanSymbol(&huffmanState);
        if (symbol == CL_COMPRESSOR_END_TOKEN)
            break;
        ... use symbol here...
    }
    
    // Arithmetic
    ArithmeticState_t arithmeticState;
    uint32_t length = DecompressArithmeticStart(&arithmeticState, arithData, sizeof(arithData));
    index = 0;
    while (index < length) 
    {
        symbol = DecompressArithmeticSymbol(&arithmeticState);
        ... use symbol here ...
    }

    // LZ77
    uint8_t localBuffer[<size determined by compression parameters>]; 
    uint32_t destIndex = 0, srcIndex = 0;
    LZ77State_t lz77State;
    DecompressLZ77Start(&lz77State, lz77Data, sizeof(lz77Data),localBuffer,sizeof(localBuffer));
    destIndex = srcIndex = 0;
    while (1)
    {
        uint32_t runLength = DecompressLZ77Block(&lz77State);
        if (runLength == CL_COMPRESSOR_END_TOKEN)
            break;
        // copy out run
        while (runLength-- > 0)
            ... use byte from localBuffer[(srcIndex++)%sizeof(localBuffer)]....
    }

    LZCLState_t lzclState;
    DecompressLZCLStart(&lzclState, lzclData, sizeof(lzclData),localBuffer,sizeof(localBuffer));
    destIndex = srcIndex = 0;
    while (1)
    {
        uint32_t runLength = DecompressLZCLBlock(&lzclState);
        if (runLength == CL_COMPRESSOR_END_TOKEN)
            break;
        // copy out run
        while (runLength-- > 0)
            ... use byte from localBuffer[(srcIndex++)%sizeof(localBuffer)]....
    }

And that's it!

## Benchmarks
Compression is generally not very fast, since the algorithms are designed to be easily extended, munged, and used to create more formats if needed. Decompression code is designed to be small, not fast, for the use case I wrote this code for. `Decompressor.c` fits all four decompression routines into a self contained 750ish line C file (~100 for Huffman, ~200 for arithmetic, ~75 for LZ77, ~250 LZCL, rest support.).

The PIC32 sample code, running on a PIC32MX150F128B at 40MHZ, compiled with x32-gcc v1.31, -O3 optimized code, obtains the following decompression rates

| Codec                  | Rate KB/s | Ratio |
| ---------------------- | --------- | ----- |
| Huffman                |   74      |  67%  |
| Arithmetic             |    3      |  66%  |
| LZ77                   |  327      |  53%  |
| LZCL                   |   16      |  42%  |
| Incremental Huffman    |   74      |  67%  |
| Incremental Arithmetic |    3      |  66%  |
| Incremental LZ77       |  285      |  53%  |
| Incremental LZCL       |   16      |  42%  |

This was tested on a compressed version of the 26217 byte file `Decompressor.c`, with both LZ variants set to allow decoding incrementally into a 257 byte buffer.

A common test for smaller files is the Calgary Corpus and Cantebury Corpus

|               Filename | File size | Arithmetic ratio | Arithmetic size | Huffman ratio | Huffman size | Lz77 ratio | Lz77 size | LZCL ratio |   LZCL size |
|------------------------|-----------|------------------|-----------------|---------------|--------------|------------|-----------|------------|-------------|
|            Calgary\bib |    111261 |            0.651 |           72483 |         0.655 |        72845 |      0.574 |     63813 |  **0.435** |   **48387** |
|          Calgary\book1 |    768771 |            0.566 |          435264 |         0.570 |       438461 |      0.661 |    508162 |  **0.486** |  **373303** |
|          Calgary\book2 |    610856 |            0.599 |          366165 |         0.603 |       368398 |      0.576 |    351818 |  **0.420** |  **256308** |
|            Calgary\geo |    102400 |            0.709 |           72596 |         0.711 |        72826 |      0.898 |     91974 |  **0.645** |   **66024** |
|           Calgary\news |    377109 |            0.649 |          244824 |         0.654 |       246492 |      0.648 |    244267 |  **0.454** |  **171081** |
|           Calgary\obj1 |     21504 |            0.755 |           16242 |         0.759 |        16321 |      0.648 |     13927 |  **0.527** |   **11339** |
|           Calgary\obj2 |    246814 |            0.784 |          193533 |         0.787 |       194366 |      0.484 |    119553 |  **0.361** |   **89035** |
|         Calgary\paper1 |     53161 |            0.626 |           33255 |         0.629 |        33433 |      0.535 |     28421 |  **0.426** |   **22649** |
|         Calgary\paper2 |     82199 |            0.577 |           47422 |         0.580 |        47706 |      0.571 |     46956 |  **0.448** |   **36842** |
|         Calgary\paper3 |     46526 |            0.586 |           27260 |         0.588 |        27360 |      0.568 |     26411 |  **0.474** |   **22036** |
|         Calgary\paper4 |     13286 |            0.596 |            7914 |         0.598 |         7942 |      0.543 |      7214 |  **0.477** |    **6334** |
|         Calgary\paper5 |     11954 |            0.627 |            7494 |         0.629 |         7522 |      0.564 |      6740 |  **0.478** |    **5718** |
|         Calgary\paper6 |     38105 |            0.630 |           23998 |         0.633 |        24117 |      0.561 |     21379 |  **0.431** |   **16412** |
|            Calgary\pic |    513216 |            0.152 |           77890 |         0.208 |       106726 |      0.153 |     78292 |  **0.103** |   **52851** |
|          Calgary\progc |     39611 |            0.653 |           25883 |         0.657 |        26007 |      0.502 |     19902 |  **0.417** |   **16519** |
|          Calgary\progl |     71646 |            0.598 |           42859 |         0.601 |        43071 |      0.362 |     25953 |  **0.277** |   **19819** |
|          Calgary\progp |     49379 |            0.611 |           30195 |         0.614 |        30303 |      0.351 |     17356 |  **0.275** |   **13568** |
|          Calgary\trans |     93695 |            0.693 |           64972 |         0.697 |        65318 |      0.412 |     38625 |  **0.312** |   **29219** |
|  Cantebury\alice29.txt |    152089 |            0.572 |           86979 |         0.577 |        87765 |      0.595 |     90565 |  **0.439** |   **66710** |
| Cantebury\asyoulik.txt |    125179 |            0.602 |           75380 |         0.606 |        75877 |      0.652 |     81660 |  **0.471** |   **58992** |
|      Cantebury\cp.html |     24603 |            0.660 |           16242 |         0.662 |        16295 |      0.501 |     12334 |  **0.423** |   **10414** |
|     Cantebury\fields.c |     11150 |            0.637 |            7097 |         0.638 |         7117 |      0.389 |      4340 |  **0.322** |    **3588** |
|  Cantebury\grammar.lsp |      3721 |            0.603 |            2245 |         0.604 |         2246 |      0.435 |      1617 |  **0.394** |    **1465** |
|   Cantebury\lcet10.txt |    426754 |            0.584 |          249251 |         0.587 |       250651 |      0.576 |    245861 |  **0.419** |  **178911** |
| Cantebury\plrabn12.txt |    481861 |            0.567 |          273117 |         0.572 |       275670 |      0.655 |    315855 |  **0.481** |  **231722** |
|         Cantebury\ptt5 |    513216 |            0.152 |           77890 |         0.208 |       106726 |      0.153 |     78292 |  **0.103** |   **52851** |
|          Cantebury\sum |     38240 |            0.673 |           25733 |         0.678 |        25915 |      0.562 |     21497 |  **0.412** |   **15746** |
|      Cantebury\xargs.1 |      4227 |            0.634 |            2679 |         0.633 |         2676 |      0.517 |      2184 |  **0.474** |    **2004** |
|                Summary |   5032533 |            0.518 |         2606862 |         0.533 |      2680152 |      0.510 |   2564968 |  **0.374** | **1879847** |

Finally, LZ77 and LZCL can vary drastically with different parameters - the above parameters are not tuned to be optimal, which can be done by exhaustively trying them.

## Project

The code includes a Visual Studio 2015 project that contains the C# compressor, a C DLL to test decompression, and also includes a PIC32 sample program to illustrate usage and to measure decompression speed. 

## LZCL

My LZCL format is a very bit optimized format, separating the usual LZ77 streams into components
   - Decisions: 0/1 to tell next token type
   - Literals: single bytes encoded
   - Tokens: each is a distance, length pair, encoded in one of two methods

It was designed to solve a use case I had to get the best compression I could, subject to very small code size and very low memory usage for an embedded project. I may finalize the spec carefully (it requires careful specification of multiple other sub-formats... a lot of work) one day.

Some advanced codecs that are small are used here: Golomb coding and Binary Adaptive Sequential Coding (BASC).   

Then, decisions is either stored as a bitstream of 0/1, or converted to runs (basic RLE compression). Tokens are tested as both (distance,length) pairs and encoded similarly to length*(maxDistance+1)+distance. All these variants are tested across all supported compression formats (Fixed length, Huffman, Arithmetic, Golomb Coding), and the best combination is chosen. 

All formats contribute a decent amount on the corpus tests above.


I plan to write two longer articles on all of this at (or possibly at lomont.org, once I merge sites):

http://clomont.com/programming/compression-notes/
http://clomont.com/software/lzcl-an-embedded-compression-library/

Happy hacking!
Chris Lomont