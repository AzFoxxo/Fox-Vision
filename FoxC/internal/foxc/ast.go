package foxc

type TypeKind int

const (
	TypeInvalid TypeKind = iota
	TypeU8
	TypeU16
	TypeVoid
)

func (t TypeKind) String() string {
	switch t {
	case TypeU8:
		return "u8"
	case TypeU16:
		return "u16"
	case TypeVoid:
		return "void"
	default:
		return "<invalid>"
	}
}

type Program struct {
	Globals []*VarDecl
	Funcs   []*FuncDecl
}

type Param struct {
	Name string
	Type TypeKind
}

type VarDecl struct {
	Name string
	Type TypeKind
	Init Expr
}

type FuncDecl struct {
	Name   string
	Ret    TypeKind
	Params []Param
	Body   []Stmt
}

type Stmt interface {
	stmtNode()
}

type Expr interface {
	exprNode()
}

type VarDeclStmt struct {
	Decl *VarDecl
}

func (*VarDeclStmt) stmtNode() {}

type AssignStmt struct {
	Name  string
	Value Expr
}

func (*AssignStmt) stmtNode() {}

type IfStmt struct {
	Cond Expr
	Then []Stmt
	Else []Stmt
}

func (*IfStmt) stmtNode() {}

type WhileStmt struct {
	Cond Expr
	Body []Stmt
}

func (*WhileStmt) stmtNode() {}

type ReturnStmt struct {
	Value Expr
}

func (*ReturnStmt) stmtNode() {}

type ExprStmt struct {
	Value Expr
}

func (*ExprStmt) stmtNode() {}

type IntLit struct {
	Value int
}

func (*IntLit) exprNode() {}

type IdentExpr struct {
	Name string
}

func (*IdentExpr) exprNode() {}

type UnaryExpr struct {
	Op string
	X  Expr
}

func (*UnaryExpr) exprNode() {}

type BinaryExpr struct {
	Op    string
	Left  Expr
	Right Expr
}

func (*BinaryExpr) exprNode() {}

type CallExpr struct {
	Callee string
	Args   []Expr
}

func (*CallExpr) exprNode() {}
