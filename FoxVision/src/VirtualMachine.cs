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
            _unprotectedMemory = new(ushort.MaxValue);
            bool loadedRom = LoadRomIntoMemory(_currentRom);

            // Create the CPU
            _processor = new(_unprotectedMemory, _options.ExecutionSpeedHz, _options.LogInstruction, _currentRom.StartAddress);
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
                () => _processor.GetCycleCount(),
                () =>
                {
                    _processor.SignalVBlank();
                },
                () =>
                {
                    Program.DebugLogROMAsData(_currentRom.Words);
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
                if (!LoadRomIntoMemory(romImage))
                {
                    return false;
                }

                _processor = new Processor(_unprotectedMemory, updated.ExecutionSpeedHz, updated.LogInstruction, romImage.StartAddress);
                StartCpuThread();
                _currentRom = romImage;
                _options.RomPath = updated.RomPath;
                _options.ExecutionSpeedHz = updated.ExecutionSpeedHz;
                _options.LogInstruction = updated.LogInstruction;
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

            if ((uint)rom.StartAddress + (uint)rom.Words.Length > (uint)_unprotectedMemory.MaxAddress + 1)
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
                ushort address = (ushort)(rom.StartAddress + i);
                _unprotectedMemory.WriteUnchecked(address, rom.Words[i]);

                if (i < previewWords)
                    Console.Write(_unprotectedMemory.ReadUnchecked(address).ToString("X4") + " ");
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