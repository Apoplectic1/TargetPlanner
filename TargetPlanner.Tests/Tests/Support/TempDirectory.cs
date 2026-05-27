using System;
using System.IO;

namespace TargetPlanner.Tests.Tests.Support
{
    // IDisposable wrapper around Path.GetTempPath() + Guid. Tests use it to give
    // SettingsStore / LocalTargetStore / FilterLibrary a writable per-test directory
    // without touching %APPDATA%, then Dispose recursively cleans up regardless of
    // what the test wrote inside. Safe to dispose multiple times.
    public sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TPTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string FilePath(string fileName) =>
            System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup. Antivirus / locked-handle / readonly issues
                // during test teardown shouldn't fail the test itself; the temp dir
                // is GUID-scoped so collisions are impossible.
            }
        }
    }
}
