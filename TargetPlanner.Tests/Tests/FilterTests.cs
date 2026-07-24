using TargetPlanner.Filters;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // Filter is the user-facing photographic-filter record. ToProfile() projects
    // the K-S moon-gate slice (ToleranceMag + CenterNm) -- Name and BandwidthNm are
    // deliberately dropped (the gate is bandwidth-independent by construction,
    // Library assumption #24; BandwidthNm feeds the Sky chart's K-S walk).
    // Structural equality flows into HdmKey so a tolerance scrub triggers cache
    // invalidation.
    public class FilterTests
    {
        private static Filter MakeH() =>
            new Filter("H", 1.0, 656.3, 3.0);

        [Fact]
        public void ToProfile_PreservesGateFields()
        {
            Filter f = new Filter(
                Name: "Custom",
                ToleranceMag: 0.45,
                CenterNm: 500.7,
                BandwidthNm: 3.0);
            var p = f.ToProfile();
            Assert.True(p.Enabled);
            Assert.Equal(0.45, p.ToleranceMag);
            Assert.Equal(500.7, p.CenterNm);
        }

        [Fact]
        public void ToProfile_AlwaysReturnsEnabledTrue()
        {
            // The Filter -> MoonLimitProfile projection doesn't carry an
            // enabled flag; the master toggle lives on PlanningPolicy
            // (MoonAvoidanceEnabled) and short-circuits the projection upstream.
            // When the projection runs at all, Enabled is true.
            var p = MakeH().ToProfile();
            Assert.True(p.Enabled);
        }

        [Fact]
        public void Record_StructuralEquality_OnIdenticalFields()
        {
            Filter a = MakeH();
            Filter b = MakeH();
            Assert.Equal(a, b);
            Assert.False(ReferenceEquals(a, b));
        }

        [Fact]
        public void Record_WithExpression_MutatesSingleField()
        {
            Filter orig = MakeH();
            Filter mut = orig with { ToleranceMag = 0.5 };
            Assert.Equal(0.5, mut.ToleranceMag);
            Assert.Equal(1.0, orig.ToleranceMag);
            Assert.Equal(orig.Name, mut.Name);
            Assert.Equal(orig.CenterNm, mut.CenterNm);
            Assert.NotEqual(orig, mut);
        }

        [Fact]
        public void Record_DifferentName_AreNotEqual()
        {
            Filter h = new Filter("H", 1.0, 656.3, 3.0);
            Filter o = new Filter("O", 1.0, 656.3, 3.0);
            Assert.NotEqual(h, o);
        }

        [Fact]
        public void Record_DifferentBandwidth_AreNotEqual()
        {
            // BandwidthNm is part of the record's structural-equality footprint --
            // a user-edited bandwidth on a builtin's name must trip cache invalidation
            // via HdmKey even though ToProfile() drops the field.
            Filter narrow = new Filter("H", 1.0, 656.3, 3.0);
            Filter wide   = new Filter("H", 1.0, 656.3, 7.0);
            Assert.NotEqual(narrow, wide);
        }
    }
}
