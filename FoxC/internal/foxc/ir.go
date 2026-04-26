package foxc

import (
	"fmt"
	"strconv"
	"strings"
)

type irInst struct {
	label string
	op    string
	args  []string
}

type cmpResult int

const (
	cmpUnknown cmpResult = iota
	cmpLess
	cmpEqual
	cmpGreater
)

type knownValue struct {
	ok  bool
	val uint16
}

func parseIRLine(line string) irInst {
	line = strings.TrimSpace(line)
	if line == "" {
		return irInst{}
	}
	if strings.HasPrefix(line, ":") {
		return irInst{label: strings.TrimPrefix(line, ":")}
	}
	fields := strings.Fields(line)
	if len(fields) == 0 {
		return irInst{}
	}
	inst := irInst{op: fields[0]}
	if len(fields) > 1 {
		inst.args = append(inst.args, fields[1:]...)
	}
	return inst
}

func renderIR(insts []irInst) string {
	var b strings.Builder
	for _, inst := range insts {
		if inst.label != "" {
			b.WriteString(":" + inst.label + "\n")
			continue
		}
		if inst.op == "" {
			continue
		}
		b.WriteString(inst.op)
		if len(inst.args) > 0 {
			b.WriteByte(' ')
			b.WriteString(strings.Join(inst.args, " "))
		}
		b.WriteByte('\n')
	}
	return b.String()
}

func optimizeIR(in []irInst) []irInst {
	insts := dropEmptyIR(in)
	insts = foldConstantBranches(insts)
	insts = copyPropIR(insts)
	insts = eliminateRedundantPushPop(insts)
	insts = peepholeIR(insts)
	insts = eliminateRedundantStores(insts)
	insts = removeUnreachable(insts)
	insts = removeTrivialJumps(insts)
	insts = removeDeadLabels(insts)
	return insts
}

func copyPropIR(in []irInst) []irInst {
	out := make([]irInst, 0, len(in))
	for i := 0; i < len(in); i++ {
		inst := in[i]
		if inst.label != "" || inst.op != "MOV" || len(inst.args) != 2 || i+1 >= len(in) {
			out = append(out, inst)
			continue
		}
		next := in[i+1]
		if next.label == "" && next.op == "MOV" && len(next.args) == 2 && next.args[0] == inst.args[1] {
			out = append(out, inst)
			out = append(out, irInst{op: "MOV", args: []string{inst.args[0], next.args[1]}})
			i++
			continue
		}
		out = append(out, inst)
	}
	return out
}

func eliminateRedundantPushPop(in []irInst) []irInst {
	out := make([]irInst, 0, len(in))
	for i := 0; i < len(in); i++ {
		inst := in[i]
		if inst.label == "" && inst.op == "PUSH" && len(inst.args) == 1 && i+1 < len(in) {
			next := in[i+1]
			if next.label == "" && next.op == "POP" && len(next.args) == 1 {
				if inst.args[0] != next.args[0] {
					out = append(out, irInst{op: "MOV", args: []string{inst.args[0], next.args[0]}})
				}
				i++
				continue
			}
		}
		out = append(out, inst)
	}
	return out
}

func eliminateRedundantStores(in []irInst) []irInst {
	out := make([]irInst, 0, len(in))
	for i := 0; i < len(in); i++ {
		inst := in[i]
		if inst.label == "" && inst.op == "STR" && len(inst.args) == 2 && isDirectAddr(inst.args[1]) && i+1 < len(in) {
			next := in[i+1]
			if next.label == "" && next.op == "STR" && len(next.args) == 2 && next.args[1] == inst.args[1] {
				continue
			}
		}
		out = append(out, inst)
	}
	return out
}

func isDirectAddr(arg string) bool {
	return strings.HasPrefix(arg, "<") && strings.HasSuffix(arg, ">")
}

func dropEmptyIR(in []irInst) []irInst {
	out := make([]irInst, 0, len(in))
	for _, inst := range in {
		if inst.label == "" && inst.op == "" {
			continue
		}
		out = append(out, inst)
	}
	return out
}

func foldConstantBranches(in []irInst) []irInst {
	reg := map[string]knownValue{
		"X": {},
		"Y": {},
	}

	setReg := func(name string, v knownValue) {
		if _, ok := reg[name]; ok {
			reg[name] = v
		}
	}

	getReg := func(name string) knownValue {
		v, ok := reg[name]
		if !ok {
			return knownValue{}
		}
		return v
	}

	cmp := cmpUnknown
	out := make([]irInst, 0, len(in))
	for _, inst := range in {
		if inst.label != "" {
			reg["X"] = knownValue{}
			reg["Y"] = knownValue{}
			cmp = cmpUnknown
			out = append(out, inst)
			continue
		}

		switch inst.op {
		case "MOV":
			if len(inst.args) == 2 {
				if imm, ok := parseImm(inst.args[0]); ok {
					setReg(inst.args[1], knownValue{ok: true, val: imm})
				} else {
					src := getReg(inst.args[0])
					if src.ok {
						setReg(inst.args[1], src)
					} else {
						setReg(inst.args[1], knownValue{})
					}
				}
			}
			cmp = cmpUnknown
		case "ADD", "SUB", "MUL", "DIV", "AND":
			if len(inst.args) == 2 {
				dst := inst.args[1]
				dv := getReg(dst)
				srcv, srcKnown := operandValue(inst.args[0], reg)
				if dv.ok && srcKnown {
					res, ok := evalALU(inst.op, dv.val, srcv)
					if ok {
						setReg(dst, knownValue{ok: true, val: res})
					} else {
						setReg(dst, knownValue{})
					}
				} else {
					setReg(dst, knownValue{})
				}
			}
			cmp = cmpUnknown
		case "LOD":
			if len(inst.args) > 0 {
				setReg(inst.args[0], knownValue{})
			}
			cmp = cmpUnknown
		case "POP":
			if len(inst.args) > 0 {
				setReg(inst.args[0], knownValue{})
			}
			cmp = cmpUnknown
		case "CMP":
			x := getReg("X")
			y := getReg("Y")
			if x.ok && y.ok {
				switch {
				case x.val < y.val:
					cmp = cmpLess
				case x.val > y.val:
					cmp = cmpGreater
				default:
					cmp = cmpEqual
				}
			} else {
				cmp = cmpUnknown
			}
		case "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE":
			if cmp != cmpUnknown && len(inst.args) == 1 {
				take := branchTaken(inst.op, cmp)
				if take {
					out = append(out, irInst{op: "JMP", args: []string{inst.args[0]}})
				}
				reg["X"] = knownValue{}
				reg["Y"] = knownValue{}
				cmp = cmpUnknown
				continue
			}
			reg["X"] = knownValue{}
			reg["Y"] = knownValue{}
			cmp = cmpUnknown
		case "JMP", "HLT":
			reg["X"] = knownValue{}
			reg["Y"] = knownValue{}
			cmp = cmpUnknown
		default:
			cmp = cmpUnknown
		}

		out = append(out, inst)
	}
	return out
}

func operandValue(op string, reg map[string]knownValue) (uint16, bool) {
	if imm, ok := parseImm(op); ok {
		return imm, true
	}
	if v, ok := reg[op]; ok && v.ok {
		return v.val, true
	}
	return 0, false
}

func evalALU(op string, dst, src uint16) (uint16, bool) {
	switch op {
	case "ADD":
		return dst + src, true
	case "SUB":
		return dst - src, true
	case "MUL":
		return dst * src, true
	case "DIV":
		if src == 0 {
			return 0, false
		}
		return dst / src, true
	case "AND":
		return dst & src, true
	default:
		return 0, false
	}
}

func branchTaken(op string, cmp cmpResult) bool {
	switch op {
	case "JEQ":
		return cmp == cmpEqual
	case "JNE":
		return cmp != cmpEqual
	case "JLT":
		return cmp == cmpLess
	case "JGT":
		return cmp == cmpGreater
	case "JLE":
		return cmp == cmpLess || cmp == cmpEqual
	case "JGE":
		return cmp == cmpEqual || cmp == cmpGreater
	default:
		return false
	}
}

func peepholeIR(in []irInst) []irInst {
	out := make([]irInst, 0, len(in))
	for _, inst := range in {
		if inst.label != "" {
			out = append(out, inst)
			continue
		}
		if inst.op == "MOV" && len(inst.args) == 2 && inst.args[0] == inst.args[1] {
			continue
		}
		if (inst.op == "ADD" || inst.op == "SUB") && len(inst.args) == 2 {
			if imm, ok := parseImm(inst.args[0]); ok && imm == 0 {
				continue
			}
		}
		if (inst.op == "MUL" || inst.op == "DIV") && len(inst.args) == 2 {
			if imm, ok := parseImm(inst.args[0]); ok && imm == 1 {
				continue
			}
		}
		if inst.op == "AND" && len(inst.args) == 2 {
			if imm, ok := parseImm(inst.args[0]); ok && imm == 0xFFFF {
				continue
			}
		}
		out = append(out, inst)
	}
	return out
}

func removeUnreachable(in []irInst) []irInst {
	out := make([]irInst, 0, len(in))
	dead := false
	for _, inst := range in {
		if inst.label != "" {
			dead = false
			out = append(out, inst)
			continue
		}
		if dead {
			continue
		}
		out = append(out, inst)
		if inst.op == "JMP" || inst.op == "HLT" {
			dead = true
		}
	}
	return out
}

func removeTrivialJumps(in []irInst) []irInst {
	out := make([]irInst, 0, len(in))
	for i := 0; i < len(in); i++ {
		inst := in[i]
		if inst.label != "" {
			out = append(out, inst)
			continue
		}
		if inst.op == "JMP" && len(inst.args) == 1 {
			next := nextNonEmpty(in, i+1)
			if next >= 0 && in[next].label == inst.args[0] {
				continue
			}
		}
		out = append(out, inst)
	}
	return out
}

func removeDeadLabels(in []irInst) []irInst {
	refs := map[string]int{}
	for _, inst := range in {
		if inst.label != "" {
			continue
		}
		if len(inst.args) == 1 && isBranch(inst.op) {
			refs[inst.args[0]]++
		}
	}

	out := make([]irInst, 0, len(in))
	for _, inst := range in {
		if inst.label != "" {
			if refs[inst.label] == 0 && inst.label != "main" {
				continue
			}
		}
		out = append(out, inst)
	}
	return out
}

func nextNonEmpty(in []irInst, start int) int {
	for i := start; i < len(in); i++ {
		if in[i].label != "" || in[i].op != "" {
			return i
		}
	}
	return -1
}

func isBranch(op string) bool {
	switch op {
	case "JMP", "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE":
		return true
	default:
		return false
	}
}

func parseImm(s string) (uint16, bool) {
	if !strings.HasPrefix(s, "%") {
		return 0, false
	}
	n, err := strconv.ParseUint(strings.TrimPrefix(s, "%"), 10, 16)
	if err != nil {
		return 0, false
	}
	return uint16(n), true
}

func (i irInst) String() string {
	if i.label != "" {
		return ":" + i.label
	}
	if len(i.args) == 0 {
		return i.op
	}
	return fmt.Sprintf("%s %s", i.op, strings.Join(i.args, " "))
}
