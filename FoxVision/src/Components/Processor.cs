using System.Diagnostics;
using System.Threading;
using Fox16Shared;
using FoxVision.Components;

namespace FoxVision
{
    internal class Processor
    {
        private ushort regX, regY, regSP, regPC, regCYC;
        private ulong _totalCycleCount;
        private byte regStatus;
        private bool _waitActive;
        private ushort _waitStartCycle;
        private ushort _waitDuration;
        private bool _vblankWaitActive;
        private int _vblankSequence;
        private int _vblankWaitSequence;

        private const byte StatusPredicateMask = 1 << 0;
        private const byte StatusLessThanMask = 1 << 1;
        private const byte StatusGreaterThanMask = 1 << 2;
        private const byte StatusNotEqualMask = 1 << 3;
        private const byte StatusActiveRegisterMask = 1 << 4;
        private const byte StatusIllegalDivisionMask = 1 << 5;
        private const byte StatusHaltMask = 1 << 6;

        private const byte OperandTypeRegister = 0b00;
        private const byte OperandTypeImmediate = 0b01;
        private const byte OperandTypeDirectMemory = 0b10;
        private const byte OperandTypeIndirectMemory = 0b11;

        private const ushort RegisterIdX = 0x0000;
        private const ushort RegisterIdY = 0x0001;
        private const ushort RegisterIdStatus = 0x0003;
        private const ushort RegisterIdSP = 0x0004;
        private const ushort RegisterIdCYC = 0x0005;
        private const ushort VramSizeInWords = 5000;
        private const ushort StackStartAddress = (ushort)(ushort.MaxValue - VramSizeInWords);

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
            regSP = StackStartAddress;
            regPC = 0;
            regCYC = 0;
            _totalCycleCount = 0;
            regStatus = 0;
            _waitActive = false;
            _waitStartCycle = 0;
            _waitDuration = 0;
            _vblankWaitActive = false;
            _vblankSequence = 0;
            _vblankWaitSequence = 0;

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

            if (_waitActive)
            {
                ushort elapsed = unchecked((ushort)(regCYC - _waitStartCycle));
                if (elapsed >= _waitDuration)
                    _waitActive = false;
            }

            if (_vblankWaitActive)
            {
                int currentSequence = Interlocked.CompareExchange(ref _vblankSequence, 0, 0);
                if (currentSequence != _vblankWaitSequence)
                    _vblankWaitActive = false;
            }

            if (!_waitActive && !_vblankWaitActive)
            {
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
            }

            long ticksToWait = Interlocked.Read(ref _ticksToWaitPerCycle);
            while (timer.ElapsedTicks < ticksToWait) { }

            timer.Reset();
            timer.Stop();

            unchecked { regCYC++; }
            _totalCycleCount++;

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

        internal void SignalVBlank()
            => Interlocked.Increment(ref _vblankSequence);

        internal ulong GetCycleCount()
            => _totalCycleCount;

        internal void Reset()
        {
            regX = 0;
            regY = 0;
            regSP = StackStartAddress;
            regPC = 0;
            regCYC = 0;
            _totalCycleCount = 0;
            regStatus = 0;
            _waitActive = false;
            _waitStartCycle = 0;
            _waitDuration = 0;
            _vblankWaitActive = false;
            _vblankSequence = 0;
            _vblankWaitSequence = 0;
        }

        private ushort DecodeExecuteInstruction(ushort opcode, ushort first_operand, ushort second_operand)
        {
            ushort decodedOpcode = DecodeOpcodeId(opcode);
            byte firstOperandType = DecodeOperandType(opcode, operandIndex: 0);
            byte secondOperandType = DecodeOperandType(opcode, operandIndex: 1);

            switch (decodedOpcode)
            {
                case 0x0:
                NOP:
                    LogInstructionExecuting("NOP", first_operand);
                    break;

                case 0x1:
                    LogInstructionExecuting("LFM", first_operand);
                    SetValueOfTheActiveRegister(RAM.ReadUnchecked(first_operand));
                    return 2;

                case 0x2:
                    LogInstructionExecuting("WTM", first_operand);
                    RAM.WriteUnchecked(first_operand, GetValueOfTheActiveRegister());
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
                    LogInstructionExecuting("BSR", first_operand);
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
                        ushort src = ResolveOperandValue(first_operand, firstOperandType);
                        ushort dst = second_operand;

                        if (!TryWriteRegisterOperand(dst, secondOperandType, src))
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
                        ushort dstAddressOperand = second_operand;

                        if (!TryReadRegisterOperand(srcRegister, firstOperandType, out ushort value, allowStatus: true))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal STR source register {srcRegister:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        if (!TryResolveMemoryAddressOperand(dstAddressOperand, secondOperandType, out ushort dstAddress))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal STR destination address operand {dstAddressOperand:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        RAM.WriteUnchecked(dstAddress, value);
                    }
                    return 3;

                // LOD - Load
                case 0x1B:
                    LogInstructionExecuting("LOD", first_operand);
                    {
                        ushort dstRegister = first_operand;
                        ushort srcAddressOperand = second_operand;

                        if (!TryResolveMemoryAddressOperand(srcAddressOperand, secondOperandType, out ushort srcAddress))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal LOD source address operand {srcAddressOperand:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        ushort value = RAM.ReadUnchecked(srcAddress);

                        if (!TryWriteRegisterOperand(dstRegister, firstOperandType, value))
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
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst + src)))
                        goto NOP;
                    return 3;

                case 0x24:
                    LogInstructionExecuting("SUB", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst - src)))
                        goto NOP;
                    return 3;

                case 0x25:
                    LogInstructionExecuting("MUL", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst * src)))
                        goto NOP;
                    return 3;

                case 0x26:
                    LogInstructionExecuting("DIV", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) =>
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
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst & src)))
                        goto NOP;
                    return 3;

                case 0x28:
                    LogInstructionExecuting("OR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst | src)))
                        goto NOP;
                    return 3;

                case 0x29:
                    LogInstructionExecuting("XOR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst ^ src)))
                        goto NOP;
                    return 3;

                case 0x2A:
                    LogInstructionExecuting("SHL", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst << src)))
                        goto NOP;
                    return 3;

                case 0x2B:
                    LogInstructionExecuting("SHR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => (ushort)(dst >> src)))
                        goto NOP;
                    return 3;

                // V1.7 stack instructions using an explicit register operand.
                case 0x2C:
                    LogInstructionExecuting("PUSH", first_operand);
                    if (!TryReadRegisterOperand(first_operand, firstOperandType, out ushort pushValue, allowStatus: true))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal PUSH source register {first_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    RAM.WriteUnchecked(regSP, pushValue);
                    unchecked { regSP--; }
                    return 2;

                case 0x2D:
                    LogInstructionExecuting("POP", first_operand);
                    if (!IsRegisterOperand(firstOperandType, first_operand))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal POP destination register {first_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    unchecked { regSP++; }
                    if (!TryWriteRegisterOperand(first_operand, firstOperandType, RAM.ReadUnchecked(regSP), allowStatus: true))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal POP destination register {first_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    return 2;

                // V1.8 timing instruction.
                case 0x2E:
                    LogInstructionExecuting("WAIT", first_operand);
                    {
                        ushort delay = ResolveOperandValue(first_operand, firstOperandType);
                        _waitStartCycle = regCYC;
                        _waitDuration = delay;
                        _waitActive = true;
                    }
                    return 2;

                case 0x2F:
                    LogInstructionExecuting("VBLANK", first_operand);
                    _vblankWaitSequence = Interlocked.CompareExchange(ref _vblankSequence, 0, 0);
                    _vblankWaitActive = true;
                    return 1;

                case 0xC000:
                    LogInstructionExecuting("DBG_LGC", first_operand);
                    Console.Write(DebugCharacters.GetCharacter(first_operand));
                    return 2;

                case 0xC001:
                    LogInstructionExecuting("DBG_MEM", first_operand);
                    DumpMemoryHex();
                    break;

                case 0xC002:
                    LogInstructionExecuting("DBG_INP", first_operand);
                    ushort chrIn = 0x28;
                    char key = Console.ReadKey().KeyChar;
                    DebugCharacters.codes.TryGetValue(key, out chrIn);
                    SetValueOfTheActiveRegister(chrIn);
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Illegal opcode {opcode:X4} (decoded {decodedOpcode:X4})");
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

        [Obsolete]
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

        private ushort DecodeOpcodeId(ushort opcode)
        {
            // Debug extension opcodes keep their full encoded value.
            if (opcode >= Opcodes.DEBUG_EXTENSION_OFFSET)
                return opcode;

            // Legacy/base opcode format still appears in older ROMs.
            if (opcode <= 0x00FF)
                return opcode;

            // New format packs opcode id in the high byte.
            return (ushort)(opcode >> 8);
        }

        private byte DecodeOperandType(ushort opcode, int operandIndex)
        {
            // Only new-format base opcodes encode operand type metadata.
            if (opcode <= 0x00FF || opcode >= Opcodes.DEBUG_EXTENSION_OFFSET)
                return OperandTypeRegister;

            return operandIndex switch
            {
                0 => (byte)((opcode >> 2) & 0b11),
                1 => (byte)((opcode >> 4) & 0b11),
                _ => OperandTypeImmediate
            };
        }

        private bool TryReadRegisterOperand(ushort operand, byte operandType, out ushort value, bool allowStatus = false)
        {
            value = 0;
            if (!IsRegisterOperand(operandType, operand))
                return false;

            return operand switch
            {
                RegisterIdX => SetOutValue(regX, out value),
                RegisterIdY => SetOutValue(regY, out value),
                RegisterIdStatus when allowStatus => SetOutValue(regStatus, out value),
                RegisterIdSP => SetOutValue(regSP, out value),
                RegisterIdCYC => SetOutValue(regCYC, out value),
                _ => false
            };
        }

        private bool TryWriteRegisterOperand(ushort operand, byte operandType, ushort value, bool allowStatus = false)
        {
            if (!IsRegisterOperand(operandType, operand))
                return false;

            if (operand == RegisterIdX)
            {
                regX = value;
                return true;
            }

            if (operand == RegisterIdY)
            {
                regY = value;
                return true;
            }

            if (operand == RegisterIdStatus && allowStatus)
            {
                regStatus = (byte)value;
                return true;
            }

            if (operand == RegisterIdSP)
            {
                regSP = value;
                return true;
            }

            return false;
        }

        private ushort ResolveOperandValue(ushort operand, byte operandType)
        {
            if (IsRegisterOperand(operandType, operand))
                return TryReadRegisterOperand(operand, operandType, out ushort registerValue, allowStatus: true) ? registerValue : (ushort)0;

            if (operandType == OperandTypeIndirectMemory)
            {
                if (!TryReadRegisterOperand(operand, operandType, out ushort indirectAddress, allowStatus: false))
                    return 0;
                return RAM.ReadUnchecked(indirectAddress);
            }

            if (operandType == OperandTypeDirectMemory)
                return RAM.ReadUnchecked(operand);

            return operand;
        }

        private bool TryExecuteBinaryMoInstruction(ushort srcOperand, byte srcOperandType, ushort dstOperand, byte dstOperandType, Func<ushort, ushort, ushort> operation)
        {
            ushort srcValue = ResolveOperandValue(srcOperand, srcOperandType);

            if (!TryReadRegisterOperand(dstOperand, dstOperandType, out ushort dstValue, allowStatus: false))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Illegal ALU destination register {dstOperand:X4}");
                Console.ResetColor();
                return false;
            }

            ushort result = operation(srcValue, dstValue);
            return TryWriteRegisterOperand(dstOperand, dstOperandType, result);
        }

        private bool TryResolveMemoryAddressOperand(ushort operand, byte operandType, out ushort address)
        {
            if (operandType == OperandTypeIndirectMemory || (operandType == OperandTypeRegister && (operand == RegisterIdX || operand == RegisterIdY)))
            {
                return TryReadRegisterOperand(operand, OperandTypeRegister, out address, allowStatus: false);
            }

            if (operandType == OperandTypeDirectMemory || operandType == OperandTypeImmediate)
            {
                address = operand;
                return true;
            }

            address = 0;
            return false;
        }

        private static bool IsRegisterOperand(byte operandType, ushort operand)
        {
            if (operandType != OperandTypeRegister && operandType != OperandTypeIndirectMemory)
                return false;

            return operand == RegisterIdX || operand == RegisterIdY || operand == RegisterIdStatus || operand == RegisterIdSP || operand == RegisterIdCYC;
        }

        private static bool SetOutValue(ushort source, out ushort destination)
        {
            destination = source;
            return true;
        }

        private void DumpMemoryHex()
        {
            const int wordsPerLine = 16;
            for (int i = 0; i <= RAM.MaxAddress; i += wordsPerLine)
            {
                Console.Write($"{i:X4}: ");
                for (int j = 0; j < wordsPerLine && i + j <= RAM.MaxAddress; j++)
                {
                    Console.Write($"{RAM.ReadUnchecked((ushort)(i + j)):X4} ");
                }
                Console.WriteLine();
            }
        }
    }
}