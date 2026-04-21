using System;
using System.Windows.Forms;

namespace TargetPlanner.Support
{
    // Helper encapsulating a triple-bound coordinate input: three NumericUpDown controls
    // (major / minutes / seconds), a TextBox showing the decimal magnitude, and an optional
    // CheckBox for the hemisphere flag. Replaces four near-identical 70-line handler pairs in
    // MainForm (Latitude / Longitude / RightAscension / Declination) with a single reusable
    // component. Addresses CODE_REVIEW P2-4.1, P2-4.2, P3-4.1.
    //
    // Responsibilities:
    //   * Keep the four surfaces (three spinners, one text box, optional checkbox) in sync
    //     without the old explicit unsubscribe/re-subscribe dance. A single mSuppress flag
    //     is raised before any internally-driven write and checked at the top of every
    //     handler.
    //   * Handle the 59.99 <-> 60.0 <-> 0.0 rollover between seconds / minutes / major, with
    //     the carry / borrow propagated across all three spinners.
    //   * Rewrite the magnitude-plus-flag invariant when the user types a negative decimal
    //     value: the magnitude becomes abs(value), the hemisphere checkbox flips.
    //   * Expose a single ValueChanged event that fires only for user-driven edits, so
    //     callers get "this input actually changed" signals without listening to 10+
    //     individual control events.
    //   * Expose SetProgrammatic(magnitude, positive) for model -> UI pushes without firing
    //     ValueChanged; the prior SyncLocationUIFromModel pattern becomes one call per helper.
    //
    // Contract:
    //   * Magnitude is always non-negative. The caller resolves the hemisphere:
    //       Lat:  location.With(latitude:  input.Magnitude, north: input.Positive)
    //       Lon:  location.With(longitude: input.Magnitude, west:  input.Positive)
    //       Dec:  target.With(declination: input.Magnitude, north: input.Positive)
    //       RA :  target.With(rightAscension: input.Magnitude)   // no hemisphere
    //   * hemisphere may be null (RA has no N/S/E/W toggle). Positive is always true when
    //     hemisphere is null, since there is no sign to record.
    //   * The caller owns maxMagnitude: 90 for latitude and declination, 180 for longitude,
    //     24 for RA. Values whose |v| exceeds the max are rejected (the UI keeps its prior
    //     state).
    public sealed class CoordinateInput : IDisposable
    {
        private readonly NumericUpDown mMajor;
        private readonly NumericUpDown mMinutes;
        private readonly NumericUpDown mSeconds;
        private readonly TextBox       mText;
        private readonly CheckBox      mHemisphere;
        private readonly double        mMaxMagnitude;
        private readonly int           mDecimals;
        private bool                   mSuppress;
        private bool                   mDisposed;

        // Decimal magnitude, always non-negative. Sign lives in Positive.
        public double Magnitude { get; private set; }

        // Hemisphere flag -- matches the backing checkbox's Checked state (true iff checked).
        // Meaning is caller-owned: for latitude, checked = North; for longitude, checked =
        // West; for declination, checked = North. Always true when hemisphere is null.
        public bool Positive { get; private set; }

        // Raised on user-driven change. Not raised by SetProgrammatic.
        public event EventHandler ValueChanged;

        public CoordinateInput(
            NumericUpDown major, NumericUpDown minutes, NumericUpDown seconds,
            TextBox text, CheckBox hemisphere,
            double maxMagnitude, int decimals = 6)
        {
            if (major    == null) throw new ArgumentNullException(nameof(major));
            if (minutes  == null) throw new ArgumentNullException(nameof(minutes));
            if (seconds  == null) throw new ArgumentNullException(nameof(seconds));
            if (text     == null) throw new ArgumentNullException(nameof(text));
            if (maxMagnitude <= 0.0) throw new ArgumentOutOfRangeException(nameof(maxMagnitude));
            if (decimals < 0)        throw new ArgumentOutOfRangeException(nameof(decimals));

            mMajor        = major;
            mMinutes      = minutes;
            mSeconds      = seconds;
            mText         = text;
            mHemisphere   = hemisphere;   // may be null
            mMaxMagnitude = maxMagnitude;
            mDecimals     = decimals;

            mMajor.ValueChanged   += OnNumericChanged;
            mMinutes.ValueChanged += OnNumericChanged;
            mSeconds.ValueChanged += OnNumericChanged;
            mText.TextChanged     += OnTextChanged;
            if (mHemisphere != null)
                mHemisphere.CheckedChanged += OnHemisphereChanged;

            // Seed state from whatever the controls currently hold. The Designer may have
            // set default spinner values; we record them so Magnitude/Positive are
            // self-consistent before any user interaction.
            Magnitude = ReadFromSpinners();
            Positive  = mHemisphere?.Checked ?? true;
        }

        // Push a model value into all bound controls without firing ValueChanged.
        public void SetProgrammatic(double magnitude, bool positive)
        {
            if (mDisposed) throw new ObjectDisposedException(nameof(CoordinateInput));

            magnitude = Math.Abs(magnitude);
            Magnitude = magnitude;
            Positive  = positive;

            mSuppress = true;
            try
            {
                WriteSpinners(magnitude);
                mText.Text = magnitude.ToString("F" + mDecimals);
                if (mHemisphere != null) mHemisphere.Checked = positive;
            }
            finally { mSuppress = false; }
        }

        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            mMajor.ValueChanged   -= OnNumericChanged;
            mMinutes.ValueChanged -= OnNumericChanged;
            mSeconds.ValueChanged -= OnNumericChanged;
            mText.TextChanged     -= OnTextChanged;
            if (mHemisphere != null)
                mHemisphere.CheckedChanged -= OnHemisphereChanged;
        }

        private void OnNumericChanged(object sender, EventArgs e)
        {
            if (mSuppress) return;

            HandleRollover();
            double v = ReadFromSpinners();
            if (v > mMaxMagnitude)
            {
                // Clamp back: WriteSpinners with the prior Magnitude reverts visually, and
                // OnNumericChanged re-enters but hits the v <= max branch the next time.
                mSuppress = true;
                try { WriteSpinners(mMaxMagnitude); }
                finally { mSuppress = false; }
                v = mMaxMagnitude;
            }
            Magnitude = v;

            mSuppress = true;
            try { mText.Text = v.ToString("F" + mDecimals); }
            finally { mSuppress = false; }

            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (mSuppress) return;
            if (!double.TryParse(mText.Text, out double v)) return;
            if (Math.Abs(v) > mMaxMagnitude) return;

            double magnitude = Math.Abs(v);
            bool typedNegative = v < 0.0;

            Magnitude = magnitude;

            mSuppress = true;
            try
            {
                WriteSpinners(magnitude);

                // If user typed a negative value and we have a hemisphere checkbox, flip it
                // (magnitude + flag convention). Re-render the text without the negative sign
                // so the on-screen value matches the stored magnitude.
                if (mHemisphere != null && typedNegative)
                {
                    mHemisphere.Checked = !mHemisphere.Checked;
                    Positive = mHemisphere.Checked;
                    mText.Text = magnitude.ToString("F" + mDecimals);
                }
                else
                {
                    Positive = mHemisphere?.Checked ?? true;
                }
            }
            finally { mSuppress = false; }

            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnHemisphereChanged(object sender, EventArgs e)
        {
            if (mSuppress) return;
            Positive = mHemisphere.Checked;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        // Mirror of the prior ScrollNumericLocationCounters body for one control group:
        // 60.0 -> 0 + carry; -0.01 -> 59.99 + borrow on seconds; 60.0 -> 0 + carry;
        // -1.0 -> 59 + borrow on minutes. Exact decimal equality is intentional -- only
        // reachable via a single step up from 59.99 (Increment=0.01) or down from 0.
        private void HandleRollover()
        {
            mSuppress = true;
            try
            {
                if (mSeconds.Value == 60.0m)
                {
                    mSeconds.Value = 0m;
                    mMinutes.Value = Clamp(mMinutes, mMinutes.Value + 1m);
                }
                else if (mSeconds.Value == -0.01m)
                {
                    mSeconds.Value = 59.99m;
                    mMinutes.Value = Clamp(mMinutes, mMinutes.Value - 1m);
                }

                if (mMinutes.Value == 60.0m)
                {
                    mMinutes.Value = 0m;
                    mMajor.Value   = Clamp(mMajor, mMajor.Value + 1m);
                }
                else if (mMinutes.Value == -1.0m)
                {
                    mMinutes.Value = 59m;
                    mMajor.Value   = Clamp(mMajor, mMajor.Value - 1m);
                }
            }
            finally { mSuppress = false; }
        }

        private double ReadFromSpinners() =>
              (double)mMajor.Value
            + (double)mMinutes.Value / 60.0
            + (double)mSeconds.Value / 3600.0;

        private void WriteSpinners(double magnitude)
        {
            double majorVal = Math.Truncate(magnitude);
            double minVal   = Math.Floor(60.0 * (magnitude - majorVal));
            double secVal   = 3600.0 * (magnitude - majorVal - minVal / 60.0);
            mMajor.Value    = Clamp(mMajor,   (decimal)majorVal);
            mMinutes.Value  = Clamp(mMinutes, (decimal)minVal);
            mSeconds.Value  = Clamp(mSeconds, (decimal)Math.Round(secVal, 2));
        }

        private static decimal Clamp(NumericUpDown spinner, decimal value)
        {
            if (value < spinner.Minimum) return spinner.Minimum;
            if (value > spinner.Maximum) return spinner.Maximum;
            return value;
        }
    }
}
