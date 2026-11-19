using System.Globalization;
using System.Text.Json;
using FoxVision.Components;

namespace FoxVision
{
    internal enum PortDeviceKind
    {
        None = 0,
        VF16Pad = 1,
        VF16Keyboard = 2,
        VF16Mouse = 3,
        VF16TTY = 4,
        VF16Flash = 5,
        VF32HardDisk = 6
    }

    internal enum BuildTargetMode
    {
        Legacy = 0,
        Extended = 1,
        Em2Am1 = 2
    }

    internal sealed class EmulatorOptions
    {
        internal const int PortCount = 8;
        internal const int DefaultWindowScale = 8;
        internal const int DefaultTargetFps = 60;
        internal const int DefaultExecutionSpeedHz = 8_000_000;
        internal const int DefaultRomPreviewWords = 64;
        internal const ulong DefaultMemorySizeBytes = ContiguousMemory.DefaultSizeBytes;
        internal const ulong MinimumMemorySizeBytes = ContiguousMemory.MinimumSizeBytes;
        internal const ulong MaximumMemorySizeBytes = ContiguousMemory.MaximumSizeBytes;
        internal const uint DefaultControllerUpKey = (uint)Gdk.Key.Up;
        internal const uint DefaultControllerDownKey = (uint)Gdk.Key.Down;
        internal const uint DefaultControllerLeftKey = (uint)Gdk.Key.Left;
        internal const uint DefaultControllerRightKey = (uint)Gdk.Key.Right;
        internal const uint DefaultControllerAKey = (uint)Gdk.Key.z;
        internal const uint DefaultControllerBKey = (uint)Gdk.Key.x;
        internal const uint DefaultControllerStartKey = (uint)Gdk.Key.Return;
        internal const uint DefaultControllerSelectKey = (uint)Gdk.Key.space;

        internal string RomPath { get; set; } = Environment.GetEnvironmentVariable("FOXVISION_ROM_PATH") ?? string.Empty;

        internal int WindowScale { get; set; } = ParseEnvInt("FOXVISION_WINDOW_SCALE", DefaultWindowScale);
        internal int TargetFps { get; set; } = ParseEnvInt("FOXVISION_TARGET_FPS", DefaultTargetFps);
        internal int ExecutionSpeedHz { get; set; } = ParseEnvInt("FOXVISION_EXECUTION_SPEED_HZ", DefaultExecutionSpeedHz);
        internal bool LogInstruction { get; set; } = ParseEnvBool("FOXVISION_LOG_INSTRUCTION", false);
        internal int RomPreviewWords { get; set; } = ParseEnvInt("FOXVISION_ROM_PREVIEW_WORDS", DefaultRomPreviewWords);
        internal ulong MemorySizeBytes { get; set; } = ParseEnvMemorySize();
        internal uint ControllerUpKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_UP_KEY", DefaultControllerUpKey);
        internal uint ControllerDownKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_DOWN_KEY", DefaultControllerDownKey);
        internal uint ControllerLeftKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_LEFT_KEY", DefaultControllerLeftKey);
        internal uint ControllerRightKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_RIGHT_KEY", DefaultControllerRightKey);
        internal uint ControllerAKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_A_KEY", DefaultControllerAKey);
        internal uint ControllerBKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_B_KEY", DefaultControllerBKey);
        internal uint ControllerStartKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_START_KEY", DefaultControllerStartKey);
        internal uint ControllerSelectKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_SELECT_KEY", DefaultControllerSelectKey);
        internal int FixedTtyPortIndex { get; set; } = ParseEnvInt("FOXVISION_TTY_PORT_INDEX", 4);
        internal PortDeviceKind[] PortDevices { get; set; } = CreateDefaultPortDevices();

        // Target mode for builds invoked from the GUI or options
        internal BuildTargetMode BuildMode { get; set; } = ParseEnvBuildMode("FOXVISION_BUILD_MODE", BuildTargetMode.Legacy);
        internal ushort? BuildMapper { get; set; } = ParseEnvMapper("FOXVISION_BUILD_MAPPER");
        internal bool BuildStrictFormat { get; set; } = ParseEnvBool("FOXVISION_BUILD_STRICT_FORMAT", true);
        internal string BuildLoadAddress { get; set; } = Environment.GetEnvironmentVariable("FOXVISION_BUILD_LOAD_ADDRESS") ?? string.Empty;

        // Backward compatibility property for extended mode
        internal bool BuildExtended
        {
            get => BuildMode != BuildTargetMode.Legacy;
            set => BuildMode = value ? BuildTargetMode.Extended : BuildTargetMode.Legacy;
        }
        internal bool Headless { get; set; } = ParseEnvBool("FOXVISION_HEADLESS", false);
        internal string KeyboardLayout { get; set; } = Environment.GetEnvironmentVariable("FOXVISION_KEYBOARD_LAYOUT") ?? string.Empty;

        internal string[] FlashSavePaths { get; set; } = CreateDefaultFlashSavePaths();
        internal string[] HardDiskSavePaths { get; set; } = CreateDefaultHardDiskSavePaths();

        internal EmulatorOptions()
        {
            ApplyPortDeviceEnvironmentOverrides();
            ApplySavedInputConfigIfUnset();
            ApplyHardDiskPathEnvironmentOverrides();
        }

        internal void SaveInputConfig()
        {
            var payload = new SavedInputConfig
            {
                ControllerUpKey = ControllerUpKey,
                ControllerDownKey = ControllerDownKey,
                ControllerLeftKey = ControllerLeftKey,
                ControllerRightKey = ControllerRightKey,
                ControllerAKey = ControllerAKey,
                ControllerBKey = ControllerBKey,
                ControllerStartKey = ControllerStartKey,
                ControllerSelectKey = ControllerSelectKey,
                FixedTtyPortIndex = FixedTtyPortIndex,
                PortDevices = ClonePortDevices(PortDevices)
            };
            // include flash save paths
            payload.FlashSavePaths = FlashSavePaths;
            // include hard disk image paths
            payload.HardDiskSavePaths = HardDiskSavePaths;
            // include keyboard layout
            payload.KeyboardLayout = KeyboardLayout;

            var configPath = GetInputConfigPath();
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }

        private void ApplySavedInputConfigIfUnset()
        {
            var configPath = GetInputConfigPath();
            if (!File.Exists(configPath))
                return;

            try
            {
                var json = File.ReadAllText(configPath);
                var saved = JsonSerializer.Deserialize<SavedInputConfig>(json);
                if (saved is null)
                    return;

                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_UP_KEY")))
                    ControllerUpKey = saved.ControllerUpKey;
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_DOWN_KEY")))
                    ControllerDownKey = saved.ControllerDownKey;
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_LEFT_KEY")))
                    ControllerLeftKey = saved.ControllerLeftKey;
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_RIGHT_KEY")))
                    ControllerRightKey = saved.ControllerRightKey;
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_A_KEY")))
                    ControllerAKey = saved.ControllerAKey;
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_B_KEY")))
                    ControllerBKey = saved.ControllerBKey;
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_START_KEY")))
                    ControllerStartKey = saved.ControllerStartKey;
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_CONTROLLER_SELECT_KEY")))
                    ControllerSelectKey = saved.ControllerSelectKey;

                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_TTY_PORT_INDEX")))
                    FixedTtyPortIndex = saved.FixedTtyPortIndex;

                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOXVISION_KEYBOARD_LAYOUT")))
                    KeyboardLayout = saved.KeyboardLayout;

                var savedPortDevices = saved.PortDevices;
                if (savedPortDevices is not null && savedPortDevices.Length == PortCount)
                {
                    for (int i = 0; i < PortCount; i++)
                    {
                        string envName = $"FOXVISION_PORT{i}_DEVICE";
                        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envName)))
                            PortDevices[i] = savedPortDevices[i];
                    }
                }

                var savedFlashPaths = saved.FlashSavePaths;
                if (savedFlashPaths is not null && savedFlashPaths.Length == PortCount)
                {
                    FlashSavePaths = savedFlashPaths;
                }

                var savedHardDiskPaths = saved.HardDiskSavePaths;
                if (savedHardDiskPaths is not null && savedHardDiskPaths.Length == PortCount)
                {
                    HardDiskSavePaths = savedHardDiskPaths;
                }

                NormalizeFixedPortDevices();
            }
            catch
            {
                // Ignore invalid persisted config and keep current defaults/environment values.
            }
        }

        private static PortDeviceKind[] CreateDefaultPortDevices()
        {
            var portDevices = new PortDeviceKind[PortCount];
            portDevices[0] = PortDeviceKind.VF16Pad;
            portDevices[1] = PortDeviceKind.VF16Pad;
            portDevices[2] = PortDeviceKind.VF16Keyboard;
            portDevices[3] = PortDeviceKind.VF16Mouse;
            portDevices[5] = PortDeviceKind.VF16Flash;
            portDevices[6] = PortDeviceKind.VF32HardDisk;
            return portDevices;
        }

        private static PortDeviceKind[] ClonePortDevices(IReadOnlyList<PortDeviceKind>? source)
        {
            var portDevices = CreateDefaultPortDevices();
            if (source is null)
            {
                return portDevices;
            }

            int count = Math.Min(PortCount, source.Count);
            for (int i = 0; i < count; i++)
            {
                portDevices[i] = source[i];
            }

            return portDevices;
        }

        private void ApplyPortDeviceEnvironmentOverrides()
        {
            for (int i = 0; i < PortCount; i++)
            {
                string envName = $"FOXVISION_PORT{i}_DEVICE";
                PortDevices[i] = ParseEnvPortDevice(envName, PortDevices[i]);
            }

            NormalizeFixedPortDevices();
        }

        private void ApplyHardDiskPathEnvironmentOverrides()
        {
            for (int i = 0; i < PortCount; i++)
            {
                string envName = $"FOXVISION_PORT{i}_HARDDISK_PATH";
                string? value = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    HardDiskSavePaths[i] = value;
                }
            }
        }

        private void NormalizeFixedPortDevices()
        {
            int fixedTtyPortIndex = GetNormalizedFixedTtyPortIndex();
            for (int i = 0; i < PortCount; i++)
            {
                if (i == fixedTtyPortIndex)
                {
                    PortDevices[i] = PortDeviceKind.VF16TTY;
                }
                else if (PortDevices[i] == PortDeviceKind.VF16TTY)
                {
                    PortDevices[i] = PortDeviceKind.None;
                }
            }

            FixedTtyPortIndex = fixedTtyPortIndex;
        }

        private int GetNormalizedFixedTtyPortIndex()
            => Math.Clamp(FixedTtyPortIndex, 0, PortCount - 1);

        private static string GetInputConfigPath()
        {
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseConfigDir = !string.IsNullOrWhiteSpace(xdgConfigHome)
                ? xdgConfigHome
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(baseConfigDir, "foxvision", "input.json");
        }

        private sealed class SavedInputConfig
        {
            public uint ControllerUpKey { get; set; } = DefaultControllerUpKey;
            public uint ControllerDownKey { get; set; } = DefaultControllerDownKey;
            public uint ControllerLeftKey { get; set; } = DefaultControllerLeftKey;
            public uint ControllerRightKey { get; set; } = DefaultControllerRightKey;
            public uint ControllerAKey { get; set; } = DefaultControllerAKey;
            public uint ControllerBKey { get; set; } = DefaultControllerBKey;
            public uint ControllerStartKey { get; set; } = DefaultControllerStartKey;
            public uint ControllerSelectKey { get; set; } = DefaultControllerSelectKey;
            public int FixedTtyPortIndex { get; set; } = 4;
            public PortDeviceKind[] PortDevices { get; set; } = CreateDefaultPortDevices();
            public string KeyboardLayout { get; set; } = string.Empty;
            public string[] FlashSavePaths { get; set; } = CreateDefaultFlashSavePaths();
            public string[] HardDiskSavePaths { get; set; } = CreateDefaultHardDiskSavePaths();
        }

        private static ulong ParseEnvMemorySize()
        {
            if (TryParseMemorySize(Environment.GetEnvironmentVariable("FOXVISION_MEMORY_SIZE"), out var sizeBytes))
                return sizeBytes;

            if (TryParseMemorySize(Environment.GetEnvironmentVariable("FOXVISION_MEMORY_SIZE_BYTES"), out sizeBytes))
                return sizeBytes;

            string? megabytes = Environment.GetEnvironmentVariable("FOXVISION_MEMORY_SIZE_MB");
            if (!string.IsNullOrWhiteSpace(megabytes)
                && ulong.TryParse(megabytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mb)
                && mb <= MaximumMemorySizeBytes / (1024UL * 1024UL))
            {
                return ContiguousMemory.NormalizeSizeBytes(mb * 1024UL * 1024UL);
            }

            return DefaultMemorySizeBytes;
        }

        internal static bool TryParseMemorySize(string? value, out ulong sizeBytes)
        {
            sizeBytes = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value.Trim();
            ulong multiplier = 1;

            if (text.Length >= 3 && text.EndsWith("IB", StringComparison.OrdinalIgnoreCase))
            {
                char unit = char.ToUpperInvariant(text[^3]);
                if (!TryGetMemoryUnitMultiplier(unit, out multiplier))
                    return false;
                text = text[..^3];
            }
            else if (text.Length >= 2
                && char.ToUpperInvariant(text[^1]) == 'B'
                && TryGetMemoryUnitMultiplier(text[^2], out multiplier))
            {
                text = text[..^2];
            }
            else
            {
                char unit = char.ToUpperInvariant(text[^1]);
                if (TryGetMemoryUnitMultiplier(unit, out multiplier))
                {
                    text = text[..^1];
                }
            }

            text = text.Trim();
            ulong parsed;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                    return false;
            }
            else if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            if (parsed == 0)
                return false;

            if (parsed > ulong.MaxValue / multiplier)
            {
                sizeBytes = MaximumMemorySizeBytes;
                return true;
            }

            sizeBytes = ContiguousMemory.NormalizeSizeBytes(parsed * multiplier);
            return true;
        }

        private static bool TryGetMemoryUnitMultiplier(char unit, out ulong multiplier)
        {
            const ulong kibibyte = 1024UL;
            const ulong mebibyte = kibibyte * 1024UL;
            const ulong gibibyte = mebibyte * 1024UL;

            switch (char.ToUpperInvariant(unit))
            {
                case 'K':
                    multiplier = kibibyte;
                    return true;
                case 'M':
                    multiplier = mebibyte;
                    return true;
                case 'G':
                    multiplier = gibibyte;
                    return true;
                default:
                    multiplier = 1;
                    return false;
            }
        }

        internal static string FormatMemorySize(ulong sizeBytes)
            => ContiguousMemory.FormatSize(sizeBytes);

        private static int ParseEnvInt(string name, int fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var parsed))
                return parsed;

            return fallback;
        }

        private static uint ParseEnvUInt(string name, uint fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && uint.TryParse(value, out var parsed))
                return parsed;

            return fallback;
        }

        private static PortDeviceKind ParseEnvPortDevice(string name, PortDeviceKind fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return Enum.TryParse(value, true, out PortDeviceKind parsed) ? parsed : fallback;
        }

        private static ushort? ParseEnvMapper(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (value.Equals("rombyte8_32", StringComparison.OrdinalIgnoreCase) || value.Equals("byte8_32", StringComparison.OrdinalIgnoreCase) || value == "2")
                return 2;
            if (value.Equals("rom32k", StringComparison.OrdinalIgnoreCase) || value == "1")
                return 1;
            if (value.Equals("rom4k", StringComparison.OrdinalIgnoreCase) || value == "0")
                return 0;
            return ushort.TryParse(value, out var m) ? m : null;
        }

        private static BuildTargetMode ParseEnvBuildMode(string name, BuildTargetMode fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            if (Enum.TryParse(value, true, out BuildTargetMode parsed))
                return parsed;

            if (value.Equals("em2_am1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("extended2", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("v2", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("em2", StringComparison.OrdinalIgnoreCase))
                return BuildTargetMode.Em2Am1;

            if (value.Equals("extended", StringComparison.OrdinalIgnoreCase))
                return BuildTargetMode.Extended;

            if (value.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                return BuildTargetMode.Legacy;

            return fallback;
        }

        private static bool ParseEnvBool(string name, bool fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var parsed))
                return parsed;

            return fallback;
        }

        internal static string[] CreateDefaultFlashSavePaths()
        {
            var paths = new string[PortCount];
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "vf16", "ports", "vf16flash");

            for (int i = 0; i < PortCount; i++)
            {
                paths[i] = Path.Combine(baseDir, $"port{i}.bin");
            }
            return paths;
        }

        internal static string[] CreateDefaultHardDiskSavePaths()
        {
            var paths = new string[PortCount];
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "vf16", "ports", "vf32harddisk");

            for (int i = 0; i < PortCount; i++)
            {
                paths[i] = Path.Combine(baseDir, $"port{i}.img");
            }
            return paths;
        }
    }
}
