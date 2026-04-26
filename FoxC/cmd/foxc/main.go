package main

import (
	"flag"
	"fmt"
	"os"

	"foxc/internal/foxc"
)

func main() {
	in := flag.String("i", "", "input FoxC file")
	out := flag.String("o", "", "output .f16 file")
	flag.Parse()

	if *in == "" {
		fmt.Fprintln(os.Stderr, "usage: foxc -i <input.fc> [-o output.f16]")
		os.Exit(2)
	}

	src, err := os.ReadFile(*in)
	if err != nil {
		fmt.Fprintf(os.Stderr, "read input: %v\n", err)
		os.Exit(1)
	}

	asm, err := foxc.Compile(string(src))
	if err != nil {
		fmt.Fprintf(os.Stderr, "compile error: %v\n", err)
		os.Exit(1)
	}

	outputPath := *out
	if outputPath == "" {
		outputPath = "out.f16"
	}

	if err := os.WriteFile(outputPath, []byte(asm), 0o644); err != nil {
		fmt.Fprintf(os.Stderr, "write output: %v\n", err)
		os.Exit(1)
	}

	fmt.Printf("FoxC compiled %s -> %s\n", *in, outputPath)
}
