using Il2CppScheduleOne;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Experimental;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.ItemLoaders;
using Il2CppScheduleOne.Skating;
using Il2CppScheduleOne.UI.Shop;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChemistryStationMkII;

public sealed partial class Mod
{
    private static StorableItemDefinition? boardDefinition;
    private static SkateboardSettings? goldenSettings;
    private static bool boardSaveVerified, boardTesting, boardRegistrationFailed;
    private sealed class ElectricState
    {
        internal float LastStep = -1;
        internal bool Failed;
        internal Skateboard? Board;
        internal float LiftStart, LiftBaseY, LiftHeight, LiftApplied;
        internal bool Lifting;
    }
    private static readonly Dictionary<IntPtr, ElectricState> ElectricBoards = new();

    private void InstallBoardPatches()
    {
        // Awake on this seller is an empty shared native method; never patch it.
        Patch(typeof(DialogueController_SkateboardSeller), "ModifyChoiceList", prefix: nameof(BoardSellerReady));
        Patch(typeof(Skateboard_Equippable), "OnMount", postfix: nameof(ElectricMounted));
        Patch(typeof(Skateboard_Equippable), "Equip", postfix: nameof(ElectricEquipped));
        Patch(typeof(Il2CppScheduleOne.UI.DialogueCanvas), "DisplayDialogueNode", prefix: nameof(RestoreBoardShopLayout));
        Patch(typeof(Il2CppScheduleOne.UI.DialogueCanvas), "OnClose", prefix: nameof(RestoreBoardShopLayout));
        Patch(typeof(Skateboard), "OnWeatherChange", postfix: nameof(ElectricWeatherChanged));
        Patch(typeof(Skateboard), "GetInput", postfix: nameof(ElectricInput));
        Patch(typeof(Skateboard), "Push", prefix: nameof(ElectricPush));
        Patch(typeof(Skateboard), "FixedUpdate", prefix: nameof(ElectricPhysics));
        Patch(typeof(Skateboard), "OnDestroy", prefix: nameof(ElectricDestroyed));
    }

    private static bool EnsureBoardDefinition()
    {
        if (!Ready || boardRegistrationFailed || Registry.Instance == null) return false;
        if (Registry.ItemExists(Rules.BoardItemId)) return boardSaveVerified || boardTesting;
        var original = Registry.GetItem<StorableItemDefinition>(Rules.BoardVanillaId);
        if (original == null) return false;
        try
        {
            var equipment = original.Equippable?.TryCast<Skateboard_Equippable>();
            if (equipment == null || equipment.SkateboardPrefab == null || equipment.SkateboardPrefab.DefaultSettings == null)
                throw new InvalidOperationException("Golden skateboard no longer has the expected native equipment/physics data.");
            if (boardDefinition == null)
            {
                goldenSettings = equipment.SkateboardPrefab.DefaultSettings.Clone();
                if (!float.IsFinite(goldenSettings.TopSpeed_Kmh) || goldenSettings.TopSpeed_Kmh <= 0 ||
                    !float.IsFinite(goldenSettings.JumpForce) || goldenSettings.JumpForce <= 0)
                    throw new InvalidOperationException("Golden board speed/jump settings are invalid.");
                boardDefinition = Object.Instantiate(original);
                boardDefinition.name = Rules.BoardItemId;
                boardDefinition.ID = Rules.BoardItemId;
                boardDefinition.Name = Rules.BoardDisplayName;
                boardDefinition.Description = "An all-terrain electric skateboard with smooth acceleration, responsive handling, powerful brakes, and extra lift. Built to glide over curbs and cruise across town.";
                boardDefinition.BasePurchasePrice = Rules.BoardPrice;
                boardDefinition.RequiresLevelToPurchase = false;
                // Retain the registered native golden-board prefab and native item class.
                // Per-ride tuning happens only after mounting; shared assets are untouched.
                Object.DontDestroyOnLoad(boardDefinition);
                SetElectricIcon();
            }
            Registry.Instance.AddToRegistry(boardDefinition);
            VerifyBoardSave();
            return boardSaveVerified;
        }
        catch (Exception ex)
        {
            boardRegistrationFailed = true;
            boardSaveVerified = false;
            if (boardDefinition != null && Registry.ItemExists(Rules.BoardItemId)) Registry.Instance.RemoveFromRegistry(boardDefinition);
            Report("electric skateboard registration; item withheld", ex);
            return false;
        }
    }

    private static void VerifyBoardSave()
    {
        if (boardSaveVerified || boardTesting || boardDefinition == null) return;
        boardTesting = true;
        try
        {
            var item = boardDefinition.GetDefaultInstance(1);
            string json = item.GetItemData().GetJson(false);
            var saved = JObject.Parse(json);
            if ((string?)saved["ID"] != Rules.BoardVanillaId || (int?)saved[Rules.BoardMarker] != 1)
                throw new InvalidOperationException("Electric board did not save with a golden-board fallback.");
            var loader = new ItemLoader();
            var restored = loader.LoadItem(json);
            if (restored == null || restored.ID != Rules.BoardItemId || restored.Quantity != 1)
                throw new InvalidOperationException("Electric board marked reload failed.");
            saved.Remove(Rules.BoardMarker);
            var fallback = loader.LoadItem(saved.ToString());
            if (fallback == null || fallback.ID != Rules.BoardVanillaId)
                throw new InvalidOperationException("Golden board fallback failed.");
            var list = new Il2CppSystem.Collections.Generic.List<ItemData>();
            list.Add(item.GetItemData());
            string inventory = new ItemSet(list).GetJSON();
            if (inventory.Contains(Rules.BoardItemId, StringComparison.Ordinal) || !inventory.Contains(Rules.BoardMarker, StringComparison.Ordinal))
                throw new InvalidOperationException("Electric board inventory serialization bypassed fallback.");
            var probe = goldenSettings!.Clone();
            if (probe.Pointer == goldenSettings.Pointer) throw new InvalidOperationException("Physics clone is shared.");
            float originalSpeed = goldenSettings.TopSpeed_Kmh;
            TuneElectricSettings(probe);
            float firstSpeed = probe.TopSpeed_Kmh, firstJump = probe.JumpForce;
            TuneElectricSettings(probe);
            if (probe.TopSpeed_Kmh != firstSpeed || probe.JumpForce != firstJump || goldenSettings.TopSpeed_Kmh != originalSpeed || firstSpeed != RuntimeTuning.Current.BoardSpeed)
                throw new InvalidOperationException("Electric tuning compounded or changed the golden baseline.");
            boardSaveVerified = true;
            MelonLogger.Msg($"[Electric Skateboard] PASS native item/inventory save, marked reload, golden fallback, isolated/idempotent physics tuning. Golden {originalSpeed:0.#} km/h; electric {firstSpeed:0.#} km/h. Jump force multiplier {Rules.BoardJumpMultiplier:0.##}; curb assistance up to {Rules.BoardMaxStep:0.00}m. No disk saves touched.");
        }
        finally { boardTesting = false; }
    }

    private static void BoardSellerReady(DialogueController_SkateboardSeller __instance)
    {
        try
        {
            if (!EnsureBoardDefinition() || __instance.Options == null) return;
            foreach (var option in __instance.Options)
                if (option.Item != null && option.Item.ID == Rules.BoardItemId) { option.Price = Rules.BoardPrice; return; }
            // Use the seller's own purchase callback, cash deduction, fit checks and events.
            __instance.Options.Add(new DialogueController_SkateboardSeller.Option
            {
                Name = Rules.BoardDisplayName, Item = boardDefinition,
                Price = Rules.BoardPrice, IsAvailable = true, NotAvailableReason = ""
            });
            MelonLogger.Msg($"[Electric Skateboard] Added to Skateboard Shack dialogue for ${Rules.BoardPrice}.");
        }
        catch (Exception ex) { Report("skateboard seller", ex); }
    }

    private static void AddBoardToShop(ShopInterface shop)
    {
        if (!EnsureBoardDefinition() || shop == null || shop.Listings == null || shop.ListingUIPrefab == null ||
            shop.GetListing(Rules.BoardItemId) != null) return;
        var original = shop.GetListing(Rules.BoardVanillaId);
        if (original == null) return;
        var listing = new ShopListing
        {
            Item = boardDefinition, name = Rules.BoardDisplayName,
            OverridePrice = true, OverriddenPrice = Rules.BoardPrice,
            LimitedStock = false, CanBeDelivered = original.CanBeDelivered, UseIconTint = false
        };
        listing.Initialize(shop);
        shop.Listings.Add(listing);
        try { shop.CreateListingUI(listing); }
        catch { shop.Listings.Remove(listing); throw; }
        MelonLogger.Msg($"[Electric Skateboard] Added to {shop.ShopName} for ${Rules.BoardPrice}.");
    }

    private static bool IsElectric(Skateboard? board) => Ready && boardSaveVerified && board != null &&
        board.Equippable != null && board.Equippable.itemInstance != null && board.Equippable.itemInstance.ID == Rules.BoardItemId;

    private static void TuneElectricSettings(SkateboardSettings settings)
    {
        var original = goldenSettings!;
        // Always derive absolute values from our immutable baseline, including after rain.
        settings.TopSpeed_Kmh = RuntimeTuning.Current.BoardSpeed;
        settings.JumpForce = original.JumpForce * Rules.BoardJumpMultiplier;
        settings.TurnForce = original.TurnForce * RuntimeTuning.Current.BoardSteering;
        settings.TurnChangeRate = original.TurnChangeRate * RuntimeTuning.Current.BoardResponse;
        settings.TurnReturnToRestRate = original.TurnReturnToRestRate * RuntimeTuning.Current.BoardReturn;
        settings.TurnSpeedBoost = original.TurnSpeedBoost; // no extra speed exploits by weaving
        settings.BrakeForce = original.BrakeForce * RuntimeTuning.Current.BoardBrakes;
        settings.LateralFrictionForceMultiplier = original.LateralFrictionForceMultiplier * RuntimeTuning.Current.BoardGrip;
        settings.LongitudinalFrictionMultiplier = original.LongitudinalFrictionMultiplier;
    }

    private static void ConfigureElectric(Skateboard board)
    {
        if (goldenSettings == null) return;
        // Even if a game update assigns a shared settings object, clone before mutation.
        board._settings = (board._settings ?? goldenSettings).Clone();
        TuneElectricSettings(board._settings);
        board.TopSpeed_Kmh = board._settings.TopSpeed_Kmh; // legacy speed getter/UI
        board.SlowOnTerrain = !RuntimeTuning.Current.IgnoreTerrain;
        if (!ElectricBoards.ContainsKey(board.Pointer)) ElectricBoards.Add(board.Pointer, new ElectricState { Board = board });
    }

    private static void ElectricMounted(Skateboard_Equippable __instance)
    {
        try
        {
            var board = __instance.ActiveSkateboard;
            if (!IsElectric(board)) return;
            ConfigureElectric(board);
            ApplyElectricVisual(board.transform);
            MelonLogger.Msg("[Electric Skateboard] Mounted: motor, improved handling, higher jump and terrain/curb assistance active.");
        }
        catch (Exception ex) { Report("electric mount", ex); }
    }

    private static void ElectricWeatherChanged(Skateboard __instance)
    {
        try { if (IsElectric(__instance)) ConfigureElectric(__instance); }
        catch (Exception ex) { Report("electric weather", ex); }
    }

    private static void ElectricInput(Skateboard __instance)
    {
        if (IsElectric(__instance) && RuntimeTuning.Current.Motor) __instance.pushQueued = false;
    }

    private static bool ElectricPush(Skateboard __instance)
    {
        if (!IsElectric(__instance) || !RuntimeTuning.Current.Motor) return true;
        __instance.pushQueued = false;
        return false; // Motor replaces the native kick coroutine and its stamina cost.
    }

    private static void ElectricDestroyed(Skateboard __instance) => ElectricBoards.Remove(__instance.Pointer);

    private static void ElectricPhysics(Skateboard __instance)
    {
        if (!IsElectric(__instance) || !__instance.IsOwner) return;
        try
        {
            if (!ElectricBoards.TryGetValue(__instance.Pointer, out var state))
            {
                ConfigureElectric(__instance);
                state = ElectricBoards[__instance.Pointer];
            }
            if (state.Failed || __instance.Rb == null || __instance.Rb.isKinematic ||
                __instance.Equippable.State == null || !__instance.Equippable.State.IsAcceptingInput ||
                !__instance.Equippable.IsRiding || __instance.Rider == null) return;
            if (!RuntimeTuning.Current.CurbAssist) state.Lifting = false;
            if (state.Lifting) ContinueCurbLift(__instance, state);
            if (!__instance.IsGrounded(out RaycastHit ground) || ground.normal.y < .5f) return;
            var body = __instance.Rb;
            var forward = Vector3.ProjectOnPlane(__instance.transform.forward, ground.normal).normalized;
            float throttle = Mathf.Clamp01(GameInput.VehicleDriveAxis);
            if (RuntimeTuning.Current.Motor && throttle > 0 && !__instance.braking)
            {
                float speed = Vector3.ProjectOnPlane(body.velocity, ground.normal).magnitude;
                float room = Mathf.Max(0, __instance._settings.TopSpeed_Kmh / 3.6f - speed);
                float delta = Mathf.Min(Rules.BoardAcceleration * throttle * Time.fixedDeltaTime, room);
                if (delta > 0) body.AddForce(forward * delta, ForceMode.VelocityChange);
            }
            if (RuntimeTuning.Current.CurbAssist && !state.Lifting && !__instance.braking && Time.time - state.LastStep >= RuntimeTuning.Current.CurbCooldown)
                AssistCurb(__instance, state, ground, forward);
        }
        catch (Exception ex)
        {
            if (ElectricBoards.TryGetValue(__instance.Pointer, out var state)) state.Failed = true;
            Report("electric movement; assistance stopped for this ride", ex);
        }
    }

    private static bool IsOwnCollider(Skateboard board, Collider collider) =>
        collider.attachedRigidbody == board.Rb || collider.transform.IsChildOf(board.transform) ||
        (board.Rider != null && collider.transform.IsChildOf(board.Rider.transform));

    private static void AssistCurb(Skateboard board, ElectricState state, RaycastHit ground, Vector3 forward)
    {
        var body = board.Rb;
        float speed = Vector3.Dot(body.velocity, forward);
        if (speed <= .35f || body.velocity.y >= 1.5f || board.timeSinceLastJump < .25f) return;
        Vector3 direction = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        // Start far enough ahead to complete a short eased rise before reaching the curb.
        float lookAhead = Mathf.Clamp(speed * (RuntimeTuning.Current.CurbDuration + .02f) + .12f, .18f, 1.8f);
        if (!body.SweepTest(direction, out RaycastHit face, lookAhead, QueryTriggerInteraction.Ignore) ||
            face.collider == null || IsOwnCollider(board, face.collider) || face.normal.y > .65f ||
            (face.rigidbody != null && !face.rigidbody.isKinematic)) return;
        Vector3 sample = face.point + direction * .16f;
        sample.y = ground.point.y + Rules.BoardMaxStep + .08f;
        if (!Physics.Raycast(sample, Vector3.down, out RaycastHit top, Rules.BoardMaxStep + .12f,
                board.GroundDetectionMask, QueryTriggerInteraction.Ignore) || top.collider == null || IsOwnCollider(board, top.collider)) return;
        float rise = top.point.y - ground.point.y;
        if (!Rules.CanAssistStep(rise, top.normal.y, speed, body.velocity.y)) return;
        Vector3 lift = Vector3.up * (rise + .025f);
        // Clear upward travel, the board's destination, and the rider's headroom.
        // Never disable collision, advance through a wall, climb tall ledges, or lift in air.
        if (body.SweepTest(Vector3.up, out _, lift.y, QueryTriggerInteraction.Ignore)) return;
        foreach (var collider in board.MainColliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger) continue;
            var bounds = collider.bounds;
            var extents = bounds.extents - Vector3.one * .01f;
            extents = Vector3.Max(extents, Vector3.one * .01f);
            foreach (var hit in Physics.OverlapBox(bounds.center + lift, extents, Quaternion.identity,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!IsOwnCollider(board, hit)) return;
        }
        Vector3 standing = body.position;
        standing.y = top.point.y;
        foreach (var hit in Physics.OverlapCapsule(standing + Vector3.up * .6f, standing + Vector3.up * 1.65f,
                     .24f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!IsOwnCollider(board, hit)) return;
        state.Lifting = true;
        state.LiftStart = Time.time;
        state.LiftBaseY = body.position.y;
        state.LiftHeight = lift.y;
        state.LiftApplied = 0;
        state.LastStep = Time.time;
    }

    private static void ContinueCurbLift(Skateboard board, ElectricState state)
    {
        var body = board.Rb;
        // A real jump, braking, or leaving the controls immediately gives physics back.
        if (board.timeSinceLastJump < .25f || board.braking || body.velocity.y > 1.5f)
        { state.Lifting = false; return; }
        float t = Mathf.Clamp01((Time.time - state.LiftStart) / RuntimeTuning.Current.CurbDuration);
        float desired = state.LiftHeight * Rules.CurbEase(t);
        float remaining = Mathf.Max(0, state.LiftBaseY + desired - body.position.y);
        // Bounded incremental movement replaces the single-frame teleport. Never push down.
        float rise = Mathf.Min(remaining, .07f);
        if (rise > 0)
        {
            if (body.SweepTest(Vector3.up, out _, rise, QueryTriggerInteraction.Ignore))
            { state.Lifting = false; return; }
            body.MovePosition(body.position + Vector3.up * rise);
            var velocity = body.velocity;
            if (velocity.y < 0) { velocity.y = 0; body.velocity = velocity; }
        }
        state.LiftApplied = desired;
        if (t >= 1 && remaining <= .075f || Time.time - state.LiftStart > RuntimeTuning.Current.CurbDuration + .1f) state.Lifting = false;
    }
}
