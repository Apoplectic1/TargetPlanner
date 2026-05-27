using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TargetPlanner.Caches;
using TargetPlanner.Tests.Tests.Support;
using Xunit;
using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Tests.Tests
{
    // CacheAxis<TKey, TVal> direct tests via synthetic key+value types (string ->
    // string) and TaskCompletionSource-controlled build delegates. Pins the
    // per-key dedupe, stale-publish discard, DrainAndReset, faulted-build cleanup,
    // PrepareAsync progress, and empty/null behaviour that the cache-contract.md
    // section "Threading and cancellation" promises. Reach into the internal type
    // via InternalsVisibleTo.
    public class CacheAxisTests
    {
        // Constructs an axis backed by a Location accessor that points at a mutable
        // field, so tests can simulate a SetLocationAsync swap by reassigning it.
        private sealed class AxisFixture
        {
            public readonly object Gate = new object();
            public Location CurrentLocation = TestLocations.PennsPark;
            public int BuildInvocations;
            public CacheAxis<string, string> Axis;

            public AxisFixture(Func<string, Location, Task<string>> build)
            {
                Axis = new CacheAxis<string, string>(
                    Gate,
                    () => CurrentLocation,
                    (k, l) =>
                    {
                        Interlocked.Increment(ref BuildInvocations);
                        return build(k, l);
                    });
            }
        }

        // -------- Per-key dedupe --------

        [Fact]
        public async Task GetOrBuildAsync_ConcurrentSameKey_BuildsOnce()
        {
            TaskCompletionSource<string> gate = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            AxisFixture f = new AxisFixture((k, l) => gate.Task);

            Task<string> a = f.Axis.GetOrBuildAsync("k");
            Task<string> b = f.Axis.GetOrBuildAsync("k");

            // Both calls return BEFORE the underlying build completes, and both
            // join the same in-flight task. Asserting reference equality on Task
            // is the cheapest proof: the second call short-circuited into the
            // in-flight dict.
            Assert.Same(a, b);

            gate.SetResult("value");
            string va = await a;
            string vb = await b;
            Assert.Equal("value", va);
            Assert.Same(va, vb);
            Assert.Equal(1, f.BuildInvocations);
        }

        [Fact]
        public async Task GetOrBuildAsync_AfterPublish_FastPathSkipsBuild()
        {
            AxisFixture f = new AxisFixture((k, l) => Task.FromResult("value"));

            await f.Axis.GetOrBuildAsync("k");
            await f.Axis.GetOrBuildAsync("k");
            await f.Axis.GetOrBuildAsync("k");

            // The published store satisfies the second and third calls
            // synchronously -- the build delegate only ran once.
            Assert.Equal(1, f.BuildInvocations);
        }

        // -------- Stale-publish discard --------

        [Fact]
        public async Task BuildAfterLocationSwap_IsDroppedAtPublish()
        {
            TaskCompletionSource<string> gate = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            AxisFixture f = new AxisFixture((k, l) => gate.Task);

            Task<string> task = f.Axis.GetOrBuildAsync("k");

            // Simulate a SetLocationAsync swap mid-build. The axis's
            // mCurrentLocation accessor reads f.CurrentLocation live, so the next
            // publish sees the new instance.
            f.CurrentLocation = TestLocations.Sydney;

            gate.SetResult("value-from-old-location");
            await task;

            // The build's value never lands in the store because the source
            // location is no longer current.
            Assert.Null(f.Axis.GetOrNull("k"));
            Assert.Equal(0, f.Axis.Count);
        }

        // -------- Faulted-build cleanup --------

        [Fact]
        public async Task BuildThatThrows_IsRemovedFromInFlight_NextCallStartsFresh()
        {
            int call = 0;
            // Async lambda so the throw lands as a Task fault rather than a
            // synchronous-prelude exception. Production build delegates
            // (ChartCacheStore.BuildEntryAsync etc.) are async methods whose
            // faults reach the catch block inside CacheAxis.RunBuildAsync after
            // the Task<TVal> is wired into mInFlight -- so DropOnFault has an
            // entry to remove. A synchronous-throw lambda runs RunBuildAsync's
            // catch before mInFlight gets the key, so DropOnFault is a no-op
            // and the next call would see a stale faulted task. Tests should
            // match the production async-fault shape.
            AxisFixture f = new AxisFixture(async (k, l) =>
            {
                int n = Interlocked.Increment(ref call);
                await Task.Yield();
                if (n == 1) throw new InvalidOperationException("first build fails");
                return "ok";
            });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => f.Axis.GetOrBuildAsync("k"));

            // After the faulted task is dropped from the in-flight dict, the
            // next call kicks a fresh build and succeeds.
            string value = await f.Axis.GetOrBuildAsync("k");
            Assert.Equal("ok", value);
            Assert.Equal(2, f.BuildInvocations);
        }

        // -------- DrainAndReset --------

        [Fact]
        public async Task DrainAndReset_ClearsStoreAndReturnsInFlightTasks()
        {
            TaskCompletionSource<string> gate = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            AxisFixture f = new AxisFixture((k, l) => gate.Task);

            // One published entry; one in-flight.
            // Pre-publish: bypass the gate with a finished result, then start an
            // in-flight build for a different key.
            AxisFixture other = new AxisFixture((k, l) => Task.FromResult("published"));
            await other.Axis.GetOrBuildAsync("published-key");
            Assert.Equal(1, other.Axis.Count);

            // In-flight builds for the test fixture.
            Task<string> inflight1 = f.Axis.GetOrBuildAsync("a");
            Task<string> inflight2 = f.Axis.GetOrBuildAsync("b");

            // Drain WITH a location swap to simulate ChartCacheStore.SetLocationAsync's
            // semantics. The stale-publish discard at TryPublish is gated by
            // ReferenceEquals(mCurrentLocation(), buildLocation); if the location
            // doesn't move, the orphan publish lands in whatever mStore dict is
            // current at publish time (the post-drain one) -- so the contract
            // invariant relies on the owning store swapping mLocation FIRST under
            // the same lock as DrainAndReset.
            List<Task<string>> drained;
            lock (f.Gate)
            {
                f.CurrentLocation = TestLocations.Sydney;
                drained = f.Axis.DrainAndReset();
            }

            // Store + in-flight dicts are empty after the drain.
            Assert.Equal(0, f.Axis.Count);
            Assert.Null(f.Axis.GetOrNull("a"));
            Assert.Null(f.Axis.GetOrNull("b"));

            // The drained list carries the two in-flight tasks (order is dict-
            // enumeration, not insertion -- compare as a set).
            Assert.Equal(2, drained.Count);
            Assert.Contains(inflight1, drained);
            Assert.Contains(inflight2, drained);

            // Complete the gate so the orphaned tasks settle; their TryPublish
            // checks fail (currentLocation != buildLocation now) and the values
            // are silently discarded.
            gate.SetResult("orphan");
            await Task.WhenAll(inflight1, inflight2);
            Assert.Equal(0, f.Axis.Count);
        }

        // -------- PrepareAsync --------

        [Fact]
        public async Task PrepareAsync_FansOutAndTicksProgressPerCompletion()
        {
            AxisFixture f = new AxisFixture((k, l) => Task.FromResult("v_" + k));

            int progressTicks = 0;
            IProgress<int> progress = new Progress<int>(_ =>
                Interlocked.Increment(ref progressTicks));

            await f.Axis.PrepareAsync(new[] { "a", "b", "c" }, progress);

            Assert.Equal("v_a", f.Axis.GetOrNull("a"));
            Assert.Equal("v_b", f.Axis.GetOrNull("b"));
            Assert.Equal("v_c", f.Axis.GetOrNull("c"));
            Assert.Equal(3, f.BuildInvocations);
            // Progress<T> marshalls via SynchronizationContext; in an xUnit test
            // without a UI context the callback runs on the threadpool. Either
            // way every completed build ticks once -- so the tick count must
            // converge to 3 by the time WhenAll returns. Give Progress<T>'s
            // continuation a brief window to run (it's ContinueWith-backed).
            for (int i = 0; i < 50 && progressTicks < 3; i++)
                await Task.Yield();
            Assert.Equal(3, progressTicks);
        }

        [Fact]
        public async Task PrepareAsync_SurfacesFaultsViaWhenAll()
        {
            AxisFixture f = new AxisFixture((k, l) =>
            {
                if (k == "bad") throw new InvalidOperationException("nope");
                return Task.FromResult("v_" + k);
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                f.Axis.PrepareAsync(new[] { "a", "bad", "c" }));

            // The non-faulted siblings still landed -- WhenAll throws but the
            // other builds completed their TryPublish path before WhenAll
            // surfaced the fault.
            Assert.Equal("v_a", f.Axis.GetOrNull("a"));
            Assert.Equal("v_c", f.Axis.GetOrNull("c"));
            Assert.Null(f.Axis.GetOrNull("bad"));
        }

        [Fact]
        public async Task PrepareAsync_NullKeys_NoOpReturnsCompletedTask()
        {
            AxisFixture f = new AxisFixture((k, l) => Task.FromResult("v"));

            await f.Axis.PrepareAsync(null);

            Assert.Equal(0, f.BuildInvocations);
            Assert.Equal(0, f.Axis.Count);
        }

        [Fact]
        public async Task PrepareAsync_EmptyKeys_NoOp()
        {
            AxisFixture f = new AxisFixture((k, l) => Task.FromResult("v"));

            await f.Axis.PrepareAsync(Array.Empty<string>());

            Assert.Equal(0, f.BuildInvocations);
            Assert.Equal(0, f.Axis.Count);
        }

        // -------- GetOrNull --------

        [Fact]
        public void GetOrNull_BeforeBuild_ReturnsNull()
        {
            AxisFixture f = new AxisFixture((k, l) => Task.FromResult("v"));
            Assert.Null(f.Axis.GetOrNull("never-built"));
            Assert.Equal(0, f.Axis.Count);
        }
    }
}
