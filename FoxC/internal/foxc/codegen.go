package foxc

import (
	"fmt"
	"os"
	"strconv"
	"strings"
)

const (
	globalBaseAddr  = 0x8000
	globalLimitAddr = 0xEC78
	// Keep the runtime stack above FoxOS's embedded-program area at $7000.
	runtimeStackTop = 0x7800

	// 32-bit (EM=2) builds move globals above the resident shell text (which
	// ends just below $A000). Large global arrays (such as the editor's text
	// buffer) can grow past $E000, so the software stack is moved to the first
	// free word after the globals rather than assuming a fixed base.
	em2GlobalBaseAddr  = 0xA000
	em2RuntimeStackTop = 0xE000

	// Minimum space reserved for the software stack between the end of the
	// globals and the VRAM window at $EC78.
	minEm2StackWords = 0x400

	// The emulator starts SP just below the 5000-word VRAM window. Startup
	// re-seats it on every entry, including the OSRET restart path.
	hardwareStackTop = 0xFFFF - 5000
)

const (
	regX = "X"
	regY = "Y"
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
	typ     Type
	isArray bool
	length  int
	// reg is set when a scalar variable is held in an extended general-purpose
	// register instead of memory. Empty means the value lives in memory.
	reg string
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
	prog         *Program
	extendedMode bool
	em2Am1Mode   bool

	consts []string
	data   []string
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
	arrayTemp0    storage
	arrayTemp1    storage

	currentFrame *funcFrame
	inlineDepth  map[string]int
	stringLabels map[string]string

	// Register allocation state. R0-R7 are only available in extended mode;
	// tempPool is reserved for expression temporaries and localPool is used
	// for caching scalar locals/parameters across a function.
	tempPool     []string
	localPool    []string
	tempRegs     []string
	localRegs    []string
	addressTaken map[string]bool

	enableStackChecks bool
	stackLimit        uint16

	// widePointers selects two-word pointer storage. It is only needed when
	// the image is linked above the 16-bit word window, as foreground
	// programs are; keeping low images on one-word pointers avoids inflating
	// the resident shell past the globals base.
	widePointers bool

	// Memory layout for this target. 32-bit images get a higher stack and
	// globals base so their code has more room to grow.
	stackTop uint16
}

func generate(prog *Program, opts CompileOptions) (string, error) {
	globalBase := uint16(globalBaseAddr)
	stackTop := uint16(runtimeStackTop)
	if isEm2Am1Mode(opts.Mode) {
		globalBase = em2GlobalBaseAddr
		stackTop = em2RuntimeStackTop
	}
	g := &gen{
		prog:         prog,
		extendedMode: isExtendedMode(opts.Mode),
		em2Am1Mode:   isEm2Am1Mode(opts.Mode),
		nextAddr:     globalBase,
		stackTop:     uint16(stackTop),
		widePointers: isEm2Am1Mode(opts.Mode) && opts.LoadAddress > 0xFFFF,
		globals:      map[string]storage{},
		funcs:        map[string]*FuncDecl{},
		frames:       map[string]*funcFrame{},
		inline:       map[string]inlineFn{},
		inlineDepth:  map[string]int{},
		stringLabels: map[string]string{},
		addressTaken: map[string]bool{},
		stackLimit:   0xFFFF,
	}
	g.configureStackChecks()
	g.configureRegisters()

	for _, fn := range prog.Funcs {
		if fn.IsPrototype {
			continue
		}
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
		var (
			slot storage
			err  error
		)
		if gl.IsArray {
			slot, err = g.allocGlobalArray(gl.Type, gl.ArrayLen)
		} else {
			slot, err = g.allocGlobal(gl.Type)
		}
		if err != nil {
			return "", err
		}
		g.globals[gl.Name] = slot
	}

	var err error
	// Runtime stack and frame pointers stay one word: their values live in
	// the low 64K RAM window, even for foreground programs linked at
	// $100000. Keeping these slots 16-bit avoids expanding every u32
	// operation in the image into a two-word sequence.
	runtimeSlotType := Type{Base: TypeU16}
	g.stackTopSlot, err = g.allocGlobal(runtimeSlotType)
	if err != nil {
		return "", err
	}
	g.frameBaseSlot, err = g.allocGlobal(runtimeSlotType)
	if err != nil {
		return "", err
	}
	g.arrayTemp0, err = g.allocGlobal(Type{Base: TypeU16})
	if err != nil {
		return "", err
	}
	g.arrayTemp1, err = g.allocGlobal(Type{Base: TypeU16})
	if err != nil {
		return "", err
	}

	if g.em2Am1Mode {
		// Globals grow upward from $A000 and can cross the default $E000 stack
		// base. Start the software stack after the last global so frames never
		// overwrite global arrays. Small images keep the original $E000 base.
		if g.nextAddr > g.stackTop {
			g.stackTop = g.nextAddr
			if g.stackLimit < g.stackTop {
				g.stackLimit = g.stackTop
			}
		}
		if int(g.stackTop)+minEm2StackWords > int(globalLimitAddr) {
			return "", fmt.Errorf(
				"global storage leaves less than %d words for the stack (globals end at $%04X)",
				minEm2StackWords, g.nextAddr)
		}
	}

	if err := g.prepareFrames(); err != nil {
		return "", err
	}

	g.emit(":main")
	if g.em2Am1Mode {
		// FoxC's data model is word based: globals, frame slots, arrays and
		// label addresses are all counted in 16-bit words. Use the V2
		// instruction set (EM=2) with word addressing (AM=0) so indirect
		// loads/stores agree with that model. Mid-level code can still switch
		// to byte addressing at run time with set_am(1).
		g.emit("MOV %2 EM")
		g.emit("MOV %0 AM")
	} else if g.extendedMode {
		g.emit("MOV %1 EM")
	}
	// Restarting through OSRET re-enters this startup code with whatever
	// hardware stack the previous program left behind. Re-seat SP so a
	// foreground program cannot corrupt the shell's stack frames.
	g.emit(fmt.Sprintf("MOV %%%d SP", hardwareStackTop))
	g.emit(fmt.Sprintf("MOV %%%d X", g.stackTop))
	g.storeToGlobal(g.stackTopSlot)
	g.emit("MOV %0 X")
	g.storeToGlobal(g.frameBaseSlot)

	for _, gl := range prog.Globals {
		if gl.IsArray {
			if gl.Init != nil {
				str, ok := gl.Init.(*StringLit)
				if !ok {
					return "", fmt.Errorf("unsupported initializer for global array %q", gl.Name)
				}
				if err := g.emitStringArrayInit(g.globals[gl.Name], str.Value); err != nil {
					return "", err
				}
			}
			continue
		}
		if gl.Init == nil {
			continue
		}
		if err := g.emitExpr(gl.Init); err != nil {
			return "", err
		}
		g.storeToStorage(g.globals[gl.Name])
	}

	g.resetFunctionRegisters()
	g.addressTaken = collectAddressTakenStmtList(mainFn.Body)
	g.pushScope()
	if err := g.emitStmtList(mainFn.Body); err != nil {
		return "", err
	}
	g.popScope()
	g.emit("HLT")

	for _, fn := range prog.Funcs {
		if fn.IsPrototype || fn.Name == "main" {
			continue
		}
		if err := g.emitFunction(g.frames[fn.Name]); err != nil {
			return "", err
		}
	}

	for _, fn := range prog.Funcs {
		if fn.IsPrototype || fn.Name == "main" {
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
	out.WriteString(renderIR(g.ir))
	if len(g.data) > 0 {
		out.WriteString("\n\n@segment data\n")
		out.WriteString(strings.Join(g.data, "\n"))
	}

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
	if g.stackLimit < g.stackTop {
		g.stackLimit = g.stackTop
	}
}

// configureRegisters wires up the extended register file. R0-R7 are only
// available when the target is extended mode; legacy builds keep the old
// X/Y + stack evaluation strategy.
//
// Two registers are reserved for expression temporaries so that ordinary
// expressions never need to touch memory. The remaining registers are handed
// out to scalar locals and parameters, which removes repeated frame loads and
// stores from hot loops.
func (g *gen) configureRegisters() {
	if !g.extendedMode {
		return
	}

	all := []string{"R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7"}
	const tempCount = 2

	g.tempPool = append(g.tempPool, all[:tempCount]...)
	for i := len(all) - 1; i >= tempCount; i-- {
		g.localPool = append(g.localPool, all[i])
	}
}

func (g *gen) allocTemp() string {
	if len(g.tempRegs) >= len(g.tempPool) {
		return ""
	}
	r := g.tempPool[len(g.tempRegs)]
	g.tempRegs = append(g.tempRegs, r)
	return r
}

func (g *gen) releaseTemp(r string) {
	if r == "" {
		return
	}
	for i := len(g.tempRegs) - 1; i >= 0; i-- {
		if g.tempRegs[i] == r {
			g.tempRegs = append(g.tempRegs[:i], g.tempRegs[i+1:]...)
			return
		}
	}
}

// reserveLocalReg assigns a persistent register to a local/parameter when one
// is available. Variables whose address is taken must stay in memory so that
// pointer writes remain visible.
func (g *gen) reserveLocalReg(name string) string {
	if g.addressTaken[name] || len(g.localRegs) >= len(g.localPool) {
		return ""
	}
	r := g.localPool[len(g.localRegs)]
	g.localRegs = append(g.localRegs, r)
	return r
}

func (g *gen) resetFunctionRegisters() {
	g.tempRegs = g.tempRegs[:0]
	g.localRegs = g.localRegs[:0]
	g.addressTaken = map[string]bool{}
}

// spillLiveRegisters saves every live register to the hardware stack. It is
// used around calls because the callee is free to use any R register.
func (g *gen) spillLiveRegisters() []string {
	regs := make([]string, 0, len(g.localRegs)+len(g.tempRegs))
	regs = append(regs, g.localRegs...)
	regs = append(regs, g.tempRegs...)
	for _, r := range regs {
		g.emit("PUSH " + r)
	}
	return regs
}

func (g *gen) restoreLiveRegisters(regs []string) {
	for i := len(regs) - 1; i >= 0; i-- {
		g.emit("POP " + regs[i])
	}
}

func (g *gen) prepareFrames() error {
	for _, fn := range g.prog.Funcs {
		if fn.IsPrototype || fn.Name == "main" {
			continue
		}

		localCount := g.countLocalsInStmtList(fn.Body)
		inlineTemps := maxInlineArgsInStmtList(fn.Body, g.inline)
		next := 0
		fr := &funcFrame{
			decl:          fn,
			entryLabel:    "func_" + fn.Name,
			dispatchLabel: "func_" + fn.Name + "_dispatch",
			paramByName:   map[string]storage{},
		}

		fr.retIDSlot = storage{kind: storageFrame, offset: next, typ: Type{Base: TypeU16}}
		next++

		if fn.Ret.Base != TypeVoid || fn.Ret.Ptr != 0 {
			slot := storage{kind: storageFrame, offset: next, typ: fn.Ret}
			fr.retSlot = &slot
			next += g.wordCount(fn.Ret)
		}

		for _, p := range fn.Params {
			slot := storage{kind: storageFrame, offset: next, typ: p.Type}
			next += g.wordCount(p.Type)
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

	g.resetFunctionRegisters()
	g.addressTaken = collectAddressTakenStmtList(fr.decl.Body)

	g.emit(":" + fr.entryLabel)
	g.pushScope()
	for i, p := range fr.decl.Params {
		memSlot := fr.paramSlots[i]
		slot := memSlot
		if r := g.reserveLocalReg(p.Name); r != "" {
			slot.reg = r
			g.loadFromStorageInto(memSlot, r)
		}
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
		var (
			slot storage
			err  error
		)
		if n.Decl.IsArray {
			slot, err = g.allocLocalArray(n.Decl.Type, n.Decl.ArrayLen)
		} else {
			slot, err = g.allocLocal(n.Decl.Type)
		}
		if err != nil {
			return err
		}
		if !slot.isArray {
			slot.reg = g.reserveLocalReg(n.Decl.Name)
		}
		if err := g.declare(n.Decl.Name, slot); err != nil {
			return err
		}
		if n.Decl.Init != nil {
			if slot.isArray {
				str, ok := n.Decl.Init.(*StringLit)
				if !ok {
					return fmt.Errorf("unsupported initializer for array %q", n.Decl.Name)
				}
				return g.emitStringArrayInit(slot, str.Value)
			}
			if err := g.emitStoreToStorage(slot, n.Decl.Init); err != nil {
				return err
			}
		}
	case *AssignStmt:
		switch lhs := n.LHS.(type) {
		case *IdentExpr:
			slot, ok := g.lookup(lhs.Name)
			if !ok {
				return fmt.Errorf("unknown variable %q", lhs.Name)
			}
			if slot.isArray {
				return fmt.Errorf("cannot assign to array %q without an index", lhs.Name)
			}
			if err := g.emitStoreToStorage(slot, n.Value); err != nil {
				return err
			}
		case *IndexExpr:
			slot, ok := g.lookup(lhs.Name)
			if !ok {
				return fmt.Errorf("unknown variable %q", lhs.Name)
			}
			if !slot.isArray {
				return fmt.Errorf("%q is not an array", lhs.Name)
			}
			if err := g.emitArrayStore(slot, lhs.Index, n.Value); err != nil {
				return err
			}
		case *UnaryExpr:
			if lhs.Op == "*" {
				// Dereference assignment: *(expr) = value
				if err := g.emitDerefStore(lhs.X, n.Value); err != nil {
					return err
				}
			} else {
				return fmt.Errorf("unsupported lvalue in assignment")
			}
		default:
			return fmt.Errorf("unsupported lvalue in assignment")
		}
	case *AssignIndexStmt:
		slot, ok := g.lookup(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		if !slot.isArray {
			return fmt.Errorf("%q is not an array", n.Name)
		}
		if err := g.emitArrayStore(slot, n.Index, n.Value); err != nil {
			return err
		}
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
		g.emitCompareToZero(regX)
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
		g.emitCompareToZero(regX)
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
	return g.emitExprInto(e, regX)
}

// emitExprInto evaluates e and leaves the result in dst. dst is always X or an
// extended register; Y is reserved as a scratch register.
func (g *gen) emitExprInto(e Expr, dst string) error {
	switch n := e.(type) {
	case *IntLit:
		g.emitLoadImmediate(dst, uint32(n.Value))
	case *CharLit:
		g.emit(fmt.Sprintf("MOV %%%d %s", n.Value, dst))
	case *StringLit:
		label := g.stringLabel(n.Value)
		// A 32-bit image can place its data segment above 0xFFFF. Build the
		// pointer from both halves instead of emitting a label immediate that
		// the assembler would have to truncate to 16 bits.
		high, low, ok := splitRegister(dst)
		if !ok {
			g.emit("MOV " + label + " " + dst)
			return nil
		}
		g.emit("MOV @hi(" + label + ") " + high)
		g.emit("MOV @lo(" + label + ") " + low)
	case *IdentExpr:
		slot, ok := g.lookup(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}

		if slot.isArray {
			return fmt.Errorf("array %q requires an index", n.Name)
		}
		g.loadFromStorageInto(slot, dst)
	case *IndexExpr:
		slot, ok := g.lookup(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		if !slot.isArray {
			return fmt.Errorf("%q is not an array", n.Name)
		}
		if err := g.emitArrayLoadInto(slot, n.Index, dst); err != nil {
			return err
		}
	case *UnaryExpr:
		switch n.Op {
		case "&":
			// Address-of operator: return the address of the operand
			if err := g.emitAddressOfInto(n.X, dst); err != nil {
				return err
			}
		case "*":
			// Dereference operator: the address must be in X or Y.
			if err := g.emitExprInto(n.X, regX); err != nil {
				return err
			}
			resultType := g.exprType(n)
			if resultType.Ptr == 0 && resultType.Base == TypeU32 {
				if !g.loadU32FromAddressInX(dst) {
					g.emit("LOD " + dst + " X")
				}
			} else {
				g.emit("LOD " + dst + " X")
			}
		case "-", "~":
			if err := g.emitExprInto(n.X, dst); err != nil {
				return err
			}
			resultType := g.exprType(n)
			if n.Op == "-" {
				if dst == regX {
					g.emit("MOV %0 Y")
					g.emit("SUB X Y")
					g.emit("MOV Y X")
				} else {
					g.emit("MOV %0 X")
					g.emit("SUB " + dst + " X")
					g.emit("MOV X " + dst)
				}
			} else if resultType.Ptr == 0 && resultType.Base == TypeU32 {
				// 32-bit complement: build 0xFFFFFFFF in a scratch register
				// and XOR it into dst.
				scratch := regY
				if dst == regY {
					scratch = regX
				}
				high, low, ok := splitRegister(scratch)
				if !ok {
					return fmt.Errorf("cannot materialise 32-bit complement")
				}
				g.emit(fmt.Sprintf("MOV %%%d %s", 0xFFFF, high))
				g.emit(fmt.Sprintf("MOV %%%d %s", 0xFFFF, low))
				g.emit("XOR " + scratch + " " + dst)
			} else {
				g.emit("XOR %65535 " + dst)
			}
			g.maskResult(dst, resultType)
		default:
			return fmt.Errorf("unsupported unary operator %q", n.Op)
		}
	case *BinaryExpr:
		switch n.Op {
		case "&&":
			return g.emitLogicalAndInto(n.Left, n.Right, dst)
		case "||":
			return g.emitLogicalOrInto(n.Left, n.Right, dst)
		}
		if err := g.emitBinaryInto(n, dst); err != nil {
			return err
		}
	case *CallExpr:
		return g.emitCallInto(n, dst)
	default:
		return fmt.Errorf("unsupported expression")
	}
	return nil
}

func (g *gen) emitBinaryInto(n *BinaryExpr, dst string) error {
	if n.Op == "==" || n.Op == "!=" || n.Op == "<" || n.Op == ">" || n.Op == "<=" || n.Op == ">=" {
		return g.emitComparisonInto(n, dst)
	}
	if !isSupportedBinaryOp(n.Op) {
		return fmt.Errorf("unsupported binary operator %q", n.Op)
	}

	if tmp := g.allocTemp(); tmp != "" {
		defer g.releaseTemp(tmp)
		if err := g.emitExprInto(n.Left, tmp); err != nil {
			return err
		}
		if err := g.emitExprInto(n.Right, dst); err != nil {
			return err
		}
		lt, rt := g.exprType(n.Left), g.exprType(n.Right)
		switch {
		case (n.Op == "+" || n.Op == "-") && lt.Ptr > 0 && rt.Ptr == 0:
			// pointer +/- integer: scale the integer by the element width.
			g.emitScaleIndex(dst, pointerElementWords(lt))
		case n.Op == "+" && rt.Ptr > 0 && lt.Ptr == 0:
			g.emitScaleIndex(tmp, pointerElementWords(rt))
		}
		if commutativeOp(n.Op) {
			g.emitBinaryOp(n.Op, tmp, dst)
			g.maskResult(dst, g.exprType(n))
		} else {
			// dst = tmp OP dst; compute into tmp so the operand order is
			// left OP right, then move the result into dst.
			g.emitBinaryOp(n.Op, dst, tmp)
			g.emit("MOV " + tmp + " " + dst)
			if n.Op == "-" && lt.Ptr > 0 && rt.Ptr > 0 {
				g.emitScaleIndex(dst, -pointerElementWords(lt))
			}
			g.maskResult(dst, g.exprType(n))
		}
		return nil
	}

	// No free register: fall back to the hardware stack.
	if err := g.emitExprInto(n.Left, dst); err != nil {
		return err
	}
	g.emit("PUSH " + dst)
	if err := g.emitExprInto(n.Right, regX); err != nil {
		return err
	}
	g.emit("MOV X Y")
	g.emit("POP " + dst)
	lt, rt := g.exprType(n.Left), g.exprType(n.Right)
	switch {
	case (n.Op == "+" || n.Op == "-") && lt.Ptr > 0 && rt.Ptr == 0:
		g.emitScaleIndex(regY, pointerElementWords(lt))
	case n.Op == "+" && rt.Ptr > 0 && lt.Ptr == 0:
		g.emitScaleIndex(dst, pointerElementWords(rt))
	}
	g.emitBinaryOp(n.Op, "Y", dst)
	if n.Op == "-" && lt.Ptr > 0 && rt.Ptr > 0 {
		g.emitScaleIndex(dst, -pointerElementWords(lt))
	}
	g.maskResult(dst, g.exprType(n))
	return nil
}

func (g *gen) emitComparisonInto(n *BinaryExpr, dst string) error {
	if tmp := g.allocTemp(); tmp != "" {
		defer g.releaseTemp(tmp)
		if err := g.emitExprInto(n.Left, tmp); err != nil {
			return err
		}
		if err := g.emitExprInto(n.Right, dst); err != nil {
			return err
		}
		g.emit("CMP " + tmp + " " + dst)
		return g.emitCompareResultTo(n.Op, dst)
	}

	if err := g.emitExprInto(n.Left, dst); err != nil {
		return err
	}
	g.emit("PUSH " + dst)
	if err := g.emitExprInto(n.Right, regX); err != nil {
		return err
	}
	g.emit("MOV X Y")
	g.emit("POP " + dst)
	if g.extendedMode {
		g.emit("CMP " + dst + " Y")
	} else {
		if dst != regX {
			return fmt.Errorf("internal error: legacy comparison destination must be X, got %s", dst)
		}
		g.emit("CMP")
	}
	return g.emitCompareResultTo(n.Op, dst)
}

func isSupportedBinaryOp(op string) bool {
	switch op {
	case "+", "-", "*", "/", "&", "|", "^", "<<", ">>":
		return true
	default:
		return false
	}
}

func commutativeOp(op string) bool {
	switch op {
	case "+", "*", "&", "|", "^":
		return true
	default:
		return false
	}
}

func (g *gen) emitBinaryOp(op, src, dst string) {
	switch op {
	case "+":
		g.emit("ADD " + src + " " + dst)
	case "-":
		g.emit("SUB " + src + " " + dst)
	case "*":
		g.emit("MUL " + src + " " + dst)
	case "/":
		g.emit("DIV " + src + " " + dst)
	case "&":
		g.emit("AND " + src + " " + dst)
	case "|":
		g.emit("OR " + src + " " + dst)
	case "^":
		g.emit("XOR " + src + " " + dst)
	case "<<":
		g.emit("SHL " + src + " " + dst)
	case ">>":
		g.emit("SHR " + src + " " + dst)
	}
}

// emitCallInto evaluates a call and places the result in dst.
func (g *gen) emitCallInto(n *CallExpr, dst string) error {
	switch n.Callee {
	case "poke":
		if err := g.emitBuiltinPoke(n); err != nil {
			return err
		}
		g.emit("MOV %0 " + dst)
		return nil
	case "peek":
		return g.emitBuiltinPeekInto(n, dst)
	case "wait":
		if err := g.emitBuiltinWait(n); err != nil {
			return err
		}
		g.emit("MOV %0 " + dst)
		return nil
	case "cyc":
		return g.emitBuiltinCycInto(n, dst)
	case "get_memory_size":
		return g.emitBuiltinGetMemorySizeInto(n, dst)
	case "vblank":
		return g.emitBuiltinVBlank(n)
	case "in_port":
		return g.emitBuiltinInPortInto(n, dst)
	case "in_port32":
		return g.emitBuiltinInPortInto(n, dst)
	case "out_port":
		if err := g.emitBuiltinOutPort(n); err != nil {
			return err
		}
		g.emit("MOV %0 " + dst)
		return nil
	case "out_port32":
		if err := g.emitBuiltinOutPort(n); err != nil {
			return err
		}
		g.emit("MOV %0 " + dst)
		return nil
	case "am":
		g.emit("MOV AM " + dst)
		return nil
	case "set_am":
		if len(n.Args) != 1 {
			return fmt.Errorf("set_am expects 1 argument")
		}
		if err := g.emitExprInto(n.Args[0], regX); err != nil {
			return err
		}
		g.emit("MOV X AM")
		g.emit("MOV %0 " + dst)
		return nil
	case "em":
		g.emit("MOV EM " + dst)
		return nil
	case "set_em":
		if len(n.Args) != 1 {
			return fmt.Errorf("set_em expects 1 argument")
		}
		if err := g.emitExprInto(n.Args[0], regX); err != nil {
			return err
		}
		g.emit("MOV X EM")
		g.emit("MOV %0 " + dst)
		return nil
	case "os_return":
		return g.emitBuiltinOSReturn(n)
	case "os_start":
		return g.emitBuiltinOSStart(n)
	}
	if err := g.emitFunctionCall(n); err != nil {
		return err
	}
	if dst != regX {
		g.emit("MOV X " + dst)
	}
	return nil
}

func (g *gen) emitStringArrayInit(slot storage, value string) error {
	if !slot.isArray || slot.typ.Base != TypeU8 || slot.typ.Ptr != 0 {
		return fmt.Errorf("string initializer requires a u8 array")
	}
	bytes := append([]byte(value), 0)
	for i, b := range bytes {
		if err := g.emitArrayStore(slot, &IntLit{Value: i}, &CharLit{Value: b}); err != nil {
			return err
		}
	}
	return nil
}

func (g *gen) stringLabel(value string) string {
	if label, ok := g.stringLabels[value]; ok {
		return label
	}
	label := fmt.Sprintf("str_%d", len(g.stringLabels)+1)
	g.stringLabels[value] = label
	g.data = append(g.data, ":"+label)
	for _, b := range append([]byte(value), 0) {
		g.data = append(g.data, fmt.Sprintf("@word %%%d", b))
	}
	return label
}

func (g *gen) emitLogicalAnd(left, right Expr) error {
	return g.emitLogicalAndInto(left, right, regX)
}

func (g *gen) emitLogicalAndInto(left, right Expr, dst string) error {
	falseLabel := g.label("land_false")
	endLabel := g.label("land_end")

	if err := g.emitExprInto(left, regX); err != nil {
		return err
	}
	g.emitCompareToZero(regX)
	g.emit("JEQ " + falseLabel)

	if err := g.emitExprInto(right, regX); err != nil {
		return err
	}
	g.emitCompareToZero(regX)
	g.emit("JEQ " + falseLabel)

	g.emit("MOV %1 " + dst)
	g.emit("JMP " + endLabel)
	g.emit(":" + falseLabel)
	g.emit("MOV %0 " + dst)
	g.emit(":" + endLabel)
	return nil
}

func (g *gen) emitLogicalOr(left, right Expr) error {
	return g.emitLogicalOrInto(left, right, regX)
}

func (g *gen) emitLogicalOrInto(left, right Expr, dst string) error {
	trueLabel := g.label("lor_true")
	endLabel := g.label("lor_end")

	if err := g.emitExprInto(left, regX); err != nil {
		return err
	}
	g.emitCompareToZero(regX)
	g.emit("JNE " + trueLabel)

	if err := g.emitExprInto(right, regX); err != nil {
		return err
	}
	g.emitCompareToZero(regX)
	g.emit("JNE " + trueLabel)

	g.emit("MOV %0 " + dst)
	g.emit("JMP " + endLabel)
	g.emit(":" + trueLabel)
	g.emit("MOV %1 " + dst)
	g.emit(":" + endLabel)
	return nil
}

func (g *gen) emitBuiltinPoke(call *CallExpr) error {
	if len(call.Args) != 2 {
		return fmt.Errorf("poke expects 2 arguments")
	}
	if tmp := g.allocTemp(); tmp != "" {
		defer g.releaseTemp(tmp)
		if err := g.emitExprInto(call.Args[0], tmp); err != nil {
			return err
		}
		if err := g.emitExprInto(call.Args[1], regX); err != nil {
			return err
		}
		// The address operand must be X or Y; the value can be any register.
		g.emit("MOV " + tmp + " Y")
		g.emit("STR X Y")
		return nil
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
	return nil
}

func (g *gen) emitBuiltinPeek(call *CallExpr) error {
	return g.emitBuiltinPeekInto(call, regX)
}

func (g *gen) emitBuiltinPeekInto(call *CallExpr, dst string) error {
	if len(call.Args) != 1 {
		return fmt.Errorf("peek expects 1 argument")
	}
	if err := g.emitExprInto(call.Args[0], regX); err != nil {
		return err
	}
	g.emit("LOD " + dst + " X")
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
	return nil
}

func (g *gen) emitBuiltinCyc(call *CallExpr) error {
	return g.emitBuiltinCycInto(call, regX)
}

func (g *gen) emitBuiltinCycInto(call *CallExpr, dst string) error {
	if len(call.Args) != 0 {
		return fmt.Errorf("cyc expects 0 arguments")
	}
	g.emit("MOV CYC " + dst)
	return nil
}

// emitBuiltinGetMemorySizeInto reads the ISA V2.1 read-only MS register, which
// reports the installed RAM size in 1 KiB units. A primary-register destination keeps
// all 32 bits; smaller destinations receive the low word only.
func (g *gen) emitBuiltinGetMemorySizeInto(call *CallExpr, dst string) error {
	if len(call.Args) != 0 {
		return fmt.Errorf("get_memory_size expects 0 arguments")
	}
	g.emit("MOV MS " + dst)
	return nil
}

func (g *gen) emitBuiltinVBlank(call *CallExpr) error {
	if len(call.Args) != 0 {
		return fmt.Errorf("vblank expects 0 arguments")
	}
	g.emit("VBLANK")
	return nil
}

func (g *gen) emitBuiltinInPort(call *CallExpr) error {
	return g.emitBuiltinInPortInto(call, regX)
}

func (g *gen) emitBuiltinInPortInto(call *CallExpr, dst string) error {
	if len(call.Args) != 1 {
		return fmt.Errorf("in_port expects 1 argument")
	}
	port, err := portImmediateValue(call.Args[0])
	if err != nil {
		return err
	}
	g.emit(fmt.Sprintf("IN %%%d %s", port, dst))
	return nil
}

func (g *gen) emitBuiltinOutPort(call *CallExpr) error {
	if len(call.Args) != 2 {
		return fmt.Errorf("out_port expects 2 arguments")
	}
	port, err := portImmediateValue(call.Args[0])
	if err != nil {
		return err
	}
	// Second arg: value to write
	if err := g.emitExpr(call.Args[1]); err != nil {
		return err
	}
	g.emit(fmt.Sprintf("OUT X %%%d", port))
	return nil
}

func (g *gen) emitBuiltinOSReturn(call *CallExpr) error {
	if len(call.Args) != 0 {
		return fmt.Errorf("os_return expects no arguments")
	}
	g.emit("OSRET")
	return nil
}

func (g *gen) emitBuiltinOSStart(call *CallExpr) error {
	if len(call.Args) != 1 {
		return fmt.Errorf("os_start expects 1 argument")
	}
	// A 16-bit constant can use the compact absolute JMP encoding. In 32-bit
	// mode any other address is materialised in X and jumped through.
	if target, ok := call.Args[0].(*IntLit); ok {
		if target.Value < 0 || target.Value > 0xFFFFFFFF {
			return fmt.Errorf("os_start target is out of range")
		}
		if target.Value <= 0xFFFF {
			g.emit(fmt.Sprintf("JMP $%04X", target.Value))
			return nil
		}
	}
	if !g.em2Am1Mode {
		return fmt.Errorf("os_start target must be a 16-bit constant")
	}
	if err := g.emitExprInto(call.Args[0], regX); err != nil {
		return err
	}
	g.emit("JMP X")
	return nil
}

func portImmediateValue(expr Expr) (int, error) {
	lit, ok := expr.(*IntLit)
	if !ok {
		return 0, fmt.Errorf("port operand must be a constant (0-65535)")
	}
	if lit.Value < 0 || lit.Value > 65535 {
		return 0, fmt.Errorf("port operand %d out of range (0-65535)", lit.Value)
	}
	return lit.Value, nil
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

	// The callee may use any extended register, so preserve live caller
	// locals and expression temporaries first.
	savedRegs := g.spillLiveRegisters()

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

	g.restoreLiveRegisters(savedRegs)
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
	if fn.IsPrototype || fn.Name == "main" {
		return inlineFn{}, false
	}
	for _, p := range fn.Params {
		if p.Type.Base == TypeU32 && p.Type.Ptr == 0 {
			return inlineFn{}, false
		}
	}
	if fn.Ret.Base == TypeU32 && fn.Ret.Ptr == 0 {
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
	return g.emitCompareResultTo(op, regX)
}

// emitCompareToZero compares dst with zero and updates the comparison flags.
// Extended mode uses the V1.11 two-operand CMP; legacy mode keeps the
// original X-vs-Y sequence.
func (g *gen) emitCompareToZero(dst string) {
	if g.extendedMode {
		g.emit("CMP " + dst + " %0")
		return
	}
	if dst != regX {
		g.emit("MOV " + dst + " X")
	}
	g.emit("MOV %0 Y")
	g.emit("CMP")
}

// emitCompareResultTo materialises a boolean comparison result in dst. The
// comparison flags must already have been set by a CMP instruction.
func (g *gen) emitCompareResultTo(op string, dst string) error {
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
	g.emit("MOV %0 " + dst)
	g.emit("JMP " + endLabel)
	g.emit(":" + trueLabel)
	g.emit("MOV %1 " + dst)
	g.emit(":" + endLabel)
	return nil
}

// wordCount returns the number of 16-bit words a scalar occupies. Images
// linked above the 16-bit word window store pointers as two words, just like
// u32, so addresses above $FFFF survive a load/store. Low images keep the
// original one-word pointers.
func (g *gen) wordCount(t Type) int {
	if g.widePointers && t.Ptr > 0 {
		return 2
	}
	return typeWords(t)
}

func (g *gen) allocGlobal(t Type) (storage, error) {
	size := g.wordCount(t)
	if int(g.nextAddr)+size > int(globalLimitAddr) {
		return storage{}, fmt.Errorf("global storage overflow: reached reserved memory at $%04X", g.nextAddr)
	}
	name := "V_" + alphaName(int(g.nextAddr))
	g.consts = append(g.consts, fmt.Sprintf("@const %s $%04X", name, g.nextAddr))
	g.nextAddr += uint16(size)
	return storage{kind: storageGlobal, operand: "<" + name + ">", typ: t}, nil
}

func (g *gen) allocGlobalArray(elem Type, length int) (storage, error) {
	if elem.Ptr != 0 {
		return storage{}, fmt.Errorf("array of pointer element type not supported")
	}
	words := arrayWordCount(elem.Base, length)
	if words <= 0 {
		return storage{}, fmt.Errorf("invalid array length %d", length)
	}
	if int(g.nextAddr)+words > globalLimitAddr {
		return storage{}, fmt.Errorf("global array storage overflow")
	}
	name := "V_" + alphaName(int(g.nextAddr))
	g.consts = append(g.consts, fmt.Sprintf("@const %s $%04X", name, g.nextAddr))
	g.nextAddr += uint16(words)
	return storage{kind: storageGlobal, operand: "<" + name + ">", typ: elem, isArray: true, length: length}, nil
}

func (g *gen) allocLocal(t Type) (storage, error) {
	if g.currentFrame == nil {
		// main has no software frame, so its locals use compiler-managed global slots.
		return g.allocGlobal(t)
	}
	maxLocal := g.currentFrame.localStart + g.currentFrame.localSlotCount
	if g.currentFrame.localNextOffset+g.wordCount(t) > maxLocal {
		return storage{}, fmt.Errorf("local slot overflow in function %q", g.currentFrame.decl.Name)
	}
	s := storage{kind: storageFrame, offset: g.currentFrame.localNextOffset, typ: t}
	g.currentFrame.localNextOffset += g.wordCount(t)
	return s, nil
}

func (g *gen) allocLocalArray(elem Type, length int) (storage, error) {
	if elem.Ptr != 0 {
		return storage{}, fmt.Errorf("array of pointer element type not supported")
	}
	if g.currentFrame == nil {
		return g.allocGlobalArray(elem, length)
	}
	words := arrayWordCount(elem.Base, length)
	if words <= 0 {
		return storage{}, fmt.Errorf("invalid array length %d", length)
	}
	maxLocal := g.currentFrame.localStart + g.currentFrame.localSlotCount
	if g.currentFrame.localNextOffset+words > maxLocal {
		return storage{}, fmt.Errorf("local slot overflow in function %q", g.currentFrame.decl.Name)
	}
	s := storage{kind: storageFrame, offset: g.currentFrame.localNextOffset, typ: elem, isArray: true, length: length}
	g.currentFrame.localNextOffset += words
	return s, nil
}

func arrayWordCount(elem TypeKind, length int) int {
	if length <= 0 {
		return 0
	}
	switch elem {
	case TypeU8:
		return (length + 1) / 2
	case TypeU32:
		return length * 2
	default:
		return length
	}
}

func typeWords(t Type) int {
	if t.Ptr > 0 {
		return 1
	}
	if t.Base == TypeU32 {
		return 2
	}
	return 1
}

// pointerElementWords returns the number of 16-bit words occupied by one
// pointed-to element. Only u32 currently spans more than one word; u16 and
// pointer elements are one word, and u8 uses packed-byte semantics.
func pointerElementWords(t Type) int {
	if t.Ptr == 1 && t.Base == TypeU32 {
		return 2
	}
	return 1
}

// emitScaleIndex multiplies a register by the element width for positive
// values, or divides it for negative values. Only a width of two emits code.
func (g *gen) emitScaleIndex(reg string, words int) {
	switch words {
	case 2:
		g.emit("SHL %1 " + reg)
	case -2:
		g.emit("SHR %1 " + reg)
	}
}

func splitRegister(name string) (high string, low string, ok bool) {
	switch name {
	case "X":
		return "XH", "XL", true
	case "Y":
		return "YH", "YL", true
	case "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7":
		return name + "H", name + "L", true
	default:
		return "", "", false
	}
}

// scratchAddressRegister returns a primary register that can hold a memory
// address without clobbering dst. Y is reserved as the expression scratch
// register, so it is used unless dst itself is Y.
func scratchAddressRegister(dst string) string {
	if dst == regY {
		return regX
	}
	return regY
}

// loadU32FromStorageInto loads a two-word little-endian u32 value into the
// full-width register dst.
func (g *gen) loadU32FromStorageInto(slot storage, dst string) bool {
	high, low, ok := splitRegister(dst)
	if !ok {
		return false
	}
	addr := scratchAddressRegister(dst)
	if slot.kind == storageGlobal {
		g.emit("MOV " + slot.operand + " " + addr)
	} else {
		g.loadFrameAddressInto(slot.offset, addr)
	}
	g.emit("LOD " + low + " " + addr)
	g.emit("ADD %1 " + addr)
	g.emit("LOD " + high + " " + addr)
	return true
}

// storeU32ToStorageAddress stores a two-word little-endian u32 value held in
// valueReg into slot.
func (g *gen) storeU32ToStorageAddress(slot storage, valueReg string) bool {
	high, low, ok := splitRegister(valueReg)
	if !ok {
		return false
	}
	addr := scratchAddressRegister(valueReg)
	if slot.kind == storageGlobal {
		g.emit("MOV " + slot.operand + " " + addr)
	} else {
		g.loadFrameAddressInto(slot.offset, addr)
	}
	g.emit("STR " + low + " " + addr)
	g.emit("ADD %1 " + addr)
	g.emit("STR " + high + " " + addr)
	return true
}

// loadU32FromAddressInX loads a two-word little-endian u32 from the address in
// X. dst may be X; in that case the address is staged through Y first.
func (g *gen) loadU32FromAddressInX(dst string) bool {
	high, low, ok := splitRegister(dst)
	if !ok {
		return false
	}
	if dst == regX {
		g.emit("MOV X Y")
		g.emit("LOD " + low + " Y")
		g.emit("ADD %1 Y")
		g.emit("LOD " + high + " Y")
		return true
	}
	g.emit("LOD " + low + " X")
	g.emit("ADD %1 X")
	g.emit("LOD " + high + " X")
	return true
}

// emitLoadImmediate materialises a full 32-bit constant. The assembler encodes
// immediate operands as 16-bit words, so wide values are written through the
// high/low sub-register aliases.
func (g *gen) emitLoadImmediate(dst string, value uint32) {
	if value <= 0xFFFF {
		g.emit(fmt.Sprintf("MOV %%%d %s", value, dst))
		return
	}

	high, low, ok := splitRegister(dst)
	if !ok {
		g.emit(fmt.Sprintf("MOV %%%d %s", value&0xFFFF, dst))
		return
	}
	g.emit(fmt.Sprintf("MOV %%%d %s", (value>>16)&0xFFFF, high))
	g.emit(fmt.Sprintf("MOV %%%d %s", value&0xFFFF, low))
}

func (g *gen) maskResult(dst string, t Type) {
	if t.Ptr != 0 {
		return
	}
	if t.Base == TypeU8 || t.Base == TypeU16 {
		g.emit("AND %65535 " + dst)
	}
}

// exprType is a lightweight code-generation-time type probe. It is not a
// replacement for semantic analysis; it only supplies the width information
// needed to mask 16-bit arithmetic and to lay out 32-bit values.
func (g *gen) exprType(e Expr) Type {
	switch n := e.(type) {
	case *IntLit:
		switch {
		case n.Value > 0xFFFF:
			return Type{Base: TypeU32}
		case n.Value > 0xFF:
			return Type{Base: TypeU16}
		default:
			return Type{Base: TypeU8}
		}
	case *CharLit:
		return Type{Base: TypeU8}
	case *StringLit:
		return Type{Base: TypeU8, Ptr: 1}
	case *IdentExpr:
		if slot, ok := g.lookup(n.Name); ok {
			return slot.typ
		}
	case *IndexExpr:
		if slot, ok := g.lookup(n.Name); ok {
			return slot.typ
		}
	case *UnaryExpr:
		switch n.Op {
		case "&":
			t := g.exprType(n.X)
			return Type{Base: t.Base, Ptr: t.Ptr + 1}
		case "*":
			t := g.exprType(n.X)
			if t.Ptr > 0 {
				return Type{Base: t.Base, Ptr: t.Ptr - 1}
			}
			return Type{Base: TypeU16}
		case "~":
			return g.exprType(n.X)
		case "-":
			t := g.exprType(n.X)
			if t.Ptr == 0 && t.Base == TypeU32 {
				return Type{Base: TypeU32}
			}
			return Type{Base: TypeU16}
		}
	case *BinaryExpr:
		switch n.Op {
		case "&&", "||", "==", "!=", "<", ">", "<=", ">=":
			return Type{Base: TypeU8}
		}
		lt := g.exprType(n.Left)
		rt := g.exprType(n.Right)
		if lt.Ptr > 0 && rt.Ptr == 0 {
			return lt
		}
		if rt.Ptr > 0 && lt.Ptr == 0 {
			return rt
		}
		if lt.Ptr > 0 || rt.Ptr > 0 {
			return Type{Base: TypeU16}
		}
		if lt.Base == TypeU32 || rt.Base == TypeU32 {
			return Type{Base: TypeU32}
		}
		if lt.Base == TypeU16 || rt.Base == TypeU16 {
			return Type{Base: TypeU16}
		}
		return Type{Base: TypeU8}
	case *CallExpr:
		switch n.Callee {
		case "peek", "cyc", "in_port", "am", "em":
			return Type{Base: TypeU16}
		case "in_port32", "get_memory_size":
			return Type{Base: TypeU32}
		case "poke", "wait", "vblank", "out_port", "out_port32", "os_return", "os_start", "set_am", "set_em":
			return Type{Base: TypeVoid}
		}
		if fn, ok := g.funcs[n.Callee]; ok {
			return fn.Ret
		}
	}
	return Type{Base: TypeInvalid}
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

func (g *gen) emitStoreToStorage(slot storage, value Expr) error {
	if slot.reg != "" {
		if err := g.emitExprInto(value, slot.reg); err != nil {
			return err
		}
		if slot.typ.Base == TypeU8 && slot.typ.Ptr == 0 {
			g.emit("AND %255 " + slot.reg)
		}
		return nil
	}
	if err := g.emitExprInto(value, regX); err != nil {
		return err
	}
	g.storeToStorage(slot)
	return nil
}

func (g *gen) storeToStorage(slot storage) {
	if slot.isArray {
		panic("internal error: storeToStorage called on array")
	}
	if slot.reg != "" {
		panic("internal error: storeToStorage called on register slot")
	}
	if slot.typ.Base == TypeU8 && slot.typ.Ptr == 0 {
		g.emit("AND %255 X")
	}
	if slot.typ.Ptr == 0 && slot.typ.Base == TypeU32 {
		if g.storeU32ToStorageAddress(slot, regX) {
			return
		}
	}
	if g.widePointers && slot.typ.Ptr > 0 {
		if g.storeU32ToStorageAddress(slot, regX) {
			return
		}
	}
	if slot.kind == storageGlobal {
		g.emit("STR X " + slot.operand)
		return
	}

	if tmp := g.allocTemp(); tmp != "" {
		g.emit("MOV X " + tmp)
		g.loadFrameAddress(slot.offset)
		g.emit("STR " + tmp + " X")
		g.releaseTemp(tmp)
		return
	}

	g.emit("PUSH X")
	g.loadFrameAddress(slot.offset)
	g.emit("MOV X Y")
	g.emit("POP X")
	g.emit("STR X Y")
}

func (g *gen) loadFromStorage(slot storage) {
	g.loadFromStorageInto(slot, regX)
}

func (g *gen) loadFromStorageInto(slot storage, dst string) {
	if slot.isArray {
		panic("internal error: loadFromStorage called on array")
	}
	if slot.reg != "" {
		if slot.reg != dst {
			g.emit("MOV " + slot.reg + " " + dst)
		}
		return
	}
	if slot.typ.Ptr == 0 && slot.typ.Base == TypeU32 {
		if g.loadU32FromStorageInto(slot, dst) {
			return
		}
		if dst != regX && g.loadU32FromStorageInto(slot, regX) {
			g.emit("MOV X " + dst)
			return
		}
	}
	if g.widePointers && slot.typ.Ptr > 0 {
		if g.loadU32FromStorageInto(slot, dst) {
			return
		}
		if dst != regX && g.loadU32FromStorageInto(slot, regX) {
			g.emit("MOV X " + dst)
			return
		}
	}
	if slot.kind == storageGlobal {
		g.emit("LOD " + dst + " " + slot.operand)
		return
	}
	g.loadFrameAddress(slot.offset)
	g.emit("LOD " + dst + " X")
}

func (g *gen) emitAddressOf(e Expr) error {
	return g.emitAddressOfInto(e, regX)
}

// emitAddressOfInto computes the address of an lvalue and leaves it in dst.
// Variables cached in registers are excluded from caching when their address
// is taken, so this always refers to real memory-backed storage.
func (g *gen) emitAddressOfInto(e Expr, dst string) error {
	switch n := e.(type) {
	case *IdentExpr:
		slot, ok := g.lookup(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		if slot.isArray {
			return fmt.Errorf("cannot take address of array %q", n.Name)
		}
		if slot.reg != "" {
			return fmt.Errorf("internal error: address taken for register-cached variable %q", n.Name)
		}
		if slot.kind == storageGlobal {
			g.emit("MOV " + slot.operand + " " + dst)
		} else {
			g.loadFrameAddressInto(slot.offset, dst)
		}
	case *IndexExpr:
		slot, ok := g.lookup(n.Name)
		if !ok {
			return fmt.Errorf("unknown variable %q", n.Name)
		}
		if !slot.isArray {
			return fmt.Errorf("%q is not an array", n.Name)
		}
		if err := g.emitArrayAddress(slot, n.Index); err != nil {
			return err
		}
		if dst != regX {
			g.emit("MOV X " + dst)
		}
	default:
		return fmt.Errorf("cannot take address of expression")
	}
	return nil
}

func (g *gen) emitDerefStore(ptrExpr Expr, valueExpr Expr) error {
	ptrType := g.exprType(ptrExpr)
	isU32 := ptrType.Ptr == 1 && ptrType.Base == TypeU32

	// Evaluate and store the value first (source order).
	if tmp := g.allocTemp(); tmp != "" {
		defer g.releaseTemp(tmp)
		if err := g.emitExprInto(valueExpr, tmp); err != nil {
			return err
		}
		if err := g.emitExprInto(ptrExpr, regX); err != nil {
			return err
		}
		if isU32 {
			if high, low, ok := splitRegister(tmp); ok {
				g.emit("STR " + low + " X")
				g.emit("ADD %1 X")
				g.emit("STR " + high + " X")
				return nil
			}
		}
		g.emit("STR " + tmp + " X")
		return nil
	}

	if err := g.emitExprInto(valueExpr, regX); err != nil {
		return err
	}
	g.emit("PUSH X")
	if err := g.emitExprInto(ptrExpr, regX); err != nil {
		return err
	}
	g.emit("MOV X Y")
	g.emit("POP X")
	if isU32 {
		g.emit("STR XL Y")
		g.emit("ADD %1 Y")
		g.emit("STR XH Y")
		return nil
	}
	g.emit("STR X Y")
	return nil
}

func (g *gen) emitArrayStore(slot storage, index Expr, value Expr) error {
	isPackedByte := slot.typ.Base == TypeU8 && slot.typ.Ptr == 0
	isU32 := slot.typ.Ptr == 0 && slot.typ.Base == TypeU32

	if tmp := g.allocTemp(); tmp != "" {
		defer g.releaseTemp(tmp)
		if err := g.emitExprInto(value, tmp); err != nil {
			return err
		}
		if isPackedByte {
			g.emit("AND %255 " + tmp)
		}
		if err := g.emitArrayAddress(slot, index); err != nil {
			return err
		}
		if isU32 {
			if high, low, ok := splitRegister(tmp); ok {
				g.emit("STR " + low + " X")
				g.emit("ADD %1 X")
				g.emit("STR " + high + " X")
				return nil
			}
		}
		if !isPackedByte {
			g.emit("STR " + tmp + " X")
			return nil
		}

		// u8 arrays are packed: X is the word address, Y is the byte parity.
		g.emit("PUSH X")
		g.emit("LOD X X")
		evenLabel := g.label("arr_u8_even_store")
		endLabel := g.label("arr_u8_store_end")
		g.emit("CMP Y %0")
		g.emit("JEQ " + evenLabel)

		// odd index: update the high byte and preserve the low byte.
		g.emit("AND %255 X")
		g.emit("SHL %8 " + tmp)
		g.emit("OR " + tmp + " X")
		g.emit("JMP " + endLabel)

		// even index: update the low byte and preserve the high byte.
		g.emit(":" + evenLabel)
		g.emit("AND %65280 X")
		g.emit("OR " + tmp + " X")

		g.emit(":" + endLabel)
		g.emit("POP Y")
		g.emit("STR X Y")
		return nil
	}

	// No free register: use the old memory-temp path.
	if isU32 {
		if err := g.emitExpr(value); err != nil {
			return err
		}
		g.emit("PUSH X")
		if err := g.emitArrayAddress(slot, index); err != nil {
			return err
		}
		g.emit("POP Y")
		g.emit("STR YL X")
		g.emit("ADD %1 X")
		g.emit("STR YH X")
		return nil
	}
	if err := g.emitExpr(value); err != nil {
		return err
	}
	if isPackedByte {
		g.emit("AND %255 X")
	}
	g.storeToGlobal(g.arrayTemp0)

	if err := g.emitArrayAddress(slot, index); err != nil {
		return err
	}

	if !isPackedByte {
		g.emit("MOV X Y")
		g.loadFromGlobal(g.arrayTemp0)
		g.emit("STR X Y")
		return nil
	}

	g.storeToGlobal(g.arrayTemp1)

	g.emit("MOV Y X")
	g.emit("MOV %0 Y")
	g.emit("CMP")
	evenLabel := g.label("arr_u8_even_store")
	endLabel := g.label("arr_u8_store_end")
	g.emit("JEQ " + evenLabel)

	g.loadFromGlobal(g.arrayTemp1)
	g.emit("LOD X X")
	g.emit("AND %255 X")
	g.emit("MOV X Y")
	g.loadFromGlobal(g.arrayTemp0)
	g.emit("SHL %8 X")
	g.emit("OR Y X")
	g.emit("MOV X Y")
	g.loadFromGlobal(g.arrayTemp1)
	g.emit("STR Y X")
	g.emit("MOV %0 X")
	g.emit("JMP " + endLabel)

	g.emit(":" + evenLabel)
	g.loadFromGlobal(g.arrayTemp1)
	g.emit("LOD X X")
	g.emit("AND %65280 X")
	g.emit("MOV X Y")
	g.loadFromGlobal(g.arrayTemp0)
	g.emit("OR Y X")
	g.emit("MOV X Y")
	g.loadFromGlobal(g.arrayTemp1)
	g.emit("STR Y X")
	g.emit("MOV %0 X")

	g.emit(":" + endLabel)
	return nil
}

func (g *gen) emitArrayLoad(slot storage, index Expr) error {
	return g.emitArrayLoadInto(slot, index, regX)
}

func (g *gen) emitArrayLoadInto(slot storage, index Expr, dst string) error {
	if err := g.emitArrayAddress(slot, index); err != nil {
		return err
	}
	if slot.typ.Ptr == 0 && slot.typ.Base == TypeU32 {
		if g.loadU32FromAddressInX(dst) {
			return nil
		}
	}
	if !(slot.typ.Base == TypeU8 && slot.typ.Ptr == 0) {
		g.emit("LOD " + dst + " X")
		return nil
	}

	if !g.extendedMode {
		if dst != regX {
			return fmt.Errorf("internal error: legacy array load destination must be X, got %s", dst)
		}
		// Legacy path: stash the word address, compare Y (byte parity) with 0,
		// then reload the word through the saved address.
		g.storeToGlobal(g.arrayTemp1)
		g.emit("MOV Y X")
		g.emit("MOV %0 Y")
		g.emit("CMP")
		evenLabel := g.label("arr_u8_even_load")
		endLabel := g.label("arr_u8_load_end")
		g.emit("JEQ " + evenLabel)
		g.loadFromGlobal(g.arrayTemp1)
		g.emit("LOD X X")
		g.emit("SHR %8 X")
		g.emit("AND %255 X")
		g.emit("JMP " + endLabel)
		g.emit(":" + evenLabel)
		g.loadFromGlobal(g.arrayTemp1)
		g.emit("LOD X X")
		g.emit("AND %255 X")
		g.emit(":" + endLabel)
		return nil
	}

	// u8 arrays are packed: X is the word address, Y is the byte parity.
	g.emit("LOD " + dst + " X")
	evenLabel := g.label("arr_u8_even_load")
	endLabel := g.label("arr_u8_load_end")
	g.emit("CMP Y %0")
	g.emit("JEQ " + evenLabel)
	g.emit("SHR %8 " + dst)
	g.emit("AND %255 " + dst)
	g.emit("JMP " + endLabel)
	g.emit(":" + evenLabel)
	g.emit("AND %255 " + dst)
	g.emit(":" + endLabel)
	return nil
}

func (g *gen) emitArrayAddress(slot storage, index Expr) error {
	isPackedByte := slot.typ.Base == TypeU8 && slot.typ.Ptr == 0
	isU32 := slot.typ.Ptr == 0 && slot.typ.Base == TypeU32

	if tmp := g.allocTemp(); tmp != "" {
		defer g.releaseTemp(tmp)
		if err := g.emitExprInto(index, tmp); err != nil {
			return err
		}
		if isPackedByte {
			g.emit("MOV " + tmp + " Y")
			g.emit("AND %1 Y")
			g.emit("SHR %1 " + tmp)
		} else if isU32 {
			// u32 elements occupy two 16-bit words.
			g.emit("SHL %1 " + tmp)
		}
		g.loadArrayBaseAddress(slot)
		g.emit("ADD " + tmp + " X")
		return nil
	}

	if err := g.emitExpr(index); err != nil {
		return err
	}
	if isPackedByte {
		g.emit("PUSH X")
		g.emit("SHR %1 X")
		g.emit("PUSH X")
		g.loadArrayBaseAddress(slot)
		g.emit("MOV X Y")
		g.emit("POP X")
		g.emit("ADD Y X")
		g.emit("POP Y")
		g.emit("AND %1 Y")
		return nil
	}

	if isU32 {
		g.emit("SHL %1 X")
	}
	g.emit("PUSH X")
	g.loadArrayBaseAddress(slot)
	g.emit("MOV X Y")
	g.emit("POP X")
	g.emit("ADD Y X")
	return nil
}

func (g *gen) loadArrayBaseAddress(slot storage) {
	if slot.kind == storageGlobal {
		g.emit("MOV " + slot.operand + " X")
		return
	}
	g.loadFrameAddress(slot.offset)
}

func (g *gen) loadFrameAddress(offset int) {
	g.loadFromGlobal(g.frameBaseSlot)
	if offset > 0 {
		g.emit(fmt.Sprintf("ADD %%%d X", offset))
	}
}

func (g *gen) loadFrameAddressInto(offset int, dst string) {
	if dst == regX {
		g.loadFrameAddress(offset)
		return
	}
	g.loadFromGlobalInto(g.frameBaseSlot, dst)
	if offset > 0 {
		g.emit(fmt.Sprintf("ADD %%%d %s", offset, dst))
	}
}

func (g *gen) loadFromGlobal(slot storage) {
	g.loadFromGlobalInto(slot, regX)
}

func (g *gen) loadFromGlobalInto(slot storage, dst string) {
	g.emit("LOD " + dst + " " + slot.operand)
}

func (g *gen) storeToGlobal(slot storage) {
	if slot.typ.Base == TypeU8 && slot.typ.Ptr == 0 {
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

// collectAddressTakenStmtList reports which variables in a function have
// their address taken. Those variables must remain memory-backed so that
// writes through the resulting pointer are observable.
func collectAddressTakenStmtList(stmts []Stmt) map[string]bool {
	taken := map[string]bool{}
	for _, st := range stmts {
		collectAddressTakenStmt(st, taken)
	}
	return taken
}

func collectAddressTakenStmt(st Stmt, taken map[string]bool) {
	switch n := st.(type) {
	case *VarDeclStmt:
		collectAddressTakenExpr(n.Decl.Init, taken)
	case *AssignStmt:
		collectAddressTakenExpr(n.LHS, taken)
		collectAddressTakenExpr(n.Value, taken)
	case *AssignIndexStmt:
		collectAddressTakenExpr(n.Index, taken)
		collectAddressTakenExpr(n.Value, taken)
	case *ExprStmt:
		collectAddressTakenExpr(n.Value, taken)
	case *ReturnStmt:
		collectAddressTakenExpr(n.Value, taken)
	case *IfStmt:
		collectAddressTakenExpr(n.Cond, taken)
		for _, s := range n.Then {
			collectAddressTakenStmt(s, taken)
		}
		for _, s := range n.Else {
			collectAddressTakenStmt(s, taken)
		}
	case *WhileStmt:
		collectAddressTakenExpr(n.Cond, taken)
		for _, s := range n.Body {
			collectAddressTakenStmt(s, taken)
		}
	}
}

func collectAddressTakenExpr(e Expr, taken map[string]bool) {
	switch n := e.(type) {
	case *UnaryExpr:
		if n.Op == "&" {
			if id, ok := n.X.(*IdentExpr); ok {
				taken[id.Name] = true
			}
		}
		collectAddressTakenExpr(n.X, taken)
	case *BinaryExpr:
		collectAddressTakenExpr(n.Left, taken)
		collectAddressTakenExpr(n.Right, taken)
	case *IndexExpr:
		collectAddressTakenExpr(n.Index, taken)
	case *CallExpr:
		for _, a := range n.Args {
			collectAddressTakenExpr(a, taken)
		}
	}
}

func (g *gen) countLocalsInStmtList(stmts []Stmt) int {
	total := 0
	for _, st := range stmts {
		total += g.countLocalsInStmt(st)
	}
	return total
}

func (g *gen) countLocalsInStmt(st Stmt) int {
	switch n := st.(type) {
	case *VarDeclStmt:
		if n.Decl.IsArray {
			return arrayWordCount(n.Decl.Type.Base, n.Decl.ArrayLen)
		}
		return g.wordCount(n.Decl.Type)
	case *IfStmt:
		return g.countLocalsInStmtList(n.Then) + g.countLocalsInStmtList(n.Else)
	case *WhileStmt:
		return g.countLocalsInStmtList(n.Body)
	default:
		return 0
	}
}
