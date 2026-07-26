using System.Collections.Generic;

namespace VSAgent.Models
{
    public enum SlashCommandKind
    {
        Remote,
        LocalClear,
        LocalCancel,
        SteerImmediate,
        QueueAdd,
        LocalClearQueue,
        SkillActivate,
        SkillDeactivate,
        SkillClear,
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
            new SlashCommand("/steer",       "Send a follow-up message to the running session", null, SlashCommandKind.SteerImmediate),
            new SlashCommand("/queue",       "Queue a follow-up for the current run", null, SlashCommandKind.QueueAdd),
            new SlashCommand("/queue-clear", "Clear the follow-up queue", null, SlashCommandKind.LocalClearQueue),
            new SlashCommand("/cancel",      "Cancel the current request", null, SlashCommandKind.LocalCancel),
            new SlashCommand("/clear",       "Clear the current conversation", null, SlashCommandKind.LocalClear),
            new SlashCommand("/skill",       "Activate a skill by name", null, SlashCommandKind.SkillActivate),
            new SlashCommand("/skill-off",   "Deactivate a skill", null, SlashCommandKind.SkillDeactivate),
            new SlashCommand("/skill-clear", "Clear all active skills", null, SlashCommandKind.SkillClear),

            new SlashCommand("/status", "Show OMP and Visual Studio state",
                "Report the current Visual Studio solution, active project, editor, Git branch, OMP process, provider, model, active skills and queued work."),
            new SlashCommand("/model", "Show the active provider and model",
                "Report the active oh-my-pi provider, model and configured context window."),
            new SlashCommand("/tokens", "Show approximate context usage",
                "Report approximate input/output token usage, the active context sources and the configured context limit."),
            new SlashCommand("/context", "Explain the active context package",
                "List every editor, repository, Git and build source currently included in the prompt. Identify irrelevant, duplicated or sensitive context and recommend a smaller selection."),

            new SlashCommand("/analyze", "Analyze the current solution",
                "Analyze the complete solution structure, projects, dependencies, entry points, architectural boundaries and build configuration. Highlight correctness, security and maintainability risks."),
            new SlashCommand("/build", "Build the current solution",
                "Build the current solution. Report errors and warnings grouped by project and fix only issues caused by the current task."),
            new SlashCommand("/rebuild", "Clean and rebuild the solution",
                "Clean and rebuild the current solution from scratch. Report the exact result and investigate any failure."),
            new SlashCommand("/test", "Run relevant tests",
                "Discover and run the tests relevant to the current changes, then run the broader test suite when practical. Report totals and failing tests with root causes."),
            new SlashCommand("/run", "Run the active project without debugging",
                "Run the active project without attaching the debugger. Capture its exit code and useful console output."),

            new SlashCommand("/fix", "Fix the active problem",
                "Inspect the active editor, build output, tests and related code. Find the root cause, implement the smallest safe fix, add a regression test and verify the build."),
            new SlashCommand("/explain", "Explain the active code",
                "Explain what the active selection or document does, its inputs, outputs, side effects, dependencies and non-obvious failure modes."),
            new SlashCommand("/refactor", "Refactor while preserving behavior",
                "Refactor the active code for clarity, cohesion and testability while preserving public behavior. Build and run relevant tests after the change."),
            new SlashCommand("/review", "Perform a strict code review",
                "Review the current changes for correctness, regressions, security, concurrency, resource lifetime, performance, API compatibility and missing tests. Rank findings by severity and provide concrete fixes."),
            new SlashCommand("/optimize", "Optimize measured bottlenecks",
                "Analyze the active component for UI-thread blocking, repeated work, allocations, unbounded collections, I/O and process lifetime issues. Make focused optimizations and explain the evidence."),
            new SlashCommand("/tests-gen", "Generate meaningful tests",
                "Generate meaningful unit and integration tests for the active code. Follow existing test conventions and cover normal, edge, failure and cancellation behavior."),
            new SlashCommand("/document", "Update code and project documentation",
                "Add accurate XML documentation and update relevant project documentation. Do not document features that are not implemented."),

            new SlashCommand("/git", "Show repository status",
                "Report the current Git branch, upstream divergence, staged, unstaged and untracked files. Do not change repository state."),
            new SlashCommand("/diff", "Review current changes",
                "Review the staged and unstaged diff. Explain each logical change, identify risks and call out generated or sensitive files."),
            new SlashCommand("/commit", "Prepare a commit",
                "Review staged changes and propose a concise conventional commit message and body. Do not create the commit until I explicitly confirm."),

            new SlashCommand("/debug", "Diagnose the current failure",
                "Use the available exception, call stack, locals, output, build errors and tests to find the root cause. Prepare a minimal fix and a regression test."),
            new SlashCommand("/stack", "Show the current call stack",
                "If debugging is paused, print the current call stack and explain the relevant frames."),
            new SlashCommand("/step-over", "Step Over in the debugger",
                "If debugging is paused, execute Step Over and report the new location."),
            new SlashCommand("/step-into", "Step Into in the debugger",
                "If debugging is paused, execute Step Into and report the new location."),
            new SlashCommand("/step-out", "Step Out in the debugger",
                "If debugging is paused, execute Step Out and report the new location."),
            new SlashCommand("/continue", "Continue the debugger",
                "If debugging is paused, continue execution. Otherwise start debugging only when appropriate."),
            new SlashCommand("/pause", "Pause the running debugger",
                "If a debug session is running, break all and report the current execution location."),
        };
    }
}
