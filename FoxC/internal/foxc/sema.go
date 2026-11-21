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
	em2Am1Mode   bool
}

func check(prog *Program, opts CompileOptions) error {
	s := &semantic{
		funcs:        map[string]funcSig{},
		globals:      map[string]symbolInfo{},
		extendedMode: isExtendedMode(opts.Mode),
		em2Am1Mode:   isEm2Am1Mode(opts.Mode),
	}

	// In EM=2 the memory address bus is 32 bits wide, so poke/peek take
	// full-width word addresses. The stored value remains one 16-bit word.
	pokeAddr := Type{Base: TypeU16}
	if s.em2Am1Mode {
		pokeAddr = Type{Base: TypeU32}
	}
	osStartAddr := Type{Base: TypeU16}
	if s.em2Am1Mode {
		osStartAddr = Type{Base: TypeU32}
	}

	s.funcs["poke"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{pokeAddr, pokeAddr}}
	s.funcs["peek"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{pokeAddr}}
	s.funcs["wait"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}}}
	s.funcs["cyc"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{}}
	s.funcs["get_memory_size"] = funcSig{ret: Type{Base: TypeU32}, params: []Type{}}
	s.funcs["vblank"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{}}
	s.funcs["in_port"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{{Base: TypeU16}}}
	s.funcs["out_port"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}, {Base: TypeU16}}}
	s.funcs["in_port32"] = funcSig{ret: Type{Base: TypeU32}, params: []Type{{Base: TypeU16}}}
	s.funcs["out_port32"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}, {Base: TypeU32}}}
	// Variable-port forms patch the immediate port word of a nearby IN/OUT
	// instruction at runtime. Port numbers are still u16.
	s.funcs["in_port_v"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{{Base: TypeU16}}}
	s.funcs["out_port_v"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}, {Base: TypeU16}}}
	s.funcs["in_port32_v"] = funcSig{ret: Type{Base: TypeU32}, params: []Type{{Base: TypeU16}}}
	s.funcs["out_port32_v"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}, {Base: TypeU32}}}
	s.funcs["am"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{}}
	s.funcs["set_am"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}}}
	s.funcs["em"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{}}
	s.funcs["set_em"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}}}
	s.funcs["os_return"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{}}
	s.funcs["os_start"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{osStartAddr}}

	// ISA V2.2 trap ABI. syscall is checked specially so pointer arguments
	// are passed as raw machine words.
	s.funcs["syscall"] = funcSig{ret: Type{Base: TypeU32}, params: []Type{
		{Base: TypeU16}, {Base: TypeU32}, {Base: TypeU32}, {Base: TypeU32},
		{Base: TypeU32}, {Base: TypeU32}, {Base: TypeU32},
	}}
	s.funcs["sysret"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{}}
	s.funcs["sys_setvec"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU32}}}
	s.funcs["sys_setksp"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU32}}}
	s.funcs["sys_setquant"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU32}}}
	s.funcs["sys_irq"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}}}
	s.funcs["sys_setdb"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU32}}}
	s.funcs["sys_sethb"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU32}}}
	s.funcs["sys_setcb"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU32}}}
	s.funcs["get_stack_top"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{}}
	s.funcs["set_stack_top"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}}}
	s.funcs["get_frame_base"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{}}
	s.funcs["set_frame_base"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{{Base: TypeU16}}}
	s.funcs["errno"] = funcSig{ret: Type{Base: TypeU16}, params: []Type{}}
	s.funcs["halt"] = funcSig{ret: Type{Base: TypeVoid}, params: []Type{}}

	for _, g := range prog.Globals {
		if g.Type.Base == TypeVoid && g.Type.Ptr == 0 {
			return fmt.Errorf("global %q cannot be void", g.Name)
		}
		if g.Type.Base == TypeU32 && !s.extendedMode {
			return fmt.Errorf("global %q: u32 requires extended mode", g.Name)
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

	builtinSigs := make(map[string]funcSig, len(s.funcs))
	for name, sig := range s.funcs {
		builtinSigs[name] = sig
	}

	defined := map[string]bool{}
	for _, fn := range prog.Funcs {
		sig, err := s.funcSignature(fn)
		if err != nil {
			return err
		}
		if _, isBuiltin := builtinSigs[fn.Name]; isBuiltin {
			return fmt.Errorf("function %q conflicts with a builtin", fn.Name)
		}

		if fn.IsPrototype {
			if existing, exists := s.funcs[fn.Name]; exists {
				if !sameFuncSig(existing, sig) {
					return fmt.Errorf("conflicting prototype for function %q", fn.Name)
				}
			} else {
				s.funcs[fn.Name] = sig
			}
			continue
		}

		if defined[fn.Name] {
			return fmt.Errorf("duplicate definition of function %q", fn.Name)
		}
		if existing, exists := s.funcs[fn.Name]; exists && !sameFuncSig(existing, sig) {
			return fmt.Errorf("definition of function %q does not match its prototype", fn.Name)
		}
		s.funcs[fn.Name] = sig
		defined[fn.Name] = true
	}

	mainSig, ok := s.funcs["main"]
	if !ok {
		return fmt.Errorf("missing required function main")
	}
	if !defined["main"] {
		return fmt.Errorf("missing definition of function main")
	}
	if mainSig.ret.Base != TypeVoid || mainSig.ret.Ptr != 0 || len(mainSig.params) != 0 {
		return fmt.Errorf("main must have signature void main()")
	}

	for _, g := range prog.Globals {
		if g.Init != nil {
			if g.IsArray {
				str, ok := g.Init.(*StringLit)
				if !ok {
					return fmt.Errorf("global array %q initializer must be a string literal", g.Name)
				}
				if g.Type.Base != TypeU8 || g.Type.Ptr != 0 {
					return fmt.Errorf("string initializer requires a u8 array")
				}
				if g.ArrayLen < len([]byte(str.Value))+1 {
					return fmt.Errorf("string initializer for %q is too long: needs %d bytes", g.Name, len([]byte(str.Value))+1)
				}
				continue
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
		if fn.IsPrototype {
			continue
		}
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

func (s *semantic) funcSignature(fn *FuncDecl) (funcSig, error) {
	if fn.Ret.Base == TypeU32 && !s.extendedMode {
		return funcSig{}, fmt.Errorf("function %q: u32 requires extended mode", fn.Name)
	}
	params := make([]Type, 0, len(fn.Params))
	for _, p := range fn.Params {
		if p.Type.Base == TypeVoid && p.Type.Ptr == 0 {
			return funcSig{}, fmt.Errorf("parameter %q in %q cannot be void", p.Name, fn.Name)
		}
		if p.Type.Base == TypeU32 && !s.extendedMode {
			return funcSig{}, fmt.Errorf("parameter %q in %q: u32 requires extended mode", p.Name, fn.Name)
		}
		if p.Name == "" && !fn.IsPrototype {
			return funcSig{}, fmt.Errorf("parameter name required in definition of %q", fn.Name)
		}
		params = append(params, p.Type)
	}
	return funcSig{ret: fn.Ret, params: params}, nil
}

func sameFuncSig(a, b funcSig) bool {
	if !sameType(a.ret, b.ret) || len(a.params) != len(b.params) {
		return false
	}
	for i := range a.params {
		if !sameType(a.params[i], b.params[i]) {
			return false
		}
	}
	return true
}

func sameType(a, b Type) bool {
	return a.Base == b.Base && a.Ptr == b.Ptr
}

func (s *semantic) checkStmt(st Stmt) error {
	switch n := st.(type) {
	case *VarDeclStmt:
		if n.Decl.Type.Base == TypeVoid && n.Decl.Type.Ptr == 0 {
			return fmt.Errorf("variable %q cannot be void", n.Decl.Name)
		}
		if n.Decl.Type.Base == TypeU32 && !s.extendedMode {
			return fmt.Errorf("variable %q: u32 requires extended mode", n.Decl.Name)
		}
		if n.Decl.IsArray && n.Decl.ArrayLen <= 0 {
			return fmt.Errorf("array %q must have positive length", n.Decl.Name)
		}
		if err := s.declare(n.Decl.Name, symbolInfo{typ: n.Decl.Type, isArray: n.Decl.IsArray, arrayLen: n.Decl.ArrayLen}); err != nil {
			return err
		}
		if n.Decl.Init != nil {
			if n.Decl.IsArray {
				str, ok := n.Decl.Init.(*StringLit)
				if !ok {
					return fmt.Errorf("array %q initializer must be a string literal", n.Decl.Name)
				}
				if n.Decl.Type.Base != TypeU8 || n.Decl.Type.Ptr != 0 {
					return fmt.Errorf("string initializer requires a u8 array")
				}
				if n.Decl.ArrayLen < len([]byte(str.Value))+1 {
					return fmt.Errorf("string initializer for %q is too long: needs %d bytes", n.Decl.Name, len([]byte(str.Value))+1)
				}
				return nil
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
		if n.Value < 0 || n.Value > 0xFFFFFFFF {
			return Type{Base: TypeInvalid}, fmt.Errorf("literal %d out of 32-bit range", n.Value)
		}
		if n.Value > 0xFFFF {
			if !s.extendedMode {
				return Type{Base: TypeInvalid}, fmt.Errorf("32-bit literal %d requires extended mode", n.Value)
			}
			return Type{Base: TypeU32}, nil
		}
		if n.Value <= 0xFF {
			return Type{Base: TypeU8}, nil
		}
		return Type{Base: TypeU16}, nil
	case *CharLit:
		return Type{Base: TypeU8}, nil
	case *StringLit:
		return Type{Base: TypeU8, Ptr: 1}, nil
	case *CastExpr:
		if n.Type.Base == TypeVoid && n.Type.Ptr == 0 {
			return Type{Base: TypeInvalid}, fmt.Errorf("cannot cast to void")
		}
		st, err := s.exprType(n.X)
		if err != nil {
			return Type{Base: TypeInvalid}, err
		}
		if st.Base == TypeVoid && st.Ptr == 0 {
			return Type{Base: TypeInvalid}, fmt.Errorf("cannot cast void expression")
		}
		return n.Type, nil
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
			// numeric negation -> u16 unless the operand is u32
			t, err := s.exprType(n.X)
			if err != nil {
				return Type{Base: TypeInvalid}, err
			}
			if t.Ptr == 0 && t.Base == TypeU32 {
				return Type{Base: TypeU32}, nil
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
			if tx.Ptr == 1 && tx.Base == TypeVoid {
				return Type{Base: TypeInvalid}, fmt.Errorf("cannot dereference void pointer")
			}
			return Type{Base: tx.Base, Ptr: tx.Ptr - 1}, nil
		}
		if n.Op == "&" {
			// address-of: operand must be lvalue (IdentExpr or IndexExpr),
			// or a function name for the trap-vector entry point.
			switch a := n.X.(type) {
			case *IdentExpr:
				t, ok := s.lookupVar(a.Name)
				if !ok {
					if _, isFunc := s.funcs[a.Name]; isFunc {
						return Type{Base: TypeU32}, nil
					}
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
		// numeric result type: promote to the widest operand (u8 < u16 < u32)
		rank := func(t Type) int {
			switch t.Base {
			case TypeU32:
				return 3
			case TypeU16:
				return 2
			case TypeU8:
				return 1
			default:
				return 0
			}
		}
		if rank(lt) > rank(rt) {
			return Type{Base: lt.Base}, nil
		}
		return Type{Base: rt.Base}, nil
	case *CallExpr:
		if !s.extendedMode && (n.Callee == "in_port" || n.Callee == "out_port" || n.Callee == "in_port32" || n.Callee == "out_port32" ||
			n.Callee == "in_port_v" || n.Callee == "out_port_v" || n.Callee == "in_port32_v" || n.Callee == "out_port32_v" ||
			n.Callee == "am" || n.Callee == "set_am" || n.Callee == "em" || n.Callee == "set_em" || n.Callee == "get_memory_size") {
			return Type{Base: TypeInvalid}, fmt.Errorf("%s requires extended mode", n.Callee)
		}
		if !s.em2Am1Mode && (n.Callee == "in_port32" || n.Callee == "out_port32" ||
			n.Callee == "in_port32_v" || n.Callee == "out_port32_v" || n.Callee == "get_memory_size") {
			return Type{Base: TypeInvalid}, fmt.Errorf("%s requires 32-bit mode (--mode em2_am1)", n.Callee)
		}
		if n.Callee == "syscall" {
			// The ABI takes a syscall number plus six untyped machine words.
			// Pointer arguments are passed through unchanged, so this call is
			// deliberately not checked against a fixed parameter list.
			if len(n.Args) != 7 {
				return Type{Base: TypeInvalid}, fmt.Errorf("syscall expects 7 arguments")
			}
			for _, arg := range n.Args {
				at, err := s.exprType(arg)
				if err != nil {
					return Type{Base: TypeInvalid}, err
				}
				if at.Base == TypeVoid && at.Ptr == 0 {
					return Type{Base: TypeInvalid}, fmt.Errorf("syscall argument cannot be void")
				}
			}
			return Type{Base: TypeU32}, nil
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
	if dst.Ptr != 0 || src.Ptr != 0 {
		// void * is compatible with every object pointer. All FoxOS pointers
		// in a 32-bit image are stored as word addresses, so no conversion is
		// required at the representation level.
		if dst.Ptr > 0 && src.Ptr > 0 {
			if (dst.Base == TypeVoid && dst.Ptr == 1) || (src.Base == TypeVoid && src.Ptr == 1) {
				return true
			}
		}
		return false
	}
	// allow widening u8 -> u16/u32 and u16 -> u32 for non-pointer values
	switch dst.Base {
	case TypeU16:
		return src.Base == TypeU8
	case TypeU32:
		return src.Base == TypeU8 || src.Base == TypeU16
	default:
		return false
	}
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
