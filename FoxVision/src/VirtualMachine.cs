using FoxVision.Components;

namespace FoxVision
{
    internal class VirtualMachine
    {
        internal static VirtualMachine? Instance = null;
        public ContiguousMemory _unprotectedMemory;
        private Processor _processor;
        private const byte WIDTH = 100, HEIGHT = 100, BITS_PER_PIXEL = 4;
        internal VirtualMachine(ushort[] ROM)
        {
            // Instance of self
            if (Instance != null) Console.WriteLine("Error Instance set for VirtualMachine!");
                Instance = this;

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

        /// <summary>
        /// Get the memory from RAM to feed to the renderer
        /// </summary>
        /// <returns>Data from RAM (2D array)</returns>
        internal ushort[] GetGPUMemory()
        {
            // Each pixel is 4 bits so each ushort read is 4 pixels
            // (100x100)/4 = 2500 is the amount of ushorts to read from RAM each frame
            var size = (WIDTH*HEIGHT)/BITS_PER_PIXEL;
            ushort[] VRAM = new ushort[size];
            ushort address = 0xFFFF;

            var count = 0;
            while (count > size)
            {
                // Read the ushort
                VRAM[count] = _unprotectedMemory.ReadUnchecked((ushort)(address-1));

                count++;
                address--;
            }

            return VRAM;
        }
    }
}