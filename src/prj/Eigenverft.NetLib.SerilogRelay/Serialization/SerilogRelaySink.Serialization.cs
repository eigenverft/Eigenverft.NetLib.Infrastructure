using System.Collections.Generic;
using System.Text.Json;

using Serilog.Events;

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private static string SerializeProperties(LogEvent logEvent)
        {
            var properties = new Dictionary<string, string>(logEvent.Properties.Count);
            foreach (var kvp in logEvent.Properties)
            {
                properties.Add(kvp.Key, kvp.Value.ToString()!);
            }

            return JsonSerializer.Serialize(
                properties,
                typeof(Dictionary<string, string>),
                LogBatchJsonContext.Default);
        }
    }
}
