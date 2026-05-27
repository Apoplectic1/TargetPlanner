using Astronomy.Core.Horizons;
using TargetPlanner.Filters;
using TargetPlanner.State;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // HdmKey is the per-(target, H/D/M) cache key for the fits axis; its equality
    // matrix is load-bearing for cache invalidation. Every field that differs must
    // produce !Equals so the cache rebuilds; equal-field keys must be Equal so the
    // cache hits. LocalHorizon is reference-equality on purpose (avoids cache thrash
    // on the scalar case where SnapshotCurrent allocates a fresh ScalarHorizonProfile
    // each call -- HdmKey nulls those out and lets HorizonDeg differentiate).
    public class HdmKeyTests
    {
        private static Filter MakeFilter(string name = "H") =>
            new Filter(name, 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);

        private static HdmKey MakeKey(
            double horizonDeg = 30.0,
            long durationTicks = 1000L,
            Filter activeFilter = null,
            bool moonAvoidanceEnabled = true,
            IHorizonProfile localHorizon = null) => new HdmKey
            {
                HorizonDeg = horizonDeg,
                DurationTicks = durationTicks,
                ActiveFilter = activeFilter ?? MakeFilter(),
                MoonAvoidanceEnabled = moonAvoidanceEnabled,
                LocalHorizon = localHorizon,
            };

        [Fact]
        public void Equal_IdenticalFields_ReturnsTrue()
        {
            Filter f = MakeFilter();
            HdmKey a = MakeKey(activeFilter: f);
            HdmKey b = MakeKey(activeFilter: f);
            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Equal_DifferentHorizonDeg_ReturnsFalse()
        {
            Assert.NotEqual(MakeKey(horizonDeg: 30.0), MakeKey(horizonDeg: 31.0));
        }

        [Fact]
        public void Equal_DifferentDurationTicks_ReturnsFalse()
        {
            Assert.NotEqual(MakeKey(durationTicks: 1000L), MakeKey(durationTicks: 1001L));
        }

        [Fact]
        public void Equal_DifferentActiveFilter_ReturnsFalse()
        {
            Assert.NotEqual(MakeKey(activeFilter: MakeFilter("H")),
                            MakeKey(activeFilter: MakeFilter("O")));
        }

        [Fact]
        public void Equal_StructurallyEqualFilters_AreEqual()
        {
            // Filter is a record -- structural equality flows through HdmKey so a
            // re-constructed equivalent filter doesn't trip cache invalidation.
            Assert.Equal(MakeKey(activeFilter: MakeFilter("H")),
                         MakeKey(activeFilter: MakeFilter("H")));
        }

        [Fact]
        public void Equal_DifferentMoonAvoidanceEnabled_ReturnsFalse()
        {
            Assert.NotEqual(MakeKey(moonAvoidanceEnabled: true),
                            MakeKey(moonAvoidanceEnabled: false));
        }

        [Fact]
        public void Equal_LocalHorizonNullEachSide_ReturnsTrue()
        {
            Assert.Equal(MakeKey(localHorizon: null), MakeKey(localHorizon: null));
        }

        [Fact]
        public void Equal_LocalHorizonReferenceIdentical_ReturnsTrue()
        {
            IHorizonProfile p = new ScalarHorizonProfile(30.0);
            Assert.Equal(MakeKey(localHorizon: p), MakeKey(localHorizon: p));
        }

        [Fact]
        public void Equal_LocalHorizonReferenceDifferent_ReturnsFalse()
        {
            // Two ScalarHorizonProfile instances with identical altitude are still
            // !Equals -- HdmKey deliberately uses reference identity here, and TP
            // hands a single cached profile through each Snapshot so the form-side
            // mLocalHorizon's identity is the dedupe handle.
            IHorizonProfile p1 = new ScalarHorizonProfile(30.0);
            IHorizonProfile p2 = new ScalarHorizonProfile(30.0);
            Assert.NotEqual(MakeKey(localHorizon: p1), MakeKey(localHorizon: p2));
        }

        [Fact]
        public void Equal_NullVsNonNullLocalHorizon_ReturnsFalse()
        {
            Assert.NotEqual(MakeKey(localHorizon: null),
                            MakeKey(localHorizon: new ScalarHorizonProfile(30.0)));
        }

        [Fact]
        public void GetHashCode_IdenticalKeys_AreEqual()
        {
            Filter f = MakeFilter();
            Assert.Equal(MakeKey(activeFilter: f).GetHashCode(),
                         MakeKey(activeFilter: f).GetHashCode());
        }

        [Fact]
        public void GetHashCode_StableAcrossInvocations()
        {
            HdmKey k = MakeKey();
            int first = k.GetHashCode();
            int second = k.GetHashCode();
            Assert.Equal(first, second);
        }
    }
}
