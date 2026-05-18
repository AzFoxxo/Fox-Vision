package foxc

import "fmt"

type funcSig struct {
	ret    Type
	params []Type
}

type symbolInfo struct {
	typ      Type
	isArray  bool
	arrayLen int
}

type semantic struct {
	funcs        map[string]funcSig
	globals      map[string]symbolInfo
	scopes       []map[string]symbolInfo
	currFn       *FuncDecl
	extendedMode bool
}

func check(prog *Program, opts CompileOptions) error {
	s := &semantic{
		funcs:        map[string]funcSig{},
		globals:      map[string]symbolInfo{},
		extendedMode: isExtendedMode(opts.Mode),
	}

	s.funcs["poke"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}, {Base: TypeU16}}}
	s.funcs["peek"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{{Base: TypeU16}}}
	s.funcs["wait"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}}}
	s.funcs["cyc"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{}}
	s.funcs["vblank"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{}}
	s.funcs["in_port"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{{Base: TypeU16}}}
	s.funcs["out_port"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}, {Base: TypeU16}}}

	for _, g := range prog.Globals {
		if g.Type.Base == TypeVoid && g.Type.Ptr == 0 {
			return fmt.Errorf("global %q cannot be void", g.Name)
		}
		if _, exists := s.globals[g.Name]; exists {
			return fmt.Errorf("duplicate global %q", g.Name)
		}
		if g.IsArray && g.ArrayLen <= 0 {
			return fmt.Errorf("global array %q must have positive length", g.Name)
		}
		if g.IsArray && g.Type.Ptr != 0 {
			return fmt.Errorf("array %q cannot have pointer element type", g.Name)
		}
		s.globals[g.Name] = symbolInfo{typ: g.Type, isArray: g.IsArray, arrayLen: g.ArrayLen}
	}

	for _, fn := range prog.Funcs {
		if _, exists := s.funcs[fn.Name]; exists {
			return fmt.Errorf("duplicate function %q", fn.Name)
		}
		params := make([]Type, 0, len(fn.Params))
		for _, p := range fn.Params {
			if p.Type.Base == TypeVoid && p.Type.Ptr == 0 {
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
	if mainSig.ret.Base != TypeVoid || mainSig.ret.Ptr != 0 || len(mainSig.params) != 0 {
		return fmt.Errorf("main must have signature void main()")
	}

	for _, g := range prog.Globals {
		if g.Init != nil {
			if g.IsArray {
				return fmt.Errorf("global array %q cannot have initializer", g.Name)
			}
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
			if err := s.declare(p.Name, symbolInfo{typ: p.Type}); err != nil {
				return fmt.Errorf("function %s: %w", fn.Name, err)
			}
		}
		for _, st := range fn.Body {
			if err := s.checkStmt(st); err != nil {
				return fmt.Errorf("function %s: %w", fn.Name, err)
			}
		}
		if fn.Ret.Base != TypeVoid || fn.Ret.Ptr != 0 {
			if !stmtListAlwaysReturns(fn.Body) {
				return fmt.Errorf("function %s: non-void function must return on all paths", fn.Name)
			}
		}
		s.popScope()
	}

	return nil
}

func (s *semantic) checkStmt(st Stmt) error {
	switch n := st.(type) {
	case *VarDeclStmt:
		if n.Decl.Type.Base == TypeVoid && n.Decl.Type.Ptr == 0 {
			return fmt.Errorf("variable %q cannot be void", n.Decl.Name)
		}
		if n.Decl.IsArray && n.Decl.ArrayLen <= 0 {
			return fmt.Errorf("array %q must have positive length", n.Decl.Name)
		}
		if err := s.declare(n.Decl.Name, symbolInfo{typ: n.Decl.Type, isArray: n.Decl.IsArray, arrayLen: n.Decl.ArrayLen}); err != nil {
			return err
		}
		if n.Decl.Init != nil {
			if n.Decl.IsArray {
				return fmt.Errorf("array %q cannot have initializer", n.Decl.Name)
			}
			t, err := s.exprType(n.Decl.Init)
			if err != nil {
				return err
			}
			if !assignable(n.Decl.Type, t) {
				return fmt.Errorf("cannot assign %s to %s of type %s", t, n.Decl.Name, n.Decl.Type)
			}
		}
	case *AssignStmt:
		// LHS can be IdentExpr, IndexExpr, or UnaryExpr('*')
		switch l := n.LHS.(type) {
		case *IdentExpr:
			lhs, ok := s.lookupVar(l.Name)
			if !ok {
				return fmt.Errorf("unknown variable %q", l.Name)
			}
			if lhs.isArray {
				return fmt.Errorf("cannot assign to array %q without an index", l.Name)
			}
			rhs, err := s.exprType(n.Value)
			if err != nil {
				return err
			}
			if !assignable(lhs.typ, rhs) {
				return fmt.Errorf("cannot assign %s to %s of type %s", rhs, l.Name, lhs.typ)
			}
		case *IndexExpr:
			lhs, ok := s.lookupVar(l.Name)
			if !ok {
				return fmt.Errorf("unknown variable %q", l.Name)
			}
			if !lhs.isArray {
				return fmt.Errorf("%q is not an array", l.Name)
			}
			idxType, err := s.exprType(l.Index)
			if err != nil {
				return err
			}
			if idxType.Base == TypeVoid && idxType.Ptr == 0 {
				return fmt.Errorf("array index cannot be void")
			}
			rhs, err := s.exprType(n.Value)
			if err != nil {
				return err
			}
			if !assignable(lhs.typ, rhs) {
				return fmt.Errorf("cannot assign %s to %s element of type %s", rhs, l.Name, lhs.typ)
			}
		case *UnaryExpr:
			if l.Op != "*" {
				return fmt.Errorf("invalid assignment target")
			}
			t, err := s.exprType(l.X)
			if err != nil {
				return err
			}
			if t.Ptr == 0 {
				return fmt.Errorf("cannot dereference non-pointer in assignment")
			}
			pointee := Type{Base: t.Base, Ptr: t.Ptr - 1}
			rhs, err := s.exprType(n.Value)
			if err != nil {
				return err
			}
			if !assignable(pointee, rhs) {
				return fmt.Errorf("cannot assign %s to dereferenced %s", rhs, pointee)
			}
		default:
			return fmt.Errorf("unsupported assignment target")
		}
	case *AssignIndexStmt:
		lhs, ok := s.lookupVar(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		if !lhs.isArray {
			return fmt.Errorf("%q is not an array", n.Name)
		}
		idxType, err := s.exprType(n.Index)
		if err != nil {
			return err
		}
		if idxType.Base == TypeVoid && idxType.Ptr == 0 {
			return fmt.Errorf("array index cannot be void")
		}
		rhs, err := s.exprType(n.Value)
		if err != nil {
			return err
		}
		if !assignable(lhs.typ, rhs) {
			return fmt.Errorf("cannot assign %s to %s element of type %s", rhs, n.Name, lhs.typ)
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
		if s.currFn.Ret.Base == TypeVoid && s.currFn.Ret.Ptr == 0 {
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

func (s *semantic) exprType(e Expr) (Type, error) {
	switch n := e.(type) {
	case *IntLit:
		if n.Value < 0 || n.Value > 0xFFFF {
			return Type{Base: TypeInvalid}, fmt.Errorf("literal %d out of 16-bit range", n.Value)
		}
		if n.Value <= 0xFF {
			return Type{Base: TypeU8}, nil
		}
		return Type{Base: TypeU16}, nil
	case *IdentExpr:
		t, ok := s.lookupVar(n.Name)
		if !ok {
			return Type{Base: TypeInvalid}, fmt.Errorf("unknown variable %q", n.Name)
		}
		if t.isArray {
			return Type{Base: TypeInvalid}, fmt.Errorf("array %q requires an index", n.Name)
		}
		return t.typ, nil
	case *IndexExpr:
		t, ok := s.lookupVar(n.Name)
		if !ok {
			return Type{Base: TypeInvalid}, fmt.Errorf("unknown variable %q", n.Name)
		}
		if !t.isArray {
			return Type{Base: TypeInvalid}, fmt.Errorf("%q is not an array", n.Name)
		}
		idxType, err := s.exprType(n.Index)
		if err != nil {
			return Type{Base: TypeInvalid}, err
		}
		if idxType.Base == TypeVoid && idxType.Ptr == 0 {
			return Type{Base: TypeInvalid}, fmt.Errorf("array index cannot be void")
		}
		return Type{Base: t.typ.Base}, nil
	case *UnaryExpr:
		if n.Op == "~" {
			t, err := s.exprType(n.X)
			if err != nil {
				return Type{Base: TypeInvalid}, err
			}
			if t.Base == TypeVoid && t.Ptr == 0 {
				return Type{Base: TypeInvalid}, fmt.Errorf("unary operator on void")
			}
			return t, nil
		}
		if n.Op == "-" {
			// numeric negation -> u16
			_, err := s.exprType(n.X)
			if err != nil {
				return Type{Base: TypeInvalid}, err
			}
			return Type{Base: TypeU16}, nil
		}
		if n.Op == "*" {
			tx, err := s.exprType(n.X)
			if err != nil {
				return Type{Base: TypeInvalid}, err
			}
			if tx.Ptr == 0 {
				return Type{Base: TypeInvalid}, fmt.Errorf("cannot dereference non-pointer type %s", tx)
			}
			return Type{Base: tx.Base, Ptr: tx.Ptr - 1}, nil
		}
		if n.Op == "&" {
			// address-of: operand must be lvalue (IdentExpr or IndexExpr)
			switch a := n.X.(type) {
			case *IdentExpr:
				t, ok := s.lookupVar(a.Name)
				if !ok {
					return Type{Base: TypeInvalid}, fmt.Errorf("unknown variable %q", a.Name)
				}
				// address of array yields pointer to element
				if t.isArray {
					return Type{Base: t.typ.Base, Ptr: 1}, nil
				}
				return Type{Base: t.typ.Base, Ptr: t.typ.Ptr + 1}, nil
			case *IndexExpr:
				t, ok := s.lookupVar(a.Name)
				if !ok {
					return Type{Base: TypeInvalid}, fmt.Errorf("unknown variable %q", a.Name)
				}
				if !t.isArray {
					return Type{Base: TypeInvalid}, fmt.Errorf("%q is not an array", a.Name)
				}
				return Type{Base: t.typ.Base, Ptr: 1}, nil
			default:
				return Type{Base: TypeInvalid}, fmt.Errorf("address-of requires an lvalue")
			}
		}
		return Type{Base: TypeInvalid}, fmt.Errorf("unsupported unary operator %q", n.Op)
	case *BinaryExpr:
		// pointer arithmetic and comparisons
		lt, err := s.exprType(n.Left)
		if err != nil {
			return Type{Base: TypeInvalid}, err
		}
		rt, err := s.exprType(n.Right)
		if err != nil {
			return Type{Base: TypeInvalid}, err
		}
		if (lt.Base == TypeVoid && lt.Ptr == 0) || (rt.Base == TypeVoid && rt.Ptr == 0) {
			return Type{Base: TypeInvalid}, fmt.Errorf("binary operator on void")
		}
		switch n.Op {
		case "&&", "||":
			return Type{Base: TypeU8}, nil
		case "==", "!=", "<", ">", "<=", ">=":
			return Type{Base: TypeU8}, nil
		case "+":
			// pointer + int or int + pointer
			if lt.Ptr > 0 && rt.Ptr == 0 {
				return lt, nil
			}
			if rt.Ptr > 0 && lt.Ptr == 0 {
				return rt, nil
			}
			fallthrough
		case "-":
			// pointer - int -> pointer; int - pointer invalid; pointer - pointer -> integer (u16)
			if n.Op == "-" && lt.Ptr > 0 && rt.Ptr > 0 {
				// pointer subtraction yields integer
				return Type{Base: TypeU16}, nil
			}
			if (lt.Ptr > 0 && rt.Ptr == 0) || (rt.Ptr > 0 && lt.Ptr == 0 && n.Op == "+") {
				if lt.Ptr > 0 {
					return lt, nil
				}
				return rt, nil
			}
			// otherwise numeric binary op
		case "*", "/", "&", "|", "^", "<<", ">>":
			// fall through to numeric rules
		}
		// numeric result type: u16 if either operand is u16
		if lt.Base == TypeU16 || rt.Base == TypeU16 {
			return Type{Base: TypeU16}, nil
		}
		return Type{Base: TypeU8}, nil
	case *CallExpr:
		if !s.extendedMode && (n.Callee == "in_port" || n.Callee == "out_port") {
			return Type{Base: TypeInvalid}, fmt.Errorf("%s requires extended mode", n.Callee)
		}
		sig, ok := s.funcs[n.Callee]
		if !ok {
			return Type{Base: TypeInvalid}, fmt.Errorf("unknown function %q", n.Callee)
		}
		if len(sig.params) != len(n.Args) {
			return Type{Base: TypeInvalid}, fmt.Errorf("%s expects %d arguments, got %d", n.Callee, len(sig.params), len(n.Args))
		}
		for i, arg := range n.Args {
			at, err := s.exprType(arg)
			if err != nil {
				return Type{Base: TypeInvalid}, err
			}
			if !assignable(sig.params[i], at) {
				return Type{Base: TypeInvalid}, fmt.Errorf("argument %d for %s: cannot assign %s to %s", i+1, n.Callee, at, sig.params[i])
			}
		}
		return sig.ret, nil
	default:
		return Type{Base: TypeInvalid}, fmt.Errorf("unsupported expression")
	}
}

func assignable(dst, src Type) bool {
	// exact match including pointer level
	if dst.Base == src.Base && dst.Ptr == src.Ptr {
		return true
	}
	// allow widening u8 -> u16 for non-pointer values
	if dst.Ptr == 0 && src.Ptr == 0 && dst.Base == TypeU16 && src.Base == TypeU8 {
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
	s.scopes = append(s.scopes, map[string]symbolInfo{})
}

func (s *semantic) popScope() {
	s.scopes = s.scopes[:len(s.scopes)-1]
}

func (s *semantic) declare(name string, sym symbolInfo) error {
	top := s.scopes[len(s.scopes)-1]
	if _, exists := top[name]; exists {
		return fmt.Errorf("duplicate symbol %q", name)
	}
	top[name] = sym
	return nil
}

func (s *semantic) lookupVar(name string) (symbolInfo, bool) {
	for i := len(s.scopes) - 1; i >= 0; i-- {
		if t, ok := s.scopes[i][name]; ok {
			return t, true
		}
	}
	t, ok := s.globals[name]
	return t, ok
}
