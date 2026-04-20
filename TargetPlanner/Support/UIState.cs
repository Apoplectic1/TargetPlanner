namespace TargetPlanner.Support
{
    public class UIState
    {
        //public Tuple<int, int> ChartLocation { get { return ChartLocation; } set { Tuple.Create(value.Item1, value.Item2); } }
        //public Tuple<int, int> ChartSize { get { return ChartSize; } set { Tuple.Create(value.Item1, value.Item2); } }
        public bool DayChart { get; set; } = true;
        public bool YearChart { get; set; } = false;
        public bool OptimalChart { get; set; } = false;
        public bool DurationChart { get; set; } = false;
        public string TargetName { get; set; } = string.Empty;

        public UIState()
        {
        }
    }
}
