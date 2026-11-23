namespace RoushTech.Rangifer.Mail;

using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class TemplateModel
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ToJson(object values) => JsonSerializer.Serialize(values, SerializerOptions);

    // Through JSON so templates see camelCase names without opening up Fluid's member access to arbitrary types.
    public static ExpandoObject From(object values) =>
        FromJson(JsonSerializer.SerializeToNode(values, SerializerOptions)?.AsObject()
            ?? throw new ArgumentException("Template values must serialize to a JSON object.", nameof(values)));

    public static ExpandoObject FromJson(string json) =>
        FromJson(JsonNode.Parse(json)?.AsObject() ?? throw new ArgumentException("Template variables must be a JSON object.", nameof(json)));

    public static ExpandoObject FromJson(JsonObject json)
    {
        var expando = new ExpandoObject();
        var values = (IDictionary<string, object?>)expando;
        foreach (var (key, value) in json)
        {
            values[key] = Convert(value);
        }

        return expando;
    }

    private static object? Convert(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.Object when HtmlFragment.TryRead(node.AsObject(), out var fragment) => fragment,
            JsonValueKind.Object => FromJson(node.AsObject()),
            JsonValueKind.Array => node.AsArray().Select(Convert).ToList(),
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.Number => node.GetValue<double>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
