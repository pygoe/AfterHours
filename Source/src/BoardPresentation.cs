using System.Reflection;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Skating;
using Il2CppScheduleOne.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChemistryStationMkII;

public sealed partial class Mod
{
    private static readonly Dictionary<string, Mesh> BoardMeshes = new();
    private static readonly Dictionary<string, Material> BoardMaterials = new();
    private static readonly Dictionary<string, Sprite> ItemIcons = new();
    private static JObject? boardVisualData;
    private static RectTransform? shiftedShop;
    private static Vector2 originalShopPosition;
    private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> UiCorners = new(4);

    private static byte[] ReadAsset(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static Sprite LoadItemIcon(string file)
    {
        if (ItemIcons.TryGetValue(file, out var existing) && existing != null) return existing;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.name = "MkII " + file;
        if (!ImageConversion.LoadImage(texture, ReadAsset(file), true))
        { Object.Destroy(texture); throw new InvalidOperationException("Could not decode " + file); }
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f),
            Mathf.Max(texture.width, texture.height) / 2.56f);
        Object.DontDestroyOnLoad(texture);
        Object.DontDestroyOnLoad(sprite);
        ItemIcons[file] = sprite;
        return sprite;
    }

    private static void SetElectricIcon()
    {
        try { if (boardDefinition != null) boardDefinition.Icon = LoadItemIcon("electric-board-icon.png"); }
        catch (Exception ex) { Report("electric icon; original retained", ex); }
    }

    private static void ElectricEquipped(Skateboard_Equippable __instance)
    {
        try
        {
            if (__instance.itemInstance?.ID == Rules.BoardItemId) ApplyElectricVisual(__instance.transform);
        }
        catch (Exception ex) { Report("electric held model", ex); }
    }

    private static void ApplyElectricVisual(Transform scope)
    {
        // The native model anchors are verified against this game's serialized prefab.
        // Keep the board, IK points, lean transforms and colliders; replace renderers only.
        Transform? anchor = null;
        foreach (var t in scope.GetComponentsInChildren<Transform>(true))
            if (t.name == "GoldSkateboardVisual") { anchor = t; break; }
        if (anchor == null) throw new InvalidOperationException("Golden visual attachment point is missing.");
        var installed = anchor.Find("CircuitVisual");
        if (installed != null) { SuppressNativeBoardMeshes(scope, installed); return; }
        var old = scope.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.sharedMaterials.Any(m => m != null && m.name.Contains("skateboard", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var template = old.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.name.Contains("gold skateboard", StringComparison.OrdinalIgnoreCase));
        if (template == null) throw new InvalidOperationException("Native board material is missing.");
        var model = boardVisualData ??= JObject.Parse(System.Text.Encoding.UTF8.GetString(ReadAsset("electric-board.json")));
        var visual = new GameObject("CircuitVisual");
        visual.SetActive(false);
        visual.transform.SetParent(anchor, false);
        try
        {
            foreach (JObject group in (JArray)model["groups"]!)
            {
                string name = (string)group["name"]!;
                if (!BoardMeshes.TryGetValue(name, out var mesh))
                {
                    var points = ((JArray)group["vertices"]!).Select(p => new Vector3((float)p[0]!, (float)p[1]!, (float)p[2]!)).ToArray();
                    var indices = ((JArray)group["triangles"]!).Select(p => (int)p).ToArray();
                    mesh = new Mesh { name = "Circuit " + name };
                    mesh.vertices = points;
                    mesh.triangles = indices;
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    Object.DontDestroyOnLoad(mesh);
                    BoardMeshes.Add(name, mesh);
                }
                if (!BoardMaterials.TryGetValue(name, out var material))
                {
                    var c = model["colors"]![name]!;
                    material = new Material(template) { name = "Circuit " + name };
                    foreach (string prop in new[] { "_MainTex", "_BaseMap", "_BumpMap", "_MetallicGlossMap" })
                        if (material.HasProperty(prop)) material.SetTexture(prop, null);
                    var color = new Color((float)c[0]!, (float)c[1]!, (float)c[2]!, 1);
                    foreach (string prop in new[] { "_Color", "_BaseColor" })
                        if (material.HasProperty(prop)) material.SetColor(prop, color);
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", name == "metal" ? .6f : .05f);
                    foreach (string prop in new[] { "_Smoothness", "_Glossiness" })
                        if (material.HasProperty(prop)) material.SetFloat(prop, name == "metal" ? .42f : .18f);
                    material.DisableKeyword("_EMISSION");
                    material.DisableKeyword("_NORMALMAP");
                    material.DisableKeyword("_METALLICSPECGLOSSMAP");
                    if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
                    Object.DontDestroyOnLoad(material);
                    BoardMaterials.Add(name, material);
                }
                var part = new GameObject(name);
                part.layer = anchor.gameObject.layer;
                part.transform.SetParent(visual.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            SuppressNativeBoardMeshes(scope, visual.transform);
            visual.SetActive(true);
            MelonLoader.MelonLogger.Msg("[Electric Skateboard] Circuit model applied; native physics and rider alignment retained.");
        }
        catch { Object.Destroy(visual); throw; }
    }

    private static void SuppressNativeBoardMeshes(Transform scope, Transform circuit)
    {
        int detached = 0;
        foreach (var renderer in scope.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.transform.IsChildOf(circuit) ||
                !renderer.sharedMaterials.Any(m => m != null && m.name.Contains("skateboard", StringComparison.OrdinalIgnoreCase))) continue;
            // A one-time enabled=false can be undone by native visibility handling.
            // Clear only this instance's visual MeshFilter assignment. Keep components,
            // animated transforms, native MeshCollider.sharedMesh and shared prefab assets.
            renderer.enabled = false;
            renderer.forceRenderingOff = true;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) { filter.sharedMesh = null; detached++; }
        }
        if (detached > 0) DebugEvent("board.visual-replacement", $"Detached {detached} native board visual meshes; Circuit geometry and colliders retained.");
    }

    private static void RestoreBoardShopLayout()
    {
        if (shiftedShop != null) shiftedShop.anchoredPosition = originalShopPosition;
        shiftedShop = null;
    }

    public override void OnLateUpdate()
    {
        if (!Ready) return;
        try
        {
            var ui = DialogueCanvas.Instance;
            if (ui == null || !ui.IsOpen || !IsElectricBoardMenu(ui))
            { RestoreBoardShopLayout(); return; }
            if (ui.Container == null || ui.dialogueChoices == null) return;
            if (shiftedShop == null)
            { shiftedShop = ui.Container; originalShopPosition = shiftedShop.anchoredPosition; }
            shiftedShop.anchoredPosition = originalShopPosition;
            Camera? camera = ui.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : ui.canvas.worldCamera;
            float minY = float.MaxValue;
            foreach (var choice in ui.dialogueChoices)
            {
                if (choice.GameObject == null || !choice.GameObject.activeInHierarchy) continue;
                var rect = choice.GameObject.transform.TryCast<RectTransform>();
                if (rect == null) continue;
                rect.GetWorldCorners(UiCorners);
                for (int i = 0; i < 4; i++) minY = Mathf.Min(minY, RectTransformUtility.WorldToScreenPoint(camera, UiCorners[i]).y);
            }
            if (minY == float.MaxValue) return;
            float safeBottom = Screen.height * .16f;
            var inventory = PlayerInventory.Instance;
            if (inventory?.SlotUIs != null)
                foreach (var slot in inventory.SlotUIs)
                {
                    if (slot == null || !slot.gameObject.activeInHierarchy) continue;
                    var rect = slot.transform.TryCast<RectTransform>();
                    if (rect == null) continue;
                    var canvas = slot.GetComponentInParent<Canvas>();
                    Camera? slotCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    rect.GetWorldCorners(UiCorners);
                    for (int i = 0; i < 4; i++) safeBottom = Mathf.Max(safeBottom, RectTransformUtility.WorldToScreenPoint(slotCamera, UiCorners[i]).y + 16f);
                }
            float shift = Mathf.Max(0, safeBottom - minY);
            if (shift <= 0) return;
            // Convert screen pixels through the canvas scale; do not reorder choices or input bindings.
            shiftedShop.anchoredPosition = originalShopPosition + Vector2.up * (shift / Mathf.Max(.01f, ui.canvas.scaleFactor));
        }
        catch (Exception ex) { RestoreBoardShopLayout(); Report("skateboard shop layout", ex); }
    }

    private static bool IsElectricBoardMenu(DialogueCanvas ui)
    {
        if (ui.dialogueChoices == null) return false;
        foreach (var choice in ui.dialogueChoices)
            if (choice.GameObject != null && choice.GameObject.activeInHierarchy && choice.Label != null &&
                choice.Label.text.Contains(Rules.BoardDisplayName, StringComparison.Ordinal)) return true;
        return false;
    }
}
