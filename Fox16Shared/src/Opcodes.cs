namespace Fox16Shared
{
    /// <summary>
    /// Opcodes dictionary
    /// Contains the short opcode string representation and opcode value
    /// </summary>
    public class Opcodes
    {
        public const ushort PPU_OFFSET = 0xF000;
        public const ushort DEBUG_EXTENSION_OFFSET = 0xC000;
        public static Dictionary<string, ushort> instructions = new()
        {
            // CPU opcodes
            {"NOP", 0},
            {"LFM", 1},
            {"WTM", 2},
            {"SRA", 3},
            {"AXY", 4},
            {"SXY", 5},
            {"MXY", 6},
            {"DXY", 7},
            {"EQU", 8},
            {"LEQ", 9},
            {"JPZ", 10},
            {"JNZ", 11},
            {"JMP", 12},
            {"CLR", 13},
            {"HLT", 14},
            {"BSL", 15},
            {"BSR", 16},
            {"AND", 17},
            {"ORA", 18},
            {"XOR", 19},
            {"DWR", 20},
            // PPU opcodes
            {"PPU_NOP", PPU_OFFSET},
            {"PPU_DRW", PPU_OFFSET + 1},
            {"PPU_LFM", PPU_OFFSET + 2},
            {"PPU_WTV", PPU_OFFSET + 3},
            {"PPU_BRR", PPU_OFFSET + 4},
            {"PPU_BLR", PPU_OFFSET + 5},
            {"PPU_ORA", PPU_OFFSET + 6},
            {"PPU_XOR", PPU_OFFSET + 7},
            {"PPU_AND", PPU_OFFSET + 8},
            {"PPU_DEC", PPU_OFFSET + 9},
            {"PPU_INC", PPU_OFFSET + 10},
            // Debug extension opcodes
            {"DBG_LGC", DEBUG_EXTENSION_OFFSET},
            {"DGB_MEM", DEBUG_EXTENSION_OFFSET + 1},
            {"DGB_INP", DEBUG_EXTENSION_OFFSET + 2}
        };

        /// <summary>
        /// Get key using value, if several keys correspond to the same value, return an array
        /// </summary>
        /// <param name="value">Value to search for</param>
        /// <returns>Array of keys</returns>
        public static string[] GetKey(ushort value)
        {
            List<string> keys = [];
            foreach (KeyValuePair<string, ushort> pair in instructions)
            {
                if (pair.Value == value)
                {
                    keys.Add(pair.Key);
                }
            }
            return [.. keys];
        }
    }
}
