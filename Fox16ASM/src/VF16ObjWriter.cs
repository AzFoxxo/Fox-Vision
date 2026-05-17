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
            const int extendedHeaderLength = 17;
            if (romBytes.Length <= extendedHeaderLength)
                return Array.Empty<byte>();

            var payload = new byte[romBytes.Length - extendedHeaderLength];
            Array.Copy(romBytes, extendedHeaderLength, payload, 0, payload.Length);
            return payload;
        }

        return romBytes;
    }
}
