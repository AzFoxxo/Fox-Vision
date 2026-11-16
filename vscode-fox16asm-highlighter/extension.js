const vscode = require("vscode");

// ─── ISA V1 (legacy) + V1.10 (extended) + V2.0 (em2_am1) opcodes ───────────

const OPCODES = [
    // Legacy
    "NOP", "LFM", "WTM", "SRA", "AXY", "SXY", "MXY", "DXY", "EQU", "LEQ",
    "JPZ", "JNZ", "CLR", "BSL", "BSR", "ORA", "XOR", "DWR", "ILM", "IWR",
    // Modern / MOI multi-operand
    "JMP", "HLT", "INC", "DEC",
    "MOV", "STR", "LOD", "CMP",
    "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE",
    "ADD", "SUB", "MUL", "DIV", "SHL", "SHR", "AND", "OR",
    "PUSH", "POP",
    "WAIT", "VBLANK", "VBL",
    "IN", "OUT",
    "OSRET",
    "DBG_LGC", "DBG_MEM", "DBG_INP"
];

const OPCODE_DOCS = {
    NOP:     "No operation. Advances PC by one instruction word.",
    LFM:     "Legacy: Load from memory at the address in the active register.",
    WTM:     "Legacy: Write to memory at the address in the active register.",
    SRA:     "Legacy: Set register A / load immediate into the active register.",
    AXY:     "Legacy: Add X and Y, result in X. (MOI form: ADD SRC DST)",
    SXY:     "Legacy: Subtract Y from X, result in X. (MOI form: SUB SRC DST)",
    MXY:     "Legacy: Multiply X and Y, result in X. (MOI form: MUL SRC DST)",
    DXY:     "Legacy: Divide X by Y, result in X. (MOI form: DIV SRC DST)",
    EQU:     "Legacy: Set equality status flag based on X == Y.",
    LEQ:     "Legacy: Set less-than/equal status flag based on X <= Y.",
    JPZ:     "Legacy: Jump if zero flag is set.",
    JNZ:     "Legacy: Jump if zero flag is not set.",
    CLR:     "Legacy: Clear registers/state.",
    BSL:     "Legacy/MOI: Bit shift left (MOI form: SHL SRC DST).",
    BSR:     "Legacy/MOI: Bit shift right (MOI form: SHR SRC DST).",
    ORA:     "Legacy: Bitwise OR of X and Y (alias for OR).",
    XOR:     "Legacy: Bitwise XOR (MOI form: XOR SRC DST).",
    DWR:     "Legacy: Direct write to register.",
    ILM:     "Legacy: Indirect load from memory.",
    IWR:     "Legacy: Indirect write to memory.",
    JMP:     "Unconditional jump to a label.",
    HLT:     "Halt the machine.",
    INC:     "Increment a register. MOI form: INC REG",
    DEC:     "Decrement a register. MOI form: DEC REG",
    MOV:     "Move/load a value. MOI form: MOV SRC DST\nExamples:\n  MOV %42 X        ; immediate → register\n  MOV X Y          ; register → register\n  MOV %2 EM        ; set EM=2 (V2.0 32-bit mode)\n  MOV %1 AM        ; set AM=1 (byte addressing)",
    STR:     "Store a register to memory. MOI form: STR REG MEM\nExample:\n  STR X <label>",
    LOD:     "Load from memory into a register. MOI form: LOD MEM REG\nExample:\n  LOD <label> X",
    CMP:     "Compare. Zero-operand form compares X vs Y.\nMOI two-operand form: CMP SRC DST — compares SRC with DST.\nIn V2.0 mode with 32-bit registers, performs a 32-bit compare.",
    JEQ:     "Jump if equal (after CMP).",
    JNE:     "Jump if not equal (after CMP).",
    JLT:     "Jump if less-than (after CMP).",
    JGT:     "Jump if greater-than (after CMP).",
    JLE:     "Jump if less-than or equal (after CMP).",
    JGE:     "Jump if greater-than or equal (after CMP).",
    ADD:     "Add. MOI form: ADD SRC DST  (DST += SRC)",
    SUB:     "Subtract. MOI form: SUB SRC DST  (DST -= SRC)",
    MUL:     "Multiply. MOI form: MUL SRC DST  (DST *= SRC)",
    DIV:     "Divide. MOI form: DIV SRC DST  (DST /= SRC)",
    SHL:     "Shift left. MOI form: SHL SRC DST  (DST <<= SRC)",
    SHR:     "Shift right. MOI form: SHR SRC DST  (DST >>= SRC)",
    AND:     "Bitwise AND. MOI form: AND SRC DST  (DST &= SRC)",
    OR:      "Bitwise OR. MOI form: OR SRC DST  (DST |= SRC)",
    PUSH:    "Push a register onto the hardware stack.",
    POP:     "Pop the hardware stack into a register.",
    WAIT:    "Wait for N cycles. MOI form: WAIT %cycles",
    VBLANK:  "Block until the next frame VBlank (display sync).",
    VBL:     "Alias for VBLANK.",
    IN:      "Read from a port into a register. MOI form: IN PORT0 X\nIn em2_am1 mode any PORT0–PORT65535 is valid.",
    OUT:     "Write a register to a port. MOI form: OUT X PORT0\nIn em2_am1 mode any PORT0–PORT65535 is valid.",
    OSRET:   "Return from a FoxOS foreground program to the resident OS.",
    DBG_LGC: "Debug extension: emit a log code.",
    DBG_MEM: "Debug extension: dump memory.",
    DBG_INP: "Debug extension: inject debug input.",
};

// ─── Registers ──────────────────────────────────────────────────────────────

// Core 16-bit registers (all modes)
const CORE_REGISTERS = ["X", "Y", "STATUS"];

// Extended registers available in extended / em2_am1 modes (EM != 0)
const EXTENDED_REGISTERS = [
    "PC", "SP", "CYC", "EM",
    "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7"
];

// V2.0 (em2_am1) sub-registers and AM
const V2_SUBREGISTERS = [
    "AM",
    "XL", "XH", "YL", "YH",
    "PCL", "PCH", "SPL", "SPH", "CYCL", "CYCH",
    "R0L", "R0H", "R1L", "R1H", "R2L", "R2H", "R3L", "R3H",
    "R4L", "R4H", "R5L", "R5H", "R6L", "R6H", "R7L", "R7H"
];

const REGISTER_DOCS = {
    X:      "General-purpose 16-bit (32-bit in V2) register. Primary ALU operand.",
    Y:      "General-purpose 16-bit (32-bit in V2) register. Secondary ALU operand.",
    STATUS: "Status/flags register. Reflects comparison results.",
    PC:     "Program Counter — current instruction address (extended mode, read-mostly).",
    SP:     "Stack Pointer — points to top of hardware stack (extended mode).",
    CYC:    "Cycle Counter — 32-bit free-running counter, read-only (extended mode).",
    EM:     "Extension Mode register.\n  0 = legacy\n  1 = extended 16-bit\n  2 = 32-bit address space (V2.0)",
    AM:     "Addressing Mode register (V2.0 em2_am1 only).\n  0 = word addressing (legacy)\n  1 = byte addressing",
    R0: "General-purpose 32-bit register (extended/V2 mode).",
    R1: "General-purpose 32-bit register (extended/V2 mode).",
    R2: "General-purpose 32-bit register (extended/V2 mode).",
    R3: "General-purpose 32-bit register (extended/V2 mode).",
    R4: "General-purpose 32-bit register (extended/V2 mode).",
    R5: "General-purpose 32-bit register (extended/V2 mode).",
    R6: "General-purpose 32-bit register (extended/V2 mode).",
    R7: "General-purpose 32-bit register (extended/V2 mode).",
};

// Build the full MACHINE_SYMBOLS set for validation/completions
const MACHINE_SYMBOLS = new Set([
    ...CORE_REGISTERS,
    ...EXTENDED_REGISTERS,
    ...V2_SUBREGISTERS
]);

const MACHINE_SYMBOL_LIST = [
    ...CORE_REGISTERS,
    ...EXTENDED_REGISTERS,
    ...V2_SUBREGISTERS,
    // Common ports for completions (full range 0–65535 is valid but we don't enumerate all)
    "PORT0", "PORT1", "PORT2", "PORT3", "PORT4", "PORT5", "PORT6", "PORT7"
];

const JUMP_OPCODES = new Set(["JPZ", "JNZ", "JMP", "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE"]);

// Expected operand counts (null = unknown/variadic)
const OPCODE_OPERANDS = {
    NOP: 0, LFM: 1, WTM: 1, SRA: 1,
    AXY: [0, 2], SXY: [0, 2], MXY: [0, 2], DXY: [0, 2],
    EQU: 0, LEQ: 0,
    JPZ: 1, JNZ: 1, JMP: 1,
    CLR: 0, HLT: 0,
    BSL: [0, 2], BSR: [0, 2],
    AND: [0, 2], ORA: [0, 2], OR: 2, XOR: [0, 2],
    DWR: 1, ILM: 0, IWR: 0, INC: 1, DEC: 1,
    MOV: 2, STR: 2, LOD: 2, CMP: [0, 2],
    JEQ: 1, JNE: 1, JLT: 1, JGT: 1, JLE: 1, JGE: 1,
    ADD: 2, SUB: 2, MUL: 2, DIV: 2, SHL: 2, SHR: 2,
    PUSH: 1, POP: 1,
    WAIT: 1, VBLANK: 0, VBL: 0,
    IN: 2, OUT: 2,
    OSRET: 0,
    DBG_LGC: 1, DBG_MEM: 0, DBG_INP: 0
};

// ─── Symbol collection ───────────────────────────────────────────────────────

function collectSymbols(document) {
    const labels = new Set();
    const constants = new Set();

    for (let i = 0; i < document.lineCount; i += 1) {
        const line = document.lineAt(i).text;

        const labelMatch = line.match(/^\s*:([A-Za-z_][A-Za-z0-9_]*)\b/);
        if (labelMatch) labels.add(labelMatch[1]);

        const constMatch = line.match(/^\s*@const\s+([A-Za-z_][A-Za-z0-9_]*)\b/);
        if (constMatch) constants.add(constMatch[1]);
    }

    return { labels: [...labels], constants: [...constants] };
}

// ─── Completion providers ────────────────────────────────────────────────────

function createOpcodeCompletions() {
    return OPCODES.map((opcode) => {
        const item = new vscode.CompletionItem(opcode, vscode.CompletionItemKind.Keyword);
        item.detail = "Fox16ASM opcode";
        item.documentation = new vscode.MarkdownString(OPCODE_DOCS[opcode] ?? "Fox16ASM instruction.");
        return item;
    });
}

function createRegisterCompletions() {
    const items = [];

    for (const reg of CORE_REGISTERS) {
        const item = new vscode.CompletionItem(reg, vscode.CompletionItemKind.Variable);
        item.detail = "Core register";
        item.documentation = new vscode.MarkdownString(REGISTER_DOCS[reg] ?? `Register ${reg}.`);
        items.push(item);
    }

    for (const reg of EXTENDED_REGISTERS) {
        const item = new vscode.CompletionItem(reg, vscode.CompletionItemKind.Variable);
        item.detail = "Extended register (EM≠0)";
        item.documentation = new vscode.MarkdownString(REGISTER_DOCS[reg] ?? `Extended register ${reg}. Requires extended or em2_am1 mode.`);
        items.push(item);
    }

    for (const reg of V2_SUBREGISTERS) {
        const item = new vscode.CompletionItem(reg, vscode.CompletionItemKind.Variable);
        item.detail = "V2.0 sub-register (em2_am1)";
        item.documentation = new vscode.MarkdownString(REGISTER_DOCS[reg] ?? `V2.0 32-bit sub-register ${reg}. Requires --mode em2_am1.`);
        items.push(item);
    }

    return items;
}

function createDirectiveCompletions() {
    const constItem = new vscode.CompletionItem("@const", vscode.CompletionItemKind.Snippet);
    constItem.detail = "Preprocessor constant";
    constItem.documentation = "Declare a named constant: @const NAME VALUE";
    constItem.insertText = new vscode.SnippetString("@const ${1:NAME} ${2:%0}");

    return [constItem];
}

function createLabelCompletions(labels) {
    return labels.map((label) => {
        const item = new vscode.CompletionItem(label, vscode.CompletionItemKind.Reference);
        item.detail = "Label";
        item.documentation = `Jump target: ${label}`;
        return item;
    });
}

function createConstantCompletions(constants) {
    return constants.map((name) => {
        const item = new vscode.CompletionItem(`<${name}>`, vscode.CompletionItemKind.Constant);
        item.detail = "Named constant";
        item.documentation = `Preprocessor constant: <${name}>`;
        item.insertText = `<${name}>`;
        return item;
    });
}

function createMachineSymbolCompletions() {
    return MACHINE_SYMBOL_LIST.map((symbol) => {
        const isPort = symbol.startsWith("PORT");
        const isV2 = V2_SUBREGISTERS.includes(symbol);
        const isExt = EXTENDED_REGISTERS.includes(symbol);
        const kind = isPort ? vscode.CompletionItemKind.EnumMember : vscode.CompletionItemKind.Variable;
        const item = new vscode.CompletionItem(symbol, kind);
        item.detail = isPort ? "Port operand" : isV2 ? "V2.0 sub-register" : isExt ? "Extended register" : "Core register";
        item.documentation = REGISTER_DOCS[symbol] ?? (isPort ? `Port operand. In em2_am1 mode any PORT0–PORT65535 is valid.` : symbol);
        return item;
    });
}

// ─── Context detection ───────────────────────────────────────────────────────

function isDirectiveContext(linePrefix) {
    return /^\s*@[^\s]*$/.test(linePrefix);
}

function isConstantContext(linePrefix) {
    return /<[^>]*$/.test(linePrefix);
}

function isLabelDefinitionContext(linePrefix) {
    return /^\s*:[A-Za-z0-9_]*$/.test(linePrefix);
}

function isJumpOperandContext(linePrefix) {
    const m = linePrefix.match(/\b([A-Z_0-9]+)\s+([A-Za-z_0-9]*)$/);
    return !!m && JUMP_OPCODES.has(m[1]);
}

// ─── Validation ──────────────────────────────────────────────────────────────

function makeLineRange(document, lineIndex) {
    const text = document.lineAt(lineIndex).text;
    return new vscode.Range(lineIndex, 0, lineIndex, Math.max(1, text.length));
}

function validateDocument(document, collection) {
    const diagnostics = [];
    const labels = new Map();
    const usedLabels = new Map();
    const constants = new Map();

    for (let i = 0; i < document.lineCount; i += 1) {
        let line = document.lineAt(i).text;
        line = line.replace(/;.*$/g, "").replace(/\/\/.*$/g, "").trim();
        if (!line) continue;

        // Label definition
        const labelMatch = line.match(/^:([A-Za-z_][A-Za-z0-9_]*)\b/);
        if (labelMatch) {
            const name = labelMatch[1];
            if (labels.has(name)) {
                diagnostics.push(new vscode.Diagnostic(makeLineRange(document, i), `duplicate label '${name}'`, vscode.DiagnosticSeverity.Error));
            } else {
                labels.set(name, i);
            }
            line = line.replace(/^:[A-Za-z_][A-Za-z0-9_]*\b\s*/, "");
            if (!line) continue;
        }

        // @const directive
        const constMatch = line.match(/^@const\s+([A-Za-z_][A-Za-z0-9_]*)\b(?:\s+(.*))?/i);
        if (constMatch) {
            const name = constMatch[1];
            if (constants.has(name)) {
                diagnostics.push(new vscode.Diagnostic(makeLineRange(document, i), `duplicate constant '${name}'`, vscode.DiagnosticSeverity.Error));
            } else {
                constants.set(name, constMatch[2] || "");
            }
            continue;
        }

        // Opcode + operands
        const opMatch = line.match(/^([A-Za-z_][A-Za-z0-9_]*)\b(.*)/);
        if (!opMatch) continue;
        const opcode = opMatch[1].toUpperCase();
        const rest = opMatch[2].trim();

        if (!OPCODES.includes(opcode)) {
            diagnostics.push(new vscode.Diagnostic(makeLineRange(document, i), `unknown opcode '${opcode}'`, vscode.DiagnosticSeverity.Error));
            continue;
        }

        // Operand count check
        const operands = rest.length === 0
            ? []
            : rest.replace(/,/g, " ").trim().split(/\s+/).filter(Boolean);
        const expected = Object.prototype.hasOwnProperty.call(OPCODE_OPERANDS, opcode)
            ? OPCODE_OPERANDS[opcode]
            : null;
        if (expected !== null) {
            if (Array.isArray(expected)) {
                if (!expected.includes(operands.length)) {
                    diagnostics.push(new vscode.Diagnostic(makeLineRange(document, i),
                        `'${opcode}' expects ${expected.join(" or ")} operand(s), got ${operands.length}`,
                        vscode.DiagnosticSeverity.Error));
                }
            } else if (expected !== operands.length) {
                diagnostics.push(new vscode.Diagnostic(makeLineRange(document, i),
                    `'${opcode}' expects ${expected} operand(s), got ${operands.length}`,
                    vscode.DiagnosticSeverity.Error));
            }
        }

        // Track label/constant references for undefined-symbol check
        for (const opd of operands) {
            const bare = opd.replace(/[<>[\]]/g, "");
            const upper = bare.toUpperCase();
            // PORT\d+ — valid in any form; skip undefined check
            if (/^PORT\d+$/i.test(bare)) continue;
            // Known machine symbol — skip
            if (MACHINE_SYMBOLS.has(upper)) continue;
            // Looks like an identifier — track as label/constant reference
            if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(bare)) {
                usedLabels.set(bare, (usedLabels.get(bare) || 0) + 1);
            }
        }
    }

    // Warn on undefined references
    for (const [name] of usedLabels) {
        if (!labels.has(name) && !constants.has(name)) {
            diagnostics.push(new vscode.Diagnostic(
                new vscode.Range(0, 0, 0, 1),
                `undefined label or constant '${name}'`,
                vscode.DiagnosticSeverity.Warning
            ));
        }
    }

    collection.set(document.uri, diagnostics);
}

// ─── Hover provider ──────────────────────────────────────────────────────────

function provideHover(document, position) {
    const wordRange = document.getWordRangeAtPosition(position, /[A-Za-z0-9_]+/);
    if (!wordRange) return null;
    const word = document.getText(wordRange).toUpperCase();

    if (OPCODE_DOCS[word]) {
        return new vscode.Hover(new vscode.MarkdownString(`**\`${word}\`** — ${OPCODE_DOCS[word]}`));
    }

    const regDoc = REGISTER_DOCS[word];
    if (regDoc) {
        const tag = V2_SUBREGISTERS.includes(word)
            ? "V2.0 sub-register"
            : EXTENDED_REGISTERS.includes(word)
                ? "extended register"
                : "register";
        return new vscode.Hover(new vscode.MarkdownString(`**\`${word}\`** *(${tag})* — ${regDoc}`));
    }

    if (/^PORT\d+$/.test(word)) {
        const portNum = parseInt(word.slice(4), 10);
        const rangeNote = portNum <= 7
            ? "Available in all extended modes."
            : "Only valid in `--mode em2_am1` (PORT0–PORT65535).";
        return new vscode.Hover(new vscode.MarkdownString(`**\`${word}\`** *(port operand)* — ${rangeNote}`));
    }

    return null;
}

// ─── Activation ──────────────────────────────────────────────────────────────

function activate(context) {
    const diagnostics = vscode.languages.createDiagnosticCollection("fox16asm");
    const timers = new Map();

    function scheduleValidation(document) {
        if (document.languageId !== "fox16asm") return;
        const key = document.uri.toString();
        const existing = timers.get(key);
        if (existing) clearTimeout(existing);
        const timer = setTimeout(() => validateDocument(document, diagnostics), 150);
        timers.set(key, timer);
    }

    const completionProvider = vscode.languages.registerCompletionItemProvider(
        { language: "fox16asm", scheme: "file" },
        {
            provideCompletionItems(document, position) {
                const linePrefix = document.lineAt(position).text.slice(0, position.character);
                const symbols = collectSymbols(document);

                if (isDirectiveContext(linePrefix)) return createDirectiveCompletions();
                if (isConstantContext(linePrefix)) return createConstantCompletions(symbols.constants);
                if (isLabelDefinitionContext(linePrefix) || isJumpOperandContext(linePrefix)) {
                    return createLabelCompletions(symbols.labels);
                }

                return [
                    ...createOpcodeCompletions(),
                    ...createRegisterCompletions(),
                    ...createMachineSymbolCompletions(),
                    ...createDirectiveCompletions(),
                    ...createLabelCompletions(symbols.labels),
                    ...createConstantCompletions(symbols.constants)
                ];
            }
        },
        "@", "<", ":"
    );

    const hoverProvider = vscode.languages.registerHoverProvider(
        { language: "fox16asm", scheme: "file" },
        { provideHover }
    );

    context.subscriptions.push(completionProvider, diagnostics, hoverProvider);
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(scheduleValidation),
        vscode.workspace.onDidChangeTextDocument((e) => scheduleValidation(e.document)),
        vscode.workspace.onDidCloseTextDocument((doc) => {
            diagnostics.delete(doc.uri);
            const key = doc.uri.toString();
            const timer = timers.get(key);
            if (timer) { clearTimeout(timer); timers.delete(key); }
        })
    );

    for (const document of vscode.workspace.textDocuments) scheduleValidation(document);
}

function deactivate() { }

module.exports = { activate, deactivate };
