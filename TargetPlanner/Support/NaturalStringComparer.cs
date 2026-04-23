using System.Collections;
using System.Collections.Generic;

namespace TargetPlanner.Support
{
    /// <summary>
    /// Compares strings with embedded numeric runs as numbers rather than character-by-character,
    /// so e.g. "M2" sorts before "M10" (instead of after, which is what an ordinal compare gives).
    /// Non-numeric segments compare case-insensitively under the invariant culture.
    /// </summary>
    /// <remarks>
    /// Used to feed pre-sorted target names into <c>CheckedListBox_SelectedTargets</c> after
    /// disabling its built-in <see cref="System.Windows.Forms.CheckedListBox.Sorted"/> property
    /// (which would otherwise re-sort lexically and put "M10" before "M2"). NINA target names
    /// are mostly catalogue-style (M, NGC, IC, Sh2-) where catalogue numbers want numeric order.
    /// </remarks>
    public sealed class NaturalStringComparer : IComparer<string>, IComparer
    {
        /// <summary>Shared instance using ordinal-ignore-case for non-digit segments.</summary>
        public static readonly NaturalStringComparer OrdinalIgnoreCase = new NaturalStringComparer();

        private NaturalStringComparer() { }

        public int Compare(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    // Find the digit runs.
                    int aEnd = i;
                    while (aEnd < a.Length && char.IsDigit(a[aEnd])) aEnd++;
                    int bEnd = j;
                    while (bEnd < b.Length && char.IsDigit(b[bEnd])) bEnd++;

                    // Strip leading zeros so "007" and "7" compare as equal magnitudes.
                    int aStart = i;
                    while (aStart < aEnd - 1 && a[aStart] == '0') aStart++;
                    int bStart = j;
                    while (bStart < bEnd - 1 && b[bStart] == '0') bStart++;

                    int aLen = aEnd - aStart;
                    int bLen = bEnd - bStart;
                    if (aLen != bLen) return aLen - bLen;

                    for (int k = 0; k < aLen; k++)
                    {
                        int diff = a[aStart + k] - b[bStart + k];
                        if (diff != 0) return diff;
                    }

                    // Equal magnitudes; deterministic tiebreak by leading-zero count so "007" and
                    // "7" don't collide and the sort stays stable across calls.
                    int aLead = aStart - i;
                    int bLead = bStart - j;
                    if (aLead != bLead) return aLead - bLead;

                    i = aEnd;
                    j = bEnd;
                }
                else
                {
                    int cmp = char.ToUpperInvariant(a[i]) - char.ToUpperInvariant(b[j]);
                    if (cmp != 0) return cmp;
                    i++;
                    j++;
                }
            }

            return (a.Length - i) - (b.Length - j);
        }

        int IComparer.Compare(object x, object y) => Compare(x as string, y as string);
    }
}
