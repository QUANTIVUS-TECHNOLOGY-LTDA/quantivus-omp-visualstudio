using System.Collections.Generic;

namespace VSAgent.Services
{
    /// <summary>
    /// Aggregates everything needed to populate the env-vars that oh-my-pi reads.
    /// Built once per restart of the omp process from current settings + credentials.
    /// </summary>
    public sealed class OmpEnvironment
    {
        public string ActiveProvider { get; set; }
        public string ActiveModel { get; set; }
        public string SmolModel { get; set; }
        public string SlowModel { get; set; }
        public string PlanModel { get; set; }
        public string OmpProfile { get; set; }
        public double AutoCompactThresholdPercent { get; set; }
        public string SearchProvider { get; set; }

        public Dictionary<string, string> EnvOverrides { get; } = new();
    }

    /// <summary>
    /// Converts an <see cref="OmpEnvironment"/> into the key/value list that
    /// OmpAcpClient injects into the omp ProcessStartInfo.EnvironmentVariables.
    /// </summary>
    public static class OmpEnvironmentBuilder
    {
        public static Dictionary<string, string> Build(OmpEnvironment env, CredentialStore creds, WebSearchConfig search)
        {
            var dict = new Dictionary<string, string>();
            if (env.EnvOverrides != null)
                foreach (var kv in env.EnvOverrides) dict[kv.Key] = kv.Value ?? string.Empty;

            // Active provider model
            if (!string.IsNullOrEmpty(env.ActiveModel)) dict["OMP_MODEL"] = env.ActiveModel;
            if (!string.IsNullOrEmpty(env.SmolModel)) dict["PI_SMOL_MODEL"] = env.SmolModel;
            if (!string.IsNullOrEmpty(env.SlowModel)) dict["PI_SLOW_MODEL"] = env.SlowModel;
            if (!string.IsNullOrEmpty(env.PlanModel)) dict["PI_PLAN_MODEL"] = env.PlanModel;
            if (!string.IsNullOrEmpty(env.OmpProfile)) dict["OMP_PROFILE"] = env.OmpProfile;

            // Provider credentials
            if (creds != null && !string.IsNullOrEmpty(env.ActiveProvider))
            {
                var spec = ProviderCatalog.FindById(env.ActiveProvider);
                if (spec != null)
                {
                    var oauth = creds.GetOAuthToken(env.ActiveProvider);
                    if (!string.IsNullOrEmpty(oauth) && !string.IsNullOrEmpty(spec.OAuthEnvVar))
                        dict[spec.OAuthEnvVar] = oauth;
                    var apiKey = creds.GetApiKey(env.ActiveProvider);
                    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(spec.ApiKeyEnvVar))
                        dict[spec.ApiKeyEnvVar] = apiKey;
                    var baseUrl = creds.GetExtra(env.ActiveProvider, "base_url");
                    if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(spec.BaseUrlEnvVar))
                        dict[spec.BaseUrlEnvVar] = baseUrl;
                }
            }

            // Web search providers
            if (search != null)
            {
                void Put(string envName, string val) { if (!string.IsNullOrEmpty(val)) dict[envName] = val; }
                Put("EXA_API_KEY", search.ExaApiKey);
                Put("BRAVE_API_KEY", search.BraveApiKey);
                Put("PERPLEXITY_API_KEY", search.PerplexityApiKey);
                Put("PERPLEXITY_COOKIES", search.PerplexityCookies);
                Put("TAVILY_API_KEY", search.TavilyApiKey);
                Put("TINYFISH_API_KEY", search.TinyFishApiKey);
                Put("FIRECRAWL_API_KEY", search.FirecrawlApiKey);
                Put("ANTHROPIC_SEARCH_API_KEY", search.AnthropicSearchApiKey);
                Put("ANTHROPIC_SEARCH_BASE_URL", search.AnthropicSearchBaseUrl);
            }

            return dict;
        }
    }

    /// <summary>
    /// Web-search provider configuration (EXA, Brave, Perplexity, etc.).
    /// </summary>
    public sealed class WebSearchConfig
    {
        public string Provider { get; set; } = "native";
        public string ExaApiKey { get; set; }
        public string BraveApiKey { get; set; }
        public string PerplexityApiKey { get; set; }
        public string PerplexityCookies { get; set; }
        public string TavilyApiKey { get; set; }
        public string TinyFishApiKey { get; set; }
        public string FirecrawlApiKey { get; set; }
        public string AnthropicSearchApiKey { get; set; }
        public string AnthropicSearchBaseUrl { get; set; }
    }
}
