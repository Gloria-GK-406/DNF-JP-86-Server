using System.Collections.Immutable;
using CrapMeter;
using CrapMeter.Inventory;
using Xunit;

namespace CrapMeter.Tests;

public sealed class SourceInventoryTests
{
    private static FileInventory Scan(string source, string fileName = "Sample.cs") =>
        SourceInventory.ScanFile(
            repositoryRoot: Path.Combine("C:", "repo"),
            project: "Sample.Project",
            path: Path.Combine("C:", "repo", "Server", "src", "Sample.Project", fileName),
            text: source);

    private static SourceMember Member(FileInventory file, string signature) =>
        Assert.Single(file.Members.Where(m => m.Signature == signature));

    [Fact]
    public void A_straight_line_method_has_complexity_one()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        SourceMember member = Member(file, "Add(int, int)");
        Assert.Equal(1, member.Complexity);
        Assert.Equal("N.C.Add", member.QualifiedSymbol);
        Assert.Equal(MemberKind.Method, member.Kind);
        Assert.True(member.HasBody);
    }

    [Fact]
    public void Each_decision_point_adds_one()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                public int Branchy(int a, string? s)
                {
                    if (a > 0 && a < 10) { a++; }          // if + &&        = 2
                    else { a--; }                           // else 不计
                    while (a > 0) { a--; }                  // while          = 1
                    foreach (var c in s ?? "") { a++; }     // foreach + ??   = 2
                    try { a++; }
                    catch (Exception) when (a > 0) { a--; } // catch + filter = 2
                    return s?.Length ?? a;                  // ?. + ??        = 2
                }
            }
            """);

        // 1 + 2 + 1 + 2 + 2 + 2 = 10
        Assert.Equal(10, Member(file, "Branchy(int, string?)").Complexity);
    }

    [Fact]
    public void Switch_counts_cases_but_not_default()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                public int Pick(int a)
                {
                    switch (a)
                    {
                        case 1: return 1;
                        case 2: return 2;
                        case 3: return 3;
                        default: return 0;
                    }
                }
            }
            """);

        Assert.Equal(4, Member(file, "Pick(int)").Complexity);
    }

    [Fact]
    public void Switch_expression_arms_and_pattern_combinators_count()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                public string Name(int a) => a switch
                {
                    1 or 2 => "small",     // arm + or = 2
                    > 3 and < 9 => "mid",  // arm + and = 2
                    _ => "other",          // arm = 1
                };
            }
            """);

        Assert.Equal(6, Member(file, "Name(int)").Complexity);
    }

    // D-1：lambda 与局部函数的判定点计入所在的源码成员——覆盖率也是按行号归并回来的，
    // 两端口径必须相同。
    [Fact]
    public void Lambdas_and_local_functions_count_into_the_declaring_member()
    {
        FileInventory file = Scan("""
            using System.Linq;
            namespace N;
            public class C
            {
                public int Count(int[] items)
                {
                    int Local(int x) => x > 0 ? 1 : 0;      // 局部函数里的 ?: = 1
                    return items.Count(i => i > 0 && i < 9) // lambda 里的 &&  = 1
                        + Local(items.Length);
                }
            }
            """);

        SourceMember member = Member(file, "Count(int[])");
        Assert.Equal(3, member.Complexity);
        Assert.DoesNotContain(file.Members, m => m.Signature.StartsWith("Local", StringComparison.Ordinal));
    }

    [Fact]
    public void Overloads_are_separate_members_with_disjoint_ranges()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                public int Do(int a) => a;
                public int Do(string a) => a.Length;
                public int Do(int a, int b) => a + b;
            }
            """);

        var overloads = file.Members.Where(m => m.QualifiedSymbol == "N.C.Do").ToList();
        Assert.Equal(3, overloads.Count);
        Assert.Equal(
            ["Do(int)", "Do(int, int)", "Do(string)"],
            overloads.Select(m => m.Signature).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(3, overloads.Select(m => m.StartLine).Distinct().Count());
    }

    [Fact]
    public void Partial_class_members_are_attributed_to_their_own_file()
    {
        FileInventory first = Scan("""
            namespace N;
            public partial class C
            {
                public int A() => 1;
            }
            """, "C.Part1.cs");

        FileInventory second = Scan("""
            namespace N;
            public partial class C
            {
                public int B() => 2;
            }
            """, "C.Part2.cs");

        Assert.Equal("N.C.A", Assert.Single(first.Members).QualifiedSymbol);
        Assert.Equal("N.C.B", Assert.Single(second.Members).QualifiedSymbol);
        Assert.EndsWith("C.Part1.cs", first.RelativePath, StringComparison.Ordinal);
        Assert.EndsWith("C.Part2.cs", second.RelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_arity_is_part_of_the_identity()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C<T>
            {
                public U Map<U>(T value) => default!;
            }
            """);

        SourceMember member = Assert.Single(file.Members);
        Assert.Equal("N.C<>.Map<>", member.QualifiedSymbol);
    }

    [Fact]
    public void Explicit_interface_implementations_keep_their_prefix()
    {
        FileInventory file = Scan("""
            using System;
            namespace N;
            public class C : IDisposable
            {
                void IDisposable.Dispose() { }
            }
            """);

        Assert.Equal("N.C.IDisposable.Dispose", Assert.Single(file.Members).QualifiedSymbol);
    }

    [Fact]
    public void All_accessors_have_separate_function_identities()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                public int Auto { get; set; }
                public int Expression => 1;
                public int Explicit
                {
                    get { return 1; }
                    set { _ = value; }
                }
            }
            """);

        Assert.Equal(MemberKind.AutoProperty, Member(file, "Auto.get").Kind);
        Assert.False(Member(file, "Auto.set").HasBody);
        Assert.Equal(MemberKind.PropertyAccessor, Member(file, "Expression.get").Kind);
        Assert.True(Member(file, "Explicit.get").HasBody);
        Assert.True(Member(file, "Explicit.set").HasBody);
    }

    // coverlet 的列信息是退化的（sc=1 ec=2），写成一行的两个访问器按行分不开。
    // 与其让它们互相判成 Ambiguous 两边都丢证据，不如并成一个单位——
    // 复杂度按整份声明算（1 + get 里的 ?: + set 里的 if = 3）。
    [Fact]
    public void Line_breaks_do_not_change_accessor_granularity_or_complexity()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                private int _v;
                public int OneLine { get => _v > 0 ? 1 : 0; set { if (value > 0) { _v = value; } } }
            }
            """);

        Assert.Equal(2, file.Members.Length);
        Assert.Equal(2, Member(file, "OneLine.get").Complexity);
        Assert.Equal(2, Member(file, "OneLine.set").Complexity);
    }

    [Fact]
    public void Declarations_without_a_body_are_recorded_as_bodyless()
    {
        FileInventory file = Scan("""
            namespace N;
            public interface I
            {
                int Value { get; }
                void Go();
            }
            public abstract class C
            {
                public abstract void Go();
            }
            """);

        Assert.All(file.Members, m => Assert.False(m.HasBody));
    }

    // 字段初始化器与 record 头会产生序列点，但它们不属于任何源码成员。
    // 记下这些区间，是为了让归并不掉的点有解释，而不是被静默丢弃。
    [Fact]
    public void Field_initializers_and_type_headers_are_recorded_as_non_member_ranges()
    {
        FileInventory file = Scan("""
            using System.Collections.Generic;
            namespace N;
            public record Point(int X, int Y);
            public class C
            {
                private readonly List<int> _items = new();
                private readonly int _plain;
            }
            """);

        Assert.Contains(file.NonMemberRanges, r => r.Kind == "type-header");
        Assert.Contains(file.NonMemberRanges, r => r.Kind == "field-initializer");
        // 没有初始化器的字段不产生序列点，也就不需要记区间。
        Assert.Single(file.NonMemberRanges.Where(r => r.Kind == "field-initializer"));
    }

    [Fact]
    public void Nested_types_get_their_own_container_path()
    {
        FileInventory file = Scan("""
            namespace N;
            public class Outer
            {
                public class Inner
                {
                    public int Go() => 1;
                }
            }
            """);

        SourceMember member = Assert.Single(file.Members);
        Assert.Equal("N.Outer.Inner", member.Container);
        Assert.Equal("N.Outer.Inner.Go", member.QualifiedSymbol);
    }

    [Fact]
    public void Overlapping_accessors_still_have_distinct_signatures()
    {
        FileInventory file = Scan("""
            namespace N;
            public class C
            {
                public C() { }
                public int A { get; set; }
                public int B { get => 1; set { _ = value; } }
                public int Go() { return 1; }
                ~C() { }
            }
            """);

        Assert.Equal(7, file.Members.Length);
        Assert.Equal(file.Members.Length, file.Members.Select(m => m.Signature).Distinct().Count());
    }

    [Fact]
    public void Top_level_statements_and_event_accessors_are_inventoried()
    {
        FileInventory top = Scan("if (args.Length > 0) await Task.Delay(1);", "Program.cs");
        Assert.Equal(2, Assert.Single(top.Members).Complexity);
        FileInventory events = Scan("class C { public event System.Action E; }");
        Assert.Equal(2, events.Members.Length);
        Assert.Contains(events.Members, m => m.Signature == "E.add");
        Assert.Contains(events.Members, m => m.Signature == "E.remove");
    }

    [Fact]
    public void Ref_parameters_do_not_collide_with_value_parameters()
    {
        var file = Scan("class C { void M(int n) {} void M(ref int n) {} }");
        Assert.Equal(2, file.Members.Select(m => m.Signature).Distinct().Count());
    }
}
