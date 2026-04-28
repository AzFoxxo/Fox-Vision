package foxc

import (
	"fmt"
	"os"
	"strconv"
	"strings"
)

const (
	globalBaseAddr  = 0x2000
	runtimeStackTop = 28672
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

type inlineFn struct {
	fn  *FuncDecl
	ret *ReturnStmt
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
	inlineTempStart int
	inlineTempCount int
	frameSize       int
	localNextOffset int
	returnTargets   []returnTarget
	nextReturnID    int
}

type gen struct {
	prog *Program

	consts []string
	ir     []irInst

	nextAddr uint16
	nextID   int

	globals map[string]storage
	funcs   map[string]*FuncDecl
	frames  map[string]*funcFrame
	inline  map[string]inlineFn

	scopes []map[string]storage

	stackTopSlot  storage
	frameBaseSlot storage

	currentFrame *funcFrame
	inlineDepth  map[string]int

	enableStackChecks bool
	stackLimit        uint16
}

func generate(prog *Program) (string, error) {
	g := &gen{
		prog:        prog,
		nextAddr:    globalBaseAddr,
		globals:     map[string]storage{},
		funcs:       map[string]*FuncDecl{},
		frames:      map[string]*funcFrame{},
		inline:      map[string]inlineFn{},
		inlineDepth: map[string]int{},
		stackLimit:  0xFFFF,
	}
	g.configureStackChecks()

	for _, fn := range prog.Funcs {
		g.funcs[fn.Name] = fn
	}
	mainFn, ok := g.funcs["main"]
	if !ok {
		return "", fmt.Errorf("missing main function")
	}

	for _, fn := range prog.Funcs {
		if in, ok := shortInlineCandidate(fn); ok {
			g.inline[fn.Name] = in
		}
	}

	for _, gl := range prog.Globals {
		slot, err := g.allocGlobal(gl.Type)
		if err != nil {
			return "", err
		}
		g.globals[gl.Name] = slot
	}

	var err error
	g.stackTopSlot, err = g.allocGlobal(TypeU16)
	if err != nil {
		return "", err
	}
	g.frameBaseSlot, err = g.allocGlobal(TypeU16)
	if err != nil {
		return "", err
	}

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
	out.WriteString(renderIR(optimizeIR(g.ir)))

	return out.String(), nil
}

func (g *gen) configureStackChecks() {
	g.enableStackChecks = os.Getenv("FOXC_STACK_CHECK") == "1"
	if !g.enableStackChecks {
		return
	}
	if raw := strings.TrimSpace(os.Getenv("FOXC_STACK_LIMIT")); raw != "" {
		if v, err := strconv.ParseUint(raw, 10, 16); err == nil {
			g.stackLimit = uint16(v)
		}
	}
	if g.stackLimit < runtimeStackTop {
		g.stackLimit = runtimeStackTop
	}
}

func (g *gen) prepareFrames() error {
	for _, fn := range g.prog.Funcs {
		if fn.Name == "main" {
			continue
		}

		localCount := countLocalsInStmtList(fn.Body)
		inlineTemps := maxInlineArgsInStmtList(fn.Body, g.inline)
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
		fr.inlineTempStart = fr.localStart + localCount
		fr.inlineTempCount = inlineTemps
		fr.frameSize = fr.inlineTempStart + fr.inlineTempCount
		fr.localNextOffset = fr.localStart
		g.frames[fn.Name] = fr
	}
	return nil
}

func maxInlineArgsInStmtList(stmts []Stmt, inline map[string]inlineFn) int {
	maxArgs := 0
	for _, st := range stmts {
		if n := maxInlineArgsInStmt(st, inline); n > maxArgs {
			maxArgs = n
		}
	}
	return maxArgs
}

func maxInlineArgsInStmt(st Stmt, inline map[string]inlineFn) int {
	switch n := st.(type) {
	case *VarDeclStmt:
		if n.Decl.Init != nil {
			return maxInlineArgsInExpr(n.Decl.Init, inline)
		}
	case *AssignStmt:
		return maxInlineArgsInExpr(n.Value, inline)
	case *ExprStmt:
		return maxInlineArgsInExpr(n.Value, inline)
	case *ReturnStmt:
		if n.Value != nil {
			return maxInlineArgsInExpr(n.Value, inline)
		}
	case *IfStmt:
		maxArgs := maxInlineArgsInExpr(n.Cond, inline)
		if t := maxInlineArgsInStmtList(n.Then, inline); t > maxArgs {
			maxArgs = t
		}
		if e := maxInlineArgsInStmtList(n.Else, inline); e > maxArgs {
			maxArgs = e
		}
		return maxArgs
	case *WhileStmt:
		maxArgs := maxInlineArgsInExpr(n.Cond, inline)
		if b := maxInlineArgsInStmtList(n.Body, inline); b > maxArgs {
			maxArgs = b
		}
		return maxArgs
	}
	return 0
}

func maxInlineArgsInExpr(e Expr, inline map[string]inlineFn) int {
	switch n := e.(type) {
	case *UnaryExpr:
		return maxInlineArgsInExpr(n.X, inline)
	case *BinaryExpr:
		left := maxInlineArgsInExpr(n.Left, inline)
		right := maxInlineArgsInExpr(n.Right, inline)
		if right > left {
			return right
		}
		return left
	case *CallExpr:
		maxArgs := 0
		if _, ok := inline[n.Callee]; ok {
			maxArgs = len(n.Args)
		}
		for _, a := range n.Args {
			if m := maxInlineArgsInExpr(a, inline); m > maxArgs {
				maxArgs = m
			}
		}
		return maxArgs
	default:
		return 0
	}
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
		slot, err := g.allocLocal(n.Decl.Type)
		if err != nil {
			return err
		}
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
		switch n.Op {
		case "&&":
			return g.emitLogicalAnd(n.Left, n.Right)
		case "||":
			return g.emitLogicalOr(n.Left, n.Right)
		}
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
		if n.Callee == "wait" {
			return g.emitBuiltinWait(n)
		}
		if n.Callee == "cyc" {
			return g.emitBuiltinCyc(n)
		}
		if n.Callee == "vblank" {
			return g.emitBuiltinVBlank(n)
		}
		return g.emitFunctionCall(n)
	default:
		return fmt.Errorf("unsupported expression")
	}
	return nil
}

func (g *gen) emitLogicalAnd(left, right Expr) error {
	falseLabel := g.label("land_false")
	endLabel := g.label("land_end")

	if err := g.emitExpr(left); err != nil {
		return err
	}
	g.emit("MOV %0 Y")
	g.emit("CMP")
	g.emit("JEQ " + falseLabel)

	if err := g.emitExpr(right); err != nil {
		return err
	}
	g.emit("MOV %0 Y")
	g.emit("CMP")
	g.emit("JEQ " + falseLabel)

	g.emit("MOV %1 X")
	g.emit("JMP " + endLabel)
	g.emit(":" + falseLabel)
	g.emit("MOV %0 X")
	g.emit(":" + endLabel)
	return nil
}

func (g *gen) emitLogicalOr(left, right Expr) error {
	trueLabel := g.label("lor_true")
	endLabel := g.label("lor_end")

	if err := g.emitExpr(left); err != nil {
		return err
	}
	g.emit("MOV %0 Y")
	g.emit("CMP")
	g.emit("JNE " + trueLabel)

	if err := g.emitExpr(right); err != nil {
		return err
	}
	g.emit("MOV %0 Y")
	g.emit("CMP")
	g.emit("JNE " + trueLabel)

	g.emit("MOV %0 X")
	g.emit("JMP " + endLabel)
	g.emit(":" + trueLabel)
	g.emit("MOV %1 X")
	g.emit(":" + endLabel)
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

func (g *gen) emitBuiltinWait(call *CallExpr) error {
	if len(call.Args) != 1 {
		return fmt.Errorf("wait expects 1 argument")
	}
	if err := g.emitExpr(call.Args[0]); err != nil {
		return err
	}
	g.emit("WAIT X")
	g.emit("MOV %0 X")
	return nil
}

func (g *gen) emitBuiltinCyc(call *CallExpr) error {
	if len(call.Args) != 0 {
		return fmt.Errorf("cyc expects 0 arguments")
	}
	g.emit("MOV CYC X")
	return nil
}

func (g *gen) emitBuiltinVBlank(call *CallExpr) error {
	if len(call.Args) != 0 {
		return fmt.Errorf("vblank expects 0 arguments")
	}
	g.emit("VBLANK")
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

	if in, ok := g.inline[fn.Name]; ok && g.inlineDepth[fn.Name] == 0 && g.currentFrame != nil {
		g.inlineDepth[fn.Name]++
		err := g.emitInlineFunctionCall(in, call)
		g.inlineDepth[fn.Name]--
		return err
	}

	fr, ok := g.frames[fn.Name]
	if !ok {
		return fmt.Errorf("missing frame for function %q", fn.Name)
	}
	if len(call.Args) != len(fr.paramSlots) {
		return fmt.Errorf("%s expects %d args", fn.Name, len(fr.paramSlots))
	}

	// Save caller frame base on the hardware stack.
	g.loadFromGlobal(g.frameBaseSlot)
	g.emit("PUSH X")

	for _, a := range call.Args {
		if err := g.emitExpr(a); err != nil {
			return err
		}
		g.emit("PUSH X")
	}

	// Enter callee frame: frameBase := old stackTop, stackTop += frameSize.
	g.loadFromGlobal(g.stackTopSlot)
	g.storeToGlobal(g.frameBaseSlot)
	if fr.frameSize > 0 {
		if err := g.emitStackTopAdvance(fr.frameSize); err != nil {
			return err
		}
	}

	for i := len(fr.paramSlots) - 1; i >= 0; i-- {
		g.emit("POP X")
		g.storeToStorage(fr.paramSlots[i])
	}

	if fr.nextReturnID >= 0xFFFF {
		return fmt.Errorf("too many call sites for function %q", fn.Name)
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

	// Leave callee frame and restore caller base.
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

func (g *gen) emitStackTopAdvance(frameSize int) error {
	if frameSize < 0 {
		return fmt.Errorf("invalid frame size %d", frameSize)
	}
	g.emit("MOV X Y")
	g.emit(fmt.Sprintf("ADD %%%d X", frameSize))
	if g.enableStackChecks {
		overflowLabel := g.label("stack_overflow")
		okLabel := g.label("stack_ok")

		// Detect 16-bit wraparound: new stack top must not become less than old top.
		g.emit("CMP")
		g.emit("JLT " + overflowLabel)

		g.emit(fmt.Sprintf("MOV %%%d Y", g.stackLimit))
		g.emit("CMP")
		g.emit("JGT " + overflowLabel)
		g.emit("JMP " + okLabel)
		g.emit(":" + overflowLabel)
		g.emit("HLT")
		g.emit(":" + okLabel)
	}
	g.storeToGlobal(g.stackTopSlot)
	return nil
}

func (g *gen) emitInlineFunctionCall(in inlineFn, call *CallExpr) error {
	if len(call.Args) != len(in.fn.Params) {
		return fmt.Errorf("%s expects %d args", in.fn.Name, len(in.fn.Params))
	}
	if g.currentFrame == nil {
		return fmt.Errorf("cannot inline %q at global scope", in.fn.Name)
	}
	if len(call.Args) > g.currentFrame.inlineTempCount {
		return fmt.Errorf("insufficient inline temporary storage in %q", g.currentFrame.decl.Name)
	}

	tempSlots := make([]storage, len(call.Args))
	for i, a := range call.Args {
		if err := g.emitExpr(a); err != nil {
			return err
		}
		tempSlots[i] = storage{kind: storageFrame, offset: g.currentFrame.inlineTempStart + i, typ: in.fn.Params[i].Type}
		g.storeToStorage(tempSlots[i])
	}

	g.pushScope()
	defer g.popScope()
	for i, p := range in.fn.Params {
		if err := g.declare(p.Name, tempSlots[i]); err != nil {
			return err
		}
	}

	if in.ret.Value == nil {
		g.emit("MOV %0 X")
		return nil
	}
	return g.emitExpr(in.ret.Value)
}

func shortInlineCandidate(fn *FuncDecl) (inlineFn, bool) {
	if fn.Name == "main" {
		return inlineFn{}, false
	}
	if len(fn.Body) != 1 {
		return inlineFn{}, false
	}
	r, ok := fn.Body[0].(*ReturnStmt)
	if !ok {
		return inlineFn{}, false
	}
	if r.Value == nil {
		return inlineFn{}, true
	}
	if exprCost(r.Value) > 8 {
		return inlineFn{}, false
	}
	if exprCallsName(r.Value, fn.Name) {
		return inlineFn{}, false
	}
	return inlineFn{fn: fn, ret: r}, true
}

func exprCost(e Expr) int {
	switch n := e.(type) {
	case *IntLit, *IdentExpr:
		return 1
	case *UnaryExpr:
		return 1 + exprCost(n.X)
	case *BinaryExpr:
		return 1 + exprCost(n.Left) + exprCost(n.Right)
	case *CallExpr:
		total := 1
		for _, a := range n.Args {
			total += exprCost(a)
		}
		return total
	default:
		return 99
	}
}

func exprCallsName(e Expr, name string) bool {
	switch n := e.(type) {
	case *UnaryExpr:
		return exprCallsName(n.X, name)
	case *BinaryExpr:
		return exprCallsName(n.Left, name) || exprCallsName(n.Right, name)
	case *CallExpr:
		if n.Callee == name {
			return true
		}
		for _, a := range n.Args {
			if exprCallsName(a, name) {
				return true
			}
		}
		return false
	default:
		return false
	}
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

func (g *gen) allocGlobal(t TypeKind) (storage, error) {
	if g.nextAddr >= runtimeStackTop {
		return storage{}, fmt.Errorf("global storage overflow: reached stack region at $%04X", g.nextAddr)
	}
	name := "V_" + alphaName(int(g.nextAddr))
	g.consts = append(g.consts, fmt.Sprintf("@const %s $%04X", name, g.nextAddr))
	g.nextAddr++
	return storage{kind: storageGlobal, operand: "<" + name + ">", typ: t}, nil
}

func (g *gen) allocLocal(t TypeKind) (storage, error) {
	if g.currentFrame == nil {
		// main has no software frame, so its locals use compiler-managed global slots.
		return g.allocGlobal(t)
	}
	maxLocal := g.currentFrame.localStart + g.currentFrame.localSlotCount
	if g.currentFrame.localNextOffset >= maxLocal {
		return storage{}, fmt.Errorf("local slot overflow in function %q", g.currentFrame.decl.Name)
	}
	s := storage{kind: storageFrame, offset: g.currentFrame.localNextOffset, typ: t}
	g.currentFrame.localNextOffset++
	return s, nil
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
	g.ir = append(g.ir, parseIRLine(line))
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
