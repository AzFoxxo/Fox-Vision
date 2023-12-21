
namespace Fox16ASM
{
    struct Label
    {
        public ushort address;
        public string name;

        public Label(ushort address, string name)
        {
            this.address = address;
            this.name = name;
        }
    }
}