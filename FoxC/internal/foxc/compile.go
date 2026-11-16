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
	return strings.EqualFold(strings.TrimSpace(mode), "extended") || isEm2Am1Mode(mode)
}

func isEm2Am1Mode(mode string) bool {
	m := strings.ToLower(strings.TrimSpace(mode))
	return m == "em2_am1" || m == "am1_em2" || m == "em2" || m == "v2" || m == "extended2"
}

func ParseLoadAddress(value string) (uint16, error) {
	value = strings.TrimSpace(value)
	if strings.HasPrefix(value, "$") {
		value = "0x" + value[1:]
	}
	n, err := strconv.ParseUint(value, 0, 16)
	return uint16(n), err
}
