package main

import (
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	"foxc/internal/foxc"
)

func main() {
	in := flag.String("i", "", "input FoxC file")
	out := flag.String("o", "", "output file")
	mode := flag.String("mode", "legacy", "target machine mode (legacy|extended|em2_am1)")
	strictFormat := flag.Bool("strict-format", false, "enforce ROM size compliance for the selected mode")
	loadAddressText := flag.String("load-address", "0", "load address (decimal, 0x-prefixed hexadecimal, or $ hexadecimal)")
	globalBaseText := flag.String("global-base", "0", "override the global storage base (word address)")
	mapper := flag.String("mapper", "", "ROM mapper (0|rom4k, 1|rom32k, 2|rombyte8_32); default depends on mode")
	romVersion := flag.String("rom-version", "", "extended ROM header version (1, 2, or 3); default depends on mode")

	var includeDirs stringSlice
	flag.Var(&includeDirs, "I", "add header search directory (repeatable)")
	flag.Var(&includeDirs, "include-dir", "add header search directory (repeatable)")
	asmOnly := flag.Bool("asm", false, "assemble only: output Fox16ASM instead of binary ROM")
	flag.BoolVar(asmOnly, "a", false, "short for --asm")
	flag.Usage = func() {
		fmt.Fprintln(os.Stderr, "usage: foxc -i <input.fc> [-o output] [--mode legacy|extended|em2_am1] [--load-address address] [--global-base address] [--mapper 0|1|2] [--rom-version 1|2|3] [-I DIR] [--include-dir DIR] [--strict-format] [-a|--asm]")
		fmt.Fprintln(os.Stderr, "")
		fmt.Fprintln(os.Stderr, "Modes:")
		fmt.Fprintln(os.Stderr, "  legacy    16-bit address space, X/Y plus stack evaluation")
		fmt.Fprintln(os.Stderr, "  extended  16-bit address space, R0-R7 registers, ports 0-7")
		fmt.Fprintln(os.Stderr, "  em2_am1   V2.0 ISA: EM=2, 32-bit registers and u32 support;")
		fmt.Fprintln(os.Stderr, "            the compiler starts in word addressing (AM=0) to match")
		fmt.Fprintln(os.Stderr, "            its word-based labels, globals and pointers")
	}
	flag.Parse()

	if *in == "" {
		flag.Usage()
		os.Exit(2)
	}

	src, err := os.ReadFile(*in)
	if err != nil {
		fmt.Fprintf(os.Stderr, "read input: %v\n", err)
		os.Exit(1)
	}

	loadAddress, err := foxc.ParseLoadAddress(*loadAddressText)
	if err != nil {
		fmt.Fprintf(os.Stderr, "invalid load address: %v\n", err)
		os.Exit(2)
	}

	globalBase, err := foxc.ParseLoadAddress(*globalBaseText)
	if err != nil {
		fmt.Fprintf(os.Stderr, "invalid global base: %v\n", err)
		os.Exit(2)
	}
	if globalBase > 0xFFFF {
		fmt.Fprintf(os.Stderr, "invalid global base: must fit in a 16-bit word address\n")
		os.Exit(2)
	}
	if err := validateMapper(*mapper); err != nil {
		fmt.Fprintf(os.Stderr, "invalid mapper: %v\n", err)
		os.Exit(2)
	}

	if err := validateRomVersion(*romVersion); err != nil {
		fmt.Fprintf(os.Stderr, "invalid rom version: %v\n", err)
		os.Exit(2)
	}

	asm, err := foxc.CompileWithOptions(string(src), foxc.CompileOptions{
		Mode:        *mode,
		LoadAddress: loadAddress,
		GlobalBase:  globalBase,
		SourcePath:  *in,
		IncludeDirs: includeDirs,
	})
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
		"--load-address", fmt.Sprintf("0x%04X", loadAddress),
	}

	if *strictFormat {
		asmCmd = append(asmCmd, "--strict-format")
	}

	if *mapper != "" {
		asmCmd = append(asmCmd, "--mapper", *mapper)
	}

	if *romVersion != "" {
		asmCmd = append(asmCmd, "--rom-version", *romVersion)
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

// validateMapper mirrors the assembler's --mapper argument handling so FoxC
// fails fast instead of forwarding a value fox16asm would reject.
func validateMapper(value string) error {
	switch value {
	case "", "0", "rom4k", "1", "rom32k", "2", "rombyte8_32", "byte8_32":
		return nil
	}
	return fmt.Errorf("%q (expected 0|rom4k, 1|rom32k, or 2|rombyte8_32)", value)
}

func validateRomVersion(value string) error {
	switch value {
	case "", "1", "2", "3":
		return nil
	}
	return fmt.Errorf("%q (expected 1, 2, or 3)", value)
}

type stringSlice []string

func (s *stringSlice) String() string {
	if s == nil {
		return ""
	}
	return strings.Join(*s, ",")
}

func (s *stringSlice) Set(value string) error {
	*s = append(*s, value)
	return nil
}
