using TargetPlanner.Filters;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // Filter is the user-facing photographic-filter record. ToProfile() projects
    // the Lorentzian slice for the moon-clear gate -- name + wavelength + bandwidth
    // are deliberately dropped (the Lorentzian is wavelength-agnostic; CenterNm
    // feeds K-S brightness, BandwidthNm feeds future IS work). Structural equality
    // flows into HdmKey so a Lorentzian scrub triggers cache invalidation.
    public class FilterTests
    {
        private static Filter MakeH() =>
            new Filter("H", 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);

        [Fact]
        public void ToProfile_PreservesLorentzianFields()
        {
            Filter f = new Filter(
                Name: "Custom",
                SeparationDeg: 45.0,
                WidthDays: 7.0,
                RelaxEnabled: true,
                RelaxMinAltDeg: -10.0,
                RelaxMaxAltDeg: 3.0,
                RelaxScale: 1.5,
                CenterNm: 500.7,
                BandwidthNm: 3.0);
            var p = f.ToProfile();
            Assert.True(p.Enabled);
            Assert.Equal(45.0, p.SeparationDeg);
            Assert.Equal(7.0, p.WidthDays);
            Assert.True(p.RelaxEnabled);
            Assert.Equal(-10.0, p.RelaxMinAltDeg);
            Assert.Equal(3.0, p.RelaxMaxAltDeg);
            Assert.Equal(1.5, p.RelaxScale);
        }

        [Fact]
        public void ToProfile_AlwaysReturnsEnabledTrue()
        {
            // The Filter -> MoonAvoidanceProfile projection doesn't carry an
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
            Filter mut = orig with { SeparationDeg = 60.0 };
            Assert.Equal(60.0, mut.SeparationDeg);
            Assert.Equal(30.0, orig.SeparationDeg);
            Assert.Equal(orig.Name, mut.Name);
            Assert.Equal(orig.CenterNm, mut.CenterNm);
            Assert.NotEqual(orig, mut);
        }

        [Fact]
        public void Record_DifferentName_AreNotEqual()
        {
            Filter h = new Filter("H", 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);
            Filter o = new Filter("O", 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);
            Assert.NotEqual(h, o);
        }

        [Fact]
        public void Record_DifferentBandwidth_AreNotEqual()
        {
            // BandwidthNm is part of the record's structural-equality footprint --
            // a user-edited bandwidth on a builtin's name must trip cache invalidation
            // via HdmKey even though ToProfile() drops the field.
            Filter narrow = new Filter("H", 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);
            Filter wide   = new Filter("H", 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 7.0);
            Assert.NotEqual(narrow, wide);
        }
    }
}
