using System.Text.Json;

namespace Fineage.Agent.Observation;

/// <summary>
/// Marks untrusted world text (NPC names, quest titles, item names, dialog) as data, not
/// instructions. Foundation for prompt-injection defense (AI-023): consumers must treat
/// <c>role=data</c> and <c>untrusted=true</c> as untrusted content.
/// </summary>
public static class UntrustedText
{
    public static JsonElement Mark(string? text)
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                text = text ?? "",
                untrusted = true,
                role = "data"
            }));
        return document.RootElement.Clone();
    }
}
