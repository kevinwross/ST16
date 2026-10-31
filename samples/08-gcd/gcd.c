// 08-gcd — loops + functions (Euclid's algorithm, small).
//
// gcd(48, 18): 48 % 18 = 12, 18 % 12 = 6, 12 % 6 = 0 → 6. The 'while'
// lowers to the same JZ/JMP idiom you see in the assembly samples.
//
// Expected console output: good + newline

int gcd(int a, int b)
{
    while (b != 0)
    {
        int t = a % b;
        a = b;
        b = t;
    }
    return a;
}

int main()
{
    int g = gcd(48, 18);
    if (g != 6)
        return 1;
    putchar('g');
    putchar('o');
    putchar('o');
    putchar('d');
    putchar('\n');
    return 0;
}
