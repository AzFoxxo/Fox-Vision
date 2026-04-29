using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Cairo;
using Gtk;
using System.Numerics;

namespace FoxVision.Components
{
    internal sealed class GraphicsRenderer : IDisposable
    {
        private const int Width = 100;
        private const int Height = 100;
        private const int PixelsPerPackedByte = 2;
        private const int PackedBytesPerRow = Width / PixelsPerPackedByte;
        private const int VramSizeInBytes = 5000;

        private const ushort VramStartAddress = 0xFFFF;
        private const ushort VramEndAddress = (ushort)(VramStartAddress - VramSizeInBytes + 1);
        private const ushort ControllerStateAddress = 0x1000;

        private const byte ControllerUpMask = 1 << 0;
        private const byte ControllerDownMask = 1 << 1;
        private const byte ControllerLeftMask = 1 << 2;
        private const byte ControllerRightMask = 1 << 3;
        private const byte ControllerAMask = 1 << 4;
        private const byte ControllerBMask = 1 << 5;
        private const byte ControllerStartMask = 1 << 6;
        private const byte ControllerSelectMask = 1 << 7;

        private enum ControllerButton
        {
            Up,
            Down,
            Left,
            Right,
            A,
            B,
            Start,
            Select
        }

        private static readonly ControllerButton[] ControllerButtons =
        [
            ControllerButton.Up,
            ControllerButton.Down,
            ControllerButton.Left,
            ControllerButton.Right,
            ControllerButton.A,
            ControllerButton.B,
            ControllerButton.Start,
            ControllerButton.Select
        ];

        private readonly ContiguousMemory _ram;
        private readonly Func<EmulatorOptions, bool> _launchOptionsRequested;
        private readonly Func<string, EmulatorOptions, bool> _buildAndLaunchRomRequested;
        private readonly Func<int, bool> _setExecutionSpeedRequested;
        private readonly Func<bool, bool> _setInstructionLoggingRequested;
        private readonly Func<bool, bool> _setPauseRequested;
        private readonly Func<ulong> _getProcessorCycleCountRequested;
        private readonly System.Action _signalVBlankRequested;
        private readonly Func<bool> _showDecompRequested;
        private readonly EmulatorOptions _currentOptions;
        private int _windowScale;
        private int _targetFps;
        private long _fpsSampleStartTimestamp;
        private int _fpsSampleFrameCount;
        private double _measuredFps;
        private long _cycleSampleStartTimestamp;
        private ulong _cycleSampleStartCount;
        private double _measuredCyclesPerSecond;
        private bool _showFpsOverlay;
        private bool _showCycleOverlay;
        private readonly string _currentRomPath;
        private readonly byte[] _framebufferData;
        private readonly GCHandle _framebufferHandle;
        private readonly ushort[] _vramSnapshot;
        private readonly ImageSurface _frameSurface;
        private readonly SurfacePattern _framePattern;
        private readonly Window _window;
        private readonly DrawingArea _drawingArea;
        private uint _frameTimerId;
        private Window? _memoryWindow;
        private TextView? _memoryTextView;
        private Entry? _memoryStartEntry;
        private Entry? _memoryLengthEntry;
        private uint _memoryTimerId;

        private bool _disposed;

        private static readonly uint[] PaletteArgb32 =
        [
            0xFF1A1C2C,
            0xFF5D275D,
            0xFFB13E53,
            0xFFEF7D57,
            0xFFFFCD75,
            0xFFA7F070,
            0xFF38B764,
            0xFF257179,
            0xFF29366F,
            0xFF3B5DC9,
            0xFF41A6F6,
            0xFF73EFF7,
            0xFFF4F4F4,
            0xFF94B0C2,
            0xFF566C86,
            0xFF333C57
        ];

        private static readonly ulong[] PackedPaletteArgb32 = CreatePackedPaletteArgb32();

        internal GraphicsRenderer(
            ContiguousMemory ram,
            EmulatorOptions options,
            Func<EmulatorOptions, bool> launchOptionsRequested,
            Func<string, EmulatorOptions, bool> buildAndLaunchRomRequested,
            Func<int, bool> setExecutionSpeedRequested,
            Func<bool, bool> setInstructionLoggingRequested,
            Func<bool, bool> setPauseRequested,
            Func<ulong> getProcessorCycleCountRequested,
            System.Action signalVBlankRequested,
            Func<bool> showDecompRequested)
        {
            int windowScale = options.WindowScale;
            int targetFps = options.TargetFps;
            if (windowScale <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowScale), "Window scale must be greater than zero.");

            if (targetFps <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetFps), "Target FPS must be greater than zero.");

            _ram = ram;
            _launchOptionsRequested = launchOptionsRequested;
            _buildAndLaunchRomRequested = buildAndLaunchRomRequested;
            _setExecutionSpeedRequested = setExecutionSpeedRequested;
            _setInstructionLoggingRequested = setInstructionLoggingRequested;
            _setPauseRequested = setPauseRequested;
            _getProcessorCycleCountRequested = getProcessorCycleCountRequested;
            _signalVBlankRequested = signalVBlankRequested;
            _showDecompRequested = showDecompRequested;
            _currentOptions = CloneOptions(options);
            _windowScale = windowScale;
            _targetFps = targetFps;
            _fpsSampleStartTimestamp = Stopwatch.GetTimestamp();
            _fpsSampleFrameCount = 0;
            _measuredFps = 0;
            _cycleSampleStartTimestamp = _fpsSampleStartTimestamp;
            _cycleSampleStartCount = 0;
            _measuredCyclesPerSecond = 0;
            _currentRomPath = _currentOptions.RomPath;
            _framebufferData = new byte[Width * Height * sizeof(uint)];
            _framebufferHandle = GCHandle.Alloc(_framebufferData, GCHandleType.Pinned);
            _vramSnapshot = new ushort[VramSizeInBytes];
            _frameSurface = new ImageSurface(_framebufferData, Format.ARGB32, Width, Height, Width * sizeof(uint));
            _framePattern = new SurfacePattern(_frameSurface)
            {
                Filter = Filter.Nearest
            };

            Gtk.Application.Init();

            _window = new Window("FoxVision Display")
            {
                Title = "FoxVision Display",
                DefaultWidth = Width * _windowScale,
                DefaultHeight = Height * _windowScale + 28,
                Resizable = false
            };
            _window.DeleteEvent += (_, _) => Gtk.Application.Quit();
            _window.AddEvents((int)(Gdk.EventMask.KeyPressMask | Gdk.EventMask.ButtonPressMask));
            _window.KeyPressEvent += OnKeyPressEvent;

            var headerBar = new HeaderBar
            {
                Title = "FoxVision Display",
                ShowCloseButton = true
            };

            var fileButton = new MenuButton
            {
                Label = "File",
                Relief = ReliefStyle.None,
                FocusOnClick = false,
                UseUnderline = false
            };
            fileButton.CanFocus = false;

            var fileMenu = new Gtk.Menu();
            var loadItem = new Gtk.MenuItem("Load ROM");
            loadItem.Activated += (_, _) => LoadRomFromDialog();
            fileMenu.Append(loadItem);

            var buildRomItem = new Gtk.MenuItem("Build ROM");
            buildRomItem.Activated += (_, _) => BuildRomFromDialog();
            fileMenu.Append(buildRomItem);

            var quitItem = new Gtk.MenuItem("Quit");
            quitItem.Activated += (_, _) => Gtk.Application.Quit();
            fileMenu.Append(quitItem);
            fileMenu.ShowAll();

            fileButton.Popup = fileMenu;
            headerBar.PackStart(fileButton);

            var emulatorButton = new MenuButton
            {
                Label = "Emulator",
                Relief = ReliefStyle.None,
                FocusOnClick = false,
                UseUnderline = false
            };
            emulatorButton.CanFocus = false;

            var emulatorMenu = new Gtk.Menu();

            var pauseItem = new CheckMenuItem("Pause or resume")
            {
                Active = false
            };
            pauseItem.Activated += (_, _) =>
            {
                bool paused = pauseItem.Active;
                _setPauseRequested(paused);
            };
            emulatorMenu.Append(pauseItem);

            emulatorMenu.Append(new SeparatorMenuItem());

            var executionSpeedItem = new Gtk.MenuItem("Set execution speed (hz)");
            executionSpeedItem.Activated += (_, _) => ShowExecutionSpeedDialog();
            emulatorMenu.Append(executionSpeedItem);

            emulatorMenu.ShowAll();
            emulatorButton.Popup = emulatorMenu;
            headerBar.PackStart(emulatorButton);

            var debugButton = new MenuButton
            {
                Label = "Debug",
                Relief = ReliefStyle.None,
                FocusOnClick = false,
                UseUnderline = false
            };
            debugButton.CanFocus = false;

            var debugMenu = new Gtk.Menu();

            var logInstructionItem = new CheckMenuItem("Log instruction")
            {
                Active = _currentOptions.LogInstruction
            };
            logInstructionItem.Activated += (_, _) =>
            {
                bool enabled = logInstructionItem.Active;
                if (_setInstructionLoggingRequested(enabled))
                {
                    _currentOptions.LogInstruction = enabled;
                }
            };
            debugMenu.Append(logInstructionItem);

            var liveMemoryItem = new Gtk.MenuItem("Live memory");
            liveMemoryItem.Activated += (_, _) => ShowLiveMemoryWindow();
            debugMenu.Append(liveMemoryItem);

            var decompItem = new Gtk.MenuItem("Decompile to Console");
            decompItem.Activated += (_, _) => _showDecompRequested();
            debugMenu.Append(decompItem);

            debugMenu.Append(new SeparatorMenuItem());

            var showFpsItem = new Gtk.MenuItem("Show FPS");
            showFpsItem.Activated += (_, _) => ToggleFpsOverlay();
            debugMenu.Append(showFpsItem);

            var showCycleItem = new Gtk.MenuItem("Show actual clock cycle (over rated cycles)");
            showCycleItem.Activated += (_, _) => ToggleClockCycleOverlay();
            debugMenu.Append(showCycleItem);

            debugMenu.ShowAll();
            debugButton.Popup = debugMenu;
            headerBar.PackStart(debugButton);

            var settingsButton = new MenuButton
            {
                Label = "Settings",
                Relief = ReliefStyle.None,
                FocusOnClick = false,
                UseUnderline = false
            };
            settingsButton.CanFocus = false;

            var settingsMenu = new Gtk.Menu();

            var targetFpsItem = new Gtk.MenuItem("Target FPS");
            targetFpsItem.Activated += (_, _) => ShowTargetFpsDialog();
            settingsMenu.Append(targetFpsItem);

            var windowScaleItem = new Gtk.MenuItem("Window Scale");
            windowScaleItem.Activated += (_, _) => ShowWindowScaleDialog();
            settingsMenu.Append(windowScaleItem);

            var controllerConfigItem = new Gtk.MenuItem("Controller Configuration");
            controllerConfigItem.Activated += (_, _) => ShowControllerConfigurationDialog();
            settingsMenu.Append(controllerConfigItem);

            var saveInputConfigItem = new Gtk.MenuItem("Save Input Configuration");
            saveInputConfigItem.Activated += (_, _) => SaveInputConfiguration();
            settingsMenu.Append(saveInputConfigItem);

            settingsMenu.ShowAll();
            settingsButton.Popup = settingsMenu;
            headerBar.PackStart(settingsButton);

            headerBar.Title = string.IsNullOrWhiteSpace(_currentRomPath)
                ? "FoxVision"
                : $"FoxVision | {System.IO.Path.GetFileName(_currentRomPath)}";

            _drawingArea = new DrawingArea
            {
                Hexpand = true,
                Vexpand = true
            };
            _drawingArea.CanFocus = true;
            _drawingArea.AddEvents((int)(Gdk.EventMask.KeyPressMask | Gdk.EventMask.ButtonPressMask));
            _drawingArea.SetSizeRequest(Width * _windowScale, Height * _windowScale);
            _drawingArea.Drawn += OnDrawn;
            _drawingArea.KeyPressEvent += OnKeyPressEvent;
            _drawingArea.ButtonPressEvent += (_, _) => _drawingArea.GrabFocus();
            _window.ButtonPressEvent += (_, _) => _drawingArea.GrabFocus();

            var outerBox = new Box(Orientation.Vertical, 0);
            outerBox.PackStart(_drawingArea, true, true, 0);

            _window.Titlebar = headerBar;
            _window.Add(outerBox);
            _window.ShowAll();
            _drawingArea.GrabFocus();

            // Controller state is exposed as a memory-mapped byte at 0x1000.
            _ram.WriteUnchecked(ControllerStateAddress, 0);

            _frameTimerId = GLib.Timeout.Add((uint)Math.Max(1, 1000 / _targetFps), OnFrameTick);

            Console.WriteLine($"GTK graphics renderer initialised ({Width}x{Height}, VRAM 0x{VramEndAddress:X4}-0x{VramStartAddress:X4})");
        }

        internal void Run()
        {
            Gtk.Application.Run();
        }

        private bool OnFrameTick()
        {
            if (_disposed || !_window.Visible)
            {
                return false;
            }

            _signalVBlankRequested();
            UpdateMeasuredFps();
            UpdateMeasuredCyclesPerSecond();
            _drawingArea.QueueDraw();
            return true;
        }

        private void OnDrawn(object? sender, DrawnArgs args)
        {
            if (_disposed)
            {
                return;
            }

            CopyFrameFromVram();
            DrawFrame(args.Cr);
        }

        private void DrawFrame(Context context)
        {
            context.Save();
            context.Scale(_windowScale, _windowScale);
            context.SetSource(_framePattern);
            context.Paint();

            DrawOverlay(context);
            context.Restore();
        }

        private void DrawOverlay(Context context)
        {
            if (!_showFpsOverlay && !_showCycleOverlay)
            {
                return;
            }

            List<string> overlayLines = [];
            if (_showFpsOverlay)
            {
                overlayLines.Add($"FPS: {_measuredFps:F2}");
            }

            if (_showCycleOverlay)
            {
                overlayLines.Add($"Cycle: {_measuredCyclesPerSecond:N0} / {_currentOptions.ExecutionSpeedHz:N0} Hz");
            }

            const double fontSize = 4.0;
            const double padding = 1.5;
            const double lineSpacing = 0.0;

            context.Save();
            context.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            context.SetFontSize(fontSize);

            var fontExtents = context.FontExtents;
            double lineHeight = fontExtents.Ascent + fontExtents.Descent + lineSpacing;

            double maxWidth = 0;
            foreach (string line in overlayLines)
            {
                var extents = context.TextExtents(line);
                if (extents.Width > maxWidth)
                {
                    maxWidth = extents.Width;
                }
            }

            double boxWidth = maxWidth + (padding * 2);
            double boxHeight = (lineHeight * overlayLines.Count) + padding;
            double x = Width - boxWidth - 1.0;
            double y = 1.0;

            context.SetSourceRGBA(0.0, 0.0, 0.0, 0.45);
            context.Rectangle(x, y, boxWidth, boxHeight);
            context.Fill();

            context.SetSourceRGBA(1.0, 1.0, 1.0, 0.95);
            double textY = y + padding + fontExtents.Ascent;
            foreach (string line in overlayLines)
            {
                var extents = context.TextExtents(line);
                context.MoveTo(x + boxWidth - padding - extents.Width - extents.XBearing, textY);
                context.ShowText(line);
                textY += lineHeight;
            }

            context.Restore();
        }

        private void CopyFrameFromVram()
        {
            _frameSurface.Flush();

            _ram.CopyDescendingUnchecked(VramStartAddress, _vramSnapshot);

            Span<ulong> framebuffer = MemoryMarshal.Cast<byte, ulong>(_framebufferData.AsSpan());
            int vectorWidth = Vector<ulong>.Count;
            int packedIndex = 0;
            Span<ulong> vectorBuffer = stackalloc ulong[Vector<ulong>.Count];

            while (packedIndex <= _vramSnapshot.Length - vectorWidth)
            {
                for (int lane = 0; lane < vectorWidth; lane++)
                {
                    vectorBuffer[lane] = PackedPaletteArgb32[(byte)_vramSnapshot[packedIndex + lane]];
                }

                new Vector<ulong>(vectorBuffer).CopyTo(framebuffer.Slice(packedIndex, vectorWidth));
                packedIndex += vectorWidth;
            }

            for (; packedIndex < _vramSnapshot.Length; packedIndex++)
            {
                framebuffer[packedIndex] = PackedPaletteArgb32[(byte)_vramSnapshot[packedIndex]];
            }

            _frameSurface.MarkDirty();
        }

        private static ulong[] CreatePackedPaletteArgb32()
        {
            var packedPalette = new ulong[256];
            for (int packedPixels = 0; packedPixels < packedPalette.Length; packedPixels++)
            {
                uint highPixel = PaletteArgb32[(packedPixels >> 4) & 0x0F];
                uint lowPixel = PaletteArgb32[packedPixels & 0x0F];
                packedPalette[packedPixels] = lowPixel | ((ulong)highPixel << 32);
            }

            return packedPalette;
        }

        private void LoadRomFromDialog()
        {
            var dialog = new FileChooserDialog(
                "Load ROM",
                _window,
                FileChooserAction.Open,
                "Cancel",
                ResponseType.Cancel,
                "Load",
                ResponseType.Accept);

            dialog.SetCurrentFolder(Environment.CurrentDirectory);

            var response = (ResponseType)dialog.Run();
            if (response == ResponseType.Accept)
            {
                var selectedPath = dialog.Filename;
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    var updatedOptions = CloneOptions(_currentOptions);
                    updatedOptions.RomPath = selectedPath;
                    if (_launchOptionsRequested(updatedOptions))
                    {
                        _currentOptions.RomPath = selectedPath;
                    }
                }
            }

            dialog.Destroy();
        }

        private void BuildRomFromDialog()
        {
            var dialog = new FileChooserDialog(
                "Build ROM From Source",
                _window,
                FileChooserAction.Open,
                "Cancel",
                ResponseType.Cancel,
                "Build & Run",
                ResponseType.Accept);

            var sourceFilter = new FileFilter
            {
                Name = "Fox sources (*.f16, *.fc)"
            };
            sourceFilter.AddPattern("*.f16");
            sourceFilter.AddPattern("*.fc");
            dialog.AddFilter(sourceFilter);

            dialog.SetCurrentFolder(Environment.CurrentDirectory);

            var response = (ResponseType)dialog.Run();
            if (response == ResponseType.Accept)
            {
                var selectedPath = dialog.Filename;
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    if (_buildAndLaunchRomRequested(selectedPath, CloneOptions(_currentOptions)))
                    {
                        _currentOptions.RomPath = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(selectedPath)) ?? Environment.CurrentDirectory,
                            "vfox16.bin");
                    }
                }
            }

            dialog.Destroy();
        }

        private void ShowTargetFpsDialog()
        {
            ShowPositiveIntDialog(
                "Target FPS",
                "Target FPS",
                _currentOptions.TargetFps,
                value =>
                {
                    _currentOptions.TargetFps = value;
                    ApplyTargetFps(value);
                    return true;
                });
        }

        private void ShowWindowScaleDialog()
        {
            ShowPositiveIntDialog(
                "Window Scale",
                "Window Scale",
                _currentOptions.WindowScale,
                value =>
                {
                    _currentOptions.WindowScale = value;
                    ApplyWindowScale(value);
                    return true;
                });
        }

        private void ShowExecutionSpeedDialog()
        {
            ShowPositiveIntDialog(
                "Execution Speed",
                "Execution speed (Hz)",
                _currentOptions.ExecutionSpeedHz,
                value =>
                {
                    if (_setExecutionSpeedRequested(value))
                    {
                        _currentOptions.ExecutionSpeedHz = value;
                        return true;
                    }

                    return false;
                });
        }

        private void ApplyTargetFps(int targetFps)
        {
            _targetFps = targetFps;

            if (_frameTimerId != 0)
                GLib.Source.Remove(_frameTimerId);

            _frameTimerId = GLib.Timeout.Add((uint)Math.Max(1, 1000 / _targetFps), OnFrameTick);
        }

        private void ApplyWindowScale(int windowScale)
        {
            _windowScale = windowScale;
            int width = Width * _windowScale;
            int height = Height * _windowScale;

            _drawingArea.SetSizeRequest(width, height);
            _window.SetDefaultSize(width, height + 28);
            _window.Resize(width, height + 28);
            _drawingArea.QueueDraw();
        }

        private void UpdateMeasuredFps()
        {
            _fpsSampleFrameCount++;
            long nowTimestamp = Stopwatch.GetTimestamp();
            long elapsedTicks = nowTimestamp - _fpsSampleStartTimestamp;
            if (elapsedTicks < Stopwatch.Frequency)
            {
                return;
            }

            _measuredFps = _fpsSampleFrameCount * (double)Stopwatch.Frequency / elapsedTicks;
            _fpsSampleFrameCount = 0;
            _fpsSampleStartTimestamp = nowTimestamp;
        }

        private void UpdateMeasuredCyclesPerSecond()
        {
            ulong currentCycleCount = _getProcessorCycleCountRequested();
            long nowTimestamp = Stopwatch.GetTimestamp();
            long elapsedTicks = nowTimestamp - _cycleSampleStartTimestamp;
            if (elapsedTicks < Stopwatch.Frequency)
            {
                return;
            }

            ulong cycleDelta = currentCycleCount - _cycleSampleStartCount;
            _measuredCyclesPerSecond = cycleDelta * (double)Stopwatch.Frequency / elapsedTicks;
            _cycleSampleStartCount = currentCycleCount;
            _cycleSampleStartTimestamp = nowTimestamp;
        }

        private void ToggleFpsOverlay()
        {
            _showFpsOverlay = !_showFpsOverlay;
            _drawingArea.QueueDraw();
        }

        private void ToggleClockCycleOverlay()
        {
            _showCycleOverlay = !_showCycleOverlay;
            _drawingArea.QueueDraw();
        }

        private void OnKeyPressEvent(object? sender, KeyPressEventArgs args)
        {
            if (TryMapKeyToControllerMask(args.Event.KeyValue, out var buttonMask))
            {
                LatchControllerButton(buttonMask);
            }
        }

        private void LatchControllerButton(byte buttonMask)
        {
            // Controller input is latched: key press sets the corresponding bit,
            // and release does not clear it. ROM code must clear 0x1000 explicitly.
            byte currentState = (byte)(_ram.ReadUnchecked(ControllerStateAddress) & 0x00FF);
            byte nextState = (byte)(currentState | buttonMask);
            if (nextState != currentState)
            {
                _ram.WriteUnchecked(ControllerStateAddress, nextState);
            }
        }

        private bool TryMapKeyToControllerMask(uint keyValue, out byte buttonMask)
        {
            uint normalizedKey = NormalizeKeyValue(keyValue);
            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerUpKey))
            {
                buttonMask = ControllerUpMask;
                return true;
            }

            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerDownKey))
            {
                buttonMask = ControllerDownMask;
                return true;
            }

            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerLeftKey))
            {
                buttonMask = ControllerLeftMask;
                return true;
            }

            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerRightKey))
            {
                buttonMask = ControllerRightMask;
                return true;
            }

            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerAKey))
            {
                buttonMask = ControllerAMask;
                return true;
            }

            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerBKey))
            {
                buttonMask = ControllerBMask;
                return true;
            }

            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerStartKey))
            {
                buttonMask = ControllerStartMask;
                return true;
            }

            if (normalizedKey == NormalizeKeyValue(_currentOptions.ControllerSelectKey))
            {
                buttonMask = ControllerSelectMask;
                return true;
            }

            buttonMask = 0;
            return false;
        }

        private static uint NormalizeKeyValue(uint keyValue)
        {
            if (keyValue >= (uint)Gdk.Key.A && keyValue <= (uint)Gdk.Key.Z)
            {
                return keyValue + ((uint)Gdk.Key.a - (uint)Gdk.Key.A);
            }

            return keyValue;
        }

        private void ShowControllerConfigurationDialog()
        {
            var dialog = new Dialog("Controller Configuration", _window, DialogFlags.Modal)
            {
                Resizable = false,
                DefaultWidth = 420
            };

            dialog.AddButton("Close", ResponseType.Close);

            var content = dialog.ContentArea;
            var grid = new Grid
            {
                ColumnSpacing = 10,
                RowSpacing = 8,
                MarginTop = 12,
                MarginBottom = 12,
                MarginStart = 12,
                MarginEnd = 12
            };

            int row = 0;
            foreach (var button in ControllerButtons)
            {
                var buttonName = GetControllerButtonName(button);
                var mappingLabel = new Label(GetKeyDisplayName(GetControllerKey(_currentOptions, button)))
                {
                    Halign = Align.Start
                };

                var assignButton = new Button("Assign Key");
                assignButton.Clicked += (_, _) => CaptureControllerKey(dialog, button, mappingLabel);

                grid.Attach(new Label(buttonName) { Halign = Align.Start }, 0, row, 1, 1);
                grid.Attach(mappingLabel, 1, row, 1, 1);
                grid.Attach(assignButton, 2, row, 1, 1);

                row++;
            }

            content.Add(grid);
            dialog.ShowAll();
            dialog.Run();
            dialog.Destroy();
        }

        private void SaveInputConfiguration()
        {
            try
            {
                _currentOptions.SaveInputConfig();
                ShowInfo(_window, "Input configuration saved.");
            }
            catch (Exception ex)
            {
                ShowError(_window, $"Failed to save input configuration: {ex.Message}");
            }
        }

        private void CaptureControllerKey(Window parent, ControllerButton button, Label mappingLabel)
        {
            var captureDialog = new Dialog($"Assign {GetControllerButtonName(button)}", parent, DialogFlags.Modal)
            {
                Resizable = false,
                DefaultWidth = 320
            };

            captureDialog.AddButton("Cancel", ResponseType.Cancel);
            captureDialog.AddEvents((int)Gdk.EventMask.KeyPressMask);

            captureDialog.ContentArea.Add(new Label("Press any key to bind this controller button.")
            {
                MarginTop = 16,
                MarginBottom = 16,
                MarginStart = 16,
                MarginEnd = 16,
                Wrap = true
            });

            captureDialog.KeyPressEvent += (_, args) =>
            {
                var keyValue = args.Event.KeyValue;
                if ((Gdk.Key)keyValue == Gdk.Key.Escape)
                {
                    captureDialog.Respond(ResponseType.Cancel);
                    args.RetVal = true;
                    return;
                }

                SetControllerKey(_currentOptions, button, keyValue);
                mappingLabel.Text = GetKeyDisplayName(GetControllerKey(_currentOptions, button));
                captureDialog.Respond(ResponseType.Ok);
                args.RetVal = true;
            };

            captureDialog.ShowAll();
            captureDialog.Run();
            captureDialog.Destroy();
        }

        private static uint GetControllerKey(EmulatorOptions options, ControllerButton button)
        {
            return button switch
            {
                ControllerButton.Up => options.ControllerUpKey,
                ControllerButton.Down => options.ControllerDownKey,
                ControllerButton.Left => options.ControllerLeftKey,
                ControllerButton.Right => options.ControllerRightKey,
                ControllerButton.A => options.ControllerAKey,
                ControllerButton.B => options.ControllerBKey,
                ControllerButton.Start => options.ControllerStartKey,
                ControllerButton.Select => options.ControllerSelectKey,
                _ => 0
            };
        }

        private static void SetControllerKey(EmulatorOptions options, ControllerButton button, uint keyValue)
        {
            uint normalizedKey = NormalizeKeyValue(keyValue);
            switch (button)
            {
                case ControllerButton.Up:
                    options.ControllerUpKey = normalizedKey;
                    break;
                case ControllerButton.Down:
                    options.ControllerDownKey = normalizedKey;
                    break;
                case ControllerButton.Left:
                    options.ControllerLeftKey = normalizedKey;
                    break;
                case ControllerButton.Right:
                    options.ControllerRightKey = normalizedKey;
                    break;
                case ControllerButton.A:
                    options.ControllerAKey = normalizedKey;
                    break;
                case ControllerButton.B:
                    options.ControllerBKey = normalizedKey;
                    break;
                case ControllerButton.Start:
                    options.ControllerStartKey = normalizedKey;
                    break;
                case ControllerButton.Select:
                    options.ControllerSelectKey = normalizedKey;
                    break;
            }
        }

        private static string GetControllerButtonName(ControllerButton button)
        {
            return button switch
            {
                ControllerButton.Up => "Up",
                ControllerButton.Down => "Down",
                ControllerButton.Left => "Left",
                ControllerButton.Right => "Right",
                ControllerButton.A => "A",
                ControllerButton.B => "B",
                ControllerButton.Start => "Start",
                ControllerButton.Select => "Select",
                _ => "Unknown"
            };
        }

        private static string GetKeyDisplayName(uint keyValue)
        {
            uint normalizedKey = NormalizeKeyValue(keyValue);
            var keyName = Gdk.Keyval.Name(normalizedKey);
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return $"0x{normalizedKey:X}";
            }

            if (keyName.Length == 1)
            {
                return keyName.ToUpperInvariant();
            }

            return keyName.Replace("_", " ");
        }

        private void ShowPositiveIntDialog(string title, string fieldLabel, int currentValue, Func<int, bool> applyValue)
        {
            var dialog = new Dialog(title, _window, DialogFlags.Modal)
            {
                Resizable = false,
                DefaultWidth = 360
            };

            dialog.AddButton("Cancel", ResponseType.Cancel);
            dialog.AddButton("Apply", ResponseType.Ok);

            var content = dialog.ContentArea;
            var grid = new Grid
            {
                ColumnSpacing = 10,
                RowSpacing = 8,
                MarginTop = 12,
                MarginBottom = 12,
                MarginStart = 12,
                MarginEnd = 12
            };

            var valueEntry = new Entry(currentValue.ToString()) { Hexpand = true };
            grid.Attach(new Label(fieldLabel) { Halign = Align.Start }, 0, 0, 1, 1);
            grid.Attach(valueEntry, 1, 0, 1, 1);

            content.Add(grid);
            dialog.ShowAll();

            while (true)
            {
                var result = (ResponseType)dialog.Run();
                if (result != ResponseType.Ok)
                {
                    dialog.Destroy();
                    return;
                }

                if (!int.TryParse(valueEntry.Text, out var value) || value <= 0)
                {
                    ShowError(dialog, $"{fieldLabel} must be a positive integer.");
                    continue;
                }

                if (applyValue(value))
                {
                    dialog.Destroy();
                    return;
                }

                dialog.Destroy();
                return;
            }
        }

        private void ShowLiveMemoryWindow()
        {
            if (_memoryWindow is not null)
            {
                _memoryWindow.Present();
                return;
            }

            var memoryWindow = new Window("FoxVision Live Memory")
            {
                DefaultWidth = 700,
                DefaultHeight = 500,
                Resizable = true
            };

            var layout = new Box(Orientation.Vertical, 8)
            {
                MarginTop = 10,
                MarginBottom = 10,
                MarginStart = 10,
                MarginEnd = 10
            };

            var controls = new Box(Orientation.Horizontal, 8);
            _memoryStartEntry = new Entry("$0000") { WidthChars = 10 };
            _memoryLengthEntry = new Entry("128") { WidthChars = 8 };

            controls.PackStart(new Label("Start") { Halign = Align.Start }, false, false, 0);
            controls.PackStart(_memoryStartEntry, false, false, 0);
            controls.PackStart(new Label("Words") { Halign = Align.Start }, false, false, 0);
            controls.PackStart(_memoryLengthEntry, false, false, 0);

            var refreshNowButton = new Button("Refresh now");
            refreshNowButton.Clicked += (_, _) => RefreshMemoryWindow();
            controls.PackEnd(refreshNowButton, false, false, 0);

            _memoryTextView = new TextView
            {
                Editable = false,
                Monospace = true,
                CursorVisible = false,
                WrapMode = WrapMode.None
            };

            var scroller = new ScrolledWindow();
            scroller.Add(_memoryTextView);

            layout.PackStart(controls, false, false, 0);
            layout.PackStart(scroller, true, true, 0);

            memoryWindow.Add(layout);
            memoryWindow.DeleteEvent += (_, _) => CloseMemoryWindow();
            memoryWindow.Destroyed += (_, _) => CloseMemoryWindow();

            _memoryWindow = memoryWindow;
            _memoryTimerId = GLib.Timeout.Add(200, () =>
            {
                if (_disposed || _memoryWindow is null)
                    return false;

                RefreshMemoryWindow();
                return true;
            });

            RefreshMemoryWindow();
            memoryWindow.ShowAll();
        }

        private void RefreshMemoryWindow()
        {
            if (_memoryTextView is null || _memoryStartEntry is null || _memoryLengthEntry is null)
                return;

            if (!TryParseUShortInput(_memoryStartEntry.Text, out ushort startAddress))
            {
                _memoryTextView.Buffer.Text = "Invalid start address. Use decimal, 0xFFFF, or $FFFF.";
                return;
            }

            if (!int.TryParse(_memoryLengthEntry.Text, out int wordCount) || wordCount <= 0)
            {
                _memoryTextView.Buffer.Text = "Invalid word count. Enter a positive integer.";
                return;
            }

            wordCount = Math.Min(wordCount, 4096);
            var sb = new StringBuilder(wordCount * 8);
            ushort current = startAddress;
            for (int i = 0; i < wordCount; i++)
            {
                if (i % 8 == 0)
                {
                    if (i > 0)
                        sb.AppendLine();
                    sb.Append($"${current:X4}: ");
                }

                ushort value = _ram.ReadUnchecked(current);
                sb.Append($"{value:X4} ");

                if (current == _ram.MaxAddress)
                    break;

                current++;
            }

            _memoryTextView.Buffer.Text = sb.ToString().TrimEnd();
        }

        private void CloseMemoryWindow()
        {
            if (_memoryTimerId != 0)
            {
                GLib.Source.Remove(_memoryTimerId);
                _memoryTimerId = 0;
            }

            _memoryWindow = null;
            _memoryTextView = null;
            _memoryStartEntry = null;
            _memoryLengthEntry = null;
        }

        private static bool TryParseUShortInput(string? text, out ushort value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            if (trimmed.StartsWith("$", StringComparison.Ordinal))
                return ushort.TryParse(trimmed[1..], System.Globalization.NumberStyles.HexNumber, null, out value);

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out value);

            return ushort.TryParse(trimmed, out value);
        }

        private static EmulatorOptions CloneOptions(EmulatorOptions options)
        {
            return new EmulatorOptions
            {
                RomPath = options.RomPath,
                WindowScale = options.WindowScale,
                TargetFps = options.TargetFps,
                ExecutionSpeedHz = options.ExecutionSpeedHz,
                LogInstruction = options.LogInstruction,
                RomPreviewWords = options.RomPreviewWords,
                ControllerUpKey = options.ControllerUpKey,
                ControllerDownKey = options.ControllerDownKey,
                ControllerLeftKey = options.ControllerLeftKey,
                ControllerRightKey = options.ControllerRightKey,
                ControllerAKey = options.ControllerAKey,
                ControllerBKey = options.ControllerBKey,
                ControllerStartKey = options.ControllerStartKey,
                ControllerSelectKey = options.ControllerSelectKey
            };
        }

        private static void ShowError(Window parent, string message)
        {
            var errorDialog = new MessageDialog(parent, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok, message);
            errorDialog.Run();
            errorDialog.Destroy();
        }

        private static void ShowInfo(Window parent, string message)
        {
            var infoDialog = new MessageDialog(parent, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok, message);
            infoDialog.Run();
            infoDialog.Destroy();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_frameTimerId != 0)
                GLib.Source.Remove(_frameTimerId);

            if (_memoryWindow is not null)
                _memoryWindow.Destroy();

            if (_memoryTimerId != 0)
                GLib.Source.Remove(_memoryTimerId);
            _framePattern.Dispose();
            _frameSurface.Dispose();
            if (_framebufferHandle.IsAllocated)
                _framebufferHandle.Free();
            _window.Destroy();
        }
    }
}