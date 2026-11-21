using FoxVision.Components;
using System.Threading;

namespace FoxVision
{
    internal class VirtualMachine
    {
        private readonly ContiguousMemory _unprotectedMemory;
        private Processor _processor;
        private readonly EmulatorOptions _options;
        private readonly object _reloadLock = new();
        private RomImage _currentRom;
        private int _shutdownRequested;
        private Thread? _cpuThread;

        internal VirtualMachine(RomImage initialRom, EmulatorOptions options)
        {
            _options = options;
            _currentRom = initialRom;

            // Create a new block of contiguous memory for the RAM
            _unprotectedMemory = new(_options.MemorySizeBytes);
            bool loadedRom = LoadRomIntoMemory(_currentRom);

            // Create the CPU
            _processor = new(_unprotectedMemory, _options.ExecutionSpeedHz, _options.LogInstruction, _currentRom.ResetAddress, _options.PortDevices, _currentRom.Mapper);
            _processor.SetFlashSavePaths(_options.FlashSavePaths);
            _processor.SetHardDiskSavePaths(_options.HardDiskSavePaths);

            if (_options.Headless)
            {
                if (loadedRom)
                {
                    while (!_processor.ExecuteCycle())
                    {
                    }
                }

                Console.WriteLine("Processor has been halted");
                Console.WriteLine($"Cycle count: {_processor.GetCycleCount()}");
                Console.WriteLine("Now exiting");
                return;
            }

            if (loadedRom)
            {
                StartCpuThread();
            }

            using (var renderer = new GraphicsRenderer(
                _unprotectedMemory,
                options,
                updated => TryLoadRomInPlace(updated),
                (sourcePath, updated) => TryBuildAndLoadRomInPlace(sourcePath, updated),
                executionSpeedHz =>
                {
                    _processor.SetExecutionSpeedHz(executionSpeedHz);
                    return true;
                },
                memorySizeBytes =>
                {
                    _unprotectedMemory.Resize(memorySizeBytes);
                    _options.MemorySizeBytes = _unprotectedMemory.SizeBytes;
                    return true;
                },
                enabled =>
                {
                    _processor.SetInstructionLogging(enabled);
                    return true;
                },
                paused =>
                {
                    _processor.SetPaused(paused);
                    return true;
                },
                portConfig =>
                {
                    _processor.SetPortConfiguration(portConfig.PortDevices);
                    // Propagate any flash save path changes as well
                    if (portConfig.FlashSavePaths is not null && portConfig.FlashSavePaths.Length == EmulatorOptions.PortCount)
                    {
                        _processor.SetFlashSavePaths(portConfig.FlashSavePaths);
                        _options.FlashSavePaths = portConfig.FlashSavePaths.ToArray();
                    }

                    if (portConfig.HardDiskSavePaths is not null && portConfig.HardDiskSavePaths.Length == EmulatorOptions.PortCount)
                    {
                        _processor.SetHardDiskSavePaths(portConfig.HardDiskSavePaths);
                        _options.HardDiskSavePaths = portConfig.HardDiskSavePaths.ToArray();
                    }

                    _options.PortDevices = portConfig.PortDevices.ToArray();
                    return true;
                },
                buttonMask =>
                {
                    _processor.LatchControllerButton(buttonMask);
                    return true;
                },
                keyboardReport =>
                {
                    _processor.SetKeyboardReport(keyboardReport);
                    return true;
                },
                () => _processor.RequestQuitForeground(),
                (buttons, wheel, dx, dy) =>
                {
                    _processor.SetMouseState(buttons, (sbyte)wheel, (sbyte)dx, (sbyte)dy);
                    return true;
                },
                () => _processor.GetCycleCount(),
                () =>
                {
                    _processor.SignalVBlank();
                },
                () =>
                {
                    Program.DebugLogROMAsData(_currentRom);
                    return true;
                }))
            {
                renderer.Run();
            }

            StopCpuThread();

            Console.WriteLine("Processor has been halted");
            Console.WriteLine("Now exiting");
        }

        private bool TryLoadRomInPlace(EmulatorOptions updated)
        {
            if (string.IsNullOrWhiteSpace(updated.RomPath))
            {
                return false;
            }

            if (!Program.TryLoadRomImage(updated.RomPath, out var romImage))
            {
                return false;
            }

            lock (_reloadLock)
            {
                StopCpuThread();
                _unprotectedMemory.Resize(updated.MemorySizeBytes);
                if (!LoadRomIntoMemory(romImage))
                {
                    return false;
                }

                _processor = new Processor(_unprotectedMemory, updated.ExecutionSpeedHz, updated.LogInstruction, romImage.ResetAddress, null, romImage.Mapper);
                _processor.SetPortConfiguration(updated.PortDevices);
                if (updated.FlashSavePaths is not null && updated.FlashSavePaths.Length == EmulatorOptions.PortCount)
                {
                    _processor.SetFlashSavePaths(updated.FlashSavePaths);
                    _options.FlashSavePaths = updated.FlashSavePaths.ToArray();
                }

                _processor.SetHardDiskSavePaths(updated.HardDiskSavePaths);
                _options.HardDiskSavePaths = updated.HardDiskSavePaths.ToArray();
                StartCpuThread();
                _currentRom = romImage;
                _options.RomPath = updated.RomPath;
                _options.ExecutionSpeedHz = updated.ExecutionSpeedHz;
                _options.LogInstruction = updated.LogInstruction;
                _options.MemorySizeBytes = _unprotectedMemory.SizeBytes;
                _options.PortDevices = updated.PortDevices.ToArray();
            }

            return true;
        }

        private bool TryBuildAndLoadRomInPlace(string sourcePath, EmulatorOptions updated)
        {
            if (!Program.TryBuildRom(sourcePath, updated, out var builtRomPath))
            {
                return false;
            }

            updated.RomPath = builtRomPath;
            return TryLoadRomInPlace(updated);
        }

        private bool LoadRomIntoMemory(RomImage rom)
        {
            if (rom.Words.Length > rom.MaximumWords)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ROM is larger than the machine limit (0x0000-0x{(rom.MaximumWords - 1):X4}). The machine will remain halted.");
                Console.ForegroundColor = ConsoleColor.White;

                return false;
            }

            ulong maxAddress = (ulong)_unprotectedMemory.MaxWordAddress + 1;
            if ((ulong)rom.StartAddress + (ulong)rom.Words.Length > maxAddress)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ROM payload does not fit in RAM at the requested start address.");
                Console.ForegroundColor = ConsoleColor.White;

                return false;
            }

            _unprotectedMemory.ClearUnchecked();

            var size = rom.Words.Length;
            Console.ForegroundColor = ConsoleColor.Yellow;
            int previewWords = Math.Min(size, _options.RomPreviewWords);
            for (int i = 0; i < size; i++)
            {
                uint address = (uint)(rom.StartAddress + (uint)i);
                _unprotectedMemory.WriteWord(address, rom.Words[i]);

                if (i < previewWords)
                    Console.Write(_unprotectedMemory.ReadWord(address).ToString("X4") + " ");
            }

            if (size > previewWords)
                Console.Write("...");

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            Console.WriteLine("ROM copied to RAM");

            return true;
        }

        private void StartCpuThread()
        {
            Interlocked.Exchange(ref _shutdownRequested, 0);
            _cpuThread = new Thread(() =>
            {
                while (Interlocked.CompareExchange(ref _shutdownRequested, 0, 0) == 0)
                {
                    if (_processor.ExecuteCycle())
                    {
                        Interlocked.Exchange(ref _shutdownRequested, 1);
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "FoxVision-CPU"
            };

            _cpuThread.Start();
        }

        private void StopCpuThread()
        {
            Interlocked.Exchange(ref _shutdownRequested, 1);
            if (_cpuThread is not null && _cpuThread.IsAlive)
            {
                _cpuThread.Join();
            }

            _cpuThread = null;
        }
    }
}