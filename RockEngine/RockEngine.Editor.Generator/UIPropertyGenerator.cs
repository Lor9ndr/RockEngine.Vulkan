using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RockEngine.Editor.Generator
{
    [Generator]
    public class UIPropertyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                    transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
                .Where(static m => m is not null)
                .Select(static (m, _) => m!.Value); // распаковываем nullable

            var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

            context.RegisterSourceOutput(compilationAndClasses,
                (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl &&
                   classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) ||
                                               m.IsKind(SyntaxKind.InternalKeyword));
        }

        private static (ClassDeclarationSyntax classDecl, bool isComponent, bool hasUI)? GetSemanticTargetForGeneration(
            GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var semanticModel = context.SemanticModel;
            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
            if (classSymbol == null || classSymbol.IsAbstract)
            {
                return null;
            }

            bool isComponent = IsComponentClass(classSymbol);
            bool hasUI = HasUIEditableMembers(classSymbol);

            if (!isComponent && !hasUI)
            {
                return null;
            }

            return (classDeclaration, isComponent, hasUI);
        }

        private static bool IsComponentClass(INamedTypeSymbol classSymbol)
        {
            return (classSymbol.AllInterfaces.Any(i =>
                       i.Name == "IComponent" && i.ContainingAssembly.Name.Contains("RockEngine")) ||
                   classSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == "ComponentAttribute"))
                   && !classSymbol.IsAbstract;
        }

        private static void Execute(Compilation compilation,
            ImmutableArray<(ClassDeclarationSyntax classDecl, bool isComponent, bool hasUI)> classInfos,
            SourceProductionContext context)
        {
            // Collect all own types that need generation
            var ownTypes = new List<(INamedTypeSymbol Symbol, bool IsPartial)>();
            foreach (var (classDecl, _, _) in classInfos)
            {
                var model = compilation.GetSemanticModel(classDecl.SyntaxTree);
                if (model.GetDeclaredSymbol(classDecl) is INamedTypeSymbol classSymbol &&
                    (IsComponentClass(classSymbol) || HasUIEditableMembers(classSymbol)))
                {
                    bool isPartial = classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
                    ownTypes.Add((classSymbol, isPartial));
                }
            }

            // Generate sources for own types
            foreach (var (symbol, isPartial) in ownTypes)
            {
                var source = GenerateUIPropertyAccessors(symbol, compilation, isPartial);
                context.AddSource($"{symbol.Name}_UIProperties.g.cs", source);
            }

            // Collect dependency types (components or UI‑editable classes)
            var dependencyTypes = GetDependencyUIRenderableTypes(compilation).ToList();
            foreach (var symbol in dependencyTypes)
            {
                var source = GenerateUIPropertyAccessors(symbol, compilation, isPartial: false);
                context.AddSource($"{symbol.Name}_DependencyUIProperties.g.cs", source);
            }

            // Build factory for all non‑partial types (own non‑partial + dependencies)
            var factoryTypes = ownTypes
                .Where(t => !t.IsPartial)
                .Select(t => t.Symbol)
                .Concat(dependencyTypes)
                .ToList();

            if (factoryTypes.Count > 0)
            {
                var factorySource = GenerateUIPropertyAccessorProviderFactory(factoryTypes);
                context.AddSource("UIPropertyAccessorProviderFactory.g.cs", factorySource);
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetDependencyUIRenderableTypes(Compilation compilation)
        {
            var sourceAssembly = compilation.Assembly;
            foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                foreach (var type in GetAllTypes(referencedAssembly.GlobalNamespace))
                {
                    if (type is INamedTypeSymbol namedType &&
                        !SymbolEqualityComparer.Default.Equals(namedType.ContainingAssembly, sourceAssembly) &&
                        (IsComponentClass(namedType) || HasUIEditableMembers(namedType)))
                    {
                        yield return namedType;
                    }
                }
            }
        }

        // Recursive type walkers
        private static IEnumerable<ITypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                if (member is INamespaceSymbol nestedNs)
                {
                    foreach (var type in GetAllTypes(nestedNs))
                    {
                        yield return type;
                    }
                }
                else if (member is ITypeSymbol type)
                {
                    yield return type;
                    foreach (var nested in GetNestedTypes(type))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static IEnumerable<ITypeSymbol> GetNestedTypes(ITypeSymbol type)
        {
            foreach (var nested in type.GetTypeMembers())
            {
                yield return nested;
                foreach (var deeper in GetNestedTypes(nested))
                {
                    yield return deeper;
                }
            }
        }

        // ---- Generation of a single type ----
        private static string GenerateUIPropertyAccessors(INamedTypeSymbol classSymbol, Compilation compilation, bool isPartial)
        {
            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
            var className = classSymbol.Name;
            var uiMembers = GetUIMembers(classSymbol).ToList();
            var fullyQualified = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();

            var usingStatements = @"using System;
using System.Collections.Generic;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers;
using RockEngine.Core.Attributes;";

            if (isPartial)
            {
                return $$"""
                // <auto-generated/>
                #nullable enable
                {{usingStatements}}

                namespace {{namespaceName}}
                {
                    {{accessibility}} partial class {{className}} : IUIPropertyAccessorProvider
                    {
                        private static readonly UIPropertyAccessor[] _uiPropertyAccessors = new UIPropertyAccessor[]
                        {
                {{GeneratePropertyAccessors(uiMembers, classSymbol)}}
                        };

                        public static IReadOnlyList<UIPropertyAccessor> GetUIPropertyAccessors() => _uiPropertyAccessors;
                        IReadOnlyList<UIPropertyAccessor> IUIPropertyAccessorProvider.GetUIPropertyAccessors() => _uiPropertyAccessors;
                    }

                    {{accessibility}} struct {{className}}PropertyAccess
                    {
                        private readonly {{className}} _instance ;
                        public {{className}}PropertyAccess({{className}} instance) { _instance  = instance; }
                {{GeneratePropertyAccessMethods(uiMembers, classSymbol)}}
                    }
                }
                """;
            }
            else
            {
                return $$"""
                // <auto-generated/>
                #nullable enable
                {{usingStatements}}

                namespace {{namespaceName}}
                {
                    public static class {{className}}UIProperties
                    {
                        private static readonly UIPropertyAccessor[] _uiPropertyAccessors = new UIPropertyAccessor[]
                        {
                {{GeneratePropertyAccessors(uiMembers, classSymbol)}}
                        };

                        public static IReadOnlyList<UIPropertyAccessor> GetUIPropertyAccessors() => _uiPropertyAccessors;
                    }

                    public readonly struct {{className}}UIPropertyAccessorProvider : IUIPropertyAccessorProvider
                    {
                        private readonly {{fullyQualified}} _instance;
                        public {{className}}UIPropertyAccessorProvider({{fullyQualified}} instance) => _instance = instance;
                        public IReadOnlyList<UIPropertyAccessor> GetUIPropertyAccessors() => {{className}}UIProperties.GetUIPropertyAccessors();
                    }

                    public struct {{className}}PropertyAccess
                    {
                        private readonly {{fullyQualified}} _instance;
                        public {{className}}PropertyAccess({{fullyQualified}} instance) { _instance = instance; }
                {{GeneratePropertyAccessMethods(uiMembers, classSymbol)}}
                    }
                }
                """;
            }
        }

        // ---- Shared accessors generation (works with properties and fields) ----
        private static string GeneratePropertyAccessors(IEnumerable<ISymbol> members, INamedTypeSymbol containingType)
        {
            var accessors = new List<string>();
            var fullContainingType = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            foreach (var member in members)
            {
                if (member is IPropertySymbol prop)
                {
                    accessors.Add(GenerateAccessorForProperty(prop, fullContainingType));
                }
                else if (member is IFieldSymbol field)
                {
                    accessors.Add(GenerateAccessorForField(field, fullContainingType));
                }
            }
            return string.Join(",\n", accessors);
        }

        private static string GenerateAccessorForProperty(IPropertySymbol property, string fullContainingType)
        {
            var uiAttr = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "UIEditableAttribute");
            var displayName = uiAttr?.NamedArguments
                .FirstOrDefault(kvp => kvp.Key == "DisplayName").Value.Value?.ToString() ?? property.Name;

            var propertyTypeForTypeOf = GetFullTypeNameForTypeOf(property.Type);
            var propertyTypeForCast = GetFullTypeName(property.Type);

            bool canWrite = property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public;
            string setter = canWrite
                ? $"(component, value) => (({fullContainingType})component).{property.Name} = ({propertyTypeForCast})value"
                : "null";

            return $$"""
                        new UIPropertyAccessor(
                            name: "{{property.Name}}",
                            displayName: "{{displayName}}",
                            propertyType: typeof({{propertyTypeForTypeOf}}),
                            getValue: (component) => (({{fullContainingType}})component).{{property.Name}},
                            setValue: {{setter}},
                            canWrite: {{canWrite.ToString().ToLowerInvariant()}},
                            attributes: new Attribute[] { {{GetAttributeInitializers(property)}} }
                        )
            """;
        }

        private static string GenerateAccessorForField(IFieldSymbol field, string fullContainingType)
        {
            var uiAttr = field.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "UIEditableAttribute");
            var displayName = uiAttr?.NamedArguments
                .FirstOrDefault(kvp => kvp.Key == "DisplayName").Value.Value?.ToString() ?? field.Name;

            var fieldTypeForTypeOf = GetFullTypeNameForTypeOf(field.Type);
            var fieldTypeForCast = GetFullTypeName(field.Type);
            string setter = $"(component, value) => (({fullContainingType})component).{field.Name} = ({fieldTypeForCast})value";

            return $$"""
                        new UIPropertyAccessor(
                            name: "{{field.Name}}",
                            displayName: "{{displayName}}",
                            propertyType: typeof({{fieldTypeForTypeOf}}),
                            getValue: (component) => (({{fullContainingType}})component).{{field.Name}},
                            setValue: {{setter}},
                            canWrite: true,
                            attributes: new Attribute[] { {{GetAttributeInitializers(field)}} }
                        )
            """;
        }

        // ---- Property/field access methods for the optional struct ----
        private static string GeneratePropertyAccessMethods(IEnumerable<ISymbol> members, INamedTypeSymbol containingType)
        {
            var methods = new List<string>();
            foreach (var member in members)
            {
                string typeName, name;
                bool canWrite = false;

                if (member is IPropertySymbol prop)
                {
                    typeName = GetFullTypeName(prop.Type);
                    name = prop.Name;
                    canWrite = prop.SetMethod != null && prop.SetMethod.DeclaredAccessibility == Accessibility.Public;
                }
                else if (member is IFieldSymbol field)
                {
                    typeName = GetFullTypeName(field.Type);
                    name = field.Name;
                    canWrite = true;
                }
                else
                {
                    continue;
                }

                methods.Add($"        public {typeName} Get{name}() => _instance.{name};");
                if (canWrite)
                {
                    methods.Add($"        public void Set{name}({typeName} value) => _instance.{name} = value;");
                }
            }
            return string.Join("\n", methods);
        }

        // ---- Factory for non‑partial types ----
        private static string GenerateUIPropertyAccessorProviderFactory(IEnumerable<INamedTypeSymbol> types)
        {
            var ifStatements = new StringBuilder();
            foreach (var type in types)
            {
                var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var shortName = type.Name;
                var varName = shortName.Length > 1
                    ? char.ToLowerInvariant(shortName[0]) + shortName.Substring(1)
                    : shortName.ToLowerInvariant();

                ifStatements.AppendLine(
                    $"        if (obj is {fullName} {varName}) return new {type.ContainingNamespace.ToDisplayString()}.{shortName}UIPropertyAccessorProvider({varName});");
            }

            return $$"""
            // <auto-generated/>
            #nullable enable

            using RockEngine.Core.ECS.Components;
            using RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers;

            namespace RockEngine.Editor.Generator
            {
                public static class UIPropertyAccessorProviderFactory
                {
                    public static IUIPropertyAccessorProvider? GetProvider(object obj)
                    {
            {{ifStatements}}
                        return null;
                    }
                }
            }
            """;
        }

        // ---- Attribute helper methods (unchanged) ----
        private static string GetFullTypeNameForTypeOf(ITypeSymbol typeSymbol)
        {
            var nonNullableType = typeSymbol.NullableAnnotation == NullableAnnotation.Annotated
                ? typeSymbol.WithNullableAnnotation(NullableAnnotation.None)
                : typeSymbol;

            if (nonNullableType is INamedTypeSymbol { IsGenericType: true } genericType &&
                genericType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                nonNullableType = genericType.TypeArguments[0];
                return $"{nonNullableType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}?";
            }

            return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static string GetFullTypeName(ITypeSymbol typeSymbol) =>
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string GetAttributeInitializers(ISymbol member)
        {
            var attributes = member.GetAttributes()
                .Where(a => a.AttributeClass?.Name is "RangeAttribute" or "ColorAttribute" or "StepAttribute")
                .Select(attr =>
                {
                    if (attr.AttributeClass?.Name == "RangeAttribute")
                    {
                        var min = GetRangeValue(attr.ConstructorArguments[0]);
                        var max = GetRangeValue(attr.ConstructorArguments[1]);
                        return $"new global::RockEngine.Core.Attributes.RangeAttribute({min}, {max})";
                    }
                    else if (attr.AttributeClass?.Name == "ColorAttribute")
                    {
                        return "new global::RockEngine.Core.Attributes.ColorAttribute()";
                    }
                    else if (attr.AttributeClass?.Name == "StepAttribute")
                    {
                        var step = GetStepValue(attr.ConstructorArguments[0]);
                        return $"new global::RockEngine.Core.Attributes.StepAttribute({step})";
                    }
                    return null;
                })
                .Where(a => a != null);

            return string.Join(", ", attributes);
        }

        private static string GetStepValue(TypedConstant constant)
        {
            if (constant.Value == null)
            {
                return "0.1f";
            }

            if (constant.Type?.SpecialType == SpecialType.System_Single)
            {
                return FormatFloatLiteral((float)constant.Value);
            }

            return $"{constant.Value}f";
        }

        private static string GetRangeValue(TypedConstant constant)
        {
            if (constant.Value == null)
            {
                return "0f";
            }

            if (IsFloatMaxValue(constant))
            {
                return "float.MaxValue";
            }

            if (IsFloatMinValue(constant))
            {
                return "float.MinValue";
            }

            if (constant.Type?.SpecialType == SpecialType.System_Single)
            {
                return FormatFloatLiteral((float)constant.Value);
            }

            return $"{constant.Value}f";
        }

        private static bool IsFloatMaxValue(TypedConstant constant) =>
            constant.Value is float fv && fv == float.MaxValue ||
            constant.Value is double dv && dv >= 3.4028234663852886e38;

        private static bool IsFloatMinValue(TypedConstant constant) =>
            constant.Value is float fv && fv == float.MinValue ||
            constant.Value is double dv && dv <= -3.4028234663852886e38;

        private static string FormatFloatLiteral(float value)
        {
            var str = value.ToString("G9", CultureInfo.InvariantCulture);
            if (!str.EndsWith("f", StringComparison.OrdinalIgnoreCase))
            {
                str += "f";
            }

            return str;
        }

        // ---- UI member detection ----
        private static bool HasUIEditableMembers(INamedTypeSymbol classSymbol)
        {
            return IsComponentClass(classSymbol) || classSymbol.GetMembers().Any(member =>
            {
                if (member is IFieldSymbol field && !field.IsStatic &&
                    field.DeclaredAccessibility == Accessibility.Public &&
                    field.GetAttributes().Any(a => IsUIAttribute(a.AttributeClass)))
                {
                    return true;
                }

                if (member is IPropertySymbol prop && prop.GetMethod != null &&
                    prop.GetMethod.DeclaredAccessibility == Accessibility.Public &&
                    !prop.IsStatic &&
                    prop.GetAttributes().Any(a => IsUIAttribute(a.AttributeClass)))
                {
                    return true;
                }

                return false;
            });
        }

        private static bool IsUIAttribute(INamedTypeSymbol? attrClass) =>
            attrClass?.Name is "UIEditableAttribute" or "RangeAttribute" or "ColorAttribute" or "StepAttribute";

        private static IEnumerable<ISymbol> GetUIMembers(INamedTypeSymbol classSymbol)
        {
            bool isComponent = IsComponentClass(classSymbol);

            foreach (var member in classSymbol.GetMembers())
            {
                if (isComponent)
                {
                    // For components, emit all public, non‑static fields and properties (with getter)
                    if (member is IFieldSymbol field &&
                        !field.IsStatic &&
                        field.DeclaredAccessibility == Accessibility.Public)
                    {
                        yield return field;
                    }
                    else if (member is IPropertySymbol prop &&
                             !prop.IsStatic &&
                             prop.GetMethod != null &&
                             prop.GetMethod.DeclaredAccessibility == Accessibility.Public)
                    {
                        yield return prop;
                    }
                }
                else
                {
                    // For non‑components, only emit members with a UI attribute
                    if (member is IPropertySymbol prop &&
                        prop.GetMethod != null &&
                        prop.GetMethod.DeclaredAccessibility == Accessibility.Public &&
                        !prop.IsStatic &&
                        prop.GetAttributes().Any(a => IsUIAttribute(a.AttributeClass)))
                    {
                        yield return prop;
                    }
                    else if (member is IFieldSymbol field &&
                             !field.IsStatic &&
                             field.DeclaredAccessibility == Accessibility.Public &&
                             field.GetAttributes().Any(a => IsUIAttribute(a.AttributeClass)))
                    {
                        yield return field;
                    }
                }
            }
        }
    }
}