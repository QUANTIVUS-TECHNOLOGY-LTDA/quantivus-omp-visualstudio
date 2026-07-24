using System.Collections.Generic;

namespace VSAgent.Services
{
    /// <summary>
    /// Static catalogue of every oh-my-pi (omp) provider, the env-var name(s) it
    /// reads, optional base-URL override, and which authentication modes it
    /// accepts. The values come from `omp --help` environment-variable docs.
    /// </summary>
    public sealed class ProviderSpec
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ProviderKind Kind { get; }
        public string ApiKeyEnvVar { get; }
        public string OAuthEnvVar { get; }
        public string BaseUrlEnvVar { get; }
        public string ModelEnvVar { get; }
        public string Description { get; }

        public ProviderSpec(string id, string displayName, ProviderKind kind,
            string apiKeyEnvVar, string oauthEnvVar, string baseUrlEnvVar,
            string modelEnvVar, string description)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            ApiKeyEnvVar = apiKeyEnvVar;
            OAuthEnvVar = oauthEnvVar;
            BaseUrlEnvVar = baseUrlEnvVar;
            ModelEnvVar = modelEnvVar;
            Description = description;
        }

        public override string ToString() => DisplayName;
    }

    public enum ProviderKind
    {
        Anthropic,
        AnthropicFoundry,
        OpenAI,
        GoogleGemini,
        GitHubCopilot,
        AzureOpenAI,
        Groq,
        Cerebras,
        XaiGrok,
        OpenRouter,
        Kilo,
        Mistral,
        Zai,
        UmansCodingPlan,
        Minimax,
        OpenCode,
        Cursor,
        VercelAIGateway,
        WaferServerless,
        AwsBedrock,
        GoogleVertex,
        Ollama,
    }

    public static class ProviderCatalog
    {
        public static readonly IReadOnlyList<ProviderSpec> All = new[]
        {
            new ProviderSpec("anthropic", "Anthropic Claude", ProviderKind.Anthropic,
                "ANTHROPIC_API_KEY", "ANTHROPIC_OAUTH_TOKEN", "ANTHROPIC_BASE_URL",
                "OMP_MODEL",
                "Anthropic Claude models. Supports API key and OAuth."),

            new ProviderSpec("anthropic-foundry", "Anthropic Foundry (mTLS)", ProviderKind.AnthropicFoundry,
                "ANTHROPIC_FOUNDRY_API_KEY", null, "FOUNDRY_BASE_URL",
                "OMP_MODEL",
                "Anthropic Foundry mode. mTLS via CLAUDE_CODE_CLIENT_CERT / CLAUDE_CODE_CLIENT_KEY."),

            new ProviderSpec("openai", "OpenAI", ProviderKind.OpenAI,
                "OPENAI_API_KEY", null, "OPENAI_BASE_URL",
                "OMP_MODEL",
                "OpenAI GPT models (gpt-5.2, gpt-5.4, ...)."),

            new ProviderSpec("google", "Google Gemini", ProviderKind.GoogleGemini,
                "GEMINI_API_KEY", null, null,
                "OMP_MODEL",
                "Google Gemini API."),

            new ProviderSpec("github-copilot", "GitHub Copilot", ProviderKind.GitHubCopilot,
                "COPILOT_GITHUB_TOKEN", null, null,
                "OMP_MODEL",
                "GitHub Copilot chat models."),

            new ProviderSpec("azure-openai", "Azure OpenAI", ProviderKind.AzureOpenAI,
                "AZURE_OPENAI_API_KEY", null, "AZURE_OPENAI_ENDPOINT",
                "OMP_MODEL",
                "Azure-hosted OpenAI models."),

            new ProviderSpec("groq", "Groq", ProviderKind.Groq,
                "GROQ_API_KEY", null, null, "OMP_MODEL", "Groq inference."),

            new ProviderSpec("cerebras", "Cerebras", ProviderKind.Cerebras,
                "CEREBRAS_API_KEY", null, null, "OMP_MODEL", "Cerebras inference."),

            new ProviderSpec("xai", "xAI Grok", ProviderKind.XaiGrok,
                "XAI_API_KEY", null, null, "OMP_MODEL", "xAI Grok models."),

            new ProviderSpec("openrouter", "OpenRouter", ProviderKind.OpenRouter,
                "OPENROUTER_API_KEY", null, "OPENROUTER_BASE_URL",
                "OMP_MODEL",
                "OpenRouter aggregated gateway (openrouter/<model>)."),

            new ProviderSpec("kilo", "Kilo Gateway", ProviderKind.Kilo,
                "KILO_API_KEY", null, null, "OMP_MODEL", "Kilo Gateway models."),

            new ProviderSpec("mistral", "Mistral", ProviderKind.Mistral,
                "MISTRAL_API_KEY", null, null, "OMP_MODEL", "Mistral inference."),

            new ProviderSpec("zai", "z.ai (Zhipu / GLM)", ProviderKind.Zai,
                "ZAI_API_KEY", null, null, "OMP_MODEL", "z.ai (ZhipuAI/GLM) models."),

            new ProviderSpec("umans-coding-plan", "Umans AI Coding Plan", ProviderKind.UmansCodingPlan,
                "UMANS_AI_CODING_PLAN_API_KEY", null, null, "OMP_MODEL",
                "Umans Coding Plan models."),

            new ProviderSpec("minimax", "MiniMax", ProviderKind.Minimax,
                "MINIMAX_API_KEY", null, null, "OMP_MODEL",
                "MiniMax models (MiniMax-M2/M2.5/M2.7/M3)."),

            new ProviderSpec("opencode", "OpenCode Zen / Go", ProviderKind.OpenCode,
                "OPENCODE_API_KEY", null, null, "OMP_MODEL", "OpenCode Zen or Go models."),

            new ProviderSpec("cursor", "Cursor AI", ProviderKind.Cursor,
                "CURSOR_ACCESS_TOKEN", null, null, "OMP_MODEL", "Cursor AI models."),

            new ProviderSpec("vercel-ai-gateway", "Vercel AI Gateway", ProviderKind.VercelAIGateway,
                "AI_GATEWAY_API_KEY", null, null, "OMP_MODEL", "Vercel AI Gateway."),

            new ProviderSpec("wafer-serverless", "Wafer Serverless", ProviderKind.WaferServerless,
                "WAFER_SERVERLESS_API_KEY", null, null, "OMP_MODEL", "Wafer Serverless (PAYG)."),

            new ProviderSpec("aws-bedrock", "AWS Bedrock", ProviderKind.AwsBedrock,
                "AWS_PROFILE", null, null, "OMP_MODEL",
                "AWS Bedrock (or AWS_ACCESS_KEY_ID + AWS_SECRET_ACCESS_KEY)."),

            new ProviderSpec("google-vertex", "Google Vertex AI", ProviderKind.GoogleVertex,
                "GOOGLE_CLOUD_PROJECT", null, "GOOGLE_CLOUD_LOCATION",
                "OMP_MODEL",
                "Google Vertex AI (requires GOOGLE_APPLICATION_CREDENTIALS)."),

            new ProviderSpec("ollama", "Ollama (local)", ProviderKind.Ollama,
                null, null, "OLLAMA_HOST", "OMP_MODEL",
                "Local Ollama models. No API key needed."),
        };

        public static ProviderSpec FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in All)
                if (string.Equals(p.Id, id, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        public static IReadOnlyList<ProviderSpec> Core => new[]
        {
            FindById("anthropic"),
            FindById("openai"),
            FindById("google"),
            FindById("github-copilot"),
        };
    }
}
