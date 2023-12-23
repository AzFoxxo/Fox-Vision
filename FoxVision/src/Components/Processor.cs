#define SKIP_DEBUG

using System.Diagnostics;
using FoxVision.Components;

namespace FoxVision
{
    /*
    *   This class is the main processor for the virtual machine.
    *   It does not deal with graphical operations.
    *   Note: This class defers from the technical specification
    *   in regard to the OSR register. Unlike the 8 bit OSR register,
    *   separate unsigned 16 bit variables are used for each flag.
    *   This is done in an attempt to simplify the design of the processor.
    */
    internal class Processor
    {
        // Registers
        private ushort regX, regY, regPC;
        private ushort flgEqual, flgActiveReg, flgDivZero, flgHalt;
        private const long clock = 8_000_000;
        private const long ticksInSec = 10_000_000;
        private const long timeToWait = clock / ticksInSec;

        // CPU timer
        Stopwatch timer;

        ContiguousMemory RAM;

        /// <summary>
        ///  Create the CPU
        /// </summary>
        /// <param name="RAM">Reference to the RAM</param>
        internal Processor(ContiguousMemory RAM)
        {
            // Set RAM reference
            this.RAM = RAM;

            // Set registers
            regX = 0x0;
            regY = 0x0;
            regPC = 0x0;
            flgHalt = 0x0;

            // Set flags
            flgEqual = 0x0;
            flgActiveReg = 0x0;

            // Create a new CPU timer
            timer = new Stopwatch();

            Console.WriteLine("Processor created");
        }

        internal bool ExecuteCycle()
        {
            timer.Start();

            // Read the data from RAM and validate bounds of second ushort
            ushort[] data = [RAM.ReadUnchecked(regPC), 0x0];
            if (regPC != RAM.Size)
                data[1] = RAM.ReadUnchecked((ushort)(regPC + 1));

            // Decode and execute the instruction
            regPC += DecodeExecuteInstruction(data[0], data[1]);

            // Bounds check PC
            if (regPC > RAM.Size) regPC = 0;

            timer.Stop();
            long elapsed_time = timer.ElapsedTicks;

            // Calculate how long to wait
            while (true)
            {
                if (elapsed_time >= timeToWait)
                    break;
            }

            // Return if halt OSR flag has been set
            return flgHalt != 0x0;
        }

        /// <summary>
        /// Decode and execute the instruction
        /// </summary>
        /// <param name="data">two uint16s of RAM (first opcode, second data)</param>
        /// <returns></returns>
        private ushort DecodeExecuteInstruction(ushort opcode, ushort data)
        {
            // Determine which opcode is being executed
            switch (opcode)
            {
                case 0x0:
                NOP:
                    LogInstructionExecuting("NOP");
                    break;
                case 0x1:
                    LogInstructionExecuting("LFM");
                    SetValueOfTheActiveRegister(RAM.ReadUnchecked((ushort)(data - 1)));
                    return 2;
                case 0x2:
                    LogInstructionExecuting("WTM");
                    RAM.WriteUnchecked((ushort)(data - 1), GetValueOfTheActiveRegister());
                    return 2;
                case 0x3:
                    LogInstructionExecuting("SRA");
                    ChangeActiveRegister(RAM.ReadUnchecked((ushort)(data - 1)));
                    return 2;
                case 0x4:
                    LogInstructionExecuting("AXY");
                    SetValueOfTheActiveRegister((ushort)(regX + regY));
                    break;
                case 0x5:
                    LogInstructionExecuting("SXY");
                    SetValueOfTheActiveRegister((ushort)(regX - regY));
                    break;
                case 0x6:
                    LogInstructionExecuting("MXY");
                    SetValueOfTheActiveRegister((ushort)(regX * regY));
                    break;
                case 0x7:
                    LogInstructionExecuting("DXY");
                    if (regX == 0)
                    {
                        SetValueOfTheActiveRegister(0);
                        flgDivZero = 0x1;
                        break;
                    }
                    SetValueOfTheActiveRegister((ushort)(regX / regY));
                    break;
                case 0x8:
                    LogInstructionExecuting("EQU");
                    if (regX == regY) flgEqual = 0x1; else flgEqual = 0x0;
                    break;
                case 0x9:
                    LogInstructionExecuting("LEQ");
                    if (regX < regY) flgEqual = 0x1; else flgEqual = 0x0;
                    break;
                case 0xA:
                    LogInstructionExecuting("JPZ");
                    if (flgEqual == 0x0) regPC = RAM.ReadUnchecked(data);
                    return 2;
                case 0xB:
                    LogInstructionExecuting("JNZ");
                    if (flgEqual != 0x0) regPC = RAM.ReadUnchecked(data);
                    return 2;
                case 0xC:
                    LogInstructionExecuting("JMP");
                    regPC = RAM.ReadUnchecked(data);
                    return 2;
                case 0xD:
                    LogInstructionExecuting("CLR");
                    flgEqual = 0x0;
                    flgActiveReg = 0x0;
                    flgDivZero = 0x0;
                    flgHalt = 0x0;
                    break;
                case 0xE:
                    LogInstructionExecuting("HLT");
                    flgHalt = 0x1;
                    break;
                case 0xF:
                    LogInstructionExecuting("BSL");
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() << 1));
                    break;
                case 0x10:
                    LogInstructionExecuting("BRL");
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() >> 1));
                    break;
                case 0x11:
                    LogInstructionExecuting("AND");
                    var and = (ushort)(GetValueOfTheActiveRegister() & GetValueOfTheInactiveRegister());
                    SetValueOfTheActiveRegister(and);
                    break;
                case 0x12:
                    LogInstructionExecuting("ORA");
                    var or = (ushort)(GetValueOfTheActiveRegister() | GetValueOfTheInactiveRegister());
                    SetValueOfTheActiveRegister(or);
                    break;
                case 0x13:
                    LogInstructionExecuting("ORA");
                    var xor = (ushort)(GetValueOfTheActiveRegister() ^ GetValueOfTheInactiveRegister());
                    SetValueOfTheActiveRegister(xor);
                    break;
                case 0x14:
                    LogInstructionExecuting("DWR");
                    SetValueOfTheActiveRegister(data);
                    return 2;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Illegal instruction");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"Attempt to interpret opcode {opcode:X4} with data {data:X4} ({data})");
                    Console.WriteLine("skipping instruction and executing NOP");
                    goto NOP;
            }

            return 1;
        }

        /// <summary>
        /// Debug log the opcode currently being executed
        /// </summary>
        /// <param name="opcode">String of the opcode</param>
        private void LogInstructionExecuting(string opcode)
        {
            // Skip
            #if SKIP_DEBUG
            return;
            #endif

            Console.Write("Executing: ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(opcode);
            Console.ForegroundColor = ConsoleColor.White;
        }

        /// <summary>
        /// Change the active register to
        /// </summary>
        /// <param name="regID">reg ID</param>
        private void ChangeActiveRegister(ushort regID) => flgActiveReg = regID;

        /// <summary>
        /// Get value from active register
        /// </summary>
        /// <returns>Value of the active register</returns>
        private ushort GetValueOfTheActiveRegister() => (flgActiveReg == 0x0) ? regX : regY;

        /// <summary>
        /// Get value from inactive register
        /// </summary>
        /// <returns>Value of the active register</returns>
        private ushort GetValueOfTheInactiveRegister() => (flgActiveReg == 0x0) ? regY : regX;

        /// <summary>
        /// Set active register value
        /// </summary>
        /// <param name="value">Value to set the active register to</param>
        private void SetValueOfTheActiveRegister(ushort value)
        {
            if (flgActiveReg == 0x0)
                regX = value;
            else
                regY = value;
        }

        /// <summary>
        /// Set inactive register value
        /// </summary>
        /// <param name="value">Value to set the inactive register to</param>
        /// </summary>
        private void SetValueOfTheInactiveRegister(ushort value)
        {
            if (flgActiveReg == 0x1)
                regX = value;
            else
                regY = value;
        }
    }
}