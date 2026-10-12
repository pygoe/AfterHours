using MelonLoader;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;

namespace ChemistryStationMkII;

public sealed partial class Mod
{
    private static bool qualityAuditComplete;

    private static void AuditQualityBenefits()
    {
        if (qualityAuditComplete || !ovenVerified || Customer.UnlockedCustomers == null || Customer.UnlockedCustomers.Count == 0) return;
        try
        {
            var meth = Registry.GetItem<ProductDefinition>("meth");
            if (meth == null) return;
            float premium = CustomerData.GetQualityScalar(EQuality.Premium);
            float heavenly = CustomerData.GetQualityScalar(EQuality.Heavenly);
            if (heavenly <= premium) throw new InvalidOperationException("Native Heavenly scalar is not above Premium.");
            int count = 0, improved = 0;
            float maxDifference = 0f;
            foreach (var customer in Customer.UnlockedCustomers)
            {
                if (customer == null || customer.CustomerData == null) continue;
                float before = customer.GetProductEnjoyment(meth, EQuality.Premium);
                float after = customer.GetProductEnjoyment(meth, EQuality.Heavenly);
                count++;
                if (after > before + .0001f) improved++;
                maxDifference = Math.Max(maxDifference, after - before);
            }
            if (count == 0) return;
            qualityAuditComplete = true;
            MelonLogger.Msg($"[Mk II quality] Native quality scalar: Premium={premium}, Heavenly={heavenly}. Compared {count} unlocked customer enjoyment scores: {improved} improve with Heavenly, maximum increase={maxDifference:0.000}. Other customers may already be at their quality-benefit cap. Read-only audit; no relationships or money changed.");
        }
        catch (Exception ex)
        {
            qualityAuditComplete = true;
            Report("native customer quality audit", ex);
        }
    }

}
