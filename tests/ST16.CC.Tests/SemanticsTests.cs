using ST16.CC;
using Xunit;

namespace ST16.CC.Tests;

/// <summary>Semantic diagnostic tests (each asserts the reported message part).</summary>
public class SemanticsTests
{
    [Fact]
    public void MissingMain_IsAnError()
    {
        CcTestKit.ExpectCompileError("int foo()\n{\n    return 1;\n}\n", "main");
    }

    [Fact]
    public void ArgCountMismatch_Definition()
    {
        CcTestKit.ExpectCompileError(
            "int f(int a, int b)\n{\n    return a;\n}\n" +
            "int main()\n{\n    return f(1, 2, 3);\n}\n",
            "'f' expects 2 arguments, got 3");
    }

    [Fact]
    public void ArgCountMismatch_ProTOTYPE()
    {
        CcTestKit.ExpectCompileError(
            "int f(int a, int b);\n" +
            "int main()\n{\n    return f(1, 2, 3);\n}\n",
            "'f' expects 2 arguments, got 3");
    }

    [Fact]
    public void PutcharArgCount_IsChecked()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    putchar();\n    return 0;\n}\n",
            "'putchar' expects 1 argument, got 0");
    }

    [Fact]
    public void GetcharArgCount_IsChecked()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    getchar(1);\n    return 0;\n}\n",
            "'getchar' expects 0 arguments, got 1");
    }

    [Fact]
    public void UnsignedLiteralIntoInt_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    int x = 60000;\n    return 0;\n}\n",
            "does not fit in type 'int'");
    }

    [Fact]
    public void AssignmentOfUnsignedLiteralIntoInt_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    int x = 1;\n    x = 60000;\n    return 0;\n}\n",
            "does not fit in type 'int'");
    }

    [Fact]
    public void UnsupportedKeyword_Long_IsRejected()
    {
        CcTestKit.ExpectCompileError(
            "long x;\nint main()\n{\n    return 0;\n}\n",
            "not supported");
    }

    [Fact]
    public void MemberAccess_OnNonStruct_IsRejected()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    int *p;\n    return p->x;\n}\n",
            "not a struct or union");
    }

    [Fact]
    public void VoidFunctionMustNotReturnValue()
    {
        CcTestKit.ExpectCompileError(
            "void f()\n{\n    return 1;\n}\nint main()\n{\n    return 0;\n}\n",
            "'return' with a value");
    }

    [Fact]
    public void BreakOutsideLoop_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    break;\n    return 0;\n}\n",
            "break");
    }

    [Fact]
    public void ContinueOutsideLoop_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    continue;\n    return 0;\n}\n",
            "continue");
    }

    [Fact]
    public void GlobalRedeclaration_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int x;\nint x;\nint main()\n{\n    return 0;\n}\n",
            "already declared");
    }

    [Fact]
    public void LocalRedeclaration_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    int a;\n    int a;\n    return 0;\n}\n",
            "redeclares");
    }

    [Fact]
    public void AssignmentToConst_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    const int c = 5;\n    c = 6;\n    return 0;\n}\n",
            "const object");
    }

    [Fact]
    public void ArrayInitTooLong_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int main()\n{\n    int a[2] = { 1, 2, 3 };\n    return 0;\n}\n",
            "has 3 items but the array has 2");
    }

    [Fact]
    public void GlobalInitializerWithIdentifier_IsAnError()
    {
        CcTestKit.ExpectCompileError(
            "int a = 5;\nint b = a;\nint main()\n{\n    return 0;\n}\n",
            "constant expressions");
    }

    [Fact]
    public void NonLvalueAssignment_IsAnError()
    {
        // '1 = 2' — the left side is not an lvalue.
        var ex = RecordError("int main()\n{\n    1 = 2;\n    return 0;\n}\n");
        Assert.NotNull(ex);
    }

    [Fact]
    public void PrototypeOnlyCall_FailsAtAssemblyWithUndefinedLabel()
    {
        CcTestKit.ExpectCompileError(
            "int f(int a);\nint main()\n{\n    return f(1);\n}\n",
            "undefined label 'f'");
    }

    private static CCException? RecordError(string source)
    {
        try
        {
            CCCompiler.Compile(source, "test.c");
            return null;
        }
        catch (CCException ex)
        {
            return ex;
        }
    }
}
