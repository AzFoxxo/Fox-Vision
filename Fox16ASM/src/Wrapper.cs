using System.Runtime.InteropServices;

class Program
{
    [DllImport("your_library_name.dll")]
    public static extern string get_value(int key);
}
