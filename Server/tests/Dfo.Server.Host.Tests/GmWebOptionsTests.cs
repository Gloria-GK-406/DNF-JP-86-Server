using System.Net;
using Dfo.Server.GmWeb;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Host.Tests;

/// <summary>#305 RQ-030/RQ-034: parsing and validation of <c>GmWeb:TrustedProxies</c>.</summary>
public sealed class GmWebOptionsTests
{
    private static GmWebOptions Parse(IEnumerable<KeyValuePair<string, string?>> settings, bool enabled = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GmWeb:Enabled"] = enabled ? "true" : "false",
            ["GmWeb:ListenUrl"] = "http://127.0.0.1:18081",
        }.Concat(settings)).Build();
        return GmWebOptions.FromConfiguration(configuration);
    }

    private static KeyValuePair<string, string?> Entry(string key, string? value) => new(key, value);

    [Fact]
    public void Unconfigured_trusts_no_proxy()
        => Assert.Empty(Parse([]).TrustedProxies);

    [Fact]
    public void Array_and_comma_forms_are_read_and_mapped_addresses_are_normalized()
    {
        var options = Parse([
            Entry("GmWeb:TrustedProxies:0", "192.0.2.1"),
            Entry("GmWeb:TrustedProxies:1", " 2001:db8::7 "),
            Entry("GmWeb:TrustedProxies:2", "::ffff:192.0.2.1"),
            Entry("GmWeb:TrustedProxies:3", "::ffff:127.0.0.1"),
        ]);
        Assert.Equal([IPAddress.Parse("192.0.2.1"), IPAddress.Parse("2001:db8::7"), IPAddress.Loopback], options.TrustedProxies);

        var scalar = Parse([Entry("GmWeb:TrustedProxies", "192.0.2.1, 198.51.100.1")]);
        Assert.Equal([IPAddress.Parse("192.0.2.1"), IPAddress.Parse("198.51.100.1")], scalar.TrustedProxies);
    }

    [Theory]
    [InlineData("proxy.example")]
    [InlineData("10.0.0.0/8")]
    [InlineData("127.0.0.1:80")]
    [InlineData("[::1]:80")]
    [InlineData("[::1]")]
    [InlineData("127.1")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("fe80::1%3")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::ffff:0.0.0.0")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_entry_that_is_not_one_IP_address_fails_naming_the_setting(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse([Entry("GmWeb:TrustedProxies:0", value)]));
        Assert.Contains("GmWeb:TrustedProxies", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_or_blank_setting_value_means_no_proxies(string value)
        => Assert.Empty(Parse([Entry("GmWeb:TrustedProxies", value)]).TrustedProxies);

    [Fact]
    public void An_empty_JSON_array_means_no_proxies()
    {
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """{ "GmWeb": { "Enabled": true, "ListenUrl": "http://127.0.0.1:18081", "TrustedProxies": [] } }"""));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        Assert.Empty(GmWebOptions.FromConfiguration(configuration).TrustedProxies);
    }

    [Fact]
    public void An_empty_entry_in_the_comma_form_fails_naming_the_setting()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse([Entry("GmWeb:TrustedProxies", "192.0.2.1,,198.51.100.1")]));
        Assert.Contains("GmWeb:TrustedProxies", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_back_office_does_not_read_the_setting()
        => Assert.Empty(Parse([Entry("GmWeb:TrustedProxies:0", "proxy.example")], enabled: false).TrustedProxies);

    [Fact]
    public async Task An_invalid_entry_stops_the_host_from_starting()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => GmTestHost.StartAsync(
            configuration: new Dictionary<string, string?> { ["GmWeb:TrustedProxies:0"] = "proxy.example" }));
        Assert.Contains("GmWeb:TrustedProxies", error.Message, StringComparison.Ordinal);
    }
}
