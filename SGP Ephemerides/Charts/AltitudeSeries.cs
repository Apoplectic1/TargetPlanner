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
        // Per-day intermediates that do not depend on Horizon or Duration. Populated once by
        // BuildYearSeries and consumed on every Horizon/Duration spinner tick by
        // BuildOptimalSeries, so the rebuild path never re-enters ComputeNight (CoordinateSharp,
        // the actual hot path) or GetAltitudeAzimuth.
        private struct NightCacheEntry
        {
            public DateTime Dusk;
            public DateTime Dawn;
            public double   AltDusk;         // target altitude at Dusk, degrees
            public double   AltDawn;         // target altitude at Dawn, degrees
            public double   LstDusk;         // Local Sidereal Time at Dusk, hours
            public double   LstDawn;         // LST at Dawn, hours; linearized so LstDawn > LstDusk
            public bool     TransitInNight;  // does RA_hours (mod 24) fall in [LstDusk, LstDawn]?
            public double   YearAlt;         // max night altitude -- the Year series value
            public bool     IsPolar;         // night.Dusk/Dawn were DateTime.MinValue
            public DateTime SentinelX;       // X coordinate for Year and Optimal points on this day
        }

        public Location.Location Location { get; set; }
        public Target.Target Target { get; set; }
        public List<Series> TargetSeriesList { get; private set; }
        private List<NightCacheEntry> mYearCache;

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

        // Return the existing Series by name if present so repeat calls reuse the same Series
        // object (preserves chart binding); otherwise create and register a fresh one. This is
        // what makes BuildYearAndOptimalSeries safe to call more than once per AltitudeSeries
        // instance -- the Series identity survives rebuilds so mChart.Series keeps referencing
        // the points that just got repopulated.
        private Series FindOrCreateSeries(string name, string seriesType, Color color)
        {
            string fullName = name + "-" + seriesType;
            foreach (Series existing in TargetSeriesList)
            {
                if (existing.Name == fullName) return existing;
            }
            Series fresh = MakeSeries(name, seriesType, color);
            TargetSeriesList.Add(fresh);
            return fresh;
        }

        public async void BuildSeriesList()
        {
            // Each Target owns its AltitudeSeries, so a second build on the same Target (user
            // re-clicks Graph Ephemeride, or opens the multi-target popup after the main chart)
            // must start from a clean TargetSeriesList -- otherwise BuildMoonSeries and
            // BuildDaySeries, which unconditionally create fresh Series objects, would leave
            // duplicates next to the prior run. Year and Optimal are idempotent on their own via
            // FindOrCreateSeries, but clearing here keeps all four series on the same lifecycle.
            TargetSeriesList.Clear();

            BuildMoonSeries();
            BuildDaySeries();
            await Task.Run(() =>
            {
                BuildYearSeries();
                BuildOptimalSeries();
            });
        }

        // Rebuild only the Optimal series on Horizon or Duration change. Day, Moon, and Year
        // don't depend on either input, so they stay as-is. Reads mYearCache (populated by
        // BuildYearSeries during the initial build) instead of recomputing dusk/dawn/LSTs etc.
        // Cold-start path: if the cache hasn't been populated yet -- e.g., the user scrubbed a
        // spinner before the initial Task.Run completed -- build Year synchronously first.
        public void RebuildOptimalSeries()
        {
            if (mYearCache == null || mYearCache.Count == 0)
            {
                BuildYearSeries();
            }
            BuildOptimalSeries();
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

        // Compute-once per AltitudeSeries lifetime: walk 365 days, call ComputeNight and two
        // GetAltitudeAzimuth calls per day, store the Horizon/Duration-independent intermediates
        // in mYearCache, and emit the Year series. Year values are invariant to Horizon and
        // Duration so this never needs to run on a spinner tick.
        private void BuildYearSeries()
        {
            Location.Location locationClone = Clone(Location);

            Series yearSeries = FindOrCreateSeries(Target.Name, "Year", new Color());
            yearSeries.Points.Clear();

            double latSigned  = Location.North ?  Location.Latitude  : -Location.Latitude;
            double decSigned  = Target.North   ?  Target.Declination : -Target.Declination;
            double lonDegEast = Location.West  ? -Location.Longitude :  Location.Longitude;
            double raHours    = Target.RightAscension;

            double meridianAlt = Astrometry.MeridianAltitude(latSigned, decSigned);

            DateTime startDay = DateTime.Now.AddDays(-DateTime.Now.Day);
            DateTime endDay   = startDay.AddYears(1);
            int      totalDays = (int)endDay.Subtract(startDay).TotalDays;

            // Build into a local list and atomically assign at the end so a concurrent reader on
            // the UI thread (e.g. RebuildOptimalSeries during the initial Task.Run) sees either
            // null or a fully populated cache, never a half-filled one.
            List<NightCacheEntry> cache = new List<NightCacheEntry>(totalDays);

            for (int day = 0; day < totalDays; day++)
            {
                locationClone.DateTime = startDay.AddDays(day);
                NightWindow night = Astrometry.ComputeNight(locationClone);

                NightCacheEntry entry = new NightCacheEntry();

                if (night.AstronomicalDusk == DateTime.MinValue || night.AstronomicalDawn == DateTime.MinValue)
                {
                    entry.IsPolar   = true;
                    entry.SentinelX = startDay.AddDays(day).AddHours(12);
                    entry.YearAlt   = -90.0;
                    cache.Add(entry);
                    yearSeries.Points.AddXY(entry.SentinelX, entry.YearAlt);
                    continue;
                }

                entry.Dusk      = night.AstronomicalDusk;
                entry.Dawn      = night.AstronomicalDawn;
                entry.SentinelX = entry.Dawn.AddMinutes(-1);

                locationClone.DateTime = entry.Dusk;
                entry.AltDusk = Astrometry.GetAltitudeAzimuth(Target, locationClone).Item1;

                locationClone.DateTime = entry.Dawn;
                entry.AltDawn = Astrometry.GetAltitudeAzimuth(Target, locationClone).Item1;

                entry.LstDusk = Astrometry.LocalSiderealTime(entry.Dusk.ToUniversalTime(), lonDegEast);
                entry.LstDawn = Astrometry.LocalSiderealTime(entry.Dawn.ToUniversalTime(), lonDegEast);
                if (entry.LstDawn < entry.LstDusk) entry.LstDawn += 24.0;

                entry.TransitInNight = false;
                for (int k = -1; k <= 1; k++)
                {
                    double t = raHours + 24.0 * k;
                    if (t >= entry.LstDusk && t <= entry.LstDawn)
                    {
                        entry.TransitInNight = true;
                        break;
                    }
                }

                double yearAlt = Math.Max(entry.AltDusk, entry.AltDawn);
                if (entry.TransitInNight && meridianAlt > yearAlt) yearAlt = meridianAlt;
                entry.YearAlt = yearAlt;

                cache.Add(entry);
                yearSeries.Points.AddXY(entry.SentinelX, entry.YearAlt);
            }

            mYearCache = cache;
        }

        // Walk mYearCache to emit the Optimal series. Reads cached dusk/dawn altitudes, LSTs,
        // and the TransitInNight flag; no ComputeNight, no GetAltitudeAzimuth. The Year-as-upper-
        // bound pre-filter short-circuits every day where YearAlt is below the current horizon
        // (Year is the max possible altitude that night, so if it's below horizon the whole
        // k-loop is guaranteed to yield -90).
        private void BuildOptimalSeries()
        {
            Series optimalSeries = FindOrCreateSeries(Target.Name, "Optimal", new Color());
            optimalSeries.Points.Clear();

            double latSigned   = Location.North ?  Location.Latitude  : -Location.Latitude;
            double decSigned   = Target.North   ?  Target.Declination : -Target.Declination;
            double raHours     = Target.RightAscension;
            double horizonDeg  = Location.Horizon;
            double durationHrs = Location.Duration.TotalHours;

            double meridianAlt = Astrometry.MeridianAltitude(latSigned, decSigned);
            double haHorizon   = Astrometry.HourAngleAtAltitude(latSigned, decSigned, horizonDeg);

            const double SiderealHoursPerSolarDay = 24.06570982441908;

            foreach (NightCacheEntry entry in mYearCache)
            {
                if (entry.IsPolar || entry.YearAlt < horizonDeg)
                {
                    optimalSeries.Points.AddXY(entry.SentinelX, -90.0);
                    continue;
                }

                double optimalAlt = -90.0;

                if (double.IsPositiveInfinity(haHorizon))
                {
                    double lengthSolar = (entry.LstDawn - entry.LstDusk) * 24.0 / SiderealHoursPerSolarDay;
                    if (lengthSolar >= durationHrs)
                    {
                        optimalAlt = Math.Max(entry.AltDusk, entry.AltDawn);
                        if (entry.TransitInNight && meridianAlt > optimalAlt) optimalAlt = meridianAlt;
                    }
                }
                else
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        double center  = raHours + 24.0 * k;
                        double ahStart = center - haHorizon;
                        double ahEnd   = center + haHorizon;
                        double s = Math.Max(entry.LstDusk, ahStart);
                        double e = Math.Min(entry.LstDawn, ahEnd);
                        if (s >= e) continue;

                        double lengthSolar = (e - s) * 24.0 / SiderealHoursPerSolarDay;
                        if (lengthSolar < durationHrs) continue;

                        double altAtStart = (s == entry.LstDusk) ? entry.AltDusk : horizonDeg;
                        double altAtEnd   = (e == entry.LstDawn) ? entry.AltDawn : horizonDeg;
                        bool   transitInWindow = (center >= s && center <= e);
                        double windowMax = transitInWindow
                            ? meridianAlt
                            : Math.Max(altAtStart, altAtEnd);
                        if (windowMax > optimalAlt) optimalAlt = windowMax;
                    }
                }

                optimalSeries.Points.AddXY(entry.SentinelX, optimalAlt);
            }
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
