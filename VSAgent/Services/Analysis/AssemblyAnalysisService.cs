using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using VSAgent.Models;
using TypeInfo = VSAgent.Models.TypeInfo;
using MemberInfo = VSAgent.Models.MemberInfo;
using ParameterInfo = VSAgent.Models.ParameterInfo;

namespace VSAgent.Services.Analysis
{
    /// <summary>
    /// Inspects .dll and .exe files. Handles both managed (.NET) and unmanaged
    /// (native) PE files in a single code path so the DLLSpy tab and the
    /// <c>vs_analyze_assembly</c> MCP tool can present a uniform shape.
    ///
    /// Managed assemblies use <see cref="System.Reflection.Metadata"/> to avoid
    /// loading the assembly into the runtime — that means we can also analyze
    /// mixed-mode binaries and assemblies targeting other frameworks without
    /// runtime failures. Native modules fall back to PE header / export table
    /// inspection via <see cref="PEReader"/>.
    /// </summary>
    public sealed class AssemblyAnalysisService
    {
        public AssemblyAnalysis Analyze(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Assembly file not found.", filePath);

            var result = new AssemblyAnalysis
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                AnalyzedAtUtc = DateTime.UtcNow
            };

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new PEReader(stream);
            if (!reader.HasMetadata)
            {
                return BuildUnmanagedAnalysis(filePath, reader, result);
            }
            return BuildManagedAnalysis(filePath, reader, result);
        }

        // ===== Managed (.NET) =====================================================

        private static AssemblyAnalysis BuildManagedAnalysis(
            string filePath, PEReader reader, AssemblyAnalysis result)
        {
            result.IsManaged = true;
            result.AssemblyKind = "managed";

            var md = reader.GetMetadataReader();
            FillIdentity(md, result);
            result.Architecture = ReadMachine(reader.PEHeaders.CoffHeader.Machine);
            result.TargetFramework = InferTargetFramework(md);

            foreach (var th in md.TypeDefinitions)
            {
                try
                {
                    var td = md.GetTypeDefinition(th);
                    var type = ReadType(md, td);
                    if (type != null) result.Types.Add(type);
                }
                catch { /* malformed entries are skipped to keep the whole file usable */ }
            }

            result.Exports = ReadExportedTypes(md, result.Identity.Name);

            foreach (var rh in md.AssemblyReferences)
            {
                var ar = md.GetAssemblyReference(rh);
                var name = md.GetString(ar.Name);
                if (string.IsNullOrEmpty(name)) continue;
                result.References.Add(new DependencyEdge
                {
                    From = result.Identity.Name,
                    To = name,
                    ToVersion = ar.Version.ToString(),
                    Kind = "reference"
                });
            }

            BuildDependencyGraph(result);
            return result;
        }

        private static void FillIdentity(MetadataReader md, AssemblyAnalysis result)
        {
            if (md.IsAssembly)
            {
                var ad = md.GetAssemblyDefinition();
                result.Identity.Name = md.GetString(ad.Name) ?? string.Empty;
                result.Identity.Version = ad.Version.ToString();
                result.Identity.Culture = md.GetString(ad.Culture) ?? string.Empty;
            }
            else
            {
                result.Identity.Name = Path.GetFileNameWithoutExtension(result.FilePath);
            }
            if (md.IsAssembly)
            {
                var ad = md.GetAssemblyDefinition();
                result.Identity.PublicKeyToken = ComputePublicKeyToken(md, ad.PublicKey);
            }
        }

        private static string ComputePublicKeyToken(MetadataReader md, BlobHandle publicKey)
        {
            try
            {
                var bytes = md.GetBlobBytes(publicKey);
                if (bytes == null || bytes.Length == 0) return string.Empty;
                using var sha1 = System.Security.Cryptography.SHA1.Create();
                var hash = sha1.ComputeHash(bytes);
                var token = new byte[8];
                Array.Copy(hash, hash.Length - 8, token, 0, 8);
                Array.Reverse(token);
                var sb = new StringBuilder(16);
                foreach (var b in token) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string InferTargetFramework(MetadataReader md)
        {
            foreach (var ad in md.CustomAttributes)
            {
                try
                {
                    var ca = md.GetCustomAttribute(ad);
                    var name = ResolveAttributeTypeName(md, ca);
                    if (!string.Equals(name, "TargetFrameworkAttribute", StringComparison.OrdinalIgnoreCase) &&
                        !name.EndsWith(".TargetFrameworkAttribute", StringComparison.Ordinal))
                        continue;
                    var value = ReadFirstStringArg(md, ca);
                    if (!string.IsNullOrEmpty(value)) return value;
                }
                catch { }
            }
            return string.Empty;
        }

        private static string ResolveAttributeTypeName(MetadataReader md, CustomAttribute ca)
        {
            try
            {
                var member = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                var parent = member.Parent;
                return md.GetTypeReference((TypeReferenceHandle)parent).Name.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string? ReadFirstStringArg(MetadataReader md, CustomAttribute ca)
        {
            try
            {
                var reader = md.GetBlobReader(ca.Value);
                var prolog = reader.ReadUInt16();
                if (prolog != 0x0001) return null;
                while (reader.RemainingBytes > 0)
                {
                    var kind = reader.ReadByte();
                    if (kind == 0xFF) break; // end of fixed/named args
                    var isLong = (kind & 0x80) != 0;
                    var length = isLong ? reader.ReadUInt32() : (uint)reader.ReadByte();
                    if (length == 0) continue;
                    var str = Encoding.UTF8.GetString(reader.ReadBytes((int)length));
                    if (!string.IsNullOrEmpty(str)) return str;
                }
            }
            catch { }
            return null;
        }

        private static TypeInfo? ReadType(MetadataReader md, TypeDefinition td)
        {
            var name = md.GetString(td.Name);
            var ns = md.GetString(td.Namespace) ?? string.Empty;
            if (string.IsNullOrEmpty(name) || name == "<Module>") return null;

            var visibility = td.Attributes & TypeAttributes.VisibilityMask;
            var type = new TypeInfo
            {
                Name = name,
                Namespace = ns,
                FullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name,
                IsPublic = visibility == TypeAttributes.Public ||
                           visibility == TypeAttributes.NestedPublic,
                IsAbstract = (td.Attributes & TypeAttributes.Abstract) == TypeAttributes.Abstract,
                IsSealed = (td.Attributes & TypeAttributes.Sealed) == TypeAttributes.Sealed
            };

            if ((td.Attributes & TypeAttributes.Interface) == TypeAttributes.Interface)
            {
                type.Kind = "interface";
            }
            else
            {
                try
                {
                    var baseType = td.BaseType;
                    if (!baseType.IsNil)
                    {
                        var tr = md.GetTypeReference((TypeReferenceHandle)baseType);
                        var baseName = md.GetString(tr.Name);
                        if (baseName == "Enum") type.Kind = "enum";
                        else if (baseName == "ValueType") type.Kind = "struct";
                        else if (baseName == "MulticastDelegate") type.Kind = "delegate";
                        else type.BaseType = ResolveTypeRefName(md, tr);
                    }
                }
                catch { }
            }

            foreach (var mh in td.GetMethods())
            {
                try
                {
                    var mdef = md.GetMethodDefinition(mh);
                    var member = ReadMethod(md, mdef);
                    if (member != null) type.Members.Add(member);
                }
                catch { }
            }
            foreach (var ph in td.GetProperties())
            {
                try
                {
                    var pdef = md.GetPropertyDefinition(ph);
                    var member = ReadProperty(md, pdef);
                    if (member != null) type.Members.Add(member);
                }
                catch { }
            }
            foreach (var fh in td.GetFields())
            {
                try
                {
                    var fdef = md.GetFieldDefinition(fh);
                    var member = ReadField(md, fdef);
                    if (member != null) type.Members.Add(member);
                }
                catch { }
            }
            foreach (var eh in td.GetEvents())
            {
                try
                {
                    var edef = md.GetEventDefinition(eh);
                    var member = ReadEvent(md, edef);
                    if (member != null) type.Members.Add(member);
                }
                catch { }
            }
            foreach (var ih in td.GetInterfaceImplementations())
            {
                try
                {
                    var iimpl = md.GetInterfaceImplementation(ih);
                    if (iimpl.Interface.IsNil) continue;
                    var tr = md.GetTypeReference((TypeReferenceHandle)iimpl.Interface);
                    var resolved = ResolveTypeRefName(md, tr);
                    if (!string.IsNullOrEmpty(resolved)) type.Interfaces.Add(resolved);
                }
                catch { }
            }
            return type;
        }

        private static MemberInfo? ReadMethod(MetadataReader md, MethodDefinition mdef)
        {
            var name = md.GetString(mdef.Name);
            if (string.IsNullOrEmpty(name)) return null;
            var isCtor = name == ".ctor" || name == ".cctor";
            var attrs = mdef.Attributes;
            var member = new MemberInfo
            {
                Name = name,
                Kind = isCtor ? "constructor" : "method",
                IsStatic = (attrs & MethodAttributes.Static) == MethodAttributes.Static,
                IsAbstract = (attrs & MethodAttributes.Abstract) == MethodAttributes.Abstract,
                IsVirtual = (attrs & MethodAttributes.Virtual) == MethodAttributes.Virtual,
                Visibility = ResolveMethodVisibility(attrs),
                IsPublic = ResolveMethodVisibility(attrs) == "public",
                ReturnType = "void",
                Signature = name + "(...)"
            };
            // Decode signature — best-effort.
            try
            {
                var sig = mdef.DecodeSignature(new SignatureFormatter(), null);
                member.ReturnType = isCtor ? string.Empty : sig.ReturnType;
                var paramString = string.Join(", ", sig.ParameterTypes);
                member.Signature = isCtor
                    ? name + "(" + paramString + ")"
                    : (string.IsNullOrEmpty(member.ReturnType) ? "?" : member.ReturnType) + " " + name + "(" + paramString + ")";
                foreach (var p in sig.ParameterTypes)
                {
                    member.Parameters.Add(new ParameterInfo { Type = p, Name = string.Empty });
                }
            }
            catch { }
            return member;
        }

        private static MemberInfo? ReadProperty(MetadataReader md, PropertyDefinition pdef)
        {
            var name = md.GetString(pdef.Name);
            if (string.IsNullOrEmpty(name)) return null;
            var type = "?";
            try { var sig = pdef.DecodeSignature(new SignatureFormatter(), null); type = sig.ReturnType ?? "?"; } catch { }
            if (string.IsNullOrEmpty(type)) type = "?";
            return new MemberInfo
            {
                Name = name,
                Kind = "property",
                Visibility = "public",
                IsPublic = true,
                Signature = name + " : " + type
            };
        }

        private static MemberInfo? ReadField(MetadataReader md, FieldDefinition fdef)
        {
            var name = md.GetString(fdef.Name);
            if (string.IsNullOrEmpty(name)) return null;
            var type = "?";
            try { type = fdef.DecodeSignature(new SignatureFormatter(), null) ?? "?"; } catch { }
            return new MemberInfo
            {
                Name = name,
                Kind = "field",
                Visibility = ResolveFieldVisibility(fdef.Attributes),
                IsPublic = ResolveFieldVisibility(fdef.Attributes) == "public",
                IsStatic = (fdef.Attributes & FieldAttributes.Static) == FieldAttributes.Static,
                Signature = name + " : " + type
            };
        }

        private static MemberInfo? ReadEvent(MetadataReader md, EventDefinition edef)
        {
            var name = md.GetString(edef.Name);
            if (string.IsNullOrEmpty(name)) return null;
            return new MemberInfo
            {
                Name = name,
                Kind = "event",
                Visibility = "public",
                IsPublic = true,
                Signature = name
            };
        }

        private static string ResolveMethodVisibility(MethodAttributes attrs)
        {
            var v = attrs & MethodAttributes.MemberAccessMask;
            switch (v)
            {
                case MethodAttributes.Public: return "public";
                case MethodAttributes.Family: return "protected";
                case MethodAttributes.Assembly: return "internal";
                case MethodAttributes.FamANDAssem: return "private protected";
                case MethodAttributes.FamORAssem: return "protected internal";
                default: return "private";
            }
        }

        private static string ResolveFieldVisibility(FieldAttributes attrs)
        {
            var v = attrs & FieldAttributes.FieldAccessMask;
            switch (v)
            {
                case FieldAttributes.Public: return "public";
                case FieldAttributes.Family: return "protected";
                case FieldAttributes.Assembly: return "internal";
                default: return "private";
            }
        }

        private static string ResolveTypeRefName(MetadataReader md, TypeReference tr)
        {
            var name = md.GetString(tr.Name);
            var ns = md.GetString(tr.Namespace) ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static List<MemberInfo> ReadExportedTypes(MetadataReader md, string assemblyName)
        {
            var list = new List<MemberInfo>();
            foreach (var eh in md.ExportedTypes)
            {
                try
                {
                    var et = md.GetExportedType(eh);
                    var name = md.GetString(et.Name);
                    var ns = md.GetString(et.Namespace) ?? string.Empty;
                    var full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
                    if (string.IsNullOrEmpty(name)) continue;
                    list.Add(new MemberInfo
                    {
                        Name = full ?? string.Empty,
                        Kind = "export",
                        Visibility = "public",
                        IsPublic = true,
                        Signature = full ?? string.Empty
                    });
                }
                catch { }
            }
            return list;
        }

        // ===== Unmanaged (native) =================================================

        private static AssemblyAnalysis BuildUnmanagedAnalysis(
            string filePath, PEReader reader, AssemblyAnalysis result)
        {
            result.IsManaged = false;
            result.IsValidPe = true;
            result.AssemblyKind = "native";
            result.Architecture = ReadMachine(reader.PEHeaders.CoffHeader.Machine);
            result.Identity.Name = Path.GetFileNameWithoutExtension(filePath);

            try
            {
                var exportDir = reader.PEHeaders.PEHeader?.ExportTableDirectory;
                if (exportDir.HasValue && exportDir.Value.Size > 0)
                {
                    var block = reader.GetSectionData(exportDir.Value.RelativeVirtualAddress);
                    var raw = block.GetReader();
                    var dir = raw.ReadBytes(exportDir.Value.Size);
                    if (dir != null && dir.Length >= 40)
                    {
                        int numberOfFunctions = BitConverter.ToInt32(dir, 20);
                        int numberOfNames = BitConverter.ToInt32(dir, 24);
                        int addressOfFunctionsRva = BitConverter.ToInt32(dir, 28);
                        int addressOfNamesRva = BitConverter.ToInt32(dir, 32);
                        int addressOfNameOrdsRva = BitConverter.ToInt32(dir, 36);

                        var fnBlock = reader.GetSectionData(addressOfFunctionsRva).GetReader().ReadBytes(numberOfFunctions * 4);
                        var nmBlock = reader.GetSectionData(addressOfNamesRva).GetReader().ReadBytes(numberOfNames * 4);
                        var noBlock = reader.GetSectionData(addressOfNameOrdsRva).GetReader().ReadBytes(numberOfNames * 2);

                        var nameLookup = new Dictionary<int, string>();
                        for (int i = 0; i < numberOfNames; i++)
                        {
                            int nameRva2 = BitConverter.ToInt32(nmBlock, i * 4);
                            var nameData = reader.GetSectionData(nameRva2).GetReader();
                            nameLookup[i] = ReadNullString(nameData);
                        }
                        for (int i = 0; i < numberOfFunctions; i++)
                        {
                            int ordinal = BitConverter.ToInt32(fnBlock, i * 4);
                            string name = i < numberOfNames ? nameLookup[i] : "#" + ordinal;
                            result.Exports.Add(new MemberInfo
                            {
                                Name = name,
                                Kind = "export",
                                Visibility = "public",
                                IsPublic = true,
                                Signature = "ordinal " + ordinal,
                                ReturnType = "external"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = "Export parsing failed: " + ex.Message;
            }

            BuildDependencyGraph(result);
            return result;
        }

        private static string ReadNullString(System.Reflection.Metadata.BlobReader reader)
        {
            var sb = new StringBuilder();
            while (reader.RemainingBytes > 0)
            {
                var b = reader.ReadByte();
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        // ===== Helpers ============================================================

        private static string ReadMachine(Machine machine)
        {
            switch (machine)
            {
                case Machine.I386: return "x86";
                case Machine.Amd64: return "x64";
                case Machine.Arm: return "arm";
                case Machine.Arm64: return "arm64";
                default: return machine.ToString().ToLowerInvariant();
            }
        }

        private static void BuildDependencyGraph(AssemblyAnalysis analysis)
        {
            var graph = analysis.Graph;
            graph.Root = analysis.Identity.Name;
            graph.Nodes.Clear();
            graph.Edges.Clear();

            graph.Nodes.Add(new GraphNode
            {
                Id = analysis.Identity.Name,
                Label = string.IsNullOrEmpty(analysis.Identity.Name) ? analysis.FileName : analysis.Identity.Name,
                Kind = "self",
                IsRoot = true
            });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { analysis.Identity.Name };
            foreach (var edge in analysis.References)
            {
                if (string.IsNullOrEmpty(edge.To)) continue;
                if (seen.Add(edge.To))
                {
                    graph.Nodes.Add(new GraphNode
                    {
                        Id = edge.To,
                        Label = edge.To,
                        Kind = "dependency"
                    });
                }
                graph.Edges.Add(new GraphEdge
                {
                    From = edge.From,
                    To = edge.To,
                    Kind = edge.Kind
                });
            }
        }
    }

    /// <summary>
    /// Minimal signature formatter used by System.Reflection.Metadata to
    /// render parameter types. Only the type names are emitted — full generic
    /// instantiations and custom modifiers are intentionally omitted to keep
    /// the rendering predictable across language versions.
    /// </summary>
    internal sealed class SignatureFormatter : ISignatureTypeProvider<string, object>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => "ref " + elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
            => genericType + "<" + string.Join(", ", typeArguments) + ">";
        public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString().ToLowerInvariant();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(System.Reflection.Metadata.MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var td = reader.GetTypeDefinition(handle);
            return reader.GetString(td.Namespace) + "." + reader.GetString(td.Name);
        }
        public string GetTypeFromReference(System.Reflection.Metadata.MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var tr = reader.GetTypeReference(handle);
            return reader.GetString(tr.Namespace) + "." + reader.GetString(tr.Name);
        }
        public string GetTypeFromSpecification(System.Reflection.Metadata.MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "?";
    }
}
