// Visual Studio automation exposes types named Process, Window and TextRange.
// The workbench uses the WPF/System.Diagnostics types for these simple names.
global using Process = System.Diagnostics.Process;
global using Window = System.Windows.Window;
global using TextRange = System.Windows.Documents.TextRange;
