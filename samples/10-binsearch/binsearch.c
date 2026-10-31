// 10-binsearch — pointers (arrays), control flow, a "real" algorithm.
//
// Binary search over a sorted global array. Exercises the full
// if/else ladder, global-array indexing, and a function with two
// parameters (watch the caller store both argument slots).
//
// Expected console output: ok + newline

int a[8] = { 1, 3, 5, 7, 9, 11, 13, 15 };

// Index of x in a[0..n-1] (sorted), or -1 when absent.
int binsearch(int x, int n)
{
    int lo = 0;
    int hi = n - 1;
    while (lo <= hi)
    {
        int mid = (lo + hi) / 2;
        if (a[mid] == x)
            return mid;
        if (a[mid] < x)
            lo = mid + 1;
        else
            hi = mid - 1;
    }
    return -1;
}

int main()
{
    if (binsearch(11, 8) != 5)
        return 1;
    if (binsearch(4, 8) != -1)
        return 2;
    if (binsearch(1, 8) != 0)
        return 3;
    if (binsearch(15, 8) != 7)
        return 4;
    putchar('o');
    putchar('k');
    putchar('\n');
    return 0;
}
