// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyModel;

namespace Microsoft.AspNetCore.Mvc.Analyzers.Infrastructure
{
    public abstract class AnalyzerTestBase : IDisposable
    {
        public Workspace Workspace { get; private set; }

        protected abstract DiagnosticAnalyzer DiagnosticAnalyzer { get; }

        protected virtual CodeFixProvider CodeFixProvider { get; }

        protected Project CreateProject(string source)
        {
            var projectId = ProjectId.CreateNewId(debugName: "TestProject");
            var newFileName = "Test.cs";
            var documentId = DocumentId.CreateNewId(projectId, debugName: newFileName);
            var metadataReferences = DependencyContext.Default.CompileLibraries
                .SelectMany(c => c.ResolveReferencePaths())
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToList();

            Workspace = Workspace ?? new AdhocWorkspace();

            var solution = Workspace
                .CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
                .AddMetadataReferences(projectId, metadataReferences)
                .AddDocument(documentId, newFileName, SourceText.From(source));

            return solution.GetProject(projectId);
        }

        protected async Task<Diagnostic[]> GetDiagnosticAsync(Project project)
        {
            var compilation = await project.GetCompilationAsync();
            var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(DiagnosticAnalyzer));
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
            return diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToArray();
        }

        protected Task<string> ApplyCodeFixAsync(
            Project project,
            Diagnostic[] analyzerDiagnostic,
            int codeFixIndex = 0)
        {
            var diagnostic = analyzerDiagnostic.Single();
            return ApplyCodeFixAsync(project, diagnostic, codeFixIndex);
        }

        protected async Task<string> ApplyCodeFixAsync(
            Project project,
            Diagnostic analyzerDiagnostic,
            int codeFixIndex = 0)
        {
            if (CodeFixProvider == null)
            {
                throw new InvalidOperationException($"{nameof(CodeFixProvider)} has not been assigned.");
            }

            var document = project.Documents.Single();
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, analyzerDiagnostic, (a, d) => actions.Add(a), CancellationToken.None);
            await CodeFixProvider.RegisterCodeFixesAsync(context);

            if (actions.Count == 0)
            {
                throw new InvalidOperationException("CodeFix produced no actions to apply.");
            }

            var updatedSolution = await ApplyFixAsync(actions[codeFixIndex]);
            var updatedProject = updatedSolution.GetProject(project.Id);
            // Todo: figure out why this doesn't work.
            // await EnsureCompilable(updatedProject);

            return await GetStringFromDocumentAsync(updatedProject.Documents.Single());
        }

        private static async Task EnsureCompilable(Project project)
        {
            var compilation = await project
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .GetCompilationAsync();
            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Length != 0)
            {
                var message = string.Join(
                    Environment.NewLine,
                    diagnostics.Select(d => CSharpDiagnosticFormatter.Instance.Format(d)));
                throw new InvalidOperationException($"Compilation failed:{Environment.NewLine}{message}");
            }
        }

        private static async Task<Solution> ApplyFixAsync(CodeAction codeAction)
        {
            var operations = await codeAction.GetOperationsAsync(CancellationToken.None);
            return Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;
        }

        private static async Task<string> GetStringFromDocumentAsync(Document document)
        {
            var simplifiedDoc = await Simplifier.ReduceAsync(document, Simplifier.Annotation);
            var root = await simplifiedDoc.GetSyntaxRootAsync();
            root = Formatter.Format(root, Formatter.Annotation, simplifiedDoc.Project.Solution.Workspace);
            return root.GetText().ToString();
        }

        public void Dispose()
        {
            Workspace?.Dispose();
        }
    }
}
