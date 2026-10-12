using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.ItemLoaders;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.UI.Stations;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(ChemistryStationMkII.Mod), "After Hours", "0.4.2", "After Hours Project")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ChemistryStationMkII;

public sealed partial class Mod : MelonMod
{
    internal static BuildableItemDefinition? Definition;
    internal static bool Ready;
    private static bool selfTesting;
    private static bool saveContractVerified;
    private float nextCheck;
    private static readonly HashSet<int> Configured = new();
    private static readonly HashSet<string> Reported = new();
    [ThreadStatic] private static ChemistryStation? finishing;

    public override void OnInitializeMelon()
    {
        // Deliberately explicit: no silent partial installation after a game update.
        try
        {
            InitializeSettings();
            InitializeDiagnostics();
            HarmonyOwner = HarmonyInstance.Id;
            InstallNetworkPatches();
            Patch(typeof(Registry), "Start", postfix: nameof(RegistryReady));
            Patch(typeof(ShopInterface), "Start", postfix: nameof(ShopReady));
            Patch(typeof(ShopInterface), "Open", prefix: nameof(ShopReady));
            Patch(typeof(ChemistryStation), "InitializeGridItem", postfix: nameof(StationInitialized));
            Patch(typeof(ChemistryStation), "OnTimePass", prefix: nameof(TimePass));
            Patch(typeof(ChemistryStation), "UpdateClock", postfix: nameof(ClockUpdated));
            Patch(typeof(ChemistryStation), "get_Name", postfix: nameof(StationName));
            Patch(typeof(ChemistryStation), "DoesOutputHaveSpace", postfix: nameof(OutputSpace));
            Patch(typeof(ChemistryStation), "CreateTrash", prefix: nameof(DisposeChemistryTrash));
            // FishNet queues FinalizeOperation through an RPC even in solo play.
            // Scope the actual receiver, not the asynchronous sending wrapper.
            Patch(typeof(ChemistryStation), "RpcLogic___FinalizeOperation_2166136261", prefix: nameof(FinishPrefix), finalizer: nameof(FinishFinalizer));
            Patch(typeof(StationRecipe), "GetProductInstance", postfix: nameof(ProductCreated), arguments: new[] { typeof(EQuality) });
            Patch(typeof(OvenCookOperation), "GetProductItem", postfix: nameof(OvenProductCreated));
            Patch(typeof(ChemistryStationInterface), "Open", postfix: nameof(InterfaceOpened));
            Patch(typeof(ChemistryStationInterface), "UpdateUI", postfix: nameof(InterfaceUpdated));
            Patch(typeof(SaveData), "GetJson", postfix: nameof(SaveJson));
            Patch(typeof(ItemLoader), "LoadItem", prefix: nameof(LoadItem));
            InstallOvenPatches();
            InstallBoardPatches();

            var routine = typeof(StartChemistryStationBehaviour).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => AccessTools.Property(t, "__4__this")?.PropertyType == typeof(StartChemistryStationBehaviour)
                             && AccessTools.Method(t, "MoveNext") != null);
            WorkerRoutineType = routine;
            Patch(routine, "MoveNext", postfix: nameof(WorkerStep));
            Ready = true;
            LoggerInstance.Msg("After Hours 0.4.2 co-op test build ready. Open After Hours in the phone Mod Manager for settings. Read the tuning warning before changing production.");
        }
        catch (Exception ex)
        {
            HarmonyInstance.UnpatchSelf();
            Ready = false;
            LoggerInstance.Error("Compatibility check failed; no gameplay patches remain active. " + ex);
        }
    }

    private void Patch(Type type, string method, string? prefix = null, string? postfix = null,
        string? finalizer = null, Type[]? arguments = null)
    {
        var target = AccessTools.Method(type, method, arguments) ?? throw new MissingMethodException(type.FullName, method);
        HarmonyMethod? H(string? name) => name == null ? null : new HarmonyMethod(typeof(Mod), name);
        HarmonyInstance.Patch(target, H(prefix), H(postfix), finalizer: H(finalizer));
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Configured.Clear();
        TickCarries.Clear();
        OvenOperations.Clear();
        ElectricBoards.Clear();
        qualityAuditComplete = false;
        nextCheck = 0;
    }

    public override void OnUpdate()
    {
        if (!Ready) return;
        UpdateSettings();
        UpdateNetwork();
        if (Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + 3f;
        try
        {
            if (!EnsureDefinition()) return;
            EnsureOvenDefinition();
            EnsureBoardDefinition();
            AuditQualityBenefits();
            foreach (var shop in ShopInterface.AllShops) AddToShop(shop);
        }
        catch (Exception ex) { Report("registration", ex); }
    }

    internal static bool IsMkII(ChemistryStation? station) =>
        Ready && station != null && station.ItemInstance != null && station.ItemInstance.ID == Rules.ItemId;

    internal static bool IsLiquidRecipe(StationRecipe? recipe) =>
        recipe != null && recipe.Product != null && recipe.Product.Item != null &&
        recipe.Product.Item.ID == "liquidmeth";

    private static bool EnsureDefinition()
    {
        if (!Ready || Registry.Instance == null) return false;
        if (Registry.ItemExists(Rules.ItemId))
        {
            VerifySaveContract();
            return Ready;
        }
        var vanilla = Registry.GetItem<BuildableItemDefinition>(Rules.VanillaId);
        if (vanilla == null || vanilla.BuiltItem == null) return false;
        if (Definition == null)
        {
            Definition = Object.Instantiate(vanilla);
            Definition.name = Rules.ItemId;
            Definition.ID = Rules.ItemId;
            Definition.Name = Rules.DisplayName;
            Definition.Description = "An upgraded chemistry station for faster processing, greater yields, and higher-quality batches. Built-in waste disposal keeps your workspace clean.";
            Definition.BasePurchasePrice = Rules.Price;
            try { Definition.Icon = LoadItemIcon("chemistry-mkii-icon.png"); }
            catch (Exception ex) { Report("chemistry icon; original retained", ex); }
            // The native prefab and item class preserve worker, placement, and network contracts.
            // Only placed Mk II instances are tinted; the shared vanilla prefab is never edited.
            Object.DontDestroyOnLoad(Definition);
        }
        Registry.Instance.AddToRegistry(Definition);
        VerifySaveContract();
        return Ready && Registry.ItemExists(Rules.ItemId);
    }

    private static void VerifySaveContract()
    {
        if (selfTesting || saveContractVerified || Definition == null) return;
        selfTesting = true;
        try
        {
            // Native game methods, in-memory only. No save files, inventory, money,
            // placed stations, or recipe assets are changed by this check.
            var original = Definition.GetDefaultInstance(1);
            string json = original.GetItemData().GetJson(false);
            var parsed = JObject.Parse(json);
            if ((string?)parsed["ID"] != Rules.VanillaId || (int?)parsed[Rules.Marker] != 1)
                throw new InvalidOperationException("Native item serializer did not emit vanilla fallback + upgrade marker.");
            var loader = new ItemLoader();
            var roundTrip = loader.LoadItem(json);
            if (roundTrip == null || roundTrip.ID != Rules.ItemId || roundTrip.Quantity != 1)
                throw new InvalidOperationException("Native item loader did not restore the Mk II marker.");
            parsed.Remove(Rules.Marker);
            var withoutMod = loader.LoadItem(parsed.ToString());
            if (withoutMod == null || withoutMod.ID != Rules.VanillaId)
                throw new InvalidOperationException("Vanilla item fallback failed.");
            var records = new Il2CppSystem.Collections.Generic.List<ItemData>();
            records.Add(original.GetItemData());
            string inventoryJson = new ItemSet(records).GetJSON();
            if (inventoryJson.Contains(Rules.ItemId, StringComparison.Ordinal) || !inventoryJson.Contains(Rules.Marker, StringComparison.Ordinal))
                throw new InvalidOperationException("Native inventory serialization bypassed the compatible item format.");
            saveContractVerified = true;
            MelonLogger.Msg("[Chemistry Mk II] PASS native save checks: item encoding, marked reload, vanilla fallback, inventory encoding. No disk saves touched.");
            MelonLogger.Msg($"[Chemistry Mk II] Native quality colors: Premium={ItemQuality.GetColor(EQuality.Premium)}, Heavenly={ItemQuality.GetColor(EQuality.Heavenly)}.");
        }
        catch (Exception ex)
        {
            Ready = false;
            // Do not offer a new custom item when its native save path failed.
            if (Registry.ItemExists(Rules.ItemId)) Registry.Instance.RemoveFromRegistry(Definition);
            MelonLogger.Error("[Chemistry Mk II] DISABLED: native save compatibility check failed. " + ex);
        }
        finally { selfTesting = false; }
    }

    private static void RegistryReady() { try { EnsureDefinition(); EnsureOvenDefinition(); EnsureBoardDefinition(); } catch (Exception ex) { Report("registry", ex); } }
    private static void ShopReady(ShopInterface __instance)
    {
        try { if (EnsureDefinition()) { EnsureOvenDefinition(); AddToShop(__instance); RefreshPrices(__instance); } }
        catch (Exception ex) { Report("shop", ex); }
    }

    private static void AddToShop(ShopInterface shop)
    {
        AddBoardToShop(shop);
        AddOvenToShop(shop);
        if (!Ready || !saveContractVerified || shop == null || Definition == null || shop.Listings == null || shop.ListingUIPrefab == null) return;
        if (shop.GetListing(Rules.ItemId) != null) return;
        // Oscar's warehouse is identified by its existing chemistry-station listing,
        // rather than a guessed NPC object name or a locale-dependent display string.
        var original = shop.GetListing(Rules.VanillaId);
        if (original == null) return;
        var listing = new ShopListing
        {
            Item = Definition,
            name = Rules.DisplayName,
            OverridePrice = true,
            OverriddenPrice = Rules.Price,
            LimitedStock = false,
            CanBeDelivered = original.CanBeDelivered,
            UseIconTint = false
        };
        listing.Initialize(shop);
        shop.Listings.Add(listing);
        try { shop.CreateListingUI(listing); }
        catch { shop.Listings.Remove(listing); throw; }
        MelonLogger.Msg($"[Chemistry Mk II] Added {Rules.DisplayName} to {shop.ShopName} ({shop.ShopCode}) for ${Rules.Price}.");
    }

    private static void StationInitialized(ChemistryStation __instance, ItemInstance __0)
    {
        if (__0 == null || __0.ID != Rules.ItemId || __instance.isGhost ||
            !Configured.Add(__instance.GetInstanceID())) return;
        try
        {
            ApplyBenchFinish(__instance.transform);
        }
        catch (Exception ex) { Report("appearance (gameplay remains active)", ex); }
    }

    private static void TimePass(ChemistryStation __instance, ref int __0)
    {
        if (IsMkII(__instance) && __instance.CurrentCookOperation != null)
            __0 = ScaledTick(__instance.Pointer, __instance.CurrentCookOperation.Pointer, __0, Rules.ProcessSpeed);
    }

    private static bool DisposeChemistryTrash(ChemistryStation __instance)
    {
        // This native method only spawns empty ingredient containers. Ingredient
        // consumption, task cleanup and starting the cook happen in the caller.
        // Workers already consume ingredients without spawning these objects.
        if (!Ready || !IsMkII(__instance) || __instance.isGhost || !RuntimeTuning.Current.AutoTrash) return true;
        DebugEvent("chemistry.trash", "Disposed ingredient containers");
        return false;
    }

    private static void ClockUpdated(ChemistryStation __instance)
    {
        if (!IsMkII(__instance) || __instance.CurrentCookOperation == null || __instance.Alarm?.ScreenText == null) return;
        var op = __instance.CurrentCookOperation;
        if (op.Recipe == null) return;
        int minutes = Rules.RemainingMinutes(op.Recipe.CookTime_Mins, op.CurrentTime);
        __instance.Alarm.DisplayMinutes(minutes);
    }

    private static void StationName(ChemistryStation __instance, ref string __result)
    {
        if (IsMkII(__instance)) __result = Rules.DisplayName;
    }

    private static bool HasDoubleOutputSpace(ChemistryStation station, StationRecipe recipe)
    {
        var quality = station.CurrentCookOperation?.ProductQuality ?? recipe.CalculateQuality(station.GetIngredients());
        var probe = recipe.GetProductInstance(quality);
        // Stack compatibility must use the upgraded quality, including when an
        // existing output stack contains a different grade.
        var qualityProbe = probe.TryCast<QualityItemInstance>();
        qualityProbe?.SetQuality((EQuality)Rules.UpgradeQuality((int)quality));
        int required = Rules.OutputQuantity(recipe.Product.Quantity);
        probe.SetQuantity(required);
        return station.OutputSlot.GetCapacityForItem(probe, false) >= required;
    }

    private static void OutputSpace(ChemistryStation __instance, StationRecipe __0, ref bool __result)
    {
        if (IsMkII(__instance) && IsLiquidRecipe(__0))
            __result = HasDoubleOutputSpace(__instance, __0);
    }

    private static bool FinishPrefix(ChemistryStation __instance, out ChemistryStation? __state)
    {
        __state = finishing;
        var operation = __instance.CurrentCookOperation;
        if (!IsMkII(__instance) || operation == null || !IsLiquidRecipe(operation.Recipe)) return true;
        // The native observer also clears client visuals. Only the host produces output;
        // a client must not cancel cleanup based on its potentially newer output slot.
        if (NetworkIsClient) return true;
        if (!HasDoubleOutputSpace(__instance, operation.Recipe)) return false;
        finishing = __instance;
        MelonLogger.Msg($"[Chemistry Mk II] Completing liquid batch: original quality={operation.ProductQuality}; target quantity={Rules.OutputQuantity(operation.Recipe.Product.Quantity)}, target quality={(EQuality)Rules.UpgradeQuality((int)operation.ProductQuality)}.");
        return true;
    }

    private static Exception? FinishFinalizer(Exception? __exception, ChemistryStation? __state)
    {
        finishing = __state; // Also restored if another mod or the native call throws.
        return __exception;
    }

    private static void ProductCreated(StationRecipe __instance, StorableItemInstance __result)
    {
        if (finishing == null || __result == null || !IsLiquidRecipe(__instance)) return;
        var op = finishing.CurrentCookOperation;
        if (op == null || op.RecipeID != __instance.RecipeID) return;
        __result.SetQuantity(Rules.OutputQuantity(__instance.Product.Quantity));
        // Use the operation's original grade; never increment an already-upgraded
        // result. Repeated callbacks therefore cannot turn +1 into +2.
        __result.TryCast<QualityItemInstance>()?.SetQuality(
            (EQuality)Rules.UpgradeQuality((int)op.ProductQuality));
        MelonLogger.Msg($"[Chemistry Mk II] Created output: {__result.Quantity}x {__result.ID}, quality={__result.TryCast<QualityItemInstance>()?.Quality}.");
    }

    private static Type? WorkerRoutineType;

    private static void OvenProductCreated(OvenCookOperation __instance, int __0, ItemInstance __result)
    {
        // Preserve the native liquid's grade through the oven, without awarding
        // another bonus. Applies equally to vanilla liquid and upgraded liquid.
        if (__instance.IngredientID != "liquidmeth" || __result == null) return;
        __result.TryCast<QualityItemInstance>()?.SetQuality(__instance.IngredientQuality);
        if (IsUpgradedOperation(__instance))
        {
            // Native callers request either a one-item capacity probe or the full
            // batch. Derive from the requested quantity, never multiply a patched result.
            __result.SetQuantity(Rules.ScaleOvenRequest(__0));
            __result.TryCast<QualityItemInstance>()?.SetQuality((EQuality)Rules.OvenQuality((int)__instance.IngredientQuality));
            if (__0 > 1 && !ovenTesting)
                DebugEvent("oven.output", $"[Oven Mk II] Created output: {__result.Quantity}x {__result.ID}, quality={__instance.IngredientQuality}.");
        }
    }

    private static void WorkerStep(object __instance, bool __result)
    {
        if (!__result || WorkerRoutineType == null) return;
        var worker = AccessTools.Property(WorkerRoutineType, "__4__this").GetValue(__instance) as StartChemistryStationBehaviour;
        if (worker == null || !IsMkII(worker.targetStation)) return;
        var currentProperty = AccessTools.Property(WorkerRoutineType, "__2__current");
        var current = currentProperty.GetValue(__instance) as Il2CppSystem.Object;
        var wait = current?.TryCast<WaitForSeconds>();
        if (wait != null && wait.m_Seconds > 0)
            currentProperty.SetValue(__instance, new WaitForSeconds(wait.m_Seconds / Rules.WorkerSpeed));
    }

    private static void InterfaceOpened(ChemistryStationInterface __instance, ChemistryStation __0)
        => RefreshRecipeLabels(__instance, __0);

    private static void InterfaceUpdated(ChemistryStationInterface __instance)
        => RefreshRecipeLabels(__instance, __instance.ChemistryStation);

    private static string Duration(int minutes) => minutes % 60 == 0
        ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";

    private static void RefreshRecipeLabels(ChemistryStationInterface ui, ChemistryStation? station)
    {
        if (station == null || ui.recipeEntries == null) return;
        bool upgraded = IsMkII(station);
        foreach (var entry in ui.recipeEntries)
        {
            if (entry?.Recipe == null) continue;
            int minutes = upgraded ? Rules.RemainingMinutes(entry.Recipe.CookTime_Mins, 0) : entry.Recipe.CookTime_Mins;
            entry.CookingTimeLabel.text = Duration(minutes);
            entry.TitleLabel.text = upgraded && IsLiquidRecipe(entry.Recipe) ? "High Quality Liquid Meth" : entry.Recipe.RecipeTitle;
        }
        // The game refreshes the active recipe separately from the recipe list.
        // Rewrite it after each native UI refresh, without modifying shared recipes.
        var current = station.CurrentCookOperation;
        var inProgress = ui.InProgressRecipeEntry;
        if (upgraded && current?.Recipe != null && inProgress?.CookingTimeLabel != null)
        {
            inProgress.CookingTimeLabel.text = Duration(Rules.RemainingMinutes(current.Recipe.CookTime_Mins, current.CurrentTime));
            inProgress.TitleLabel.text = IsLiquidRecipe(current.Recipe) ? "High Quality Liquid Meth" : current.Recipe.RecipeTitle;
        }
    }

    private static void SaveJson(ref string __result)
    {
        // Works for direct item data and JSON-in-JSON property/inventory saves.
        // Never modify the game's actual item definitions or in-memory ID to save.
        if (__result != null && Rules.ContainsCustomId(__result))
            __result = SaveCompatibility.Encode(__result);
    }

    private static void LoadItem(ref string __0)
    {
        if (__0 == null) return;
        if (__0.Contains(Rules.BoardMarker, StringComparison.Ordinal))
        {
            if (EnsureBoardDefinition()) __0 = SaveCompatibility.DecodeItem(__0);
        }
        else if (__0.Contains(Rules.OvenMarker, StringComparison.Ordinal))
        {
            if (EnsureOvenDefinition()) __0 = SaveCompatibility.DecodeItem(__0);
        }
        else if (__0.Contains(Rules.Marker, StringComparison.Ordinal) && EnsureDefinition())
            __0 = SaveCompatibility.DecodeItem(__0);
    }

    private static void Report(string context, Exception ex)
    {
        DebugEvent("error." + context, ex.GetType().Name);
        if (Reported.Add(context)) MelonLogger.Error($"[Chemistry Mk II] {context}: {ex}");
    }
}
