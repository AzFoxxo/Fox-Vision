
namespace Fox16ASM
{
    struct Token
    {
        public object value;
        public TokenType type;

        public Token(object value, TokenType type)
        {
            this.value = value;
            this.type = type;
        }
    }
}