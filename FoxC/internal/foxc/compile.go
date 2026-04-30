package foxc

import "strings"

type CompileOptions struct {
	Mode string
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
