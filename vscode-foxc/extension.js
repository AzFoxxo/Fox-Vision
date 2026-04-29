"use strict";

const vscode = require("vscode");

const TYPES = ["u8", "u16", "void"];
const KEYWORDS = ["if", "else", "while", "return"];
const BUILTINS = ["poke", "peek", "wait", "cyc", "vblank"];

const BUILTIN_SIGNATURES = new Map([
    ["poke", { ret: "void", params: 2 }],
    ["peek", { ret: "u16", params: 1 }],
    ["wait", { ret: "void", params: 1 }],
    ["cyc", { ret: "u16", params: 0 }],
    ["vblank", { ret: "void", params: 0 }]
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
    return TYPES.map((type) =>
        createCompletionItem(type, vscode.CompletionItemKind.Class, "FoxC type", `FoxC type: ${type}`)
    );
}

function createBuiltinItems() {
    return BUILTINS.map((name) => {
        const signature = BUILTIN_SIGNATURES.get(name);
        const documentation = signature
            ? `Builtin ${name}(${Array(signature.params).fill("u16").join(", ")}) -> ${signature.ret}`
            : `Builtin ${name}`;
        return createCompletionItem(name, vscode.CompletionItemKind.Function, "FoxC builtin", documentation);
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
        )
    ];
}

function collectDocumentSymbols(document) {
    const names = new Set();
    const declarationPattern = /\b(?:u8|u16|void)\s+([A-Za-z_][A-Za-z0-9_]*)\b/g;

    for (let line = 0; line < document.lineCount; line += 1) {
        const text = document.lineAt(line).text;
        declarationPattern.lastIndex = 0;
        let match = declarationPattern.exec(text);
        while (match) {
            names.add(match[1]);
            match = declarationPattern.exec(text);
        }
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
    return token.kind === "op" && ["-", "~"].includes(token.value);
}

function makeRange(token, endColumnOffset = 1) {
    const start = new vscode.Position(token.line - 1, token.column - 1);
    const end = new vscode.Position(token.line - 1, token.column - 1 + endColumnOffset);
    return new vscode.Range(start, end);
}

function makeDiagnostic(token, message, severity = vscode.DiagnosticSeverity.Error) {
    return new vscode.Diagnostic(makeRange(token, Math.max(1, String(token.value || "").length)), message, severity);
}

function collectTopLevelSymbols(tokens) {
    const globals = new Map();
    const functions = new Map();
    const diagnostics = [];

    for (const builtin of BUILTINS) {
        functions.set(builtin, BUILTIN_SIGNATURES.get(builtin));
    }

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

    function readParamCount() {
        let count = 0;
        let depth = 1;
        let sawType = false;

        while (index < tokens.length && depth > 0) {
            const token = advance();
            if (token.kind === "eof") {
                break;
            }
            if (isOpenParen(token)) {
                depth += 1;
                continue;
            }
            if (isCloseParen(token)) {
                depth -= 1;
                continue;
            }
            if (depth > 1) {
                continue;
            }
            if (isTypeToken(token)) {
                sawType = true;
                continue;
            }
            if (sawType && isIdentifierToken(token)) {
                count += 1;
                sawType = false;
                continue;
            }
            if (isComma(token)) {
                sawType = false;
            }
        }

        return count;
    }

    while (index < tokens.length) {
        const token = peek();
        if (token.kind === "eof") {
            break;
        }
        if (!isTypeToken(token)) {
            advance();
            continue;
        }

        const typeToken = advance();
        const nameToken = peek();
        if (!isIdentifierToken(nameToken)) {
            advance();
            continue;
        }

        advance();
        const nextToken = peek();
        if (isOpenParen(nextToken)) {
            advance();
            const paramCount = readParamCount();
            if (functions.has(nameToken.value)) {
                diagnostics.push(makeDiagnostic(nameToken, `duplicate function ${nameToken.value}`));
            } else {
                functions.set(nameToken.value, { ret: typeToken.value, params: paramCount });
            }
            if (isOpenBrace(peek())) {
                advance();
                skipBlock();
            }
            continue;
        }

        if (globals.has(nameToken.value)) {
            diagnostics.push(makeDiagnostic(nameToken, `duplicate global ${nameToken.value}`));
        } else {
            globals.set(nameToken.value, typeToken.value);
        }
        skipToSemicolon();
    }

    return { globals, functions, diagnostics };
}

class FoxCValidator {
    constructor(tokens, symbols) {
        this.tokens = tokens;
        this.symbols = symbols;
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
            if (!isTypeToken(this.current())) {
                this.addError(this.current(), "expected a top-level declaration");
                this.advance();
                continue;
            }

            const typeToken = this.advance();
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

        const mainSig = this.symbols.functions.get("main");
        if (!mainSig) {
            this.diagnostics.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), "missing required function main"));
        } else if (mainSig.ret !== "void" || mainSig.params !== 0) {
            this.diagnostics.push(new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), "main must have signature void main()"));
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
                const paramName = this.expect("ident", undefined, "expected a parameter name");
                if (paramType && paramName) {
                    params.push({ type: paramType.value, name: paramName.value });
                }
                if (this.match("op", ",")) {
                    continue;
                }
                break;
            }
        }

        this.expect("op", ")", "expected ')' after parameter list");
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
            const nameToken = this.expect("ident", undefined, "expected a variable name");
            if (!nameToken) {
                this.synchronizeToStatementEnd();
                return;
            }
            if (typeToken.value === "void") {
                this.addError(typeToken, `variable ${nameToken.value} cannot be void`);
            }
            this.declare(nameToken, typeToken.value);
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

        if (this.check("ident") && this.tokens[this.index + 1] && isAssign(this.tokens[this.index + 1])) {
            const nameToken = this.advance();
            this.advance();
            if (!this.resolveVariable(nameToken.value)) {
                this.addError(nameToken, `unknown variable ${nameToken.value}`);
            }
            this.parseExpression();
            if (!this.expect("op", ";", "expected ';' after assignment")) {
                this.synchronizeToStatementEnd();
            }
            return;
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
    const symbols = collectTopLevelSymbols(tokens);
    const validator = new FoxCValidator(tokens, symbols);
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
}

function deactivate() {}

module.exports = {
    activate,
    deactivate
};