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
		if n.Op == "-" {
			if lit, ok := n.X.(*IntLit); ok {
				return &IntLit{Value: normalize16(-lit.Value)}
			}
		}
		return n
	case *BinaryExpr:
		n.Left = foldExpr(n.Left)
		n.Right = foldExpr(n.Right)
		l, lok := n.Left.(*IntLit)
		r, rok := n.Right.(*IntLit)
		if !lok || !rok {
			return n
		}
		lv := uint16(l.Value)
		rv := uint16(r.Value)
		switch n.Op {
		case "+":
			return &IntLit{Value: int(lv + rv)}
		case "-":
			return &IntLit{Value: int(lv - rv)}
		case "*":
			return &IntLit{Value: int(lv * rv)}
		case "/":
			if rv == 0 {
				return n
			}
			return &IntLit{Value: int(lv / rv)}
		case "&":
			return &IntLit{Value: int(lv & rv)}
		case "==":
			if lv == rv {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		case "!=":
			if lv != rv {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		case "<":
			if lv < rv {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		case ">":
			if lv > rv {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		case "<=":
			if lv <= rv {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		case ">=":
			if lv >= rv {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		case "&&":
			if lv != 0 && rv != 0 {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		case "||":
			if lv != 0 || rv != 0 {
				return &IntLit{Value: 1}
			}
			return &IntLit{Value: 0}
		default:
			return n
		}
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
