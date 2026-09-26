using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Eigenverft.NetLib.SerilogRelay
{
    [JsonSerializable(typeof(LogBatchPayload))]
    [JsonSerializable(typeof(List<LogEntry>))]
    [JsonSerializable(typeof(LogEntry))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    internal partial class LogBatchJsonContext : JsonSerializerContext
    {
    }
}
