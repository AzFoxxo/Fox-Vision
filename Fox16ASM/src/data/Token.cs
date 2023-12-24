
namespace Fox16ASM
{
    struct Token(object value, TokenType type)
    {
        public object value = value;
        public TokenType type = type;
    }
}