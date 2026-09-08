using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrapMeter.Inventory;

/// <summary>
/// 源码级圈复杂度：<c>C = 1 + 判定点数</c>。
/// 统计范围是成员的整个体，含其中的 lambda 与局部函数——这与覆盖率按行号归并回声明处的
/// 方向一致，两端口径必须相同（见 plan 的 DP-2）。
/// </summary>
public static class CyclomaticComplexity
{
    /// <summary>判定点集合的可读描述，原样写进运行清单，供跨次比较时先比口径。</summary>
    public const string Definition =
        "C = 1 + count of: if | while | do | for | foreach (incl. await foreach) | " +
        "each non-default case label | each switch-expression arm | catch clause | " +
        "when/catch-filter clause | ?: | && | || | ?? | ??= | ?. and ?[] | " +
        "each 'and'/'or' pattern. Counted over the member's whole body including nested " +
        "lambdas and local functions. NOT counted: else, try, finally, goto, LINQ query clauses.";

    public static int Of(SyntaxNode member)
    {
        var walker = new Walker();
        walker.Visit(member);
        return 1 + walker.DecisionPoints;
    }

    private sealed class Walker : CSharpSyntaxWalker
    {
        public int DecisionPoints { get; private set; }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            DecisionPoints++;
            base.VisitIfStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            DecisionPoints++;
            base.VisitWhileStatement(node);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            DecisionPoints++;
            base.VisitDoStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            DecisionPoints++;
            base.VisitForStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            DecisionPoints++;
            base.VisitForEachStatement(node);
        }

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            DecisionPoints++;
            base.VisitForEachVariableStatement(node);
        }

        public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
        {
            DecisionPoints++;
            base.VisitCaseSwitchLabel(node);
        }

        public override void VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
        {
            DecisionPoints++;
            base.VisitCasePatternSwitchLabel(node);
        }

        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
        {
            DecisionPoints++;
            base.VisitSwitchExpressionArm(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            DecisionPoints++;
            base.VisitCatchClause(node);
        }

        public override void VisitCatchFilterClause(CatchFilterClauseSyntax node)
        {
            DecisionPoints++;
            base.VisitCatchFilterClause(node);
        }

        public override void VisitWhenClause(WhenClauseSyntax node)
        {
            DecisionPoints++;
            base.VisitWhenClause(node);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            DecisionPoints++;
            base.VisitConditionalExpression(node);
        }

        public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            DecisionPoints++;
            base.VisitConditionalAccessExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.LogicalAndExpression)
                || node.IsKind(SyntaxKind.LogicalOrExpression)
                || node.IsKind(SyntaxKind.CoalesceExpression))
            {
                DecisionPoints++;
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            {
                DecisionPoints++;
            }

            base.VisitAssignmentExpression(node);
        }

        public override void VisitBinaryPattern(BinaryPatternSyntax node)
        {
            DecisionPoints++;
            base.VisitBinaryPattern(node);
        }
    }
}
