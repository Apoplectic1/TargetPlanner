using System;
using TargetPlanner.Caches;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // DayWindowKey is the per-(target, single-night) altitude-curve cache key.
    // Equality is (ticks, count) -- cheap two-long compare. ChartStartUtc must
    // round-trip with DateTimeKind.Utc preserved so callers needing the DateTime
    // don't accidentally hand off an Unspecified instant that an AltAz.At-style
    // Library method would re-interpret as local time.
    public class DayWindowKeyTests
    {
        [Fact]
        public void Equal_IdenticalFields_ReturnsTrue()
        {
            DayWindowKey a = new DayWindowKey { ChartStartUtcTicks = 12345L, Count = 720 };
            DayWindowKey b = new DayWindowKey { ChartStartUtcTicks = 12345L, Count = 720 };
            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equal_DifferentTicks_ReturnsFalse()
        {
            DayWindowKey a = new DayWindowKey { ChartStartUtcTicks = 12345L, Count = 720 };
            DayWindowKey b = new DayWindowKey { ChartStartUtcTicks = 12346L, Count = 720 };
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Equal_DifferentCount_ReturnsFalse()
        {
            DayWindowKey a = new DayWindowKey { ChartStartUtcTicks = 12345L, Count = 720 };
            DayWindowKey b = new DayWindowKey { ChartStartUtcTicks = 12345L, Count = 721 };
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void ChartStartUtc_PreservesKindUtc()
        {
            DateTime instant = new DateTime(2026, 5, 27, 22, 0, 0, DateTimeKind.Utc);
            DayWindowKey k = new DayWindowKey
            {
                ChartStartUtcTicks = instant.Ticks,
                Count = 720,
            };
            Assert.Equal(DateTimeKind.Utc, k.ChartStartUtc.Kind);
            Assert.Equal(instant, k.ChartStartUtc);
        }

        [Fact]
        public void DefaultStruct_HasZeroFields()
        {
            // The cache uses default(DayWindowKey) (Count == 0) as the sentinel for
            // "no valid Day window" (polar night or empty-targets boot). Verify the
            // default-struct shape stays predictable.
            DayWindowKey d = default;
            Assert.Equal(0L, d.ChartStartUtcTicks);
            Assert.Equal(0, d.Count);
        }

        [Fact]
        public void Equal_DefaultStructAndExplicitZeros_AreEqual()
        {
            DayWindowKey d = default;
            DayWindowKey explicitZero = new DayWindowKey { ChartStartUtcTicks = 0L, Count = 0 };
            Assert.Equal(d, explicitZero);
        }
    }
}
