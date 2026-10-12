namespace ChemistryStationMkII;

internal static class Rules
{
    internal const string ItemId = "jason_chemistrystation_mkii";
    internal const string VanillaId = "chemistrystation";
    internal const string Marker = "ChemistryStationMkII";
    internal const string DisplayName = "Chemistry Station Mk II";
    internal static int Price => RuntimeTuning.Current.ChemPrice;
    internal static double ProcessSpeed => 480d / RuntimeTuning.Current.ChemMinutes;
    internal static float WorkerSpeed => RuntimeTuning.Current.ChemWorkerSpeed;
    internal static int YieldMultiplier => RuntimeTuning.Current.ChemYield;
    internal const string OvenItemId = "jason_laboven_mkii";
    internal const string OvenVanillaId = "laboven";
    internal const string OvenMarker = "LabOvenMkII";
    internal const string OvenDisplayName = "Lab Oven Mk II";
    internal static int OvenPrice => RuntimeTuning.Current.OvenPrice;
    internal static double OvenProcessSpeed => 360d / RuntimeTuning.Current.OvenMinutes;
    internal static int MethPerLiquid => RuntimeTuning.Current.OvenYield;
    internal const string BoardItemId = "jason_electricskateboard";
    internal const string BoardVanillaId = "goldenskateboard";
    internal const string BoardMarker = "JasonElectricSkateboard";
    internal const string BoardDisplayName = "Electric Skateboard";
    internal static int BoardPrice => RuntimeTuning.Current.BoardPrice;
    internal static float BoardSpeedMultiplier => RuntimeTuning.Current.BoardSpeed / 32f;
    internal static float BoardJumpMultiplier => RuntimeTuning.Current.BoardJump;
    internal static float BoardAcceleration => RuntimeTuning.Current.BoardAcceleration;
    internal static float BoardMaxStep => RuntimeTuning.Current.CurbHeight;
    internal static float CurbEase(float progress)
    {
        float t = Math.Clamp(progress, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    internal static bool CanAssistStep(float height, float surfaceUp, float speed, float verticalSpeed) =>
        float.IsFinite(height) && height > .035f && height <= BoardMaxStep &&
        surfaceUp >= .75f && speed > .35f && verticalSpeed < 1.5f;

    internal static bool ContainsCustomId(string text) =>
        text.Contains(ItemId, StringComparison.Ordinal) || text.Contains(OvenItemId, StringComparison.Ordinal) ||
        text.Contains(BoardItemId, StringComparison.Ordinal);

    internal static int OvenQuality(int quality) => Tuning.Quality(quality, RuntimeTuning.Current.OvenQualityBoost, RuntimeTuning.Current.OvenQuality);

    internal static int ScaleOvenRequest(int requested) => checked((int)Math.Ceiling(requested * (double)MethPerLiquid / 10d));

    internal static int OvenOutputQuantity(int ingredientQuantity) => checked(ingredientQuantity * MethPerLiquid);

    internal static int OvenAdvance(int minutes) => minutes <= 0 ? minutes :
        (int)Math.Min((long)minutes * OvenProcessSpeed, int.MaxValue);

    internal static int OvenRemainingMinutes(int recipeMinutes, int progress) =>
        (int)Math.Ceiling(Math.Max(0L, (long)recipeMinutes - progress) / (double)OvenProcessSpeed);

    internal static int Advance(int minutes) => minutes <= 0 ? minutes :
        (int)Math.Min((long)minutes * ProcessSpeed, int.MaxValue);

    internal static int RemainingMinutes(int recipeMinutes, int progress) =>
        (int)Math.Ceiling(Math.Max(0L, (long)recipeMinutes - progress) / (double)ProcessSpeed);

    internal static int OutputQuantity(int vanillaQuantity) =>
        checked(vanillaQuantity * YieldMultiplier);

    // Native tiers: Trash 0, Poor 1, Standard 2, Premium 3, Heavenly 4.
    internal static int UpgradeQuality(int quality) => Tuning.Quality(quality, RuntimeTuning.Current.ChemQualityBoost, RuntimeTuning.Current.ChemQuality);

}
