using FoxVision.Components;

namespace FoxVision
{
    internal class VirtualMachine
    {
        private readonly ContiguousMemory _unprotectedMemory;
        private readonly Processor _processor;
        private readonly GraphicsUnit _graphicsUnit;
        internal VirtualMachine(ushort[] ROM)
        {
            // Create a new block of contiguous memory for the RAM
            _unprotectedMemory = new(ushort.MaxValue+1);

            // Copy the first 4kb of ROM into the unprotected memory or all room, whichever is smaller
            var size = Math.Min(ROM.Length, 4096);

            // In case of odd number, add a zero to the end of the ROM
            Console.ForegroundColor = ConsoleColor.Yellow;
            for (ushort i = 0; i < size; i++)
            {
                _unprotectedMemory.WriteUnchecked(i, ROM[i]);
                Console.Write(_unprotectedMemory.ReadUnchecked(i).ToString("X4") + " ");
            }
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();

            Console.WriteLine("ROM copied to RAM");

            // Create the graphics unit
            _graphicsUnit = new(192, 108);

            // Create the CPU
            _processor = new(_unprotectedMemory, _graphicsUnit);
            
            // Enter CPU cycle
            while (!_processor.ExecuteCycle())
            {
                
            }

            // Exit
            Console.WriteLine("Processor has been halted");
            Console.WriteLine("Now exiting");
        }
    }
}