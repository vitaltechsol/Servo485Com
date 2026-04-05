using EasyModbus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Servo485Com
{
    using System;
    using System.Threading;
    using EasyModbus;

    public enum AccelMode
    {
        None = 0, // Pn109=0
        Linear = 1, // Pn109=1, Pn110=time constant (ms)
        SCurve = 2  // Pn109=2, Pn111=Ta (ms), Pn112=Ts (ms)
    }

    public class AasdServoRs485
    {
        private readonly ModbusClient mbc;

        // Core Pn parameters (numbers are the Pn indices you write with WriteSingleRegister)
        private const int Pn002 = 2;   // Control mode
        private const int Pn117 = 117; // Location command source
        private const int Pn118 = 118; // pause behavior selector (optional)
        private const int Pn119 = 119; // pause decel time (ms) (optional)

        // Accel shaping for position moves
        private const int Pn109 = 109; // accel/decel mode (0 none, 1 linear, 2 s-curve)
        private const int Pn110 = 110; // linear filter time constant (ms)
        private const int Pn111 = 111; // S-curve Ta (ms)
        private const int Pn112 = 112; // S-curve Ts (ms)

        // Internal position slots — pulses (high/low) and speed (rpm)
        private static readonly int[,] SlotPulsePn = new int[,]
        {
        {120, 121}, // slot 0: high, low
        {122, 123}, // slot 1
        {124, 125}, // slot 2
        {126, 127}, // slot 3
        };
        private static readonly int[] SlotSpeedPn = { 128, 129, 130, 131 };

        // Input comm-control select (which functions are controlled by comms)
        private const int Pn068 = 68;   // low bank (e.g., Son)
        private const int Pn069 = 69;   // high bank (e.g., Pos1/Pos2/Ptriger/Pstop)

        // Input logic status words (active level is 0 = ON)
        private const int Pn070 = 70;   // includes Son, etc.
        private const int Pn071 = 71;   // includes Pos1/Pos2/Ptriger/Pstop

        // Bit positions (Pn070 / Pn071)
        private const int BIT_SON = 0;  // Pn070 bit0: Son (0=ON)
        private const int BIT_POS1 = 8;  // Pn071 bit8: Pos1 (0=ON)
        private const int BIT_POS2 = 9;  // Pn071 bit9: Pos2 (0=ON)
        private const int BIT_PTRIGER = 10; // Pn071 bit10: Ptriger (0=ON)
        private const int BIT_PSTOP = 11; // Pn071 bit11: Pstop (0=ON)

        public AasdServoRs485(ModbusClient mbc)
        {
            this.mbc = mbc ?? throw new ArgumentNullException(nameof(mbc));
        }

        // --- low-level helpers ---
        private ushort ReadPn(int pn) => unchecked((ushort)mbc.ReadHoldingRegisters(pn, 1)[0]);
        private void WritePn(int pn, int val) => mbc.WriteSingleRegister(pn, val);

        private void SetBitLow(int pn, int bit)  // 0 = ON
        {
            var cur = ReadPn(pn);
            var next = (ushort)(cur & ~(1 << bit));
            if (next != cur) WritePn(pn, next);
        }
        private void SetBitHigh(int pn, int bit) // 1 = OFF
        {
            var cur = ReadPn(pn);
            var next = (ushort)(cur | (1 << bit));
            if (next != cur) WritePn(pn, next);
        }

        // One-time init for internal-position via RS-485 (leave masks as-is if you’ve already set them)
        public void Init(bool setCommControlMasks = false, int pn068Mask = 0x0001, int pn069Mask = 0x0F00)
        {
            // Position mode + internal position source
            WritePn(Pn002, 2);  // position mode
            WritePn(Pn117, 1);  // internal position source

            // Optional: pause behavior (Pn118=0 drop remaining; =1 resume remaining). Your choice.
            // WritePn(Pn118, 1);
            // WritePn(Pn119, 50); // ms decel on pstop

            // Hand control of Son + Pos1/Pos2/Ptriger/Pstop to communication (adjust masks to your setup)
            if (setCommControlMasks)
            {
                WritePn(Pn068, pn068Mask); // e.g., enable comm control for Son
                WritePn(Pn069, pn069Mask); // e.g., enable comm control for Pos1/Pos2/Ptriger/Pstop
            }

            // Enable servo (Son active-low)
            SetBitLow(Pn070, BIT_SON);
        }

        /// <summary>
        /// Move to an absolute position (pulses) with speed (rpm) and acceleration shaping.
        /// - rpm: 0..3000 (clamped)
        /// - accelMode: None, Linear(timeMs), or SCurve(Ta, Ts)
        /// - accelParam1Ms: Linear: timeMs; SCurve: Ta ms
        /// - accelParam2Ms: SCurve: Ts ms (ignored for None/Linear)
        /// - slot: which internal slot to use (0..3). Defaults to 0.
        /// </summary>
        public void MoveTo(int pulses, int rpm, AccelMode accelMode,
                           int accelParam1Ms, int accelParam2Ms = 0,
                           int slot = 0, int triggerPulseMs = 30)
        {
            if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
            // 1) Configure acceleration shaping (Pn109..Pn112)
            switch (accelMode)
            {
                case AccelMode.None:
                    WritePn(Pn109, 0);
                    break;

                case AccelMode.Linear:
                    WritePn(Pn109, 1);
                    WritePn(Pn110, Clamp(accelParam1Ms, 5, 500)); // linear time constant (ms)
                    break;

                case AccelMode.SCurve:
                    WritePn(Pn109, 2);
                    WritePn(Pn111, Clamp(accelParam1Ms, 5, 340)); // Ta
                    WritePn(Pn112, Clamp(accelParam2Ms, 5, 150)); // Ts
                    break;
            }

            // 2) Set slot speed (Pn128..Pn131)
            int speedPn = SlotSpeedPn[slot];
            WritePn(speedPn, Clamp(rpm, 0, 3000));

            // 3) Program slot pulses (Pn120..Pn127)
            WriteSlotPulses(slot, pulses);

            // 4) Select slot via Pos1/Pos2 and pulse Ptriger
            SelectSlot(slot);
            PulsePtriger(triggerPulseMs);
        }

        private void WriteSlotPulses(int slot, int pulses)
        {
            int highPn = SlotPulsePn[slot, 0];
            int lowPn = SlotPulsePn[slot, 1];

            int high = pulses / 10000;
            int low = pulses % 10000;
            if (pulses < 0 && low != 0) { high -= 1; low = 10000 + low; }

            WritePn(highPn, high);
            WritePn(lowPn, low);
        }

        private void SelectSlot(int slot)
        {
            // Truth table (Off=1, On=0): N=0 => pos1=Off, pos2=Off; N=1 => Off/On; N=2 => On/Off; N=3 => On/On
            bool pos1On = (slot == 1) || (slot == 3);
            bool pos2On = (slot == 2) || (slot == 3);

            // Set Off (1) by SetBitHigh; set On (0) by SetBitLow
            if (pos1On) SetBitLow(Pn071, BIT_POS1); else SetBitHigh(Pn071, BIT_POS1);
            if (pos2On) SetBitLow(Pn071, BIT_POS2); else SetBitHigh(Pn071, BIT_POS2);
        }

        private void PulsePtriger(int pulseMs)
        {
            // Ensure high (OFF), then drive low (ON), then release high again
            SetBitHigh(Pn071, BIT_PTRIGER);
            Thread.Sleep(5);
            SetBitLow(Pn071, BIT_PTRIGER);
            Thread.Sleep(Math.Max(10, pulseMs));
            SetBitHigh(Pn071, BIT_PTRIGER);
        }

        // Optional helper: stop (pstop is active-low)
        public void Stop()
        {
            SetBitLow(Pn071, BIT_PSTOP);
            Thread.Sleep(20);
            SetBitHigh(Pn071, BIT_PSTOP);
        }

        private static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi) ? hi : v;
    }

}
