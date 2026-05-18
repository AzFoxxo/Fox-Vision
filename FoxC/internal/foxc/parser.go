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
		base, err := p.parseType()
		if err != nil {
			return nil, err
		}
		// allow pointer declarators: '*' between type and identifier
		ptr := 0
		for p.at(tokStar) {
			p.next()
			ptr++
		}
		nameTok, err := p.expect(tokIdent, "expected identifier")
		if err != nil {
			return nil, err
		}
		// build Type
		typ := Type{Base: base, Ptr: ptr}
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

func (p *parser) parseFunction(ret Type, name string) (*FuncDecl, error) {
	if _, err := p.expect(tokLParen, "expected '('"); err != nil {
		return nil, err
	}
	var params []Param
	if !p.at(tokRParen) {
		for {
			base, err := p.parseType()
			if err != nil {
				return nil, err
			}
			// allow pointer declarator in parameter
			ptr := 0
			for p.at(tokStar) {
				p.next()
				ptr++
			}
			paramName, err := p.expect(tokIdent, "expected parameter name")
			if err != nil {
				return nil, err
			}
			params = append(params, Param{Name: paramName.text, Type: Type{Base: base, Ptr: ptr}})
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
		base, err := p.parseType()
		if err != nil {
			return nil, err
		}
		// allow pointer declarators on local vars too: '*' between type and identifier
		ptr := 0
		for p.at(tokStar) {
			p.next()
			ptr++
		}
		nameTok, err := p.expect(tokIdent, "expected variable name")
		if err != nil {
			return nil, err
		}
		typ := Type{Base: base, Ptr: ptr}
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

	// Support assignment to lvalues: identifiers, indexed elements, and dereferenced pointers
	if p.startsAssignment() {
		lval, err := p.parseLValue()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokAssign, "expected '='"); err != nil {
			return nil, err
		}
		value, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokSemi, "expected ';'"); err != nil {
			return nil, err
		}
		return &AssignStmt{LHS: lval, Value: value}, nil
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

func (p *parser) parseLValue() (Expr, error) {
	// support unary '*' chains
	if p.at(tokStar) {
		p.next()
		x, err := p.parseLValue()
		if err != nil {
			return nil, err
		}
		return &UnaryExpr{Op: "*", X: x}, nil
	}
	// support parenthesized expressions like *(expr)
	if p.at(tokLParen) {
		p.next()
		x, err := p.parseExpr()
		if err != nil {
			return nil, err
		}
		if _, err := p.expect(tokRParen, "expected ')'"); err != nil {
			return nil, err
		}
		return x, nil
	}
	if p.at(tokIdent) {
		name := p.next().text
		if p.at(tokLBracket) {
			p.next()
			index, err := p.parseExpr()
			if err != nil {
				return nil, err
			}
			if _, err := p.expect(tokRBracket, "expected ']'"); err != nil {
				return nil, err
			}
			return &IndexExpr{Name: name, Index: index}, nil
		}
		return &IdentExpr{Name: name}, nil
	}
	return nil, fmt.Errorf("expected lvalue")
}

func (p *parser) startsAssignment() bool {
	// lookahead to see if tokens form an lvalue followed by '='
	i := 0
	// consume leading '*'
	for p.peek(i).kind == tokStar {
		i++
	}
	// handle parenthesized expressions
	if p.peek(i).kind == tokLParen {
		depth := 1
		i++
		for depth > 0 {
			k := p.peek(i)
			if k.kind == tokEOF {
				return false
			}
			if k.kind == tokLParen {
				depth++
			} else if k.kind == tokRParen {
				depth--
			}
			i++
		}
		return p.peek(i).kind == tokAssign
	}
	if p.peek(i).kind != tokIdent {
		return false
	}
	i++
	// optional indexing
	if p.peek(i).kind == tokLBracket {
		depth := 0
		for {
			k := p.peek(i)
			if k.kind == tokLBracket {
				depth++
			} else if k.kind == tokRBracket {
				depth--
				if depth == 0 {
					i++
					break
				}
			} else if k.kind == tokEOF {
				return false
			}
			i++
		}
	}
	return p.peek(i).kind == tokAssign
}

func (p *parser) parseVarDeclTail(typ Type, name string) (*VarDecl, error) {
	decl := &VarDecl{Name: name, Type: typ}
	if p.at(tokLBracket) {
		p.next()
		lenTok, err := p.expect(tokNumber, "expected array length")
		if err != nil {
			return nil, err
		}
		arrLen, err := strconv.Atoi(lenTok.text)
		if err != nil || arrLen <= 0 {
			return nil, fmt.Errorf("invalid array length %q at %d:%d", lenTok.text, lenTok.line, lenTok.col)
		}
		if _, err := p.expect(tokRBracket, "expected ']'"); err != nil {
			return nil, err
		}
		decl.IsArray = true
		decl.ArrayLen = arrLen
	}
	if p.at(tokAssign) {
		if decl.IsArray {
			curr := p.curr()
			return nil, fmt.Errorf("array initializer is not supported at %d:%d", curr.line, curr.col)
		}
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
	return p.parseLogicalOr()
}

func (p *parser) parseLogicalOr() (Expr, error) {
	left, err := p.parseLogicalAnd()
	if err != nil {
		return nil, err
	}
	for p.at(tokOrOr) {
		op := p.next().text
		right, err := p.parseLogicalAnd()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseLogicalAnd() (Expr, error) {
	left, err := p.parseBitwiseOr()
	if err != nil {
		return nil, err
	}
	for p.at(tokAndAnd) {
		op := p.next().text
		right, err := p.parseBitwiseOr()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseBitwiseOr() (Expr, error) {
	left, err := p.parseBitwiseXor()
	if err != nil {
		return nil, err
	}
	for p.at(tokPipe) {
		op := p.next().text
		right, err := p.parseBitwiseXor()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseBitwiseXor() (Expr, error) {
	left, err := p.parseBitwiseAnd()
	if err != nil {
		return nil, err
	}
	for p.at(tokCaret) {
		op := p.next().text
		right, err := p.parseBitwiseAnd()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseBitwiseAnd() (Expr, error) {
	left, err := p.parseEquality()
	if err != nil {
		return nil, err
	}
	for p.at(tokAmp) {
		op := p.next().text
		right, err := p.parseEquality()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
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
	left, err := p.parseShift()
	if err != nil {
		return nil, err
	}
	for p.at(tokLt) || p.at(tokGt) || p.at(tokLe) || p.at(tokGe) {
		op := p.next().text
		right, err := p.parseShift()
		if err != nil {
			return nil, err
		}
		left = &BinaryExpr{Op: op, Left: left, Right: right}
	}
	return left, nil
}

func (p *parser) parseShift() (Expr, error) {
	left, err := p.parseAdd()
	if err != nil {
		return nil, err
	}
	for p.at(tokShl) || p.at(tokShr) {
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
	for p.at(tokStar) || p.at(tokSlash) {
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
	if p.at(tokTilde) {
		p.next()
		x, err := p.parseUnary()
		if err != nil {
			return nil, err
		}
		return &UnaryExpr{Op: "~", X: x}, nil
	}
	if p.at(tokStar) {
		p.next()
		x, err := p.parseUnary()
		if err != nil {
			return nil, err
		}
		return &UnaryExpr{Op: "*", X: x}, nil
	}
	if p.at(tokAmp) {
		p.next()
		x, err := p.parseUnary()
		if err != nil {
			return nil, err
		}
		return &UnaryExpr{Op: "&", X: x}, nil
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
		if p.at(tokLBracket) {
			p.next()
			index, err := p.parseExpr()
			if err != nil {
				return nil, err
			}
			if _, err := p.expect(tokRBracket, "expected ']'"); err != nil {
				return nil, err
			}
			return &IndexExpr{Name: name, Index: index}, nil
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

func (p *parser) startsIndexedAssignment() bool {
	if !p.at(tokIdent) || p.peek(1).kind != tokLBracket {
		return false
	}
	depth := 0
	for i := 1; ; i++ {
		t := p.peek(i)
		if t.kind == tokEOF {
			return false
		}
		switch t.kind {
		case tokLBracket:
			depth++
		case tokRBracket:
			depth--
			if depth == 0 {
				return p.peek(i+1).kind == tokAssign
			}
		}
	}
}
