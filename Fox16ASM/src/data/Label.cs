
namespace Fox16ASM
{
    struct Label
    {
        public ushort line;
        public string name;

        public Label(ushort address, string name)
        {
            this.line = address;
            this.name = name;
        }
    }
}