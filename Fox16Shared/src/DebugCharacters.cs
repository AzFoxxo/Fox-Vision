using System.Runtime.InteropServices;

namespace Fox16Shared
{
    /// <summary>
    /// Opcodes dictionary
    /// Contains the short opcode string representation and opcode value
    /// </summary>
    public class DebugCharacters
    {
        public static Dictionary<char, ushort> codes = new()
        {
            // Character codes
            { '#', 0},
            { 'A', 1},
            { 'B', 2},
            { 'C', 3},
            { 'D', 4},
            { 'E', 5},
            { 'F', 6},
            { 'G', 7},
            { 'H', 8},
            { 'I', 9},
            { 'J', 10},
            { 'K', 11},
            { 'L', 12},
            { 'M', 13},
            { 'N', 14},
            { 'O', 15},
            { 'P', 16},
            { 'Q', 17},
            { 'R', 18},
            { 'S', 19},
            { 'T', 20},
            { 'U', 21},
            { 'V', 22},
            { 'W', 23},
            { 'X', 24},
            { 'Y', 25},
            { 'Z', 26},
            { '-', 27},
            { '0', 28},
            { '1', 29},
            { '2', 30},
            { '3', 31},
            { '4', 32},
            { '5', 33},
            { '6', 34},
            { '7', 35},
            { '8', 36},
            { '9', 37},
            { '\n', 38},
            { ' ', 39}
        };

        /// <summary>
        /// Return first character with matching character code
        /// </summary>
        /// <param name="value">Value to search for</param>
        /// <returns>character</returns>
        public static char GetCharacter(ushort value)
        {
            List<char> keys = [];
            foreach (KeyValuePair<char, ushort> pair in codes)
            {
                if (pair.Value == value)
                {
                    return pair.Key;
                }
            }
            return '?';
        }
    }
}
