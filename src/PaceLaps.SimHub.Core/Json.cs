using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PaceLaps.Plugin.Core
{
    /// <summary>
    /// JSON reading for the contracts. Dates stay strings (Newtonsoft would turn them into DateTime and lose the
    /// milliseconds' format); unknown fields are ignored, as the contracts require.
    /// </summary>
    internal static class Json
    {
        public static JToken Parse(string text)
        {
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            return JToken.ReadFrom(reader);
        }

        public static JObject? Object(string text) => Parse(text) as JObject;

        public static string? Str(this JObject o, string name) =>
            o.TryGetValue(name, out var t) && t.Type == JTokenType.String ? (string?)t : null;

        public static bool Bool(this JObject o, string name, bool fallback = false) =>
            o.TryGetValue(name, out var t) && t.Type == JTokenType.Boolean ? (bool)t : fallback;

        public static int? Int(this JObject o, string name) =>
            o.TryGetValue(name, out var t) && t.Type == JTokenType.Integer ? (int?)(long)t : null;

        public static double? Num(this JObject o, string name) =>
            o.TryGetValue(name, out var t) && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double?)t : null;

        public static JObject? Obj(this JObject o, string name) =>
            o.TryGetValue(name, out var t) ? t as JObject : null;

        public static IEnumerable<JObject> Objects(this JObject o, string name) =>
            o.TryGetValue(name, out var t) && t is JArray a ? a.OfType<JObject>() : Enumerable.Empty<JObject>();

        public static IReadOnlyList<string> Strings(this JObject o, string name) =>
            o.TryGetValue(name, out var t) && t is JArray a
                ? a.Where(x => x.Type == JTokenType.String).Select(x => (string)x!).ToList()
                : new List<string>();

        public static IReadOnlyList<int> Ints(this JObject o, string name) =>
            o.TryGetValue(name, out var t) && t is JArray a
                ? a.Where(x => x.Type == JTokenType.Integer).Select(x => (int)(long)x).ToList()
                : new List<int>();
    }
}
