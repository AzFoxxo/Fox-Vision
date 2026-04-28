using FoxVision.Components;
using System.Threading;

namespace FoxVision
{
    internal class VirtualMachine
    {
        private ContiguousMemory _unprotectedMemory;
        private Processor _processor;
        private readonly EmulatorOptions _options;

        internal VirtualMachine(ushort[] ROM, EmulatorOptions options)
        {
            _options = options;

            // Create a new block of contiguous memory for the RAM
            _unprotectedMemory = new(ushort.MaxValue);

            // Copy as much ROM as possible into RAM while respecting memory bounds.
            var size = Math.Min(ROM.Length, _unprotectedMemory.MaxAddress + 1);
            if (ROM.Length > size)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"ROM is larger than addressable RAM and will be truncated: {ROM.Length} words -> {size} words");
                Console.ForegroundColor = ConsoleColor.White;
            }

            // In case of odd number, add a zero to the end of the ROM
            Console.ForegroundColor = ConsoleColor.Yellow;
            int previewWords = Math.Min(size, _options.RomPreviewWords);
            for (int i = 0; i < size; i++)
            {
                ushort address = (ushort)i;
                _unprotectedMemory.WriteUnchecked(address, ROM[i]);

                if (i < previewWords)
                    Console.Write(_unprotectedMemory.ReadUnchecked(address).ToString("X4") + " ");
            }

            if (size > previewWords)
                Console.Write("...");

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();

            Console.WriteLine("ROM copied to RAM");

            // Create the CPU
            _processor = new(_unprotectedMemory, _options.ExecutionSpeedHz, _options.LogInstruction);

            var shutdownRequested = 0;
            Thread cpuThread = new(() =>
            {
                while (Interlocked.CompareExchange(ref shutdownRequested, 0, 0) == 0)
                {
                    if (_processor.ExecuteCycle())
                    {
                        Interlocked.Exchange(ref shutdownRequested, 1);
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "FoxVision-CPU"
            };

            cpuThread.Start();

            using (var renderer = new GraphicsRenderer(
                _unprotectedMemory,
                options,
                updated => Program.TryLaunchRomProcess(updated.RomPath, updated),
                (sourcePath, updated) => Program.TryBuildAndLaunchRomProcess(sourcePath, updated),
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
                () =>
                {
                    _processor.SignalVBlank();
                },
                () =>
                {
                    Program.DebugLogROMAsData(ROM);
                    return true;
                }))
            {
                renderer.Run();
            }

            Interlocked.Exchange(ref shutdownRequested, 1);
            cpuThread.Join();

            Console.WriteLine("Processor has been halted");
            Console.WriteLine("Now exiting");
        }
    }
}