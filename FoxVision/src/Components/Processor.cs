#define SKIP_DEBUG

using System.Diagnostics;
using Fox16Shared;
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
            ushort oldPC = regPC;
            ushort diffPC = DecodeExecuteInstruction(data[0], data[1]);
            
            // If regPC and oldPC are the same, add diffPC
            if (regPC == oldPC)
                regPC += diffPC; // This ensures if PC was modified by a jump instruction, it isn't modified

            // Bounds check PC
            if (regPC > RAM.Size) regPC = 0;

            // Calculate how long to wait
            while (true)
            {
                if (timer.ElapsedTicks >= timeToWait)
                    break;
            }

            timer.Reset();
            timer.Stop();

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
                    LogInstructionExecuting("NOP", data);
                    break;
                case 0x1:
                    LogInstructionExecuting("LFM", data);
                    SetValueOfTheActiveRegister(RAM.ReadUnchecked((ushort)(data - 1)));
                    return 2;
                case 0x2:
                    LogInstructionExecuting("WTM", data);
                    RAM.WriteUnchecked((ushort)(data - 1), GetValueOfTheActiveRegister());
                    return 2;
                case 0x3:
                    LogInstructionExecuting("SRA", data);
                    ChangeActiveRegister(RAM.ReadUnchecked((ushort)(data - 1)));
                    return 2;
                case 0x4:
                    LogInstructionExecuting("AXY", data);
                    SetValueOfTheActiveRegister((ushort)(regX + regY));
                    break;
                case 0x5:
                    LogInstructionExecuting("SXY", data);
                    SetValueOfTheActiveRegister((ushort)(regX - regY));
                    break;
                case 0x6:
                    LogInstructionExecuting("MXY", data);
                    SetValueOfTheActiveRegister((ushort)(regX * regY));
                    break;
                case 0x7:
                    LogInstructionExecuting("DXY", data);
                    if (regX == 0)
                    {
                        SetValueOfTheActiveRegister(0);
                        flgDivZero = 0x1;
                        break;
                    }
                    SetValueOfTheActiveRegister((ushort)(regX / regY));
                    break;
                case 0x8:
                    LogInstructionExecuting("EQU", data);
                    if (regX == regY) flgEqual = 0x1; else flgEqual = 0x0;
                    break;
                case 0x9:
                    LogInstructionExecuting("LEQ", data);
                    if (regX < regY) flgEqual = 0x1; else flgEqual = 0x0;
                    break;
                case 0xA:
                    LogInstructionExecuting("JPZ", data);
                    if (flgEqual == 0x0) regPC = data;
                    return 2;
                case 0xB:
                    LogInstructionExecuting("JNZ", data);
                    if (flgEqual != 0x0) regPC = data;
                    return 2;
                case 0xC:
                    LogInstructionExecuting("JMP", data);
                    regPC = data;
                    return 2;
                case 0xD:
                    LogInstructionExecuting("CLR", data);
                    flgEqual = 0x0;
                    flgActiveReg = 0x0;
                    flgDivZero = 0x0;
                    flgHalt = 0x0;
                    break;
                case 0xE:
                    LogInstructionExecuting("HLT", data);
                    flgHalt = 0x1;
                    break;
                case 0xF:
                    LogInstructionExecuting("BSL", data);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() << 1));
                    break;
                case 0x10:
                    LogInstructionExecuting("BRL", data);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() >> 1));
                    break;
                case 0x11:
                    LogInstructionExecuting("AND", data);
                    var and = (ushort)(GetValueOfTheActiveRegister() & GetValueOfTheInactiveRegister());
                    SetValueOfTheActiveRegister(and);
                    break;
                case 0x12:
                    LogInstructionExecuting("ORA", data);
                    var or = (ushort)(GetValueOfTheActiveRegister() | GetValueOfTheInactiveRegister());
                    SetValueOfTheActiveRegister(or);
                    break;
                case 0x13:
                    LogInstructionExecuting("ORA", data);
                    var xor = (ushort)(GetValueOfTheActiveRegister() ^ GetValueOfTheInactiveRegister());
                    SetValueOfTheActiveRegister(xor);
                    break;
                case 0x14:
                    LogInstructionExecuting("DWR", data);
                    SetValueOfTheActiveRegister(data);
                    return 2;
                // Extension debug instructions
                case 0xC000:
                    LogInstructionExecuting("DBG_LGC", data);
                    char chr = DebugCharacters.GetCharacter(data);
                    Console.Write(chr);
                    return 2;
                case 0xC001:
                    LogInstructionExecuting("DGB_MEM", data);

                    // Print all memory
                    for (ushort i = 0; i < RAM.Size; i++)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write($"${i:X4}");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write(" ");
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"{RAM.ReadUnchecked(i):X4} ");
                    }
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
                case 0xC002:
                    ushort chrIn = 0x28; // 40 - not recognised key
                    char keyIN = (char)Console.ReadKey().Key;
                    DebugCharacters.codes.TryGetValue(keyIN, out chrIn);
                    LogInstructionExecuting("DGB_INP", data);
                    SetValueOfTheActiveRegister(data);
                    break;
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
        private void LogInstructionExecuting(string opcode, ushort data)
        {
            // Skip
            #if SKIP_DEBUG
            return;
            #endif

            Console.Write("Executing: ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write(opcode);
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($" at ${regPC:X4} ({regPC}) with data {data}");
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