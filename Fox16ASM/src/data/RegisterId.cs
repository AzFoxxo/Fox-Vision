namespace Fox16ASM
{
    enum RegisterId : ushort
    {
        X = 0x0000,
        Y = 0x0001,
        STATUS = 0x0003,
        SP = 0x0004,
    }

    static class RegisterOperands
    {
        internal static bool TryParseSymbol(string symbol, out RegisterId register)
        {
            register = symbol switch
            {
                "X" => RegisterId.X,
                "Y" => RegisterId.Y,
                "STATUS" => RegisterId.STATUS,
                "SP" => RegisterId.SP,
                _ => default
            };

            return symbol is "X" or "Y" or "STATUS" or "SP";
        }

        internal static bool IsSupportedRegisterId(ushort value)
            => value == (ushort)RegisterId.X
                || value == (ushort)RegisterId.Y
                || value == (ushort)RegisterId.STATUS
                || value == (ushort)RegisterId.SP;
    }
}
