using Fox16Shared;

namespace Fox16ASM
{
    class MacroResolution()
    {
        public string[] Resolve(string[] lines)
        {
            Console.WriteLine("Macro resolution");
            List<string> resolvedLines = [];
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("MOV"))
                {
                    var parts = line.Split(" ");
                    if (parts.Length != 3) throw new Exception("Invalid MOV instruction");
                    var dest = parts[1];
                    var src = parts[2];
                    string destType = $"{dest[0]}";
                    string srcType = $"{src[0]}";

                    // Register
                    if (destType == "#") destType = $"%{(ushort)SourceDest.Register}";
                    if (srcType == "#") srcType = $"%{(ushort)SourceDest.Register}";

                    // Memory
                    if (destType == "$") destType = $"%{(ushort)SourceDest.Memory}";
                    if (srcType == "$") srcType = $"%{(ushort)SourceDest.Memory}";

                    // Immediate
                    if (destType == "!") destType = $"%{(ushort)SourceDest.Immediate}";
                    if (srcType == "!") throw new Exception("Invalid immediate source");

                    // Display parts
                    Console.WriteLine($"Dest: {dest}, Src: {src}");
                    Console.WriteLine($"DestType: {destType}, SrcType: {srcType}");

                    // Add the SSM
                    resolvedLines.Add($"SSM {srcType}");

                    // Add the SDM
                    resolvedLines.Add($"SDM {destType}");

                    // Clean up MOV
                    var MOVString = "MOV ";

                    // If string contains #, remove it - Source
                    if (src.Contains("#")) {
                        src = src[1..];

                        // Change A to 0
                        if (src == "A") src = "%0";

                        // Change B to 1
                        if (src == "B") src = "%1";
                    }

                    // If string contains #, remove it - Dest
                    if (dest.Contains("#")) {
                        dest = dest[1..];

                        // Change A to 0
                        if (dest == "A") dest = "%0";

                        // Change B to 1
                        if (dest == "B") dest = "%1";
                    }

                    // If the string starts with !, remove it
                    if (src.Contains("!")) src = src.Substring(1);

                    // If the string starts with !, remove it
                    if (dest.Contains("!")) dest = dest.Substring(1);

                    MOVString += $"{dest} {src}";
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"MOV: {MOVString}");
                    Console.ResetColor();
                    resolvedLines.Add(MOVString);

                } else resolvedLines.Add(line);
                
            }
            Console.WriteLine("Macro resolution complete");

            return [.. resolvedLines];
        }
    }
}