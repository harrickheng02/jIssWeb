using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Controllers;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ArenaReveal")]
public class ArenaAuthorControllerTests
{
    private readonly ArenaRevealIntegrationFixture _fx;

    public ArenaAuthorControllerTests(ArenaRevealIntegrationFixture fx)
    {
        _fx = fx;
    }

    private ArenaAuthorController CreateController()
    {
        var client = _fx.GetClient();
        var options = Microsoft.Extensions.Options.Options.Create(new MongoSettings { DatabaseName = _fx.DatabaseName });
        return new ArenaAuthorController(client, options);
    }

    private async Task InsertPersonaAsync(string id, string state)
    {
        var personas = _fx.GetClient().GetDatabase(_fx.DatabaseName)
            .GetCollection<AgentPersonaRecord>(AgentMongoSetup.PersonasCollectionName);
        await personas.InsertOneAsync(new AgentPersonaRecord
        {
            Id = id,
            AgentUserId = id,
            PersonaId = "p-" + id,
            Nickname = "n-" + id,
            State = state,
            CreatedAtUtc = DateTime.UtcNow,
        });
    }

    private static bool IsAi(ActionResult<ApiResult<AuthorIsAiResponse>> action)
    {
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var body = Assert.IsType<ApiResult<AuthorIsAiResponse>>(ok.Value);
        Assert.True(body.Success);
        return body.Data!.IsAI;
    }

    [Fact]
    public async Task Eliminated_persona_返回_isAI_true()
    {
        await _fx.ClearAsync();
        await InsertPersonaAsync("agent-eliminated", AgentPersonaState.Eliminated);

        var ctrl = CreateController();
        var action = await ctrl.Get("agent-eliminated");

        Assert.True(IsAi(action));
    }

    [Fact]
    public async Task Active_persona_返回_isAI_false_不泄露内部态()
    {
        await _fx.ClearAsync();
        await InsertPersonaAsync("agent-active", AgentPersonaState.Active);

        var ctrl = CreateController();
        var action = await ctrl.Get("agent-active");

        Assert.False(IsAi(action));
    }

    [Fact]
    public async Task 不存在的用户_返回_isAI_false()
    {
        await _fx.ClearAsync();

        var ctrl = CreateController();
        var action = await ctrl.Get("no-such-user");

        Assert.False(IsAi(action));
    }

    [Fact]
    public async Task 空_userId_返回_isAI_false_且不查库()
    {
        await _fx.ClearAsync();

        var ctrl = CreateController();
        var action = await ctrl.Get(null);
        var action2 = await ctrl.Get("   ");

        Assert.False(IsAi(action));
        Assert.False(IsAi(action2));
    }
}
