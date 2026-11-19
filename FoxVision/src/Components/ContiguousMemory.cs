namespace FoxVision.Components
{
    internal class ContiguousMemory
    {
        internal const ulong MinimumSizeBytes = 128UL * 1024UL;
        internal const ulong MaximumSizeBytes = 4UL * 1024UL * 1024UL * 1024UL;
        internal const ulong DefaultSizeBytes = MaximumSizeBytes;
        private const int BaseWordCount = 65536;
        private const int PageWordCount = 65536;

        private readonly ushort[] _memory;
        private readonly Dictionary<uint, ushort[]> _extendedPages = new();
        private readonly object _memoryLock = new();
        private ulong _sizeBytes;
        private uint _maxWordAddress;

        internal ushort MaxAddress => (ushort)(BaseWordCount - 1);
        internal ulong SizeBytes
        {
            get
            {
                lock (_memoryLock)
                    return _sizeBytes;
            }
        }

        internal uint MaxWordAddress
        {
            get
            {
                lock (_memoryLock)
                    return _maxWordAddress;
            }
        }

        internal ContiguousMemory(ulong sizeBytes)
        {
            _memory = new ushort[BaseWordCount];
            _sizeBytes = NormalizeSizeBytes(sizeBytes);
            _maxWordAddress = (uint)(_sizeBytes / 2 - 1);

            Console.WriteLine($"Created contiguous memory: {FormatSize(_sizeBytes)} (word addresses 0x00000000 to 0x{_maxWordAddress:X8})");
        }

        internal static ulong NormalizeSizeBytes(ulong sizeBytes)
        {
            if (sizeBytes > MaximumSizeBytes)
                sizeBytes = MaximumSizeBytes;

            // Memory is word-addressable, so keep the configured size aligned to
            // a whole number of 16-bit words.
            sizeBytes &= ~1UL;

            if (sizeBytes < MinimumSizeBytes)
                sizeBytes = MinimumSizeBytes;

            return sizeBytes;
        }

        internal static string FormatSize(ulong sizeBytes)
        {
            const ulong kibibyte = 1024UL;
            const ulong mebibyte = kibibyte * 1024UL;
            const ulong gibibyte = mebibyte * 1024UL;

            if (sizeBytes >= gibibyte && sizeBytes % gibibyte == 0)
                return $"{sizeBytes / gibibyte} GB";
            if (sizeBytes >= mebibyte && sizeBytes % mebibyte == 0)
                return $"{sizeBytes / mebibyte} MB";
            if (sizeBytes >= kibibyte && sizeBytes % kibibyte == 0)
                return $"{sizeBytes / kibibyte} KB";

            return $"{sizeBytes} bytes";
        }

        internal void Resize(ulong sizeBytes)
        {
            sizeBytes = NormalizeSizeBytes(sizeBytes);

            lock (_memoryLock)
            {
                _sizeBytes = sizeBytes;
                _maxWordAddress = (uint)(sizeBytes / 2 - 1);

                uint maxPageIndex = _maxWordAddress >> 16;
                if (_extendedPages.Count > 0)
                {
                    var pagesToRemove = _extendedPages.Keys.Where(pageIndex => pageIndex > maxPageIndex).ToArray();
                    foreach (uint pageIndex in pagesToRemove)
                    {
                        _extendedPages.Remove(pageIndex);
                    }
                }
            }
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
                return ReadWordUnchecked(wordAddress);
            }
        }

        internal void WriteWord(uint wordAddress, ushort data)
        {
            lock (_memoryLock)
            {
                WriteWordUnchecked(wordAddress, data);
            }
        }

        private ushort ReadWordUnchecked(uint wordAddress)
        {
            if (wordAddress > _maxWordAddress)
                return 0;

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

        private void WriteWordUnchecked(uint wordAddress, ushort data)
        {
            if (wordAddress > _maxWordAddress)
                return;

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
                page = new ushort[PageWordCount];
                _extendedPages[pageIndex] = page;
            }

            page[offset] = data;
        }

        internal byte ReadByte(uint byteAddress)
        {
            lock (_memoryLock)
            {
                uint wordAddress = byteAddress >> 1;
                if (wordAddress > _maxWordAddress)
                    return 0;

                ushort word = ReadWordUnchecked(wordAddress);
                return (byte)((byteAddress & 1) == 0 ? (word & 0xFF) : ((word >> 8) & 0xFF));
            }
        }

        internal void WriteByte(uint byteAddress, byte data)
        {
            lock (_memoryLock)
            {
                uint wordAddress = byteAddress >> 1;
                if (wordAddress > _maxWordAddress)
                    return;

                ushort word = ReadWordUnchecked(wordAddress);
                if ((byteAddress & 1) == 0)
                {
                    word = (ushort)((word & 0xFF00) | data);
                }
                else
                {
                    word = (ushort)((word & 0x00FF) | (data << 8));
                }
                WriteWordUnchecked(wordAddress, word);
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
