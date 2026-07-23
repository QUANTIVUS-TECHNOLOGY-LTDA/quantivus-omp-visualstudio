using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using VSAgent.Services;
using VSAgent.Services.VisualStudio;
using VSAgent.ToolWindows;

namespace VSAgent.Commands
{
    internal sealed class RefactorCodeCommand
    {
        public const int CommandId = 0x0102;
        public static readonly Guid CommandSet = new Guid("85d7e8c0-5c8f-4d02-8c7e-0b3d3b6e1a2f");
        private readonly AsyncPackage package;
        private readonly ChatGPTService agentService;
        private readonly EditorContextService editorContext;

        private RefactorCodeCommand(AsyncPackage package, OleMenuCommandService commandService, DTE2 dte)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            if (commandService == null) throw new ArgumentNullException(nameof(commandService));
            agentService = new ChatGPTService();
            editorContext = new EditorContextService(dte);
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
        }

        public static RefactorCodeCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte == null) throw new InvalidOperationException("Visual Studio DTE service is unavailable.");
            Instance = new RefactorCodeCommand(package, commandService, dte);
        }

        private async void Execute(object sender, EventArgs e)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var code = editorContext.GetSelectedText();
                if (string.IsNullOrWhiteSpace(code)) code = editorContext.GetCurrentMethod();
                if (string.IsNullOrWhiteSpace(code)) code = editorContext.GetCurrentType();
                if (string.IsNullOrWhiteSpace(code)) { ShowError("Select code or place the caret inside a supported member or type first."); return; }
                var result = await agentService.RefactorCodeAsync(code);
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowResult("Code Refactoring Suggestions", result);
            }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowError("Error refactoring code: " + ex.Message);
            }
        }

        private void ShowResult(string title, string content)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = package.FindToolWindow(typeof(VSAgentToolWindow), 0, true);
            if (window?.Content is Views.VSAgentControl control) control.ShowResult(title, content);
        }

        private void ShowError(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(package, message, "Quantivus OMP",
                Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
