using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System;
using System.Collections.Generic;

namespace MMLib.Refactoring.IntroducePrivateField
{
    [ExportCodeRefactoringProvider(LanguageNames.VisualBasic, Name = nameof(IntroducePrivateFieldCodeRefactoringProvider)), Shared]
    internal class IntroducePrivateFieldCodeRefactoringProviderVB : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);
            var node = root.FindNode(context.Span);
            var parameter = node.Parent as ParameterSyntax;
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

            var action = CodeAction.Create(
                $"Introduce as private field '{underscorePrefix}'",
                ct =>
                CreateFieldAsync(context, parameter, parameterName, underscorePrefix, ct));

            context.RegisterRefactoring(action);
        }

        private async Task<Document> CreateFieldAsync(
            CodeRefactoringContext context,
            ParameterSyntax parameter,
            string paramName, string fieldName,
            CancellationToken cancellationToken)
        {
            var oldConstructor = parameter
                                 .Ancestors()
                                 .OfType<ConstructorBlockSyntax>()
                                 .First();

            ExpressionSyntax expression = CreateAssignmentExpression(paramName);

            var constructorBody = SyntaxFactory.AssignmentStatement(
                                        SyntaxKind.SimpleAssignmentStatement,
                                        SyntaxFactory.IdentifierName(fieldName),
                                        SyntaxFactory.Token(SyntaxKind.EqualsToken),
                                        expression);

            var newConstructor = oldConstructor.AddStatements(constructorBody);

            var oldClass = parameter.FirstAncestorOrSelf<ClassBlockSyntax>();
            var oldClassWithNewCtor = oldClass.ReplaceNode(oldConstructor, newConstructor);

            var fieldDeclaration = CreateFieldDeclaration(RoslynHelpers.GetParameterType(parameter), fieldName);

            fieldDeclaration = fieldDeclaration.WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var members = oldClassWithNewCtor.Members.Insert(0, fieldDeclaration);

            var newClass = oldClassWithNewCtor
                .WithMembers(members)
                .WithAdditionalAnnotations(Formatter.Annotation);
            
            var oldRoot = await context.Document
                .GetSyntaxRootAsync(cancellationToken)
                .ConfigureAwait(false);
            var newRoot = oldRoot.ReplaceNode(oldClass, newClass);

            return context.Document.WithSyntaxRoot(newRoot);
        }

        private static ExpressionSyntax CreateAssignmentExpression(string paramName)
        {
            return SyntaxFactory.IdentifierName(paramName);
        }

        public static FieldDeclarationSyntax CreateFieldDeclaration(string type, string name)
        {
            var par = SyntaxFactory.ModifiedIdentifier(name);
            var parameters = SyntaxFactory.SeparatedList(new List<ModifiedIdentifierSyntax>() { par });

            return SyntaxFactory
                .FieldDeclaration(
                    SyntaxFactory.VariableDeclarator(
                        parameters,
                        SyntaxFactory.SimpleAsClause(SyntaxFactory.ParseTypeName(type)), null).WithAdditionalAnnotations(Formatter.Annotation))
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));
        }
    }
}