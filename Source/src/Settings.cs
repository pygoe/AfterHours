using System.Reflection;
using MelonLoader;
using MelonLoader.Preferences;
using Newtonsoft.Json;
using UnityEngine;
using Il2CppScheduleOne.UI.Shop;

namespace ChemistryStationMkII;

public sealed partial class Mod
{
    private static readonly Dictionary<FieldInfo, MelonPreferences_Entry> SettingsEntries = new();
    private static MelonPreferences_Entry<bool> verbose = null!, exportReport = null!, resetDefaults = null!;
    private static MelonPreferences_Entry<string> sessionStatus = null!;
    private static bool settingsDirty;
    private static Tuning localTuning = new();
    private static string activeSettingsJson = "";
    private sealed class TickCarry { internal IntPtr Operation; internal double Fraction; }
    private static readonly Dictionary<IntPtr, TickCarry> TickCarries = new();

    private void InitializeSettings()
    {
        var info = MelonPreferences.CreateCategory("AfterHours_00Information", "00 · After Hours — read first");
        info.CreateEntry("TuningWarning", "Live changes affect unfinished batches.", "TUNING WARNING",
            "Values apply when you finish editing; Save keeps them for future launches. Cook time, yield and grade changes may affect batches already running. Finished items stay unchanged.");
        info.CreateEntry("CapacityWarning", "Back up before experimenting.", "OUTPUT CAPACITY",
            "Full output must fit. Excessive yields pause production until reduced or enough matching stack space is available. Large yields may require a stack-size mod.");
        info.CreateEntry("CoopWarning", "Co-op TEST build — everyone needs the same DLL.", "HOST CONTROLS GAMEPLAY",
            "The host controls gameplay values for everyone. Client fields are local preferences for their next hosted/solo game; they do not override the host. Host settings are never saved over client preferences.");
        sessionStatus = info.CreateEntry("SessionStatus", "Starting", "Session status (informational)",
            "Updated automatically. When joining a host, the support report contains the effective host settings.", false, true, null);
        foreach (string section in new[] { "Chemistry", "Oven", "Board" })
        {
            var category = MelonPreferences.CreateCategory("AfterHours_" + section, section + " settings");
            foreach (var field in typeof(Tuning).GetFields())
            {
                var control = field.GetCustomAttribute<ControlAttribute>()!;
                if (control.Section != section) continue;
                var method = typeof(Mod).GetMethod(nameof(CreateControl), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(field.FieldType);
                method.Invoke(null, new object[] { category, field, control });
            }
        }
        var support = MelonPreferences.CreateCategory("AfterHours_ZSupport", "Support and reset");
        verbose = support.CreateEntry("VerboseLogging", false, "Detailed debug logging", "Enable before reproducing a bug. Logs stay on your PC; nothing is uploaded.");
        exportReport = support.CreateEntry("ExportSupportReport", false, "Create support report", "Switch on to write a report in UserData/AfterHours/Reports. Resets automatically. Share it plus MelonLoader/Latest.log; review logs for personal details first.", false, true, null);
        resetDefaults = support.CreateEntry("ResetDefaults", false, "Reset gameplay settings", "Restores this mod's default gameplay values immediately; click Save to keep them. Host changes are sent to clients.", false, true, null);
        exportReport.Value = false;
        resetDefaults.Value = false;
        settingsDirty = true;
        UpdateSettings();
    }

    private static void CreateControl<T>(MelonPreferences_Category category, FieldInfo field, ControlAttribute control)
    {
        ValueValidator? validator = typeof(T) == typeof(int) || typeof(T) == typeof(float)
            ? new FiniteRange(control.Min, control.Max, field.GetValue(new Tuning())!) : null;
        var entry = category.CreateEntry(field.Name, (T)field.GetValue(new Tuning())!, control.Label,
            control.Help + (validator != null ? $" Range: {control.Min}–{control.Max}." : ""), false, false, validator);
        entry.OnEntryValueChanged.Subscribe((_, _) => settingsDirty = true);
        SettingsEntries.Add(field, entry);
    }

    private sealed class FiniteRange(double min, double max, object fallback) : ValueValidator
    {
        public override bool IsValid(object value) => value != null && double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) && v >= min && v <= max;
        public override object EnsureValid(object value)
        {
            if (IsValid(value)) return value;
            double v;
            try { v = Convert.ToDouble(value); } catch { return fallback; }
            if (!double.IsFinite(v)) return fallback;
            return Convert.ChangeType(Math.Clamp(v, min, max), fallback.GetType());
        }
    }

    private static void UpdateSettings()
    {
        try
        {
            if (resetDefaults?.Value == true)
            {
                resetDefaults.Value = false;
                var defaults = new Tuning();
                foreach (var pair in SettingsEntries) pair.Value.BoxedValue = pair.Key.GetValue(defaults);
                settingsDirty = true;
            }
            if (settingsDirty)
            {
                settingsDirty = false;
                var next = new Tuning();
                foreach (var pair in SettingsEntries) pair.Key.SetValue(next, pair.Value.BoxedValue);
                localTuning = next.Normalize();
                foreach (var pair in SettingsEntries)
                    if (!Equals(pair.Value.BoxedValue, pair.Key.GetValue(localTuning))) pair.Value.BoxedValue = pair.Key.GetValue(localTuning);
                if (!NetworkIsClient) ApplyTuning(localTuning, "local/host preferences");
                else MelonLogger.Msg("[After Hours] Local preferences updated; this session continues using the host settings.");
            }
            if (exportReport?.Value == true) { exportReport.Value = false; WriteSupportReport(); }
        }
        catch (Exception ex) { Report("settings update", ex); }
    }

    private static void ApplyTuning(Tuning values, string source)
    {
        values = values.Normalize();
        string json = JsonConvert.SerializeObject(values);
        if (json == activeSettingsJson) return;
        RuntimeTuning.Current = values;
        activeSettingsJson = json;
        SettingsRevision++;
        if (Definition != null) Definition.BasePurchasePrice = Rules.Price;
        if (OvenDefinition != null) OvenDefinition.BasePurchasePrice = Rules.OvenPrice;
        if (boardDefinition != null) boardDefinition.BasePurchasePrice = Rules.BoardPrice;
        foreach (var state in ElectricBoards.Values.ToArray())
            if (state.Board != null) { ConfigureElectric(state.Board); state.Lifting = false; }
        MelonLogger.Msg($"[After Hours] Settings revision {SettingsRevision} from {source}: chemistry {values.ChemMinutes}m/{values.ChemYield}x; oven {values.OvenMinutes}m/{values.OvenYield} per liquid; board {values.BoardSpeed} km/h.");
    }

    private static int ScaledTick(IntPtr station, IntPtr operation, int minutes, double speed)
    {
        if (!TickCarries.TryGetValue(station, out var state) || state.Operation != operation)
            TickCarries[station] = state = new TickCarry { Operation = operation };
        return RuntimeTuning.ScaleMinutes(minutes, speed, ref state.Fraction);
    }

    private static void RefreshPrices(ShopInterface shop)
    {
        if (shop == null) return;
        foreach (var pair in new[] { (Rules.ItemId, Rules.Price), (Rules.OvenItemId, Rules.OvenPrice), (Rules.BoardItemId, Rules.BoardPrice) })
        {
            var listing = shop.GetListing(pair.Item1);
            if (listing != null) { listing.OverridePrice = true; listing.OverriddenPrice = pair.Item2; }
        }
    }
}
