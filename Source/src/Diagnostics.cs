using System.Security.Cryptography;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace ChemistryStationMkII;

public sealed partial class Mod
{
    internal static string BuildFingerprint = "unavailable";
    private static readonly Dictionary<string, long> EventCounts = new();
    private static readonly Queue<string> RecentEvents = new();
    private static string ReportDirectory => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "AfterHours", "Reports");

    private void InitializeDiagnostics()
    {
        using var stream = File.OpenRead(typeof(Mod).Assembly.Location);
        using var sha = SHA256.Create();
        BuildFingerprint = Convert.ToHexString(sha.ComputeHash(stream));
        MelonLogger.Msg($"[After Hours] Version 0.4.2; game {Application.version}; Unity {Application.unityVersion}; DLL checksum {BuildFingerprint}. Diagnostics enabled; detailed events optional in phone settings.");
    }

    private static void DebugEvent(string code, string message)
    {
        EventCounts.TryGetValue(code, out long count);
        EventCounts[code] = count + 1;
        string entry = $"{DateTime.UtcNow:O} [{code}] {message}";
        if (RecentEvents.Count >= 200) RecentEvents.Dequeue();
        RecentEvents.Enqueue(entry);
        if (verbose?.Value == true && (count < 20 || count % 100 == 0))
            MelonLogger.Msg("[After Hours DEBUG] " + entry);
    }

    private static void WriteSupportReport()
    {
        try
        {
            Directory.CreateDirectory(ReportDirectory);
            var report = new
            {
                Product = "After Hours: Industry & Motion", Version = "0.4.2", Utc = DateTime.UtcNow,
                FileChecksum_SHA256 = BuildFingerprint,
                ChecksumExplanation = "A file fingerprint to identify this exact DLL; not encryption or a password.",
                Game = Application.version, Unity = Application.unityVersion,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Session = sessionStatus.Value,
                Ready, ChemistrySaveCheck = saveContractVerified, OvenSaveCheck = ovenVerified, BoardSaveCheck = boardSaveVerified,
                PatchedMethods = HarmonyLib.Harmony.GetAllPatchedMethods().Count(m => HarmonyLib.Harmony.GetPatchInfo(m)?.Owners.Contains(HarmonyOwner) == true),
                SettingsRevision, EffectiveSettings = RuntimeTuning.Current, LocalPreferences = localTuning,
                Mods = MelonMod.RegisteredMelons.Select(m => new { Name = m.Info.Name, Version = m.Info.Version }).ToArray(),
                Events = EventCounts, RecentEvents = RecentEvents.ToArray(),
                Errors = Reported.OrderBy(s => s).ToArray(),
                Instructions = "Share this report and MelonLoader/Latest.log from BOTH host and client after a co-op issue. Include what happened and steps to reproduce. Review raw logs for personal details. Nothing is uploaded automatically."
            };
            string file = Path.Combine(ReportDirectory, $"AfterHours-support-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(file, JsonConvert.SerializeObject(report, Formatting.Indented));
            MelonLogger.Msg("[After Hours] Support report written: " + file);
        }
        catch (Exception ex) { MelonLogger.Error("[After Hours] Could not write support report: " + ex.Message); }
    }

    private static string HarmonyOwner = "";
}
