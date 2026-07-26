using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Usage",
    "VSTHRD109:Switch instead of assert in async methods",
    Justification = "Prompt expansion is invoked by a WPF command on the Visual Studio UI thread and intentionally captures that synchronization context across awaited Git I/O.",
    Scope = "member",
    Target = "~M:VSAgent.Views.VSAgentControl.ExpandPromptVariablesAsync(System.String)~System.Threading.Tasks.Task{System.String}")]
