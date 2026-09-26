using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CrystalContract;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CrystalContractTests
{
    private static ShopFixture Create(bool active = true)
    {
        var f = new ShopFixture();
        f.Sql("INSERT INTO character_condition(character_id,stamina,pc_room_state) VALUES(1,28,0),(2,82,0);");
        if (active) f.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,97,{f.Clock.GetUtcNow().AddDays(1).ToUnixTimeSeconds()});");
        return f;
    }
    private static ValueTask Request(ShopFixture f, byte mode, byte selection) =>
        f.Services.GetRequiredService<CrystalContractHandler>().HandleAsync(new(f.Connection),
            new CrystalContractRequest(GameMessageMetadata.Empty, mode, selection), default);

    [Theory]
    [InlineData(0,0)] [InlineData(0,5)] [InlineData(1,3)] [InlineData(0,255)]
    public async Task Save_commits_before_ACK_and_notification_and_preserves_other_fields(byte mode, byte selection)
    {
        using var f = Create();
        f.Sender.BeforeSend = () => Assert.Equal(selection, f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));
        await Request(f,mode,selection);
        Assert.True(Assert.IsType<CrystalContractResponse>(f.Sender.Messages[0]).Success);
        Assert.Equal("CubeProgressionStateMessage", f.Sender.Messages[1].GetType().Name);
        var row=await f.Services.GetRequiredService<ICharacterConditionTable>().FindAsync(new(1),default);
        Assert.Equal(selection,row!.CrystalCubeGrade); Assert.Equal(mode,row.CrystalCubeType);
        Assert.Equal(28,row.Stamina);
        Assert.Equal(255,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=2"));
    }
    [Theory]
    [InlineData(2,0)] [InlineData(0,6)] [InlineData(0,254)]
    public async Task Unknown_selection_is_refused(byte mode, byte selection)
    {
        using var f=Create();await Request(f,mode,selection);
        Assert.False(Assert.IsType<CrystalContractResponse>(Assert.Single(f.Sender.Messages)).Success);
        Assert.Equal(255,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));
    }
    [Fact]
    public async Task Missing_or_expired_entitlement_is_refused_and_old_selection_is_retained()
    {
        using var f=Create(false);await Request(f,0,1);
        Assert.False(Assert.IsType<CrystalContractResponse>(Assert.Single(f.Sender.Messages)).Success);
        f.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,97,{f.Clock.GetUtcNow().ToUnixTimeSeconds()});");
        await Request(f,0,1);
        Assert.False(Assert.IsType<CrystalContractResponse>(f.Sender.Messages.Last()).Success);
        Assert.Equal(255,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));
    }
    [Fact]
    public async Task Stale_session_does_not_change_previous_character()
    {
        using var f=Create();var old=f.Session;f.Select(2);
        Assert.False(await f.Services.GetRequiredService<ICommandBus>().SendAsync(new SelectCrystalContractCommand(f.OnlineUser,old,0,1)));
        Assert.Equal(255,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));
    }
    [Fact]
    public async Task Failed_persistence_does_not_send_success()
    {
        using var f=Create();f.Sql("CREATE TRIGGER refuse_crystal BEFORE UPDATE ON character_condition BEGIN SELECT RAISE(ABORT,'test'); END;");
        await Assert.ThrowsAsync<SqliteException>(async ()=>await Request(f,0,1));
        Assert.Empty(f.Sender.Messages);
        Assert.Equal(255,f.Scalar("SELECT crystal_cube_grade FROM character_condition WHERE character_id=1"));
    }
}
