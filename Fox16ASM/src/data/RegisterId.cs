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
        R0 = 0x0007,
        R1 = 0x0008,
        R2 = 0x0009,
        R3 = 0x000A,
        R4 = 0x000B,
        R5 = 0x000C,
        R6 = 0x000D,
        R7 = 0x000E,
        AM = 0x000F,

        XL = 0x0010,
        XH = 0x0011,
        YL = 0x0012,
        YH = 0x0013,
        PCL = 0x0014,
        PCH = 0x0015,
        SPL = 0x0016,
        SPH = 0x0017,
        CYCL = 0x0018,
        CYCH = 0x0019,

        // Read-only memory-size register, introduced in ISA V2.1. Reports
        // installed RAM in 1 KiB units. Available when EM != 0.
        MS = 0x001A,

        // ISA V2.2 trap and process-window registers.
        VEC = 0x001B,
        IRQEN = 0x001C,
        KSP = 0x001D,
        DB = 0x001E,
        HB = 0x001F,
        CB = 0x0030,
        QUANT = 0x0031,

        R0L = 0x0020,
        R0H = 0x0021,
        R1L = 0x0022,
        R1H = 0x0023,
        R2L = 0x0024,
        R2H = 0x0025,
        R3L = 0x0026,
        R3H = 0x0027,
        R4L = 0x0028,
        R4H = 0x0029,
        R5L = 0x002A,
        R5H = 0x002B,
        R6L = 0x002C,
        R6H = 0x002D,
        R7L = 0x002E,
        R7H = 0x002F,
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
                "R0" => RegisterId.R0,
                "R1" => RegisterId.R1,
                "R2" => RegisterId.R2,
                "R3" => RegisterId.R3,
                "R4" => RegisterId.R4,
                "R5" => RegisterId.R5,
                "R6" => RegisterId.R6,
                "R7" => RegisterId.R7,
                "AM" => RegisterId.AM,

                "XL" => RegisterId.XL,
                "XH" => RegisterId.XH,
                "YL" => RegisterId.YL,
                "YH" => RegisterId.YH,
                "PCL" => RegisterId.PCL,
                "PCH" => RegisterId.PCH,
                "SPL" => RegisterId.SPL,
                "SPH" => RegisterId.SPH,
                "CYCL" => RegisterId.CYCL,
                "CYCH" => RegisterId.CYCH,
                "MS" => RegisterId.MS,
                "VEC" => RegisterId.VEC,
                "IRQEN" => RegisterId.IRQEN,
                "KSP" => RegisterId.KSP,
                "DB" => RegisterId.DB,
                "HB" => RegisterId.HB,
                "CB" => RegisterId.CB,
                "QUANT" => RegisterId.QUANT,

                "R0L" => RegisterId.R0L,
                "R0H" => RegisterId.R0H,
                "R1L" => RegisterId.R1L,
                "R1H" => RegisterId.R1H,
                "R2L" => RegisterId.R2L,
                "R2H" => RegisterId.R2H,
                "R3L" => RegisterId.R3L,
                "R3H" => RegisterId.R3H,
                "R4L" => RegisterId.R4L,
                "R4H" => RegisterId.R4H,
                "R5L" => RegisterId.R5L,
                "R5H" => RegisterId.R5H,
                "R6L" => RegisterId.R6L,
                "R6H" => RegisterId.R6H,
                "R7L" => RegisterId.R7L,
                "R7H" => RegisterId.R7H,
                _ => default
            };

            return symbol is "X" or "Y" or "PC" or "STATUS" or "SP" or "CYC" or "EM"
                or "R0" or "R1" or "R2" or "R3" or "R4" or "R5" or "R6" or "R7" or "AM"
                or "XL" or "XH" or "YL" or "YH" or "PCL" or "PCH" or "SPL" or "SPH" or "CYCL" or "CYCH" or "MS"
                or "VEC" or "IRQEN" or "KSP" or "DB" or "HB" or "CB" or "QUANT"
                or "R0L" or "R0H" or "R1L" or "R1H" or "R2L" or "R2H" or "R3L" or "R3H"
                or "R4L" or "R4H" or "R5L" or "R5H" or "R6L" or "R6H" or "R7L" or "R7H";
        }

        internal static bool IsSupportedRegisterId(ushort value)
            => (value >= (ushort)RegisterId.X && value <= (ushort)RegisterId.AM)
            || (value >= (ushort)RegisterId.XL && value <= (ushort)RegisterId.CYCH)
            || value == (ushort)RegisterId.MS
            || (value >= (ushort)RegisterId.VEC && value <= (ushort)RegisterId.HB)
            || value == (ushort)RegisterId.CB
            || value == (ushort)RegisterId.QUANT
            || (value >= (ushort)RegisterId.R0L && value <= (ushort)RegisterId.R7H);
    }
}
