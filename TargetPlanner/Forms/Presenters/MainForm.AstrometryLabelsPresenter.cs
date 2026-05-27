using System;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Night;
using Astronomy.Core.Sun;
using TargetPlanner.Support;

namespace TargetPlanner
{
    // Astrometry-label refresh concern: compute dawn / dusk / sun-altitude /
    // moon-altitude / moon-phase / illumination / moon-rise / moon-set values
    // and push them to the corresponding labels. Pure render-to-label logic;
    // the chart pipeline owns its own astronomical computations -- this is
    // just the side panel mirroring the same astronomy. Lifted out of
    // MainForm.cs -- partial-class file split, same pattern as the other
    // presenter partials.
    public partial class MainForm
    {
        // Push every astrometry-derived label. Reads NightWindow from the cache
        // (single source of truth for dusk / dawn / illumination); falls back
        // to NightCalculator.ComputeNight when the cache is still cold (early
        // form-init before mCache is constructed). The five remaining values
        // (sun altitude, moon altitude / phase, moon rise / set) are computed
        // inline -- one-shot ~150 us of Meeus per call, cheap enough to fire on
        // every spinner tick. Called from UpdateLocalDateTimeEvents (date/time
        // scrubs), OnLocationEdited (lat/lon/N/W/elevation spinners),
        // ComboBox_Location_SelectionIndexChanged (preset picks), and the
        // coordinator's post-apply hook.
        private void RefreshAstrometryLabels()
        {
            DateTime utc = mObservation.Utc;
            TimeZoneInfo zone = mObservation.Zone;
            NightWindow night = mCache?.LocationNightCache?.Starting
                             ?? NightCalculator.ComputeNight(mLocation, utc);
            double latSigned = mLocation.LatSigned();
            double lonEast   = mLocation.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, mLocation.Elevation);

            double sunAlt = SunPosition.AltAzAt(mLocation, utc).Altitude;
            double moonAlt = AstroUtil.GetMoonAltitude(utc, observer);
            string moonPhase = AstroUtil.GetMoonPhaseName(utc);
            // Bracket-by-night so the displayed rise/set match the chart's
            // dusk->dawn window. GetMoonRiseAndSet (UTC-calendar-day search)
            // returned the prior local evening's set for non-UTC observers --
            // see Library AstroUtil.GetMoonRiseAndSetForNight remarks.
            RiseAndSetEvent moonRs = AstroUtil.GetMoonRiseAndSetForNight(
                night.AstronomicalDusk, night.AstronomicalDawn,
                latSigned, lonEast, mLocation.Elevation);

            Label_AstronomicalDuskValue.Text = FormatZoned(night.AstronomicalDusk, zone);
            Label_AstronomicalDawnValue.Text = FormatZoned(night.AstronomicalDawn, zone);
            Label_SunAltitudeValue.Text = sunAlt.ToString("F0") + "°";
            Label_LunarAltitudeValue.Text = moonAlt.ToString("F0") + "°";
            Label_LunarIlluminationFractionValue.Text = (night.LunarIlluminationFraction * 100).ToString("F0") + "%";
            Label_LunarPhaseValue.Text = moonPhase;
            Label_MoonRiseValue.Text = FormatZoned(moonRs.Rise, zone);
            Label_MoonSetValue.Text  = FormatZoned(moonRs.Set,  zone);
        }

        // Format a UTC instant as a wall-clock short time in the observer's zone.
        // "--:--" placeholder for the no-event case (polar summer Sun events;
        // moon below the horizon for the whole bracket-night search window) so
        // the label doesn't silently read "12:00 AM" from a MinValue sentinel.
        private static string FormatZoned(DateTime? utc, TimeZoneInfo zone)
            => utc.HasValue ? FormatZoned(utc.Value, zone) : "--:--";

        private static string FormatZoned(DateTime utc, TimeZoneInfo zone)
            => utc == DateTime.MinValue
                ? "--:--"
                : TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToShortTimeString();
    }
}
