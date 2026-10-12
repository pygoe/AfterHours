using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChemistryStationMkII;

internal sealed class SessionMessage
{
    public int Protocol = 1;
    public string Kind = "";
    public string Build = "";
    public string Game = "";
    public string Session = "";
    public int Revision;
    public Tuning? Settings;
}

internal static class NetworkProtocol
{
    internal const int MaximumBytes = 8192;
    internal static SessionMessage Parse(string json, string build, string game)
    {
        if (json.Length > MaximumBytes) throw new FormatException("Settings packet exceeds size limit.");
        using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 8 };
        var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new FormatException("Trailing packet content.");
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        { TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error });
        var message = root.ToObject<SessionMessage>(serializer) ?? throw new FormatException("Empty packet.");
        if (message.Protocol != 1 || message.Build != build || message.Game != game)
            throw new FormatException("Mod build or game version differs. Install the same release on every PC.");
        if (!Guid.TryParseExact(message.Session, "N", out _) || message.Revision < 1 || message.Kind is not ("settings" or "ack"))
            throw new FormatException("Invalid session message.");
        if (message.Kind == "settings")
        {
            if (message.Settings == null || root["Settings"] is not JObject fields ||
                fields.Count != typeof(Tuning).GetFields().Length || fields.Properties().Any(p => p.Value.Type == JTokenType.Null))
                throw new FormatException("Incomplete settings manifest.");
            if (JsonConvert.SerializeObject(message.Settings) != JsonConvert.SerializeObject(message.Settings.Normalize()))
                throw new FormatException("Host settings are outside supported ranges.");
        }
        else if (message.Settings != null) throw new FormatException("Clients cannot supply gameplay settings.");
        return message;
    }
}
