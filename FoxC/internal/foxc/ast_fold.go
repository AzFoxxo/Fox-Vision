package foxc

func foldConstantsProgram(p *Program) {
	for _, g := range p.Globals {
		if g.Init != nil {
			g.Init = foldExpr(g.Init)
		}
	}
	for _, fn := range p.Funcs {
		for i, st := range fn.Body {
			fn.Body[i] = foldStmt(st)
		}
	}
}

func foldStmt(st Stmt) Stmt {
	switch n := st.(type) {
	case *VarDeclStmt:
		if n.Decl.Init != nil {
			n.Decl.Init = foldExpr(n.Decl.Init)
		}
	case *AssignStmt:
		n.Value = foldExpr(n.Value)
	case *AssignIndexStmt:
		n.Index = foldExpr(n.Index)
		n.Value = foldExpr(n.Value)
	case *ExprStmt:
		n.Value = foldExpr(n.Value)
	case *ReturnStmt:
		if n.Value != nil {
			n.Value = foldExpr(n.Value)
		}
	case *IfStmt:
		n.Cond = foldExpr(n.Cond)
		for i, item := range n.Then {
			n.Then[i] = foldStmt(item)
		}
		for i, item := range n.Else {
			n.Else[i] = foldStmt(item)
		}
	case *WhileStmt:
		n.Cond = foldExpr(n.Cond)
		for i, item := range n.Body {
			n.Body[i] = foldStmt(item)
		}
	}
	return st
}

func foldExpr(e Expr) Expr {
	switch n := e.(type) {
	case *UnaryExpr:
		n.X = foldExpr(n.X)
		if lit, ok := n.X.(*IntLit); ok {
			switch n.Op {
			case "-":
				if lit.Value > 0xFFFF {
					return &IntLit{Value: int(uint32(0) - uint32(lit.Value))}
				}
				return &IntLit{Value: normalize16(-lit.Value)}
			case "~":
				if lit.Value > 0xFFFF {
					return &IntLit{Value: int(^uint32(lit.Value))}
				}
				return &IntLit{Value: int(^uint16(lit.Value))}
			}
		}
		return n
	case *IndexExpr:
		n.Index = foldExpr(n.Index)
		return n
	case *BinaryExpr:
		n.Left = foldExpr(n.Left)
		n.Right = foldExpr(n.Right)
		l, lok := n.Left.(*IntLit)
		r, rok := n.Right.(*IntLit)
		if !lok || !rok {
			return n
		}
		if v, ok := foldIntegerBinary(l.Value, r.Value, n.Op); ok {
			return &IntLit{Value: v}
		}
		return n
	case *CallExpr:
		for i, a := range n.Args {
			n.Args[i] = foldExpr(a)
		}
		return n
	default:
		return n
	}
}

func normalize16(v int) int {
	return int(uint16(v))
}

// foldIntegerBinary folds integer constant expressions. Operations widen to
// 32 bits when either literal does not fit in 16 bits; otherwise the existing
// 16-bit wraparound behaviour is preserved.
func foldIntegerBinary(lv, rv int, op string) (int, bool) {
	switch op {
	case "==":
		if lv == rv {
			return 1, true
		}
		return 0, true
	case "!=":
		if lv != rv {
			return 1, true
		}
		return 0, true
	case "&&":
		if lv != 0 && rv != 0 {
			return 1, true
		}
		return 0, true
	case "||":
		if lv != 0 || rv != 0 {
			return 1, true
		}
		return 0, true
	}

	wide := lv > 0xFFFF || rv > 0xFFFF
	if wide {
		a, b := uint32(lv), uint32(rv)
		switch op {
		case "+":
			return int(a + b), true
		case "-":
			return int(a - b), true
		case "*":
			return int(a * b), true
		case "/":
			if b == 0 {
				return 0, false
			}
			return int(a / b), true
		case "&":
			return int(a & b), true
		case "|":
			return int(a | b), true
		case "^":
			return int(a ^ b), true
		case "<<":
			if b >= 32 {
				return 0, false
			}
			return int(a << b), true
		case ">>":
			if b >= 32 {
				return 0, false
			}
			return int(a >> b), true
		case "<":
			if a < b {
				return 1, true
			}
			return 0, true
		case ">":
			if a > b {
				return 1, true
			}
			return 0, true
		case "<=":
			if a <= b {
				return 1, true
			}
			return 0, true
		case ">=":
			if a >= b {
				return 1, true
			}
			return 0, true
		}
		return 0, false
	}

	a, b := uint16(lv), uint16(rv)
	switch op {
	case "+":
		return int(a + b), true
	case "-":
		return int(a - b), true
	case "*":
		return int(a * b), true
	case "/":
		if b == 0 {
			return 0, false
		}
		return int(a / b), true
	case "&":
		return int(a & b), true
	case "|":
		return int(a | b), true
	case "^":
		return int(a ^ b), true
	case "<<":
		if b >= 16 {
			return 0, false
		}
		return int(uint16(a << b)), true
	case ">>":
		if b >= 16 {
			return 0, false
		}
		return int(a >> b), true
	case "<":
		if a < b {
			return 1, true
		}
		return 0, true
	case ">":
		if a > b {
			return 1, true
		}
		return 0, true
	case "<=":
		if a <= b {
			return 1, true
		}
		return 0, true
	case ">=":
		if a >= b {
			return 1, true
		}
		return 0, true
	}
	return 0, false
}
