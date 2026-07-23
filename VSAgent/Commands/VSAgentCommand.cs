using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using VSAgent.ToolWindows;

namespace VSAgent.Commands
{
    internal sealed class VSAgentCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("85d7e8c0-5c8f-4d02-8c7e-0b3d3b6e1a2f");
        private readonly AsyncPackage package;

        private VSAgentCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            if (commandService == null) throw new ArgumentNullException(nameof(commandService));
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
        }

        public static VSAgentCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new VSAgentCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = package.FindToolWindow(typeof(VSAgentToolWindow), 0, true);
            if (window?.Frame == null) throw new NotSupportedException("Cannot create tool window");
            ErrorHandler.ThrowOnFailure(((IVsWindowFrame)window.Frame).Show());
        }
    }
}
