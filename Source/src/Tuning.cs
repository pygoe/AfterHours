namespace ChemistryStationMkII;

[AttributeUsage(AttributeTargets.Field)]
internal sealed class ControlAttribute(string section, string label, string help, double min = 0, double max = 1) : Attribute
{
    internal string Section = section, Label = label, Help = help;
    internal double Min = min, Max = max;
}

public enum QualityChoice { FollowIngredients = -1, Trash, Poor, Standard, Premium, Heavenly }

// Shared by the settings UI, network manifest and dependency-free rule tests.
internal sealed class Tuning
{
    [Control("Chemistry", "Cook time (game minutes)", "Liquid batch duration. Other recipes scale proportionally.", 1, 1440)] public int ChemMinutes = 120;
    [Control("Chemistry", "Liquid yield multiplier", "Output per normal liquid output. Full output must fit; high yields may require a stack-size mod.", 1, 20)] public int ChemYield = 2;
    [Control("Chemistry", "Quality increase", "Tiers added to ingredient quality; capped at Heavenly. Ignored when a fixed quality is selected.", -4, 4)] public int ChemQualityBoost = 1;
    [Control("Chemistry", "Output quality", "Follow ingredients plus the quality increase, or select a fixed grade.")] public QualityChoice ChemQuality = QualityChoice.FollowIngredients;
    [Control("Chemistry", "Worker speed multiplier", "Setup waits only; travel and worker assignments are unchanged.", .25, 20)] public float ChemWorkerSpeed = 4;
    [Control("Chemistry", "Automatic trash disposal", "Dispose of empty ingredient containers after mixing.")] public bool AutoTrash = true;
    [Control("Chemistry", "Shop price", "Applies when reopening the shop.", 1, 50000)] public int ChemPrice = 3000;
    [Control("Oven", "Cook time (game minutes)", "Liquid batch duration. Other recipes scale proportionally.", 1, 1440)] public int OvenMinutes = 60;
    [Control("Oven", "Meth per liquid", "Full output must fit. Larger quantities may require a stack-size mod.", 1, 200)] public int OvenYield = 20;
    [Control("Oven", "Quality increase", "Default zero preserves the liquid grade; capped at Heavenly.", -4, 4)] public int OvenQualityBoost = 0;
    [Control("Oven", "Output quality", "Preserve the liquid grade plus the increase, or choose a fixed grade.")] public QualityChoice OvenQuality = QualityChoice.FollowIngredients;
    [Control("Oven", "Worker speed multiplier", "Setup and harvest waits only.", .25, 20)] public float OvenWorkerSpeed = 4;
    [Control("Oven", "Shop price", "Applies when reopening the shop.", 1, 50000)] public int OvenPrice = 3000;
    [Control("Board", "Top speed (km/h)", "Golden board baseline is 32 km/h. High speeds can stress native collision handling.", 5, 80)] public float BoardSpeed = 44.8f;
    [Control("Board", "Jump force multiplier", "Relative to the golden board; actual height depends on charge and physics.", .5, 3)] public float BoardJump = 1.4f;
    [Control("Board", "Motor acceleration", "Acceleration in metres per second squared.", .5, 30)] public float BoardAcceleration = 8;
    [Control("Board", "Steering strength", "Relative to the golden board.", .5, 3)] public float BoardSteering = 1.25f;
    [Control("Board", "Steering response", "Relative to the golden board.", .5, 4)] public float BoardResponse = 1.6f;
    [Control("Board", "Steering return", "How quickly steering returns to centre.", .5, 4)] public float BoardReturn = 1.8f;
    [Control("Board", "Lateral grip", "Relative to the golden board.", .5, 3)] public float BoardGrip = 1.5f;
    [Control("Board", "Brake strength", "Relative to the golden board.", .5, 4)] public float BoardBrakes = 1.65f;
    [Control("Board", "Electric motor", "Disable to use native kick pushing and its stamina cost.")] public bool Motor = true;
    [Control("Board", "Ignore terrain slowdown", "Disable for native terrain resistance.")] public bool IgnoreTerrain = true;
    [Control("Board", "Curb assistance", "Clearance-checked assistance over small static steps.")] public bool CurbAssist = true;
    [Control("Board", "Maximum curb height (m)", "Does not remove collisions or climb walls.", .05, .45)] public float CurbHeight = .38f;
    [Control("Board", "Curb smoothing (seconds)", "Time over which the rise is eased. Larger values are gentler.", .08, .5)] public float CurbDuration = .12f;
    [Control("Board", "Curb cooldown (seconds)", "Minimum delay before another assisted step.", .1, 1)] public float CurbCooldown = .45f;
    [Control("Board", "Shop price", "Applies when reopening the seller dialogue.", 1, 50000)] public int BoardPrice = 3000;

    internal Tuning Normalize()
    {
        var result = (Tuning)MemberwiseClone();
        var defaults = new Tuning();
        foreach (var field in typeof(Tuning).GetFields())
        {
            var c = (ControlAttribute)Attribute.GetCustomAttribute(field, typeof(ControlAttribute))!;
            if (field.FieldType == typeof(int)) field.SetValue(result, Math.Clamp((int)field.GetValue(this)!, (int)c.Min, (int)c.Max));
            else if (field.FieldType == typeof(float))
            {
                float v = (float)field.GetValue(this)!;
                field.SetValue(result, float.IsFinite(v) ? Math.Clamp(v, (float)c.Min, (float)c.Max) : field.GetValue(defaults));
            }
            else if (field.FieldType.IsEnum && !Enum.IsDefined(field.FieldType, field.GetValue(this)!)) field.SetValue(result, field.GetValue(defaults));
        }
        return result;
    }
    internal static int Quality(int original, int boost, QualityChoice fixedGrade) =>
        fixedGrade == QualityChoice.FollowIngredients ? Math.Clamp(original + boost, 0, 4) : (int)fixedGrade;
}

internal static class RuntimeTuning
{
    internal static Tuning Current = new();
    // Retain fractional native minutes so any integer cook duration works, including slower-than-vanilla.
    internal static int ScaleMinutes(int minutes, double speed, ref double carry)
    {
        if (minutes <= 0) return minutes;
        double value = minutes * speed + carry;
        if (value >= int.MaxValue) { carry = 0; return int.MaxValue; }
        int whole = (int)Math.Floor(value + 1e-9);
        carry = Math.Max(0, value - whole);
        return whole;
    }
}
