using System.Text.Json.Serialization;

namespace Fling.Config;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(FlingConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;
