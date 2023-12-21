/*
 *   Copyright (c) 2023 Az Foxxo
 *   All rights reserved.
 */

namespace Fox16ASM
{
    class Opcodes
    {
        public static Dictionary<string, ushort> instructions = new Dictionary<string, ushort>()
        {
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
            {"JPL", 12},
            {"CLR", 13},
            {"HLT", 14},
            {"BSL", 15},
            {"BSR", 16},
            {"AND", 17},
            {"ORA", 18},
            {"XOR", 19},
            {"DWR", 20}
        };
    }
}
