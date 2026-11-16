using System.Diagnostics;
using System.Threading;
using Fox16Shared;
using FoxVision.Components;

namespace FoxVision
{
    internal class Processor
    {
        private uint regX, regY, regSP, regPC, regCYC;
        private readonly uint _resetPC;
        private readonly ushort _mapper;
        private uint[] regR;
        private ushort regEM;
        private ushort regAM;
        private ulong _totalCycleCount;
        private byte regStatus;
        private bool _waitActive;
        private ushort _waitStartCycle;
        private ushort _waitDuration;
        private bool _vblankWaitActive;
        private int _vblankSequence;
        private int _vblankWaitSequence;
        private byte _controllerState;
        private ushort _keyboardReport;
        private readonly object _keyboardLock = new();
        private byte _mouseButtons;
        private sbyte _mouseWheel;
        private sbyte _mouseDX;
        private sbyte _mouseDY;
        private readonly object _mouseLock = new();
        private readonly Queue<ushort> _ttyInputBuffer = new();
        private readonly object _ttyLock = new();

        // VF16Flash storage (32K words)
        private readonly ushort[] _flashStorage = new ushort[0x8000];
        private readonly ushort[] _flashPendingCommand = new ushort[PortCount]; // Track command word for each port
        private readonly bool[] _flashCommandReady = new bool[PortCount];       // Track if command is actually pending
        private readonly object _flashLock = new();

        // Flash save paths and background save management
        private readonly string[] _flashSavePaths;
        private readonly Task[] _flashSaveTasks = new Task[PortCount];
        private readonly object[] _flashSaveTaskLocks = new object[PortCount];
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
        private const ushort RegisterIdR0 = 0x0007;
        private const ushort RegisterIdR1 = 0x0008;
        private const ushort RegisterIdR2 = 0x0009;
        private const ushort RegisterIdR3 = 0x000A;
        private const ushort RegisterIdR4 = 0x000B;
        private const ushort RegisterIdR5 = 0x000C;
        private const ushort RegisterIdR6 = 0x000D;
        private const ushort RegisterIdR7 = 0x000E;
        private const ushort RegisterIdPC = 0x0002;
        private const ushort RegisterIdStatus = 0x0003;
        private const ushort RegisterIdSP = 0x0004;
        private const ushort RegisterIdCYC = 0x0005;
        private const ushort RegisterIdEM = 0x0006;
        private const ushort RegisterIdAM = 0x000F;

        private const ushort RegisterIdXL = 0x0010;
        private const ushort RegisterIdXH = 0x0011;
        private const ushort RegisterIdYL = 0x0012;
        private const ushort RegisterIdYH = 0x0013;
        private const ushort RegisterIdPCL = 0x0014;
        private const ushort RegisterIdPCH = 0x0015;
        private const ushort RegisterIdSPL = 0x0016;
        private const ushort RegisterIdSPH = 0x0017;
        private const ushort RegisterIdCYCL = 0x0018;
        private const ushort RegisterIdCYCH = 0x0019;

        private const ushort RegisterIdR0L = 0x0020;
        private const ushort RegisterIdR0H = 0x0021;
        private const ushort RegisterIdR1L = 0x0022;
        private const ushort RegisterIdR1H = 0x0023;
        private const ushort RegisterIdR2L = 0x0024;
        private const ushort RegisterIdR2H = 0x0025;
        private const ushort RegisterIdR3L = 0x0026;
        private const ushort RegisterIdR3H = 0x0027;
        private const ushort RegisterIdR4L = 0x0028;
        private const ushort RegisterIdR4H = 0x0029;
        private const ushort RegisterIdR5L = 0x002A;
        private const ushort RegisterIdR5H = 0x002B;
        private const ushort RegisterIdR6L = 0x002C;
        private const ushort RegisterIdR6H = 0x002D;
        private const ushort RegisterIdR7L = 0x002E;
        private const ushort RegisterIdR7H = 0x002F;
        private const int PortCount = 8;
        private const ushort VramSizeInWords = 5000;
        private const ushort StackStartAddress = (ushort)(ushort.MaxValue - VramSizeInWords);

        private readonly Stopwatch timer;
        private readonly ContiguousMemory RAM;
        private PortDeviceKind[] _portDevices;
        private long _ticksToWaitPerCycle;
        private int _logInstructionEnabled;
        private int _pauseRequested;

        internal Processor(ContiguousMemory RAM, int executionSpeedHz, bool logInstruction)
            : this(RAM, executionSpeedHz, logInstruction, 0, null, 0)
        {
        }


        internal Processor(ContiguousMemory RAM, int executionSpeedHz, bool logInstruction, uint initialPC, PortDeviceKind[]? portDevices = null, ushort mapper = 0)
        {
            this.RAM = RAM;
            _resetPC = initialPC;
            _mapper = mapper;

            regX = 0;
            regY = 0;
            regR = new uint[8];
            regSP = StackStartAddress;
            regPC = initialPC;
            regCYC = 0;
            if (_mapper == 2)
            {
                regEM = 2;
                regAM = 1;
            }
            else
            {
                regEM = 0;
                regAM = 0;
            }
            _totalCycleCount = 0;
            regStatus = 0;
            _waitActive = false;
            _waitStartCycle = 0;
            _waitDuration = 0;
            _vblankWaitActive = false;
            _vblankSequence = 0;
            _vblankWaitSequence = 0;
            _controllerState = 0;
            _portDevices = ClonePortDevices(portDevices);
            // Initialize flash save task locks
            for (int i = 0; i < PortCount; i++)
            {
                _flashSaveTaskLocks[i] = new object();
            }

            // Set flash save paths and load from disk
            _flashSavePaths = new string[PortCount];
            LoadFlashFromDisk();


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
                ushort elapsed = unchecked((ushort)((regCYC & 0xFFFF) - _waitStartCycle));
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
                ushort[] data = new ushort[] { RAM.ReadWord(regPC), RAM.ReadWord(regPC + 1), RAM.ReadWord(regPC + 2) };

                uint oldPC = regPC;
                ushort diffPC = DecodeExecuteInstruction(data[0], data[1], data[2]);

                if (regPC == oldPC)
                    regPC += diffPC;

                if (!Is32BitMemoryEnabled && regPC > RAM.MaxAddress)
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

        internal void SetPortConfiguration(PortDeviceKind[] portDevices)
            => Interlocked.Exchange(ref _portDevices, ClonePortDevices(portDevices));

        internal void SignalVBlank()
            => Interlocked.Increment(ref _vblankSequence);

        internal ulong GetCycleCount()
            => _totalCycleCount;

        internal void Reset()
        {
            regX = 0;
            regY = 0;
            regSP = StackStartAddress;
            regPC = _resetPC;
            regCYC = 0;
            if (_mapper == 2)
            {
                regEM = 2;
                regAM = 1;
            }
            else
            {
                regEM = 0;
                regAM = 0;
            }
            if (regR is not null)
                Array.Clear(regR, 0, regR.Length);
            _totalCycleCount = 0;
            regStatus = 0;
            _waitActive = false;
            _waitStartCycle = 0;
            _waitDuration = 0;
            _vblankWaitActive = false;
            _vblankSequence = 0;
            _vblankWaitSequence = 0;
            _controllerState = 0;
            lock (_ttyLock)
            {
                _ttyInputBuffer.Clear();
            }
            lock (_flashLock)
            {
                Array.Clear(_flashPendingCommand, 0, _flashPendingCommand.Length);
                Array.Clear(_flashCommandReady, 0, _flashCommandReady.Length);
            }
        }

        internal void LatchControllerButton(byte buttonMask)
        {
            _controllerState = (byte)(_controllerState | buttonMask);
        }

        internal void SetKeyboardReport(ushort report)
        {
            lock (_keyboardLock)
            {
                _keyboardReport = report;
            }
        }

        internal void SetMouseState(byte buttons, sbyte wheel, sbyte dx, sbyte dy)
        {
            lock (_mouseLock)
            {
                _mouseButtons = buttons;
                _mouseWheel = wheel;
                _mouseDX = dx;
                _mouseDY = dy;
            }
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
                    SetValueOfTheActiveRegister(ReadMemory(first_operand));
                    return 2;

                case 0x2:
                    LogInstructionExecuting("WTM", first_operand);
                    WriteMemory(first_operand, GetValueOfTheActiveRegister());
                    return 2;

                case 0x3:
                    LogInstructionExecuting("SRA", first_operand);
                    if (IsExtendedModeEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("SRA is unavailable in extension mode.");
                        Console.ResetColor();
                        goto NOP;
                    }
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
                    if ((regY & 0xFFFF) == 0)
                    {
                        SetIllegalDivision(true);
                        SetValueOfTheActiveRegister(0);
                        break;
                    }
                    SetIllegalDivision(false);
                    SetValueOfTheActiveRegister((ushort)((regX & 0xFFFF) / (regY & 0xFFFF)));
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
                        bool lessThan = (regX & 0xFFFF) < (regY & 0xFFFF);
                        bool greaterThan = (regX & 0xFFFF) > (regY & 0xFFFF);
                        bool equal = (regX & 0xFFFF) == (regY & 0xFFFF);
                        // Preserve legacy LEQ flow: JPZ/JNZ consume the predicate bit.
                        SetComparisonFlags(lessThan, lessThan, greaterThan, !equal);
                    }
                    break;

                case 0xA:
                    LogInstructionExecuting("JPZ", first_operand);
                    if (!IsPredicateSet)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                case 0xB:
                    LogInstructionExecuting("JNZ", first_operand);
                    if (IsPredicateSet)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                case 0xC:
                    LogInstructionExecuting("JMP", first_operand);
                    regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint jmpTarget) ? jmpTarget : first_operand;
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
                    SetValueOfTheActiveRegister(ReadMemory(GetValueOfTheActiveRegister()));
                    break;

                case 0x16:
                    LogInstructionExecuting("IWR", first_operand);
                    WriteMemory(GetValueOfTheInactiveRegister(), GetValueOfTheActiveRegister());
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
                        ushort dst = second_operand;

                        // A 32-bit move is used when both sides are primary
                        // registers. Moves involving an immediate or an H/L
                        // sub-register retain their 16-bit width and zero-extend
                        // when the destination is a primary register.
                        if (Is32BitMemoryEnabled
                            && firstOperandType == OperandTypeRegister
                            && secondOperandType == OperandTypeRegister
                            && IsPrimary32BitRegister(first_operand)
                            && IsPrimary32BitRegister(dst)
                            && TryRead32BitRegister(first_operand, out uint src32))
                        {
                            if (!TryWrite32BitRegister(dst, src32))
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Illegal MOV destination {dst:X4}");
                                Console.ResetColor();
                                goto NOP;
                            }
                        }
                        else
                        {
                            ushort src = ResolveOperandValue(first_operand, firstOperandType);

                            if (!TryWriteRegisterOperand(dst, secondOperandType, src))
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Illegal MOV destination {dst:X4}");
                                Console.ResetColor();
                                goto NOP;
                            }
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

                        if (!TryResolveMemoryAddressOperand(dstAddressOperand, secondOperandType, out uint dstAddress))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal STR destination address operand {dstAddressOperand:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        WriteMemory(dstAddress, value);
                    }
                    return 3;

                // LOD - Load
                case 0x1B:
                    LogInstructionExecuting("LOD", first_operand);
                    {
                        ushort dstRegister = first_operand;
                        ushort srcAddressOperand = second_operand;

                        if (!TryResolveMemoryAddressOperand(srcAddressOperand, secondOperandType, out uint srcAddress))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal LOD source address operand {srcAddressOperand:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        ushort value = ReadMemory(srcAddress);

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
                        // Operand count is encoded in low two bits of the opcode word.
                        var operandCount = (byte)(opcode & 0b11);
                        if (operandCount == 2)
                        {
                            // V1.11: CMP SRC DST - compare two supplied operands (register/immediate/memory).
                            // V2.0 promotes the comparison to 32 bits whenever a primary
                            // 32-bit register participates, so mixed register/immediate
                            // and register/sub-register comparisons behave correctly.
                            bool use32Bit = Is32BitMemoryEnabled
                                && ((firstOperandType == OperandTypeRegister && IsPrimary32BitRegister(first_operand))
                                    || (secondOperandType == OperandTypeRegister && IsPrimary32BitRegister(second_operand)));
                            if (use32Bit
                                && TryResolveOperandValue32(first_operand, firstOperandType, out uint first32)
                                && TryResolveOperandValue32(second_operand, secondOperandType, out uint second32))
                            {
                                bool equal = first32 == second32;
                                bool lessThan = first32 < second32;
                                bool greaterThan = first32 > second32;
                                SetComparisonFlags(equal, lessThan, greaterThan, !equal);
                                return 3;
                            }
                            else
                            {
                                ushort a = ResolveOperandValue(first_operand, firstOperandType);
                                ushort b = ResolveOperandValue(second_operand, secondOperandType);
                                bool equal = a == b;
                                bool lessThan = a < b;
                                bool greaterThan = a > b;
                                SetComparisonFlags(equal, lessThan, greaterThan, !equal);
                                return 3;
                            }
                        }
                        else
                        {
                            // Legacy behaviour: compare X and Y
                            bool equal = (regX & 0xFFFF) == (regY & 0xFFFF);
                            bool lessThan = (regX & 0xFFFF) < (regY & 0xFFFF);
                            bool greaterThan = (regX & 0xFFFF) > (regY & 0xFFFF);
                            SetComparisonFlags(equal, lessThan, greaterThan, !equal);
                        }
                    }
                    break;

                // JEQ/JNE/JLT/JGT/JLE/JGE - Status-driven conditional jumps.
                case 0x1D:
                    LogInstructionExecuting("JEQ", first_operand);
                    if (IsEqual)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                case 0x1E:
                    LogInstructionExecuting("JNE", first_operand);
                    if (IsNotEqual)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                case 0x1F:
                    LogInstructionExecuting("JLT", first_operand);
                    if (IsLessThan)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                case 0x20:
                    LogInstructionExecuting("JGT", first_operand);
                    if (IsGreaterThan)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                case 0x21:
                    LogInstructionExecuting("JLE", first_operand);
                    if (IsLessThan || IsEqual)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                case 0x22:
                    LogInstructionExecuting("JGE", first_operand);
                    if (IsGreaterThan || IsEqual)
                        regPC = TryResolveMemoryAddressOperand(first_operand, firstOperandType, out uint targetPC) ? targetPC : first_operand;
                    return 2;

                // V1.6 multi-operand ALU instructions.
                case 0x23:
                    LogInstructionExecuting("ADD", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst + src))
                        goto NOP;
                    return 3;

                case 0x24:
                    LogInstructionExecuting("SUB", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst - src))
                        goto NOP;
                    return 3;

                case 0x25:
                    LogInstructionExecuting("MUL", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst * src))
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
                        return dst / src;
                    }))
                    {
                        goto NOP;
                    }
                    return 3;

                case 0x27:
                    LogInstructionExecuting("AND", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst & src))
                        goto NOP;
                    return 3;

                case 0x28:
                    LogInstructionExecuting("OR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst | src))
                        goto NOP;
                    return 3;

                case 0x29:
                    LogInstructionExecuting("XOR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst ^ src))
                        goto NOP;
                    return 3;

                case 0x2A:
                    LogInstructionExecuting("SHL", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst << (int)(src & 31)))
                        goto NOP;
                    return 3;

                case 0x2B:
                    LogInstructionExecuting("SHR", first_operand);
                    if (!TryExecuteBinaryMoInstruction(first_operand, firstOperandType, second_operand, secondOperandType, (src, dst) => dst >> (int)(src & 31)))
                        goto NOP;
                    return 3;

                // V1.7 stack instructions using an explicit register operand.
                case 0x2C:
                    LogInstructionExecuting("PUSH", first_operand);
                    {
                        // Primary registers push 32 bits; H/L sub-registers and the
                        // 16-bit control registers push 16 bits. SP advances by the
                        // address-granularity size of the pushed value (AM-aware).
                        if (Is32BitMemoryEnabled
                            && firstOperandType == OperandTypeRegister
                            && IsPrimary32BitRegister(first_operand)
                            && TryRead32BitRegister(first_operand, out uint pushValue32))
                        {
                            PushStackValue(pushValue32, 32);
                            return 2;
                        }

                        if (!TryReadRegisterOperand(first_operand, firstOperandType, out ushort pushValue, allowStatus: true))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal PUSH source register {first_operand:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        PushStackValue(pushValue, 16);
                    }
                    return 2;

                case 0x2D:
                    LogInstructionExecuting("POP", first_operand);
                    {
                        if (!IsRegisterOperand(firstOperandType, first_operand))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Illegal POP destination register {first_operand:X4}");
                            Console.ResetColor();
                            goto NOP;
                        }

                        if (Is32BitMemoryEnabled && IsPrimary32BitRegister(first_operand) && Supports32BitRegisterWrite(first_operand))
                        {
                            uint popped32 = PopStackValue(32);
                            if (!TryWrite32BitRegister(first_operand, popped32))
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Illegal POP destination register {first_operand:X4}");
                                Console.ResetColor();
                                goto NOP;
                            }
                        }
                        else
                        {
                            ushort popped16 = (ushort)PopStackValue(16);
                            if (!TryWriteRegisterOperand(first_operand, firstOperandType, popped16, allowStatus: true))
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Illegal POP destination register {first_operand:X4}");
                                Console.ResetColor();
                                goto NOP;
                            }
                        }
                    }
                    return 2;

                // V1.8 timing instruction.
                case 0x2E:
                    LogInstructionExecuting("WAIT", first_operand);
                    {
                        ushort delay = ResolveOperandValue(first_operand, firstOperandType);
                        _waitStartCycle = (ushort)(regCYC & 0xFFFF);
                        _waitDuration = delay;
                        _waitActive = true;
                    }
                    return 2;

                // V1.9 vertical blank wait instruction.
                case 0x2F:
                    LogInstructionExecuting("VBLANK", first_operand);
                    _vblankWaitSequence = Interlocked.CompareExchange(ref _vblankSequence, 0, 0);
                    _vblankWaitActive = true;
                    return 1;

                case 0x30:
                    LogInstructionExecuting("IN", first_operand);
                    if (!IsExtendedModeEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("IN is unavailable until EM is enabled.");
                        Console.ResetColor();
                        goto NOP;
                    }

                    if (firstOperandType != OperandTypeImmediate)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal IN port operand {first_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    if (!TryReadPort(first_operand, out ushort inValue))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal IN port {first_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    if (!TryWriteRegisterOperand(second_operand, secondOperandType, inValue, allowStatus: true))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal IN destination register {second_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    return 3;

                case 0x31:
                    LogInstructionExecuting("OUT", first_operand);
                    if (!IsExtendedModeEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("OUT is unavailable until EM is enabled.");
                        Console.ResetColor();
                        goto NOP;
                    }

                    if (!TryReadRegisterOperand(first_operand, firstOperandType, out ushort outValue, allowStatus: true))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal OUT source register {first_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    if (secondOperandType != OperandTypeImmediate)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal OUT port operand {second_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    if (!TryWritePort(second_operand, outValue))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Illegal OUT port {second_operand:X4}");
                        Console.ResetColor();
                        goto NOP;
                    }

                    return 3;

                case 0x32:
                    LogInstructionExecuting("OSRET", first_operand);
                    regPC = 0;
                    return 2;

                // Debug extension opcodes
                case 0xC000:
                    if (IsExtendedModeEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Legacy debug opcodes are unavailable in extension mode.");
                        Console.ResetColor();
                        goto NOP;
                    }
                    LogInstructionExecuting("DBG_LGC", first_operand);
                    Console.Write(DebugCharacters.GetCharacter(first_operand));
                    return 2;

                case 0xC001:
                    if (IsExtendedModeEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Legacy debug opcodes are unavailable in extension mode.");
                        Console.ResetColor();
                        goto NOP;
                    }
                    LogInstructionExecuting("DBG_MEM", first_operand);
                    DumpMemoryHex();
                    break;

                case 0xC002:
                    if (IsExtendedModeEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Legacy debug opcodes are unavailable in extension mode.");
                        Console.ResetColor();
                        goto NOP;
                    }
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
                Program.DebugLogInstructionFromMemory(RAM, regPC);
        }

        private void ChangeActiveRegister(ushort regID)
            => SetActiveRegister(regID != 0);

        private ushort GetValueOfTheActiveRegister()
            => (ushort)((IsActiveRegisterY ? regY : regX) & 0xFFFF);

        private ushort GetValueOfTheInactiveRegister()
            => (ushort)((IsActiveRegisterY ? regX : regY) & 0xFFFF);

        private void SetValueOfTheActiveRegister(ushort value)
        {
            if (IsActiveRegisterY)
                regY = value;
            else
                regX = value;
        }

        private bool IsPredicateSet
            => (regStatus & StatusPredicateMask) != 0;

        private bool IsExtendedModeEnabled
            => regEM != 0;

        private bool Is32BitMemoryEnabled
            => regEM == 2;

        private static ushort NormalizeExtensionMode(ushort value)
            => value switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                // Unknown values behave as plain extended mode rather than
                // silently enabling the 32-bit bus.
                _ => 1
            };

        private bool IsByteAddressingEnabled
            => regAM == 1;

        private ushort ReadMemory(uint address)
        {
            if (!Is32BitMemoryEnabled)
                address &= 0xFFFF;

            if (IsByteAddressingEnabled)
            {
                return (ushort)RAM.ReadByte(address);
            }
            else
            {
                return RAM.ReadWord(address);
            }
        }

        private void WriteMemory(uint address, ushort value)
        {
            if (!Is32BitMemoryEnabled)
                address &= 0xFFFF;

            if (IsByteAddressingEnabled)
            {
                RAM.WriteByte(address, (byte)(value & 0xFF));
            }
            else
            {
                RAM.WriteWord(address, value);
            }
        }

        // Pushes a value onto the hardware stack. The stack grows downwards.
        // In word-addressed mode each 16-bit unit occupies one word; in
        // byte-addressed mode each unit occupies one byte.
        private void PushStackValue(uint value, int bits)
        {
            int units = IsByteAddressingEnabled ? bits / 8 : bits / 16;

            if (IsByteAddressingEnabled)
            {
                for (int i = units - 1; i >= 0; i--)
                {
                    uint address = regSP - (uint)(units - 1 - i);
                    if (!Is32BitMemoryEnabled)
                        address &= 0xFFFF;
                    RAM.WriteByte(address, (byte)(value >> (8 * i)));
                }
            }
            else
            {
                for (int i = units - 1; i >= 0; i--)
                {
                    uint address = regSP - (uint)(units - 1 - i);
                    if (!Is32BitMemoryEnabled)
                        address &= 0xFFFF;
                    RAM.WriteWord(address, (ushort)(value >> (16 * i)));
                }
            }

            unchecked { regSP -= (uint)units; }
        }

        // Pops a value from the hardware stack. After raising SP by the size of
        // the stored value the units are read back in little-endian order.
        private uint PopStackValue(int bits)
        {
            int units = IsByteAddressingEnabled ? bits / 8 : bits / 16;
            unchecked { regSP += (uint)units; }

            uint value = 0;
            for (int i = 0; i < units; i++)
            {
                uint unitAddress = regSP - (uint)(units - 1 - i);
                if (!Is32BitMemoryEnabled)
                    unitAddress &= 0xFFFF;
                uint unit = IsByteAddressingEnabled
                    ? RAM.ReadByte(unitAddress)
                    : RAM.ReadWord(unitAddress);
                value |= unit << (IsByteAddressingEnabled ? 8 * i : 16 * i);
            }

            return value;
        }

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

        private static bool RequiresExtendedMode(ushort operand)
            => (operand >= RegisterIdR0 && operand <= RegisterIdAM)
            || (operand >= RegisterIdXL && operand <= RegisterIdCYCH)
            || (operand >= RegisterIdR0L && operand <= RegisterIdR7H);

        private static bool IsPrimary32BitRegister(ushort operand)
            => operand == RegisterIdX || operand == RegisterIdY || operand == RegisterIdPC
            || operand == RegisterIdSP || operand == RegisterIdCYC
            || (operand >= RegisterIdR0 && operand <= RegisterIdR7);

        private bool TryRead32BitRegister(ushort operand, out uint value)
        {
            value = 0;
            if (RequiresExtendedMode(operand) && !IsExtendedModeEnabled)
                return false;
            switch (operand)
            {
                case RegisterIdX: value = regX; return true;
                case RegisterIdY: value = regY; return true;
                case RegisterIdPC: value = regPC; return true;
                case RegisterIdSP: value = regSP; return true;
                case RegisterIdCYC: value = regCYC; return true;
                case RegisterIdR0: value = regR[0]; return true;
                case RegisterIdR1: value = regR[1]; return true;
                case RegisterIdR2: value = regR[2]; return true;
                case RegisterIdR3: value = regR[3]; return true;
                case RegisterIdR4: value = regR[4]; return true;
                case RegisterIdR5: value = regR[5]; return true;
                case RegisterIdR6: value = regR[6]; return true;
                case RegisterIdR7: value = regR[7]; return true;
                case RegisterIdEM: value = regEM; return true;
                case RegisterIdAM: value = regAM; return true;

                case RegisterIdXL: value = regX & 0xFFFF; return true;
                case RegisterIdXH: value = (regX >> 16) & 0xFFFF; return true;
                case RegisterIdYL: value = regY & 0xFFFF; return true;
                case RegisterIdYH: value = (regY >> 16) & 0xFFFF; return true;
                case RegisterIdPCL: value = regPC & 0xFFFF; return true;
                case RegisterIdPCH: value = (regPC >> 16) & 0xFFFF; return true;
                case RegisterIdSPL: value = regSP & 0xFFFF; return true;
                case RegisterIdSPH: value = (regSP >> 16) & 0xFFFF; return true;
                case RegisterIdCYCL: value = regCYC & 0xFFFF; return true;
                case RegisterIdCYCH: value = (regCYC >> 16) & 0xFFFF; return true;

                case RegisterIdR0L: value = regR[0] & 0xFFFF; return true;
                case RegisterIdR0H: value = (regR[0] >> 16) & 0xFFFF; return true;
                case RegisterIdR1L: value = regR[1] & 0xFFFF; return true;
                case RegisterIdR1H: value = (regR[1] >> 16) & 0xFFFF; return true;
                case RegisterIdR2L: value = regR[2] & 0xFFFF; return true;
                case RegisterIdR2H: value = (regR[2] >> 16) & 0xFFFF; return true;
                case RegisterIdR3L: value = regR[3] & 0xFFFF; return true;
                case RegisterIdR3H: value = (regR[3] >> 16) & 0xFFFF; return true;
                case RegisterIdR4L: value = regR[4] & 0xFFFF; return true;
                case RegisterIdR4H: value = (regR[4] >> 16) & 0xFFFF; return true;
                case RegisterIdR5L: value = regR[5] & 0xFFFF; return true;
                case RegisterIdR5H: value = (regR[5] >> 16) & 0xFFFF; return true;
                case RegisterIdR6L: value = regR[6] & 0xFFFF; return true;
                case RegisterIdR6H: value = (regR[6] >> 16) & 0xFFFF; return true;
                case RegisterIdR7L: value = regR[7] & 0xFFFF; return true;
                case RegisterIdR7H: value = (regR[7] >> 16) & 0xFFFF; return true;
                default:
                    return false;
            }
        }

        private static bool Supports32BitRegisterWrite(ushort operand)
            => operand == RegisterIdX || operand == RegisterIdY || operand == RegisterIdSP
            || (operand >= RegisterIdR0 && operand <= RegisterIdR7);

        private bool TryWrite32BitRegister(ushort operand, uint value)
        {
            if (RequiresExtendedMode(operand) && !IsExtendedModeEnabled)
                return false;

            switch (operand)
            {
                case RegisterIdX: regX = value; return true;
                case RegisterIdY: regY = value; return true;
                case RegisterIdSP: regSP = value; return true;
                case RegisterIdR0: regR[0] = value; return true;
                case RegisterIdR1: regR[1] = value; return true;
                case RegisterIdR2: regR[2] = value; return true;
                case RegisterIdR3: regR[3] = value; return true;
                case RegisterIdR4: regR[4] = value; return true;
                case RegisterIdR5: regR[5] = value; return true;
                case RegisterIdR6: regR[6] = value; return true;
                case RegisterIdR7: regR[7] = value; return true;
                default:
                    return false;
            }
        }

        private bool TryReadRegisterOperand(ushort operand, byte operandType, out ushort value, bool allowStatus = false)
        {
            value = 0;
            if (!IsRegisterOperand(operandType, operand))
                return false;

            if (RequiresExtendedMode(operand) && !IsExtendedModeEnabled)
                return false;

            return operand switch
            {
                RegisterIdX => SetOutValue((ushort)(regX & 0xFFFF), out value),
                RegisterIdY => SetOutValue((ushort)(regY & 0xFFFF), out value),
                RegisterIdPC => SetOutValue((ushort)(regPC & 0xFFFF), out value),
                RegisterIdStatus when allowStatus => SetOutValue(regStatus, out value),
                RegisterIdSP => SetOutValue((ushort)(regSP & 0xFFFF), out value),
                RegisterIdCYC => SetOutValue((ushort)(regCYC & 0xFFFF), out value),
                RegisterIdEM => SetOutValue(regEM, out value),
                RegisterIdAM => SetOutValue(regAM, out value),

                RegisterIdR0 => SetOutValue((ushort)(regR[0] & 0xFFFF), out value),
                RegisterIdR1 => SetOutValue((ushort)(regR[1] & 0xFFFF), out value),
                RegisterIdR2 => SetOutValue((ushort)(regR[2] & 0xFFFF), out value),
                RegisterIdR3 => SetOutValue((ushort)(regR[3] & 0xFFFF), out value),
                RegisterIdR4 => SetOutValue((ushort)(regR[4] & 0xFFFF), out value),
                RegisterIdR5 => SetOutValue((ushort)(regR[5] & 0xFFFF), out value),
                RegisterIdR6 => SetOutValue((ushort)(regR[6] & 0xFFFF), out value),
                RegisterIdR7 => SetOutValue((ushort)(regR[7] & 0xFFFF), out value),

                RegisterIdXL => SetOutValue((ushort)(regX & 0xFFFF), out value),
                RegisterIdXH => SetOutValue((ushort)((regX >> 16) & 0xFFFF), out value),
                RegisterIdYL => SetOutValue((ushort)(regY & 0xFFFF), out value),
                RegisterIdYH => SetOutValue((ushort)((regY >> 16) & 0xFFFF), out value),
                RegisterIdPCL => SetOutValue((ushort)(regPC & 0xFFFF), out value),
                RegisterIdPCH => SetOutValue((ushort)((regPC >> 16) & 0xFFFF), out value),
                RegisterIdSPL => SetOutValue((ushort)(regSP & 0xFFFF), out value),
                RegisterIdSPH => SetOutValue((ushort)((regSP >> 16) & 0xFFFF), out value),
                RegisterIdCYCL => SetOutValue((ushort)(regCYC & 0xFFFF), out value),
                RegisterIdCYCH => SetOutValue((ushort)((regCYC >> 16) & 0xFFFF), out value),

                RegisterIdR0L => SetOutValue((ushort)(regR[0] & 0xFFFF), out value),
                RegisterIdR0H => SetOutValue((ushort)((regR[0] >> 16) & 0xFFFF), out value),
                RegisterIdR1L => SetOutValue((ushort)(regR[1] & 0xFFFF), out value),
                RegisterIdR1H => SetOutValue((ushort)((regR[1] >> 16) & 0xFFFF), out value),
                RegisterIdR2L => SetOutValue((ushort)(regR[2] & 0xFFFF), out value),
                RegisterIdR2H => SetOutValue((ushort)((regR[2] >> 16) & 0xFFFF), out value),
                RegisterIdR3L => SetOutValue((ushort)(regR[3] & 0xFFFF), out value),
                RegisterIdR3H => SetOutValue((ushort)((regR[3] >> 16) & 0xFFFF), out value),
                RegisterIdR4L => SetOutValue((ushort)(regR[4] & 0xFFFF), out value),
                RegisterIdR4H => SetOutValue((ushort)((regR[4] >> 16) & 0xFFFF), out value),
                RegisterIdR5L => SetOutValue((ushort)(regR[5] & 0xFFFF), out value),
                RegisterIdR5H => SetOutValue((ushort)((regR[5] >> 16) & 0xFFFF), out value),
                RegisterIdR6L => SetOutValue((ushort)(regR[6] & 0xFFFF), out value),
                RegisterIdR6H => SetOutValue((ushort)((regR[6] >> 16) & 0xFFFF), out value),
                RegisterIdR7L => SetOutValue((ushort)(regR[7] & 0xFFFF), out value),
                RegisterIdR7H => SetOutValue((ushort)((regR[7] >> 16) & 0xFFFF), out value),
                _ => false
            };
        }

        private bool TryWriteRegisterOperand(ushort operand, byte operandType, ushort value, bool allowStatus = false)
        {
            if (!IsRegisterOperand(operandType, operand))
                return false;

            if (RequiresExtendedMode(operand) && !IsExtendedModeEnabled)
                return false;

            switch (operand)
            {
                case RegisterIdX: regX = value; return true;
                case RegisterIdY: regY = value; return true;
                case RegisterIdSP: regSP = value; return true;
                case RegisterIdStatus when allowStatus: regStatus = (byte)value; return true;
                case RegisterIdEM: regEM = NormalizeExtensionMode(value); return true;
                case RegisterIdAM: regAM = (ushort)(value != 0 ? 1 : 0); return true;

                case RegisterIdR0: regR[0] = value; return true;
                case RegisterIdR1: regR[1] = value; return true;
                case RegisterIdR2: regR[2] = value; return true;
                case RegisterIdR3: regR[3] = value; return true;
                case RegisterIdR4: regR[4] = value; return true;
                case RegisterIdR5: regR[5] = value; return true;
                case RegisterIdR6: regR[6] = value; return true;
                case RegisterIdR7: regR[7] = value; return true;

                case RegisterIdXL: regX = (regX & 0xFFFF0000) | value; return true;
                case RegisterIdXH: regX = (regX & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdYL: regY = (regY & 0xFFFF0000) | value; return true;
                case RegisterIdYH: regY = (regY & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdSPL: regSP = (regSP & 0xFFFF0000) | value; return true;
                case RegisterIdSPH: regSP = (regSP & 0x0000FFFF) | ((uint)value << 16); return true;

                case RegisterIdR0L: regR[0] = (regR[0] & 0xFFFF0000) | value; return true;
                case RegisterIdR0H: regR[0] = (regR[0] & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdR1L: regR[1] = (regR[1] & 0xFFFF0000) | value; return true;
                case RegisterIdR1H: regR[1] = (regR[1] & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdR2L: regR[2] = (regR[2] & 0xFFFF0000) | value; return true;
                case RegisterIdR2H: regR[2] = (regR[2] & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdR3L: regR[3] = (regR[3] & 0xFFFF0000) | value; return true;
                case RegisterIdR3H: regR[3] = (regR[3] & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdR4L: regR[4] = (regR[4] & 0xFFFF0000) | value; return true;
                case RegisterIdR4H: regR[4] = (regR[4] & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdR5L: regR[5] = (regR[5] & 0xFFFF0000) | value; return true;
                case RegisterIdR5H: regR[5] = (regR[5] & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdR6L: regR[6] = (regR[6] & 0xFFFF0000) | value; return true;
                case RegisterIdR6H: regR[6] = (regR[6] & 0x0000FFFF) | ((uint)value << 16); return true;
                case RegisterIdR7L: regR[7] = (regR[7] & 0xFFFF0000) | value; return true;
                case RegisterIdR7H: regR[7] = (regR[7] & 0x0000FFFF) | ((uint)value << 16); return true;
                default:
                    return false;
            }
        }

        private ushort ResolveOperandValue(ushort operand, byte operandType)
        {
            if (IsRegisterOperand(operandType, operand))
                return TryReadRegisterOperand(operand, operandType, out ushort registerValue, allowStatus: true) ? registerValue : (ushort)0;

            if (operandType == OperandTypeIndirectMemory)
            {
                if (!TryResolveMemoryAddressOperand(operand, operandType, out uint indirectAddress))
                    return 0;
                return ReadMemory(indirectAddress);
            }

            if (operandType == OperandTypeDirectMemory)
                return ReadMemory(operand);

            return operand;
        }

        // Resolves an operand as a 32-bit value. Immediates and 16-bit
        // sub-registers are zero-extended, while primary registers, pointers
        // and memory accesses preserve their full width.
        private bool TryResolveOperandValue32(ushort operand, byte operandType, out uint value)
        {
            value = 0;

            if (operandType == OperandTypeRegister)
                return TryRead32BitRegister(operand, out value);

            if (operandType == OperandTypeIndirectMemory)
            {
                if (!TryResolveMemoryAddressOperand(operand, operandType, out uint address))
                    return false;
                value = ReadMemory(address);
                return true;
            }

            if (operandType == OperandTypeDirectMemory)
            {
                value = ReadMemory(operand);
                return true;
            }

            value = operand;
            return true;
        }

        private bool TryExecuteBinaryMoInstruction(ushort srcOperand, byte srcOperandType, ushort dstOperand, byte dstOperandType, Func<uint, uint, uint> operation)
        {
            // A primary 32-bit destination performs a full 32-bit operation
            // only when the 32-bit memory/register model is enabled (EM=2).
            if (Is32BitMemoryEnabled
                && dstOperandType == OperandTypeRegister
                && IsPrimary32BitRegister(dstOperand)
                && TryRead32BitRegister(dstOperand, out uint dstValue32))
            {
                if (!TryResolveOperandValue32(srcOperand, srcOperandType, out uint srcValue32))
                    return false;

                uint result32 = operation(srcValue32, dstValue32);
                return TryWrite32BitRegister(dstOperand, result32);
            }

            ushort srcValue = ResolveOperandValue(srcOperand, srcOperandType);

            if (!TryReadRegisterOperand(dstOperand, dstOperandType, out ushort dstValue, allowStatus: false))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Illegal ALU destination register {dstOperand:X4}");
                Console.ResetColor();
                return false;
            }

            uint result = operation(srcValue, dstValue) & 0xFFFF;
            return TryWriteRegisterOperand(dstOperand, dstOperandType, (ushort)result);
        }

        private bool TryResolveMemoryAddressOperand(ushort operand, byte operandType, out uint address)
        {
            if (operandType == OperandTypeIndirectMemory || operandType == OperandTypeRegister)
            {
                if (TryRead32BitRegister(operand, out address))
                {
                    if (!Is32BitMemoryEnabled)
                        address &= 0xFFFF;
                    return true;
                }
                address = 0;
                return false;
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

            return (operand >= RegisterIdX && operand <= RegisterIdAM)
                || (operand >= RegisterIdXL && operand <= RegisterIdCYCH)
                || (operand >= RegisterIdR0L && operand <= RegisterIdR7H);
        }

        private bool TryReadPort(ushort port, out ushort value)
        {
            var portDevices = _portDevices;
            if (port < portDevices.Length)
            {
                var kind = portDevices[port];
                if (kind == PortDeviceKind.VF16Pad)
                {
                    value = _controllerState;
                    return true;
                }

                if (kind == PortDeviceKind.VF16Keyboard)
                {
                    // Read keyboard report
                    lock (_keyboardLock)
                    {
                        value = _keyboardReport;
                    }
                    return true;
                }

                if (kind == PortDeviceKind.VF16Mouse)
                {
                    // Mouse is a single-port packed snapshot:
                    // bits 0-2 buttons, bit 3 wheel up, bit 4 wheel down,
                    // bits 5-9 X delta signed 5-bit, bits 10-14 Y delta signed 5-bit.
                    lock (_mouseLock)
                    {
                        ushort packedButtons = (ushort)(_mouseButtons & 0x0007);
                        ushort packedWheelUp = _mouseWheel > 0 ? (ushort)(1 << 3) : (ushort)0;
                        ushort packedWheelDown = _mouseWheel < 0 ? (ushort)(1 << 4) : (ushort)0;
                        ushort packedX = (ushort)(PackSigned5(_mouseDX) << 5);
                        ushort packedY = (ushort)(PackSigned5(_mouseDY) << 10);
                        value = (ushort)(packedButtons | packedWheelUp | packedWheelDown | packedX | packedY);
                    }
                    return true;
                }

                if (kind == PortDeviceKind.VF16TTY)
                {
                    lock (_ttyLock)
                    {
                        if (_ttyInputBuffer.Count == 0)
                        {
                            PumpTtyInputBuffer();
                        }

                        value = _ttyInputBuffer.Count > 0 ? _ttyInputBuffer.Dequeue() : (ushort)0;
                    }

                    return true;
                }

                if (kind == PortDeviceKind.VF16Flash)
                {
                    lock (_flashLock)
                    {
                        if (_flashCommandReady[port])
                        {
                            // There's a pending command from a previous OUT
                            ushort command = _flashPendingCommand[port];
                            ushort address = (ushort)((command >> 1) & 0x7FFF); // Bits 1-15
                            byte operation = (byte)(command & 0x0001);          // Bit 0

                            if (operation == 0) // Read operation
                            {
                                value = _flashStorage[address];
                            }
                            else
                            {
                                // Attempted read after write command - return 0
                                value = 0;
                            }

                            _flashCommandReady[port] = false; // Clear pending command
                        }
                        else
                        {
                            // No pending command - return 0
                            value = 0;
                        }
                    }
                    return true;
                }
            }

            value = 0;
            return true;
        }

        private void PumpTtyInputBuffer()
        {
            try
            {
                while (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(intercept: true);
                    ushort ascii = NormalizeConsoleKeyToAscii(keyInfo.KeyChar, keyInfo.Key);
                    if (ascii != 0)
                    {
                        _ttyInputBuffer.Enqueue(ascii);
                    }
                }
            }
            catch
            {
                // No console available or input not supported in this environment.
            }
        }

        private static ushort NormalizeConsoleKeyToAscii(char keyChar, ConsoleKey key)
        {
            if (key == ConsoleKey.Enter)
                return 0x000D;

            if (key == ConsoleKey.Backspace)
                return 0x0008;

            if (key == ConsoleKey.Tab)
                return 0x0009;

            if (keyChar == '\0')
                return 0;

            return keyChar <= 0x7F ? (ushort)keyChar : (ushort)'?';
        }

        private static ushort PackSigned5(sbyte value)
        {
            int clamped = Math.Clamp((int)value, -16, 15);
            return (ushort)(clamped & 0x001F);
        }

        private bool TryWritePort(ushort port, ushort value)
        {
            var portDevices = _portDevices;
            if (port < portDevices.Length)
            {
                var kind = portDevices[port];
                if (kind == PortDeviceKind.VF16Pad)
                {
                    if (!IsExtendedModeEnabled)
                    {
                        _controllerState = (byte)(value & 0x00FF);
                    }
                }

                if (kind == PortDeviceKind.VF16Keyboard)
                {
                    // OUT to a keyboard port is a no-op in current implementation
                    // Future: support LED state (NumLock/CapsLock) in high bits.
                }

                if (kind == PortDeviceKind.VF16TTY)
                {
                    char outputChar = (char)(value & 0x00FF);
                    if (outputChar == '\n')
                    {
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.Write(outputChar);
                    }

                    return true;
                }

                if (kind == PortDeviceKind.VF16Flash)
                {
                    lock (_flashLock)
                    {
                        if (!_flashCommandReady[port]) // No pending command, this is a new command word
                        {
                            // Store the command word for the next operation
                            _flashPendingCommand[port] = value;
                            _flashCommandReady[port] = true;
                        }
                        else // There's a pending command, this is the data word
                        {
                            ushort command = _flashPendingCommand[port];
                            ushort address = (ushort)((command >> 1) & 0x7FFF); // Bits 1-15
                            byte operation = (byte)(command & 0x0001);           // Bit 0

                            if (operation == 1) // Write operation
                            {
                                if (address < _flashStorage.Length)
                                {
                                    _flashStorage[address] = value;
                                    SaveFlashToDisk(port);
                                }
                                // If address is beyond storage, silently ignore (per spec)
                            }
                            // If operation == 0 (read), this OUT is unexpected; just clear pending command

                            _flashCommandReady[port] = false; // Clear pending command
                        }
                    }
                    return true;
                }
            }

            return true;
        }

        private static PortDeviceKind[] ClonePortDevices(IReadOnlyList<PortDeviceKind>? portDevices)
        {
            var cloned = new PortDeviceKind[PortCount];
            cloned[0] = PortDeviceKind.VF16Pad;

            if (portDevices is null)
            {
                return cloned;
            }

            int count = Math.Min(PortCount, portDevices.Count);
            for (int i = 0; i < count; i++)
            {
                cloned[i] = portDevices[i];
            }

            return cloned;
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
        internal void SetFlashSavePaths(string[]? paths)
        {
            if (paths is not null && paths.Length == PortCount)
            {
                Array.Copy(paths, _flashSavePaths, PortCount);
                LoadFlashFromDisk();
            }
        }

        private void LoadFlashFromDisk()
        {
            lock (_flashLock)
            {
                for (int port = 0; port < PortCount; port++)
                {
                    string path = _flashSavePaths[port];
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    try
                    {
                        if (File.Exists(path))
                        {
                            byte[] buffer = File.ReadAllBytes(path);
                            int wordsToRead = System.Math.Min(buffer.Length / 2, _flashStorage.Length);
                            for (int i = 0; i < wordsToRead; i++)
                            {
                                _flashStorage[i] = (ushort)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void SaveFlashToDisk(int port)
        {
            if (port < 0 || port >= PortCount)
                return;

            string path = _flashSavePaths[port];
            if (string.IsNullOrWhiteSpace(path))
                return;

            lock (_flashSaveTaskLocks[port])
            {
                _flashSaveTasks[port] = Task.Run(() =>
                {
                    try
                    {
                        byte[] buffer = new byte[_flashStorage.Length * 2];
                        lock (_flashLock)
                        {
                            for (int i = 0; i < _flashStorage.Length; i++)
                            {
                                buffer[i * 2] = (byte)(_flashStorage[i] & 0xFF);
                                buffer[i * 2 + 1] = (byte)((_flashStorage[i] >> 8) & 0xFF);
                            }
                        }

                        var dir = System.IO.Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(dir))
                            System.IO.Directory.CreateDirectory(dir);

                        System.IO.File.WriteAllBytes(path, buffer);
                    }
                    catch
                    {
                    }
                });
            }
        }

    }
}