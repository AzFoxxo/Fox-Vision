const vscode = require("vscode");

const OPCODES = [
    "NOP", "LFM", "WTM", "SRA", "AXY", "SXY", "MXY", "DXY", "EQU", "LEQ",
    "JPZ", "JNZ", "JMP", "CLR", "HLT", "BSL", "BSR", "AND", "ORA", "OR",
    "XOR", "DWR", "ILM", "IWR", "INC", "DEC", "MOV", "STR", "LOD", "CMP",
    "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE", "ADD", "SUB", "MUL", "DIV",
    "SHL", "SHR", "DBG_LGC", "DBG_MEM", "DBG_INP", "DGB_MEM", "DGB_INP"
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
    DBG_LGC: "Debug extension: log code.",
    DBG_MEM: "Debug extension: dump memory.",
    DBG_INP: "Debug extension: input debug action.",
    DGB_MEM: "Legacy alias for DBG_MEM.",
    DGB_INP: "Legacy alias for DBG_INP."
};

const REGISTERS = ["X", "Y", "STATUS"];
const JUMP_OPCODES = new Set(["JPZ", "JNZ", "JMP", "JEQ", "JNE", "JLT", "JGT", "JLE", "JGE"]);

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

function activate(context) {
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

    context.subscriptions.push(provider);
}

function deactivate() {}

module.exports = {
    activate,
    deactivate
};
