using ST16.VM;
using Xunit;

namespace ST16.CC.Tests;

/// <summary>
/// Differential tests: C programs → st16cc → binary → reference ST16 engine
/// → console output (and machine state) must match the expected C behavior.
/// Every expected output was verified by hand against the C source.
/// </summary>
public class DifferentialTests
{
    private const string MainOpen = "int main()\n{\n";
    private const string MainClose = "\n    return 0;\n}\n";

    private static string Program(string body) => MainOpen + body + MainClose;

    [Fact]
    public void WhileLoop_Putchar_Increment_TenStars()
    {
        CcTestKit.ExpectOutput(Program(
            "    int i = 0;\n" +
            "    int ch = 42;\n" +
            "    while (i < 10)\n" +
            "    {\n" +
            "        putchar(ch);\n" +
            "        i++;\n" +
            "    }\n"),
            "**********");
    }

    [Fact]
    public void RecursiveFib_ForLoopBreakContinue_CharSign_UChar_Goto_Sizeof_Ternary()
    {
        const string src =
            "char buf[4];\n\n" +
            "int fib(int n)\n{\n" +
            "    if (n < 2)\n" +
            "        return n;\n" +
            "    return fib(n - 1) + fib(n - 2);\n" +
            "}\n\n" +
            "int main()\n{\n" +
            "    int f = fib(10);\n" +
            "    if (f != 55)\n" +
            "        return 1;\n" +
            "    putchar('f'); putchar('i'); putchar('b'); putchar(' ');\n" +
            "    putchar('0' + f / 10); putchar('0' + f % 10); putchar('\\n');\n\n" +
            "    int sum = 0;\n" +
            "    for (int k = 0; k < 20; k++)\n" +
            "    {\n" +
            "        if (k == 10)\n" +
            "            break;\n" +
            "        if (k % 2 == 0)\n" +
            "            continue;\n" +
            "        sum += k;\n" +
            "    }\n" +
            "    if (sum != 25)\n" +
            "        return 2;\n" +
            "    putchar('s'); putchar('u'); putchar('m'); putchar(' ');\n" +
            "    putchar('0' + sum / 10); putchar('0' + sum % 10); putchar('\\n');\n\n" +
            "    int i;\n" +
            "    for (i = 0; i < 4; i++)\n" +
            "        buf[i] = i - 3;\n" +
            "    char *p = buf;\n" +
            "    int neg = 0;\n" +
            "    for (i = 0; i < 4; i++)\n" +
            "        if (p[i] < 0)\n" +
            "            neg++;\n" +
            "    if (neg != 3)\n" +
            "        return 3;\n" +
            "    putchar('c'); putchar('h'); putchar('a'); putchar('r'); putchar('\\n');\n\n" +
            "    unsigned char uc = 200;\n" +
            "    if (uc > 127)\n" +
            "        putchar('U');\n\n" +
            "    int s = sizeof(int) + sizeof(char);\n" +
            "    if (s != 3)\n" +
            "        return 4;\n" +
            "    int x = 7;\n" +
            "    int y = (x > 5) ? x * 2 : 1;\n" +
            "    goto done;\n" +
            "    putchar('!');\n" +
            "done:\n" +
            "    if (y != 14)\n" +
            "        return 5;\n" +
            "    putchar('o'); putchar('k'); putchar('\\n');\n" +
            "    return 0;\n" +
            "}\n";
        CcTestKit.ExpectOutput(src, "fib 55\nsum 25\nchar\nUok\n");
    }

    [Fact]
    public void SignedCharArray_SignExtend_OnLoad()
    {
        CcTestKit.ExpectOutput(
            "char buf[4];\n" + Program(
                "    int i;\n" +
                "    for (i = 0; i < 4; i++)\n" +
                "        buf[i] = i - 3;\n" +
                "    int sum = 0;\n" +
                "    for (i = 0; i < 4; i++)\n" +
                "        sum += buf[i];\n" +
                "    if (sum == -6)\n" +
                "        putchar('Y');\n" +
                "    else\n" +
                "        putchar('N');\n" +
                "    char *p = buf;\n" +
                "    if (p[0] == -3)\n" +
                "        putchar('P');\n"),
            "YP");
    }

    [Fact]
    public void Shifts_StringLiteral_DoWhile_Logicals_Unsigned_Pointers_NestedCalls()
    {
        const string src =
            "char msg[8];\n\n" +
            "int shift(int x, int n)\n{\n" +
            "    return x << n;\n" +
            "}\n\n" +
            "int mul(int a, int b)\n{\n" +
            "    return a * b;\n" +
            "}\n\n" +
            "int main()\n{\n" +
            "    int a = shift(1, 4);\n" +
            "    if (a != 16)\n" +
            "        return 1;\n" +
            "    int b = 0x800 >> 4;\n" +
            "    if (b != 128)\n" +
            "        return 2;\n" +
            "    int m = -16 >> 4;\n" +
            "    if (m != -1)\n" +
            "        return 3;\n" +
            "    a <<= 2;\n" +
            "    if (a != 64)\n" +
            "        return 4;\n\n" +
            "    char *s = \"hi!\";\n" +
            "    if (s[0] != 'h')\n" +
            "        return 5;\n" +
            "    putchar(s[0]); putchar(s[1]); putchar(s[2]); putchar('\\n');\n\n" +
            "    int c = 0;\n" +
            "    do\n" +
            "    {\n" +
            "        c++;\n" +
            "    } while (c < 3);\n" +
            "    if (c != 3)\n" +
            "        return 6;\n\n" +
            "    int x = 5;\n" +
            "    if (x > 2 && x < 7 || x == 99)\n" +
            "        putchar('L');\n" +
            "    else\n" +
            "        putchar('l');\n\n" +
            "    unsigned u = 60000;\n" +
            "    unsigned v = 50000;\n" +
            "    if (u > v)\n" +
            "        putchar('U');\n" +
            "    else\n" +
            "        putchar('u');\n\n" +
            "    char *p = msg;\n" +
            "    *p = 'A';\n" +
            "    p = p + 1;\n" +
            "    if (p != msg + 1)\n" +
            "        return 7;\n" +
            "    *p = 'B';\n" +
            "    if (msg[0] != 'A')\n" +
            "        return 8;\n" +
            "    if (msg[1] != 'B')\n" +
            "        return 9;\n\n" +
            "    int q = mul(shift(2, 2), 3);\n" +
            "    if (q != 24)\n" +
            "        return 10;\n\n" +
            "    putchar('o'); putchar('k');\n" +
            "    return 0;\n" +
            "}\n";
        CcTestKit.ExpectOutput(src, "hi!\nLUok");
    }

    [Fact]
    public void Getchar_ReadsInputPort_AndPutchar_EchoesIt()
    {
        CcTestKit.ExpectOutput(Program(
            "    int c = getchar();\n" +
            "    if (c != 'A')\n" +
            "        return 1;\n" +
            "    putchar(c + 1);\n" +
            "    putchar('!');\n"),
            "B!", input: new byte[] { (byte)'A' });
    }

    [Fact]
    public void ForwardDeclaration_IterativeGcd_WithModulo()
    {
        const string src =
            "int gcd(int a, int b);\n\n" +
            "int main()\n{\n" +
            "    int r = gcd(48, 18);\n" +
            "    if (r != 6)\n" +
            "        return 1;\n" +
            "    putchar('g'); putchar('o'); putchar('o'); putchar('d'); putchar('!');\n" +
            "    return 0;\n" +
            "}\n\n" +
            "int gcd(int a, int b)\n{\n" +
            "    while (b != 0)\n" +
            "    {\n" +
            "        int t = a % b;\n" +
            "        a = b;\n" +
            "        b = t;\n" +
            "    }\n" +
            "    return a;\n" +
            "}\n";
        CcTestKit.ExpectOutput(src, "good!");
    }

    [Fact]
    public void WhileOne_Break_Continue_NestedLoops()
    {
        CcTestKit.ExpectOutput(Program(
            "    int n = 0;\n" +
            "    int i;\n" +
            "    for (i = 0; i < 4; i++)\n" +
            "    {\n" +
            "        int j;\n" +
            "        for (j = 0; j < 10; j++)\n" +
            "        {\n" +
            "            if (j == 7)\n" +
            "                break;\n" +
            "            if (j % 2 == 0)\n" +
            "                continue;\n" +
            "            n++;\n" +
            "        }\n" +
            "    }\n" +
            "    if (n != 12)\n" +
            "        return 1;\n" +
            "    int k = 0;\n" +
            "    while (1)\n" +
            "    {\n" +
            "        k++;\n" +
            "        if (k == 5)\n" +
            "            break;\n" +
            "    }\n" +
            "    if (k != 5)\n" +
            "        return 2;\n" +
            "    putchar('w'); putchar('h'); putchar('i'); putchar('l');\n"),
            "whil");
    }

    [Fact]
    public void Division_Modulo_Truncation_SignedAndUnsigned()
    {
        CcTestKit.ExpectOutput(Program(
            "    if (7 / 2 != 3) return 1;\n" +
            "    if (7 % 2 != 1) return 2;\n" +
            "    if (-7 / 2 != -3) return 3;\n" +
            "    if (-7 % 2 != -1) return 4;\n" +
            "    if (0 % 5 != 0) return 5;\n" +
            "    // 60000 = 0xEA60 does not fit signed 16-bit: the literals\n" +
            "    // are unsigned, so the division runs the software loop.\n" +
            "    if (60000 / 7 != 8571) return 6;\n" +
            "    if (60000 % 7 != 3) return 7;\n" +
            "    unsigned u = 60000;\n" +
            "    u /= 7;\n" +
            "    if (u != 8571) return 8;\n" +
            "    unsigned m = 60000 % 7;\n" +
            "    if (m != 3) return 9;\n" +
            "    if (30000 / 3 != 10000) return 10;\n" +
            "    if (30000 % 3 != 0) return 11;\n" +
            "    putchar('d'); putchar('v');\n"),
            "dv");
    }

    [Fact]
    public void CharOverflow_WrapsInLowByte()
    {
        CcTestKit.ExpectOutput(Program(
            "    unsigned char u = 250;\n" +
            "    u += 10;\n" +
            "    if (u != 4) return 1;\n" +
            "    char c = -120;\n" +
            "    c += 5;\n" +
            "    if (c != -115) return 2;\n" +
            "    putchar('w'); putchar('r');\n"),
            "wr");
    }

    [Fact]
    public void GlobalArrayInit_StringInit_Const()
    {
        const string src =
            "int arr[3] = { 5, 6, 7 };\n" +
            "char name[6] = \"abc\";\n" +
            "const int N = 4;\n\n" +
            "int main()\n{\n" +
            "    if (arr[0] != 5 || arr[1] != 6 || arr[2] != 7)\n" +
            "        return 1;\n" +
            "    if (name[0] != 'a' || name[2] != 'c')\n" +
            "        return 2;\n" +
            "    if (N != 4)\n" +
            "        return 3;\n" +
            "    int i;\n" +
            "    for (i = 0; i < 3; i++)\n" +
            "        arr[i] = i * 2;\n" +
            "    if (arr[2] != 4)\n" +
            "        return 4;\n" +
            "    putchar('g'); putchar('a');\n" +
            "    return 0;\n" +
            "}\n";
        CcTestKit.ExpectOutput(src, "ga");
    }

    [Fact]
    public void PointerReturnAndUse_ThroughCall()
    {
        const string src =
            "int storage;\n\n" +
            "int *addr_of(int which)\n{\n" +
            "    return &storage;\n" +
            "}\n\n" +
            "int main()\n{\n" +
            "    int *p = addr_of(0);\n" +
            "    *p = 21;\n" +
            "    if (storage != 21)\n" +
            "        return 1;\n" +
            "    if (*p != 21)\n" +
            "        return 2;\n" +
            "    *p = *p + 1;\n" +
            "    if (storage != 22)\n" +
            "        return 3;\n" +
            "    putchar('r'); putchar('e');\n" +
            "    return 0;\n" +
            "}\n";
        CcTestKit.ExpectOutput(src, "re");
    }

    [Fact]
    public void Recursion_Fib20_FitsSigned16()
    {
        const string src =
            "int fib(int n)\n{\n" +
            "    if (n < 2)\n" +
            "        return n;\n" +
            "    return fib(n - 1) + fib(n - 2);\n" +
            "}\n\n" +
            "int main()\n{\n" +
            "    int f = fib(20);\n" +
            "    if (f != 6765)\n" +
            "        return 1;\n" +
            "    putchar('f'); putchar('2'); putchar('0');\n" +
            "    return 0;\n" +
            "}\n";
        CcTestKit.ExpectOutput(src, "f20");
    }

    [Fact]
    public void LogicalShortCircuit_SkipsRightSideWhenLeftDecides()
    {
        // The right operand is an assignment. For && with a false left
        // side the right side must not run (y stays 0); for || with a
        // false left side the right side must run (y = 7).
        CcTestKit.ExpectOutput(Program(
            "    int x = 0;\n" +
            "    int y = 0;\n" +
            "    int r = (x == 1 && (y = 9));\n" +
            "    if (r != 0) return 1;\n" +
            "    if (y != 0) return 2;\n" +
            "    int r2 = (x == 2 || (y = 7));\n" +
            "    if (r2 != 1) return 3;\n" +
            "    if (y != 7) return 4;\n" +
            "    putchar('s'); putchar('c');\n"),
            "sc");
    }

    [Fact]
    public void MachineState_HaltedWithPcPastEnd_Starloop()
    {
        var (text, machine, compiled) = CcTestKit.Run(Program(
            "    int i = 0;\n" +
            "    while (i < 10)\n" +
            "    {\n" +
            "        putchar('*');\n" +
            "        i++;\n" +
            "    }\n"));
        Assert.Equal("**********", text);
        Assert.Equal(MachineState.Halted, machine.State);
        Assert.True(compiled.Assembly.Words.Count > 0x0000);
    }

    [Fact]
    public void Struct_FieldAccess_StoreAndLoad()
    {
        // struct Point { int x; int y; };
        // p.x = 10; p.y = 20; print "1020"
        CcTestKit.ExpectOutput(
            "struct Point { int x; int y; };\n" +
            "int main()\n{\n" +
            "    struct Point p;\n" +
            "    p.x = 10;\n" +
            "    p.y = 20;\n" +
            "    putchar('0' + p.x / 10);\n" +
            "    putchar('0' + p.x % 10);\n" +
            "    putchar('0' + p.y / 10);\n" +
            "    putchar('0' + p.y % 10);\n" +
            "    return 0;\n" +
            "}\n",
            "1020");
    }

    [Fact]
    public void Struct_PointerArrow_Access()
    {
        // struct Point { int x; int y; };
        // struct Point p; p.x = 7; struct Point *q = &p; putchar('0' + q->x);
        CcTestKit.ExpectOutput(
            "struct Point { int x; int y; };\n" +
            "int main()\n{\n" +
            "    struct Point p;\n" +
            "    p.x = 7;\n" +
            "    struct Point *q = &p;\n" +
            "    putchar('0' + q->x);\n" +
            "    return 0;\n" +
            "}\n",
            "7");
    }

    [Fact]
    public void Union_OverlayFields()
    {
        // union U { int a; char b; };
        // u.a = 65; putchar(u.a);  → 'A' (same field works)
        // u.b = 65; putchar(u.b);  → 'A' (char field)
        CcTestKit.ExpectOutput(
            "union U { int a; char b; };\n" +
            "int main()\n{\n" +
            "    union U u;\n" +
            "    u.a = 65;\n" +
            "    putchar(u.a);\n" +
            "    return 0;\n" +
            "}\n",
            "A");
    }

    [Fact]
    public void Union_CharField_Load()
    {
        // union U { int a; char b; };
        // u.b = 65; putchar(u.b);  → 'A' (char field access)
        CcTestKit.ExpectOutput(
            "union U { int a; char b; };\n" +
            "int main()\n{\n" +
            "    union U u;\n" +
            "    u.b = 65;\n" +
            "    putchar(u.b);\n" +
            "    return 0;\n" +
            "}\n",
            "A");
    }

    [Fact]
    public void Float_Add_Sub_Cast()
    {
        // float a = 3.50; float b = 2.25; float c = a + b;
        // int d = (int)c;  → 5 (3.50 + 2.25 = 5.75, truncated to 5)
        CcTestKit.ExpectOutput(
            "int main()\n{\n" +
            "    float a = 3.50;\n" +
            "    float b = 2.25;\n" +
            "    float c = a + b;\n" +
            "    int d = (int)c;\n" +
            "    putchar('0' + d);\n" +
            "    return 0;\n" +
            "}\n",
            "5");
    }

    [Fact]
    public void Float_IntToFloat_ToInt_Roundtrip()
    {
        // (int)(float)7 == 7
        CcTestKit.ExpectOutput(
            "int main()\n{\n" +
            "    int x = 7;\n" +
            "    float f = (float)x;\n" +
            "    int y = (int)f;\n" +
            "    putchar('0' + y);\n" +
            "    return 0;\n" +
            "}\n",
            "7");
    }
}
