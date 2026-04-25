using System.Diagnostics;
using System.Threading;
using Fox16Shared;
using FoxVision.Components;

namespace FoxVision
{
    internal class Processor
    {
        private ushort regX, regY, regPC;
        private byte regStatus;

        private const byte StatusPredicateMask = 1 << 0;
        private const byte StatusLessThanMask = 1 << 1;
        private const byte StatusGreaterThanMask = 1 << 2;
        private const byte StatusNotEqualMask = 1 << 3;
        private const byte StatusActiveRegisterMask = 1 << 4;
        private const byte StatusIllegalDivisionMask = 1 << 5;
        private const byte StatusHaltMask = 1 << 6;

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
            regStatus = 0;

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

            return IsHalted;
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
                        SetIllegalDivision(true);
                        SetValueOfTheActiveRegister(0);
                        break;
                    }
                    SetIllegalDivision(false);
                    SetValueOfTheActiveRegister((ushort)(regX / regY));
                    break;

                case 0x8:
                    LogInstructionExecuting("EQU", first_operand);
                    {
                        bool equal = regX == regY;
                        bool lessThan = regX < regY;
                        bool greaterThan = regX > regY;
                        SetComparisonFlags(equal, lessThan, greaterThan, !equal);
                    }
                    break;

                case 0x9:
                    LogInstructionExecuting("LEQ", first_operand);
                    {
                        bool lessThan = regX < regY;
                        bool greaterThan = regX > regY;
                        bool equal = regX == regY;
                        // Preserve legacy LEQ flow: JPZ/JNZ consume the predicate bit.
                        SetComparisonFlags(lessThan, lessThan, greaterThan, !equal);
                    }
                    break;

                case 0xA:
                    LogInstructionExecuting("JPZ", first_operand);
                    if (!IsPredicateSet)
                        regPC = first_operand;
                    return 2;

                case 0xB:
                    LogInstructionExecuting("JNZ", first_operand);
                    if (IsPredicateSet)
                        regPC = first_operand;
                    return 2;

                case 0xC:
                    LogInstructionExecuting("JMP", first_operand);
                    regPC = first_operand;
                    return 2;

                case 0xD:
                    LogInstructionExecuting("CLR", first_operand);
                    regStatus = 0;
                    break;

                case 0xE:
                    LogInstructionExecuting("HLT", first_operand);
                    SetHaltFlag(true);
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
                            0x8002 => regStatus,
                            _ => 0
                        };

                        if (srcRegister != 0x8000 && srcRegister != 0x8001 && srcRegister != 0x8002)
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

                // CMP - Compare X and Y and update Status bits.
                case 0x1C:
                    LogInstructionExecuting("CMP", first_operand);
                    {
                        bool equal = regX == regY;
                        bool lessThan = regX < regY;
                        bool greaterThan = regX > regY;
                        SetComparisonFlags(equal, lessThan, greaterThan, !equal);
                    }
                    break;

                // JEQ/JNE/JLT/JGT/JLE/JGE - Status-driven conditional jumps.
                case 0x1D:
                    LogInstructionExecuting("JEQ", first_operand);
                    if (IsEqual)
                        regPC = first_operand;
                    return 2;

                case 0x1E:
                    LogInstructionExecuting("JNE", first_operand);
                    if (IsNotEqual)
                        regPC = first_operand;
                    return 2;

                case 0x1F:
                    LogInstructionExecuting("JLT", first_operand);
                    if (IsLessThan)
                        regPC = first_operand;
                    return 2;

                case 0x20:
                    LogInstructionExecuting("JGT", first_operand);
                    if (IsGreaterThan)
                        regPC = first_operand;
                    return 2;

                case 0x21:
                    LogInstructionExecuting("JLE", first_operand);
                    if (IsLessThan || IsEqual)
                        regPC = first_operand;
                    return 2;

                case 0x22:
                    LogInstructionExecuting("JGE", first_operand);
                    if (IsGreaterThan || IsEqual)
                        regPC = first_operand;
                    return 2;

                // V1.6 multi-operand ALU instructions.
                case 0x23:
                    LogInstructionExecuting("ADD", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst + src)))
                        goto NOP;
                    return 3;

                case 0x24:
                    LogInstructionExecuting("SUB", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst - src)))
                        goto NOP;
                    return 3;

                case 0x25:
                    LogInstructionExecuting("MUL", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst * src)))
                        goto NOP;
                    return 3;

                case 0x26:
                    LogInstructionExecuting("DIV", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) =>
                    {
                        if (src == 0)
                        {
                            SetIllegalDivision(true);
                            return 0;
                        }

                        SetIllegalDivision(false);
                        return (ushort)(dst / src);
                    }))
                    {
                        goto NOP;
                    }
                    return 3;

                case 0x27:
                    LogInstructionExecuting("AND", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst & src)))
                        goto NOP;
                    return 3;

                case 0x28:
                    LogInstructionExecuting("OR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst | src)))
                        goto NOP;
                    return 3;

                case 0x29:
                    LogInstructionExecuting("XOR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst ^ src)))
                        goto NOP;
                    return 3;

                case 0x2A:
                    LogInstructionExecuting("SHL", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst << src)))
                        goto NOP;
                    return 3;

                case 0x2B:
                    LogInstructionExecuting("SHR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, second_operand, (src, dst) => (ushort)(dst >> src)))
                        goto NOP;
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
            => SetActiveRegister(regID != 0);

        private ushort GetValueOfTheActiveRegister()
            => IsActiveRegisterY ? regY : regX;

        private ushort GetValueOfTheInactiveRegister()
            => IsActiveRegisterY ? regX : regY;

        private void SetValueOfTheActiveRegister(ushort value)
        {
            if (IsActiveRegisterY)
                regY = value;
            else
                regX = value;
        }

        private bool IsPredicateSet
            => (regStatus & StatusPredicateMask) != 0;

        private bool IsEqual
            => IsPredicateSet;

        private bool IsLessThan
            => (regStatus & StatusLessThanMask) != 0;

        private bool IsGreaterThan
            => (regStatus & StatusGreaterThanMask) != 0;

        private bool IsNotEqual
            => (regStatus & StatusNotEqualMask) != 0;

        private bool IsActiveRegisterY
            => (regStatus & StatusActiveRegisterMask) != 0;

        private bool IsHalted
            => (regStatus & StatusHaltMask) != 0;

        private void SetComparisonFlags(bool predicate, bool lessThan, bool greaterThan, bool notEqual)
        {
            SetStatusFlag(StatusPredicateMask, predicate);
            SetStatusFlag(StatusLessThanMask, lessThan);
            SetStatusFlag(StatusGreaterThanMask, greaterThan);
            SetStatusFlag(StatusNotEqualMask, notEqual);
        }

        private void SetActiveRegister(bool useY)
            => SetStatusFlag(StatusActiveRegisterMask, useY);

        private void SetIllegalDivision(bool illegal)
            => SetStatusFlag(StatusIllegalDivisionMask, illegal);

        private void SetHaltFlag(bool halt)
            => SetStatusFlag(StatusHaltMask, halt);

        private void SetStatusFlag(byte mask, bool enabled)
        {
            if (enabled)
                regStatus |= mask;
            else
                regStatus &= (byte)~mask;
        }

        private ushort ResolveMovSourceValue(ushort src)
        {
            return src switch
            {
                0x8000 => regX,
                0x8001 => regY,
                0x8002 => regStatus,
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

        private bool TryExecuteBinaryMoInstruction(ushort srcOperand, ushort dstOperand, Func<ushort, ushort, ushort> operation)
        {
            ushort srcValue = ResolveMovSourceValue(srcOperand);

            ushort dstValue = dstOperand switch
            {
                0x8000 => regX,
                0x8001 => regY,
                _ => 0
            };

            if (dstOperand != 0x8000 && dstOperand != 0x8001)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Illegal ALU destination register {dstOperand:X4}");
                Console.ResetColor();
                return false;
            }

            ushort result = operation(srcValue, dstValue);
            return TryWriteMovDestination(dstOperand, result);
        }
    }
}