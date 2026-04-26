package foxc

type tokenKind int

const (
	tokEOF tokenKind = iota
	tokIdent
	tokNumber
	tokLParen
	tokRParen
	tokLBrace
	tokRBrace
	tokComma
	tokSemi
	tokAssign
	tokPlus
	tokMinus
	tokStar
	tokSlash
	tokAmp
	tokAndAnd
	tokOrOr
	tokEq
	tokNe
	tokLt
	tokGt
	tokLe
	tokGe
	tokType
	tokIf
	tokElse
	tokWhile
	tokReturn
)

type token struct {
	kind tokenKind
	text string
	line int
	col  int
}
