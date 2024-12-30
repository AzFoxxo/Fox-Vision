namespace Fox16Shared
{
    /*
    *   SourceDest enum stores the source and
    *   destination for the CPU when using the
    *   move instruction.
    */
    public enum SourceDest : ushort
    {
        Register,
        Memory,
        Immediate
    }
}