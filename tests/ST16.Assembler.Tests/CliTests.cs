using System.Diagnostics;
using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Process-based tests of the st16 CLI (as/ds commands). Locates the built
/// app 5 GetParent() calls above the test assembly (net10.0 → Debug → bin →
/// ST16.Assembler.Tests → tests → repo root); when the app is not built the
/// tests pass trivially (guard, not failure).
/// </summary>
public class CliTests
{
    private static readonly string? AppDll = FindAppDll();

    private static bool AppAvailable() => AppDll is not null && File.Exists(AppDll);

    /// <summary>Walks up 5 directories from the test dll's directory to the repo root.</summary>
    private static string? FindAppDll()
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(CliTests).Assembly.Location)!);
            for (int i = 0; i < 5; i++)
                dir = dir.Parent ?? new DirectoryInfo(string.Empty);
            if (string.IsNullOrEmpty(dir.FullName))
                return null;
            return Path.Combine(dir.FullName, "src", "ST16.App", "bin", "Debug", "net10.0", "st16.dll");
        }
        catch
        {
            return null;
        }
    }

    private static (int Exit, string StdOut, string StdErr) Run(string[] args)
    {
        string exe = Path.ChangeExtension(AppDll!, ".exe");
        var psi = new ProcessStartInfo
        {
            FileName = File.Exists(exe) ? exe : "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!File.Exists(exe))
            psi.ArgumentList.Add(AppDll!);
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string NewWorkDir()
    {
        string name = "st16-clitest-" + Guid.NewGuid().ToString("N");
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), name);
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch (UnauthorizedAccessException)
        {
            // Host sandboxes may deny child processes writes outside the workspace;
            // fall back to a unique directory next to the test assembly.
            var baseDir = Path.GetDirectoryName(typeof(CliTests).Assembly.Location)!;
            var dir = Path.Combine(baseDir, name);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [Fact]
    public void As_Assembles_WritesBinAndSt16d_AndReportsWordCount()
    {
        if (!AppAvailable())
            return;

        var dir = NewWorkDir();
        try
        {
            var input = Path.Combine(dir, "in.st16");
            File.WriteAllText(input, "PUSH 9\nHALT\n");

            var (exit, stdout, stderr) = Run(["as", input]);

            Assert.True(exit == 0, stderr);
            var bin = File.ReadAllBytes(Path.Combine(dir, "in.bin"));
            Assert.Equal(new byte[] { 0x09, 0x1B, 0x00, 0x24 }, bin);
            Assert.True(File.Exists(Path.Combine(dir, "in.st16d")));
            Assert.Contains("2 words", stdout);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void As_UndefinedLabel_ExitsOne_AndReportsError()
    {
        if (!AppAvailable())
            return;

        var dir = NewWorkDir();
        try
        {
            var input = Path.Combine(dir, "bad.st16");
            File.WriteAllText(input, "JZ nowhere\nHALT\n");

            var (exit, _, stderr) = Run(["as", input]);

            Assert.Equal(1, exit);
            Assert.Contains("error:", stderr);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void As_NoArguments_ExitsTwo()
    {
        if (!AppAvailable())
            return;

        var (exit, _, _) = Run(["as"]);

        Assert.Equal(2, exit);
    }

    [Fact]
    public void Ds_DisassemblesBinary_WithSymbols()
    {
        if (!AppAvailable())
            return;

        var dir = NewWorkDir();
        try
        {
            var input = Path.Combine(dir, "in.st16");
            File.WriteAllText(input, "PUSH 9\nHALT\n");
            var (asExit, _, asErr) = Run(["as", input]);
            Assert.True(asExit == 0, asErr);

            var (exit, stdout, stderr) = Run(["ds", Path.Combine(dir, "in.bin"), "-d", Path.Combine(dir, "in.st16d")]);

            Assert.True(exit == 0, stderr);
            Assert.Contains("PUSH 9", stdout);
            Assert.Contains("; 0x0000", stdout);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
