package main

import (
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"

	"foxc/internal/foxc"
)

func main() {
	in := flag.String("i", "", "input FoxC file")
	out := flag.String("o", "", "output file")
	mode := flag.String("mode", "legacy", "target machine mode (legacy|extended)")
	strictFormat := flag.Bool("strict-format", false, "enforce ROM size compliance for the selected mode")
	asmOnly := flag.Bool("asm", false, "assemble only: output Fox16ASM instead of binary ROM")
	flag.BoolVar(asmOnly, "a", false, "short for --asm")
	flag.Parse()

	if *in == "" {
		fmt.Fprintln(os.Stderr, "usage: foxc -i <input.fc> [-o output] [--mode legacy|extended] [--strict-format] [-a|--asm]")
		os.Exit(2)
	}

	src, err := os.ReadFile(*in)
	if err != nil {
		fmt.Fprintf(os.Stderr, "read input: %v\n", err)
		os.Exit(1)
	}

	asm, err := foxc.CompileWithOptions(string(src), foxc.CompileOptions{Mode: *mode})
	if err != nil {
		fmt.Fprintf(os.Stderr, "compile error: %v\n", err)
		os.Exit(1)
	}

	// Determine output path for assembly
	asmPath := *out
	if asmPath == "" {
		if *asmOnly {
			asmPath = "out.f16"
		} else {
			asmPath = "out.f16" // temporary, will be assembled to .bin
		}
	} else if *asmOnly {
		// If --asm flag is set, output assembly
	} else if filepath.Ext(asmPath) != ".f16" {
		// If output doesn't end in .f16, assume it's a .bin path
		// We'll keep assembly as a temp file
		asmPath = asmPath + ".f16"
	}

	// Write assembly output
	if err := os.WriteFile(asmPath, []byte(asm), 0o644); err != nil {
		fmt.Fprintf(os.Stderr, "write assembly: %v\n", err)
		os.Exit(1)
	}

	if *asmOnly {
		fmt.Printf("FoxC compiled %s -> %s\n", *in, asmPath)
		return
	}

	// Run assembler to produce binary ROM
	binPath := *out
	if binPath == "" {
		binPath = "out.bin"
	} else if filepath.Ext(binPath) == ".f16" {
		// Replace .f16 with .bin
		binPath = binPath[:len(binPath)-4] + ".bin"
	}

	// Build assembler command
	asmCmd := []string{"-i", asmPath, "-o", binPath, "--mode", *mode}
	if *strictFormat {
		asmCmd = append(asmCmd, "--strict-format")
	}

	// Try to find fox16asm executable
	asmExe := "fox16asm"
	var cmd *exec.Cmd
	if _, err := exec.LookPath(asmExe); err == nil {
		cmd = exec.Command(asmExe, asmCmd...)
	} else {
		// Try in common build output location first.
		builtExe := "Fox16ASM/bin/Debug/net10.0/Fox16ASM"
		if _, statErr := os.Stat(builtExe); statErr == nil {
			cmd = exec.Command(builtExe, asmCmd...)
		} else {
			exePath, exeErr := os.Executable()
			if exeErr != nil {
				fmt.Fprintf(os.Stderr, "error: fox16asm not found in PATH and not built at %s\n", builtExe)
				fmt.Fprintf(os.Stderr, "build Fox16ASM with: dotnet build Fox16ASM/Fox16ASM.csproj\n")
				os.Remove(asmPath)
				os.Exit(1)
			}
			repoRoot := filepath.Dir(filepath.Dir(exePath))
			projectPath := filepath.Join(repoRoot, "Fox16ASM", "Fox16ASM.csproj")
			if _, statErr := os.Stat(projectPath); statErr != nil {
				fmt.Fprintf(os.Stderr, "error: fox16asm not found in PATH and not built at %s\n", builtExe)
				fmt.Fprintf(os.Stderr, "build Fox16ASM with: dotnet build Fox16ASM/Fox16ASM.csproj\n")
				os.Remove(asmPath)
				os.Exit(1)
			}
			dotnetArgs := append([]string{"run", "--project", projectPath, "--"}, asmCmd...)
			cmd = exec.Command("dotnet", dotnetArgs...)
		}
	}

	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr

	if err := cmd.Run(); err != nil {
		fmt.Fprintf(os.Stderr, "assembler error: %v\n", err)
		// Clean up temp assembly file
		os.Remove(asmPath)
		os.Exit(1)
	}

	// Clean up temp assembly file
	if asmPath != *out {
		os.Remove(asmPath)
	}

	fmt.Printf("FoxC compiled %s -> %s\n", *in, binPath)
}
