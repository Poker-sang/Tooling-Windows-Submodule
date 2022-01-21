// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Labs.Core.Attributes;
using Microsoft.CodeAnalysis;

namespace CommunityToolkit.Labs.Core;

/// <summary>
/// Crawls all referenced projects for <see cref="ToolkitSampleAttribute"/>s and generates a static method that returns metadata for each one found.
/// </summary>
[Generator]
public partial class ToolkitSampleMetadataGenerator : ISourceGenerator
{
    /// <inheritdoc />
    public void Initialize(GeneratorInitializationContext context)
    {
        // not needed
    }

    /// <inheritdoc />
    public void Execute(GeneratorExecutionContext context)
    {
        // Find all types in all assemblies.
        var assemblies = context.Compilation.SourceModule.ReferencedAssemblySymbols;

        var types = assemblies.SelectMany(asm => CrawlForAllNamedTypes(asm.GlobalNamespace))
                              .Where(x => x is not null && x.TypeKind == TypeKind.Class && x.CanBeReferencedByName) // remove null and invalid values.
                              .Cast<INamedTypeSymbol>(); // strip nullability from type.

        if (types is null)
            return;

        // Get all attributes + the original type symbol.
        var allAttributeData = types.SelectMany(type => type.GetAttributes(), (Type, Attribute) => (Type, Attribute));

        // Find and reconstruct relevant attributes.
        var toolkitSampleAttributeData = allAttributeData
            .Where(x => IsToolkitSampleAttribute(x.Attribute))
            .Select(x => (Attribute: ReconstructAttribute<ToolkitSampleAttribute>(x.Attribute), AttachedQualifiedTypeName: x.Type.ToString()));

        var optionsPaneAttributes = allAttributeData
            .Where(x => IsToolkitSampleOptionsPaneAttribute(x.Attribute))
            .Select(x => (Attribute: ReconstructAttribute<ToolkitSampleOptionsPaneAttribute>(x.Attribute), AttachedQualifiedTypeName: x.Type.ToString()));

        // Reconstruct sample metadata from attributes
        var sampleMetadata = toolkitSampleAttributeData.Select(sample =>
            new ToolkitSampleRecord(
                sample.Attribute.Category,
                sample.Attribute.Subcategory,
                sample.Attribute.DisplayName,
                sample.Attribute.Description,
                sample.AttachedQualifiedTypeName,
                optionsPaneAttributes.FirstOrDefault(opt => opt.Attribute.SampleId == sample.Attribute.Id).AttachedQualifiedTypeName)
        );

        // Build source string
        var source = BuildRegistrationCallsFromMetadata(sampleMetadata);
        context.AddSource($"ToolkitSampleRegistry.g.cs", source);
    }

    static private string BuildRegistrationCallsFromMetadata(IEnumerable<ToolkitSampleRecord> sampleMetadata)
    {
        return $@"// <auto-generated/>
namespace CommunityToolkit.Labs.Core;

internal static class ToolkitSampleRegistry
{{
    public static System.Collections.Generic.IEnumerable<{nameof(ToolkitSampleMetadata)}> Execute()
    {{
        {
        string.Join("\n        ", sampleMetadata.Select(MetadataToRegistryCall).ToArray())
    }
    }}
}}";

        static string MetadataToRegistryCall(ToolkitSampleRecord metadata)
        {
            var sampleOptionsParam = metadata.SampleOptionsAssemblyQualifiedName is null ? "null" : $"typeof({metadata.SampleOptionsAssemblyQualifiedName})";

            return @$"yield return new {nameof(ToolkitSampleMetadata)}({nameof(ToolkitSampleCategory)}.{metadata.Category}, {nameof(ToolkitSampleSubcategory)}.{metadata.Subcategory}, ""{metadata.DisplayName}"", ""{metadata.Description}"", typeof({metadata.SampleAssemblyQualifiedName}), {sampleOptionsParam});";
        }
    }

    private static IEnumerable<INamedTypeSymbol> CrawlForAllNamedTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                foreach (var item in CrawlForAllNamedTypes(nestedNamespace))
                    yield return item;
            }

            if (member is INamedTypeSymbol typeSymbol)
                yield return typeSymbol;
        }
    }

    private static bool IsToolkitSampleAttribute(AttributeData attr)
        => attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"global::{typeof(ToolkitSampleAttribute).FullName}";

    private static bool IsToolkitSampleOptionsPaneAttribute(AttributeData attr)
        => attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"global::{typeof(ToolkitSampleOptionsPaneAttribute).FullName}";

    private static T ReconstructAttribute<T>(AttributeData attributeData)
    {
        // Fully reconstructing the attribute as it was received
        // gives us safety against changes to the attribute constructor signature.
        var attributeArgs = attributeData.ConstructorArguments.Select(PrepareTypeForActivator).ToArray();
        return (T)Activator.CreateInstance(typeof(T), attributeArgs);
    }

    private static object? PrepareTypeForActivator(TypedConstant typedConstant)
    {
        if (typedConstant.Type is null)
            throw new ArgumentNullException(nameof(typedConstant.Type));

        // Types prefixed with global:: do not work with Type.GetType and must be stripped away.
        var assemblyQualifiedName = typedConstant.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");

        var argType = Type.GetType(assemblyQualifiedName);

        // Enums arrive as the underlying integer type, which doesn't work as a param for Activator.CreateInstance()
        if (argType != null && typedConstant.Kind == TypedConstantKind.Enum)
            return Enum.Parse(argType, typedConstant.Value?.ToString());

        return typedConstant.Value;
    }

    /// <remarks>
    /// A new record must be used instead of using <see cref="ToolkitSampleMetadata"/> directly
    /// because we cannot <c>Type.GetType</c> using the <paramref name="SampleAssemblyQualifiedName"/>,
    /// but we can safely generate a type reference in the final output using <c>typeof(AssemblyQualifiedName)</c>.
    /// </remarks>
    private sealed record ToolkitSampleRecord(
        ToolkitSampleCategory Category,
        ToolkitSampleSubcategory Subcategory,
        string DisplayName,
        string Description,
        string SampleAssemblyQualifiedName,
        string? SampleOptionsAssemblyQualifiedName);
}
