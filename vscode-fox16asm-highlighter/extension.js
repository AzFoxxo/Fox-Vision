const vscode = require("vscode");

const OPCODES = [
    "NOP", "LFM", "WTM", "SRA", "AXY", "SXY", "MXY", "DXY", "EQU", "LEQ",
    "JPZ", "JNZ", "JMP", "CLR", "HLT", "BSL", "BSR", "AND", "ORA", "OR",
    "XOR", "DWR", "ILM", "IWR", "INC", "DEC", "MOV", "STR", "LOD", "CMP",
    "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE", "ADD", "SUB", "MUL", "DIV",
    "SHL", "SHR", "PUSH", "POP", "WAIT", "VBLANK", "VBL", "IN", "OUT",
    "DBG_LGC", "DBG_MEM", "DBG_INP"
];

const OPCODE_DOCS = {
    NOP: "No operation.",
    LFM: "Load from memory.",
    WTM: "Write to memory.",
    SRA: "Set register A/immediate value.",
    AXY: "Add X and Y.",
    SXY: "Subtract X and Y.",
    MXY: "Multiply X and Y.",
    DXY: "Divide X and Y.",
    EQU: "Set equality status flag.",
    LEQ: "Set less-than/equal status flag.",
    JPZ: "Jump if zero flag set.",
    JNZ: "Jump if zero flag not set.",
    JMP: "Unconditional jump.",
    CLR: "Clear registers/state.",
    HLT: "Halt the machine.",
    BSL: "Bit shift left.",
    BSR: "Bit shift right.",
    AND: "Bitwise AND.",
    ORA: "Bitwise OR (legacy mnemonic).",
    OR: "Bitwise OR.",
    XOR: "Bitwise XOR.",
    DWR: "Direct write to register/output.",
    ILM: "Inactive/indirect load memory.",
    IWR: "Inactive/indirect write memory.",
    INC: "Increment.",
    DEC: "Decrement.",
    MOV: "Move value.",
    STR: "Store value.",
    LOD: "Load value.",
    CMP: "Compare values.",
    JEQ: "Jump if equal.",
    JNE: "Jump if not equal.",
    JLT: "Jump if less-than.",
    JGT: "Jump if greater-than.",
    JLE: "Jump if less-than/equal.",
    JGE: "Jump if greater-than/equal.",
    ADD: "Add values.",
    SUB: "Subtract values.",
    MUL: "Multiply values.",
    DIV: "Divide values.",
    SHL: "Shift left.",
    SHR: "Shift right.",
    PUSH: "Push a register onto the stack.",
    POP: "Pop the stack into a register.",
    WAIT: "Wait for the requested number of cycles.",
    VBLANK: "Wait until the next rendered frame refresh.",
    VBL: "Alias for VBLANK.",
    IN: "Read a 16-bit value from a port into a register.",
    OUT: "Write a 16-bit register value to a port.",
    DBG_LGC: "Debug extension: log code.",
    DBG_MEM: "Debug extension: dump memory.",
    DBG_INP: "Debug extension: input debug action.",

};

const REGISTERS = ["X", "Y", "STATUS"];
const MACHINE_SYMBOLS = new Set([
    "X", "Y", "STATUS", "PC", "SP", "CYC", "EM",
    "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7",
    "PORT0", "PORT1", "PORT2", "PORT3", "PORT4", "PORT5", "PORT6", "PORT7"
]);
const MACHINE_SYMBOL_LIST = [...MACHINE_SYMBOLS];
const JUMP_OPCODES = new Set(["JPZ", "JNZ", "JMP", "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE"]);

// Expected operand counts for a subset of opcodes. Use null for variadic/unknown.
const OPCODE_OPERANDS = {
    NOP: 0,
    LFM: 1,
    WTM: 1,
    SRA: 1,
    AXY: [0, 2],
    SXY: [0, 2],
    MXY: [0, 2],
    DXY: [0, 2],
    EQU: 0,
    LEQ: 0,
    JPZ: 1,
    JNZ: 1,
    JMP: 1,
    CLR: 0,
    HLT: 0,
    BSL: [0, 2],
    BSR: [0, 2],
    AND: [0, 2],
    ORA: [0, 2],
    OR: 2,
    XOR: [0, 2],
    DWR: 1,
    ILM: 0,
    IWR: 0,
    INC: 0,
    DEC: 0,
    MOV: 2,
    STR: 2,
    LOD: 2,
    CMP: [0, 2],
    JEQ: 1,
    JNE: 1,
    JLT: 1,
    JGT: 1,
    JLE: 1,
    JGE: 1,
    ADD: 2,
    SUB: 2,
    MUL: 2,
    DIV: 2,
    SHL: 2,
    SHR: 2,
    PUSH: 1,
    POP: 1,
    WAIT: 1,
    VBLANK: 0,
    VBL: 0,
    IN: 2,
    OUT: 2,
    DBG_LGC: 1,
    DBG_MEM: 0,
    DBG_INP: 0
};

function collectSymbols(document) {
    const labels = new Set();
    const constants = new Set();

    for (let i = 0; i < document.lineCount; i += 1) {
        const line = document.lineAt(i).text;

        const labelMatch = line.match(/^\s*:([A-Za-z_][A-Za-z0-9_]*)\b/);
        if (labelMatch) {
            labels.add(labelMatch[1]);
        }

        const constMatch = line.match(/^\s*@const\s+([A-Za-z_][A-Za-z0-9_]*)\b/);
        if (constMatch) {
            constants.add(constMatch[1]);
        }
    }

    return {
        labels: [...labels],
        constants: [...constants]
    };
}

function createOpcodeCompletions() {
    return OPCODES.map((opcode) => {
        const item = new vscode.CompletionItem(opcode, vscode.CompletionItemKind.Keyword);
        item.detail = "Fox16ASM opcode";
        item.documentation = OPCODE_DOCS[opcode] ?? "Fox16ASM instruction.";
        return item;
    });
}

function createRegisterCompletions() {
    return REGISTERS.map((register) => {
        const item = new vscode.CompletionItem(register, vscode.CompletionItemKind.Variable);
        item.detail = "Fox16ASM register";
        item.documentation = `Register ${register}.`;
        return item;
    });
}

function createDirectiveCompletions() {
    const constDirective = new vscode.CompletionItem("@const", vscode.CompletionItemKind.Snippet);
    constDirective.detail = "Preprocessor directive";
    constDirective.documentation = "Declare a named constant for preprocessor substitution.";
    constDirective.insertText = new vscode.SnippetString("@const ${1:NAME} ${2:%0}");

    return [constDirective];
}

function createLabelCompletions(labels) {
    return labels.map((label) => {
        const item = new vscode.CompletionItem(label, vscode.CompletionItemKind.Reference);
        item.detail = "Label reference";
        item.documentation = `Jump/reference label: ${label}`;
        return item;
    });
}

function createConstantCompletions(constants) {
    return constants.map((constantName) => {
        const item = new vscode.CompletionItem(`<${constantName}>`, vscode.CompletionItemKind.Constant);
        item.detail = "Named constant";
        item.documentation = `Preprocessor constant: <${constantName}>`;
        item.insertText = `<${constantName}>`;
        return item;
    });
}

function createMachineSymbolCompletions() {
    return MACHINE_SYMBOL_LIST.map((symbol) => {
        const kind = symbol.startsWith("PORT") ? vscode.CompletionItemKind.EnumMember : vscode.CompletionItemKind.Variable;
        const item = new vscode.CompletionItem(symbol, kind);
        item.detail = "Fox16ASM machine symbol";
        item.documentation = `Machine symbol: ${symbol}`;
        return item;
    });
}

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
    const jumpMatch = linePrefix.match(/\b([A-Z_0-9]+)\s+([A-Za-z_0-9]*)$/);
    if (!jumpMatch) {
        return false;
    }

    return JUMP_OPCODES.has(jumpMatch[1]);
}

function makeRangeForLine(document, line) {
    const text = document.lineAt(line).text;
    return new vscode.Range(line, 0, line, Math.max(1, text.length));
}

function validateDocument(document, collection) {
    const diagnostics = [];
    const labels = new Map();
    const usedLabels = new Map();
    const constants = new Map();

    for (let i = 0; i < document.lineCount; i += 1) {
        let line = document.lineAt(i).text;
        // strip comments starting with ; or //
        line = line.replace(/;.*$/g, "").replace(/\/\/.*$/g, "").trim();
        if (!line) continue;

        // Label definition
        const labelMatch = line.match(/^:([A-Za-z_][A-Za-z0-9_]*)\b/);
        if (labelMatch) {
            const name = labelMatch[1];
            if (labels.has(name)) {
                diagnostics.push(new vscode.Diagnostic(makeRangeForLine(document, i), `duplicate label ${name}`, vscode.DiagnosticSeverity.Error));
            } else {
                labels.set(name, i);
            }
            // continue; labels may also be alone on a line
            line = line.replace(/^:[A-Za-z_][A-Za-z0-9_]*\b\s*/, "");
            if (!line) continue;
        }

        // const directive
        const constMatch = line.match(/^@const\s+([A-Za-z_][A-Za-z0-9_]*)\b(?:\s+(.*))?/i);
        if (constMatch) {
            const name = constMatch[1];
            if (constants.has(name)) {
                diagnostics.push(new vscode.Diagnostic(makeRangeForLine(document, i), `duplicate constant ${name}`, vscode.DiagnosticSeverity.Error));
            } else {
                constants.set(name, constMatch[2] || "");
            }
            continue;
        }

        // opcode + operands
        const opMatch = line.match(/^([A-Za-z_][A-Za-z0-9_]*)\b(.*)$/);
        if (!opMatch) continue;
        const opcode = opMatch[1].toUpperCase();
        const rest = opMatch[2].trim();

        if (!OPCODES.includes(opcode)) {
            diagnostics.push(new vscode.Diagnostic(makeRangeForLine(document, i), `unknown opcode ${opcode}`, vscode.DiagnosticSeverity.Error));
            continue;
        }

        const operands = rest.length === 0
            ? []
            : rest.replace(/,/g, " ").trim().split(/\s+/).filter(Boolean);
        const expected = Object.prototype.hasOwnProperty.call(OPCODE_OPERANDS, opcode) ? OPCODE_OPERANDS[opcode] : null;
        if (expected !== null && expected !== undefined) {
            if (Array.isArray(expected)) {
                if (!expected.includes(operands.length)) {
                    diagnostics.push(new vscode.Diagnostic(makeRangeForLine(document, i), `${opcode} expects ${expected.join(" or ")} operand(s), got ${operands.length}`, vscode.DiagnosticSeverity.Error));
                }
            } else if (expected !== operands.length) {
                diagnostics.push(new vscode.Diagnostic(makeRangeForLine(document, i), `${opcode} expects ${expected} operand(s), got ${operands.length}`, vscode.DiagnosticSeverity.Error));
            }
        }

        // record label usages for jump/operand checks
        for (const opd of operands) {
            const labelRef = opd.replace(/[<>\[\]]/g, "");
            const upperLabelRef = labelRef.toUpperCase();
            if (/^PORT\d+[^\s]*$/i.test(labelRef) && !MACHINE_SYMBOLS.has(upperLabelRef)) {
                diagnostics.push(new vscode.Diagnostic(makeRangeForLine(document, i), `invalid port symbol ${labelRef}`, vscode.DiagnosticSeverity.Error));
                continue;
            }
            if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(labelRef) && !MACHINE_SYMBOLS.has(upperLabelRef)) {
                usedLabels.set(labelRef, (usedLabels.get(labelRef) || 0) + 1);
            }
        }
    }

    // undefined label uses
    for (const [name] of usedLabels) {
        if (!labels.has(name) && !constants.has(name)) {
            diagnostics.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), `undefined label or constant ${name}`, vscode.DiagnosticSeverity.Warning));
        }
    }

    collection.set(document.uri, diagnostics);
}

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

    const provider = vscode.languages.registerCompletionItemProvider(
        { language: "fox16asm", scheme: "file" },
        {
            provideCompletionItems(document, position) {
                const linePrefix = document.lineAt(position).text.slice(0, position.character);
                const symbols = collectSymbols(document);

                if (isDirectiveContext(linePrefix)) {
                    return createDirectiveCompletions();
                }

                if (isConstantContext(linePrefix)) {
                    return createConstantCompletions(symbols.constants);
                }

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
        "@",
        "<",
        ":"
    );

    const hoverProvider = vscode.languages.registerHoverProvider(
        { language: "fox16asm", scheme: "file" },
        {
            provideHover(document, position) {
                const wordRange = document.getWordRangeAtPosition(position, /[A-Za-z0-9_]+/);
                if (!wordRange) return null;
                const word = document.getText(wordRange).toUpperCase();
                if (OPCODE_DOCS[word]) {
                    return new vscode.Hover(`**${word}** — ${OPCODE_DOCS[word]}`);
                }
                if (REGISTERS.includes(word)) {
                    return new vscode.Hover(`**${word}** — register`);
                }
                if (MACHINE_SYMBOLS.has(word)) {
                    return new vscode.Hover(`**${word}** — machine symbol`);
                }
                return null;
            }
        }
    );

    context.subscriptions.push(provider, diagnostics, hoverProvider);
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(scheduleValidation),
        vscode.workspace.onDidChangeTextDocument((event) => scheduleValidation(event.document)),
        vscode.workspace.onDidCloseTextDocument((document) => {
            diagnostics.delete(document.uri);
            const key = document.uri.toString();
            const timer = timers.get(key);
            if (timer) {
                clearTimeout(timer);
                timers.delete(key);
            }
        })
    );

    for (const document of vscode.workspace.textDocuments) scheduleValidation(document);
}

function deactivate() { }

module.exports = {
    activate,
    deactivate
};
