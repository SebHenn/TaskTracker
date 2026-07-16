using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskTracker.Core.Storage
{
    public static class CoreJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };
    }
}
