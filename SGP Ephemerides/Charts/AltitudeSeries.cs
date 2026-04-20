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
            NightWindow night = Astrometry.ComputeNight(locationClone);

            Series daySeries = MakeSeries(Target.Name, "Day", new Color());

            duskOffset = (night.AstronomicalDusk.Minute > 30.0) ? 0.0 : -1.0;
            DateTime start = night.AstronomicalDusk.AddHours(duskOffset).Date.AddHours(night.AstronomicalDusk.AddHours(duskOffset).Hour);

            dawnOffset = (night.AstronomicalDawn.Minute > 30.0) ? 2.0 : 1.0;
            DateTime stop = night.AstronomicalDawn.AddHours(dawnOffset).Date.AddHours(night.AstronomicalDawn.AddHours(dawnOffset).Hour);

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

        // Analytic per-day Year and Optimal values. A stellar target's altitude curve is a pure
        // sinusoid in hour angle, so on any connected time interval the max altitude is either at
        // upper transit (HA = 0, altitude = meridianAlt) or at an endpoint -- no minute scan
        // needed. Per day we do two GetAltitudeAzimuth calls (dusk, dawn) plus arithmetic,
        // instead of ~600 per day.
        private void BuildYearAndOptimalSeries()
        {
            Location.Location locationClone = Clone(Location);

            Series yearSeries    = MakeSeries(Target.Name, "Year",    new Color());
            Series optimalSeries = MakeSeries(Target.Name, "Optimal", new Color());

            double latSigned   = Location.North ?  Location.Latitude  : -Location.Latitude;
            double decSigned   = Target.North   ?  Target.Declination : -Target.Declination;
            double lonDegEast  = Location.West  ? -Location.Longitude :  Location.Longitude;
            double raHours     = Target.RightAscension;
            double horizonDeg  = Location.Horizon;
            double durationHrs = Location.Duration.TotalHours;

            double meridianAlt = Astrometry.MeridianAltitude(latSigned, decSigned);
            double haHorizon   = Astrometry.HourAngleAtAltitude(latSigned, decSigned, horizonDeg);

            const double SiderealHoursPerSolarDay = 24.06570982441908;

            DateTime startDay = DateTime.Now.AddDays(-DateTime.Now.Day);
            DateTime endDay   = startDay.AddYears(1);
            TimeSpan dayDelta = endDay.Subtract(startDay);

            for (int day = 0; day < dayDelta.TotalDays; day++)
            {
                locationClone.DateTime = startDay.AddDays(day);
                NightWindow night = Astrometry.ComputeNight(locationClone);

                if (night.AstronomicalDusk == DateTime.MinValue || night.AstronomicalDawn == DateTime.MinValue)
                {
                    DateTime sentinel = startDay.AddDays(day).AddHours(12);
                    yearSeries.Points.AddXY(sentinel, -90.0);
                    optimalSeries.Points.AddXY(sentinel, -90.0);
                    continue;
                }

                locationClone.DateTime = night.AstronomicalDusk;
                double altDusk = Astrometry.GetAltitudeAzimuth(Target, locationClone).Item1;

                locationClone.DateTime = night.AstronomicalDawn;
                double altDawn = Astrometry.GetAltitudeAzimuth(Target, locationClone).Item1;

                double lstDusk = Astrometry.LocalSiderealTime(night.AstronomicalDusk.ToUniversalTime(), lonDegEast);
                double lstDawn = Astrometry.LocalSiderealTime(night.AstronomicalDawn.ToUniversalTime(), lonDegEast);
                if (lstDawn < lstDusk) lstDawn += 24.0;

                bool transitInNight = false;
                for (int k = -1; k <= 1; k++)
                {
                    double t = raHours + 24.0 * k;
                    if (t >= lstDusk && t <= lstDawn) { transitInNight = true; break; }
                }

                double yearAlt = Math.Max(altDusk, altDawn);
                if (transitInNight && meridianAlt > yearAlt) yearAlt = meridianAlt;

                double optimalAlt = -90.0;

                if (double.IsPositiveInfinity(haHorizon))
                {
                    double lengthSolar = (lstDawn - lstDusk) * 24.0 / SiderealHoursPerSolarDay;
                    if (lengthSolar >= durationHrs)
                    {
                        optimalAlt = Math.Max(altDusk, altDawn);
                        if (transitInNight && meridianAlt > optimalAlt) optimalAlt = meridianAlt;
                    }
                }
                else if (!double.IsNaN(haHorizon))
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        double center  = raHours + 24.0 * k;
                        double ahStart = center - haHorizon;
                        double ahEnd   = center + haHorizon;
                        double s = Math.Max(lstDusk, ahStart);
                        double e = Math.Min(lstDawn, ahEnd);
                        if (s >= e) continue;

                        double lengthSolar = (e - s) * 24.0 / SiderealHoursPerSolarDay;
                        if (lengthSolar < durationHrs) continue;

                        double altAtStart = (s == lstDusk) ? altDusk : horizonDeg;
                        double altAtEnd   = (e == lstDawn) ? altDawn : horizonDeg;
                        bool transitInWindow = (center >= s && center <= e);
                        double windowMax = transitInWindow
                            ? meridianAlt
                            : Math.Max(altAtStart, altAtEnd);
                        if (windowMax > optimalAlt) optimalAlt = windowMax;
                    }
                }

                yearSeries.Points.AddXY(night.AstronomicalDawn.AddMinutes(-1), yearAlt);
                optimalSeries.Points.AddXY(night.AstronomicalDawn.AddMinutes(-1), optimalAlt);
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
            NightWindow night = Astrometry.ComputeNight(locationClone);

            duskOffset = (night.AstronomicalDusk.Minute > 30.0) ? 0.0 : -1.0;
            DateTime start = night.AstronomicalDusk.AddHours(duskOffset).Date.AddHours(night.AstronomicalDusk.AddHours(duskOffset).Hour);

            dawnOffset = (night.AstronomicalDawn.Minute > 30.0) ? 2.0 : 1.0;
            DateTime stop = night.AstronomicalDawn.AddHours(dawnOffset).Date.AddHours(night.AstronomicalDawn.AddHours(dawnOffset).Hour);

            Series moonSeries = MakeSeries("Moon", "Day",
                Color.FromArgb((int)(night.LunarIlluminationFraction * 250.0), 209, 209, 209));
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
