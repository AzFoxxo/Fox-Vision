using System.Text.Json;

namespace FoxVision
{
    internal enum PortDeviceKind
    {
        None = 0,
        VF16Pad = 1
    }

    internal sealed class EmulatorOptions
    {
        internal const int PortCount = 8;
        internal const int DefaultWindowScale = 8;
        internal const int DefaultTargetFps = 60;
        internal const int DefaultExecutionSpeedHz = 8_000_000;
        internal const int DefaultRomPreviewWords = 64;
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
        internal uint ControllerUpKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_UP_KEY", DefaultControllerUpKey);
        internal uint ControllerDownKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_DOWN_KEY", DefaultControllerDownKey);
        internal uint ControllerLeftKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_LEFT_KEY", DefaultControllerLeftKey);
        internal uint ControllerRightKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_RIGHT_KEY", DefaultControllerRightKey);
        internal uint ControllerAKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_A_KEY", DefaultControllerAKey);
        internal uint ControllerBKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_B_KEY", DefaultControllerBKey);
        internal uint ControllerStartKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_START_KEY", DefaultControllerStartKey);
        internal uint ControllerSelectKey { get; set; } = ParseEnvUInt("FOXVISION_CONTROLLER_SELECT_KEY", DefaultControllerSelectKey);
        internal PortDeviceKind[] PortDevices { get; set; } = CreateDefaultPortDevices();

        // When true, build operations invoked from the GUI will target extended mode
        // (passes `--mode extended` to the FoxC compiler and assembler).
        internal bool BuildExtended { get; set; } = false;

        internal EmulatorOptions()
        {
            ApplyPortDeviceEnvironmentOverrides();
            ApplySavedInputConfigIfUnset();
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
                PortDevices = ClonePortDevices(PortDevices)
            };

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
        }

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
            public PortDeviceKind[] PortDevices { get; set; } = CreateDefaultPortDevices();
        }

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

        private static bool ParseEnvBool(string name, bool fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var parsed))
                return parsed;

            return fallback;
        }
    }
}
