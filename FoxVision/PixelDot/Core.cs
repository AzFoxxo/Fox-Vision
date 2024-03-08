/*
 *   Copyright (c) 2024 Az Foxxo (@AzFoxxo on GitHub)
 *   All rights reserved.

 *   Permission is hereby granted, free of charge, to any person obtaining a copy
 *   of this software and associated documentation files (the "Software"), to deal
 *   in the Software without restriction, including without limitation the rights
 *   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *   copies of the Software, and to permit persons to whom the Software is
 *   furnished to do so, subject to the following conditions:
 
 *   The above copyright notice and this permission notice shall be included in all
 *   copies or substantial portions of the Software.
 
 *   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *   SOFTWARE.
 */

// Imports
using SFML.Graphics;
using SFML.System;
using SFML.Window;

namespace PixelDot {
    public class Core {
        // Window
        public static RenderWindow? Window { get; private set; }

        // Pixel scale
        public static int PixelScale { get; private set;}

        // Background colour
        public static Color BackgroundColor { get; set; } = Palette.GetColour(0);

        // Screen width and height
        public static int ScreenWidth => (int)Window!.Size.X / PixelScale;
        public static int ScreenHeight => (int)Window!.Size.Y / PixelScale;

        /// <summary> Main entry point of the application </summary>
        /// <param name="args"> Arguments passed to the application </param>
        /// <returns> Nothing </returns>
        public static void Entry(string[] args) {
            #region Argument Checking
            // Check one argument has been provided (ROM)
            if (args.Length < 3) {
                Console.WriteLine("Not enough arguments provided\nUsage: PixelDot.exe <width> <height> <scale>");
                return;
            }

            // First two args are width and height
            var width = int.Parse(args[0]);
            var height = int.Parse(args[1]);

            // Third arg is scale
            PixelScale = int.Parse(args[2]);
            PixelScale = PixelScale < 1 ? 1 : PixelScale;

            // Size restrictions
            if (width < 100) {
                width = 100;
            } else if (width > VideoMode.DesktopMode.Width) {
                width = (int)VideoMode.DesktopMode.Width;
            }
            if (height < 50) {
                height = 50;
            } else if (height > VideoMode.DesktopMode.Height) {
                height = (int)VideoMode.DesktopMode.Height;
            }
            #endregion

            #region Window Creation
            // Create the window
            Window = new RenderWindow(new VideoMode((uint) width, (uint) height), "PixelDot", Styles.Titlebar | Styles.Close);
            Window.Closed += (sender, e) => Window.Close();
            Window.SetTitle($"Viso Fox 16");
            #endregion

            // Enter the main loop
            while (Window.IsOpen) {
                // Current time
                var time = DateTime.Now;

                // Process events
                Window.DispatchEvents();

                // Check if the window has been closed
                if (!Window.IsOpen) {
                    break;
                }

                // Clear screen
                Window.Clear(BackgroundColor);

                // Read the data
                ushort[] VRAM;
                if (FoxVision.VirtualMachine.Instance != null) {
                    // Copy the memory from the VRAM
                    VRAM = FoxVision.VirtualMachine.Instance.GetGPUMemory();
                    
                    // Console.WriteLine($"Size of VRAM {VRAM.Length}");

                    int x = 0, y = 0;
                    for (int position = 0; position < VRAM.Length; position++)
                    {
                        var data = VRAM[position];
                        byte byte1 = (byte)(data & 0xFF);          // First byte
                        byte byte2 = (byte)((data >> 8) & 0xFF);   // Second byte
                        byte byte3 = (byte)((data >> 16) & 0xFF);  // Third byte
                        byte byte4 = (byte)((data >> 24) & 0xFF);  // Fourth byte

                        // Console.Write($"B1 {byte1}\tB2 {byte2}\tB3 {byte3}\tB4 {byte4}\tDT: {data:X4}\n");

                        for (int i = 0; i < 4; i++)
                        {
                            if (x >= 100) { x = 0; y++; }
                            byte b = 0;
                            if (i == 0) b = byte1;
                            if (i == 1) b = byte2;
                            if (i == 2) b = byte3;
                            if (i == 3) b = byte4;
                            var random = new Random();
                            DrawPixel(x, y, Palette.GetColour(b));
                            x++;
                        }
                    }
                }

                // Update the display
                Window.Display();

                // Check at least 60ms have passed since last frame
                var timePassed = DateTime.Now - time;
                if (timePassed.TotalMilliseconds < 16.666666666666666666666666666667) {
                    // Sleep for the remaining time
                    Thread.Sleep((int)(16.666666666666666666666666666667 - timePassed.TotalMilliseconds));
                }
            }
        }

        // Clean-up the application
        ~Core() {
            // Close the window
            Window?.Close();

            // Dispose of the window
            Window?.Dispose();
        }

        /// <summary> Draw pixel on screen </summary>
        /// <param name="x"> X position of pixel </param>
        /// <param name="y"> Y position of pixel </param>
        /// <param name="color"> Color of pixel </param>
        /// <param name="width"> Width of pixel </param>
        /// <param name="height"> Height of pixel </param>
        public static void DrawPixel(int x, int y, Color color, int width=1, int height=1) {
            // Draw the rectangle
            var rectangle = new RectangleShape(new Vector2f(width * PixelScale, height * PixelScale))
            {
                Position = new Vector2f(x * PixelScale, y * PixelScale),
                FillColor = color
            };
            Window?.Draw(rectangle);
        }
    }
}
