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
#include "main.h"

/*********************** Configuration bits ***********************************/

// clock config bits
#pragma config FPLLIDIV = DIV_3         // PLL Input Divider
#pragma config FPLLMUL  = MUL_20        // PLL Multiplier
#pragma config FPLLODIV = DIV_2         // PLL Output Divider
#pragma config FPBDIV   = DIV_1         // Peripheral Clock divisor
#pragma config POSCMOD  = HS            // Primary Oscillator
#pragma config FNOSC    = PRIPLL        // Oscillator Selection

// remaining DEVCGF0
#pragma config CP       = ON            // Code Protect
#pragma config BWP      = ON            // Boot Flash Write Protect
#pragma config PWP      = OFF           // Program Flash Write Protect
#pragma config ICESEL   = ICS_PGx2      // ICE/ICD Comm Channel Select
#pragma config JTAGEN   = OFF           // JTAG Enable (JTAG Disabled)
#pragma config DEBUG    = OFF           // Background Debugger Enable

// remaining DEVCFG1
#pragma config FWDTWINSZ = WISZ_25      // Watchdog Timer Window Size (Window Size is 25%)
#pragma config FWDTEN   = OFF           // Watchdog Timer
#pragma config WINDIS   = OFF           // Watchdog Timer Window Enable (Watchdog Timer is in Non-Window Mode)
#pragma config WDTPS    = PS512         // Watchdog Timer Postscale (1:1), normal trips once per ms, this slows
#pragma config FCKSM    = CSDCMD        // Clock Switching & Fail Safe Clock Monitor (Clock Switch Disable, FSCM Disabled)
#pragma config OSCIOFNC = OFF           // CLKO Enable
#pragma config IESO     = OFF           // Internal/External Switch-over
#pragma config FSOSCEN  = OFF           // Secondary Oscillator Enable (disabled)


int peripheralClock = 0;
int GetPeripheralClock()
{
    return peripheralClock;
}

/*********************** UART settings ****************************************/

// define these if UART is needed
#define UART_DEFAULT_BAUDRATE    (2500000) // The desired startup baud rate
#define UART_DEFAULT_SAMPLE_RATE      (16) // 4 or 16
#define UART_DEFAULT_UART_PORTNUM        1 // default UART number to use, 1,2,...,6
#define UART_DEFAULT_UART_PORT       UART1 // default UART to use, must match integer version

#define UART_DEFAULT_RX_IOPORT    IOPORT_A // default UART receive port and bit number
#define UART_DEFAULT_RX_IOBIT        BIT_4 // digital in port for UART
#define UART_DEFAULT_TX_IOPORT    IOPORT_A // default UART transmit port and bit number
#define UART_DEFAULT_TX_IOBIT        BIT_0 // digital out port for UART
// any special command goes here
#define UART_DEFAULT_HARDWARE_CONFIG() U1RXR=2; RPA0R=1; // RPA4 = U1RX and RPA0 = U1TX

// the currently selected UART id, one of UART1, UART2, ... UART6
UART_MODULE selectedUartId = UART_DEFAULT_UART_PORT;
uint32_t actualUartBaudRate = 0; // todo - set from calling set rate, default to 0 until started
uint8_t  uartSampleRate     = UART_DEFAULT_SAMPLE_RATE;   // 4 or 16 - todo - default with macro?

// Initialize the UART settings to the default settings.
// If startDefault is true, start the default UART.
// requires the PB clock is set, so call, for example,
// int pbClock = SYSTEMConfig( SYS_CLOCK, SYS_CFG_WAIT_STATES | SYS_CFG_PCACHE);
// before calling this.
// Returns true on success, else false
bool InitializeUART()
{
    // set the input/output bits
    PORTClearBits(UART_DEFAULT_RX_IOPORT, UART_DEFAULT_RX_IOBIT);
    PORTClearBits(UART_DEFAULT_TX_IOPORT, UART_DEFAULT_TX_IOBIT);
    PORTSetPinsDigitalIn(UART_DEFAULT_RX_IOPORT, UART_DEFAULT_RX_IOBIT);
    PORTSetPinsDigitalOut(UART_DEFAULT_TX_IOPORT, UART_DEFAULT_TX_IOBIT);

    // any special configuration
    UART_DEFAULT_HARDWARE_CONFIG();

    // ensure this is set
    selectedUartId = UART_DEFAULT_UART_PORT;

    uint32_t desiredBaudRate = UART_DEFAULT_BAUDRATE;
    uint8_t sampleRate =  UART_DEFAULT_SAMPLE_RATE;
    
    if (sampleRate != 4 && sampleRate != 16)
    { // invalid sampling rate
        return false; // error!
    }

    uint32_t pbClockRate = GetPeripheralClock();

    if (desiredBaudRate > pbClockRate/sampleRate)
    { // too fast
        return false; // error!
    }

    UART_CONFIGURATION configMode = UART_ENABLE_PINS_TX_RX_ONLY;
    if (sampleRate == 4)
        configMode |= UART_ENABLE_HIGH_SPEED;
    UARTConfigure(selectedUartId, configMode);

    UARTSetFifoMode(selectedUartId, 0);

    UARTSetLineControl(selectedUartId, 
              UART_DATA_SIZE_8_BITS|
              UART_PARITY_NONE|
              UART_STOP_BITS_1
            );

    // generate and save settings
    actualUartBaudRate = UARTSetDataRate(selectedUartId, pbClockRate, desiredBaudRate);
    uartSampleRate = sampleRate;

    UARTEnable(selectedUartId, 
          UART_ENABLE |
          UART_PERIPHERAL | 
          UART_RX |
          UART_TX
            );

    return true;
}

// blocking call to write byte to currently selected UART
void UARTWriteByteBlocking(uint8_t byte)
{
    while (!UARTTransmitterIsReady(selectedUartId))
    { // wait till bit clear, then ready to transmit - todo - timeout?
    }
    UARTSendDataByte(selectedUartId, byte);

    // NOTE: uses something like
    // while (!U1STAbits.TRMT) {} // wait for ready
    // U1TXREG = byte; // write a byte
}

// print the message to the currently selected UART
// blocking
static void UARTPrint(const char * message)
{
    while (*message != 0)
    {
        UARTWriteByteBlocking(*message);
        ++message;
    }
}

/*********************** Printing *********************************************/

#define TEXT_SIZE 100

// buffer for all text formatting
char text[TEXT_SIZE];

void Print(const char* format, ...)
{
    va_list argptr;
    va_start(argptr, format);
    if (format == NULL)
        return; // error!
    else
    {
        // size clamp the output - triggers constraint handler if error
        int length = vsnprintf(
                text,
                TEXT_SIZE, 
                format, 
                argptr); // causes section overflow...

        // check overflow and note it
        if (length >= TEXT_SIZE-1 || length < 0)
            return; // error!

        if (length > 0)
            UARTPrint(text);
    }
    va_end(argptr);
}


void Initialize()
{
        // All of these items will affect the performance of your code and cause it to run significantly slower than you would expect.
        // Configure the device for maximum performance but do not change the PBDIV
        // Given the options, this function will change the flash wait states, RAM
        // wait state and enable prefetch cache but will not change the PBDIV.
        // The PBDIV value is already set via the pragma FPBDIV option above..
	peripheralClock = SYSTEMConfigPerformance(SYS_CLOCK);

	WriteCoreTimer(0); // Core timer ticks once every two clocks (verified)

	// set default digital port A for IO
	DDPCONbits.JTAGEN = 0; // turn off JTAG
	DDPCONbits.TROEN = 0; // ensure no tracing on

    if (!InitializeUART())
        return; // error!

    // enable multivectored interrupts
	INTEnableSystemMultiVectoredInt();
}
/*
 * From xc32-gcc v1.31 -O3 optimized code
Chris Lomont decompression testing, version 0.1, clock 40000000
Codec                    , start size, end size, ratio,     ticks, KB/s, Canaries, Checksum,
                  Huffman,      17572,    26217,   67%,   6884046,   74,       OK,    11045,
               Arithmetic,      17474,    26217,   66%, 141888684,    3,       OK,    11045,
                     LZ77,      13905,    26217,   53%,   1565265,  327,       OK,    11045,
                     LZCL,      11067,    26217,   42%,  30542748,   16,       OK,    11045,
      Huffman incremental,      17572,    26217,   67%,   6857839,   74,       OK,    11045,
   Arithmetic incremental,      17474,    26217,   66%, 141888676,    3,       OK,    11045,
         LZ77 incremental,      13905,    26217,   53%,   1794330,  285,       OK,    11045,
         LZCL incremental,      11067,    26217,   42%,  30771760,   16,       OK,    11045,
Done.
 */

int main(void)
{
    Initialize();

    
    Print(ENDLINE ENDLINE);
    Print("Chris Lomont decompression testing, version %d.%d, clock %d" ENDLINE, 
            VERSION>>4, VERSION&15,
            GetPeripheralClock()
            );

    
    DoTests();
    
    Print("Done."ENDLINE);
    
    while (true)
    {
        // infinite loop
    }

    return 0;
}

