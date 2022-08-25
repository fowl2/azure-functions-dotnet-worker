﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Azure.Functions.Worker.Sdk.Generators
{
    [Generator]
    public class ExtensionStartupRunnerGeneratorV2 : IIncrementalGenerator
    {
        /// <summary>
        /// The attribute which extension authors will apply on an assembly which contains their startup type.
        /// </summary>
        private const string AttributeTypeName = "WorkerExtensionStartupAttribute";

        /// <summary>
        /// Fully qualified name of the above "WorkerExtensionStartupAttribute" attribute.
        /// </summary>
        private const string AttributeTypeFullName =
            "Microsoft.Azure.Functions.Worker.Core.WorkerExtensionStartupAttribute";

        /// <summary>
        /// Fully qualified name of the base type which extension startup classes should implement.
        /// </summary>
        private const string StartupBaseClassName = "Microsoft.Azure.Functions.Worker.Core.WorkerExtensionStartup";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //var sd = context.CompilationProvider.SelectMany

            var assemblies = context.CompilationProvider
    .SelectMany((compilation, token) =>
        compilation.SourceModule.ReferencedAssemblySymbols.Where(IsAssemblyWithStarupAttribute));

            var source = assemblies.Collect();

            //context.RegisterSourceOutput(source, Execute3);
            context.RegisterSourceOutput(source, (c, d) => RunValidationsAndProduceCode(c, d));

        }

        private bool IsAssemblyWithStarupAttribute(IAssemblySymbol arg)
        {
            var extensionStartupAttribute = GetStartupAttributeData(arg);

            return extensionStartupAttribute != null;
        }

        private AttributeData? GetStartupAttributeData(IAssemblySymbol arg)
        {
            var extensionStartupAttribute = arg.GetAttributes()
                .FirstOrDefault(a =>
                        (a.AttributeClass?.Name.Equals(AttributeTypeName,
                            StringComparison.Ordinal) ?? false) &&
                                                                //Call GetFullName only if class name matches.
                                                                 a.AttributeClass.GetFullName()
                                                                  .Equals(AttributeTypeFullName,StringComparison.Ordinal)
                );

            return extensionStartupAttribute;
        }

        private void RunValidationsAndProduceCode(SourceProductionContext context, ImmutableArray<IAssemblySymbol> assemblySymbols)
        {
            var extensionStartupTypeNames = GetExtensionStartupTypes(context, assemblySymbols);
            if (!extensionStartupTypeNames.Any())
            {
                return;
            }

            SourceText sourceText;
            using (var stringWriter = new StringWriter())
            using (var indentedTextWriter = new IndentedTextWriter(stringWriter))
            {
                indentedTextWriter.WriteLine("// <auto-generated/>");
                indentedTextWriter.WriteLine("using System;");
                indentedTextWriter.WriteLine("using Microsoft.Azure.Functions.Worker.Core;");
                WriteAssemblyAttribute(indentedTextWriter);
                indentedTextWriter.WriteLine("namespace Microsoft.Azure.Functions.Worker");
                indentedTextWriter.WriteLine("{");
                indentedTextWriter.Indent++;
                WriteStartupCodeExecutorClass(indentedTextWriter, extensionStartupTypeNames);
                indentedTextWriter.Indent--;
                indentedTextWriter.WriteLine("}");

                indentedTextWriter.Flush();
                sourceText = SourceText.From(stringWriter.ToString(), encoding: Encoding.UTF8);
            }

            // Add the source code to the compilation
            context.AddSource($"WorkerExtensionStartupCodeExecutor.g.cs", sourceText);
        }

        private IEnumerable<string> GetExtensionStartupTypes(SourceProductionContext context, ImmutableArray<IAssemblySymbol> assemblySymbols)
        {
            List<string>? typeNameList = null;
            foreach (var assemblySymbol in assemblySymbols)
            {
                var extensionStartupAttribute = GetStartupAttributeData(assemblySymbol);
                if (extensionStartupAttribute != null)
                {
                    // WorkerExtensionStartupAttribute has a constructor with one param, the type of startup implementation class.
                    var firstConstructorParam = extensionStartupAttribute.ConstructorArguments[0];
                    if (firstConstructorParam.Value is not ITypeSymbol typeSymbol)
                    {
                        continue;
                    }

                    var fullTypeName = typeSymbol.ToDisplayString();
                    var hasAnyError = ReportDiagnosticErrorsIfAny(context, typeSymbol);

                    if (!hasAnyError)
                    {
                        typeNameList ??= new List<string>();
                        typeNameList.Add(fullTypeName);
                    }
                }
            }

            return typeNameList ?? Enumerable.Empty<string>();
        }

        private static bool ReportDiagnosticErrorsIfAny(SourceProductionContext context, ITypeSymbol typeSymbol)
        {
            var hasAnyError = false;

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
            {
                // Check public parameterless constructor exist for the type.
                var constructorExist = namedTypeSymbol.InstanceConstructors
                    .Any(c => c.Parameters.Length == 0 &&
                              c.DeclaredAccessibility == Accessibility.Public);
                if (!constructorExist)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ConstructorMissing, Location.None,
                        typeSymbol.ToDisplayString()));
                    hasAnyError = true;
                }

                // Check the extension startup class implements WorkerExtensionStartup abstract class.
                if (!namedTypeSymbol.BaseType!.GetFullName().Equals(StartupBaseClassName, StringComparison.Ordinal))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.IncorrectBaseType, Location.None,
                        typeSymbol.ToDisplayString(), StartupBaseClassName));
                    hasAnyError = true;
                }
            }

            return hasAnyError;
        }

        /// <summary>
        /// Writes an assembly attribute with type information about our auto generated WorkerExtensionStartupCodeExecutor class.
        /// </summary>
        private static void WriteAssemblyAttribute(IndentedTextWriter textWriter)
        {
            textWriter.WriteLine(
                "[assembly: WorkerExtensionStartupCodeExecutorInfo(typeof(Microsoft.Azure.Functions.Worker.WorkerExtensionStartupCodeExecutor))]");
        }
        private static void WriteStartupCodeExecutorClass(IndentedTextWriter textWriter, IEnumerable<string> startupTypeNames)
        {
            textWriter.WriteLine("internal class WorkerExtensionStartupCodeExecutor : WorkerExtensionStartup");
            textWriter.WriteLine("{");
            textWriter.Indent++;
            textWriter.WriteLine("public override void Configure(IFunctionsWorkerApplicationBuilder applicationBuilder)");
            textWriter.WriteLine("{");
            textWriter.Indent++;

            foreach (var typeName in startupTypeNames)
            {
                textWriter.WriteLine("try");
                textWriter.WriteLine("{");
                textWriter.Indent++;

                textWriter.WriteLine($"new {typeName}().Configure(applicationBuilder);");

                textWriter.Indent--;
                textWriter.WriteLine("}");
                textWriter.WriteLine("catch (Exception ex)");
                textWriter.WriteLine("{");
                textWriter.Indent++;
                textWriter.WriteLine($"Console.Error.WriteLine(\"Error calling Configure on {typeName} instance.\"+ex.ToString());");
                textWriter.Indent--;
                textWriter.WriteLine("}");
            }

            textWriter.Indent--;
            textWriter.WriteLine("}");
            textWriter.Indent--;
            textWriter.WriteLine("}");
        }
    }
}
