using System.Text.Json.Serialization;
using Fling.Content;

namespace Fling.Net;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PairRequest))]
[JsonSerializable(typeof(PairResponse))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(ClipResponse))]
[JsonSerializable(typeof(ClipPayload))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
