namespace NutriForge.Infrastructure.Ai;

/// <summary>
/// Configuration for the NutritionAssistant's chat provider (section <c>Ai</c>). Provider-swappable;
/// secrets come from user-secrets / env / Key Vault, never source. When unconfigured the assistant
/// endpoint reports 503 rather than failing requests.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary><c>None</c> (default) | <c>OpenAI</c> | <c>AzureOpenAI</c> | <c>Ollama</c>.</summary>
    public string Provider { get; set; } = "None";

    /// <summary>Model / deployment name, e.g. <c>gpt-4o-mini</c>.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>API key (OpenAI / Azure key auth). Omit for Azure Managed Identity or Ollama.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Endpoint for Azure OpenAI or a local Ollama (e.g. <c>http://localhost:11434</c>).</summary>
    public string? Endpoint { get; set; }
}
