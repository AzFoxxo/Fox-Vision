namespace FoxVision.Components
{
    /*
    *   This class is stores a copy of the memory used
    *   to draw the current frame.
    */
    internal class GraphicsUnit
    {
        private readonly ContiguousMemory _memory;
        private const uint defaultWidth = 192;
        private const uint defaultHeight = 108;
        private const uint maxMemorySize = 0xFFFF / 2; // 32KB limit
        private readonly uint _width;
        private readonly uint _height;

        /// <summary>
        /// Create a new graphics unit based on the screen size
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        internal GraphicsUnit(uint width, uint height)
        {
            // Size screen size
            _width = width > 0 ? width : defaultWidth;
            _height = height > 0 ? height : defaultHeight;

            // Calculate the size of the memory block
            var bytes = (width * height) / 2;

            // Check memory size is valid
            if (bytes > maxMemorySize)
                throw new ArgumentOutOfRangeException("Graphics memory size is too large! Reduce screen width and height to fit within 32kb (half RAM size) limit.");

            _memory = new ContiguousMemory(bytes);

            Console.WriteLine("Created graphics unit");
        }

        /// <summary>
        /// Split pixel data from byte to two pixels
        /// </summary>
        /// <param name="pixel"></param>
        /// <returns></returns>
        private static Tuple<byte, byte> SplitPixels(byte pixel)
        {
            // Split byte in half
            byte low = (byte)(pixel & 0x0F);
            byte high = (byte)(pixel >> 4);

            return new Tuple<byte, byte>(low, high);
        }

        internal Pixel GetPixel(byte pixelByte)
        {
            switch (pixelByte)
            {
                // Black
                case 0x00:
                    return new(26, 28, 44);
                // Purple
                case 0x01:
                    return new(93, 39, 93);
                // Red
                case 0x02:
                    return new(177, 62, 83);
                // Orange
                case 0x03:
                    return new(239, 125, 87);
                // Yellow
                case 0x04:
                    return new(255, 205, 117);
                // Light green
                case 0x05:
                    return new(167, 240, 112);
                // Green
                case 0x06:
                    return new(56, 183, 100);
                // Dark green
                case 0x07:
                    return new(37, 113, 121);
                // Dark blue
                case 0x08:
                    return new(41, 54, 111);
                // Blue
                case 0x09:
                    return new(59, 93, 201);
                // Light blue
                case 0x0A:
                    return new(65, 166, 246);
                // Turquoise
                case 0x0B:
                    return new(115, 239, 247);
                // White
                case 0x0C:
                    return new(244, 244, 244);
                // Light grey
                case 0x0D:
                    return new(148, 176, 194);
                // Grey
                case 0x0E:
                    return new(86, 108, 134);
                // Dark grey
                case 0x0F:
                    return new(51, 60, 87);
                default:
                    return new(0, 0, 0);
            }
        }
    }


    /// <summary>
    /// Represents a single pixel with a red, green and blue colour channel
    /// </summary>
    internal readonly struct Pixel
    {
        internal readonly byte red;
        internal readonly byte green;
        internal readonly byte blue;

        internal Pixel(byte red, byte green, byte blue)
        {
            this.red = red;
            this.green = green;
            this.blue = blue;
        }
    }
}