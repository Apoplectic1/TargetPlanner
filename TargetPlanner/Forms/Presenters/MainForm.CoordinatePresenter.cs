using System;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Coordinate-input concern: the four CoordinateInput.ValueChanged callbacks
    // (Lat / Lon / RA / Dec) plus the model→UI synchronization methods that push
    // mLocation / mSelection.SelectedSingle back into the inputs without firing
    // those callbacks recursively. Split out of MainForm.cs (PR-7.4b of the
    // architectural-review campaign).
    //
    // Same partial-class file-split rationale as SortPresenter: the four
    // callbacks orchestrate mLocation + mSelection + the four CoordinateInput
    // helpers + the ComboBox_SelectTarget text + mSyncingLocationUI flag.
    // Constructor-injecting all of that is heavier ceremony than the relocation.
    public partial class MainForm
    {
        // Coordinate-input callbacks (ValueChanged from CoordinateInput).
        //
        // Each of these fires only for user-driven edits (spinner tick, textbox edit, or
        // hemisphere checkbox flip). Programmatic writes via SetProgrammatic during
        // SyncLocationUIFromModel / SyncTargetUIFromModel suppress the event, so these
        // callbacks are only ever invoked with the helper's Magnitude / Positive already
        // reflecting the user's intent.
        //
        // Every location-side callback funnels into OnLocationEdited so the combo flips to
        // "Custom" on any geographic change; RA/Dec do not affect the location combo.
        private void OnLatitudeEdited(object sender, EventArgs e)
        {
            mLocation = mLocation.With(
                latitude: Math.Round(mLatitudeInput.Magnitude, 6),
                north:    mLatitudeInput.Positive);
            OnLocationEdited(sender, e);
        }

        private void OnLongitudeEdited(object sender, EventArgs e)
        {
            mLocation = mLocation.With(
                longitude: Math.Round(mLongitudeInput.Magnitude, 6),
                west:      mLongitudeInput.Positive);
            OnLocationEdited(sender, e);
        }

        private void OnRightAscensionEdited(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            Target t = mSelection.SelectedSingle ?? Target.Default;
            mSelection.SetSelectedSingle(
                t.With(name: ComboTextOrFallback(t.Name),
                       rightAscension: Math.Round(mRaInput.Magnitude, 6)));
        }

        private void OnDeclinationEdited(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            Target t = mSelection.SelectedSingle ?? Target.Default;
            mSelection.SetSelectedSingle(
                t.With(
                    name:        ComboTextOrFallback(t.Name),
                    declination: Math.Round(mDecInput.Magnitude, 6),
                    north:       mDecInput.Positive));
        }

        // Honor the user's typed name in ComboBox_SelectTarget when building a new
        // SelectedSingle from a spinner edit. Without this, OnVmSelectedSingleChanged
        // overwrites the combo's typed text with the prior target's name on every
        // RA/Dec edit -- making "type a new name + adjust RA/Dec + Add" impossible.
        // Falls back to the prior name when the combo is blank.
        private string ComboTextOrFallback(string fallback)
        {
            string typed = ComboBox_SelectTarget?.Text;
            return string.IsNullOrWhiteSpace(typed) ? fallback : typed;
        }

        // Push mLocation into the lat / lon / N / W / Horizon / Duration inputs. The two
        // CoordinateInput helpers handle all their own suppress-during-write plumbing; for
        // Horizon / Duration (single-spinner, not triple-bound) we unsubscribe/re-subscribe
        // around the write directly. mSyncingLocationUI still gates OnLocationEdited so the
        // sync itself doesn't flip the combo to "Custom".
        private void SyncLocationUIFromModel()
        {
            mSyncingLocationUI = true;
            try
            {
                mLatitudeInput.SetProgrammatic(mLocation.Latitude,  positive: mLocation.North);
                mLongitudeInput.SetProgrammatic(mLocation.Longitude, positive: mLocation.West);

                NumericUpDown_TargetFloor.ValueChanged    -= NumericUpDown_TargetFloor_ValueChanged;
                NumericUpDown_TargetDuration.ValueChanged -= NumericUpDown_TargetDuration_ValueChanged;
                NumericUpDown_LocalElevation.ValueChanged -= NumericUpDown_LocalElevation_ValueChanged;
                NumericUpDown_Extinction.ValueChanged     -= NumericUpDown_Extinction_ValueChanged;
                ComboBox_Bortle.SelectedIndexChanged      -= ComboBox_Bortle_SelectedIndexChanged;
                NumericUpDown_TimeZone.ValueChanged       -= NumericUpDown_TimeZone_ValueChanged;
                NumericUpDown_TargetFloor.Value    = ClampToRange(NumericUpDown_TargetFloor,    (decimal)mPlanningPreferences.TargetFloorDeg);
                NumericUpDown_TargetDuration.Value = ClampToRange(NumericUpDown_TargetDuration, (decimal)mPlanningPreferences.MinDuration.TotalHours);
                NumericUpDown_LocalElevation.Value = ClampToRange(NumericUpDown_LocalElevation, (decimal)mLocation.Elevation);
                NumericUpDown_Extinction.Value     = ClampToRange(NumericUpDown_Extinction,     (decimal)mLocation.ExtinctionK);
                int bortleIdx = mLocation.BortleClass - 1;
                if (bortleIdx >= 0 && bortleIdx < ComboBox_Bortle.Items.Count)
                    ComboBox_Bortle.SelectedIndex = bortleIdx;
                // TimeZone spinner mirrors the active TZ's BaseUtcOffset hours. The TZ on
                // Location was built as a no-DST custom zone (TimeZoneFromUtcOffsetHours),
                // so BaseUtcOffset is the canonical persisted value -- a round-trip
                // through the spinner shows the exact integer the user set.
                if (mLocation.TimeZoneInfo != null)
                    NumericUpDown_TimeZone.Value = ClampToRange(
                        NumericUpDown_TimeZone,
                        (decimal)mLocation.TimeZoneInfo.BaseUtcOffset.TotalHours);
                NumericUpDown_TargetFloor.ValueChanged    += NumericUpDown_TargetFloor_ValueChanged;
                NumericUpDown_TargetDuration.ValueChanged += NumericUpDown_TargetDuration_ValueChanged;
                NumericUpDown_LocalElevation.ValueChanged += NumericUpDown_LocalElevation_ValueChanged;
                NumericUpDown_Extinction.ValueChanged     += NumericUpDown_Extinction_ValueChanged;
                ComboBox_Bortle.SelectedIndexChanged      += ComboBox_Bortle_SelectedIndexChanged;
                NumericUpDown_TimeZone.ValueChanged       += NumericUpDown_TimeZone_ValueChanged;
            }
            finally { mSyncingLocationUI = false; }
        }

        // Push mSelection.SelectedSingle into the RA / Dec coordinate inputs. No equivalent guard flag because
        // OnRightAscensionEdited / OnDeclinationEdited don't have the combo-flip side effect
        // that SyncLocationUIFromModel has to suppress; SetProgrammatic already skips
        // ValueChanged.
        private void SyncTargetUIFromModel()
        {
            Target t = mSelection?.SelectedSingle;
            if (t == null) return;
            mRaInput.SetProgrammatic(t.RightAscension, positive: true);
            mDecInput.SetProgrammatic(t.Declination,   positive: t.North);
        }
    }
}
