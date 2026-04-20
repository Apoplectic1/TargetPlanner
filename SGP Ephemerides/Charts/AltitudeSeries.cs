using Newtonsoft.Json;
using SGP_Ephemerides.Support;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms.DataVisualization.Charting;

namespace SGP_Ephemerides.Charts
{
    public class AltitudeSeries
    {
        public Location.Location Location { get; set; }
        public Target.Target Target { get; set; }
        public List<Series> TargetSeriesList { get; private set; }

        public AltitudeSeries()
        {
            TargetSeriesList = new List<Series>();
        }

        public void ClearTargetList()
        {
            TargetSeriesList.Clear();
        }

        private static Series MakeSeries(string name, string seriesType, Color color) => new Series
        {
            Name = name + "-" + seriesType,
            Color = color,
            IsXValueIndexed = true,
            XValueType = ChartValueType.DateTime,
            ChartType = SeriesChartType.Line,
        };

        public async void BuildSeriesList()
        {
            BuildMoonSeries();
            BuildDaySeries();
            await Task.Run(() => BuildYearAndOptimalSeries());
        }

        private void BuildDaySeries()
        {
            DateTime point;
            Tuple<double, double> targetPosition;
            TimeSpan delta;
            int minutes;
            double duskOffset;
            double dawnOffset;

            Location.Location locationClone = Clone(Location);
            Support.Astrometry.Location(locationClone);

            Series daySeries = MakeSeries(Target.Name, "Day", new Color());

            duskOffset = (Astrometry.AstronomicalDusk.Minute > 30.0) ? 0.0 : -1.0;
            DateTime start = Astrometry.AstronomicalDusk.AddHours(duskOffset).Date.AddHours(Astrometry.AstronomicalDusk.AddHours(duskOffset).Hour);

            dawnOffset = (Astrometry.AstronomicalDawn.Minute > 30.0) ? 2.0 : 1.0;
            DateTime stop = Astrometry.AstronomicalDawn.AddHours(dawnOffset).Date.AddHours(Astrometry.AstronomicalDawn.AddHours(dawnOffset).Hour);

            delta = stop.Subtract(start);

            minutes = 0;
            while (minutes < Convert.ToInt32(Math.Round(delta.TotalMinutes, 0)))
            {
                point = start.AddMinutes(minutes);
                locationClone.DateTime = point;
                targetPosition = Astrometry.GetAltitudeAzimuth(Target, locationClone);
                daySeries.Points.AddXY(point, targetPosition.Item1);
                minutes++;
            }

            TargetSeriesList.Add(daySeries);
        }

        // One pass over the next year of nights produces both the "Year" series (max altitude
        // per night) and the "Optimal" series (altitude at the moment the target first clears
        // horizon, gated on the target being above horizon continuously for >= Duration).
        // Emitting both from the same day/minute loop halves the GetAltitudeAzimuth cost and
        // guarantees both series appear in TargetSeriesList at the same instant -- so the user
        // no longer has to visit the Year chart before the Optimal chart has populated.
        private void BuildYearAndOptimalSeries()
        {
            Location.Location locationClone = Clone(Location);

            Series yearSeries    = MakeSeries(Target.Name, "Year",    new Color());
            Series optimalSeries = MakeSeries(Target.Name, "Optimal", new Color());

            DateTime startDay = DateTime.Now.AddDays(-DateTime.Now.Day);
            DateTime endDay   = startDay.AddYears(1);
            TimeSpan dayDelta = endDay.Subtract(startDay);

            List<Tuple<DateTime, DateTime, double>> horizonCrossingList = new List<Tuple<DateTime, DateTime, double>>();

            for (int day = 0; day < dayDelta.TotalDays; day++)
            {
                locationClone.DateTime = startDay.AddDays(day);
                Support.Astrometry.Location(locationClone);

                DateTime startMinute = Astrometry.AstronomicalDusk;
                DateTime endMinute   = Astrometry.AstronomicalDawn;
                TimeSpan minuteDelta = endMinute.Subtract(startMinute);

                DateTime point = startMinute;
                DateTime aboveHorizonStartTime = startMinute;
                DateTime aboveHorizonStopTime  = startMinute;
                double   maxAltitude           = -90.0;
                double   aboveHorizonAltitude  = -90.0;
                bool     aboveHorizon          = false;

                for (int minute = 0; minute < minuteDelta.TotalMinutes; minute++)
                {
                    point = startMinute.AddMinutes(minute);
                    locationClone.DateTime = point;
                    Tuple<double, double> targetPosition = Astrometry.GetAltitudeAzimuth(Target, locationClone);
                    double alt = targetPosition.Item1;

                    if (alt > maxAltitude) maxAltitude = alt;

                    if (alt >= locationClone.Horizon && !aboveHorizon)
                    {
                        aboveHorizonAltitude  = alt;
                        aboveHorizonStartTime = point;
                        aboveHorizon = true;
                    }
                    else if (alt <= locationClone.Horizon && aboveHorizon)
                    {
                        aboveHorizonStopTime = point;
                        aboveHorizon = false;
                        horizonCrossingList.Add(Tuple.Create(aboveHorizonStartTime, aboveHorizonStopTime, aboveHorizonAltitude));
                    }
                }

                if (aboveHorizon)                                  // still above horizon at astronomical dawn
                {
                    aboveHorizonStopTime = point;
                    horizonCrossingList.Add(Tuple.Create(aboveHorizonStartTime, aboveHorizonStopTime, aboveHorizonAltitude));
                }

                double optimalAltitude        = locationClone.Horizon;
                double maxAboveHorizonMinutes = 0;
                foreach (var crossing in horizonCrossingList)
                {
                    TimeSpan crossingDelta = crossing.Item2.Subtract(crossing.Item1);
                    if (crossingDelta >= locationClone.Duration)   // above horizon long enough to be usable?
                    {
                        if (crossing.Item3 > optimalAltitude)             optimalAltitude        = crossing.Item3;
                        if (crossingDelta.TotalMinutes > maxAboveHorizonMinutes) maxAboveHorizonMinutes = crossingDelta.TotalMinutes;
                    }
                }
                horizonCrossingList.Clear();

                if (maxAboveHorizonMinutes <= 0) optimalAltitude = -90;

                yearSeries.Points.AddXY(endMinute.AddMinutes(-1), maxAltitude);
                optimalSeries.Points.AddXY(endMinute.AddMinutes(-1), optimalAltitude);
            }

            TargetSeriesList.Add(yearSeries);
            TargetSeriesList.Add(optimalSeries);
        }

        private void BuildMoonSeries()
        {
            CoordinateSharp.Celestial cCelestial;
            TimeSpan delta;
            int minutes;
            double duskOffset;
            double dawnOffset;
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(Location.DateTime);
            double LongitudeSign = Location.West ? -1.0 : 1.0;

            Location.Location locationClone = Clone(Location);
            Support.Astrometry.Location(locationClone);

            duskOffset = (Astrometry.AstronomicalDusk.Minute > 30.0) ? 0.0 : -1.0;
            DateTime start = Astrometry.AstronomicalDusk.AddHours(duskOffset).Date.AddHours(Astrometry.AstronomicalDusk.AddHours(duskOffset).Hour);

            dawnOffset = (Astrometry.AstronomicalDawn.Minute > 30.0) ? 2.0 : 1.0;
            DateTime stop = Astrometry.AstronomicalDawn.AddHours(dawnOffset).Date.AddHours(Astrometry.AstronomicalDawn.AddHours(dawnOffset).Hour);

            Series moonSeries = MakeSeries("Moon", "Day",
                Color.FromArgb((int)(Astrometry.LunarIlluminationFraction * 250.0), 209, 209, 209));
            moonSeries.ChartType = SeriesChartType.Area;
            moonSeries.IsVisibleInLegend = false;

            delta = stop.Subtract(start);

            minutes = 0;
            while (minutes < Convert.ToInt32(Math.Round(delta.TotalMinutes, 0)))
            {
                DateTime point = start.AddMinutes(minutes);
                cCelestial = CoordinateSharp.Celestial.CalculateCelestialTimes(locationClone.Latitude, LongitudeSign * locationClone.Longitude, point, utcOffset.Hours);
                moonSeries.Points.AddXY(point, cCelestial.MoonAltitude);

                minutes++;
            }

            TargetSeriesList.Add(moonSeries);
        }

        public static T Clone<T>(T source)
        {
            var serialized = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<T>(serialized);
        }
    }
}
