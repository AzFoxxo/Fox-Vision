namespace FoxVision.Components
{
    internal class ContiguousMemory
    {
        private ushort[] _memory;
        private readonly object _memoryLock = new();
        internal ushort MaxAddress { get; private set; }

        internal ContiguousMemory(ushort size)
        {
            _memory = new ushort[size + 1];
            this.MaxAddress = size;

            Console.WriteLine($"Created contiguous memory with addresses 0x0000 to 0x{size:X4}");
        }

        internal void WriteUnchecked(ushort address, ushort data)
        {
            lock (_memoryLock)
            {
                _memory[address] = data;
            }
        }

        internal ushort ReadUnchecked(ushort address)
        {
            lock (_memoryLock)
            {
                return _memory[address];
            }
        }

        internal void CopyDescendingUnchecked(ushort startAddress, Span<ushort> destination)
        {
            lock (_memoryLock)
            {
                ushort address = startAddress;
                for (int index = 0; index < destination.Length; index++)
                {
                    destination[index] = _memory[address];
                    address--;
                }
            }
        }

        internal void ClearUnchecked()
        {
            lock (_memoryLock)
            {
                Array.Clear(_memory, 0, _memory.Length);
            }
        }
    }
}