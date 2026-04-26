package foxc

import (
	"fmt"
	"strings"
)

type storageKind int

const (
	storageGlobal storageKind = iota
	storageFrame
)

type storage struct {
	kind    storageKind
	operand string
	offset  int
	typ     TypeKind
}

type returnTarget struct {
	id    int
	label string
}

type funcFrame struct {
	decl            *FuncDecl
	entryLabel      string
	dispatchLabel   string
	paramByName     map[string]storage
	paramSlots      []storage
	retIDSlot       storage
	retSlot         *storage
	localStart      int
	localSlotCount  int
	frameSize       int
	localNextOffset int
	returnTargets   []returnTarget
	nextReturnID    int
}

type gen struct {
	prog *Program

	consts []string
	text   strings.Builder

	nextAddr uint16
	nextID   int

	globals map[string]storage
	funcs   map[string]*FuncDecl
	frames  map[string]*funcFrame

	scopes []map[string]storage

	stackTopSlot  storage
	frameBaseSlot storage

	currentFrame *funcFrame
}

func generate(prog *Program) (string, error) {
	g := &gen{
		prog:     prog,
		nextAddr: 0x2000,
		globals:  map[string]storage{},
		funcs:    map[string]*FuncDecl{},
		frames:   map[string]*funcFrame{},
	}
	for _, fn := range prog.Funcs {
		g.funcs[fn.Name] = fn
	}
	mainFn, ok := g.funcs["main"]
	if !ok {
		return "", fmt.Errorf("missing main function")
	}

	for _, gl := range prog.Globals {
		g.globals[gl.Name] = g.allocGlobal(gl.Type)
	}
	g.stackTopSlot = g.allocGlobal(TypeU16)
	g.frameBaseSlot = g.allocGlobal(TypeU16)

	if err := g.prepareFrames(); err != nil {
		return "", err
	}

	g.emit(":main")
	g.emit("MOV %28672 X")
	g.storeToGlobal(g.stackTopSlot)
	g.emit("MOV %0 X")
	g.storeToGlobal(g.frameBaseSlot)

	for _, gl := range prog.Globals {
		if gl.Init == nil {
			continue
		}
		if err := g.emitExpr(gl.Init); err != nil {
			return "", err
		}
		g.storeToStorage(g.globals[gl.Name])
	}

	g.pushScope()
	if err := g.emitStmtList(mainFn.Body); err != nil {
		return "", err
	}
	g.popScope()
	g.emit("HLT")

	for _, fn := range prog.Funcs {
		if fn.Name == "main" {
			continue
		}
		if err := g.emitFunction(g.frames[fn.Name]); err != nil {
			return "", err
		}
	}

	for _, fn := range prog.Funcs {
		if fn.Name == "main" {
			continue
		}
		g.emitDispatch(g.frames[fn.Name])
	}

	var out strings.Builder
	for _, c := range g.consts {
		out.WriteString(c)
		out.WriteByte('\n')
	}
	if len(g.consts) > 0 {
		out.WriteByte('\n')
	}
	out.WriteString(g.text.String())

	return out.String(), nil
}

func (g *gen) prepareFrames() error {
	for _, fn := range g.prog.Funcs {
		if fn.Name == "main" {
			continue
		}

		localCount := countLocalsInStmtList(fn.Body)
		next := 0
		fr := &funcFrame{
			decl:          fn,
			entryLabel:    "func_" + fn.Name,
			dispatchLabel: "func_" + fn.Name + "_dispatch",
			paramByName:   map[string]storage{},
		}

		fr.retIDSlot = storage{kind: storageFrame, offset: next, typ: TypeU16}
		next++

		if fn.Ret != TypeVoid {
			slot := storage{kind: storageFrame, offset: next, typ: fn.Ret}
			fr.retSlot = &slot
			next++
		}

		for _, p := range fn.Params {
			slot := storage{kind: storageFrame, offset: next, typ: p.Type}
			next++
			fr.paramSlots = append(fr.paramSlots, slot)
			fr.paramByName[p.Name] = slot
		}

		fr.localStart = next
		fr.localSlotCount = localCount
		fr.frameSize = fr.localStart + localCount
		fr.localNextOffset = fr.localStart
		g.frames[fn.Name] = fr
	}
	return nil
}

func (g *gen) emitFunction(fr *funcFrame) error {
	g.currentFrame = fr
	defer func() { g.currentFrame = nil }()
	fr.localNextOffset = fr.localStart

	g.emit(":" + fr.entryLabel)
	g.pushScope()
	for _, p := range fr.decl.Params {
		slot := fr.paramByName[p.Name]
		if err := g.declare(p.Name, slot); err != nil {
			return err
		}
	}

	if err := g.emitStmtList(fr.decl.Body); err != nil {
		return err
	}
	g.emit("JMP " + fr.dispatchLabel)
	g.popScope()

	return nil
}

func (g *gen) emitDispatch(fr *funcFrame) {
	g.emit(":" + fr.dispatchLabel)
	g.loadFromStorage(fr.retIDSlot)
	for _, rt := range fr.returnTargets {
		g.emit(fmt.Sprintf("MOV %%%d Y", rt.id))
		g.emit("CMP")
		g.emit("JEQ " + rt.label)
	}
	g.emit("HLT")
}

func (g *gen) emitStmtList(stmts []Stmt) error {
	for _, st := range stmts {
		if err := g.emitStmt(st); err != nil {
			return err
		}
	}
	return nil
}

func (g *gen) emitStmt(st Stmt) error {
	switch n := st.(type) {
	case *VarDeclStmt:
		slot := g.allocLocal(n.Decl.Type)
		if err := g.declare(n.Decl.Name, slot); err != nil {
			return err
		}
		if n.Decl.Init != nil {
			if err := g.emitExpr(n.Decl.Init); err != nil {
				return err
			}
			g.storeToStorage(slot)
		}
	case *AssignStmt:
		slot, ok := g.lookup(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		if err := g.emitExpr(n.Value); err != nil {
			return err
		}
		g.storeToStorage(slot)
	case *ExprStmt:
		if err := g.emitExpr(n.Value); err != nil {
			return err
		}
	case *IfStmt:
		elseLabel := g.label("if_else")
		endLabel := g.label("if_end")
		if err := g.emitExpr(n.Cond); err != nil {
			return err
		}
		g.emit("MOV %0 Y")
		g.emit("CMP")
		g.emit("JEQ " + elseLabel)
		g.pushScope()
		if err := g.emitStmtList(n.Then); err != nil {
			return err
		}
		g.popScope()
		g.emit("JMP " + endLabel)
		g.emit(":" + elseLabel)
		g.pushScope()
		if err := g.emitStmtList(n.Else); err != nil {
			return err
		}
		g.popScope()
		g.emit(":" + endLabel)
	case *WhileStmt:
		start := g.label("while_start")
		end := g.label("while_end")
		g.emit(":" + start)
		if err := g.emitExpr(n.Cond); err != nil {
			return err
		}
		g.emit("MOV %0 Y")
		g.emit("CMP")
		g.emit("JEQ " + end)
		g.pushScope()
		if err := g.emitStmtList(n.Body); err != nil {
			return err
		}
		g.popScope()
		g.emit("JMP " + start)
		g.emit(":" + end)
	case *ReturnStmt:
		if g.currentFrame == nil {
			if n.Value != nil {
				if err := g.emitExpr(n.Value); err != nil {
					return err
				}
			}
			g.emit("HLT")
			return nil
		}
		if n.Value != nil {
			if err := g.emitExpr(n.Value); err != nil {
				return err
			}
			if g.currentFrame.retSlot == nil {
				return fmt.Errorf("value return in void function")
			}
			g.storeToStorage(*g.currentFrame.retSlot)
		}
		g.emit("JMP " + g.currentFrame.dispatchLabel)
	default:
		return fmt.Errorf("unsupported statement")
	}
	return nil
}

func (g *gen) emitExpr(e Expr) error {
	switch n := e.(type) {
	case *IntLit:
		g.emit(fmt.Sprintf("MOV %%%d X", n.Value&0xFFFF))
	case *IdentExpr:
		slot, ok := g.lookup(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		g.loadFromStorage(slot)
	case *UnaryExpr:
		if n.Op != "-" {
			return fmt.Errorf("unsupported unary operator %q", n.Op)
		}
		if err := g.emitExpr(n.X); err != nil {
			return err
		}
		g.emit("MOV %0 Y")
		g.emit("SUB X Y")
		g.emit("MOV Y X")
	case *BinaryExpr:
		if err := g.emitExpr(n.Left); err != nil {
			return err
		}
		g.emit("PUSH X")
		if err := g.emitExpr(n.Right); err != nil {
			return err
		}
		g.emit("MOV X Y")
		g.emit("POP X")
		switch n.Op {
		case "+":
			g.emit("ADD Y X")
		case "-":
			g.emit("SUB Y X")
		case "*":
			g.emit("MUL Y X")
		case "/":
			g.emit("DIV Y X")
		case "&":
			g.emit("AND Y X")
		case "==", "!=", "<", ">", "<=", ">=":
			if err := g.emitCompareResult(n.Op); err != nil {
				return err
			}
		default:
			return fmt.Errorf("unsupported binary operator %q", n.Op)
		}
	case *CallExpr:
		if n.Callee == "poke" {
			return g.emitBuiltinPoke(n)
		}
		if n.Callee == "peak" || n.Callee == "peek" {
			return g.emitBuiltinPeak(n)
		}
		return g.emitFunctionCall(n)
	default:
		return fmt.Errorf("unsupported expression")
	}
	return nil
}

func (g *gen) emitBuiltinPoke(call *CallExpr) error {
	if len(call.Args) != 2 {
		return fmt.Errorf("poke expects 2 arguments")
	}
	if err := g.emitExpr(call.Args[0]); err != nil {
		return err
	}
	g.emit("PUSH X")
	if err := g.emitExpr(call.Args[1]); err != nil {
		return err
	}
	g.emit("MOV X Y")
	g.emit("POP X")
	g.emit("STR Y X")
	g.emit("MOV %0 X")
	return nil
}

func (g *gen) emitBuiltinPeak(call *CallExpr) error {
	if len(call.Args) != 1 {
		return fmt.Errorf("peak expects 1 argument")
	}
	if err := g.emitExpr(call.Args[0]); err != nil {
		return err
	}
	g.emit("LOD X X")
	return nil
}

func (g *gen) emitFunctionCall(call *CallExpr) error {
	fn, ok := g.funcs[call.Callee]
	if !ok {
		return fmt.Errorf("unknown function %q", call.Callee)
	}
	if fn.Name == "main" {
		return fmt.Errorf("main cannot be called")
	}
	fr, ok := g.frames[fn.Name]
	if !ok {
		return fmt.Errorf("missing frame for function %q", fn.Name)
	}
	if len(call.Args) != len(fr.paramSlots) {
		return fmt.Errorf("%s expects %d args", fn.Name, len(fr.paramSlots))
	}

	g.loadFromGlobal(g.frameBaseSlot)
	g.emit("PUSH X")

	for _, a := range call.Args {
		if err := g.emitExpr(a); err != nil {
			return err
		}
		g.emit("PUSH X")
	}

	g.loadFromGlobal(g.stackTopSlot)
	g.storeToGlobal(g.frameBaseSlot)
	if fr.frameSize > 0 {
		g.emit(fmt.Sprintf("ADD %%%d X", fr.frameSize))
		g.storeToGlobal(g.stackTopSlot)
	}

	for i := len(fr.paramSlots) - 1; i >= 0; i-- {
		g.emit("POP X")
		g.storeToStorage(fr.paramSlots[i])
	}

	fr.nextReturnID++
	rt := returnTarget{
		id:    fr.nextReturnID,
		label: g.label("ret_" + fn.Name),
	}
	fr.returnTargets = append(fr.returnTargets, rt)

	g.emit(fmt.Sprintf("MOV %%%d X", rt.id))
	g.storeToStorage(fr.retIDSlot)
	g.emit("JMP " + fr.entryLabel)
	g.emit(":" + rt.label)

	if fr.retSlot != nil {
		g.loadFromStorage(*fr.retSlot)
		g.emit("MOV X Y")
	}

	g.loadFromGlobal(g.frameBaseSlot)
	g.storeToGlobal(g.stackTopSlot)
	g.emit("POP X")
	g.storeToGlobal(g.frameBaseSlot)

	if fr.retSlot != nil {
		g.emit("MOV Y X")
	} else {
		g.emit("MOV %0 X")
	}
	return nil
}

func (g *gen) emitCompareResult(op string) error {
	g.emit("CMP")
	trueLabel := g.label("cmp_true")
	endLabel := g.label("cmp_end")
	switch op {
	case "==":
		g.emit("JEQ " + trueLabel)
	case "!=":
		g.emit("JNE " + trueLabel)
	case "<":
		g.emit("JLT " + trueLabel)
	case ">":
		g.emit("JGT " + trueLabel)
	case "<=":
		g.emit("JLE " + trueLabel)
	case ">=":
		g.emit("JGE " + trueLabel)
	default:
		return fmt.Errorf("unsupported comparator %q", op)
	}
	g.emit("MOV %0 X")
	g.emit("JMP " + endLabel)
	g.emit(":" + trueLabel)
	g.emit("MOV %1 X")
	g.emit(":" + endLabel)
	return nil
}

func (g *gen) allocGlobal(t TypeKind) storage {
	name := "V_" + alphaName(int(g.nextAddr))
	g.consts = append(g.consts, fmt.Sprintf("@const %s $%04X", name, g.nextAddr))
	g.nextAddr++
	return storage{kind: storageGlobal, operand: "<" + name + ">", typ: t}
}

func (g *gen) allocLocal(t TypeKind) storage {
	if g.currentFrame == nil {
		return g.allocGlobal(t)
	}
	if g.currentFrame.localNextOffset >= g.currentFrame.frameSize {
		return storage{kind: storageFrame, offset: g.currentFrame.frameSize - 1, typ: t}
	}
	s := storage{kind: storageFrame, offset: g.currentFrame.localNextOffset, typ: t}
	g.currentFrame.localNextOffset++
	return s
}

func alphaName(n int) string {
	if n < 0 {
		n = -n
	}
	// Base-26 letter encoding (A..Z) to keep constant names lexer-safe.
	b := make([]byte, 0, 8)
	x := n
	for {
		r := x % 26
		b = append([]byte{byte('A' + r)}, b...)
		x = x/26 - 1
		if x < 0 {
			break
		}
	}
	return string(b)
}

func (g *gen) storeToStorage(slot storage) {
	if slot.typ == TypeU8 {
		g.emit("AND %255 X")
	}
	if slot.kind == storageGlobal {
		g.emit("STR X " + slot.operand)
		return
	}
	g.emit("PUSH X")
	g.loadFrameAddress(slot.offset)
	g.emit("MOV X Y")
	g.emit("POP X")
	g.emit("STR X Y")
}

func (g *gen) loadFromStorage(slot storage) {
	if slot.kind == storageGlobal {
		g.emit("LOD X " + slot.operand)
		return
	}
	g.loadFrameAddress(slot.offset)
	g.emit("LOD X X")
}

func (g *gen) loadFrameAddress(offset int) {
	g.loadFromGlobal(g.frameBaseSlot)
	if offset > 0 {
		g.emit(fmt.Sprintf("ADD %%%d X", offset))
	}
}

func (g *gen) loadFromGlobal(slot storage) {
	g.emit("LOD X " + slot.operand)
}

func (g *gen) storeToGlobal(slot storage) {
	if slot.typ == TypeU8 {
		g.emit("AND %255 X")
	}
	g.emit("STR X " + slot.operand)
}

func (g *gen) emit(line string) {
	g.text.WriteString(line)
	g.text.WriteByte('\n')
}

func (g *gen) label(prefix string) string {
	g.nextID++
	return fmt.Sprintf("%s_%d", prefix, g.nextID)
}

func (g *gen) pushScope() {
	g.scopes = append(g.scopes, map[string]storage{})
}

func (g *gen) popScope() {
	g.scopes = g.scopes[:len(g.scopes)-1]
}

func (g *gen) declare(name string, st storage) error {
	top := g.scopes[len(g.scopes)-1]
	if _, exists := top[name]; exists {
		return fmt.Errorf("duplicate symbol %q", name)
	}
	top[name] = st
	return nil
}

func (g *gen) lookup(name string) (storage, bool) {
	for i := len(g.scopes) - 1; i >= 0; i-- {
		if st, ok := g.scopes[i][name]; ok {
			return st, true
		}
	}
	st, ok := g.globals[name]
	return st, ok
}

func countLocalsInStmtList(stmts []Stmt) int {
	total := 0
	for _, st := range stmts {
		total += countLocalsInStmt(st)
	}
	return total
}

func countLocalsInStmt(st Stmt) int {
	switch n := st.(type) {
	case *VarDeclStmt:
		return 1
	case *IfStmt:
		return countLocalsInStmtList(n.Then) + countLocalsInStmtList(n.Else)
	case *WhileStmt:
		return countLocalsInStmtList(n.Body)
	default:
		return 0
	}
}
