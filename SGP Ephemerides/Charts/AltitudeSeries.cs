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
        private Series mTargetSeries;

        public AltitudeSeries()
        {
            TargetSeriesList = new List<Series>();
        }

        public void jLocation(in Location.Location location) 
        { 
         
            
        }

        public void ClearTargetList()
        {
            TargetSeriesList.Clear();
        }

        private void NewSeries(string name, string seriesType, Color color)
        {
            mTargetSeries = new Series
            {
                Name = name + "-" + seriesType,
                Color = color,
                IsXValueIndexed = true,
                XValueType = ChartValueType.DateTime
            };
        }

        public async void BuildSeriesList()
        {
            BuildMoonSeries();
            BuildDaySeries();
            await Task.Run(() =>
            {
                BuildYearSeries();
            });
            await Task.Run(() =>
            {
                BuildYearOptimalSeries();
            });
        }

        public void BuildYearSeriesList()
        {
            BuildYearSeries();
        }

        private void BuildDaySeries()
        {
            DateTime point;
            Tuple<double, double> targetPosition;
            TimeSpan delta;
            int minutes;
            double duskOffset;
            double dawnOffset;

            Location.Location locationClone = new Location.Location();
            locationClone = Clone(Location);

            Support.Astrometry.Location(locationClone);

            point = new DateTime();
            NewSeries(Target.Name, "Day", new Color());

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
                mTargetSeries.Points.AddXY(point, targetPosition.Item1);
                minutes++;
            }

            mTargetSeries.ChartType = SeriesChartType.Line;
            TargetSeriesList.Add(mTargetSeries);
        }

        private void BuildYearSeries()
        {
            DateTime point;
            Tuple<double, double> targetPosition;
            TimeSpan dayDelta, minutedelta;
            DateTime startMinute, endMinute;
            int day, minute;
            double maxAltitude;

            Location.Location locationClone = new Location.Location();
            locationClone = Clone(Location);

            NewSeries(Target.Name, "Year", new Color());

            startMinute = new DateTime();
            endMinute = new DateTime();
            point = new DateTime();

            DateTime startDay = new DateTime();
            startDay = DateTime.Now.AddDays(-DateTime.Now.Day);

            DateTime endDay = new DateTime();
            endDay = DateTime.Now.AddDays(-DateTime.Now.Day).AddYears(1);


            dayDelta = endDay.Subtract(startDay);

            day = 0;
            while (day < dayDelta.TotalDays)
            {
                locationClone.DateTime = startDay.AddDays(day);

                Support.Astrometry.Location(locationClone);

                startMinute = Astrometry.AstronomicalDusk;
                endMinute = Astrometry.AstronomicalDawn;
                minutedelta = endMinute.Subtract(startMinute);

                minute = 0;
                maxAltitude = -90.0;
                while (minute < minutedelta.TotalMinutes)
                {
                    point = startMinute.AddMinutes(minute);
                    locationClone.DateTime = point;
                    targetPosition = Astrometry.GetAltitudeAzimuth(Target, locationClone);

                    maxAltitude = (targetPosition.Item1 < maxAltitude) ? maxAltitude : targetPosition.Item1;
                    minute += 1;
                }

                mTargetSeries.Points.AddXY(endMinute.AddMinutes(-1), maxAltitude);
                day++;
            }

            mTargetSeries.ChartType = SeriesChartType.Line;
            TargetSeriesList.Add(mTargetSeries);
        }

        private void BuildYearOptimalSeries()
        {
            DateTime point, startMinute, endMinute, aboveHorizonStartTime, aboveHorizonStopTime;
            List<Tuple<DateTime, DateTime, double>> horizonCrossingList = new List<Tuple<DateTime, DateTime, double>>();
            TimeSpan dayDelta, minutedelta, crossingDelta ;
            Tuple<double, double> targetPosition;
            bool aboveHorizon = false;
            double maxAltitude;
            double aboveHorizonAltitude;
            double maxAboveHorizonMinutes;
            int day, minute;

            Location.Location locationClone = new Location.Location();
            locationClone = Clone(Location);

            NewSeries(Target.Name, "Optimal", new Color());

            startMinute = new DateTime();
            endMinute = new DateTime();
            point = new DateTime();

            DateTime startDay = new DateTime();
            startDay = DateTime.Now.AddDays(-DateTime.Now.Day);

            DateTime endDay = new DateTime();
            endDay = DateTime.Now.AddDays(-DateTime.Now.Day).AddYears(1);


            dayDelta = endDay.Subtract(startDay);

            day = 0;
            while (day < dayDelta.TotalDays)
            {
                locationClone.DateTime = startDay.AddDays(day);

                Support.Astrometry.Location(locationClone);

                startMinute = Astrometry.AstronomicalDusk;
                endMinute = Astrometry.AstronomicalDawn;
                minutedelta = endMinute.Subtract(startMinute);

                aboveHorizonStartTime = startMinute;
                aboveHorizonStopTime = startMinute;

                minute = 0;
                maxAltitude = -90.0;
                aboveHorizon = false;
                aboveHorizonAltitude = -90.0;
                while (minute < minutedelta.TotalMinutes)
                {
                    point = startMinute.AddMinutes(minute);
                    locationClone.DateTime = point;
                    targetPosition = Astrometry.GetAltitudeAzimuth(Target, locationClone);

                    maxAltitude = (targetPosition.Item1 < maxAltitude) ? maxAltitude : targetPosition.Item1;

                    if ((targetPosition.Item1 >= locationClone.Horizon) && (aboveHorizon == false))  // Mark time target the first time it rises above horizon
                    {
                        aboveHorizonAltitude = targetPosition.Item1;
                        aboveHorizonStartTime = point;
                        aboveHorizon = true;
                    }

                    if ((targetPosition.Item1 <= locationClone.Horizon) && (aboveHorizon == true))  // Mark time target falls below horizon after being above horizon
                    {
                        aboveHorizonStopTime = point;
                        aboveHorizon = false;
                        horizonCrossingList.Add(Tuple.Create(aboveHorizonStartTime, aboveHorizonStopTime, aboveHorizonAltitude));
                    }

                    minute += 1;
                }

                if (aboveHorizon == true) // Target was above horizon at Astonimical Dawn
                {
                    aboveHorizonStopTime = point;
                    horizonCrossingList.Add(Tuple.Create(aboveHorizonStartTime, aboveHorizonStopTime, aboveHorizonAltitude));
                }

                // Deal with multiple above -> below's. Cheat: just take first time
                maxAboveHorizonMinutes = 0;
                aboveHorizonAltitude = locationClone.Horizon;
                foreach (var item in horizonCrossingList)
                {
                    crossingDelta = item.Item2.Subtract(item.Item1);
                    if (crossingDelta >= locationClone.Duration)   // Was target above horizon for at least the minimum Duration?
                    {
                        aboveHorizonAltitude   = (item.Item3 > aboveHorizonAltitude) ? item.Item3 : aboveHorizonAltitude;
                        maxAboveHorizonMinutes = (crossingDelta.TotalMinutes > maxAboveHorizonMinutes) ? crossingDelta.TotalMinutes : maxAboveHorizonMinutes;
                    }
                }
                horizonCrossingList.Clear();

                maxAltitude = (maxAboveHorizonMinutes > 0) ? maxAltitude : -90;
                aboveHorizonAltitude = (maxAboveHorizonMinutes > 0) ? aboveHorizonAltitude : -90;

                mTargetSeries.Points.AddXY(endMinute.AddMinutes(-1), aboveHorizonAltitude); // maxAltitude);
                day++;
            }

            mTargetSeries.ChartType = SeriesChartType.Line;
            TargetSeriesList.Add(mTargetSeries);
        }

        private void BuildMoonSeries()
        {
            CoordinateSharp.Celestial cCelestial;
            TimeSpan delta;
            int minutes;
            double duskOffset;
            double dawnOffset;
            double LongitudeSign;
            TimeSpan utcOffset;
            utcOffset = TimeZoneInfo.Local.GetUtcOffset(Location.DateTime);
            LongitudeSign = (Location.West) ? -1.0 : 1.0;

            Location.Location locationClone = new Location.Location();
            locationClone = Clone(Location);

            Support.Astrometry.Location(locationClone);

            duskOffset = (Astrometry.AstronomicalDusk.Minute > 30.0) ? 0.0 : -1.0;
            DateTime start = Astrometry.AstronomicalDusk.AddHours(duskOffset).Date.AddHours(Astrometry.AstronomicalDusk.AddHours(duskOffset).Hour);

            dawnOffset = (Astrometry.AstronomicalDawn.Minute > 30.0) ? 2.0 : 1.0;
            DateTime stop = Astrometry.AstronomicalDawn.AddHours(dawnOffset).Date.AddHours(Astrometry.AstronomicalDawn.AddHours(dawnOffset).Hour);

            NewSeries("Moon", "Day", Color.FromArgb((int)(Astrometry.LunarIlluminationFraction * 250.0), 209, 209, 209));

            delta = stop.Subtract(start);

            minutes = 0;
            while (minutes < Convert.ToInt32(Math.Round(delta.TotalMinutes, 0)))
            {
                DateTime point;

                point = start.AddMinutes(minutes);
                cCelestial = CoordinateSharp.Celestial.CalculateCelestialTimes(locationClone.Latitude, LongitudeSign * locationClone.Longitude, point, utcOffset.Hours);
                mTargetSeries.Points.AddXY(point, cCelestial.MoonAltitude);

                minutes++;
            }

            mTargetSeries.ChartType = SeriesChartType.Area;
            mTargetSeries.IsVisibleInLegend = false;
            TargetSeriesList.Add(mTargetSeries);
        }

        public static T Clone<T>(T source)
        {
            var serialized = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<T>(serialized);
        }
    }
}
