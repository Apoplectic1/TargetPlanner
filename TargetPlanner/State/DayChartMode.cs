namespace TargetPlanner.State
{
    // Day-chart target-filter mode driven by the two radio buttons overlaid on
    // the plot area's top-left. Pure visibility filter -- both modes use the
    // same HD-overlay step shape (Tonight.Floor over Tonight.Start/End);
    // BestSession.For naturally returns the centered window for centered-
    // fittable targets and the wall-pushed window for the rest.
    //
    //   Floor   -- all fit-tonight targets (current behavior, default).
    //   Transit -- only targets whose strict transit-centered placement fits
    //              (NightFit.CenteredFloor.HasValue) -- corresponds to
    //              Sessions chart's "Symmetric" series.
    public enum DayChartMode
    {
        Floor,
        Transit,
    }
}
