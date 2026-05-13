# `TargetPlanner.Imaging` — native / PCL isolation boundary

This namespace is the **only** place in TargetPlanner where:

- `unsafe` blocks are tolerated,
- P/Invoke or COM signatures live,
- types from `Astronomy.PCL` or `Astronomy.PCL.Native` are referenced directly,
- pointer arithmetic, span manipulation around native buffers, or anything else
  that needs `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` lives.

Everything that **leaves** this namespace must be a fully-managed DTO — plain
records / classes / structs over `string`, `double`, `int`, `byte[]`,
`IReadOnlyList<T>`, etc. No native handles, no pointers, no `IDisposable` that
wraps a leaked C-side resource without managed ownership.

## Why this exists

`TargetPlanner.csproj` declares `<AllowUnsafeBlocks>false</AllowUnsafeBlocks>`
as a structural assertion. The WinForms host should compile without unsafe
support even after `Astronomy.PCL` is referenced. When the first XISF /
PCL-backed consumer lands, that code goes here behind an isolation seam that
flips `AllowUnsafeBlocks=true` only for this one folder (via a nested
`Directory.Build.props` or per-file pragmas — TBD when the first consumer
arrives).

## When the first consumer arrives

Suggested shape:

```csharp
// TargetPlanner/Imaging/XisfImage.cs
namespace TargetPlanner.Imaging
{
    // Managed DTO exposed upward.
    public sealed record XisfImageMetadata(
        int Width, int Height, int BitsPerSample, string BayerPattern);

    public sealed record XisfImagePreview(
        ReadOnlyMemory<byte> RgbPixels, int Width, int Height);
}

// TargetPlanner/Imaging/Internal/XisfLoader.cs  (file-private to this namespace)
namespace TargetPlanner.Imaging.Internal
{
    internal static class XisfLoader
    {
        // unsafe entry; never escapes Imaging.
        internal static unsafe XisfImagePreview Load(string path) { ... }
    }
}
```

Consumers elsewhere in TP call into `TargetPlanner.Imaging` for the public
DTOs only. The compile-time `AllowUnsafeBlocks=false` on the WinForms host
project enforces the boundary.

## Don't propagate this pattern

This is a workaround for the WinForms + native-PCL combination. The future
NINA plugin is WPF and shouldn't carry the same isolation pattern unless its
own PCL story diverges.
