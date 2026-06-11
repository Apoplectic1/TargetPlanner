// The shared diagnostics contract (Log / ScreenCapture / AppLogIdentity) is used across ~two dozen files; a
// single project-wide global using keeps the bare `Log.X` call sites unchanged after Support\Log.cs was retired
// in favour of Astronomy.Diagnostics. (TP's only global using — the codebase otherwise imports explicitly.)
global using Astronomy.Diagnostics;
