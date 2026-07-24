using System;

namespace VSAgent.Services
{
    public sealed class ContextUsageTracker
    {
        public const long MaxTokens = 1_000_000;
        public const int CharsPerToken = 4;

        public long InputChars { get; private set; }
        public long OutputChars { get; private set; }

        public long InputTokens => InputChars / CharsPerToken;
        public long OutputTokens => OutputChars / CharsPerToken;
        public long TotalTokens => InputTokens + OutputTokens;

        public double UsagePercent => Math.Min(100.0, TotalTokens * 100.0 / MaxTokens);

        public string FormatUsage() => $"ctx: {UsagePercent:F1}%/{MaxTokens / 1_000_000}M";

        public void AddInput(string text)
        {
            if (!string.IsNullOrEmpty(text)) InputChars += text.Length;
        }

        public void AddOutput(string text)
        {
            if (!string.IsNullOrEmpty(text)) OutputChars += text.Length;
        }

        public void Reset()
        {
            InputChars = 0;
            OutputChars = 0;
        }
    }
}
