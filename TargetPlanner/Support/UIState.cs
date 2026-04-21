namespace TargetPlanner.Support
{
    public class UIState
    {
        public bool DayChart     { get; set; } = true;
        public bool YearChart    { get; set; } = false;
        public bool OptimalChart { get; set; } = false;
        public bool DurationChart { get; set; } = false;
        public string TargetName { get; set; } = string.Empty;
    }
}
