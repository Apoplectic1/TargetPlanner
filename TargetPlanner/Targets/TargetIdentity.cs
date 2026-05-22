using System;
using System.Collections.Generic;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Targets
{
    // The single, shared definition of "the same imaging target" used across TP.
    // Both the recursive target scanner (collapsing the per-file rows a load
    // produces) and the CheckedListBox_SelectedTargets duplicate-row tint route
    // through here, so "duplicate" means exactly one thing app-wide.
    //
    // Two records are the same target when their object names match -- after the
    // imaging-only " Stars" designation is stripped -- AND their coordinates fall
    // within ~1 arcminute. The name keeps coordinate-close mosaic panels
    // ("M101 P1" vs "M101 P2") apart; the coordinate test is the safety net. A
    // stars frame and its light frame share both, so they collapse
    // ("M101 P1 Stars" + "M101 P1" -> one target "M101 P1").
    //
    // TP-only: Astronomy.Core carries no notion of target identity or duplicates.
    public static class TargetIdentity
    {
        // Two targets count as the same sky position when their angular
        // separation is within this many degrees (~1 arcminute). Wide enough to
        // absorb plate-solve drift between an object's frames; far tighter than
        // the gap between any two genuinely distinct deep-sky targets.
        private const double ToleranceDeg = 1.0 / 60.0;

        private const double DegToRad = Math.PI / 180.0;

        // Trailing token marking a short-exposure star-only capture. Stripped
        // before names are compared so "M101 Stars" resolves onto "M101".
        private const string StarsSuffix = " Stars";

        // Canonical target name: trimmed, with a trailing " Stars" removed. The
        // loaders stamp this onto every Target they build, so a stars frame and
        // its light frame arrive already sharing a name. Re-applied inside
        // AreSameTarget so hand-typed names (which skip the loaders) still
        // compare canonically.
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string trimmed = name.Trim();
            if (trimmed.EndsWith(StarsSuffix, StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - StarsSuffix.Length).Trim();
            return trimmed;
        }

        // True when a and b are the same imaging target: equal normalized names
        // (case-insensitive) AND coordinates within ToleranceDeg.
        public static bool AreSameTarget(Target a, Target b)
        {
            if (a == null || b == null) return false;
            if (!string.Equals(NormalizeName(a.Name), NormalizeName(b.Name),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return WithinTolerance(a, b);
        }

        // Picks the subset of <paramref name="candidates"/> that duplicate neither
        // anything in <paramref name="existing"/> nor an earlier-accepted
        // candidate. First occurrence wins; input order is preserved. This is the
        // "collapse the per-filter / stars / per-frame rows into one entry, and
        // skip what the list already holds" pass every load funnels through.
        public static List<Target> SelectNewTargets(
            IEnumerable<Target> candidates, IReadOnlyList<Target> existing)
        {
            var accepted = new List<Target>();
            if (candidates == null) return accepted;

            // Two targets can only match when their names do, so bucket by
            // normalized name and run the coordinate test only within a bucket --
            // O(n) tiny buckets instead of an O(n^2) sweep. Seeding the buckets
            // with the existing set screens candidates against it in the same pass.
            var byName = new Dictionary<string, List<Target>>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
            {
                foreach (Target e in existing)
                    if (e != null) Bucket(byName, e.Name).Add(e);
            }

            foreach (Target c in candidates)
            {
                if (c == null) continue;
                List<Target> bucket = Bucket(byName, c.Name);
                if (AnyWithinTolerance(c, bucket)) continue;
                bucket.Add(c);
                accepted.Add(c);
            }
            return accepted;
        }

        // The duplicate-set bucket for a (raw, un-normalized) name, created empty
        // on first use.
        private static List<Target> Bucket(
            Dictionary<string, List<Target>> byName, string rawName)
        {
            string key = NormalizeName(rawName);
            if (!byName.TryGetValue(key, out List<Target> bucket))
            {
                bucket = new List<Target>(1);
                byName[key] = bucket;
            }
            return bucket;
        }

        private static bool AnyWithinTolerance(Target t, List<Target> sameName)
        {
            for (int i = 0; i < sameName.Count; i++)
                if (WithinTolerance(t, sameName[i])) return true;
            return false;
        }

        // Angular-separation test. Declination is resolved to signed degrees; the
        // RA delta is wrapped across the 0h/24h seam and scaled by cos(mean dec)
        // so the comparison is true on-sky separation rather than coordinate
        // distance (RA degrees converge toward the poles).
        private static bool WithinTolerance(Target a, Target b)
        {
            double decA = a.North ? a.Declination : -a.Declination;
            double decB = b.North ? b.Declination : -b.Declination;
            double dDecDeg = decA - decB;

            // RA hours -> degrees, then wrapped into [-180, 180] so 23.99h vs
            // 0.01h reads as a small delta, not a near-full circle.
            double dRaDeg = (a.RightAscension - b.RightAscension) * 15.0;
            dRaDeg -= 360.0 * Math.Round(dRaDeg / 360.0);

            double meanDecRad = (decA + decB) * 0.5 * DegToRad;
            double dRaSkyDeg = dRaDeg * Math.Cos(meanDecRad);

            double sepDeg = Math.Sqrt(dDecDeg * dDecDeg + dRaSkyDeg * dRaSkyDeg);
            return sepDeg <= ToleranceDeg;
        }
    }
}
