namespace Fox16ASM
{
    enum RegisterId : ushort
    {
        X = 0x0000,
        Y = 0x0001,
        R0 = 0x0007,
        R1 = 0x0008,
        R2 = 0x0009,
        R3 = 0x000A,
        R4 = 0x000B,
        R5 = 0x000C,
        R6 = 0x000D,
        R7 = 0x000E,
        PC = 0x0002,
        STATUS = 0x0003,
        SP = 0x0004,
        CYC = 0x0005,
        EM = 0x0006,
    }

    static class RegisterOperands
    {
        internal static bool TryParseSymbol(string symbol, out RegisterId register)
        {
            register = symbol switch
            {
                "X" => RegisterId.X,
                "Y" => RegisterId.Y,
                "R0" => RegisterId.R0,
                "R1" => RegisterId.R1,
                "R2" => RegisterId.R2,
                "R3" => RegisterId.R3,
                "R4" => RegisterId.R4,
                "R5" => RegisterId.R5,
                "R6" => RegisterId.R6,
                "R7" => RegisterId.R7,
                "PC" => RegisterId.PC,
                "STATUS" => RegisterId.STATUS,
                "SP" => RegisterId.SP,
                "CYC" => RegisterId.CYC,
                "EM" => RegisterId.EM,
                _ => default
            };

            return symbol is "X" or "Y" or "R0" or "R1" or "R2" or "R3" or "R4" or "R5" or "R6" or "R7" or "PC" or "STATUS" or "SP" or "CYC" or "EM";
        }

        internal static bool IsSupportedRegisterId(ushort value)
            => value == (ushort)RegisterId.X
                || value == (ushort)RegisterId.Y
                || value == (ushort)RegisterId.R0
                || value == (ushort)RegisterId.R1
                || value == (ushort)RegisterId.R2
                || value == (ushort)RegisterId.R3
                || value == (ushort)RegisterId.R4
                || value == (ushort)RegisterId.R5
                || value == (ushort)RegisterId.R6
                || value == (ushort)RegisterId.R7
                || value == (ushort)RegisterId.PC
                || value == (ushort)RegisterId.STATUS
                || value == (ushort)RegisterId.SP
                || value == (ushort)RegisterId.CYC
                || value == (ushort)RegisterId.EM;
    }
}
