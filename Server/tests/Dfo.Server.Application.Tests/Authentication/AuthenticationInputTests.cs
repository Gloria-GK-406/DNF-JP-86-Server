using Dfo.Server.Application.Authentication;

namespace Dfo.Server.Application.Tests.Authentication;

public sealed class AuthenticationInputTests
{
    public static IEnumerable<object[]> InvalidInputs()
    {
        foreach (var name in new[] { "", "a?b", " a", "a ", "a\tb", "a\u007fb", "玩家", new string('a', 257) })
            yield return [name, "password", true, "loginId"];
        foreach (var password in new[] { "", "1234567", new string('p', 129), "abc\0defgh" })
            yield return ["player", password, true, "password"];
        yield return ["player", "", false, "password"];
        yield return ["player", new string('p', 129), false, "password"];
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void InvalidFieldsAreReportedWithoutTheirValues(string name, string password, bool registration, string field)
    {
        var errors = AuthenticationInputRules.Validate(name, password, registration);
        Assert.True(errors.ContainsKey(field));
        Assert.NotEmpty(errors[field]);
    }

    [Theory]
    [InlineData("a", "12345678", true)]
    [InlineData("a b\"&", "😀😀😀😀😀😀😀😀", true)]
    [InlineData("a", "1", false)]
    public void AcceptedInputsFollowTheirContract(string name, string password, bool registration) =>
        Assert.Empty(AuthenticationInputRules.Validate(name, password, registration));

    [Fact]
    public void LimitsCountUnicodeScalarsInsteadOfUtf16CodeUnits() =>
        Assert.Empty(AuthenticationInputRules.Validate(new string('a', 256), string.Concat(Enumerable.Repeat("😀", 128)), true));

    [Fact]
    public void UnpairedSurrogatesAreNotUnicodeScalars() =>
        Assert.True(AuthenticationInputRules.Validate("player", "abcdefg" + (char)0xd800, true).ContainsKey("password"));
}
