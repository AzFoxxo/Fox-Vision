namespace FoxVision.Components
{
    internal class ContiguousMemory
    {
        private ushort[] _memory;
        private readonly Dictionary<uint, ushort[]> _extendedPages = new();
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

        internal ushort ReadWord(uint wordAddress)
        {
            lock (_memoryLock)
            {
                uint pageIndex = wordAddress >> 16;
                ushort offset = (ushort)(wordAddress & 0xFFFF);
                if (pageIndex == 0)
                {
                    return offset < _memory.Length ? _memory[offset] : (ushort)0;
                }

                if (_extendedPages.TryGetValue(pageIndex, out var page))
                {
                    return page[offset];
                }

                return 0;
            }
        }

        internal void WriteWord(uint wordAddress, ushort data)
        {
            lock (_memoryLock)
            {
                uint pageIndex = wordAddress >> 16;
                ushort offset = (ushort)(wordAddress & 0xFFFF);
                if (pageIndex == 0)
                {
                    if (offset < _memory.Length)
                        _memory[offset] = data;
                    return;
                }

                if (!_extendedPages.TryGetValue(pageIndex, out var page))
                {
                    page = new ushort[65536];
                    _extendedPages[pageIndex] = page;
                }

                page[offset] = data;
            }
        }

        internal byte ReadByte(uint byteAddress)
        {
            uint wordAddress = byteAddress >> 1;
            ushort word = ReadWord(wordAddress);
            return (byte)((byteAddress & 1) == 0 ? (word & 0xFF) : ((word >> 8) & 0xFF));
        }

        internal void WriteByte(uint byteAddress, byte data)
        {
            uint wordAddress = byteAddress >> 1;
            lock (_memoryLock)
            {
                ushort word = ReadWord(wordAddress);
                if ((byteAddress & 1) == 0)
                {
                    word = (ushort)((word & 0xFF00) | data);
                }
                else
                {
                    word = (ushort)((word & 0x00FF) | (data << 8));
                }
                WriteWord(wordAddress, word);
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
                _extendedPages.Clear();
            }
        }
    }
}
