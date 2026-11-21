package foxc

import (
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

type CompileOptions struct {
	Mode        string
	LoadAddress uint32
	// GlobalBase overrides the word address where globals are allocated.
	// Resident images whose text grows beyond the default $A000 need a
	// higher base so code and globals do not overlap.
	GlobalBase uint32
	// SourcePath is the path of src, used to resolve relative includes.
	SourcePath string
	// IncludeDirs are searched for #include directives.
	IncludeDirs []string
}

func Compile(src string) (string, error) {
	return CompileWithOptions(src, CompileOptions{})
}

func CompileWithOptions(src string, opts CompileOptions) (string, error) {
	src, err := Preprocess(src, PreprocessOptions{
		SourcePath:  opts.SourcePath,
		IncludeDirs: opts.IncludeDirs,
	})
	if err != nil {
		return "", err
	}
	tokens, err := lex(src)
	if err != nil {
		return "", err
	}
	ast, err := parse(tokens)
	if err != nil {
		return "", err
	}
	foldConstantsProgram(ast)
	if err := check(ast, opts); err != nil {
		return "", err
	}
	return generate(ast, opts)
}

func isExtendedMode(mode string) bool {
	return strings.EqualFold(strings.TrimSpace(mode), "extended") || isEm2Am1Mode(mode)
}

func isEm2Am1Mode(mode string) bool {
	m := strings.ToLower(strings.TrimSpace(mode))
	return m == "em2_am1" || m == "am1_em2" || m == "em2" || m == "v2" || m == "extended2"
}

func ParseLoadAddress(value string) (uint32, error) {
	value = strings.TrimSpace(value)
	if strings.HasPrefix(value, "$") {
		value = "0x" + value[1:]
	}
	n, err := strconv.ParseUint(value, 0, 32)
	return uint32(n), err
}

// PreprocessOptions controls header inclusion for a translation unit.
type PreprocessOptions struct {
	// SourcePath is the path of the file being compiled. Relative quoted
	// includes are resolved against its directory first.
	SourcePath string
	// IncludeDirs are searched for quoted includes (after the source directory)
	// and angle-bracket includes (in order).
	IncludeDirs []string
}

type preprocessor struct {
	opts     PreprocessOptions
	included map[string]bool
	stack    []string
}

// Preprocess expands FoxC include directives into a single source string.
// Headers are included at most once per translation unit, which allows a
// header to contain either signatures or complete definitions without
// duplicate-symbol errors when it is included from several places.
func Preprocess(src string, opts PreprocessOptions) (string, error) {
	pp := &preprocessor{
		opts:     opts,
		included: map[string]bool{},
	}
	base := opts.SourcePath
	if strings.TrimSpace(base) == "" {
		base = "input.fc"
	}
	absBase, err := filepath.Abs(base)
	if err != nil {
		return "", fmt.Errorf("resolve source path %q: %w", base, err)
	}
	return pp.expand(src, absBase)
}

// PreprocessFile reads path and expands its include directives.
func PreprocessFile(path string, opts PreprocessOptions) (string, error) {
	abs, err := filepath.Abs(path)
	if err != nil {
		return "", fmt.Errorf("resolve source path %q: %w", path, err)
	}
	data, err := os.ReadFile(abs)
	if err != nil {
		return "", fmt.Errorf("read input: %w", err)
	}
	opts.SourcePath = abs
	return Preprocess(string(data), opts)
}

func (pp *preprocessor) expand(src, path string) (string, error) {
	canonical := filepath.Clean(path)
	for _, active := range pp.stack {
		if active == canonical {
			return "", fmt.Errorf("recursive include of %q", path)
		}
	}
	if pp.included[canonical] {
		return "\n", nil
	}
	pp.included[canonical] = true
	pp.stack = append(pp.stack, canonical)
	defer func() { pp.stack = pp.stack[:len(pp.stack)-1] }()

	var out strings.Builder
	for _, line := range strings.Split(src, "\n") {
		trimmed := strings.TrimSpace(line)
		switch {
		case strings.HasPrefix(trimmed, "#include"):
			target, angled, err := parseIncludeDirective(trimmed)
			if err != nil {
				return "", fmt.Errorf("%s: %w", path, err)
			}
			resolved, err := pp.resolve(path, target, angled)
			if err != nil {
				return "", fmt.Errorf("%s: %w", path, err)
			}
			data, err := os.ReadFile(resolved)
			if err != nil {
				return "", fmt.Errorf("%s: include %q: %w", path, target, err)
			}
			out.WriteString("// begin include ")
			out.WriteString(resolved)
			out.WriteByte('\n')
			expanded, err := pp.expand(string(data), resolved)
			if err != nil {
				return "", err
			}
			out.WriteString(expanded)
			if !strings.HasSuffix(expanded, "\n") {
				out.WriteByte('\n')
			}
			out.WriteString("// end include ")
			out.WriteString(resolved)
			out.WriteByte('\n')
		case trimmed == "#pragma once":
			// Headers are already included at most once; accept the directive
			// so conventional headers can be used unchanged.
			out.WriteByte('\n')
		default:
			out.WriteString(line)
			out.WriteByte('\n')
		}
	}
	return out.String(), nil
}

func parseIncludeDirective(line string) (string, bool, error) {
	rest := strings.TrimSpace(strings.TrimPrefix(line, "#include"))
	if rest == "" {
		return "", false, fmt.Errorf("#include requires a path")
	}
	var open, close byte
	switch rest[0] {
	case '"':
		open, close = '"', '"'
	case '<':
		open, close = '<', '>'
	default:
		return "", false, fmt.Errorf("#include path must be quoted or angle-bracketed")
	}
	if len(rest) < 2 {
		return "", false, fmt.Errorf("unterminated #include path")
	}
	end := strings.IndexByte(rest[1:], close)
	if end < 0 {
		return "", false, fmt.Errorf("unterminated #include path")
	}
	target := rest[1 : 1+end]
	if target == "" {
		return "", false, fmt.Errorf("empty #include path")
	}
	trailing := strings.TrimSpace(rest[2+end:])
	if trailing != "" && !strings.HasPrefix(trailing, "//") {
		return "", false, fmt.Errorf("unexpected text after #include path")
	}
	return target, open == '<', nil
}

func (pp *preprocessor) resolve(current, target string, angled bool) (string, error) {
	if filepath.IsAbs(target) {
		return filepath.Clean(target), nil
	}
	var candidates []string
	currentDir := filepath.Dir(current)
	if angled {
		candidates = append(candidates, pp.opts.IncludeDirs...)
		candidates = append(candidates, currentDir)
	} else {
		candidates = append(candidates, currentDir)
		candidates = append(candidates, pp.opts.IncludeDirs...)
	}
	for _, dir := range candidates {
		dir = strings.TrimSpace(dir)
		if dir == "" {
			continue
		}
		if !filepath.IsAbs(dir) {
			abs, err := filepath.Abs(dir)
			if err != nil {
				continue
			}
			dir = abs
		}
		path := filepath.Clean(filepath.Join(dir, target))
		if info, err := os.Stat(path); err == nil && !info.IsDir() {
			return path, nil
		}
	}
	return "", fmt.Errorf("cannot find include %q", target)
}
