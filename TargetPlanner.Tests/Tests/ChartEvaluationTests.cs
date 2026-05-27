using TargetPlanner.State;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // ChartEvaluation is the cache->coordinator post-Ensure DTO. BrightnessInputsChanged
    // is the only field consumed today (post-apply hook gates Sky's K-S re-walk on it).
    // EnsureWork / RenderWork drive progress sizing in the coordinator's bar-owner
    // closure.
    public class ChartEvaluationTests
    {
        [Fact]
        public void Construct_WithRequiredField_SucceedsAndDefaultsWorkToZero()
        {
            ChartEvaluation eval = new ChartEvaluation { BrightnessInputsChanged = true };
            Assert.True(eval.BrightnessInputsChanged);
            Assert.Equal(0, eval.EnsureWork);
            Assert.Equal(0, eval.RenderWork);
        }

        [Fact]
        public void Record_StructuralEquality_OnIdenticalFields()
        {
            ChartEvaluation a = new ChartEvaluation
            {
                BrightnessInputsChanged = true,
                EnsureWork = 10,
                RenderWork = 5,
            };
            ChartEvaluation b = new ChartEvaluation
            {
                BrightnessInputsChanged = true,
                EnsureWork = 10,
                RenderWork = 5,
            };
            Assert.Equal(a, b);
        }

        [Fact]
        public void Record_WithExpression_MutatesSingleField()
        {
            ChartEvaluation orig = new ChartEvaluation { BrightnessInputsChanged = false };
            ChartEvaluation mut = orig with { BrightnessInputsChanged = true };
            Assert.True(mut.BrightnessInputsChanged);
            Assert.False(orig.BrightnessInputsChanged);
        }
    }
}
