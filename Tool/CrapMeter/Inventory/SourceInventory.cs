using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrapMeter.Inventory;

/// <summary>类型内但不属于任何成员的行区间，用来解释归并不掉的序列点。</summary>
public sealed record NonMemberRange(int StartLine, int EndLine, string Kind);

public sealed record FileInventory(
    string Project,
    string RelativePath,
    ImmutableArray<SourceMember> Members,
    ImmutableArray<NonMemberRange> NonMemberRanges);

public sealed record InventoryResult(
    ImmutableArray<FileInventory> Files,
    ImmutableArray<string> Projects)
{
    public IEnumerable<SourceMember> Members => Files.SelectMany(f => f.Members);
}

/// <summary>
/// 独立于覆盖率报告建立生产函数清册（spec RQ-007）：清册的完整性只取决于源码，
/// 不取决于覆盖率工具报了哪些行。
/// </summary>
public static class SourceInventory
{
    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Preview, DocumentationMode.None);

    /// <param name="repositoryRoot">仓库根，用来算规范化相对路径。</param>
    /// <param name="scanRoot">被测源码根，其下第一级目录名即项目名。</param>
    public static InventoryResult Scan(string repositoryRoot, string scanRoot)
    {
        var files = ImmutableArray.CreateBuilder<FileInventory>();
        var projects = new SortedSet<string>(StringComparer.Ordinal);

        string solution = Path.Combine(repositoryRoot, "Server", "86JP.Server.slnx");
        var projectDirectories = System.Xml.Linq.XDocument.Load(solution).Descendants("Project")
            .Select(p => p.Attribute("Path")?.Value.Replace('\\', '/'))
            .Where(p => p?.StartsWith("src/", StringComparison.Ordinal) == true)
            .Select(p => Path.GetDirectoryName(Path.Combine(repositoryRoot, "Server", p!))!)
            .ToArray();
        foreach (string directory in projectDirectories) projects.Add(Path.GetFileName(directory));

        foreach (string path in projectDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(IsProductionSource)
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            string project = ProjectOf(scanRoot, path);
            projects.Add(project);
            files.Add(ScanFile(repositoryRoot, project, path, File.ReadAllText(path)));
        }

        return new InventoryResult(files.ToImmutable(), [.. projects]);
    }

    private static bool IsProductionSource(string path)
    {
        string normalized = Paths.Normalize(path);
        return !normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ProjectOf(string scanRoot, string path)
    {
        string relative = Path.GetRelativePath(scanRoot, path);
        int slash = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return slash < 0 ? relative : relative[..slash];
    }

    public static FileInventory ScanFile(
        string repositoryRoot, string project, string path, string text)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, ParseOptions, path);
        string relativePath = Paths.Relative(repositoryRoot, path);
        var walker = new MemberWalker(tree, project, relativePath);
        walker.Visit(tree.GetRoot());

        return new FileInventory(
            project,
            relativePath,
            walker.Members.ToImmutable(),
            walker.NonMemberRanges.ToImmutable());
    }

    private sealed class MemberWalker(SyntaxTree tree, string project, string relativePath)
        : CSharpSyntaxWalker
    {
        private readonly List<string> _namespaceParts = [];
        private readonly List<string> _typeParts = [];

        public ImmutableArray<SourceMember>.Builder Members { get; } =
            ImmutableArray.CreateBuilder<SourceMember>();

        public ImmutableArray<NonMemberRange>.Builder NonMemberRanges { get; } =
            ImmutableArray.CreateBuilder<NonMemberRange>();

        private string Container => string.Join(
            '.', _namespaceParts.Concat(_typeParts).Where(p => p.Length > 0));

        public override void VisitCompilationUnit(CompilationUnitSyntax node)
        {
            var statements = node.Members.OfType<GlobalStatementSyntax>().ToArray();
            if (statements.Length > 0)
            {
                var span = tree.GetLineSpan(TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End));
                Members.Add(new SourceMember(project, relativePath, "Program", "Program.<Main>", "<Main>(string[])",
                    MemberKind.Method, span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                    1 + statements.Sum(s => CyclomaticComplexity.Of(s) - 1), true));
            }
            base.VisitCompilationUnit(node);
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            _namespaceParts.Add(node.Name.ToString());
            base.VisitNamespaceDeclaration(node);
            _namespaceParts.RemoveAt(_namespaceParts.Count - 1);
        }

        public override void VisitFileScopedNamespaceDeclaration(
            FileScopedNamespaceDeclarationSyntax node)
        {
            _namespaceParts.Add(node.Name.ToString());
            base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) =>
            VisitType(node, base.VisitClassDeclaration);

        public override void VisitStructDeclaration(StructDeclarationSyntax node) =>
            VisitType(node, base.VisitStructDeclaration);

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
            VisitType(node, base.VisitInterfaceDeclaration);

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) =>
            VisitType(node, base.VisitRecordDeclaration);

        private void VisitType<T>(T node, Action<T> visitBase) where T : TypeDeclarationSyntax
        {
            RecordTypeHeader(node);
            _typeParts.Add(TypeName(node));
            visitBase(node);
            _typeParts.RemoveAt(_typeParts.Count - 1);
        }

        private static string TypeName(TypeDeclarationSyntax node) =>
            node.TypeParameterList is { Parameters.Count: > 0 } tp
                ? node.Identifier.ValueText + "<" + new string(',', tp.Parameters.Count - 1) + ">"
                : node.Identifier.ValueText;

        /// <summary>
        /// 类型声明头：主构造函数的参数初始化、record 合成成员（Equals/ToString/…）的序列点
        /// 都落在这里，不属于任何源码成员。记下来是为了让归并不掉的点有解释，而不是被丢掉。
        /// </summary>
        private void RecordTypeHeader(TypeDeclarationSyntax node)
        {
            int end = node.ParameterList?.Span.End
                ?? node.BaseList?.Span.End
                ?? node.Identifier.Span.End;
            AddNonMemberRange(TextSpan.FromBounds(node.Identifier.SpanStart, end), "type-header");
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Declaration.Variables.Any(v => v.Initializer is not null))
            {
                AddNonMemberRange(node.Span, "field-initializer");
            }

            base.VisitFieldDeclaration(node);
        }

        public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            AddNonMemberRange(node.Span, "event-field");
            foreach (var variable in node.Declaration.Variables)
            {
                Add(node, variable.Identifier.ValueText + ".add", "", MemberKind.AutoEvent, false);
                Add(node, variable.Identifier.ValueText + ".remove", "", MemberKind.AutoEvent, false);
            }
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            string name = node.TypeParameterList is { Parameters.Count: > 0 } tp
                ? node.Identifier.ValueText + "<" + new string(',', tp.Parameters.Count - 1) + ">"
                : node.Identifier.ValueText;
            Add(node, ExplicitPrefix(node.ExplicitInterfaceSpecifier) + name,
                Parameters(node.ParameterList), MemberKind.Method,
                HasBody(node.Body, node.ExpressionBody));
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            bool isStatic = node.Modifiers.Any(SyntaxKind.StaticKeyword);
            Add(node, node.Identifier.ValueText, Parameters(node.ParameterList),
                isStatic ? MemberKind.StaticConstructor : MemberKind.Constructor,
                HasBody(node.Body, node.ExpressionBody));
        }

        public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node) =>
            Add(node, "~" + node.Identifier.ValueText, "()", MemberKind.Destructor,
                HasBody(node.Body, node.ExpressionBody));

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node) =>
            Add(node, "operator " + node.OperatorToken.ValueText, Parameters(node.ParameterList),
                MemberKind.Operator, HasBody(node.Body, node.ExpressionBody));

        public override void VisitConversionOperatorDeclaration(
            ConversionOperatorDeclarationSyntax node) =>
            Add(node, "operator " + node.Type, Parameters(node.ParameterList),
                MemberKind.ConversionOperator, HasBody(node.Body, node.ExpressionBody));

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node) =>
            AddPropertyLike(
                node, ExplicitPrefix(node.ExplicitInterfaceSpecifier) + node.Identifier.ValueText,
                string.Empty, node.ExpressionBody, node.AccessorList,
                MemberKind.PropertyAccessor, MemberKind.AutoProperty);

        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node) =>
            AddPropertyLike(
                node, ExplicitPrefix(node.ExplicitInterfaceSpecifier) + "this",
                Parameters(node.ParameterList), node.ExpressionBody, node.AccessorList,
                MemberKind.IndexerAccessor, MemberKind.AutoProperty);

        public override void VisitEventDeclaration(EventDeclarationSyntax node) =>
            AddPropertyLike(
                node, ExplicitPrefix(node.ExplicitInterfaceSpecifier) + node.Identifier.ValueText,
                string.Empty, expressionBody: null, node.AccessorList,
                MemberKind.EventAccessor, MemberKind.AutoEvent);

        private static string ExplicitPrefix(ExplicitInterfaceSpecifierSyntax? specifier) =>
            specifier is null ? string.Empty : specifier.Name.ToString() + ".";

        private void AddPropertyLike(
            MemberDeclarationSyntax node,
            string name,
            string signatureSuffix,
            ArrowExpressionClauseSyntax? expressionBody,
            AccessorListSyntax? accessorList,
            MemberKind accessorKind,
            MemberKind autoKind)
        {
            if (expressionBody is not null)
            {
                Add(node, name + ".get", signatureSuffix, accessorKind, hasBody: true);
                return;
            }

            if (node is PropertyDeclarationSyntax { Initializer: { } initializer })
                AddNonMemberRange(initializer.Span, "property-initializer");
            if (accessorList is not null)
            {
                foreach (AccessorDeclarationSyntax accessor in accessorList.Accessors)
                {
                    bool hasBody = HasBody(accessor.Body, accessor.ExpressionBody);
                    Add(accessor, name + "." + accessor.Keyword.ValueText, signatureSuffix,
                        hasBody ? accessorKind : autoKind, hasBody);
                }
            }
        }

        private static bool HasBody(BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody) =>
            body is not null || expressionBody is not null;

        private static string Parameters(BaseParameterListSyntax? list) =>
            list is null
                ? "()"
                : "(" + string.Join(", ", list.Parameters.Select(p =>
                    (p.Modifiers.Count > 0 ? p.Modifiers.ToString() + " " : "") + (p.Type?.ToString() ?? "?"))) + ")";

        private void Add(
            SyntaxNode node, string name, string signatureSuffix, MemberKind kind, bool hasBody)
        {
            FileLinePositionSpan span = tree.GetLineSpan(node.Span);
            string container = Container;
            Members.Add(new SourceMember(
                Project: project,
                RelativePath: relativePath,
                Container: container,
                QualifiedSymbol: container.Length == 0 ? name : container + "." + name,
                Signature: name + signatureSuffix,
                Kind: kind,
                StartLine: span.StartLinePosition.Line + 1,
                EndLine: span.EndLinePosition.Line + 1,
                Complexity: CyclomaticComplexity.Of(node),
                HasBody: hasBody));
        }

        private void AddNonMemberRange(TextSpan span, string kind)
        {
            FileLinePositionSpan lines = tree.GetLineSpan(span);
            NonMemberRanges.Add(new NonMemberRange(
                lines.StartLinePosition.Line + 1, lines.EndLinePosition.Line + 1, kind));
        }
    }
}
