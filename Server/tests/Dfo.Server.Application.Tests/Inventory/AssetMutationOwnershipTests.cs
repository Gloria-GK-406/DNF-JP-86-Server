using System.Text.RegularExpressions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class AssetMutationOwnershipTests
{
    [Theory]
    [InlineData("TrySetCeraBalancesAsync", "Accounts/AccountWalletDomainService.cs")]
    [InlineData("SetProgressAsync", "Characters/PlayingCharacter.Experience.cs")]
    [InlineData("UpsertExpiryAsync", "Accounts/PremiumServiceDomainService.cs")]
    [InlineData("ChangeCharacterAsync", "Inventory/InventoryLevelDomainService.cs")]
    [InlineData("ChangeAccountAsync", "Inventory/InventoryLevelDomainService.cs")]
    public void Business_mutations_have_one_domain_owner(string method, string owner)
    {
        var root = Repository();
        var references = new List<string>();
        foreach (var project in new[] { "Dfo.Server.Application", "Dfo.Server.Protocol", "Dfo.Server.GmWeb" })
        {
            var directory = Path.Combine(root, "Server", "src", project);
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (!Regex.IsMatch(File.ReadAllText(file), @"\.\s*" + method + @"\s*\(")) continue;
                references.Add(project + "/" + Path.GetRelativePath(directory, file).Replace('\\', '/'));
            }
        }
        Assert.Equal(new[] { "Dfo.Server.Application/" + owner }, references.Order().ToArray());
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("The source ownership test requires the repository checkout.");
    }
}
