#define DEBUG

namespace FoxVision.Components
{
    internal class ContiguousMemory
    {
        private readonly ushort[] _memory;
        internal UInt32 Size {get; private set;}

        internal ContiguousMemory(UInt32 size)
        {
            _memory = new ushort[size];
            this.Size = size;

            Console.WriteLine($"Created contiguous memory of size {size}bytes");
        }

        internal void WriteUnchecked(ushort address, ushort data)
        {
            #if DEBUG
            try
            {
                Write(address, data);
            }
            catch (IndexOutOfRangeException)
            {
                Console.WriteLine(new string('-', Console.WindowWidth));
                Console.WriteLine($"Must be 0-{Size-1}.");
                Console.WriteLine($"Is RAM size {_memory.Length} correct? It should be {ushort.MaxValue+1} (+1 for the zero) for full range of values.");
                Console.WriteLine(new string('-', Console.WindowWidth));
                Console.BackgroundColor = ConsoleColor.Red;
                Console.WriteLine(Environment.StackTrace);
                Console.BackgroundColor = ConsoleColor.Black;
                Environment.Exit(1);
            }
            #else
            Write(address, data);
            #endif
        }

        internal ushort ReadUnchecked(ushort address)
        {
            #if DEBUG
            try
            {
                return Read(address);
            }
            catch (IndexOutOfRangeException)
            {
                Console.WriteLine(new string('-', Console.WindowWidth));
                Console.WriteLine($"Tried to read from {address} which is out of bounds!");
                Console.WriteLine($"Must be 0-{Size-1}.");
                Console.WriteLine($"Is RAM size {_memory.Length} correct? It should be {ushort.MaxValue+1} (+1 for the zero) for full range of values.");
                Console.WriteLine(new string('-', Console.WindowWidth));
                Console.BackgroundColor = ConsoleColor.Red;
                Console.WriteLine(Environment.StackTrace);
                Console.BackgroundColor = ConsoleColor.Black;
                Environment.Exit(1);
                return 0;
            }
            #else
            return Read(address);
            #endif
        }

        private void Write(ushort address, ushort data) => _memory[address] = data;
        private ushort Read(ushort address) => _memory[address];
    }
}