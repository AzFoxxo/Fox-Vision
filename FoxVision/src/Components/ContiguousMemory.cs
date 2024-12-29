// Enable debugging for errors when accessing and writing memory
#define DEBUG

namespace FoxVision.Components
{
    internal class ContiguousMemory
    {
        private readonly ushort[] _memory;
        internal uint Size { get; private set; }

        /// <summary>
        /// Create a block of contiguous memory
        /// </summary>
        /// <param name="size">Size of array, if you want to use uint16 max value, add one for desired behaviour</param>
        internal ContiguousMemory(uint size)
        {
            _memory = new ushort[size];
            Size = size;

            Console.WriteLine($"Created contiguous memory of size {size}bytes");
        }

        /// <summary>
        /// Write data to memory (unsafe)
        /// </summary>
        /// <param name="address">Address to write to</param>
        /// <param name="data">Data to write</param>
        internal void WriteUnchecked(ushort address, ushort data)
        {
            // If debugging is enabled, catch illegal memory access and write value to the address
#if DEBUG
            try
            {
                Write(address, data);
            }
            catch (IndexOutOfRangeException)
            {
                PrintMemoryViolation(Environment.StackTrace, true, address, data);
            }
#else
            Write(address, data);
#endif
        }

        /// <summary>
        /// Read data from memory (unsafe)
        /// </summary>
        /// <param name="address">Address to read from</param>
        /// <returns>Value returned</returns>
        internal ushort ReadUnchecked(ushort address)
        {
            // If debugging is enabled, catch illegal memory access and read value at the address
#if DEBUG
            try
            {
                return Read(address);
            }
            catch (IndexOutOfRangeException)
            {
                PrintMemoryViolation(Environment.StackTrace, false, address, 0);
                return 0;
            }
#else
            return Read(address);
#endif
        }

        // Memory violation logging
        private void PrintMemoryViolation(string stackTrace, bool isWrite, ushort address, ushort value)
        {
            Console.WriteLine(new string('-', Console.WindowWidth));

            if (isWrite)
                Console.WriteLine($"Tried writing {value} to address {address}.");
            else
                Console.WriteLine($"Tried reading from address {address}.");

            Console.WriteLine($"Must be 0-{Size - 1}.");
            Console.WriteLine($"Is the RAM size of {_memory.Length} correct? It should be {ushort.MaxValue + 1} (+1 for the zero) for the full address space of the CPU.");
            Console.WriteLine(new string('-', Console.WindowWidth));
            Console.BackgroundColor = ConsoleColor.Red;
            Console.WriteLine(Environment.StackTrace);
            Console.BackgroundColor = ConsoleColor.Black;
            Environment.Exit(1);
        }

        // Underlying methods for writing and reading
        private void Write(ushort address, ushort data) => _memory[address] = data;
        private ushort Read(ushort address) => _memory[address];
    }
}