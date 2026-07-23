using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VSAgent.Commands;
using VSAgent.Services;
using VSAgent.ToolWindows;

namespace VSAgent
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(VSAgentPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(VSAgentToolWindow), Style = VsDockStyle.Tabbed, Window = "DocumentWell", Orientation = ToolWindowOrientation.none)]
    [ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "VS Agent", "General", 0, 0, true, SupportsProfiles = true)]
    public sealed class VSAgentPackage : AsyncPackage
    {
        public const string PackageGuidString = "41f0c0d8-5c2b-4f02-8c7e-0b3d3b6e1a2f";

        internal static AgentHostService AgentHost { get; private set; }

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            AgentHost = new AgentHostService();
            try
            {
                await AgentHost.InitializeAsync(this, cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Quantivus OMP initialization failed: " + ex);
            }

            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await VSAgentCommand.InitializeAsync(this);
            await ExplainCodeCommand.InitializeAsync(this);
            await RefactorCodeCommand.InitializeAsync(this);
            await GenerateTestsCommand.InitializeAsync(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AgentHost?.Dispose();
                AgentHost = null;
            }
            base.Dispose(disposing);
        }
    }
}
