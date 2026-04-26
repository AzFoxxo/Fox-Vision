package foxc

import (
	"fmt"
	"strconv"
)

type parser struct {
	tokens []token
	pos    int
}

func parse(tokens []token) (*Program, error) {
	p := &parser{tokens: tokens}
	prog := &Program{}
	for !p.at(tokEOF) {
		typ, err := p.parseType()
		if err != nil {
			return nil, err
		}
		nameTok, err := p.expect(tokIdent, "expected identifier")
		if err != nil {
			return nil, err
		}
		if p.at(tokLParen) {
			fn, err := p.parseFunction(typ, nameTok.text)
			if err != nil {
				return nil, err
			}
			prog.Funcs = append(prog.Funcs, fn)
			continue
		}
		decl, err := p.parseVarDeclTail(typ, nameTok.text)
		if err != nil {
			return nil, err
		}
		prog.Globals = append(prog.Globals, decl)
	}
	return prog, nil
}

func (p *parser) parseFunction(ret TypeKind, name string) (*FuncDecl, error) {
	if _, err := p.expect(tokLParen, "expected '('"); err != nil {
		return nil, err
	}
	var params []Param
	if !p.at(tokRParen) {
		for {
			typ, err := p.parseType()
			if err != nil {
				return nil, err
			}
			paramName, err := p.expect(tokIdent, "expected parameter name")
			if err != nil {
				return nil, err
			}
			params = append(params, Param{Name: paramName.text, Type: typ})
			if p.at(tokComma) {
				p.next()
				continue
			}
			break
		}
	}
	if _, err := p.expect(tokRParen, "expected ')'"); err != nil {
		return nil, err
	}
	body, err := p.parseBlock()
	if err != nil {
		return nil, err
	}
	return &FuncDecl{Name: name, Ret: ret, Params: params, Body: body}, nil
}

func (p *parser) parseBlock() ([]Stmt, error) {
	if _, err := p.expect(tokLBrace, "expected '{'"); err != nil {
		return nil, err
	}
	var out []Stmt
	for !p.at(tokRBrace) {
		stmt, err := p.parseStmt()
		if err != nil {
			return nil, err
		}
		out = append(out, stmt)
	}
	if _, err := p.expect(tokRBrace, "expected '}'"); err != nil {
		return nil, err
	}
	return out, nil
}

func (p *parser) parseStmt() (Stmt, error) {
	if p.at(tokType) {
		typ, err := p.parseType()
		if err != nil {
			return nil, err
		}
		nameTok, err := p.expect(tokIdent, "expected variable name")
		if err != nil {
			return nil, err
		}
		decl, err := p.parseVarDeclTail(typ, nameTok.text)
		if err != nil {
			return nil, err
		}
		return &VarDeclStmt{Decl: decl}, nil
	}

	if p.at(tokIf) {
		p.next()
		if _, err := p.expect(tokLParen, "expected '('"); err != nil {
			return nil, err
		}
		cond, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokRParen, "expected ')'"); err != nil {
			return nil, err
		}
		thenBody, err := p.parseBlock()
		if err != nil {
			return nil, err
		}
		var elseBody []Stmt
		if p.at(tokElse) {
			p.next()
			elseBody, err = p.parseBlock()
			if err != nil {
				return nil, err
			}
		}
		return &IfStmt{Cond: cond, Then: thenBody, Else: elseBody}, nil
	}

	if p.at(tokWhile) {
		p.next()
		if _, err := p.expect(tokLParen, "expected '('"); err != nil {
			return nil, err
		}
		cond, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokRParen, "expected ')'"); err != nil {
			return nil, err
		}
		body, err := p.parseBlock()
		if err != nil {
			return nil, err
		}
		return &WhileStmt{Cond: cond, Body: body}, nil
	}

	if p.at(tokReturn) {
		p.next()
		if p.at(tokSemi) {
			p.next()
			return &ReturnStmt{}, nil
		}
		expr, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokSemi, "expected ';'"); err != nil {
			return nil, err
		}
		return &ReturnStmt{Value: expr}, nil
	}

	if p.at(tokIdent) && p.peek(1).kind == tokAssign {
		name := p.next().text
		p.next()
		value, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokSemi, "expected ';'"); err != nil {
			return nil, err
		}
		return &AssignStmt{Name: name, Value: value}, nil
	}

	expr, err := p.parseExpr()
	if err != nil {
		return nil, err
	}
	if _, err := p.expect(tokSemi, "expected ';'"); err != nil {
		return nil, err
	}
	return &ExprStmt{Value: expr}, nil
}

func (p *parser) parseVarDeclTail(typ TypeKind, name string) (*VarDecl, error) {
	decl := &VarDecl{Name: name, Type: typ}
	if p.at(tokAssign) {
		p.next()
		expr, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		decl.Init = expr
	}
	if _, err := p.expect(tokSemi, "expected ';'"); err != nil {
		return nil, err
	}
	return decl, nil
}

func (p *parser) parseExpr() (Expr, error) {
	return p.parseEquality()
}

func (p *parser) parseEquality() (Expr, error) {
	left, err := p.parseCompare()
	if err != nil {
		return nil, err
	}
	for p.at(tokEq) || p.at(tokNe) {
		op := p.next().text
		right, err := p.parseCompare()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseCompare() (Expr, error) {
	left, err := p.parseAdd()
	if err != nil {
		return nil, err
	}
	for p.at(tokLt) || p.at(tokGt) || p.at(tokLe) || p.at(tokGe) {
		op := p.next().text
		right, err := p.parseAdd()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseAdd() (Expr, error) {
	left, err := p.parseMul()
	if err != nil {
		return nil, err
	}
	for p.at(tokPlus) || p.at(tokMinus) {
		op := p.next().text
		right, err := p.parseMul()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseMul() (Expr, error) {
	left, err := p.parseUnary()
	if err != nil {
		return nil, err
	}
	for p.at(tokStar) || p.at(tokSlash) || p.at(tokAmp) {
		op := p.next().text
		right, err := p.parseUnary()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseUnary() (Expr, error) {
	if p.at(tokMinus) {
		p.next()
		x, err := p.parseUnary()
		if err != nil {
			return nil, err
		}
		return &UnaryExpr{Op: "-", X: x}, nil
	}
	return p.parsePrimary()
}

func (p *parser) parsePrimary() (Expr, error) {
	if p.at(tokNumber) {
		tok := p.next()
		v, _ := strconv.Atoi(tok.text)
		return &IntLit{Value: v}, nil
	}
	if p.at(tokIdent) {
		name := p.next().text
		if p.at(tokLParen) {
			p.next()
			var args []Expr
			if !p.at(tokRParen) {
				for {
					a, err := p.parseExpr()
					if err != nil {
						return nil, err
					}
					args = append(args, a)
					if p.at(tokComma) {
						p.next()
						continue
					}
					break
				}
			}
			if _, err := p.expect(tokRParen, "expected ')'"); err != nil {
				return nil, err
			}
			return &CallExpr{Callee: name, Args: args}, nil
		}
		return &IdentExpr{Name: name}, nil
	}
	if p.at(tokLParen) {
		p.next()
		e, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokRParen, "expected ')'"); err != nil {
			return nil, err
		}
		return e, nil
	}
	curr := p.curr()
	return nil, fmt.Errorf("unexpected token %q at %d:%d", curr.text, curr.line, curr.col)
}

func (p *parser) parseType() (TypeKind, error) {
	tok, err := p.expect(tokType, "expected type")
	if err != nil {
		return TypeInvalid, err
	}
	switch tok.text {
	case "u8":
		return TypeU8, nil
	case "u16":
		return TypeU16, nil
	case "void":
		return TypeVoid, nil
	default:
		return TypeInvalid, fmt.Errorf("unknown type %q at %d:%d", tok.text, tok.line, tok.col)
	}
}

func (p *parser) at(k tokenKind) bool {
	return p.curr().kind == k
}

func (p *parser) curr() token {
	if p.pos >= len(p.tokens) {
		return token{kind: tokEOF}
	}
	return p.tokens[p.pos]
}

func (p *parser) peek(n int) token {
	i := p.pos + n
	if i >= len(p.tokens) {
		return token{kind: tokEOF}
	}
	return p.tokens[i]
}

func (p *parser) next() token {
	t := p.curr()
	if p.pos < len(p.tokens) {
		p.pos++
	}
	return t
}

func (p *parser) expect(k tokenKind, msg string) (token, error) {
	t := p.curr()
	if t.kind != k {
		return token{}, fmt.Errorf("%s at %d:%d", msg, t.line, t.col)
	}
	p.pos++
	return t, nil
}
