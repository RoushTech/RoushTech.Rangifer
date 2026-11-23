namespace RoushTech.Rangifer.Mail;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>Markup built in code for a template value, written as-is where every other value is HTML-encoded.</summary>
public sealed record HtmlFragment([property: JsonPropertyName(HtmlFragment.JsonKey)] string Html)
{
    public const string JsonKey = "__html";

    public static bool TryRead(JsonObject obj, [NotNullWhen(true)] out HtmlFragment? fragment)
    {
        if (obj.Count == 1 && obj[JsonKey] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            fragment = new HtmlFragment(value.GetValue<string>());
            return true;
        }

        fragment = null;
        return false;
    }
}
