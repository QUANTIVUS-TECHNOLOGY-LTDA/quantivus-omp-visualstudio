using Microsoft.VisualStudio.Shell;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VSAgent
{
    internal class OptionsProvider
    {
        [ComVisible(true)]
        public sealed class GeneralOptions : DialogPage
        {
            [Category("oh-my-pi")]
            [DisplayName("Executable path")]
            [Description("Optional absolute path to omp.exe. Leave empty to use Runtime or PATH discovery.")]
            public string OmpExecutablePath { get; set; } = string.Empty;

            [Category("Permissions")]
            [DisplayName("Auto-approve read-only requests")]
            [Description("Allows the ACP client to select an allow option only for requests that appear read-only.")]
            public bool AutoApproveReadOnly { get; set; } = true;

            [Category("Context")]
            [DisplayName("Auto-compact threshold (%)")]
            [Description("When the context usage reaches this percentage, /compact is enqueued automatically. Set to 0 to disable.")]
            public double ContextCompactThresholdPercent { get; set; } = 80.0;

            [Category("Model")]
            [DisplayName("Model provider")]
            [Description("Provider passed to oh-my-pi via the OMP_PROVIDER environment variable. Common: Anthropic, OpenAI, Google, Custom.")]
            public string ModelProvider { get; set; } = "Anthropic";

            [Category("Model")]
            [DisplayName("Model name")]
            [Description("Model name passed to oh-my-pi via the OMP_MODEL environment variable. Leave empty for the provider's default.")]
            public string ModelName { get; set; } = string.Empty;
        }
    }
}
