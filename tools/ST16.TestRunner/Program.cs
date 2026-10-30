// In-process xunit runner (vstest's testhost cannot register a parent-exit
// callback in some sandboxed environments; this runner has no child process).
// Usage: dotnet run --project tools/ST16.TestRunner -- <test-assembly.dll> [...]
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

internal static class Program
{
    private sealed class Sink : IMessageSink, Xunit.IMessageSinkWithTypes
    {
        public int Passed, Failed, Skipped;
        public bool Debug => Environment.GetEnvironmentVariable("RUNNER_DEBUG") == "1";

        // RunTestCases is async void: RunAll returns before execution finishes,
        // so block on the assembly-finished message instead.
        public ManualResetEventSlim AssemblyFinished = new(false);
        public int AssemblyTestsRun = -1;

        /// <summary>Substring filter on DisplayName (empty = run all); TEST_FILTER env var.</summary>
        public string Filter { get; set; } = Environment.GetEnvironmentVariable("TEST_FILTER") ?? "";
        public bool FilterActive => Filter.Length > 0;

        public void Dispose() { }

        public bool OnMessage(IMessageSinkMessage message)
        {
            Handle(message);
            return true;
        }

        public bool OnMessageWithTypes(IMessageSinkMessage message, HashSet<string> types)
        {
            Handle(message);
            return true;
        }

        private void Handle(IMessageSinkMessage message)
        {
            switch (message)
            {
                case Xunit.Sdk.TestPassed p when FilterActive && !p.Test.DisplayName.Contains(Filter):
                    break;   // filtered out: not counted
                case Xunit.Sdk.TestPassed:
                    Passed++;
                    break;
                case Xunit.Sdk.TestSkipped s:
                    Skipped++;
                    Console.WriteLine($"  SKIP {s.Test.DisplayName} {s.Reason}");
                    break;
                case Xunit.Sdk.TestFailed f when FilterActive && !f.Test.DisplayName.Contains(Filter):
                    break;
                case Xunit.Sdk.TestFailed f:
                    Failed++;
                    Console.WriteLine($"  FAIL {f.Test.DisplayName}");
                    for (int i = 0; i < f.Messages.Length; i++)
                        Console.WriteLine($"       {f.ExceptionTypes[i]}: {f.Messages[i]}");
                    break;
                case Xunit.Sdk.TestAssemblyFinished af:
                    AssemblyTestsRun = af.TestsRun;
                    AssemblyFinished.Set();
                    break;
                case Xunit.Sdk.TestCollectionFinished cf:
                    if (cf.TestsRun == 0 || cf.TestsFailed > 0)
                        Console.WriteLine($"[collection] run={cf.TestsRun} failed={cf.TestsFailed} skipped={cf.TestsSkipped}");
                    break;
                default:
                    if (Debug)
                        Console.WriteLine($"[msg {message.GetType().Name}] " +
                                          (message is Xunit.Sdk.DiagnosticMessage d ? d.Message : ""));
                    break;
            }
        }
    }

    private sealed class DefaultOptions : ITestFrameworkDiscoveryOptions, ITestFrameworkExecutionOptions
    {
        private readonly Dictionary<string, object?> _values = new();

        public TValue GetValue<TValue>(string key) =>
            _values.TryGetValue(key, out object? v) && v is TValue t ? t : default!;

        public void SetValue<TValue>(string key, TValue value) => _values[key] = value;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: ST16.TestRunner <test-assembly.dll> [more.dll ...]");
            return 2;
        }

        // The test assembly (and its non-xunit dependencies) live in its own bin
        // directory; make them resolvable from this process's default load context.
        var searchDirs = new List<string>();
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in searchDirs)
            {
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            return null;
        };

        int totalFailed = 0;
        foreach (string file in args)
        {
            string path = Path.GetFullPath(file);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"not found: {path}");
                return 2;
            }

            searchDirs.Add(Path.GetDirectoryName(path)!);
            Assembly testAssembly;
            try
            {
                testAssembly = Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"cannot load {path}: {ex.Message}");
                return 2;
            }

            Console.WriteLine($"=== {Path.GetFileName(path)}");
            var sink = new Sink();
            using (var framework = new Xunit.Sdk.XunitTestFramework(sink))
            {
                framework.GetExecutor(testAssembly.GetName()).RunAll(sink, new DefaultOptions(), new DefaultOptions());
                if (!sink.AssemblyFinished.Wait(TimeSpan.FromMinutes(10)))
                    Console.Error.WriteLine("timed out waiting for assembly finish");
            }
            if (sink.AssemblyTestsRun == 0)
                Console.Error.WriteLine("warning: no tests were executed in " + Path.GetFileName(path));
            Console.WriteLine($"{sink.Passed} passed, {sink.Failed} failed, {sink.Skipped} skipped");
            totalFailed += sink.Failed;
        }

        return totalFailed == 0 ? 0 : 1;
    }
}
