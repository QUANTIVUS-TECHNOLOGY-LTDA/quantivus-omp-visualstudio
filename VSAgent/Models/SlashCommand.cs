using System.Collections.Generic;

namespace VSAgent.Models
{
    public enum SlashCommandKind
    {
        Remote,           // Send PromptText to oh-my-pi as a new turn
        LocalClear,       // Clear response + history
        LocalCancel,      // Cancel the current request
        SteerImmediate,   // Send the rest of the prompt as a follow-up to the running session
        QueueAdd,         // Add the rest of the prompt to the follow-up queue
        LocalClearQueue,  // Clear the follow-up queue
        SkillActivate,    // Activate a skill by name (rest of prompt is the skill name)
        SkillDeactivate,  // Deactivate a skill
        SkillClear,       // Clear all active skills
    }

    public sealed class SlashCommand
    {
        public string Name { get; }
        public string Description { get; }
        public string PromptText { get; }
        public SlashCommandKind Kind { get; }

        public SlashCommand(string name, string description, string promptText, SlashCommandKind kind = SlashCommandKind.Remote)
        {
            Name = name;
            Description = description;
            PromptText = promptText;
            Kind = kind;
        }

        public static readonly IReadOnlyList<SlashCommand> All = new[]
        {
            // --- Session ---
            new SlashCommand("/steer",      "Send a follow-up message to the running session", null, SlashCommandKind.SteerImmediate),
            new SlashCommand("/queue-clear", "Clear the follow-up queue", null, SlashCommandKind.LocalClearQueue),
            new SlashCommand("/skill",       "Activate a skill (prepends to subsequent prompts)", null, SlashCommandKind.SkillActivate),
            new SlashCommand("/skill-off",   "Deactivate a skill", null, SlashCommandKind.SkillDeactivate),
            new SlashCommand("/skill-clear", "Clear all active skills", null, SlashCommandKind.SkillClear),
            new SlashCommand("/status",     "Show current oh-my-pi status and VS state",
                "Report the current Visual Studio state, the active solution, and the current oh-my-pi session status."),
            new SlashCommand("/model",      "Show the active oh-my-pi model",
                "Report which oh-my-pi model is currently driving the agent."),
            new SlashCommand("/tokens",     "Show token usage for this session",
                "Report the approximate token usage of the current conversation and the configured context window."),
            // --- Solution / build ---
            new SlashCommand("/analyze",    "Analyze the current solution structure",
                "Analyze the entire solution: list projects, dependencies, and entry points. Highlight anything that looks broken or surprising."),
            new SlashCommand("/build",      "Build the current solution",
                "Build the current solution and report errors and warnings, grouped by project. Stop at the first hard error if useful."),
            new SlashCommand("/rebuild",    "Clean and rebuild the current solution",
                "Clean the current solution and rebuild it from scratch. Report the outcome and any failures."),
            new SlashCommand("/test",       "Run all tests in the solution",
                "Discover and run all unit tests in the solution. Report the totals and any failing tests with their messages."),
            new SlashCommand("/run",        "Run the active project (without debugger)",
                "Start the active project as a normal process (no debugger attached). Report when it exits and any console output."),

            // --- Code actions ---
            new SlashCommand("/explain",    "Explain the active document",
                "Explain what the active document does, how it works, and any defects or improvements."),
            new SlashCommand("/refactor",   "Suggest refactorings for the active document",
                "Review the active document and suggest concrete refactorings. Preserve behavior."),
            new SlashCommand("/tests-gen",  "Generate unit tests for the active document",
                "Generate unit tests for the active document. Detect the test framework from the solution."),
            new SlashCommand("/document",   "Document the active document",
                "Add XML doc comments or inline comments where useful, without changing behavior."),

            // --- Source control ---
            new SlashCommand("/git",        "Show git status of the solution",
                "Run git status on the solution directory. Report modified, staged, and untracked files."),
            new SlashCommand("/diff",       "Show unstaged changes in the active document",
                "Show the diff between the working tree and HEAD for the active document. Quote the relevant hunks."),

            // --- Debugging ---
            new SlashCommand("/stack",      "Show the current call stack (when paused in the debugger)",
                "If a debug session is paused, print the current call stack."),
            new SlashCommand("/step-over",  "Step Over in the debugger",
                "If a debug session is active, execute Step Over. Otherwise start debugging and break at the next statement."),
            new SlashCommand("/step-into",  "Step Into in the debugger",
                "If a debug session is active, execute Step Into. Otherwise start debugging and break at the next statement."),
            new SlashCommand("/step-out",   "Step Out in the debugger",
                "If a debug session is active, execute Step Out. Otherwise start debugging and break at the next statement."),
            new SlashCommand("/continue",   "Continue execution in the debugger",
                "If a debug session is paused, continue execution. Otherwise start debugging."),
            new SlashCommand("/pause",      "Pause the running debugger",
                "If a debug session is running, break all into the debugger."),
        };
    }
}
