"use strict";

const vscode = require("vscode");
const fs = require("fs");
const path = require("path");

const TYPES = ["u8", "u16", "u32", "void"];
const KEYWORDS = ["if", "else", "while", "return"];
const BUILTINS = [
    "poke", "peek", "wait", "cyc", "vblank",
    "in_port", "out_port",
    "am", "set_am", "em", "set_em",
    "os_start", "os_return"
];

const BUILTIN_SIGNATURES = new Map([
    ["poke",      { ret: "void", params: 2, detail: "poke(addr: u16, value: u16) -> void",   doc: "Write a 16-bit word to memory at `addr`." }],
    ["peek",      { ret: "u16",  params: 1, detail: "peek(addr: u16) -> u16",                doc: "Read a 16-bit word from memory at `addr`." }],
    ["wait",      { ret: "void", params: 1, detail: "wait(cycles: u16) -> void",             doc: "Block for the given number of CPU cycles." }],
    ["cyc",       { ret: "u16",  params: 0, detail: "cyc() -> u16",                          doc: "Read the low 16 bits of the CPU cycle counter (`CYC` register)." }],
    ["vblank",    { ret: "void", params: 0, detail: "vblank() -> void",                      doc: "Block until the next display VBlank (frame sync)." }],
    ["in_port",   { ret: "u16",  params: 1, detail: "in_port(port: u16) -> u16",             doc: "Read a value from a hardware port. Requires extended or em2_am1 mode.\nPort range: 0–7 in extended mode; 0–65535 in em2_am1 mode." }],
    ["out_port",  { ret: "void", params: 2, detail: "out_port(port: u16, value: u16) -> void", doc: "Write a value to a hardware port. Requires extended or em2_am1 mode.\nPort range: 0–7 in extended mode; 0–65535 in em2_am1 mode." }],
    ["am",        { ret: "u16",  params: 0, detail: "am() -> u16",                           doc: "Read the AM (Addressing Mode) register. **em2_am1 mode only.**\nReturns 1 when byte addressing is active." }],
    ["set_am",    { ret: "void", params: 1, detail: "set_am(value: u16) -> void",            doc: "Write the AM register. **em2_am1 mode only.**\n`set_am(1)` enables byte addressing; `set_am(0)` disables it." }],
    ["em",        { ret: "u16",  params: 0, detail: "em() -> u16",                           doc: "Read the EM (Extension Mode) register. **em2_am1 mode only.**\nReturns 2 in V2.0 32-bit mode." }],
    ["set_em",    { ret: "void", params: 1, detail: "set_em(value: u16) -> void",            doc: "Write the EM register. **em2_am1 mode only.**\n`set_em(2)` enables 32-bit address space." }],
    ["os_start",  { ret: "void", params: 1, detail: "os_start(address: u16) -> void",        doc: "Start a FoxOS foreground program at the given address." }],
    ["os_return", { ret: "void", params: 0, detail: "os_return() -> void",                   doc: "Return from a FoxOS foreground program to the resident OS (`OSRET`)." }],
]);

function createCompletionItem(label, kind, detail, documentation, insertText) {
    const item = new vscode.CompletionItem(label, kind);
    item.detail = detail;
    item.documentation = documentation;
    if (insertText !== undefined) {
        item.insertText = insertText;
    }
    return item;
}

function createKeywordItems() {
    return KEYWORDS.map((keyword) =>
        createCompletionItem(keyword, vscode.CompletionItemKind.Keyword, "FoxC keyword", `FoxC keyword: ${keyword}`)
    );
}

function createTypeItems() {
    const TYPE_DOCS = {
        u8:   "8-bit unsigned integer. Packed two-per-word in arrays.",
        u16:  "16-bit unsigned integer. Native word size.",
        u32:  "32-bit unsigned integer. **Requires `--mode em2_am1`.** Occupies two 16-bit words in memory.",
        void: "No value. Used as function return type."
    };
    return TYPES.map((type) =>
        createCompletionItem(type, vscode.CompletionItemKind.Class, `FoxC type`, new vscode.MarkdownString(TYPE_DOCS[type] ?? type))
    );
}

function createBuiltinItems() {
    return BUILTINS.map((name) => {
        const sig = BUILTIN_SIGNATURES.get(name);
        const detail = sig ? sig.detail : `builtin ${name}`;
        const doc = sig ? new vscode.MarkdownString(sig.doc) : undefined;
        return createCompletionItem(name, vscode.CompletionItemKind.Function, "FoxC builtin", doc ?? detail);
    });
}

function createSnippetItems() {
    return [
        createCompletionItem(
            "if",
            vscode.CompletionItemKind.Snippet,
            "If statement snippet",
            "Insert an if statement",
            new vscode.SnippetString("if (${1:condition}) {\n\t$0\n}")
        ),
        createCompletionItem(
            "ifelse",
            vscode.CompletionItemKind.Snippet,
            "If/else snippet",
            "Insert an if/else statement",
            new vscode.SnippetString("if (${1:condition}) {\n\t$2\n} else {\n\t$0\n}")
        ),
        createCompletionItem(
            "while",
            vscode.CompletionItemKind.Snippet,
            "While loop snippet",
            "Insert a while loop",
            new vscode.SnippetString("while (${1:condition}) {\n\t$0\n}")
        ),
        createCompletionItem(
            "main",
            vscode.CompletionItemKind.Snippet,
            "Program entry point",
            "Insert a main function",
            new vscode.SnippetString("void main() {\n\t$0\n}")
        ),
        createCompletionItem(
            "return",
            vscode.CompletionItemKind.Snippet,
            "Return statement snippet",
            "Insert a return statement",
            new vscode.SnippetString("return ${1:value};")
        ),
        createCompletionItem(
            "u32var",
            vscode.CompletionItemKind.Snippet,
            "u32 variable (em2_am1 mode)",
            new vscode.MarkdownString("Declare a 32-bit variable. Requires `--mode em2_am1`."),
            new vscode.SnippetString("u32 ${1:name} = ${2:0};")
        ),
        createCompletionItem(
            "include",
            vscode.CompletionItemKind.Snippet,
            "Include a header",
            new vscode.MarkdownString("Insert a `#include` directive. Headers may contain prototypes or whole code."),
            new vscode.SnippetString('#include "${1:header.fh}"')
        ),
        createCompletionItem(
            "prototype",
            vscode.CompletionItemKind.Snippet,
            "Function prototype",
            "Declare a function signature without a body",
            new vscode.SnippetString("${1:u16} ${2:name}(${3:u16 value});")
        )
    ];
}

// ---------------------------------------------------------------------------
// Header / include support
// ---------------------------------------------------------------------------

function parseIncludeDirective(raw) {
    const match = /^#\s*include\s+(?:"([^"]+)"|<([^>]+)>)/.exec(raw.trim());
    if (!match) return null;
    if (match[1] !== undefined) return { target: match[1], angled: false };
    if (match[2] !== undefined) return { target: match[2], angled: true };
    return null;
}

function getIncludeDirectories(document) {
    const dirs = [];
    const config = vscode.workspace.getConfiguration("foxc", document.uri);
    const configured = config.get("includePaths", []);
    const workspaceFolder = vscode.workspace.getWorkspaceFolder(document.uri);
    for (const entry of configured) {
        if (typeof entry !== "string" || entry.length === 0) continue;
        if (path.isAbsolute(entry)) {
            dirs.push(entry);
        } else if (workspaceFolder) {
            dirs.push(path.join(workspaceFolder.uri.fsPath, entry));
        } else {
            dirs.push(path.resolve(path.dirname(document.uri.fsPath), entry));
        }
    }
    return dirs;
}

function resolveInclude(fromPath, target, angled, document) {
    const candidates = [];
    const currentDir = path.dirname(fromPath);
    const includeDirs = getIncludeDirectories(document);
    if (angled) {
        candidates.push(...includeDirs, currentDir);
    } else {
        candidates.push(currentDir, ...includeDirs);
    }
    for (const dir of candidates) {
        const full = path.join(dir, target);
        try {
            if (fs.statSync(full).isFile()) return full;
        } catch (_) {
            // not found in this directory; try the next one
        }
    }
    return null;
}

function collectDocumentSymbols(document) {
    // Include symbols from headers as well as from the open document, so
    // completions work for functions declared in included headers.
    const symbols = collectTranslationUnitSymbols(document);
    const names = new Set();
    for (const name of symbols.globals.keys()) names.add(name);
    for (const name of symbols.functions.keys()) {
        if (!BUILTINS.includes(name)) names.add(name);
    }
    return [...names].sort((left, right) => left.localeCompare(right));
}

function createSymbolItems(symbols) {
    return symbols.map((symbol) =>
        createCompletionItem(symbol, vscode.CompletionItemKind.Variable, "Declared symbol", `Symbol from this document: ${symbol}`)
    );
}

function tokenize(text) {
    const tokens = [];
    let index = 0;
    let line = 1;
    let column = 1;

    function current() {
        return text[index];
    }

    function next() {
        return text[index + 1];
    }

    function advance() {
        const ch = text[index];
        index += 1;
        if (ch === "\n") {
            line += 1;
            column = 1;
        } else {
            column += 1;
        }
        return ch;
    }

    function addToken(kind, value, tokenLine, tokenColumn) {
        tokens.push({ kind, value, line: tokenLine, column: tokenColumn });
    }

    while (index < text.length) {
        const ch = current();

        if (ch === "/" && next() === "/") {
            while (index < text.length && current() !== "\n") {
                advance();
            }
            continue;
        }

        if (/\s/.test(ch)) {
            advance();
            continue;
        }

        const tokenLine = line;
        const tokenColumn = column;

        if (ch === "#") {
            let value = "";
            while (index < text.length && current() !== "\n") {
                value += advance();
            }
            addToken("preprocessor", value.trim(), tokenLine, tokenColumn);
            continue;
        }

        if (ch === "'" || ch === "\"") {
            const quote = ch;
            let value = advance();
            let terminated = false;
            while (index < text.length && current() !== "\n") {
                const literalChar = advance();
                value += literalChar;
                if (literalChar === "\\") {
                    if (index < text.length && current() !== "\n") {
                        value += advance();
                    }
                    continue;
                }
                if (literalChar === quote) {
                    terminated = true;
                    break;
                }
            }
            addToken(quote === "\"" ? "string" : "char", value, tokenLine, tokenColumn);
            if (!terminated) {
                addToken("unknown", "", tokenLine, tokenColumn);
            }
            continue;
        }

        if (/[A-Za-z_]/.test(ch)) {
            let value = "";
            while (index < text.length && /[A-Za-z0-9_]/.test(current())) {
                value += advance();
            }
            if (TYPES.includes(value)) {
                addToken("type", value, tokenLine, tokenColumn);
            } else if (KEYWORDS.includes(value)) {
                addToken(value, value, tokenLine, tokenColumn);
            } else {
                addToken("ident", value, tokenLine, tokenColumn);
            }
            continue;
        }

        if (/[0-9]/.test(ch)) {
            let value = "";
            while (index < text.length && /[0-9]/.test(current())) {
                value += advance();
            }
            addToken("number", value, tokenLine, tokenColumn);
            continue;
        }

        const twoChar = `${ch}${next() || ""}`;
        if (["&&", "||", "==", "!=", "<=", ">=", "<<", ">>"].includes(twoChar)) {
            advance();
            advance();
            addToken("op", twoChar, tokenLine, tokenColumn);
            continue;
        }

        if ("(){}[],;=+-*/&|^~<>".includes(ch)) {
            advance();
            addToken("op", ch, tokenLine, tokenColumn);
            continue;
        }

        advance();
        addToken("unknown", ch, tokenLine, tokenColumn);
    }

    tokens.push({ kind: "eof", value: "", line, column });
    return tokens;
}

function isTypeToken(token) {
    return token.kind === "type";
}

function isIdentifierToken(token) {
    return token.kind === "ident";
}

function isOpenParen(token) {
    return token.kind === "op" && token.value === "(";
}

function isOpenBrace(token) {
    return token.kind === "op" && token.value === "{";
}

function isCloseBrace(token) {
    return token.kind === "op" && token.value === "}";
}

function isCloseParen(token) {
    return token.kind === "op" && token.value === ")";
}

function isOpenBracket(token) {
    return token.kind === "op" && token.value === "[";
}

function isCloseBracket(token) {
    return token.kind === "op" && token.value === "]";
}

function isComma(token) {
    return token.kind === "op" && token.value === ",";
}

function isSemicolon(token) {
    return token.kind === "op" && token.value === ";";
}

function isAssign(token) {
    return token.kind === "op" && token.value === "=";
}

function isUnaryOperator(token) {
    return token.kind === "op" && ["-", "~", "*", "&"].includes(token.value);
}

function makeRange(token, endColumnOffset = 1) {
    const start = new vscode.Position(token.line - 1, token.column - 1);
    const end = new vscode.Position(token.line - 1, token.column - 1 + endColumnOffset);
    return new vscode.Range(start, end);
}

function makeDiagnostic(token, message, severity = vscode.DiagnosticSeverity.Error) {
    return new vscode.Diagnostic(makeRange(token, Math.max(1, String(token.value || "").length)), message, severity);
}

function createEmptySymbols() {
    const functions = new Map();
    for (const builtin of BUILTINS) {
        functions.set(builtin, {
            ...BUILTIN_SIGNATURES.get(builtin),
            source: "builtin",
            isPrototype: false,
            isDefinition: true,
        });
    }
    return { globals: new Map(), functions, diagnostics: [] };
}

function addFunctionSymbol(symbols, name, info, token, source, reportDiagnostics) {
    const existing = symbols.functions.get(name);
    if (!existing) {
        symbols.functions.set(name, { ...info, source });
        return;
    }
    if (existing.source === "builtin") {
        if (reportDiagnostics) {
            symbols.diagnostics.push(makeDiagnostic(token, `function ${name} conflicts with a builtin`));
        }
        return;
    }
    if (existing.isDefinition && info.isDefinition) {
        if (reportDiagnostics) {
            symbols.diagnostics.push(makeDiagnostic(token, `duplicate definition of function ${name}`));
        }
        return;
    }
    if (info.isDefinition) {
        // A prototype followed by its definition is legal.
        symbols.functions.set(name, { ...info, source, isPrototype: true });
    }
}

function addGlobalSymbol(symbols, name, info, token, reportDiagnostics) {
    if (symbols.globals.has(name)) {
        if (reportDiagnostics) {
            symbols.diagnostics.push(makeDiagnostic(token, `duplicate global ${name}`));
        }
        return;
    }
    symbols.globals.set(name, info);
}

function collectSymbolsFromTokens(tokens, symbols, source, reportDiagnostics) {
    let index = 0;

    function peek(offset = 0) {
        return tokens[Math.min(index + offset, tokens.length - 1)];
    }

    function advance() {
        const token = tokens[index];
        index += 1;
        return token;
    }

    function skipToSemicolon() {
        while (index < tokens.length && !isSemicolon(peek()) && peek().kind !== "eof") {
            advance();
        }
        if (isSemicolon(peek())) {
            advance();
        }
    }

    function skipBlock() {
        let depth = 1;
        while (index < tokens.length && depth > 0) {
            const token = advance();
            if (isOpenBrace(token)) {
                depth += 1;
            } else if (isCloseBrace(token)) {
                depth -= 1;
            }
        }
    }

    function readParams() {
        const params = [];
        let depth = 1;
        while (index < tokens.length && depth > 0) {
            const token = advance();
            if (token.kind === "eof") break;
            if (isOpenParen(token)) { depth += 1; continue; }
            if (isCloseParen(token)) { depth -= 1; continue; }
            if (depth > 1) continue;
            if (isTypeToken(token)) {
                let typeName = token.value;
                let stars = 0;
                while (tokens[index] && tokens[index].kind === "op" && tokens[index].value === "*") {
                    typeName += advance().value;
                    stars += 1;
                }
                // `f(void)` is an explicit empty parameter list; `void *p` is a
                // pointer parameter, so only bare `void` before `)` is skipped.
                if (token.value === "void" && stars === 0 && isCloseParen(tokens[index])) continue;
                const name = isIdentifierToken(tokens[index]) ? advance().value : "";
                params.push({ type: typeName, name });
            }
        }
        return params;
    }

    while (index < tokens.length) {
        const token = peek();
        if (token.kind === "eof") break;
        if (token.kind === "preprocessor") { advance(); continue; }
        if (!isTypeToken(token)) { advance(); continue; }

        const typeToken = advance();
        while (peek().kind === "op" && peek().value === "*") advance();
        const nameToken = peek();
        if (!isIdentifierToken(nameToken)) { advance(); continue; }
        advance();

        if (isOpenParen(peek())) {
            advance();
            const params = readParams();
            let isDefinition = false;
            if (isOpenBrace(peek())) {
                isDefinition = true;
                advance();
                skipBlock();
            } else if (isSemicolon(peek())) {
                advance();
            }
            const paramText = params
                .map((p) => `${p.type}${p.name ? " " + p.name : ""}`)
                .join(", ");
            addFunctionSymbol(symbols, nameToken.value, {
                ret: typeToken.value,
                params: params.length,
                paramList: params,
                isPrototype: !isDefinition,
                isDefinition,
                detail: `${typeToken.value} ${nameToken.value}(${paramText})`,
            }, nameToken, source, reportDiagnostics);
            continue;
        }

        addGlobalSymbol(symbols, nameToken.value, { type: typeToken.value, source }, nameToken, reportDiagnostics);
        skipToSemicolon();
    }
}

function collectTopLevelSymbols(tokens) {
    const symbols = createEmptySymbols();
    collectSymbolsFromTokens(tokens, symbols, null, true);
    return symbols;
}

function isHeaderDocument(document) {
    const lower = document.uri.fsPath.toLowerCase();
    return lower.endsWith(".fh") || lower.endsWith(".h");
}

function collectTranslationUnitSymbols(document) {
    const symbols = createEmptySymbols();
    const visited = new Set();

    function collectFile(filePath, text, isMain) {
        const normalized = path.normalize(filePath);
        if (visited.has(normalized)) return;
        visited.add(normalized);

        const tokens = tokenize(text);
        collectSymbolsFromTokens(tokens, symbols, isMain ? null : filePath, isMain);

        for (const token of tokens) {
            if (token.kind !== "preprocessor") continue;
            const directive = parseIncludeDirective(token.value);
            if (!directive) continue;
            const resolved = resolveInclude(filePath, directive.target, directive.angled, document);
            if (!resolved) {
                if (isMain) {
                    symbols.diagnostics.push(makeDiagnostic(token, `cannot find include ${directive.target}`));
                }
                continue;
            }
            let content;
            try {
                content = fs.readFileSync(resolved, "utf8");
            } catch (_) {
                if (isMain) {
                    symbols.diagnostics.push(makeDiagnostic(token, `cannot read include ${directive.target}`));
                }
                continue;
            }
            collectFile(resolved, content, false);
        }
    }

    collectFile(document.uri.fsPath, document.getText(), true);
    return symbols;
}

class FoxCValidator {
    constructor(tokens, symbols, requireMain) {
        this.tokens = tokens;
        this.symbols = symbols;
        this.requireMain = requireMain !== false;
        this.index = 0;
        this.diagnostics = [];
        this.scopes = [];
        this.currentFunction = null;
    }

    current() {
        return this.tokens[this.index];
    }

    previous() {
        return this.tokens[Math.max(0, this.index - 1)];
    }

    advance() {
        const token = this.current();
        if (this.index < this.tokens.length - 1) {
            this.index += 1;
        }
        return token;
    }

    match(kind, value) {
        const token = this.current();
        if (token.kind !== kind) {
            return false;
        }
        if (value !== undefined && token.value !== value) {
            return false;
        }
        this.advance();
        return true;
    }

    check(kind, value) {
        const token = this.current();
        if (token.kind !== kind) {
            return false;
        }
        if (value !== undefined && token.value !== value) {
            return false;
        }
        return true;
    }

    addError(token, message) {
        this.diagnostics.push(makeDiagnostic(token, message));
    }

    expect(kind, value, message) {
        if (this.check(kind, value)) {
            return this.advance();
        }
        this.addError(this.current(), message);
        return null;
    }

    pushScope() {
        this.scopes.push(new Map());
    }

    popScope() {
        this.scopes.pop();
    }

    declare(nameToken, typeName) {
        const scope = this.scopes[this.scopes.length - 1];
        if (scope.has(nameToken.value)) {
            this.addError(nameToken, `duplicate symbol ${nameToken.value}`);
            return false;
        }
        scope.set(nameToken.value, typeName);
        return true;
    }

    resolveVariable(name) {
        for (let index = this.scopes.length - 1; index >= 0; index -= 1) {
            const scope = this.scopes[index];
            if (scope.has(name)) {
                return true;
            }
        }
        return this.symbols.globals.has(name);
    }

    resolveFunction(name) {
        return this.symbols.functions.get(name) || null;
    }

    parseProgram() {
        while (!this.check("eof")) {
            if (this.check("preprocessor")) {
                this.advance();
                continue;
            }
            if (!isTypeToken(this.current())) {
                this.addError(this.current(), "expected a top-level declaration");
                this.advance();
                continue;
            }

            const typeToken = this.advance();
            while (this.match("op", "*")) {
                // pointer declarator markers are allowed between type and name
            }
            const nameToken = this.expect("ident", undefined, "expected an identifier after the type");
            if (!nameToken) {
                this.synchronizeTopLevel();
                continue;
            }

            if (this.check("op", "(")) {
                this.parseFunction(typeToken, nameToken);
                continue;
            }

            this.parseGlobal(typeToken, nameToken);
        }

        if (this.requireMain) {
            const mainSig = this.symbols.functions.get("main");
            if (!mainSig) {
                this.diagnostics.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), "missing required function main"));
            } else if (mainSig.ret !== "void" || mainSig.params !== 0) {
                this.diagnostics.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), "main must have signature void main()"));
            } else if (!mainSig.isDefinition) {
                this.diagnostics.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), "missing definition of function main"));
            }
        }

        return this.diagnostics;
    }

    synchronizeTopLevel() {
        while (!this.check("eof")) {
            if (this.check("op", ";") || this.check("op", "}")) {
                this.advance();
                return;
            }
            if (isTypeToken(this.current())) {
                return;
            }
            this.advance();
        }
    }

    parseGlobal(typeToken, nameToken) {
        if (typeToken.value === "void") {
            this.addError(typeToken, `global ${nameToken.value} cannot be void`);
        }

        while (this.match("op", "[")) {
            if (!this.check("op", "]")) {
                this.parseExpression();
            }
            this.expect("op", "]", "expected ']' after array size");
        }

        if (this.match("op", "=")) {
            this.parseExpression();
        }

        if (!this.expect("op", ";", "expected ';' after global declaration")) {
            this.synchronizeToStatementEnd();
        }
    }

    parseFunction(typeToken, nameToken) {
        this.expect("op", "(", "expected '(' after function name");
        const params = [];

        if (!this.check("op", ")")) {
            while (!this.check("eof") && !this.check("op", ")")) {
                const paramType = this.expect("type", undefined, "expected a parameter type");
                let stars = 0;
                while (this.match("op", "*")) {
                    stars += 1;
                }
                // `f(void)` is an explicit empty parameter list; `void *p` is a
                // pointer parameter and must still be counted.
                if (paramType && paramType.value === "void" && stars === 0 && this.check("op", ")")) {
                    break;
                }
                const paramName = this.check("ident") ? this.advance() : null;
                if (paramType) {
                    params.push({
                        type: paramType.value,
                        name: paramName ? paramName.value : "",
                        token: paramName || paramType,
                    });
                }
                if (this.match("op", ",")) {
                    continue;
                }
                break;
            }
        }

        this.expect("op", ")", "expected ')' after parameter list");

        // A prototype ends at ';'. Its signature was already collected above.
        if (this.match("op", ";")) {
            return;
        }

        if (!this.expect("op", "{", "expected '{' to start the function body")) {
            this.synchronizeTopLevel();
            return;
        }

        this.currentFunction = { name: nameToken.value, ret: typeToken.value };
        this.pushScope();
        for (const param of params) {
            this.declare({ value: param.name, line: nameToken.line, column: nameToken.column }, param.type);
        }

        while (!this.check("eof") && !this.check("op", "}")) {
            this.parseStatement();
        }

        this.expect("op", "}", "expected '}' to close the function body");
        this.popScope();
        this.currentFunction = null;
    }

    parseStatement() {
        if (this.check("type")) {
            const typeToken = this.advance();
            while (this.match("op", "*")) {
                // pointer declarator markers are allowed between type and name
            }
            const nameToken = this.expect("ident", undefined, "expected a variable name");
            if (!nameToken) {
                this.synchronizeToStatementEnd();
                return;
            }
            if (typeToken.value === "void") {
                this.addError(typeToken, `variable ${nameToken.value} cannot be void`);
            }
            this.declare(nameToken, typeToken.value);

            while (this.match("op", "[")) {
                if (!this.check("op", "]")) {
                    this.parseExpression();
                }
                this.expect("op", "]", "expected ']' after array size");
            }

            if (this.match("op", "=")) {
                this.parseExpression();
            }
            if (!this.expect("op", ";", "expected ';' after variable declaration")) {
                this.synchronizeToStatementEnd();
            }
            return;
        }

        if (this.match("if")) {
            this.expect("op", "(", "expected '(' after if");
            this.parseExpression();
            this.expect("op", ")", "expected ')' after if condition");
            this.parseBlock();
            if (this.match("else")) {
                this.parseBlock();
            }
            return;
        }

        if (this.match("while")) {
            this.expect("op", "(", "expected '(' after while");
            this.parseExpression();
            this.expect("op", ")", "expected ')' after while condition");
            this.parseBlock();
            return;
        }

        if (this.match("return")) {
            const hasValue = !this.check("op", ";");
            if (this.currentFunction && this.currentFunction.ret === "void" && hasValue) {
                this.addError(this.current(), "void function cannot return a value");
            }
            if (this.currentFunction && this.currentFunction.ret !== "void" && !hasValue) {
                this.addError(this.current(), "non-void function must return a value");
            }
            if (hasValue) {
                this.parseExpression();
            }
            if (!this.expect("op", ";", "expected ';' after return")) {
                this.synchronizeToStatementEnd();
            }
            return;
        }

        if (this.check("op", "*")) {
            const startIndex = this.index;
            this.parseUnary();
            if (this.match("op", "=")) {
                this.parseExpression();
                if (!this.expect("op", ";", "expected ';' after assignment")) {
                    this.synchronizeToStatementEnd();
                }
                return;
            }
            this.index = startIndex;
        }

        if (this.check("ident")) {
            const startIndex = this.index;
            const nameToken = this.advance();
            while (this.match("op", "[")) {
                this.parseExpression();
                this.expect("op", "]", "expected ']' after index expression");
            }

            if (this.match("op", "=")) {
                if (!this.resolveVariable(nameToken.value)) {
                    this.addError(nameToken, `unknown variable ${nameToken.value}`);
                }
                this.parseExpression();
                if (!this.expect("op", ";", "expected ';' after assignment")) {
                    this.synchronizeToStatementEnd();
                }
                return;
            }

            this.index = startIndex;
        }

        this.parseExpression();
        if (!this.expect("op", ";", "expected ';' after expression")) {
            this.synchronizeToStatementEnd();
        }
    }

    parseBlock() {
        if (!this.expect("op", "{", "expected '{' to start a block")) {
            return;
        }
        this.pushScope();
        while (!this.check("eof") && !this.check("op", "}")) {
            this.parseStatement();
        }
        this.expect("op", "}", "expected '}' to close the block");
        this.popScope();
    }

    parseExpression() {
        this.parseLogicalOr();
    }

    parseLogicalOr() {
        this.parseLogicalAnd();
        while (this.match("op", "||")) {
            this.parseLogicalAnd();
        }
    }

    parseLogicalAnd() {
        this.parseBitwiseOr();
        while (this.match("op", "&&")) {
            this.parseBitwiseOr();
        }
    }

    parseBitwiseOr() {
        this.parseBitwiseXor();
        while (this.match("op", "|")) {
            this.parseBitwiseXor();
        }
    }

    parseBitwiseXor() {
        this.parseBitwiseAnd();
        while (this.match("op", "^")) {
            this.parseBitwiseAnd();
        }
    }

    parseBitwiseAnd() {
        this.parseEquality();
        while (this.match("op", "&")) {
            this.parseEquality();
        }
    }

    parseEquality() {
        this.parseComparison();
        while (this.match("op", "==") || this.match("op", "!=")) {
            this.parseComparison();
        }
    }

    parseComparison() {
        this.parseShift();
        while (this.match("op", "<") || this.match("op", ">") || this.match("op", "<=") || this.match("op", ">=")) {
            this.parseShift();
        }
    }

    parseShift() {
        this.parseAdditive();
        while (this.match("op", "<<") || this.match("op", ">>")) {
            this.parseAdditive();
        }
    }

    parseAdditive() {
        this.parseMultiplicative();
        while (this.match("op", "+") || this.match("op", "-")) {
            this.parseMultiplicative();
        }
    }

    parseMultiplicative() {
        this.parseUnary();
        while (this.match("op", "*") || this.match("op", "/") || this.match("op", "&")) {
            this.parseUnary();
        }
    }

    parseUnary() {
        if (isUnaryOperator(this.current())) {
            this.advance();
            this.parseUnary();
            return;
        }
        this.parsePrimary();
    }

    parsePrimary() {
        const token = this.current();

        if (this.match("number")) {
            return;
        }

        if (this.match("char") || this.match("string")) {
            return;
        }

        if (this.match("ident")) {
            const nameToken = this.previous();
            if (this.match("op", "(")) {
                let argCount = 0;
                if (!this.check("op", ")")) {
                    while (!this.check("eof") && !this.check("op", ")")) {
                        this.parseExpression();
                        argCount += 1;
                        if (this.match("op", ",")) {
                            continue;
                        }
                        break;
                    }
                }
                this.expect("op", ")", "expected ')' after call arguments");
                const signature = this.resolveFunction(nameToken.value);
                if (!signature) {
                    this.addError(nameToken, `unknown function ${nameToken.value}`);
                } else if (signature.params !== argCount) {
                    this.addError(nameToken, `${nameToken.value} expects ${signature.params} arguments, got ${argCount}`);
                }
                return;
            }

            if (!this.resolveVariable(nameToken.value)) {
                this.addError(nameToken, `unknown variable ${nameToken.value}`);
            }

            while (this.match("op", "[")) {
                this.parseExpression();
                this.expect("op", "]", "expected ']' after index expression");
            }
            return;
        }

        if (this.match("op", "(")) {
            this.parseExpression();
            this.expect("op", ")", "expected ')' after grouped expression");
            return;
        }

        if (token.kind !== "eof") {
            this.addError(token, `unexpected token ${token.value || token.kind}`);
            this.advance();
        }
    }

    synchronizeToStatementEnd() {
        while (!this.check("eof")) {
            if (this.check("op", ";")) {
                this.advance();
                return;
            }
            if (this.check("op", "}")) {
                return;
            }
            this.advance();
        }
    }
}

function validateDocument(document, collection) {
    const tokens = tokenize(document.getText());
    const symbols = collectTranslationUnitSymbols(document);
    const validator = new FoxCValidator(tokens, symbols, !isHeaderDocument(document));
    const diagnostics = [...symbols.diagnostics, ...validator.parseProgram()];
    collection.set(document.uri, diagnostics);
}

function activate(context) {
    const diagnostics = vscode.languages.createDiagnosticCollection("foxc");
    const completionProvider = vscode.languages.registerCompletionItemProvider(
        { language: "foxc", scheme: "file" },
        {
            provideCompletionItems(document) {
                return [
                    ...createSnippetItems(),
                    ...createKeywordItems(),
                    ...createTypeItems(),
                    ...createBuiltinItems(),
                    ...createSymbolItems(collectDocumentSymbols(document))
                ];
            }
        },
        " ",
        "(",
        ","
    );

    const timers = new Map();

    function scheduleValidation(document) {
        if (document.languageId !== "foxc") {
            return;
        }
        const key = document.uri.toString();
        const existing = timers.get(key);
        if (existing) {
            clearTimeout(existing);
        }
        const timer = setTimeout(() => validateDocument(document, diagnostics), 150);
        timers.set(key, timer);
    }

    context.subscriptions.push(completionProvider, diagnostics);
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

    for (const document of vscode.workspace.textDocuments) {
        scheduleValidation(document);
    }

    const hoverProvider = vscode.languages.registerHoverProvider(
        { language: "foxc", scheme: "file" },
        {
            provideHover(document, position) {
                const wordRange = document.getWordRangeAtPosition(position, /[A-Za-z_][A-Za-z0-9_]*/);
                if (!wordRange) return null;
                const word = document.getText(wordRange);

                if (BUILTIN_SIGNATURES.has(word)) {
                    const sig = BUILTIN_SIGNATURES.get(word);
                    const md = new vscode.MarkdownString(`**\`${sig.detail}\`**\n\n${sig.doc}`);
                    return new vscode.Hover(md);
                }

                // Collect functions/globals from this document and all included headers.
                const symbols = collectTranslationUnitSymbols(document);
                const func = symbols.functions.get(word);
                if (func && func.source !== "builtin") {
                    const origin = func.source ? `  *(from \`${path.basename(func.source)}\`)*` : "";
                    const label = func.detail || `${word}(...) -> ${func.ret}`;
                    const kind = func.isDefinition ? (func.isPrototype ? "declared + defined" : "defined") : "declared in header";
                    return new vscode.Hover(new vscode.MarkdownString(`**\`${label}\`**  *(${kind})*${origin}`));
                }

                const globalInfo = symbols.globals.get(word);
                if (globalInfo) {
                    const origin = globalInfo.source ? `  *(from \`${path.basename(globalInfo.source)}\`)*` : "";
                    return new vscode.Hover(new vscode.MarkdownString(`**\`${word}: ${globalInfo.type}\`**${origin}`));
                }

                // Type hover
                const TYPE_HOVER = {
                    u8:   "`u8` — 8-bit unsigned integer. Packed two-per-word in arrays.",
                    u16:  "`u16` — 16-bit unsigned integer. Native word size.",
                    u32:  "`u32` — 32-bit unsigned integer. **Requires `--mode em2_am1`.** Occupies two 16-bit words.",
                    void: "`void` — no value; used as function return type."
                };
                if (TYPE_HOVER[word]) {
                    return new vscode.Hover(new vscode.MarkdownString(TYPE_HOVER[word]));
                }

                return null;
            }
        }
    );

    context.subscriptions.push(hoverProvider);
}

function deactivate() { }

module.exports = {
    activate,
    deactivate
};