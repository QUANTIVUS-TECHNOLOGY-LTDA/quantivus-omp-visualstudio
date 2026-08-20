using System;
using System.Collections.Generic;

namespace VSAgent.Models
{
    /// <summary>
    /// Result returned by <c>AssemblyAnalysisService.AnalyzeAssembly</c>. The
    /// same shape is exposed to MCP via the <c>vs_analyze_assembly</c> tool
    /// and consumed by the DLLSpy tab in the workbench.
    /// </summary>
    public sealed class AssemblyAnalysis
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string AssemblyKind { get; set; } = string.Empty;
        public string TargetFramework { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public bool IsManaged { get; set; }
        public bool IsValidPe { get; set; } = true;
        public string? Error { get; set; }
        public AssemblyIdentity Identity { get; set; } = new AssemblyIdentity();
        public List<TypeInfo> Types { get; set; } = new();
        public List<MemberInfo> Exports { get; set; } = new();
        public List<DependencyEdge> References { get; set; } = new();
        public DependencyGraph Graph { get; set; } = new DependencyGraph();
        public DateTime AnalyzedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Identity information of an assembly: name, version, culture and the
    /// public key token. Filled in for managed assemblies; the fields stay
    /// empty for native modules where they do not apply.
    /// </summary>
    public sealed class AssemblyIdentity
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Culture { get; set; } = string.Empty;
        public string PublicKeyToken { get; set; } = string.Empty;
        public string ProcessorArchitecture { get; set; } = string.Empty;
    }

    /// <summary>
    /// Type shape used both for tree views and dependency summaries. Carries
    /// the kind (class/interface/struct/enum/delegate), the declaring
    /// namespace and base/interfaces metadata for quick filtering in the UI.
    /// </summary>
    public sealed class TypeInfo
    {
        public string FullName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = "class";
        public bool IsPublic { get; set; } = true;
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public string BaseType { get; set; } = string.Empty;
        public List<string> Interfaces { get; set; } = new();
        public List<MemberInfo> Members { get; set; } = new();
        public List<AttributeInfo> Attributes { get; set; } = new();
    }

    /// <summary>
    /// Member shape used for methods, properties, fields and events. The kind
    /// is a stable identifier so the UI can route to the right renderer
    /// (signature vs. getter/setter vs. backing field).
    /// </summary>
    public sealed class MemberInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = "method";
        public string Signature { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public List<ParameterInfo> Parameters { get; set; } = new();
        public bool IsPublic { get; set; } = true;
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsVirtual { get; set; }
        public string Visibility { get; set; } = "public";
        public List<AttributeInfo> Attributes { get; set; } = new();
    }

    public sealed class ParameterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsOptional { get; set; }
        public string? DefaultValue { get; set; }
    }

    public sealed class AttributeInfo
    {
        public string TypeName { get; set; } = string.Empty;
        public List<string> Arguments { get; set; } = new();
    }

    /// <summary>
    /// Edge in the dependency graph. Carries both the simple name (used to
    /// match nodes when the assembly is not loaded) and the full identity
    /// when available.
    /// </summary>
    public sealed class DependencyEdge
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string ToVersion { get; set; } = string.Empty;
        public string Kind { get; set; } = "reference";
    }

    /// <summary>
    /// Lightweight graph used both by the UI renderer (force layout, canvas
    /// drawing) and the MCP tool response. Coordinates are populated by the
    /// layout pass on the UI side and persisted on the model so callers can
    /// inspect the positions if they choose.
    /// </summary>
    public sealed class DependencyGraph
    {
        public string Root { get; set; } = string.Empty;
        public List<GraphNode> Nodes { get; set; } = new();
        public List<GraphEdge> Edges { get; set; } = new();
    }

    public sealed class GraphNode
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Kind { get; set; } = "assembly";
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsRoot { get; set; }
        public bool IsMissing { get; set; }
    }

    public sealed class GraphEdge
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Kind { get; set; } = "reference";
    }
}
