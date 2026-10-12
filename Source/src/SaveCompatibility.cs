using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChemistryStationMkII;

// Extra JSON fields are ignored by vanilla's native data classes. The original ID
// stays loadable without this DLL; our item loader restores the upgrade when present.
internal static class SaveCompatibility
{
    internal static string Encode(string json)
    {
        if (string.IsNullOrEmpty(json) || !Rules.ContainsCustomId(json))
            return json;
        var root = JToken.Parse(json);
        return Rewrite(root) ? root.ToString(Formatting.None) : json;
    }

    private static bool Rewrite(JToken token)
    {
        bool changed = false;
        if (token is JObject obj)
        {
            string? id = (string?)obj["ID"];
            if (id == Rules.ItemId || id == Rules.OvenItemId || id == Rules.BoardItemId)
            {
                bool oven = id == Rules.OvenItemId;
                bool board = id == Rules.BoardItemId;
                obj["ID"] = board ? Rules.BoardVanillaId : oven ? Rules.OvenVanillaId : Rules.VanillaId;
                obj[board ? Rules.BoardMarker : oven ? Rules.OvenMarker : Rules.Marker] = 1;
                changed = true;
            }
        }
        foreach (var child in token.Children().ToArray())
        {
            if (child is JValue value && value.Type == JTokenType.String)
            {
                string text = (string?)value ?? "";
                if (Rules.ContainsCustomId(text) &&
                    (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("[")))
                {
                    // A nested JSON save string, not a raw string replacement.
                    try
                    {
                        string encoded = Encode(text);
                        if (encoded != text) { value!.Value = encoded; changed = true; }
                    }
                    catch (JsonException) { /* Unrelated user text stays verbatim. */ }
                }
            }
            else changed |= Rewrite(child);
        }
        return changed;
    }

    internal static string DecodeItem(string json)
    {
        if (string.IsNullOrEmpty(json) ||
            (!json.Contains(Rules.Marker, StringComparison.Ordinal) && !json.Contains(Rules.OvenMarker, StringComparison.Ordinal) &&
             !json.Contains(Rules.BoardMarker, StringComparison.Ordinal)))
            return json;
        var item = JObject.Parse(json);
        bool oven = (string?)item["ID"] == Rules.OvenVanillaId && (int?)item[Rules.OvenMarker] == 1;
        bool chemistry = (string?)item["ID"] == Rules.VanillaId && (int?)item[Rules.Marker] == 1;
        bool board = (string?)item["ID"] == Rules.BoardVanillaId && (int?)item[Rules.BoardMarker] == 1;
        if (!oven && !chemistry && !board)
            return json;
        item["ID"] = board ? Rules.BoardItemId : oven ? Rules.OvenItemId : Rules.ItemId;
        item.Remove(board ? Rules.BoardMarker : oven ? Rules.OvenMarker : Rules.Marker);
        return item.ToString(Formatting.None);
    }
}
