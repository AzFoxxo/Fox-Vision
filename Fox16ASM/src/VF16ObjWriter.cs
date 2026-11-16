using System.Text;

namespace Fox16ASM;

public static class VF16ObjWriter
{
    public static void WriteObject(string path, byte[] textSectionData, Dictionary<string, ushort> labels)
    {
        var sectionData = StripRomHeader(textSectionData);

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        // Magic
        bw.Write(Encoding.ASCII.GetBytes("VF16OBJ"));
        bw.Write((byte)1); // version
        // Section count, symbol count, reloc count placeholders
        bw.Write((uint)1); // one section
        bw.Write((uint)labels.Count);
        bw.Write((uint)0); // no relocations

        // Section
        var nameBytes = Encoding.ASCII.GetBytes(".text");
        bw.Write((byte)nameBytes.Length);
        bw.Write(nameBytes);
        bw.Write((byte)1); // flags: alloc
        bw.Write((uint)sectionData.Length);
        bw.Write(sectionData);

        // Symbols
        foreach (var kv in labels)
        {
            var nb = Encoding.ASCII.GetBytes(kv.Key);
            bw.Write((byte)nb.Length);
            bw.Write(nb);
            bw.Write((byte)1); // global
            bw.Write((int)0); // section index
            bw.Write((uint)kv.Value);
        }

        // no relocations
    }

    private static byte[] StripRomHeader(byte[] romBytes)
    {
        const string legacyMagic = ".VISOFOX16";
        const string extendedMagic = ".VFOX16EXT";

        if (romBytes.Length >= legacyMagic.Length &&
            Encoding.ASCII.GetString(romBytes, 0, legacyMagic.Length) == legacyMagic)
        {
            var payload = new byte[Math.Max(0, romBytes.Length - legacyMagic.Length)];
            Array.Copy(romBytes, legacyMagic.Length, payload, 0, payload.Length);
            return payload;
        }

        if (romBytes.Length >= extendedMagic.Length &&
            Encoding.ASCII.GetString(romBytes, 0, extendedMagic.Length) == extendedMagic)
        {
            byte version = romBytes.Length > 10 ? romBytes[10] : (byte)1;
            int extendedHeaderLength = version switch
            {
                1 => 17,
                2 => 19,
                3 => 25,
                _ => 19
            };
            if (version == 3 && romBytes.Length >= 27)
            {
                uint expectedWords4 = (uint)((romBytes[23] << 24) | (romBytes[24] << 16) | (romBytes[25] << 8) | romBytes[26]);
                int pBytes = romBytes.Length - 27;
                if ((pBytes & 1) != 0) pBytes++;
                if ((ulong)(pBytes / 2) == expectedWords4)
                {
                    extendedHeaderLength = 27;
                }
            }

            if (romBytes.Length <= extendedHeaderLength)
                return Array.Empty<byte>();

            var payload = new byte[romBytes.Length - extendedHeaderLength];
            Array.Copy(romBytes, extendedHeaderLength, payload, 0, payload.Length);
            return payload;
        }

        return romBytes;
    }
}
