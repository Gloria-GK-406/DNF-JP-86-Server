using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.TutorialLevelUp;
namespace Dfo.Server.Application.Tests.Support;
// Direct business algorithm fixture. Decoder/transaction/scope evidence belongs to SkillGrowthProtocolCommandFlowTests.
internal static class TutorialGrantDriver
{
    internal static async ValueTask<TutorialLevelUpResult> Grant(ICharacterDomainService characters,CharacterId id,CancellationToken ct)
    {
        var assembly=typeof(ProtocolModule).Assembly;
        if(assembly.GetType("Dfo.Server.Protocol.Game.TutorialLevelUp.GameTutorialLevelUpCommandHandler") is {} whole)
            return await (ValueTask<TutorialLevelUpResult>)whole.GetMethod("GrantAsync",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[characters,id,ct])!;
        var handler=assembly.GetType("Dfo.Server.Protocol.Game.TutorialLevelUp.GrantTutorialLevelCommandHandler",true)!;
        var command=assembly.GetType("Dfo.Server.Protocol.Game.TutorialLevelUp.GrantTutorialLevelCommand",true)!;
        return await (ValueTask<TutorialLevelUpResult>)handler.GetMethod("HandleAsync")!.Invoke(Activator.CreateInstance(handler,characters),[null,Activator.CreateInstance(command,id),ct])!;
    }
}
