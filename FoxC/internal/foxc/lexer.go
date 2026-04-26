package foxc

import (
	"fmt"
	"strconv"
	"unicode"
)

type lexer struct {
	src  []rune
	pos  int
	line int
	col  int
}

func lex(input string) ([]token, error) {
	l := &lexer{src: []rune(input), line: 1, col: 1}
	var out []token
	for {
		tok, err := l.nextToken()
		if err != nil {
			return nil, err
		}
		out = append(out, tok)
		if tok.kind == tokEOF {
			return out, nil
		}
	}
}

func (l *lexer) nextToken() (token, error) {
	l.skipWhitespaceAndComments()
	if l.eof() {
		return token{kind: tokEOF, line: l.line, col: l.col}, nil
	}

	ch := l.peek()
	line, col := l.line, l.col

	if isIdentStart(ch) {
		text := l.readWhile(func(r rune) bool { return isIdentStart(r) || unicode.IsDigit(r) })
		switch text {
		case "u8", "u16", "void":
			return token{kind: tokType, text: text, line: line, col: col}, nil
		case "if":
			return token{kind: tokIf, text: text, line: line, col: col}, nil
		case "else":
			return token{kind: tokElse, text: text, line: line, col: col}, nil
		case "while":
			return token{kind: tokWhile, text: text, line: line, col: col}, nil
		case "return":
			return token{kind: tokReturn, text: text, line: line, col: col}, nil
		default:
			return token{kind: tokIdent, text: text, line: line, col: col}, nil
		}
	}

	if unicode.IsDigit(ch) {
		text := l.readWhile(unicode.IsDigit)
		if _, err := strconv.Atoi(text); err != nil {
			return token{}, fmt.Errorf("invalid number %q at %d:%d", text, line, col)
		}
		return token{kind: tokNumber, text: text, line: line, col: col}, nil
	}

	l.advance()
	switch ch {
	case '(':
		return token{kind: tokLParen, text: "(", line: line, col: col}, nil
	case ')':
		return token{kind: tokRParen, text: ")", line: line, col: col}, nil
	case '{':
		return token{kind: tokLBrace, text: "{", line: line, col: col}, nil
	case '}':
		return token{kind: tokRBrace, text: "}", line: line, col: col}, nil
	case ',':
		return token{kind: tokComma, text: ",", line: line, col: col}, nil
	case ';':
		return token{kind: tokSemi, text: ";", line: line, col: col}, nil
	case '+':
		return token{kind: tokPlus, text: "+", line: line, col: col}, nil
	case '-':
		return token{kind: tokMinus, text: "-", line: line, col: col}, nil
	case '*':
		return token{kind: tokStar, text: "*", line: line, col: col}, nil
	case '/':
		return token{kind: tokSlash, text: "/", line: line, col: col}, nil
	case '&':
		return token{kind: tokAmp, text: "&", line: line, col: col}, nil
	case '=':
		if l.match('=') {
			return token{kind: tokEq, text: "==", line: line, col: col}, nil
		}
		return token{kind: tokAssign, text: "=", line: line, col: col}, nil
	case '!':
		if l.match('=') {
			return token{kind: tokNe, text: "!=", line: line, col: col}, nil
		}
		return token{}, fmt.Errorf("unexpected '!' at %d:%d", line, col)
	case '<':
		if l.match('=') {
			return token{kind: tokLe, text: "<=", line: line, col: col}, nil
		}
		return token{kind: tokLt, text: "<", line: line, col: col}, nil
	case '>':
		if l.match('=') {
			return token{kind: tokGe, text: ">=", line: line, col: col}, nil
		}
		return token{kind: tokGt, text: ">", line: line, col: col}, nil
	default:
		return token{}, fmt.Errorf("unexpected character %q at %d:%d", ch, line, col)
	}
}

func (l *lexer) skipWhitespaceAndComments() {
	for !l.eof() {
		if unicode.IsSpace(l.peek()) {
			l.advance()
			continue
		}
		if l.peek() == '/' && l.peekNext() == '/' {
			for !l.eof() && l.peek() != '\n' {
				l.advance()
			}
			continue
		}
		break
	}
}

func (l *lexer) readWhile(pred func(rune) bool) string {
	start := l.pos
	for !l.eof() && pred(l.peek()) {
		l.advance()
	}
	return string(l.src[start:l.pos])
}

func (l *lexer) eof() bool {
	return l.pos >= len(l.src)
}

func (l *lexer) peek() rune {
	return l.src[l.pos]
}

func (l *lexer) peekNext() rune {
	if l.pos+1 >= len(l.src) {
		return 0
	}
	return l.src[l.pos+1]
}

func (l *lexer) match(r rune) bool {
	if l.eof() || l.peek() != r {
		return false
	}
	l.advance()
	return true
}

func (l *lexer) advance() {
	if l.eof() {
		return
	}
	if l.src[l.pos] == '\n' {
		l.line++
		l.col = 1
	} else {
		l.col++
	}
	l.pos++
}

func isIdentStart(r rune) bool {
	return r == '_' || unicode.IsLetter(r)
}
