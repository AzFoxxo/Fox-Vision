namespace Fox16Shared
{
    /// <summary>
    /// Opcodes dictionary
    /// Contains the short opcode string representation and opcode value
    /// </summary>
    public class Opcodes
    {
        public const ushort DEBUG_EXTENSION_OFFSET = 0xC000;
        public const ushort IO_EXTENSION_OFFSET = 0x8000;
        public static Dictionary<string, ushort> instructions = new()
        {
            // CPU opcodes
            {"NOP", 0},     // Unchanged
            {"LFM", 1},     // Defunct
            {"WTM", 2},     // Defunct
            {"SRA", 3},     // Defunct
            {"AXY", 4},     // ADD (changed behavior)
            {"SXY", 5},     // SUB (changed behavior)
            {"MXY", 6},     // MUL (changed behavior)
            {"DXY", 7},     // DIV (changed behavior)
            {"EQU", 8},     // CMP (changed behavior)
            {"LEQ", 9},     // Defunct
            {"JPZ", 10},    // JZ (changed behavior)
            {"JNZ", 11},    // JNZ (changed behavior)
            {"JMP", 12},    // JMP (unchanged)
            {"CLR", 13},    // Defunct
            {"HLT", 14},    // HLT (unchanged)
            {"BSL", 15},    // SHL (changed behavior)
            {"BSR", 16},    // SHR (changed behavior)
            {"AND", 17},    // AND (changed behavior)
            {"ORA", 18},    // OR (changed behavior)
            {"XOR", 19},    // XOR (changed behavior)
            {"DWR", 20},    // Defunct
            // V1.1
            {"ILM", 21},    // Defunct
            {"IWR", 22},    // Defunct
            // V1.2
            {"INC", 23},    // INC (changed behavior)
            {"DEC", 24},    // DEC (changed behavior)
            // V1.4
            {"SDM", 25},    // Defunct
            {"SSM", 26},    // Defunct
            {"MOV", 27},    // MOV (changed behavior)
            // V1.3
            {"GSWP", IO_EXTENSION_OFFSET},  // Unchanged
            {"GCLR", IO_EXTENSION_OFFSET + 1}, // Unchanged
            // Debug extension opcodes
            {"DBG_LGC", DEBUG_EXTENSION_OFFSET}, // Unchanged
            {"DGB_MEM", DEBUG_EXTENSION_OFFSET + 1}, // Unchanged
            {"DGB_INP", DEBUG_EXTENSION_OFFSET + 2} // Unchanged
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
