package foxc

type TypeKind int

const (
	TypeInvalid TypeKind = iota
	TypeU8
	TypeU16
	TypeVoid
)

// Type represents a possibly-pointer type. Ptr==0 means non-pointer.
type Type struct {
	Base TypeKind
	Ptr  int
}

func (t Type) String() string {
	s := "<invalid>"
	switch t.Base {
	case TypeU8:
		s = "u8"
	case TypeU16:
		s = "u16"
	case TypeVoid:
		s = "void"
	}
	for i := 0; i < t.Ptr; i++ {
		s += "*"
	}
	return s
}

type Program struct {
	Globals []*VarDecl
	Funcs   []*FuncDecl
}

type Param struct {
	Name string
	Type Type
}

type VarDecl struct {
	Name     string
	Type     Type
	IsArray  bool
	ArrayLen int
	Init     Expr
}

type FuncDecl struct {
	Name   string
	Ret    Type
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
	LHS   Expr
	Value Expr
}

func (*AssignStmt) stmtNode() {}

type AssignIndexStmt struct {
	Name  string
	Index Expr
	Value Expr
}

func (*AssignIndexStmt) stmtNode() {}

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

type IndexExpr struct {
	Name  string
	Index Expr
}

func (*IndexExpr) exprNode() {}

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
