using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace VF16Linker
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: VF16Linker link -o out.bin file1.vf16obj [file2.vf16obj ...]");
                Console.WriteLine("       VF16Linker dump file.vf16obj");
                return 1;
            }

            var cmd = args[0];
            if (cmd == "dump")
            {
                var path = args[1];
                var obj = VF16Object.ReadFrom(path);
                obj.Dump(Console.Out);
                return 0;
            }

            if (cmd == "link")
            {
                string outPath = null!;
                var inputs = new List<string>();
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "-o" && i + 1 < args.Length)
                    {
                        outPath = args[++i];
                    }
                    else inputs.Add(args[i]);
                }
                if (outPath == null || inputs.Count == 0)
                {
                    Console.WriteLine("link usage: VF16Linker link -o out.bin inputs...");
                    return 1;
                }
                var objects = new List<VF16Object>();
                foreach (var p in inputs)
                {
                    objects.Add(VF16Object.ReadFrom(p));
                }
                Linker.Link(objects, outPath);
                Console.WriteLine($"Wrote output to {outPath}");
                return 0;
            }

            Console.WriteLine("Unknown command: " + cmd);
            return 1;
        }
    }

    public class VF16Object
    {
        public const string Magic = "VF16OBJ";
        public byte Version;
        public List<Section> Sections = new();
        public List<Symbol> Symbols = new();
        public List<Relocation> Relocations = new();

        public static VF16Object ReadFrom(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            var headerBytes = br.ReadBytes(7);
            var header = Encoding.ASCII.GetString(headerBytes);
            if (header != Magic)
                throw new InvalidDataException("Not a VF16OBJ file: " + path);
            var obj = new VF16Object();
            obj.Version = br.ReadByte();
            var sectionCount = br.ReadUInt32();
            var symbolCount = br.ReadUInt32();
            var relocCount = br.ReadUInt32();

            for (int i = 0; i < sectionCount; i++)
            {
                var nameLen = br.ReadByte();
                var name = Encoding.ASCII.GetString(br.ReadBytes(nameLen));
                var flags = br.ReadByte();
                var size = br.ReadUInt32();
                var data = br.ReadBytes((int)size);
                obj.Sections.Add(new Section { Name = name, Flags = flags, Data = data, Size = size });
            }

            for (int i = 0; i < symbolCount; i++)
            {
                var nlen = br.ReadByte();
                var name = Encoding.ASCII.GetString(br.ReadBytes(nlen));
                var flags = br.ReadByte();
                var sectionIndex = br.ReadInt32();
                var offset = br.ReadUInt32();
                obj.Symbols.Add(new Symbol { Name = name, Flags = flags, SectionIndex = sectionIndex, Offset = offset });
            }

            for (int i = 0; i < relocCount; i++)
            {
                var sectionIndex = br.ReadUInt32();
                var offset = br.ReadUInt32();
                var symIndex = br.ReadUInt32();
                var type = br.ReadByte();
                obj.Relocations.Add(new Relocation { SectionIndex = (int)sectionIndex, Offset = offset, SymbolIndex = (int)symIndex, Type = type });
            }

            return obj;
        }

        public void Dump(TextWriter tw)
        {
            tw.WriteLine("VF16OBJ v" + Version);
            tw.WriteLine($"Sections ({Sections.Count}):");
            for (int i = 0; i < Sections.Count; i++)
            {
                var s = Sections[i];
                tw.WriteLine($" [{i}] {s.Name} flags=0x{s.Flags:X2} size={s.Size}");
            }
            tw.WriteLine($"Symbols ({Symbols.Count}):");
            for (int i = 0; i < Symbols.Count; i++)
            {
                var s = Symbols[i];
                tw.WriteLine($" [{i}] {s.Name} flags=0x{s.Flags:X2} sec={s.SectionIndex} off=0x{s.Offset:X}");
            }
            tw.WriteLine($"Relocations ({Relocations.Count}):");
            for (int i = 0; i < Relocations.Count; i++)
            {
                var r = Relocations[i];
                tw.WriteLine($" [{i}] sec={r.SectionIndex} off=0x{r.Offset:X} sym={r.SymbolIndex} type={r.Type}");
            }
        }
    }

    public class Section
    {
        public string Name = string.Empty;
        public byte Flags;
        public uint Size;
        public byte[] Data = Array.Empty<byte>();
    }

    public class Symbol
    {
        public string Name = string.Empty;
        public byte Flags;
        public int SectionIndex; // -1 undefined
        public uint Offset;
    }

    public class Relocation
    {
        public int SectionIndex;
        public uint Offset;
        public int SymbolIndex;
        public byte Type; // 1 = ABS16
    }

    public static class Linker
    {
        // Simple deterministic section ordering
        static readonly string[] PreferredOrder = new[] { ".text", ".data" };

        public static void Link(List<VF16Object> objects, string outPath)
        {
            // Collect section names
            var sectionNames = new LinkedList<string>();
            var seen = new HashSet<string>();
            // Ensure preferred order first
            foreach (var name in PreferredOrder) { sectionNames.AddLast(name); seen.Add(name); }

            // Add others
            foreach (var obj in objects)
                foreach (var s in obj.Sections)
                {
                    if (!seen.Contains(s.Name)) { sectionNames.AddLast(s.Name); seen.Add(s.Name); }
                }

            // Compute effective per-object section sizes (ensure space for symbols/relocs)
            var perObjSectionSize = new Dictionary<(int objIdx, int secIdx), uint>();
            for (int oi = 0; oi < objects.Count; oi++)
            {
                var obj = objects[oi];
                for (int si = 0; si < obj.Sections.Count; si++)
                {
                    uint size = obj.Sections[si].Size;
                    // ensure space for symbols located in this section
                    foreach (var sym in obj.Symbols)
                    {
                        if (sym.SectionIndex == si)
                        {
                            size = Math.Max(size, sym.Offset + 2);
                        }
                    }
                    // ensure space for relocations in this section
                    foreach (var r in obj.Relocations)
                    {
                        if (r.SectionIndex == si)
                        {
                            size = Math.Max(size, r.Offset + 2);
                        }
                    }
                    perObjSectionSize[(oi, si)] = size;
                }
            }

            // Combined section sizes and base addresses
            var combinedSize = new Dictionary<string, uint>();
            foreach (var name in sectionNames)
            {
                uint sum = 0;
                for (int oi = 0; oi < objects.Count; oi++)
                {
                    var obj = objects[oi];
                    // find matching section in object
                    for (int si = 0; si < obj.Sections.Count; si++)
                    {
                        if (obj.Sections[si].Name == name)
                        {
                            sum += perObjSectionSize[(oi, si)];
                            break;
                        }
                    }
                }
                combinedSize[name] = sum;
            }

            var baseAddr = new Dictionary<string, uint>();
            uint cursor = 0;
            foreach (var name in sectionNames)
            {
                baseAddr[name] = cursor;
                cursor += combinedSize[name];
            }

            // Compute per-object offsets within combined sections
            var perObjOffsetInCombined = new Dictionary<(int oi, int si), uint>();
            foreach (var name in sectionNames)
            {
                uint accum = 0;
                for (int oi = 0; oi < objects.Count; oi++)
                {
                    var obj = objects[oi];
                    for (int si = 0; si < obj.Sections.Count; si++)
                    {
                        if (obj.Sections[si].Name == name)
                        {
                            perObjOffsetInCombined[(oi, si)] = accum;
                            accum += perObjSectionSize[(oi, si)];
                            break;
                        }
                    }
                }
            }

            // Build combined byte arrays
            var combinedBytes = new Dictionary<string, byte[]>();
            foreach (var name in sectionNames)
            {
                combinedBytes[name] = new byte[combinedSize[name]];
            }

            // Copy object section data into combined arrays
            for (int oi = 0; oi < objects.Count; oi++)
            {
                var obj = objects[oi];
                for (int si = 0; si < obj.Sections.Count; si++)
                {
                    var s = obj.Sections[si];
                    var target = combinedBytes[s.Name];
                    var off = perObjOffsetInCombined[(oi, si)];
                    Buffer.BlockCopy(s.Data, 0, target, (int)off, s.Data.Length);
                    // rest is zero-initialized (for bss or padding)
                }
            }

            // Resolve symbols
            var symbolAddrs = new Dictionary<string, uint>();
            for (int oi = 0; oi < objects.Count; oi++)
            {
                var obj = objects[oi];
                for (int si = 0; si < obj.Symbols.Count; si++)
                {
                    var sym = obj.Symbols[si];
                    if ((sym.Flags & 1) == 0) continue; // not global
                    if (sym.SectionIndex < 0) continue; // absolute/undefined
                    var sec = obj.Sections[sym.SectionIndex];
                    var combinedBase = baseAddr[sec.Name];
                    var perObjOff = perObjOffsetInCombined[(oi, sym.SectionIndex)];
                    var addr = combinedBase + perObjOff + sym.Offset;
                    if (!symbolAddrs.ContainsKey(sym.Name)) symbolAddrs[sym.Name] = addr;
                }
            }

            // Apply relocations (only ABS16 supported)
            for (int oi = 0; oi < objects.Count; oi++)
            {
                var obj = objects[oi];
                foreach (var r in obj.Relocations)
                {
                    var sec = obj.Sections[r.SectionIndex];
                    var combinedBaseForSec = baseAddr[sec.Name];
                    var perObjOff = perObjOffsetInCombined[(oi, r.SectionIndex)];
                    var writeAddr = combinedBaseForSec + perObjOff + r.Offset;

                    // lookup symbol
                    var sym = obj.Symbols[r.SymbolIndex];
                    if (!symbolAddrs.TryGetValue(sym.Name, out var val))
                    {
                        throw new Exception($"Unresolved symbol: {sym.Name}");
                    }

                    if (r.Type == 1) // ABS16
                    {
                        // Find which combined array and offset to write into
                        var targetArr = combinedBytes[sec.Name];
                        var localOffset = (int)(perObjOff + r.Offset);
                        if (localOffset + 1 >= targetArr.Length) throw new Exception("Relocation out of range");
                        targetArr[localOffset] = (byte)(val & 0xFF);
                        targetArr[localOffset + 1] = (byte)((val >> 8) & 0xFF);
                    }
                    else throw new Exception("Unsupported relocation type: " + r.Type);
                }
            }

            // Write flat binary: sections in the chosen order
            using var of = File.Create(outPath);
            foreach (var name in sectionNames)
            {
                var bytes = combinedBytes[name];
                of.Write(bytes, 0, bytes.Length);
            }

            // Also write a simple map file alongside outPath
            var mapPath = outPath + ".map";
            using var mtw = new StreamWriter(mapPath);
            mtw.WriteLine("Sections:");
            foreach (var name in sectionNames)
            {
                mtw.WriteLine($" {name} base=0x{baseAddr[name]:X4} size=0x{combinedSize[name]:X4}");
            }
            mtw.WriteLine("Symbols:");
            foreach (var kv in symbolAddrs)
            {
                mtw.WriteLine($" {kv.Key} = 0x{kv.Value:X4}");
            }
        }
    }
}
