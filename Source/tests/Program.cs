using ChemistryStationMkII;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

int count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    count++;
}

Check(Rules.Advance(1) == 4, "one minute advances four native minutes");
Check(Rules.Advance(60) == 240, "sleep/time skip receives the same speed multiplier");
Check(Rules.Advance(0) == 0 && Rules.Advance(-1) == -1, "zero/negative time does not advance");
Check(Rules.Advance(int.MaxValue) == int.MaxValue, "large time jump cannot wrap negative");
Check(Rules.RemainingMinutes(120, 0) == 30, "two-hour native cycle displays thirty minutes");
Check(Rules.RemainingMinutes(120, 119) == 1, "remaining time rounds up");
Check(Rules.RemainingMinutes(120, 125) == 0, "completed cycle displays zero");
Check(Rules.OutputQuantity(1) == 2, "one native liquid output becomes two");
Check(Rules.OutputQuantity(3) == 6, "yield uses the recipe's quantity");
Check(Rules.UpgradeQuality(1) == 2, "low-quality pseudo advances to Standard");
Check(Rules.UpgradeQuality(2) == 3, "regular pseudo advances to Premium/purple");
Check(Rules.UpgradeQuality(3) == 4, "high-quality pseudo advances to Heavenly/gold");
Check(Rules.UpgradeQuality(4) == 4, "Heavenly quality is capped");
Check(Rules.UpgradeQuality(0) == 1, "lowest quality advances only one tier");
Check(Rules.RemainingMinutes(480, 0) == 120, "eight-hour vanilla cycle displays two hours on Mk II");

string item = new JObject { ["DataType"] = "ItemData", ["ID"] = Rules.ItemId, ["Quantity"] = 2 }.ToString();
string saved = SaveCompatibility.Encode(item);
var vanilla = JObject.Parse(saved);
Check((string?)vanilla["ID"] == Rules.VanillaId, "saved item uses an existing vanilla ID");
Check((int?)vanilla["Quantity"] == 2, "fallback preserves inventory quantity");
Check((int?)vanilla[Rules.Marker] == 1, "save keeps the optional upgrade marker");
Check(JToken.DeepEquals(JToken.Parse(item), JToken.Parse(SaveCompatibility.DecodeItem(saved))), "installed-mod load restores the exact custom item");
Check(SaveCompatibility.Encode(saved) == saved, "save transformation is idempotent");

string liquid = new JObject { ["DataType"] = "QualityItemData", ["ID"] = "liquidmeth", ["Quantity"] = 2, ["Quality"] = "Heavenly" }.ToString();
string station = new JObject
{
    ["DataType"] = "ChemistryStationData", ["GUID"] = "test-station-guid",
    ["ItemString"] = item, ["CurrentTime"] = 84,
    ["OutputContents"] = new JObject { ["Items"] = new JArray(liquid) }
}.ToString();
string property = new JObject
{
    ["Objects"] = new JArray(new JObject { ["BaseData"] = station, ["AdditionalDatas"] = new JArray() }),
    ["Inventory"] = new JArray(item),
    ["PlayerNote"] = "Keep " + Rules.ItemId + " for solo"
}.ToString();
var transformed = JObject.Parse(SaveCompatibility.Encode(property));
var stationRoundTrip = JObject.Parse((string)transformed["Objects"]![0]!["BaseData"]!);
var stationItem = JObject.Parse((string)stationRoundTrip["ItemString"]!);
Check((string?)stationItem["ID"] == Rules.VanillaId, "nested property save has vanilla-compatible station ID");
Check((int?)stationRoundTrip["CurrentTime"] == 84, "partial cooking progress survives save");
Check((string?)stationRoundTrip["GUID"] == "test-station-guid", "worker assignment identity survives save");
Check((string?)stationRoundTrip["OutputContents"]!["Items"]![0] == liquid, "native Heavenly output is preserved verbatim");
Check((string?)transformed["PlayerNote"] == "Keep " + Rules.ItemId + " for solo", "unrelated player text is not renamed");
Check((string?)JObject.Parse((string)transformed["Inventory"]![0]!)["ID"] == Rules.VanillaId, "inventory item fallback is handled too");
Check(SaveCompatibility.DecodeItem(liquid) == liquid, "ordinary liquid item load is unchanged");
string unrelated = "{\"ID\":\"storage\",\"ChemistryStationMkII\":1}";
Check(SaveCompatibility.DecodeItem(unrelated) == unrelated, "marker cannot change an unrelated item");
string unmarked = "{\"ID\":\"chemistrystation\",\"Quantity\":1}";
Check(SaveCompatibility.DecodeItem(unmarked) == unmarked, "ordinary chemistry station is never upgraded");
Check(Rules.OvenOutputQuantity(1) == 20, "oven produces twenty meth per liquid");
Check(Rules.OvenOutputQuantity(2) == 40, "oven yield scales with actual consumed input");
Check(Rules.OvenRemainingMinutes(360, 0) == 60, "six-hour oven cycle takes one hour");
Check(Rules.OvenAdvance(1) == 6, "oven minute tick advances six native minutes");
Check(Rules.OvenAdvance(60) == 360, "one-hour time skip finishes one oven batch");
Check(Rules.OvenAdvance(0) == 0 && Rules.OvenAdvance(-1) == -1, "oven does not advance on zero or negative time");
Check(Rules.OvenAdvance(int.MaxValue) == int.MaxValue, "oven time skip cannot wrap negative");
Check(Rules.OvenRemainingMinutes(360, 180) == 30, "half-cooked saved oven batch has thirty minutes remaining");
Check(Rules.OvenRemainingMinutes(360, 359) == 1 && Rules.OvenRemainingMinutes(360, 360) == 0, "oven countdown rounds up and stops at zero");
Check(Rules.OvenRemainingMinutes(360, 400) == 0, "oven countdown never goes negative after sleep");
string ovenItem = new JObject { ["ID"] = Rules.OvenItemId, ["Quantity"] = 1 }.ToString();
string savedOven = SaveCompatibility.Encode(ovenItem);
var fallbackOven = JObject.Parse(savedOven);
Check((string?)fallbackOven["ID"] == Rules.OvenVanillaId && (int?)fallbackOven[Rules.OvenMarker] == 1, "oven saves with its own native fallback and marker");
Check(JToken.DeepEquals(JToken.Parse(ovenItem), JToken.Parse(SaveCompatibility.DecodeItem(savedOven))), "oven round trip restores the exact upgrade");
Check(SaveCompatibility.Encode(savedOven) == savedOven, "oven encoding is idempotent");
string ovenBatch = new JObject
{
    ["ItemString"] = ovenItem, ["GUID"] = "oven-worker-guid", ["CurrentIngredientID"] = "liquidmeth",
    ["CurrentIngredientQuantity"] = 1, ["CurrentIngredientQuality"] = "Heavenly",
    ["CurrentProductID"] = "meth", ["CurrentCookProgress"] = 96,
    ["OutputContents"] = new JObject { ["Items"] = new JArray("{\"ID\":\"meth\",\"Quantity\":20,\"Quality\":\"Heavenly\"}") }
}.ToString();
string combinedSave = new JObject { ["Objects"] = new JArray(station, ovenBatch), ["Inventory"] = new JArray(item, ovenItem) }.ToString();
var combined = JObject.Parse(SaveCompatibility.Encode(combinedSave));
var restoredBatch = JObject.Parse((string)combined["Objects"]![1]!);
Check(!Rules.ContainsCustomId(combined.ToString()), "mixed property save has no custom station IDs");
Check((int?)restoredBatch["CurrentCookProgress"] == 96 && (int?)restoredBatch["CurrentIngredientQuantity"] == 1, "oven reload keeps progress and input quantity without duplicating input");
Check((string?)restoredBatch["CurrentIngredientQuality"] == "Heavenly", "oven reload preserves gold batch quality");
Check((string?)restoredBatch["GUID"] == "oven-worker-guid", "oven worker identity survives encoding");
Check(JToken.DeepEquals(JObject.Parse(ovenBatch)["OutputContents"], restoredBatch["OutputContents"]), "finished oven output quantity and grade survive encoding");
string baseOven = "{\"ID\":\"laboven\",\"Quantity\":1}";
Check(SaveCompatibility.DecodeItem(baseOven) == baseOven, "ordinary ovens never become upgrades");
string wrongMarker = "{\"ID\":\"chemistrystation\",\"LabOvenMkII\":1}";
Check(SaveCompatibility.DecodeItem(wrongMarker) == wrongMarker, "oven marker cannot upgrade a chemistry station");
string wrongOvenMarker = "{\"ID\":\"laboven\",\"ChemistryStationMkII\":1}";
Check(SaveCompatibility.DecodeItem(wrongOvenMarker) == wrongOvenMarker, "chemistry marker cannot upgrade an oven");
string boardItem = new JObject { ["DataType"] = "ItemData", ["ID"] = Rules.BoardItemId, ["Quantity"] = 1 }.ToString();
string boardSaved = SaveCompatibility.Encode(boardItem);
var boardFallback = JObject.Parse(boardSaved);
Check((string?)boardFallback["ID"] == Rules.BoardVanillaId && (int?)boardFallback[Rules.BoardMarker] == 1, "electric board saves as native golden board with its own marker");
Check(JToken.DeepEquals(JToken.Parse(boardItem), JToken.Parse(SaveCompatibility.DecodeItem(boardSaved))), "electric board restores exact ID, type and quantity");
Check(SaveCompatibility.Encode(boardSaved) == boardSaved, "electric board save encoding does not compound");
string plainGold = "{\"ID\":\"goldenskateboard\",\"Quantity\":1}";
Check(SaveCompatibility.DecodeItem(plainGold) == plainGold, "owned golden boards never automatically upgrade");
foreach (var id in new[] { Rules.VanillaId, Rules.OvenVanillaId, "skateboard", "meth" })
{
    string wrongBoard = new JObject { ["ID"] = id, [Rules.BoardMarker] = 1 }.ToString();
    Check(SaveCompatibility.DecodeItem(wrongBoard) == wrongBoard, "electric marker cannot change " + id);
}
foreach (var marker in new[] { Rules.Marker, Rules.OvenMarker })
{
    string wrongGold = new JObject { ["ID"] = Rules.BoardVanillaId, [marker] = 1 }.ToString();
    Check(SaveCompatibility.DecodeItem(wrongGold) == wrongGold, "bench marker cannot change a golden board: " + marker);
}
string allItems = new JObject { ["Items"] = new JArray(item, ovenItem, boardItem, plainGold, liquid) }.ToString();
var allEncoded = JObject.Parse(SaveCompatibility.Encode(allItems));
Check(!Rules.ContainsCustomId(allEncoded.ToString()), "mixed bench, oven and board inventory has only native fallback IDs");
Check(JToken.DeepEquals(JToken.Parse(boardItem), JToken.Parse(SaveCompatibility.DecodeItem((string)allEncoded["Items"]![2]!))), "board restores from nested inventory JSON");
Check((string?)allEncoded["Items"]![3] == plainGold && (string?)allEncoded["Items"]![4] == liquid, "ordinary board and Heavenly products stay verbatim in mixed inventory");
Check(Rules.CanAssistStep(.2f, 1, 5, 0), "curb assist permits a small flat curb while moving on the ground");
Check(Rules.CanAssistStep(Rules.BoardMaxStep, .9f, 5, 0), "curb assistance includes its bounded maximum");
Check(!Rules.CanAssistStep(.6f, 1, 5, 0), "curb assist rejects tall ledges");
Check(!Rules.CanAssistStep(-.2f, 1, 5, 0) && !Rules.CanAssistStep(0, 1, 5, 0), "curb assist never lifts for downhill or level ground");
Check(!Rules.CanAssistStep(.2f, .4f, 5, 0), "curb assist rejects steep wall-like landing surfaces");
Check(!Rules.CanAssistStep(.2f, 1, 0, 0), "curb assist cannot repeatedly lift a stationary board");
Check(!Rules.CanAssistStep(.2f, 1, 5, 2), "curb assist does not add lift to an active upward jump");
Check(!Rules.CanAssistStep(float.NaN, 1, 5, 0) && !Rules.CanAssistStep(float.PositiveInfinity, 1, 5, 0), "invalid curb heights are rejected");
Check(Rules.CurbEase(-1) == 0 && Rules.CurbEase(2) == 1, "curb transition clamps time at both ends");
Check(Rules.CurbEase(0) == 0 && Rules.CurbEase(1) == 1, "curb transition begins at ground level and finishes at exact target");
Check(Rules.CurbEase(.05f) < .01f && Rules.CurbEase(.95f) > .99f, "curb lift eases into and out of movement");
float priorLift = 0;
bool monotonic = true;
for (int i = 1; i <= 100; i++) { float lift = Rules.CurbEase(i / 100f); monotonic &= lift >= priorLift && lift <= 1; priorLift = lift; }
Check(monotonic, "curb transition does not reverse, bounce or overshoot mathematically");

// Duration carry must finish on the requested tick at every supported duration.
foreach (int native in new[] { 480, 360 })
{
    bool exact = true, noEarlyFinish = true;
    for (int duration = 1; duration <= 1440; duration++)
    {
        double carry = 0; int total = 0;
        for (int tick = 1; tick <= duration; tick++)
        {
            total += RuntimeTuning.ScaleMinutes(1, native / (double)duration, ref carry);
            if (tick < duration && total >= native) noEarlyFinish = false;
        }
        exact &= total == native;
    }
    Check(exact && noEarlyFinish, $"{native}-minute recipe completes exactly across all 1440 supported durations");
}
double fraction = 0;
int beforeSleep = RuntimeTuning.ScaleMinutes(1, 480d / 137, ref fraction);
Check(beforeSleep + RuntimeTuning.ScaleMinutes(136, 480d / 137, ref fraction) == 480, "fractional rate survives sleep/time skip without losing output time");
fraction = .75;
Check(RuntimeTuning.ScaleMinutes(0, 4, ref fraction) == 0 && fraction == .75 && RuntimeTuning.ScaleMinutes(-4, 4, ref fraction) == -4, "nonpositive ticks preserve fractional carry");
Check(RuntimeTuning.ScaleMinutes(int.MaxValue, 480, ref fraction) == int.MaxValue && fraction == 0, "extreme time skip saturates safely");
var unsafeTuning = new Tuning { ChemMinutes = 0, OvenMinutes = -5, ChemYield = 99, OvenYield = int.MaxValue,
    BoardSpeed = float.NaN, CurbHeight = float.PositiveInfinity, BoardJump = -1,
    ChemQuality = (QualityChoice)999, OvenQualityBoost = int.MaxValue };
var normalized = unsafeTuning.Normalize();
Check(normalized.ChemMinutes == 1 && normalized.OvenMinutes == 1 && normalized.ChemYield == 20 && normalized.OvenYield == 200, "invalid duration and yield preferences clamp to supported ranges");
Check(normalized.BoardSpeed == 44.8f && normalized.CurbHeight == .38f && normalized.BoardJump == .5f, "NaN/infinity use defaults and finite physics values clamp");
Check(normalized.ChemQuality == QualityChoice.FollowIngredients && normalized.OvenQualityBoost == 4, "invalid enum and quality boost cannot escape native quality tiers");
Check(float.IsNaN(unsafeTuning.BoardSpeed), "normalization leaves the source snapshot untouched");
Check(JsonConvert.SerializeObject(normalized) == JsonConvert.SerializeObject(normalized.Normalize()), "normalization is idempotent");
RuntimeTuning.Current = new Tuning { ChemMinutes = 137, OvenMinutes = 90, ChemYield = 3, OvenYield = 37, ChemQualityBoost = -1, OvenQuality = QualityChoice.Heavenly };
Check(Rules.RemainingMinutes(480, 0) == 137 && Rules.OvenRemainingMinutes(360, 0) == 90, "configured recipe displays use chosen durations");
Check(Rules.OutputQuantity(2) == 6 && Rules.OvenOutputQuantity(2) == 74 && Rules.ScaleOvenRequest(20) == 74, "oven yield is independent of chemistry yield");
Check(Rules.ScaleOvenRequest(1) == 4 && Rules.UpgradeQuality(3) == 2 && Rules.OvenQuality(1) == 4, "capacity probes round up and quality controls use original ingredient grade");
RuntimeTuning.Current = new Tuning { OvenYield = 1, ChemQuality = QualityChoice.Poor };
Check(Rules.ScaleOvenRequest(1) == 1 && Rules.ScaleOvenRequest(10) == 1, "low oven yields still use a nonzero capacity probe");
Check(Rules.UpgradeQuality(4) == 1 && Rules.OvenQuality(4) == 4, "fixed chemistry grade does not change default oven quality");
RuntimeTuning.Current = new Tuning();
for (int quality = 0; quality <= 4; quality++)
    Check(Rules.OvenQuality(quality) == quality, $"default oven preserves native grade {quality}");
var message = new SessionMessage { Kind = "settings", Build = "test-build", Game = "test-game", Session = Guid.NewGuid().ToString("N"), Revision = 1, Settings = new Tuning() };
string packet = JsonConvert.SerializeObject(message);
var decoded = NetworkProtocol.Parse(packet, "test-build", "test-game");
Check(JsonConvert.SerializeObject(decoded.Settings) == JsonConvert.SerializeObject(message.Settings), "host manifest round-trips every gameplay preference");
Check(System.Text.Encoding.UTF8.GetByteCount(packet) < NetworkProtocol.MaximumBytes, "complete manifest fits bounded wire packet");
void Reject(string body, string name)
{
    bool rejected = false;
    try { NetworkProtocol.Parse(body, "test-build", "test-game"); } catch (Exception) { rejected = true; }
    Check(rejected, name);
}
Reject(packet.Replace("test-build", "wrong-build"), "mismatching DLL build rejected");
Reject(packet.Replace("test-game", "wrong-game"), "mismatching game build rejected");
Reject(packet.Replace("\"Protocol\":1", "\"Protocol\":2"), "unknown network protocol rejected");
Reject(packet.Replace("\"Revision\":1", "\"Revision\":0"), "invalid revision rejected");
Reject(packet.Replace(message.Session, "not-a-session"), "invalid session ID rejected");
Reject(packet.Replace("\"ChemMinutes\":120,", ""), "incomplete host manifest rejected instead of silently defaulting");
Reject(packet.Replace("\"ChemMinutes\":120", "\"ChemMinutes\":0"), "out-of-range host manifest rejected");
Reject(packet.Replace("\"BoardSpeed\":44.8", "\"BoardSpeed\":\"NaN\""), "nonfinite network physics rejected");
Reject(packet.Replace("\"ChemMinutes\":120", "\"ChemMinutes\":null"), "null settings rejected");
Reject(packet.Replace("\"ChemMinutes\":120", "\"ChemMinutes\":120,\"ChemMinutes\":60"), "duplicate JSON settings rejected");
Reject(packet.Replace("\"ChemMinutes\":120", "\"ChemMinutes\":120,\"InjectedField\":true"), "unknown network fields rejected");
Reject(packet + "{}", "trailing packet data rejected");
Reject(new string('x', NetworkProtocol.MaximumBytes + 1), "oversized network payload rejected");
Reject(packet.Replace("\"Kind\":\"settings\"", "\"Kind\":\"ack\""), "client ack cannot contain gameplay settings");
message.Kind = "ack"; message.Settings = null;
Check(NetworkProtocol.Parse(JsonConvert.SerializeObject(message), "test-build", "test-game").Kind == "ack", "bounded client acknowledgement accepted");

Console.WriteLine($"All {count} checks passed. Unity/native gameplay still requires an in-game test.");
