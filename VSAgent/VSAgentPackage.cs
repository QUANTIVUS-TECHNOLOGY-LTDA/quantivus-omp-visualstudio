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
        internal static Microsoft.VisualStudio.OLE.Interop.IServiceProvider PackageServiceProvider { get; private set; }
        public static System.IServiceProvider GetServiceProvider()
        {
            if (PackageServiceProvider == null) return null;
            return new Microsoft.VisualStudio.Shell.ServiceProvider(PackageServiceProvider);
        }

        internal static AsyncPackage Instance { get; private set; }

        public static T GetOptions<T>() where T : Microsoft.VisualStudio.Shell.DialogPage
        {
            if (Instance != null)
                return (T)Instance.GetDialogPage(typeof(T));
            return null;
        }

        internal static SkillStore Skills { get; } = new SkillStore();
        internal static ActiveSkillRegistry ActiveSkills { get; } = new ActiveSkillRegistry(Skills);
        internal static CredentialStore Credentials { get; } = new CredentialStore();
        internal static CustomToolStore CustomTools { get; } = new CustomToolStore();
        internal static WebSearchStore WebSearch { get; } = new WebSearchStore();
        internal static WebSearchConfig WebSearchConfig { get; private set; }
        internal static AgentHostService AgentHost { get; private set; }
        public static OmpEnvironment Env { get; internal set; } = new OmpEnvironment();
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            PackageServiceProvider = this;
            Skills.Load();
            ActiveSkills.PruneMissing();
            Credentials.Load();
            CustomTools.Load();
            WebSearchConfig = WebSearch.Load();
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
