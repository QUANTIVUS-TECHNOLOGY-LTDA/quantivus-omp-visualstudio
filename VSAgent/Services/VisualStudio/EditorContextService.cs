using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;

namespace VSAgent.Services.VisualStudio
{
    internal sealed class EditorContextService
    {
        private readonly DTE2 dte;

        public EditorContextService(DTE2 dte)
        {
            this.dte = dte ?? throw new ArgumentNullException(nameof(dte));
        }

        public string GetSelectedText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return (dte.ActiveDocument?.Selection as TextSelection)?.Text ?? string.Empty;
        }

        public string GetCurrentLine()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var selection = dte.ActiveDocument?.Selection as TextSelection;
            if (selection == null) return string.Empty;

            var start = selection.ActivePoint.CreateEditPoint();
            var end = selection.ActivePoint.CreateEditPoint();
            start.StartOfLine();
            end.EndOfLine();
            return start.GetText(end);
        }

        public string GetCurrentMethod()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetCurrentElementText(
                vsCMElement.vsCMElementFunction,
                vsCMElement.vsCMElementProperty,
                vsCMElement.vsCMElementEvent);
        }

        public string GetCurrentType()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetCurrentElementText(
                vsCMElement.vsCMElementClass,
                vsCMElement.vsCMElementStruct,
                vsCMElement.vsCMElementInterface,
                vsCMElement.vsCMElementEnum,
                vsCMElement.vsCMElementDelegate);
        }

        public string GetActiveDocumentContext(int maximumCharacters = 30000)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = dte.ActiveDocument;
            if (document == null) return string.Empty;

            var selected = GetSelectedText();
            if (!string.IsNullOrWhiteSpace(selected))
                return BuildContext(document.FullName, "selection", selected, maximumCharacters);

            var method = GetCurrentMethod();
            if (!string.IsNullOrWhiteSpace(method))
                return BuildContext(document.FullName, "current member", method, maximumCharacters);

            var type = GetCurrentType();
            if (!string.IsNullOrWhiteSpace(type))
                return BuildContext(document.FullName, "current type", type, maximumCharacters);

            var textDocument = document.Object("TextDocument") as TextDocument;
            if (textDocument == null) return document.FullName ?? string.Empty;

            var start = textDocument.StartPoint.CreateEditPoint();
            var text = start.GetText(textDocument.EndPoint);
            return BuildContext(document.FullName, "document", text, maximumCharacters);
        }

        private string GetCurrentElementText(params vsCMElement[] kinds)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var document = dte.ActiveDocument;
            var selection = document?.Selection as TextSelection;
            var codeModel = document?.ProjectItem?.FileCodeModel;
            if (selection == null || codeModel == null) return string.Empty;

            foreach (var kind in kinds)
            {
                try
                {
                    var element = codeModel.CodeElementFromPoint(selection.ActivePoint, kind);
                    if (element == null) continue;
                    var start = element.StartPoint.CreateEditPoint();
                    return start.GetText(element.EndPoint);
                }
                catch
                {
                    // Some language services do not expose every CodeModel element kind.
                }
            }

            return string.Empty;
        }

        private static string BuildContext(string file, string scope, string content, int maximumCharacters)
        {
            var safeContent = content ?? string.Empty;
            if (maximumCharacters > 0 && safeContent.Length > maximumCharacters)
            {
                safeContent = safeContent.Substring(0, maximumCharacters) +
                              Environment.NewLine + "[context truncated]";
            }

            return "File: " + (file ?? string.Empty) + Environment.NewLine +
                   "Scope: " + scope + Environment.NewLine + Environment.NewLine +
                   safeContent;
        }
    }
}
