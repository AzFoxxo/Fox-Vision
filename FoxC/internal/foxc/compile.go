package foxc

import (
	"strconv"
	"strings"
)

type CompileOptions struct {
	Mode        string
	LoadAddress uint16
}

func Compile(src string) (string, error) {
	return CompileWithOptions(src, CompileOptions{})
}

func CompileWithOptions(src string, opts CompileOptions) (string, error) {
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
	return strings.EqualFold(strings.TrimSpace(mode), "extended")
}

func ParseLoadAddress(value string) (uint16, error) {
	value = strings.TrimSpace(value)
	if strings.HasPrefix(value, "$") {
		value = "0x" + value[1:]
	}
	n, err := strconv.ParseUint(value, 0, 16)
	return uint16(n), err
}
