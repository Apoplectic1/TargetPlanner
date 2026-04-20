using System;

namespace Astronomy.Core.Locations
{
    public class Location
    {
        public string Name { get; set; }
        private double _Latitude;
        public double Latitude
        {
            get { return _Latitude; }
            set
            {
                // Negative input means Southern hemisphere; positive leaves the North flag
                // to the checkbox (the UI feeds unsigned magnitudes via the spinners).
                if (value < 0.0) { _Latitude = -value; North = false; }
                else             { _Latitude =  value;                 }

                LatDegrees = Math.Truncate(_Latitude);
                LatMinutes = Math.Floor(60.0 * (_Latitude - LatDegrees));
                LatSeconds = 3600.0 * (_Latitude - LatDegrees - LatMinutes / 60.0);
            }
        }
        public double LatDegrees { get; private set; }
        public double LatMinutes { get; private set; }
        public double LatSeconds { get; private set; }
        public bool North { get; set; }


        private double _Longitude;
        public double Longitude
        {
            get { return _Longitude; }
            set
            {
                // Negative input means Western hemisphere; positive leaves the West flag
                // to the checkbox (the UI feeds unsigned magnitudes via the spinners).
                if (value < 0.0) { _Longitude = -value; West = true; }
                else             { _Longitude =  value;              }

                LonDegrees = Math.Truncate(_Longitude);
                LonMinutes = Math.Floor(60.0 * (_Longitude - LonDegrees));
                LonSeconds = 3600.0 * (_Longitude - LonDegrees - LonMinutes / 60.0);
            }
        }
        public double LonDegrees { get; private set; }
        public double LonMinutes { get; private set; }
        public double LonSeconds { get; private set; }
        public bool West { get; set; }

        public double Horizon { get; set; }
        public double MinutesAboveHorizon { get { return Duration.TotalMinutes; } set { Duration = TimeSpan.FromMinutes(value); } }
        public TimeSpan Duration { get; set; }
        public DateTime DateTime { get; set; }
        public TimeZone TimeZone { get; set; }
        public bool DayChart { get; set; }
        public bool YearChart { get; set; }
        public bool OptimalChart { get; set; }

        public Location()
        {
            Name = "Penns Park";
            Latitude  = 40.282835;
            Longitude = 74.997369;
            North = true;
            West  = true;
            Horizon = 30;
            Duration = TimeSpan.FromMinutes(240);
            DateTime = DateTime.Now;
            TimeZone = TimeZone.CurrentTimeZone;
        }
    }
}
