using System.Diagnostics;
using System.Threading;
using Fox16Shared;
using FoxVision.Components;

namespace FoxVision
{
    internal class Processor
    {
        private ushort regX, regY, regPC;
        private ushort flgEqual, flgActiveReg, flgHalt;

        private readonly Stopwatch timer;
        private readonly ContiguousMemory RAM;
        private long _ticksToWaitPerCycle;
        private int _logInstructionEnabled;
        private int _pauseRequested;

        internal Processor(ContiguousMemory RAM, int executionSpeedHz, bool logInstruction)
        {
            this.RAM = RAM;

            regX = 0;
            regY = 0;
            regPC = 0;

            flgEqual = 0;
            flgActiveReg = 0;
            flgHalt = 0;

            timer = new Stopwatch();
            SetExecutionSpeedHz(executionSpeedHz);
            SetInstructionLogging(logInstruction);
            _pauseRequested = 0;

            Console.WriteLine("Processor created");
        }

        internal bool ExecuteCycle()
        {
            if (IsPaused)
            {
                Thread.Yield();
                return false;
            }

            timer.Start();

            ushort[] data = [RAM.ReadUnchecked(regPC), 0, 0];
            if (regPC != RAM.MaxAddress)
                data[1] = RAM.ReadUnchecked((ushort)(regPC + 1));
            if (regPC + 1 != RAM.MaxAddress)
                data[2] = RAM.ReadUnchecked((ushort)(regPC + 2));

            ushort oldPC = regPC;
            ushort diffPC = DecodeExecuteInstruction(data[0], data[1], data[2]);

            if (regPC == oldPC)
                regPC += diffPC;

            if (regPC > RAM.MaxAddress)
                regPC = 0;

            long ticksToWait = Interlocked.Read(ref _ticksToWaitPerCycle);
            while (timer.ElapsedTicks < ticksToWait) { }

            timer.Reset();
            timer.Stop();

            return flgHalt != 0;
        }

        internal bool IsPaused
            => Interlocked.CompareExchange(ref _pauseRequested, 0, 0) == 1;

        internal void SetPaused(bool paused)
            => Interlocked.Exchange(ref _pauseRequested, paused ? 1 : 0);

        internal void SetExecutionSpeedHz(int executionSpeedHz)
        {
            if (executionSpeedHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(executionSpeedHz), "Execution speed must be greater than zero.");

            long ticksToWait = Math.Max(1, Stopwatch.Frequency / executionSpeedHz);
            Interlocked.Exchange(ref _ticksToWaitPerCycle, ticksToWait);
        }

        internal void SetInstructionLogging(bool enabled)
            => Interlocked.Exchange(ref _logInstructionEnabled, enabled ? 1 : 0);

        private ushort DecodeExecuteInstruction(ushort opcode, ushort first_operand, ushort second_operand)
        {
            switch (opcode)
            {
                case 0x0:
                NOP:
                    LogInstructionExecuting("NOP", first_operand);
                    break;

                case 0x1:
                    LogInstructionExecuting("LFM", first_operand);
                    SetValueOfTheActiveRegister(RAM.ReadUnchecked((ushort)(first_operand - 1)));
                    return 2;

                case 0x2:
                    LogInstructionExecuting("WTM", first_operand);
                    RAM.WriteUnchecked((ushort)(first_operand - 1), GetValueOfTheActiveRegister());
                    return 2;

                case 0x3:
                    LogInstructionExecuting("SRA", first_operand);
                    ChangeActiveRegister(first_operand);
                    return 2;

                case 0x4:
                    LogInstructionExecuting("AXY", first_operand);
                    SetValueOfTheActiveRegister((ushort)(regX + regY));
                    break;

                case 0x5:
                    LogInstructionExecuting("SXY", first_operand);
                    SetValueOfTheActiveRegister((ushort)(regX - regY));
                    break;

                case 0x6:
                    LogInstructionExecuting("MXY", first_operand);
                    SetValueOfTheActiveRegister((ushort)(regX * regY));
                    break;

                case 0x7:
                    LogInstructionExecuting("DXY", first_operand);
                    if (regY == 0)
                    {
                        SetValueOfTheActiveRegister(0);
                        break;
                    }
                    SetValueOfTheActiveRegister((ushort)(regX / regY));
                    break;

                case 0x8:
                    LogInstructionExecuting("EQU", first_operand);
                    flgEqual = (ushort)(regX == regY ? 1 : 0);
                    break;

                case 0x9:
                    LogInstructionExecuting("LEQ", first_operand);
                    flgEqual = (ushort)(regX < regY ? 1 : 0);
                    break;

                case 0xA:
                    LogInstructionExecuting("JPZ", first_operand);
                    if (flgEqual == 0)
                        regPC = first_operand;
                    return 2;

                case 0xB:
                    LogInstructionExecuting("JNZ", first_operand);
                    if (flgEqual != 0)
                        regPC = first_operand;
                    return 2;

                case 0xC:
                    LogInstructionExecuting("JMP", first_operand);
                    regPC = first_operand;
                    return 2;

                case 0xD:
                    LogInstructionExecuting("CLR", first_operand);
                    flgEqual = 0;
                    flgActiveReg = 0;
                    flgHalt = 0;
                    break;

                case 0xE:
                    LogInstructionExecuting("HLT", first_operand);
                    flgHalt = 1;
                    break;

                case 0xF:
                    LogInstructionExecuting("BSL", first_operand);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() << 1));
                    break;

                case 0x10:
                    LogInstructionExecuting("BRL", first_operand);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() >> 1));
                    break;

                case 0x11:
                    LogInstructionExecuting("AND", first_operand);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() & GetValueOfTheInactiveRegister()));
                    break;

                case 0x12:
                    LogInstructionExecuting("ORA", first_operand);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() | GetValueOfTheInactiveRegister()));
                    break;

                case 0x13:
                    LogInstructionExecuting("XOR", first_operand);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() ^ GetValueOfTheInactiveRegister()));
                    break;

                case 0x14:
                    LogInstructionExecuting("DWR", first_operand);
                    SetValueOfTheActiveRegister(first_operand);
                    return 2;

                case 0x15:
                    LogInstructionExecuting("ILM", first_operand);
                    SetValueOfTheActiveRegister(RAM.ReadUnchecked(GetValueOfTheActiveRegister()));
                    break;

                case 0x16:
                    LogInstructionExecuting("IWR", first_operand);
                    RAM.WriteUnchecked(GetValueOfTheInactiveRegister(), GetValueOfTheActiveRegister());
                    break;

                case 0x17:
                    LogInstructionExecuting("INC", first_operand);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() + 1));
                    break;

                case 0x18:
                    LogInstructionExecuting("DEC", first_operand);
                    SetValueOfTheActiveRegister((ushort)(GetValueOfTheActiveRegister() - 1));
                    break;
                // MOV - Move
                case 0x19:
                    LogInstructionExecuting("MOV", first_operand);
                    {
                        ushort src = first_operand;
                        ushort dst = second_operand;
                        ushort value = ResolveMovSourceValue(src);

                        if (!TryWriteMovDestination(dst, value))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal MOV destination {dst:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }
                    }
                    return 3;
                // STR - Store
                case 0x1A:
                    LogInstructionExecuting("STR", first_operand);
                    {
                        ushort srcRegister = first_operand;
                        ushort dstAddress = second_operand;

                        ushort value = srcRegister switch
                        {
                            0x8000 => regX,
                            0x8001 => regY,
                            _ => 0
                        };

                        if (srcRegister != 0x8000 && srcRegister != 0x8001)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal STR source register {srcRegister:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        RAM.WriteUnchecked((ushort)(dstAddress - 1), value);
                    }
                    return 3;

                // LOD - Load
                case 0x1B:
                    LogInstructionExecuting("LOD", first_operand);
                    {
                        ushort dstRegister = first_operand;
                        ushort srcAddress = second_operand;
                        ushort value = RAM.ReadUnchecked((ushort)(srcAddress - 1));

                        if (!TryWriteMovDestination(dstRegister, value))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal LOD destination register {dstRegister:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }
                    }
                    return 3;

                case 0xC000:
                    LogInstructionExecuting("DBG_LGC", first_operand);
                    Console.Write(DebugCharacters.GetCharacter(first_operand));
                    return 2;

                case 0xC002:
                    LogInstructionExecuting("DBG_INP", first_operand);
                    ushort chrIn = 0x28;
                    char key = Console.ReadKey().KeyChar;
                    DebugCharacters.codes.TryGetValue(key, out chrIn);
                    SetValueOfTheActiveRegister(chrIn);
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Illegal opcode {opcode:X4}");
                    Console.ResetColor();
                    goto NOP;
            }

            return 1;
        }

        private void LogInstructionExecuting(string opcode, ushort data)
        {
            if (Interlocked.CompareExchange(ref _logInstructionEnabled, 0, 0) == 1)
                Console.WriteLine($"{opcode} @ {regPC:X4} ({data})");
        }

        private void ChangeActiveRegister(ushort regID)
            => flgActiveReg = (ushort)(regID == 0 ? 0 : 1);

        private ushort GetValueOfTheActiveRegister()
            => flgActiveReg == 0 ? regX : regY;

        private ushort GetValueOfTheInactiveRegister()
            => flgActiveReg == 0 ? regY : regX;

        private void SetValueOfTheActiveRegister(ushort value)
        {
            if (flgActiveReg == 0)
                regX = value;
            else
                regY = value;
        }

        private ushort ResolveMovSourceValue(ushort src)
        {
            return src switch
            {
                0x8000 => regX,
                0x8001 => regY,
                _ => src
            };
        }

        private bool TryWriteMovDestination(ushort dst, ushort value)
        {
            if (dst == 0x8000)
            {
                regX = value;
                return true;
            }

            if (dst == 0x8001)
            {
                regY = value;
                return true;
            }

            return false;
        }
    }
}