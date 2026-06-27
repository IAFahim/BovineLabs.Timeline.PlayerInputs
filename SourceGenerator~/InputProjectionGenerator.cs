using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BovineLabs.Timeline.PlayerInputs.Generator
{
    [Generator]
    public sealed class InputProjectionGenerator : IIncrementalGenerator
    {
        private const string Ns = "BovineLabs.Timeline.PlayerInputs.Data";
        private const string Marker = "IPlayerInput";

        private const int KindAction = 0;
        private const int KindDelta = 1;
        private const int KindDown = 2;
        private const int KindUp = 3;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var action = Collect(context, Ns + ".InputActionAttribute", KindAction);
            var delta = Collect(context, Ns + ".InputActionDeltaAttribute", KindDelta);
            var down = Collect(context, Ns + ".InputActionDownAttribute", KindDown);
            var up = Collect(context, Ns + ".InputActionUpAttribute", KindUp);

            var all = action.Collect()
                .Combine(delta.Collect())
                .Combine(down.Collect())
                .Combine(up.Collect());

            context.RegisterSourceOutput(all, static (ctx, data) =>
            {
                var fields = data.Left.Left.Left
                    .AddRange(data.Left.Left.Right)
                    .AddRange(data.Left.Right)
                    .AddRange(data.Right);
                Emit(ctx, fields);
            });
        }

        private static IncrementalValuesProvider<FieldCandidate> Collect(
            IncrementalGeneratorInitializationContext context, string attribute, int kind)
        {
            return context.SyntaxProvider.ForAttributeWithMetadataName(
                attribute,
                static (node, _) => node is VariableDeclaratorSyntax or FieldDeclarationSyntax,
                (gen, _) => Map(gen, kind));
        }

        private static FieldCandidate Map(GeneratorAttributeSyntaxContext gen, int kind)
        {
            var field = gen.TargetSymbol as IFieldSymbol;
            var type = field?.ContainingType;
            var typeDecl = gen.TargetNode.FirstAncestorOrSelf<TypeDeclarationSyntax>();

            // Skip anything that isn't a non-static instance field (properties, static fields, const, etc.):
            // those can't be a per-provider input slot and would otherwise create a phantom '?' group.
            var skip = field == null || field.IsStatic || field.IsConst;

            var ns = type == null || type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : type.ContainingNamespace.ToDisplayString();
            var isStruct = type?.TypeKind == TypeKind.Struct;
            var isNested = type?.ContainingType != null;
            var isPartial = typeDecl?.Modifiers.Any(SyntaxKind.PartialKeyword) ?? false;
            var implementsMarker = type != null && type.AllInterfaces.Any(i => i.Name == Marker);

            var fieldType = field == null ? "?" : MapType(field.Type);
            var loc = field?.Locations.FirstOrDefault() ?? Location.None;
            var span = loc.SourceSpan;
            var lineSpan = loc.GetLineSpan().Span;
            var locInfo = new LocationInfo(
                loc.SourceTree?.FilePath ?? string.Empty, span.Start, span.Length,
                lineSpan.Start.Line, lineSpan.Start.Character, lineSpan.End.Line, lineSpan.End.Character);

            return new FieldCandidate(
                ns, type?.Name ?? "?", isStruct, isNested, isPartial, implementsMarker,
                field?.Name ?? "?", fieldType, kind, skip, locInfo);
        }

        private static string MapType(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_Boolean) return "bool";
            if (type.SpecialType == SpecialType.System_Single) return "float";
            var full = type.ToDisplayString();
            if (full == "Unity.Mathematics.float2") return "float2";
            if (full == Ns + ".ButtonState") return "ButtonState";
            return "?";
        }

        private static void Emit(SourceProductionContext ctx, ImmutableArray<FieldCandidate> fields)
        {
            foreach (var group in fields.GroupBy(f => f.Namespace + "::" + f.TypeName))
            {
                var members = group.Where(f => !f.Skip).ToImmutableArray();
                if (members.IsEmpty)
                {
                    continue;
                }

                var head = members[0];

                if (!head.ImplementsMarker)
                {
                    Report(ctx, Rules.Marker, head, head.TypeName);
                    continue;
                }

                if (!head.IsStruct)
                {
                    Report(ctx, Rules.Struct, head, head.TypeName);
                    continue;
                }

                if (head.IsNested)
                {
                    Report(ctx, Rules.Nested, head, head.TypeName);
                    continue;
                }

                if (!head.IsPartial)
                {
                    Report(ctx, Rules.Partial, head, head.TypeName);
                    continue;
                }

                var blocked = false;
                foreach (var f in members)
                {
                    if (f.FieldType == "?")
                    {
                        Report(ctx, Rules.FieldType, f, f.FieldName);
                        blocked = true;
                    }
                    else if (f.Kind == KindDelta && f.FieldType != "float" && f.FieldType != "float2")
                    {
                        Report(ctx, Rules.Delta, f, f.FieldName);
                        blocked = true;
                    }
                    else if ((f.Kind == KindDown || f.Kind == KindUp) && f.FieldType != "bool")
                    {
                        Report(ctx, Rules.UpDown, f, f.FieldName);
                        blocked = true;
                    }
                }

                // One field carrying more than one [InputAction*] attribute would emit duplicate Map/Bindings
                // members (CS0102). Block it with a clear diagnostic instead of an opaque compile error.
                foreach (var dup in members.GroupBy(f => f.FieldName).Where(g => g.Count() > 1))
                {
                    Report(ctx, Rules.Conflict, dup.First(), dup.Key);
                    blocked = true;
                }

                if (blocked)
                {
                    continue;
                }

                ctx.AddSource($"{head.Namespace.Replace('.', '_')}_{head.TypeName}.PlayerInput.g.cs",
                    SourceText.From(Render(head, members), Encoding.UTF8));
            }
        }

        private const string Ent = "global::Unity.Entities";
        private const string Data = "global::BovineLabs.Timeline.PlayerInputs.Data";
        private const string Access = "global::BovineLabs.Timeline.PlayerInputs.InputAccess";
        private const string Group = "global::BovineLabs.Timeline.PlayerInputs.PlayerInputProjectionGroup";
        private const string AuthUtil = "global::BovineLabs.Timeline.PlayerInputs.Authoring.MultiInputSettingsAuthoringUtility";
        private const string Iar = "global::UnityEngine.InputSystem.InputActionReference";

        private static string Render(FieldCandidate head, ImmutableArray<FieldCandidate> members)
        {
            var t = head.TypeName;
            var map = t + "_Map";
            var needsDelta = members.Any(f => f.Kind == KindDelta);
            var hasNs = head.Namespace.Length > 0;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable");
            if (hasNs)
            {
                sb.Append("namespace ").AppendLine(head.Namespace);
                sb.AppendLine("{");
            }

            sb.Append("partial struct ").AppendLine(t);
            sb.AppendLine("{");
            sb.AppendLine("    [System.Serializable]");
            sb.AppendLine("    public sealed class Bindings");
            sb.AppendLine("    {");
            foreach (var f in members)
            {
                sb.Append("        public ").Append(Iar).Append(' ').Append(f.FieldName).AppendLine(";");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.Append("public struct ").Append(map).Append(" : ").Append(Ent).AppendLine(".IComponentData");
            sb.AppendLine("{");
            foreach (var f in members)
            {
                sb.Append("    public byte ").Append(f.FieldName).AppendLine(";");
            }

            sb.AppendLine("}");
            sb.AppendLine();

            sb.Append("public sealed class ").Append(t).AppendLine("_Authoring : global::UnityEngine.MonoBehaviour");
            sb.AppendLine("{");
            sb.Append("    public ").Append(t).AppendLine(".Bindings Bindings;");
            sb.Append("    public sealed class Baker : ").Append(Ent).Append(".Baker<").Append(t).AppendLine("_Authoring>");
            sb.AppendLine("    {");
            sb.Append("        public override void Bake(").Append(t).AppendLine("_Authoring authoring)");
            sb.AppendLine("        {");
            sb.Append("            ").Append(AuthUtil).AppendLine(".DependsOnSettings(this);");
            sb.Append("            var entity = GetEntity(").Append(Ent).AppendLine(".TransformUsageFlags.None);");
            sb.Append("            var map = new ").Append(map).AppendLine("();");
            foreach (var f in members)
            {
                var isDelta = f.Kind == KindDelta ? "true" : "false";
                sb.Append("            map.").Append(f.FieldName).Append(" = Resolve(authoring.Bindings?.")
                    .Append(f.FieldName).Append(", \"").Append(f.FieldName).Append("\", ").Append(isDelta).AppendLine(");");
            }

            sb.AppendLine("            AddComponent(entity, map);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.Append("        private static byte Resolve(").Append(Iar).AppendLine(" reference, string field, bool isDelta)");
            sb.AppendLine("        {");
            sb.Append("            if (reference != null && ").Append(AuthUtil)
                .AppendLine(".TryGetIndex(reference, out var index))");
            sb.AppendLine("            {");
            sb.Append("                if (isDelta && IsPointerDelta(reference)) global::UnityEngine.Debug.LogError(\"").Append(t)
                .AppendLine("_Authoring: field '\" + field + \"' is [InputActionDelta] but is bound to a pointer Delta control; use [InputAction] (raw) for mouse — Delta is already per-frame travel and multiplying by dt makes it framerate-dependent.\");");
            sb.AppendLine("                return index;");
            sb.AppendLine("            }");
            sb.Append("            global::UnityEngine.Debug.LogError(\"").Append(t)
                .AppendLine("_Authoring: input field '\" + field + \"' did not resolve its InputActionReference in MultiInputSettings.\");");
            sb.AppendLine("            return byte.MaxValue;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.Append("        private static bool IsPointerDelta(").Append(Iar).AppendLine(" reference)");
            sb.AppendLine("        {");
            sb.AppendLine("            var action = reference.action;");
            sb.AppendLine("            if (action == null) return false;");
            sb.AppendLine("            if (action.expectedControlType == \"Delta\") return true;");
            sb.AppendLine("            var bindings = action.bindings;");
            sb.AppendLine("            for (int i = 0; i < bindings.Count; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                var p = bindings[i].effectivePath;");
            sb.AppendLine("                if (string.IsNullOrEmpty(p)) p = bindings[i].path;");
            sb.AppendLine("                if (!string.IsNullOrEmpty(p) && p.Contains(\"/delta\")) return true;");
            sb.AppendLine("            }");
            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.Append('[').Append(Ent).Append(".UpdateInGroup(typeof(").Append(Group).AppendLine("))]");
            sb.Append('[').Append(Ent).Append(".WorldSystemFilter(").Append(Ent)
                .Append(".WorldSystemFilterFlags.LocalSimulation | ").Append(Ent)
                .Append(".WorldSystemFilterFlags.ClientSimulation | ").Append(Ent)
                .AppendLine(".WorldSystemFilterFlags.ServerSimulation)]");
            sb.Append("public partial struct ").Append(t).Append("_Projection : ").Append(Ent).AppendLine(".ISystem");
            sb.AppendLine("{");
            sb.AppendLine("    private bool warned;");
            sb.AppendLine();
            sb.Append("    public void OnCreate(ref ").Append(Ent).AppendLine(".SystemState state)");
            sb.AppendLine("    {");
            sb.Append("        state.RequireForUpdate<").Append(Data).AppendLine(".InputRegistry>();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.Append("    public void OnUpdate(ref ").Append(Ent).AppendLine(".SystemState state)");
            sb.AppendLine("    {");
            // Coop designers naturally place one *_Authoring per player → multiple maps. Don't silently die:
            // take the first map and warn once. Zero maps with providers present is also a once-only error.
            sb.Append("        var map = default(").Append(map).AppendLine(");");
            sb.AppendLine("        var mapCount = 0;");
            sb.Append("        foreach (var m in ").Append(Ent).Append(".SystemAPI.Query<").Append(Ent)
                .Append(".RefRO<").Append(map).AppendLine(">>())");
            sb.AppendLine("        {");
            sb.AppendLine("            if (mapCount == 0) map = m.ValueRO;");
            sb.AppendLine("            mapCount++;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if (mapCount == 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!this.warned)");
            sb.AppendLine("            {");
            sb.Append("                var anyProvider = ").Append(Ent).Append(".SystemAPI.QueryBuilder().WithAll<")
                .Append(Data).AppendLine(".ProviderTag>().Build();");
            sb.AppendLine("                if (!anyProvider.IsEmpty)");
            sb.AppendLine("                {");
            sb.Append("                    global::UnityEngine.Debug.LogError(\"").Append(map)
                .AppendLine(": no \" + nameof(" + t + "_Authoring) + \" was baked but input providers exist; typed input is disabled. Add exactly one " + t + "_Authoring to the scene.\");");
            sb.AppendLine("                    this.warned = true;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if (mapCount > 1 && !this.warned)");
            sb.AppendLine("        {");
            sb.Append("            global::UnityEngine.Debug.LogError(\"").Append(map)
                .AppendLine(": found \" + mapCount + \" " + t + "_Authoring instances but expected exactly one; using the first. Keep a single " + t + "_Authoring per world.\");");
            sb.AppendLine("            this.warned = true;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.Append("        var missing = ").Append(Ent).Append(".SystemAPI.QueryBuilder().WithAll<").Append(Data)
                .Append(".ProviderTag>().WithNone<").Append(t).AppendLine(">().Build();");
            sb.Append("        if (!missing.IsEmpty) state.EntityManager.AddComponent<").Append(t).AppendLine(">(missing);");
            if (needsDelta)
            {
                sb.Append("        var dt = (float)").Append(Ent).AppendLine(".SystemAPI.Time.DeltaTime;");
            }

            sb.AppendLine();
            sb.Append("        foreach (var (input, st, axes) in ").Append(Ent).Append(".SystemAPI.Query<")
                .Append(Ent).Append(".RefRW<").Append(t).Append(">, ").Append(Ent).Append(".RefRO<").Append(Data)
                .Append(".InputState>, ").Append(Ent).Append(".DynamicBuffer<").Append(Data)
                .Append(".InputAxis>>().WithAll<").Append(Data).AppendLine(".ProviderTag>())");
            sb.AppendLine("        {");
            sb.AppendLine("            ref var v = ref input.ValueRW;");
            sb.AppendLine("            var s = st.ValueRO;");
            foreach (var f in members)
            {
                sb.Append(FillLines(f));
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            if (hasNs)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static string FillLines(FieldCandidate f)
        {
            var id = "map." + f.FieldName;
            var v = "v." + f.FieldName;
            switch (f.FieldType)
            {
                case "ButtonState":
                    return $"            {v}.Down = s.Down[{id}]; {v}.Pressed = s.Held[{id}]; {v}.Up = s.Up[{id}];\n";
                case "bool":
                    var bit = f.Kind == KindDown ? "Down" : f.Kind == KindUp ? "Up" : "Held";
                    return $"            {v} = s.{bit}[{id}];\n";
                case "float2":
                    return f.Kind == KindDelta
                        ? $"            {v} = {Access}.ReadAxis(axes, {id}) * dt;\n"
                        : $"            {v} = {Access}.ReadAxis(axes, {id});\n";
                case "float":
                    return f.Kind == KindDelta
                        ? $"            {v} = {Access}.ReadAxis(axes, {id}).x * dt;\n"
                        : $"            {v} = {Access}.ReadAxis(axes, {id}).x;\n";
                default:
                    return string.Empty;
            }
        }

        private static void Report(SourceProductionContext ctx, DiagnosticDescriptor rule, FieldCandidate at, string arg)
        {
            var loc = at.Loc;
            var location = loc.FilePath.Length > 0
                ? Location.Create(loc.FilePath, new TextSpan(loc.SpanStart, loc.SpanLength),
                    new LinePositionSpan(
                        new LinePosition(loc.StartLine, loc.StartChar),
                        new LinePosition(loc.EndLine, loc.EndChar)))
                : Location.None;
            ctx.ReportDiagnostic(Diagnostic.Create(rule, location, arg));
        }

        private readonly record struct FieldCandidate(
            string Namespace, string TypeName, bool IsStruct, bool IsNested, bool IsPartial, bool ImplementsMarker,
            string FieldName, string FieldType, int Kind, bool Skip, LocationInfo Loc);

        // Source location for diagnostics only. Equality is constant so a line shift elsewhere in the file
        // never busts the incremental cache for an otherwise-unchanged candidate.
        private readonly struct LocationInfo : System.IEquatable<LocationInfo>
        {
            public readonly string FilePath;
            public readonly int SpanStart;
            public readonly int SpanLength;
            public readonly int StartLine;
            public readonly int StartChar;
            public readonly int EndLine;
            public readonly int EndChar;

            public LocationInfo(string filePath, int spanStart, int spanLength,
                int startLine, int startChar, int endLine, int endChar)
            {
                this.FilePath = filePath;
                this.SpanStart = spanStart;
                this.SpanLength = spanLength;
                this.StartLine = startLine;
                this.StartChar = startChar;
                this.EndLine = endLine;
                this.EndChar = endChar;
            }

            public bool Equals(LocationInfo other) => true;

            public override bool Equals(object obj) => obj is LocationInfo;

            public override int GetHashCode() => 0;
        }

        private static class Rules
        {
            public static readonly DiagnosticDescriptor Marker = Make(
                "BLI001", "IPlayerInput required", "'{0}' uses input action attributes but does not implement IPlayerInput");

            public static readonly DiagnosticDescriptor Struct = Make(
                "BLI002", "Must be a struct", "IPlayerInput type '{0}' must be a struct");

            public static readonly DiagnosticDescriptor Partial = Make(
                "BLI003", "Must be partial", "IPlayerInput struct '{0}' must be declared partial");

            public static readonly DiagnosticDescriptor FieldType = Make(
                "BLI004", "Unsupported field type",
                "Input field '{0}' must be ButtonState, bool, float, or float2");

            public static readonly DiagnosticDescriptor Delta = Make(
                "BLI005", "Invalid delta field", "[InputActionDelta] field '{0}' must be float or float2");

            public static readonly DiagnosticDescriptor UpDown = Make(
                "BLI006", "Invalid up/down field", "[InputActionDown]/[InputActionUp] field '{0}' must be bool");

            public static readonly DiagnosticDescriptor Nested = Make(
                "BLI007", "Nested type unsupported", "IPlayerInput struct '{0}' must be a top-level type");

            public static readonly DiagnosticDescriptor Conflict = Make(
                "BLI008", "Conflicting input attributes",
                "Input field '{0}' has more than one [InputAction*] attribute; keep exactly one");

            private static DiagnosticDescriptor Make(string id, string title, string message)
            {
                return new DiagnosticDescriptor(id, title, message, "PlayerInputs", DiagnosticSeverity.Error, true);
            }
        }
    }
}
