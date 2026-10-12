using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.ItemLoaders;
using Il2CppScheduleOne.PlayerTasks;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.UI.Stations;
using Object = UnityEngine.Object;

namespace ChemistryStationMkII;

public sealed partial class Mod
{
    private static BuildableItemDefinition? OvenDefinition;
    private static bool ovenVerified, ovenTesting, ovenFailed;
    private static IntPtr ovenProbe;
    private static readonly Dictionary<IntPtr, LabOven> OvenOperations = new();

    private void InstallOvenPatches()
    {
        Patch(typeof(LabOven), "InitializeGridItem", postfix: nameof(OvenInitialized));
        Patch(typeof(LabOven), "RpcLogic___SetCookOperation_2611294368", postfix: nameof(OvenOperationSet));
        Patch(typeof(LabOven), "Destroy", prefix: nameof(OvenDestroyed));
        Patch(typeof(LabOven), "OnTimePass", prefix: nameof(OvenTimePass));
        Patch(typeof(LabOven), "OnUncappedMinPass", prefix: nameof(OvenMinute));
        // Correct the label in the same call that writes the vanilla countdown.
        // A frame-Update correction races minute events and visibly flashes.
        Patch(typeof(LabOven), "UpdateOvenAppearance", postfix: nameof(OvenClockUpdated));
        Patch(typeof(LabOven), "get_Name", postfix: nameof(OvenName));
        Patch(typeof(LabOven), "CanOutputSpaceFitCurrentOperation", postfix: nameof(OvenOutputSpace));
        Patch(typeof(LabOven), "IsReadyToStart", postfix: nameof(OvenReadyToStart));
        Patch(typeof(LabOvenCanvas), "DoesOvenOutputHaveSpace", postfix: nameof(OvenCanvasSpace));
        Patch(typeof(FinalizeLabOven), "Shatter", prefix: nameof(OvenManualHarvest));
        Patch(typeof(FinishLabOvenBehaviour), "CanActionStart", postfix: nameof(OvenWorkerCanHarvest));
        foreach (var type in new[] { typeof(StartLabOvenBehaviour), typeof(FinishLabOvenBehaviour) })
        {
            var routine = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => AccessTools.Property(t, "__4__this")?.PropertyType == type && AccessTools.Method(t, "MoveNext") != null);
            Patch(routine, "MoveNext", postfix: nameof(OvenWorkerStep));
        }
    }

    private static bool IsMkII(LabOven? oven) => Ready && (ovenVerified || ovenTesting) &&
        oven != null && oven.ItemInstance != null && oven.ItemInstance.ID == Rules.OvenItemId;

    private static bool EnsureOvenDefinition()
    {
        if (!Ready || ovenFailed || Registry.Instance == null) return false;
        if (Registry.ItemExists(Rules.OvenItemId))
        {
            VerifyOvenContract();
            return ovenVerified || ovenTesting;
        }
        var original = Registry.GetItem<BuildableItemDefinition>(Rules.OvenVanillaId);
        if (original?.BuiltItem == null) return false;
        if (OvenDefinition == null)
        {
            OvenDefinition = Object.Instantiate(original);
            OvenDefinition.name = Rules.OvenItemId;
            OvenDefinition.ID = Rules.OvenItemId;
            OvenDefinition.Name = Rules.OvenDisplayName;
            OvenDefinition.Description = "A high-efficiency lab oven for faster cooking and greater yields. Preserves the quality of every batch.";
            OvenDefinition.BasePurchasePrice = Rules.OvenPrice;
            try { OvenDefinition.Icon = LoadItemIcon("oven-mkii-icon.png"); }
            catch (Exception ex) { Report("oven icon; original retained", ex); }
            Object.DontDestroyOnLoad(OvenDefinition);
        }
        Registry.Instance.AddToRegistry(OvenDefinition);
        VerifyOvenContract();
        return ovenVerified || ovenTesting;
    }

    private static void VerifyOvenContract()
    {
        if (ovenTesting || ovenVerified || ovenFailed || OvenDefinition == null) return;
        ovenTesting = true;
        try
        {
            var item = OvenDefinition.GetDefaultInstance(1);
            string json = item.GetItemData().GetJson(false);
            var parsed = JObject.Parse(json);
            if ((string?)parsed["ID"] != Rules.OvenVanillaId || (int?)parsed[Rules.OvenMarker] != 1)
                throw new InvalidOperationException("Oven save lacks native fallback and upgrade marker.");
            var loader = new ItemLoader();
            if (loader.LoadItem(json)?.ID != Rules.OvenItemId)
                throw new InvalidOperationException("Oven upgrade marker did not reload.");
            parsed.Remove(Rules.OvenMarker);
            if (loader.LoadItem(parsed.ToString())?.ID != Rules.OvenVanillaId)
                throw new InvalidOperationException("Oven vanilla fallback did not reload.");
            var records = new Il2CppSystem.Collections.Generic.List<ItemData>();
            records.Add(item.GetItemData());
            string inventory = new ItemSet(records).GetJSON();
            if (inventory.Contains(Rules.OvenItemId, StringComparison.Ordinal) || !inventory.Contains(Rules.OvenMarker, StringComparison.Ordinal))
                throw new InvalidOperationException("Oven inventory serialization did not retain the fallback.");

            var liquid = Registry.GetItem<StorableItemDefinition>("liquidmeth");
            var cookable = liquid?.StationItem?.GetModule<CookableModule>();
            if (cookable?.Product == null || cookable.ProductQuantity != 10)
                throw new InvalidOperationException("Expected native liquid recipe with ten products per liquid.");
            foreach (var quality in new[] { EQuality.Standard, EQuality.Premium, EQuality.Heavenly })
            {
                var op = new OvenCookOperation("liquidmeth", quality, 1, cookable.Product.ID);
                if (op.GetProductItem(10).Quantity != 10)
                    throw new InvalidOperationException("Ordinary oven yield changed unexpectedly.");
                ovenProbe = op.Pointer;
                var output = op.GetProductItem(10);
                if (output.Quantity != Rules.MethPerLiquid || output.TryCast<QualityItemInstance>()?.Quality != (EQuality)Rules.OvenQuality((int)quality))
                    throw new InvalidOperationException("Oven product failed doubled-yield or configured-quality check.");
                if (op.GetProductItem(10).Quantity != Rules.MethPerLiquid)
                    throw new InvalidOperationException("Repeated product reads changed the oven yield.");
                // Products require the game's type-dispatching deserializer;
                // the base equipment loader deliberately discards product fields.
                var reloaded = Il2CppScheduleOne.Persistence.ItemDeserializer.LoadItem(output.GetItemData().GetJson(false));
                if (reloaded?.Quantity != Rules.MethPerLiquid || reloaded.TryCast<QualityItemInstance>()?.Quality != (EQuality)Rules.OvenQuality((int)quality))
                    throw new InvalidOperationException($"Oven product round trip expected {Rules.MethPerLiquid}/{(EQuality)Rules.OvenQuality((int)quality)}, got {reloaded?.Quantity}/{reloaded?.TryCast<QualityItemInstance>()?.Quality}.");
                ovenProbe = IntPtr.Zero;
            }
            ovenVerified = true;
            MelonLogger.Msg($"[Oven Mk II] PASS native item/inventory save fallback and reload; configured output {Rules.MethPerLiquid} per liquid with configured quality at Standard, Premium and Heavenly inputs. Native cook time {cookable.CookTime}m; Mk II {Rules.OvenRemainingMinutes(cookable.CookTime, 0)}m. No disk saves touched.");
        }
        catch (Exception ex)
        {
            ovenFailed = true;
            if (Registry.ItemExists(Rules.OvenItemId)) Registry.Instance.RemoveFromRegistry(OvenDefinition);
            MelonLogger.Error("[Oven Mk II] New oven withheld from shop: " + ex);
        }
        finally { ovenProbe = IntPtr.Zero; ovenTesting = false; }
    }

    private static void AddOvenToShop(ShopInterface shop)
    {
        if (!Ready || !ovenVerified || shop == null || OvenDefinition == null || shop.Listings == null || shop.ListingUIPrefab == null) return;
        if (shop.GetListing(Rules.OvenItemId) != null) return;
        var original = shop.GetListing(Rules.OvenVanillaId);
        if (original == null || shop.GetListing(Rules.VanillaId) == null) return;
        var listing = new ShopListing
        {
            Item = OvenDefinition, name = Rules.OvenDisplayName,
            OverridePrice = true, OverriddenPrice = Rules.OvenPrice,
            LimitedStock = false, CanBeDelivered = original.CanBeDelivered,
            UseIconTint = false
        };
        listing.Initialize(shop);
        shop.Listings.Add(listing);
        try { shop.CreateListingUI(listing); }
        catch { shop.Listings.Remove(listing); throw; }
        MelonLogger.Msg($"[Oven Mk II] Added {Rules.OvenDisplayName} to {shop.ShopName} for ${Rules.OvenPrice}.");
    }

    private static void ForgetOven(LabOven oven)
    {
        foreach (var pair in OvenOperations.ToArray())
            if (pair.Value == null || pair.Value.Pointer == oven.Pointer) OvenOperations.Remove(pair.Key);
    }

    private static void TrackOven(LabOven oven)
    {
        ForgetOven(oven);
        if (IsMkII(oven) && oven.CurrentOperation != null)
            OvenOperations[oven.CurrentOperation.Pointer] = oven;
    }

    private static bool IsUpgradedOperation(OvenCookOperation op)
    {
        if (ovenTesting && ovenProbe != IntPtr.Zero && op.Pointer == ovenProbe) return true;
        return OvenOperations.TryGetValue(op.Pointer, out var oven) && IsMkII(oven) &&
            oven.CurrentOperation != null && oven.CurrentOperation.Pointer == op.Pointer;
    }

    private static void OvenInitialized(LabOven __instance)
    {
        TrackOven(__instance);
        if (!IsMkII(__instance) || __instance.isGhost) return;
        try { ApplyBenchFinish(__instance.transform); }
        catch (Exception ex) { Report("oven finish; gameplay remains active", ex); }
    }
    private static void OvenOperationSet(LabOven __instance) => TrackOven(__instance);
    private static void OvenDestroyed(LabOven __instance) => ForgetOven(__instance);

    private static void OvenTimePass(LabOven __instance, ref int __0)
    {
        if (!IsMkII(__instance)) return;
        TrackOven(__instance);
        if (__instance.CurrentOperation != null)
            __0 = ScaledTick(__instance.Pointer, __instance.CurrentOperation.Pointer, __0, Rules.OvenProcessSpeed);
    }

    private static bool OvenMinute(LabOven __instance)
    {
        if (!IsMkII(__instance)) return true;
        // Native OnUncappedMinPass increments progress directly, bypassing
        // OnTimePass. Its remaining behavior matches OnTimePass(1), including
        // completion audio and appearance updates. Route through the speed hook once.
        __instance.OnTimePass(1);
        return false;
    }

    private static void OvenClockUpdated(LabOven __instance)
    {
        if (!IsMkII(__instance) || __instance.CurrentOperation == null || __instance.TimerLabel == null) return;
        var op = __instance.CurrentOperation;
        int remaining = Rules.OvenRemainingMinutes(op.GetCookDuration(), op.CookProgress);
        if (remaining > 0) __instance.TimerLabel.text = $"{remaining / 60}:{remaining % 60:00}";
    }

    private static void OvenName(LabOven __instance, ref string __result)
    {
        if (IsMkII(__instance)) __result = Rules.OvenDisplayName;
    }

    private static bool HasOvenSpace(LabOven oven, bool incoming = false)
    {
        var op = incoming ? null : oven.CurrentOperation;
        if (op == null)
        {
            var ingredient = oven.IngredientSlot?.ItemInstance;
            if (ingredient?.ID != "liquidmeth") return true;
            var definition = Registry.GetItem<StorableItemDefinition>(ingredient.ID);
            var recipe = definition.StationItem.GetModule<CookableModule>();
            op = new OvenCookOperation(ingredient.ID, ingredient.TryCast<QualityItemInstance>()?.Quality ?? EQuality.Standard, 1, recipe.Product.ID);
        }
        if (op.IngredientID != "liquidmeth") return true;
        int required = Rules.OvenOutputQuantity(op.IngredientQuantity);
        var probe = op.GetProductItem(1);
        probe.TryCast<QualityItemInstance>()?.SetQuality((EQuality)Rules.OvenQuality((int)op.IngredientQuality));
        probe.SetQuantity(required);
        return oven.OutputSlot.GetCapacityForItem(probe, false) >= required;
    }

    private static void OvenOutputSpace(LabOven __instance, ref bool __result)
    {
        if (IsMkII(__instance) && __instance.CurrentOperation?.IngredientID == "liquidmeth")
            __result = HasOvenSpace(__instance);
    }

    private static void OvenReadyToStart(LabOven __instance, ref bool __result)
    {
        if (__result && IsMkII(__instance)) __result = HasOvenSpace(__instance, incoming: true);
    }

    private static void OvenCanvasSpace(LabOvenCanvas __instance, ref bool __result)
    {
        if (IsMkII(__instance.Oven)) __result = __result && HasOvenSpace(__instance.Oven);
    }

    private static bool OvenManualHarvest(FinalizeLabOven __instance) =>
        !IsMkII(__instance.Oven) || HasOvenSpace(__instance.Oven);

    private static void OvenWorkerCanHarvest(FinishLabOvenBehaviour __instance, ref bool __result)
    {
        if (__result && IsMkII(__instance.targetOven)) __result = HasOvenSpace(__instance.targetOven);
    }

    private static void OvenWorkerStep(object __instance, bool __result)
    {
        if (!__result) return;
        var type = __instance.GetType();
        var owner = AccessTools.Property(type, "__4__this")?.GetValue(__instance);
        LabOven? oven = owner switch
        {
            StartLabOvenBehaviour start => start.targetOven,
            FinishLabOvenBehaviour finish => finish.targetOven,
            _ => null
        };
        if (!IsMkII(oven)) return;
        var property = AccessTools.Property(type, "__2__current");
        var wait = (property?.GetValue(__instance) as Il2CppSystem.Object)?.TryCast<WaitForSeconds>();
        if (wait != null && wait.m_Seconds > 0)
            property!.SetValue(__instance, new WaitForSeconds(wait.m_Seconds / RuntimeTuning.Current.OvenWorkerSpeed));
    }
}
