using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TargetPlanner.Support;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Settings
{
    // File-backed store for user-added (non-NINA) targets. Path:
    // %AppData%\TargetPlanner\local-targets.json. Locally-added targets are additive
    // on top of NINA's loaded list -- MainForm merges them into KnownTargets after
    // every NINA Load(...) so a re-browse doesn't wipe them. Load/Save are best-
    // effort; corrupt or missing files yield an empty list and a tp.log entry.
    public static class LocalTargetStore
    {
        public static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TargetPlanner");

        public static readonly string FilePath = Path.Combine(DirectoryPath, "local-targets.json");

        public static List<Target> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    List<LocalTargetDto> dtos = JsonConvert.DeserializeObject<List<LocalTargetDto>>(json);
                    if (dtos == null) return new List<Target>();

                    var result = new List<Target>(dtos.Count);
                    foreach (LocalTargetDto dto in dtos)
                    {
                        if (dto == null || string.IsNullOrWhiteSpace(dto.Name)) continue;
                        result.Add(new Target(
                            name:           dto.Name,
                            rightAscension: dto.RightAscension,
                            declination:    dto.Declination,
                            north:          dto.North,
                            directory:      string.Empty,
                            enabled:        true));
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error("LocalTargetStore.Load failed at '" + FilePath + "'", ex);
            }
            return new List<Target>();
        }

        public static void Save(IEnumerable<Target> targets)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var dtos = new List<LocalTargetDto>();
                if (targets != null)
                {
                    foreach (Target t in targets)
                    {
                        if (t == null) continue;
                        dtos.Add(new LocalTargetDto
                        {
                            Name           = t.Name,
                            RightAscension = t.RightAscension,
                            Declination    = t.Declination,
                            North          = t.North,
                        });
                    }
                }
                string json = JsonConvert.SerializeObject(dtos, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Log.Error("LocalTargetStore.Save failed at '" + FilePath + "'", ex);
            }
        }

        // JSON DTO. Target is a Core POCO with no JSON attributes / no parameterless ctor;
        // round-tripping it directly would couple the Library to Newtonsoft. The DTO holds
        // only the four fields a locally-added target meaningfully carries -- Directory and
        // Enabled default to "" / true on load.
        private sealed class LocalTargetDto
        {
            public string Name           { get; set; }
            public double RightAscension { get; set; }
            public double Declination    { get; set; }
            public bool   North          { get; set; }
        }
    }
}
