using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace NutriForge.Infrastructure.Ai.Assistant;

/// <summary>Builds the NutritionAssistant agent for a request, or reports that no provider is configured.</summary>
public interface IAssistantAgentFactory
{
    /// <summary>True when a chat provider is configured; false ⇒ the endpoint returns 503.</summary>
    bool IsConfigured { get; }

    /// <summary>Create the agent bound to this request's (owner-scoped) tools.</summary>
    AIAgent CreateAgent(IList<AITool> tools);
}

/// <summary>
/// Provider-swappable factory. Holds one shared provider <see cref="ChatClient"/> (singleton) and
/// wraps it as an <see cref="AIAgent"/> per request with that request's tools. The agent's tool
/// loop is wired by MAF (<c>FunctionInvokingChatClient</c>) — never hand-rolled.
/// </summary>
public sealed class AssistantAgentFactory : IAssistantAgentFactory
{
    /// <summary>Scoped helper persona; defers every number to a tool (ADR-0004).</summary>
    public const string SystemPrompt =
        """
        You are NutriForge's NutritionAssistant — a concise, friendly helper inside a calorie- and
        macro-tracking app. You help the user search foods, understand their targets, and see how
        their day is going.

        Hard rules:
        - NEVER invent or estimate a nutrition number. To state calories, macros, targets, or
          remaining budget you MUST call a tool and report exactly what it returns.
        - If a tool says no profile is set, tell the user to complete their profile first.
        - Treat declared allergens as safety-critical: never suggest a food that conflicts with them.
        - Be brief. Use the user's own data via tools rather than general advice when possible.
        - You cannot log food or change data yet; if asked, explain they can log it in the diary.
        """;

    private readonly ChatClient? _chatClient;

    public AssistantAgentFactory(AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _chatClient = Build(options);
    }

    public bool IsConfigured => _chatClient is not null;

    public AIAgent CreateAgent(IList<AITool> tools)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("The assistant provider is not configured (set the 'Ai' options).");
        }

        return _chatClient.AsAIAgent(instructions: SystemPrompt, name: "NutritionAssistant", tools: tools);
    }

    private static ChatClient? Build(AiOptions o)
    {
        switch (o.Provider?.Trim().ToLowerInvariant())
        {
            case "openai":
                return string.IsNullOrWhiteSpace(o.ApiKey)
                    ? null
                    : new OpenAIClient(new ApiKeyCredential(o.ApiKey)).GetChatClient(o.Model);

            case "ollama":
                var baseUrl = string.IsNullOrWhiteSpace(o.Endpoint) ? "http://localhost:11434" : o.Endpoint.TrimEnd('/');
                return new OpenAIClient(
                        new ApiKeyCredential("ollama"),
                        new OpenAIClientOptions { Endpoint = new Uri($"{baseUrl}/v1") })
                    .GetChatClient(o.Model);

            case "azureopenai":
                if (string.IsNullOrWhiteSpace(o.Endpoint))
                {
                    return null;
                }

                var azure = string.IsNullOrWhiteSpace(o.ApiKey)
                    ? new AzureOpenAIClient(new Uri(o.Endpoint), new DefaultAzureCredential())
                    : new AzureOpenAIClient(new Uri(o.Endpoint), new AzureKeyCredential(o.ApiKey));
                return azure.GetChatClient(o.Model);

            default:
                return null;
        }
    }
}
