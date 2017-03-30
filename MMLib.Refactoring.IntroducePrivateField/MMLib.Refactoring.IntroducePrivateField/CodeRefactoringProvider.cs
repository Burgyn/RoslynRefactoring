using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System;

namespace MMLib.Refactoring.IntroducePrivateField
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(IntroducePrivateFieldCodeRefactoringProvider)), Shared]
    internal class IntroducePrivateFieldCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);
            var node = root.FindNode(context.Span);
            var parameter = node as ParameterSyntax;
            if (parameter == null)
            {
                return;
            }

            var parameterName = RoslynHelpers.GetParameterName(parameter);
            var underscorePrefix = $"_{parameterName}";
            var uppercase = $"{parameterName.Substring(0, 1).ToUpper()}{parameterName.Substring(1)}";

            if (RoslynHelpers.VariableExists(root, parameterName, underscorePrefix, uppercase))
            {
                return;
            }
            var isReferenceType = await IsReferenceType(context, parameter);

            var action = CodeAction.Create(
                $"Introduce as private field '{underscorePrefix}'",
                ct =>
                CreateFieldAsync(context, parameter, parameterName, underscorePrefix, ct, isReferenceType));

            context.RegisterRefactoring(action);
        }

        private static async Task<bool> IsReferenceType(CodeRefactoringContext context, ParameterSyntax parameter)
        {
            var semantic = await context.Document.GetSemanticModelAsync(context.CancellationToken);
            var referenceType = semantic.GetTypeInfo(parameter.Type).Type.IsReferenceType;

            return referenceType;
        }

        private async Task<Document> CreateFieldAsync(
            CodeRefactoringContext context,
            ParameterSyntax parameter,
            string paramName, string fieldName,
            CancellationToken cancellationToken,
            bool isReferenceType)
        {
            var oldConstructor = parameter
                                 .Ancestors()
                                 .OfType<ConstructorDeclarationSyntax>()
                                 .First();

            ExpressionSyntax expression = CreateAssignmentExpression(paramName, isReferenceType);

            var newConstructor =
                oldConstructor
                .WithBody(
                    oldConstructor.Body.AddStatements(
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(fieldName),
                                expression))));

            var oldClass = parameter.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            var oldClassWithNewCtor = oldClass.ReplaceNode(oldConstructor, newConstructor);

            var fieldDeclaration = RoslynHelpers.CreateFieldDeclaration(RoslynHelpers.GetParameterType(parameter), fieldName);
            var newClass = oldClassWithNewCtor
                .WithMembers(oldClassWithNewCtor.Members.Insert(0, fieldDeclaration))
                .WithAdditionalAnnotations(Formatter.Annotation);

            var oldRoot = await context.Document
                .GetSyntaxRootAsync(cancellationToken)
                .ConfigureAwait(false);
            var newRoot = oldRoot.ReplaceNode(oldClass, newClass);

            return context.Document.WithSyntaxRoot(newRoot);
        }

        private static ExpressionSyntax CreateAssignmentExpression(string paramName, bool isReferenceType)
        {
            var throwStatement = SyntaxFactory.ThrowStatement(
               SyntaxFactory.ParseExpression($" throw new {nameof(ArgumentNullException)}(nameof({paramName}))"));

            ExpressionSyntax expression = null;
            if (isReferenceType)
            {
                expression = SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    SyntaxFactory.IdentifierName(paramName),
                    throwStatement.Expression);
            }
            else
            {
                expression = SyntaxFactory.IdentifierName(paramName);
            }

            return expression;
        }
    }
}