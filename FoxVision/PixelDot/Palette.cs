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

namespace PixelDot
{
    /// <summary> All supported colours </summary>
    public readonly struct Palette
    {
        /// <summary>
        /// Get a colour from the palette (wraps around if index is out of range)
        /// </summary>
        /// <param name="index"></param>
        /// <returns>Returns the colour</returns>
        public static SFML.Graphics.Color GetColour(byte index) => Colours[index % Colours.Length];

        // Colour palette is https://lospec.com/palette-list/sweetie-16
        public static readonly SFML.Graphics.Color[] Colours =
            {
                // RGB COLOUR          VM VAL   NAME
                new(0x1a, 0x1c, 0x2c), // 0x0 - Black
                new(0x5d, 0x27, 0x5d), // 0x1 - Purple
                new(0xb1, 0x3e, 0x53), // 0x2 - Red
                new(0xef, 0x7d, 0x57), // 0x3 - Orange
                new(0xff, 0xcd, 0x75), // 0x4 - Yellow
                new(0xa7, 0xf0, 0x70), // 0x5 - Light Green
                new(0x38, 0xb7, 0x64), // 0x6 - Green
                new(0x25, 0x71, 0x79), // 0x7 - Dark Green
                new(0x29, 0x36, 0x6f), // 0x8 - Dark Blue
                new(0x3b, 0x5d, 0xc9), // 0x9 - Blue
                new(0x41, 0xa6, 0xf6), // 0xA - Light Blue
                new(0x73, 0xef, 0xf7), // 0xB - Turquoise
                new(0xf4, 0xf4, 0xf4), // 0xC - White
                new(0x94, 0xb0, 0xc2), // 0xD - Light Grey
                new(0x56, 0x6c, 0x86), // 0xE - Grey
                new(0x33, 0x3c, 0x57) // 0xF - Dark Grey
        };
    }
}
