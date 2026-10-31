// 09-stringrev — pointers, arrays, and strings.
//
// Reverses "abcde" in place with two pointers walking from the ends.
// Every *p / *q is a LOAD off an address; q++ / p-- are word-granular
// pointer arithmetic (one word per character in the ST16 profile).
//
// Expected console output: edcba + newline

char s[8] = "abcde";

int main()
{
    char *p = s;
    while (*p)
        p++;                  // walk to the NUL
    p--;
    char *q = s;
    while (q < p)
    {
        char t = *q;
        *q = *p;
        *p = t;
        q++;
        p--;
    }
    char *r = s;
    while (*r)
    {
        putchar(*r);
        r++;
    }
    putchar('\n');
    return 0;
}
