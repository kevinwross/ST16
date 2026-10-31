// 07-factorial — recursion and function calls.
//
// factorial(6) = 720, printed digit by digit. In the debugger, follow a
// CALL: the argument is stored into the callee's frame slot before the
// CALL, and the return value comes back on top of the operand stack.
//
// Expected console output: 720 + newline

int factorial(int n)
{
    return n <= 1 ? 1 : n * factorial(n - 1);
}

int main()
{
    int f = factorial(6);
    if (f != 720)
        return 1;
    int d = f / 100;
    putchar('0' + d);
    d = f / 10 % 10;
    putchar('0' + d);
    putchar('0' + f % 10);
    putchar('\n');
    return 0;
}
