using System;
using Astronomy.NINA.Persistence;

namespace TargetPlanner.State
{
    /// <summary>
    /// Per-site user planning preferences -- the scalar floor and minimum-duration
    /// inputs the user picks via MainForm's H/D spinners. Persisted per-named-
    /// location on <see cref="NamedSite.Preferences"/>; flows into
    /// <see cref="PlanningPolicy"/> at snapshot time.
    /// </summary>
    /// <remarks>
    /// Named-immutable-type shape mirrors AL's convention (AltAz, ObservationMoment,
    /// NightWindow): grouped values travel together as one record so consumers can
    /// pass <c>preferences with { TargetFloorDeg = newValue }</c> rather than
    /// shuffling two scattered scalar fields.
    /// </remarks>
    public sealed record PlanningPreferences(double TargetFloorDeg, TimeSpan MinDuration)
    {
        /// <summary>
        /// Ship-safe defaults: 30 deg floor, 240 min minimum duration. Matches the
        /// pre-Phase-2 hardcoded Location.Default values so an empty / first-boot
        /// install reproduces the historical baseline.
        /// </summary>
        public static PlanningPreferences Default { get; } =
            new PlanningPreferences(TargetFloorDeg: 30.0, MinDuration: TimeSpan.FromMinutes(240));

        /// <summary>
        /// Materialise a <see cref="PlanningPreferences"/> record from the
        /// <see cref="PlanningPreferencesDto"/> persisted on a <see cref="NamedSite"/>.
        /// A null DTO (never-seen site / older settings.json predating the field)
        /// resolves to <see cref="Default"/>.
        /// </summary>
        public static PlanningPreferences FromDto(PlanningPreferencesDto dto) =>
            dto == null
                ? Default
                : new PlanningPreferences(dto.TargetFloorDeg, TimeSpan.FromMinutes(dto.MinDurationMinutes));

        /// <summary>
        /// Project this record back into the serialisable DTO consumed by
        /// <see cref="NamedSite.Preferences"/>.
        /// </summary>
        public PlanningPreferencesDto ToDto() => new PlanningPreferencesDto
        {
            TargetFloorDeg     = this.TargetFloorDeg,
            MinDurationMinutes = this.MinDuration.TotalMinutes,
        };
    }
}
