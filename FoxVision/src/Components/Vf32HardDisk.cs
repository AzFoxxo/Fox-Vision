using System.Buffers.Binary;

namespace FoxVision.Components
{
    /// <summary>
    /// VF32HardDisk: a 4 GiB, byte-addressed, 32-bit bus storage cartridge.
    ///
    /// The device is intended for EM=2 (32-bit mode). It is accessed through a
    /// 32-bit port as a sequence of three parts:
    ///   1. a 32-bit byte address (low two bits ignored; transfers are 32-bit aligned)
    ///   2. a 32-bit command word
    ///        bit 0      : 0 = read block, 1 = write block
    ///        bits 1..31 : transfer count - 1 (1 .. 2,147,483,648 words)
    ///   3. one 32-bit data word per transfer, read with IN or written with OUT
    ///
    /// Each transfer advances the address by four bytes. Storage is file-backed
    /// and sparse on supporting file systems, so a fresh 4 GiB image does not
    /// consume 4 GiB of physical disk space.
    /// </summary>
    internal sealed class Vf32HardDisk : IDisposable
    {
        internal const long CapacityBytes = 1L << 32; // 4 GiB
        private const int PortCount = EmulatorOptions.PortCount;
        private const int TransferSize = 4;
        private const uint AddressMask = 0xFFFF_FFFC;

        private enum Phase
        {
            Idle,
            Command,
            ReadData,
            WriteData
        }

        private sealed class PortState
        {
            internal Phase Phase;
            internal uint Address;
            internal uint Remaining;
            internal bool Write;

            internal void Reset()
            {
                Phase = Phase.Idle;
                Address = 0;
                Remaining = 0;
                Write = false;
            }
        }

        private readonly object _gate = new();
        private readonly string?[] _paths = new string?[PortCount];
        private readonly FileStream?[] _streams = new FileStream?[PortCount];
        private readonly PortState[] _states = new PortState[PortCount];
        private readonly bool[] _openFailureReported = new bool[PortCount];
        private bool _disposed;

        internal Vf32HardDisk()
        {
            for (int port = 0; port < PortCount; port++)
            {
                _states[port] = new PortState();
            }
        }

        internal void SetSavePaths(IReadOnlyList<string>? paths)
        {
            lock (_gate)
            {
                for (int port = 0; port < PortCount; port++)
                {
                    string? path = paths is not null && port < paths.Count ? paths[port] : null;
                    if (string.Equals(_paths[port], path, StringComparison.Ordinal))
                        continue;

                    CloseStream(port);
                    _paths[port] = path;
                    _openFailureReported[port] = false;
                    _states[port].Reset();
                }
            }
        }

        internal void ResetState()
        {
            lock (_gate)
            {
                for (int port = 0; port < PortCount; port++)
                {
                    _states[port].Reset();
                }
            }
        }

        internal bool TryReadWord(int port, out uint value)
        {
            value = 0;
            if ((uint)port >= PortCount)
                return false;

            lock (_gate)
            {
                var state = _states[port];
                if (state.Phase != Phase.ReadData)
                {
                    // An IN outside a read transaction has no data to return.
                    // Abort any partial command so the next OUT starts cleanly.
                    state.Reset();
                    return true;
                }

                value = ReadData(port, state.Address);
                Advance(state);
                return true;
            }
        }

        internal bool TryWriteWord(int port, uint value)
        {
            if ((uint)port >= PortCount)
                return false;

            lock (_gate)
            {
                var state = _states[port];
                switch (state.Phase)
                {
                    case Phase.Idle:
                        state.Address = value & AddressMask;
                        state.Phase = Phase.Command;
                        return true;

                    case Phase.Command:
                        state.Write = (value & 0x1) != 0;
                        state.Remaining = ((value >> 1) & 0x7FFF_FFFF) + 1;
                        state.Phase = state.Write ? Phase.WriteData : Phase.ReadData;
                        return true;

                    case Phase.WriteData:
                        WriteData(port, state.Address, value);
                        Advance(state);
                        if (state.Phase == Phase.Idle)
                            Flush(port);
                        return true;

                    default:
                        // An OUT during a read aborts the read and starts a new
                        // address/command sequence. This keeps a stray write
                        // from leaving the device stuck in a read transaction.
                        state.Reset();
                        state.Address = value & AddressMask;
                        state.Phase = Phase.Command;
                        return true;
                }
            }
        }

        private static void Advance(PortState state)
        {
            state.Address = unchecked(state.Address + TransferSize);
            state.Remaining--;

            if (state.Remaining == 0)
            {
                state.Phase = Phase.Idle;
                state.Write = false;
            }
            else
            {
                state.Phase = state.Write ? Phase.WriteData : Phase.ReadData;
            }
        }

        private uint ReadData(int port, uint address)
        {
            var stream = GetStream(port, create: false);
            if (stream is null)
                return 0;

            long offset = address;
            long length = stream.Length;
            if (offset >= length)
                return 0;

            int available = (int)Math.Min(TransferSize, length - offset);
            Span<byte> buffer = stackalloc byte[TransferSize];
            int read = 0;
            try
            {
                stream.Position = offset;
                while (read < available)
                {
                    int count = stream.Read(buffer[read..available]);
                    if (count <= 0)
                        break;
                    read += count;
                }
            }
            catch (Exception ex)
            {
                ReportOpenFailure(port, ex);
                return 0;
            }

            if (read < TransferSize)
                buffer[read..].Clear();

            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        private void WriteData(int port, uint address, uint value)
        {
            var stream = GetStream(port, create: true);
            if (stream is null)
                return;

            try
            {
                if (stream.Length < CapacityBytes)
                    stream.SetLength(CapacityBytes);

                Span<byte> buffer = stackalloc byte[TransferSize];
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
                stream.Position = address;
                stream.Write(buffer);
            }
            catch (Exception ex)
            {
                ReportOpenFailure(port, ex);
            }
        }

        private FileStream? GetStream(int port, bool create)
        {
            var stream = _streams[port];
            if (stream is not null)
                return stream;

            string? path = _paths[port];
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (!create && !File.Exists(path))
                return null;

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var opened = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.RandomAccess);

                _streams[port] = opened;
                return opened;
            }
            catch (Exception ex)
            {
                ReportOpenFailure(port, ex);
                return null;
            }
        }

        private void Flush(int port)
        {
            var stream = _streams[port];
            if (stream is null)
                return;

            try
            {
                stream.Flush(flushToDisk: false);
            }
            catch (Exception ex)
            {
                ReportOpenFailure(port, ex);
            }
        }

        private void ReportOpenFailure(int port, Exception ex)
        {
            if (_openFailureReported[port])
                return;

            _openFailureReported[port] = true;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"VF32HardDisk: image access failed for '{_paths[port]}': {ex.Message}");
            Console.ResetColor();
        }

        private void CloseStream(int port)
        {
            var stream = _streams[port];
            _streams[port] = null;
            if (stream is null)
                return;

            try
            {
                stream.Flush(flushToDisk: false);
                stream.Dispose();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                for (int port = 0; port < PortCount; port++)
                {
                    CloseStream(port);
                }
            }
        }
    }
}
