namespace Fox16ASM
{
    enum RegisterId : ushort
    {
        X = 0x0000,
        Y = 0x0001,
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
                "PC" => RegisterId.PC,
                "STATUS" => RegisterId.STATUS,
                "SP" => RegisterId.SP,
                "CYC" => RegisterId.CYC,
                "EM" => RegisterId.EM,
                _ => default
            };

            return symbol is "X" or "Y" or "PC" or "STATUS" or "SP" or "CYC" or "EM";
        }

        internal static bool IsSupportedRegisterId(ushort value)
            => value == (ushort)RegisterId.X
                || value == (ushort)RegisterId.Y
                || value == (ushort)RegisterId.PC
                || value == (ushort)RegisterId.STATUS
                || value == (ushort)RegisterId.SP
                || value == (ushort)RegisterId.CYC
                || value == (ushort)RegisterId.EM;
    }
}
