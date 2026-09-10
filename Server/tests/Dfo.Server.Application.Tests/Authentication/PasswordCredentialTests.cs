using System.Buffers.Binary;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;

namespace Dfo.Server.Application.Tests.Authentication;

public sealed class PasswordCredentialTests
{
    [Fact]
    public void ModernRecordsVerifyHaveIndependentSaltsAndUpgradeTheirWorkFactor()
    {
        var cheap = new PasswordCredentialService(100);
        var first = cheap.Hash("same password");
        var second = cheap.Hash("same password");
        Assert.StartsWith(PasswordCredentialService.Scheme, first);
        Assert.NotEqual(first, second);
        Assert.Equal(PasswordCheck.Verified, cheap.Verify(first, "same password"));
        Assert.Equal(PasswordCheck.Failed, cheap.Verify(first, "wrong password"));
        Assert.Equal(PasswordCheck.RehashNeeded, new PasswordCredentialService(200).Verify(first, "same password"));
        var production = new PasswordCredentialService().Hash("same password");
        var payload = Convert.FromBase64String(production[PasswordCredentialService.Scheme.Length..]);
        Assert.Equal(1, payload[0]);
        Assert.Equal(2U, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(1, 4)));
        Assert.Equal(220000U, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(5, 4)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("098f6bcd4621d373cade4e832627b4f6")]
    [InlineData("future:anything")]
    [InlineData("dfo-identity-v3:garbage")]
    public void MalformedAndUnknownRecordsAreDeniedWithoutEcho(string record)
    {
        Assert.Equal(PasswordCheck.Failed, new PasswordCredentialService(100).Verify(record, "sentinel"));
    }
}
