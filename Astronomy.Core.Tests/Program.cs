using BenchmarkDotNet.Running;

namespace Astronomy.Core.Tests
{
    // Dual-mode entry point. `dotnet test` discovers and runs every xUnit [Fact] in this
    // assembly regardless of this Main; it only matters for `dotnet run -c Release`, which
    // invokes the BenchmarkDotNet switcher. Passing `-- --filter *` from the shell runs all
    // benchmarks; passing `-- --list tree` enumerates them; no args drops into the interactive
    // chooser. Release is mandatory for BenchmarkDotNet -- Debug numbers are misleading.
    internal static class Program
    {
        public static int Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            return 0;
        }
    }
}
