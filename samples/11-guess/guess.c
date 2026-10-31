// 11-guess — a console I/O loop: the smallest "game".
//
// The target is 7. Each keystroke is read from port 0xF001 with
// getchar(); digits are compared and answered with L (low), H (high),
// or Y (yes — the game ends). A 'q' or end-of-input also ends the game.
//
// Input "397" → 3 is low, 9 is high, 7 is right.
// Expected console output: LHY + newline (for that input).

int main()
{
    const int target = 7;
    int guesses = 0;
    int done = 0;
    while (!done)
    {
        int c = getchar();
        if (c == 0xFFFF || c == 'q')
            break;
        int d = c - '0';
        guesses++;
        if (d == target)
        {
            putchar('Y');
            done = 1;
        }
        else if (d < target)
        {
            putchar('L');
        }
        else
        {
            putchar('H');
        }
    }
    if (guesses != 0)
        putchar('\n');
    return 0;
}
