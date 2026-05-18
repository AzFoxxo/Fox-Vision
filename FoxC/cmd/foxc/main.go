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

	// ----------------------------
	// Assembly output
	// ----------------------------
	asmPath := *out
	if asmPath == "" {
		asmPath = "out.f16"
	} else if !*asmOnly && filepath.Ext(asmPath) != ".f16" {
		asmPath += ".f16"
	}

	if err := os.WriteFile(asmPath, []byte(asm), 0o644); err != nil {
		fmt.Fprintf(os.Stderr, "write assembly: %v\n", err)
		os.Exit(1)
	}

	if *asmOnly {
		fmt.Printf("FoxC compiled %s -> %s\n", *in, asmPath)
		return
	}

	// ----------------------------
	// Binary output
	// ----------------------------
	binPath := *out
	if binPath == "" {
		binPath = "out.bin"
	} else if filepath.Ext(binPath) == ".f16" {
		binPath = binPath[:len(binPath)-4] + ".bin"
	}

	// ----------------------------
	// PATH-ONLY assembler resolution
	// ----------------------------
	asmExe, err := exec.LookPath("fox16asm")
	if err != nil {
		fmt.Fprintln(os.Stderr, "error: fox16asm not found in PATH")
		fmt.Fprintln(os.Stderr, "fix: export PATH=$HOME/vf16/tools:$PATH (fish: fish_add_path ~/vf16/tools)")
		os.Exit(1)
	}

	asmCmd := []string{
		"-i", asmPath,
		"-o", binPath,
		"--mode", *mode,
	}

	if *strictFormat {
		asmCmd = append(asmCmd, "--strict-format")
	}

	cmd := exec.Command(asmExe, asmCmd...)
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr

	if err := cmd.Run(); err != nil {
		fmt.Fprintf(os.Stderr, "assembler error: %v\n", err)
		os.Exit(1)
	}

	// cleanup only if temporary
	if *out == "" || filepath.Ext(*out) != ".f16" {
		os.Remove(asmPath)
	}

	fmt.Printf("FoxC compiled %s -> %s\n", *in, binPath)
}
