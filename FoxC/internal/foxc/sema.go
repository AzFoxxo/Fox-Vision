package foxc

import "fmt"

type funcSig struct {
	ret    TypeKind
	params []TypeKind
}

type semantic struct {
	funcs   map[string]funcSig
	globals map[string]TypeKind
	scopes  []map[string]TypeKind
	currFn  *FuncDecl
}

func check(prog *Program) error {
	s := &semantic{
		funcs:   map[string]funcSig{},
		globals: map[string]TypeKind{},
	}

	s.funcs["poke"] = funcSig{ret: TypeVoid, params: []TypeKind{TypeU16, TypeU16}}
	s.funcs["peak"] = funcSig{ret: TypeU16, params: []TypeKind{TypeU16}}
	s.funcs["peek"] = funcSig{ret: TypeU16, params: []TypeKind{TypeU16}}

	for _, g := range prog.Globals {
		if g.Type == TypeVoid {
			return fmt.Errorf("global %q cannot be void", g.Name)
		}
		if _, exists := s.globals[g.Name]; exists {
			return fmt.Errorf("duplicate global %q", g.Name)
		}
		s.globals[g.Name] = g.Type
	}

	for _, fn := range prog.Funcs {
		if _, exists := s.funcs[fn.Name]; exists {
			return fmt.Errorf("duplicate function %q", fn.Name)
		}
		params := make([]TypeKind, 0, len(fn.Params))
		for _, p := range fn.Params {
			if p.Type == TypeVoid {
				return fmt.Errorf("parameter %q in %q cannot be void", p.Name, fn.Name)
			}
			params = append(params, p.Type)
		}
		s.funcs[fn.Name] = funcSig{ret: fn.Ret, params: params}
	}

	mainSig, ok := s.funcs["main"]
	if !ok {
		return fmt.Errorf("missing required function main")
	}
	if mainSig.ret != TypeVoid || len(mainSig.params) != 0 {
		return fmt.Errorf("main must have signature void main()")
	}

	for _, g := range prog.Globals {
		if g.Init != nil {
			t, err := s.exprType(g.Init)
			if err != nil {
				return fmt.Errorf("global %s init: %w", g.Name, err)
			}
			if !assignable(g.Type, t) {
				return fmt.Errorf("cannot assign %s to global %s of type %s", t, g.Name, g.Type)
			}
		}
	}

	for _, fn := range prog.Funcs {
		s.currFn = fn
		s.pushScope()
		for _, p := range fn.Params {
			if err := s.declare(p.Name, p.Type); err != nil {
				return fmt.Errorf("function %s: %w", fn.Name, err)
			}
		}
		for _, st := range fn.Body {
			if err := s.checkStmt(st); err != nil {
				return fmt.Errorf("function %s: %w", fn.Name, err)
			}
		}
		if fn.Ret != TypeVoid && !stmtListAlwaysReturns(fn.Body) {
			return fmt.Errorf("function %s: non-void function must return on all paths", fn.Name)
		}
		s.popScope()
	}

	return nil
}

func (s *semantic) checkStmt(st Stmt) error {
	switch n := st.(type) {
	case *VarDeclStmt:
		if n.Decl.Type == TypeVoid {
			return fmt.Errorf("variable %q cannot be void", n.Decl.Name)
		}
		if err := s.declare(n.Decl.Name, n.Decl.Type); err != nil {
			return err
		}
		if n.Decl.Init != nil {
			t, err := s.exprType(n.Decl.Init)
			if err != nil {
				return err
			}
			if !assignable(n.Decl.Type, t) {
				return fmt.Errorf("cannot assign %s to %s of type %s", t, n.Decl.Name, n.Decl.Type)
			}
		}
	case *AssignStmt:
		lhs, ok := s.lookupVar(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		rhs, err := s.exprType(n.Value)
		if err != nil {
			return err
		}
		if !assignable(lhs, rhs) {
			return fmt.Errorf("cannot assign %s to %s of type %s", rhs, n.Name, lhs)
		}
	case *IfStmt:
		if _, err := s.exprType(n.Cond); err != nil {
			return err
		}
		s.pushScope()
		for _, item := range n.Then {
			if err := s.checkStmt(item); err != nil {
				return err
			}
		}
		s.popScope()
		s.pushScope()
		for _, item := range n.Else {
			if err := s.checkStmt(item); err != nil {
				return err
			}
		}
		s.popScope()
	case *WhileStmt:
		if _, err := s.exprType(n.Cond); err != nil {
			return err
		}
		s.pushScope()
		for _, item := range n.Body {
			if err := s.checkStmt(item); err != nil {
				return err
			}
		}
		s.popScope()
	case *ReturnStmt:
		if s.currFn.Ret == TypeVoid {
			if n.Value != nil {
				return fmt.Errorf("void function cannot return a value")
			}
			return nil
		}
		if n.Value == nil {
			return fmt.Errorf("non-void function must return a value")
		}
		t, err := s.exprType(n.Value)
		if err != nil {
			return err
		}
		if !assignable(s.currFn.Ret, t) {
			return fmt.Errorf("cannot return %s from %s function", t, s.currFn.Ret)
		}
	case *ExprStmt:
		if _, err := s.exprType(n.Value); err != nil {
			return err
		}
	default:
		return fmt.Errorf("unsupported statement")
	}
	return nil
}

func (s *semantic) exprType(e Expr) (TypeKind, error) {
	switch n := e.(type) {
	case *IntLit:
		if n.Value < 0 || n.Value > 0xFFFF {
			return TypeInvalid, fmt.Errorf("literal %d out of 16-bit range", n.Value)
		}
		if n.Value <= 0xFF {
			return TypeU8, nil
		}
		return TypeU16, nil
	case *IdentExpr:
		t, ok := s.lookupVar(n.Name)
		if !ok {
			return TypeInvalid, fmt.Errorf("unknown variable %q", n.Name)
		}
		return t, nil
	case *UnaryExpr:
		t, err := s.exprType(n.X)
		if err != nil {
			return TypeInvalid, err
		}
		if t == TypeVoid {
			return TypeInvalid, fmt.Errorf("unary operator on void")
		}
		return TypeU16, nil
	case *BinaryExpr:
		lt, err := s.exprType(n.Left)
		if err != nil {
			return TypeInvalid, err
		}
		rt, err := s.exprType(n.Right)
		if err != nil {
			return TypeInvalid, err
		}
		if lt == TypeVoid || rt == TypeVoid {
			return TypeInvalid, fmt.Errorf("binary operator on void")
		}
		switch n.Op {
		case "&&", "||":
			return TypeU8, nil
		case "==", "!=", "<", ">", "<=", ">=":
			return TypeU8, nil
		case "+", "-", "*", "/", "&":
			if lt == TypeU16 || rt == TypeU16 {
				return TypeU16, nil
			}
			return TypeU8, nil
		default:
			return TypeInvalid, fmt.Errorf("unsupported operator %q", n.Op)
		}
	case *CallExpr:
		sig, ok := s.funcs[n.Callee]
		if !ok {
			return TypeInvalid, fmt.Errorf("unknown function %q", n.Callee)
		}
		if len(sig.params) != len(n.Args) {
			return TypeInvalid, fmt.Errorf("%s expects %d arguments, got %d", n.Callee, len(sig.params), len(n.Args))
		}
		for i, arg := range n.Args {
			at, err := s.exprType(arg)
			if err != nil {
				return TypeInvalid, err
			}
			if !assignable(sig.params[i], at) {
				return TypeInvalid, fmt.Errorf("argument %d for %s: cannot assign %s to %s", i+1, n.Callee, at, sig.params[i])
			}
		}
		return sig.ret, nil
	default:
		return TypeInvalid, fmt.Errorf("unsupported expression")
	}
}

func assignable(dst, src TypeKind) bool {
	if dst == src {
		return true
	}
	if dst == TypeU16 && src == TypeU8 {
		return true
	}
	return false
}

func stmtListAlwaysReturns(stmts []Stmt) bool {
	for _, st := range stmts {
		if stmtAlwaysReturns(st) {
			return true
		}
	}
	return false
}

func stmtAlwaysReturns(st Stmt) bool {
	switch n := st.(type) {
	case *ReturnStmt:
		return true
	case *IfStmt:
		if len(n.Else) == 0 {
			return false
		}
		return stmtListAlwaysReturns(n.Then) && stmtListAlwaysReturns(n.Else)
	default:
		return false
	}
}

func (s *semantic) pushScope() {
	s.scopes = append(s.scopes, map[string]TypeKind{})
}

func (s *semantic) popScope() {
	s.scopes = s.scopes[:len(s.scopes)-1]
}

func (s *semantic) declare(name string, t TypeKind) error {
	top := s.scopes[len(s.scopes)-1]
	if _, exists := top[name]; exists {
		return fmt.Errorf("duplicate symbol %q", name)
	}
	top[name] = t
	return nil
}

func (s *semantic) lookupVar(name string) (TypeKind, bool) {
	for i := len(s.scopes) - 1; i >= 0; i-- {
		if t, ok := s.scopes[i][name]; ok {
			return t, true
		}
	}
	t, ok := s.globals[name]
	return t, ok
}
