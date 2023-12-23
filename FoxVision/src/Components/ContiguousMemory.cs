namespace FoxVision.Components
{
    internal class ContiguousMemory
    {
        private ushort[] _memory;
        internal ushort Size {get; private set;}

        internal ContiguousMemory(ushort size)
        {
            _memory = new ushort[size];
            this.Size = size;

            Console.WriteLine($"Created contiguous memory of size {size}bytes");
        }

        internal void WriteUnchecked(ushort address, ushort data)
        {
            _memory[address] = data;
        }

        internal ushort ReadUnchecked(ushort address)
        {
            return _memory[address];
        }
    }
}