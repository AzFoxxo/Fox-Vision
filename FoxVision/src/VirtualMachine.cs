using FoxVision.Components;

namespace FoxVision
{
    internal class VirtualMachine
    {
        private ContiguousMemory _unprotectedMemory;
        private Processor _processor;
        internal VirtualMachine(ushort[] ROM)
        {
            // Create a new block of contiguous memory for the RAM
            _unprotectedMemory = new(ushort.MaxValue);

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

            // Create the CPU
            _processor = new(_unprotectedMemory);
            
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