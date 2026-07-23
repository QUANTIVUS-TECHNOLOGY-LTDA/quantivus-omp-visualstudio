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
        }
    }
}
