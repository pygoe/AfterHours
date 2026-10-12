using UnityEngine;
using Object = UnityEngine.Object;

namespace ChemistryStationMkII;

public sealed partial class Mod
{
    private static readonly Dictionary<string, Material> BenchMaterials = new();
    private static Mesh? trimCube;

    private static Material BenchMaterial(Material original, string finish)
    {
        string key = original.GetInstanceID() + ":" + finish;
        if (BenchMaterials.TryGetValue(key, out var existing)) return existing;
        var material = new Material(original) { name = "MkII " + finish };
        Color color = finish switch
        {
            "steel" => new Color(.64f, .68f, .69f),
            "teal" => new Color(.12f, .43f, .45f),
            _ => new Color(.13f, .15f, .16f)
        };
        // Native colored texture atlases would otherwise multiply this into blue.
        // Per-instance material replacements retain each renderer's original geometry.
        foreach (string prop in new[] { "_MainTex", "_BaseMap", "_MetallicGlossMap" })
            if (material.HasProperty(prop)) material.SetTexture(prop, null);
        foreach (string prop in new[] { "_Color", "_BaseColor" })
            if (material.HasProperty(prop)) material.SetColor(prop, color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", finish == "steel" ? .7f : .2f);
        foreach (string prop in new[] { "_Smoothness", "_Glossiness" })
            if (material.HasProperty(prop)) material.SetFloat(prop, finish == "steel" ? .4f : .22f);
        material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.DisableKeyword("_EMISSION");
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
        Object.DontDestroyOnLoad(material);
        BenchMaterials[key] = material;
        return material;
    }

    private static void ApplyBenchFinish(Transform root)
    {
        if (root.Find("MkII Finish") != null) return;
        Material? trimMaterial = null;
        Bounds? tableBounds = null, legBounds = null, ovenBounds = null;
        int changed = 0;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool updated = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                string name = material.name.ToLowerInvariant();
                string? finish = null;
                if (name.StartsWith("metal table") || name.StartsWith("2x1 metal table"))
                {
                    finish = renderer.name == "Top" ? "steel" : "graphite";
                    if (renderer.name == "Top") tableBounds = MeshBoundsInRoot(root, renderer);
                    if (renderer.name == "Legs") legBounds = MeshBoundsInRoot(root, renderer);
                    trimMaterial ??= BenchMaterial(material, "teal");
                }
                else if (name.StartsWith("lab oven main"))
                {
                    finish = renderer.name == "Knob" ? "teal" : "graphite";
                    if (renderer.name == "lab oven") ovenBounds = MeshBoundsInRoot(root, renderer);
                }
                else if (name.StartsWith("lab stand mat") || name == "tray mat" || name == "tray mat (instance)")
                    finish = "graphite";
                // Glass, liquids, buttons, timers, lights, product meshes, and animated
                // burner controls never enter this explicit material allow-list.
                if (finish == null) continue;
                materials[i] = BenchMaterial(material, finish);
                updated = true;
                changed++;
            }
            if (updated) renderer.sharedMaterials = materials;
        }
        var trim = new GameObject("MkII Finish");
        trim.layer = root.gameObject.layer;
        trim.transform.SetParent(root, false);
        if (trimMaterial != null)
        {
            if (tableBounds is Bounds table)
            {
                float y = table.min.y - .028f;
                TrimPart(trim.transform, "front trim", new Vector3(table.center.x, y, table.min.z + .005f), new Vector3(table.size.x * .97f, .018f, .012f), trimMaterial);
                TrimPart(trim.transform, "rear trim", new Vector3(table.center.x, y, table.max.z - .005f), new Vector3(table.size.x * .97f, .018f, .012f), trimMaterial);
            }
            if (legBounds is Bounds legs)
                foreach (float x in new[] { legs.min.x + .027f, legs.max.x - .027f })
                    foreach (float z in new[] { legs.min.z + .027f, legs.max.z - .027f })
                        TrimPart(trim.transform, "foot trim", new Vector3(x, legs.min.y + .022f, z), new Vector3(.062f, .028f, .062f), trimMaterial);
            if (ovenBounds is Bounds oven)
            {
                float y = oven.min.y + oven.size.y * .78f;
                foreach (float z in new[] { oven.min.z - .002f, oven.max.z + .002f })
                    TrimPart(trim.transform, "oven trim", new Vector3(oven.center.x, y, z), new Vector3(oven.size.x * .97f, .015f, .006f), trimMaterial);
            }
        }
        MelonLoader.MelonLogger.Msg($"[Mk II finish] Applied graphite / steel with subtle teal trim to {changed} material slots; native geometry, controls and colliders retained.");
    }

    private static Bounds MeshBoundsInRoot(Transform root, MeshRenderer renderer)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        var bounds = filter.sharedMesh.bounds;
        var result = new Bounds(root.InverseTransformPoint(renderer.transform.TransformPoint(bounds.center)), Vector3.zero);
        foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float y in new[] { bounds.min.y, bounds.max.y })
                foreach (float z in new[] { bounds.min.z, bounds.max.z })
                    result.Encapsulate(root.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(x, y, z))));
        return result;
    }

    private static void TrimPart(Transform root, string name, Vector3 position, Vector3 size, Material material)
    {
        if (trimCube == null)
        {
            trimCube = new Mesh { name = "MkII cosmetic trim" };
            trimCube.vertices = new[] { new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f) };
            trimCube.triangles = new[] {0,1,2,0,2,3,4,7,6,4,6,5,0,4,5,0,5,1,1,5,6,1,6,2,2,6,7,2,7,3,3,7,4,3,4,0};
            trimCube.RecalculateNormals();
            trimCube.RecalculateBounds();
            Object.DontDestroyOnLoad(trimCube);
        }
        var part = new GameObject(name);
        part.layer = root.gameObject.layer;
        part.transform.SetParent(root, false);
        part.transform.localPosition = position;
        part.transform.localScale = size;
        part.AddComponent<MeshFilter>().sharedMesh = trimCube;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
    }
}
