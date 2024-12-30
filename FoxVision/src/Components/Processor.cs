// #define SKIP_DEBUG
#define SKIP_VERBOSE_DEBUGGING

using System.Diagnostics;
using Fox16Shared;

namespace FoxVision.Components
{
    /*
    *   This class is the main processor for the virtual machine.
    *   It does not deal with graphical operations.
    *   Note: This class differs from the technical specification
    *   in regard to the OSR register. Unlike the 8 bit OSR register,
    *   separate unsigned 16 bit variables are used for each flag.
    *   This is done in an attempt to simplify the design of the processor.
    */
    internal class Processor
    {
        // Registers
        private ushort regX, regY, regPC, regHold;
        private ushort flgEqual, flgActiveReg, flgDivZero, flgHalt, flgSource, flgDest;
        private const long clock = 8_000_000;
        private const long ticksInSec = 10_000_000;
        private const long timeToWait = clock / ticksInSec;

        // CPU timer
        private readonly Stopwatch timer;

        private readonly ContiguousMemory RAM;
        private readonly GraphicsUnit graphicsUnit;

        /// <summary>
        ///  Create the CPU
        /// </summary>
        /// <param name="RAM">Reference to the RAM</param>
        internal Processor(ContiguousMemory RAM, GraphicsUnit graphicsUnit)
        {
            // Set RAM reference
            this.RAM = RAM;

            // Set registers
            regX = 0x0;
            regY = 0x0;
            regPC = 0x0;
            flgHalt = 0x0;
            flgSource = 0x0;
            flgDest = 0x0;

            // Set flags
            flgEqual = 0x0;
            flgActiveReg = 0x0;

            // Set graphics unit
            this.graphicsUnit = graphicsUnit;

            // Create a new CPU timer
            timer = new Stopwatch();

            Console.WriteLine("Processor created");
        }


        /// <summary>
        /// Execute CPU cycle
        /// </summary>
        /// <returns>Return weather the CPU has halted in the cycle</returns>
        internal bool ExecuteCycle()
        {
            timer.Start();

            // Read the data from RAM and validate bounds of second ushort
            ushort[] data = [RAM.ReadUnchecked(regPC), 0x0, 0x0]; // Instruction
            if (regPC != RAM.Size)
                data[1] = RAM.ReadUnchecked((ushort)(regPC + 1)); // Data
            if (regPC + 1 != RAM.Size)
                data[2] = RAM.ReadUnchecked((ushort)(regPC + 2)); // Extended data
            

            // Decode and execute the instruction
            ushort oldPC = regPC;
            ushort diffPC = DecodeExecuteInstruction(data[0], data[1], data[2]);

            // Show the processor status (debug)
            LogProcessorStatus();

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
        private ushort DecodeExecuteInstruction(ushort opcode, ushort data, ushort extData)
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
                // V1.1
                case 0x15:
                    LogInstructionExecuting("ILM", data);
                    SetValueOfTheActiveRegister(RAM.ReadUnchecked(GetValueOfTheActiveRegister()));
                    break;
                case 0x16:
                    LogInstructionExecuting("IWR", data);
                    RAM.WriteUnchecked(GetValueOfTheInactiveRegister(), GetValueOfTheActiveRegister());
                    break;
                // V1.2
                case 0x17:
                    LogInstructionExecuting("INC", data);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() + 1));
                    break;
                case 0x18:
                    LogInstructionExecuting("DEC", data);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() - 1));
                    break;
                // V1.4
                case 0x19:
                    LogInstructionExecuting("SDM", data);
                    if (flgDest == 0x0)
                        flgDest = (ushort)SourceDest.Memory;
                    else if (flgDest == 0x1)
                        flgDest = (ushort)SourceDest.Register;
                    else
                        #if DEBUG
                        throw new InvalidOperationException("Invalid destination source");
                        #else
                        Console.WriteLine("Invalid destination source");
                        #endif
                    return 2;
                case 0x1A:
                    LogInstructionExecuting("SSM", data);
                    if (flgSource == 0x0)
                        flgSource = (ushort)SourceDest.Memory;
                    else if (flgSource == 0x1)
                        flgSource = (ushort)SourceDest.Register;
                    else if (flgSource == 0x2)
                        flgSource = (ushort)SourceDest.Immediate;
                    else
                        #if DEBUG
                        throw new InvalidOperationException("Invalid source source");
                        #else
                        Console.WriteLine("Invalid source source");
                        #endif
                    return 2;
                    case 0x1B:
                    LogInstructionExecuting("MOV", data, extData, true);

                    // Load the value and move to the hold register
                    switch (flgSource)
                    {
                        case (ushort)SourceDest.Immediate:
                            regHold = data;
                            break;
                        case (ushort)SourceDest.Register:
                            if (extData == 0x0)
                                regHold = regX;
                            else
                                regHold = regY;
                            break;
                        case (ushort)SourceDest.Memory:
                            regHold = RAM.ReadUnchecked((ushort)(data - 1));
                            break;
                    }

                    // Write the value to the destination
                    switch (flgDest)
                    {
                        case (ushort)SourceDest.Register:
                            if (extData == 0x0)
                                regX = regHold;
                            else
                                regY = regHold;
                            break;
                        case (ushort)SourceDest.Memory:
                            RAM.WriteUnchecked((ushort)(data - 1), regHold);
                            break;
                    }

                    return 3;
                // V1.3
                case 0x8000:
                    LogInstructionExecuting("GSWP", data);
                    // Lock the thread down
                    if (graphicsUnit != null)
                        lock (graphicsUnit) graphicsUnit.SwapBuffer(RAM);
                    else
                        throw new InvalidOperationException("graphicsUnit not created.");
                    break;
                case 0x8001:
                    LogInstructionExecuting("GCLR", data);
                    // Lock the thread down
                    if (graphicsUnit != null)
                        lock (graphicsUnit) graphicsUnit.Clear();
                    else
                        throw new InvalidOperationException("graphicsUnit not created.");
                    break;
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
        /// Log the processor status - the values of registers and flags
        /// </summary>
        private void LogProcessorStatus()
        {
            #if !SKIP_DEBUGGING && !SKIP_VERBOSE_DEBUGGING
            Console.WriteLine($"Processor status: A {regX:X4} ({regX}), B {regY:X4} ({regY}), PC {regPC:X4} ({regPC}), Flags: E {flgEqual}, DZ {flgDivZero}, H {flgHalt}");
            #endif
        }

        /// <summary>
        /// Debug log the opcode currently being executed
        /// </summary>
        /// <param name="opcode">String of the opcode</param>
        private void LogInstructionExecuting(string opcode, ushort data, ushort extData = 0x0, bool extended = false)
        {
            // Skip
#if SKIP_DEBUG
            return;
#endif

            Console.Write("Executing: ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write(opcode);
            Console.ForegroundColor = ConsoleColor.White;
            if (extended)
                Console.WriteLine($" at ${regPC:X4} ({regPC}) with data {data} and extended data {extData}");
            else
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