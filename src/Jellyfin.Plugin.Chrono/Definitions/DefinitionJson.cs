using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Chrono.Definitions;

public static class DefinitionJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static UniverseDefinition ParseUniverse(string json)
    {
        var universe = JsonSerializer.Deserialize<UniverseDefinition>(json, Options)
            ?? throw new InvalidDataException("Universe definition is empty.");
        return universe;
    }

    public static RegistryIndex ParseIndex(string json)
    {
        return JsonSerializer.Deserialize<RegistryIndex>(json, Options)
            ?? throw new InvalidDataException("Registry index is empty.");
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
