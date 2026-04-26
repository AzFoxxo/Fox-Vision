package foxc

func Compile(src string) (string, error) {
	tokens, err := lex(src)
	if err != nil {
		return "", err
	}
	ast, err := parse(tokens)
	if err != nil {
		return "", err
	}
	foldConstantsProgram(ast)
	if err := check(ast); err != nil {
		return "", err
	}
	return generate(ast)
}
