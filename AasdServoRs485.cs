using EasyModbus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Servo485Com
{
    public class AasdServoRs485
    {
        private readonly ModbusClient mbc;

        // Pn numbers we’ll touch (write via WriteSingleRegister)
        private const int Pn002 = 2;   // Control mode
        private const int Pn117 = 117; // Location command source
        private const int Pn120 = 120; // Internal position 0 - high (ten-thousands of pulses)
        private const int Pn121 = 121; // Internal position 0 - low (ones)
        private const int Pn128 = 128; // Internal position 0 speed (r/min)
        private const int Pn068 = 68;  // Input function comm-control select reg 1
        private const int Pn069 = 69;  // Input function comm-control select reg 2
        private const int Pn070 = 70;  // Input function logic state reg 1 (0 = ON)
        private const int Pn071 = 71;  // Input function logic state reg 2 (0 = ON)

        // Bit positions inside Pn070/Pn071 (from the manual tables)
        // Pn070 bits (low): bit0=Son
        private const int BIT_SON = 0; // Pn070 bit0 -> Son (0 = ON)

        // Pn071 bits (high): bit8=Pos1, bit9=Pos2, bit10=Ptriger, bit11=Pstop
        private const int BIT_POS1 = 8;
        private const int BIT_POS2 = 9;
        private const int BIT_PTRIGER = 10;
        private const int BIT_PSTOP = 11;

        public AasdServoRs485(ModbusClient mbc) => this.mbc = mbc ?? throw new ArgumentNullException(nameof(mbc));

        // Utility: read-modify-write helpers for Pn070/Pn071 words
        private ushort ReadPn(int pn) => unchecked((ushort)mbc.ReadHoldingRegisters(pn, 1)[0]);
        private void WritePn(int pn, int value) => mbc.WriteSingleRegister(pn, value);

        private void SetBitLow(int pn, int bit) // 0 = ON (active)
        {
            ushort cur = ReadPn(pn);
            ushort next = (ushort)(cur & ~(1 << bit));
            if (next != cur) WritePn(pn, next);
        }
        private void SetBitHigh(int pn, int bit) // 1 = OFF (inactive)
        {
            ushort cur = ReadPn(pn);
            ushort next = (ushort)(cur | (1 << bit));
            if (next != cur) WritePn(pn, next);
        }

        /// One-time setup for internal-position over comms.
        public void Init()
        {
            // Mode: Position; Source: Internal position
            WritePn(Pn002, 2);   // Position mode
            WritePn(Pn117, 1);   // Internal position source

            // Hand control of inputs to communication for the bits we will drive.
            // NOTE: exact mask for Pn068/069 depends on which functions you want comm-controlled.
            // Set the bits so Son (Pn070 bit0) and Pos1/Pos2/Ptriger/Pstop (Pn071 bits 8..11) are comm-controlled.
            // If you already set these, you can skip the next two lines or replace with your known masks.
            WritePn(Pn068, 0x0001); // example: enable comm control for Son (low bank). Adjust to your config.
            WritePn(Pn069, 0x0F00); // example: enable comm control for Pos1/Pos2/Ptriger/Pstop (bits 8..11). Adjust as needed.

            // Enable servo (Son = 0 means ON)
            SetBitLow(Pn070, BIT_SON);
        }

        /// Move to an absolute target (pulses) using Internal Position slot N=0.
        /// Optionally set a speed in r/min (defaults to 300 r/min if null).
        public void MoveToPulses(int pulses, int? rpm = null, int triggerPulseMs = 30)
        {
            // Program slot 0: high = tens of thousands, low = ones
            int high = pulses / 10000;
            int low = pulses % 10000;
            if (pulses < 0 && low != 0) { high -= 1; low = 10000 + low; } // keep high/low signs consistent

            WritePn(Pn120, high);
            WritePn(Pn121, low);

            if (rpm.HasValue) WritePn(Pn128, Math.Max(0, Math.Min(3000, rpm.Value))); // clamp to 0..3000 r/min

            // Select internal position N=0 via Pos1/Pos2 = OFF/OFF (remember: 1=OFF, 0=ON)
            SetBitHigh(Pn071, BIT_POS1); // OFF
            SetBitHigh(Pn071, BIT_POS2); // OFF

            // Pulse ptriger: OFF(1) -> ON(0) -> OFF(1)
            SetBitHigh(Pn071, BIT_PTRIGER);   // ensure HIGH (OFF)
            Thread.Sleep(5);
            SetBitLow(Pn071, BIT_PTRIGER);    // drive LOW (ON) -> triggers read of slot 0
            Thread.Sleep(Math.Max(10, triggerPulseMs));
            SetBitHigh(Pn071, BIT_PTRIGER);   // release
        }

        public void StopInternalPosition()
        {
            // Pstop is active-low (0=ON). Drive it low momentarily.
            SetBitLow(Pn071, BIT_PSTOP);
            Thread.Sleep(20);
            SetBitHigh(Pn071, BIT_PSTOP);
        }
    }
}
