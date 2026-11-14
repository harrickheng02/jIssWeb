using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JIssWeb.Common;
using JIssWeb.Report.Api.Controllers;
using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// 图灵名人堂控制器测试（#37，T12 / B4.1）。
/// 核心验收：端点在「匿名」下可访问（控制器上无 Authorize / RequireForumModerator），
/// 且 Profile 对非奖章 / 幸存者返回 404（不泄露存在性）。
/// </summary>
public class HallOfTuringControllerTests
{
    [Fact]
    public void 控制器类_无Authorize_无RequireForumModerator_匿名可访问()
    {
        var attrs = typeof(ArenaHallOfTuringController).GetCustomAttributes(false);
        Assert.DoesNotContain(attrs, a => a is AuthorizeAttribute);
        Assert.DoesNotContain(attrs, a => a.GetType().Name == "RequireForumModeratorAttribute");
        // 仍应带有 ApiController 与路由前缀，保证契约稳定。
        Assert.Contains(attrs, a => a is ApiControllerAttribute);
    }

    [Fact]
    public async Task Hall_返回Ok_且携带视图模型()
    {
        var vm = new HallOfTuringViewModel { Items = new List<HallCard>(), ActiveSurvivorCount = 1 };
        var svc = new Mock<IHallOfTuringService>(MockBehavior.Strict);
        svc.Setup(x => x.HallAsync(It.IsAny<CancellationToken>())).ReturnsAsync(vm);
        var ctrl = new ArenaHallOfTuringController(svc.Object);

        var result = await ctrl.Hall(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(vm, ok.Value);
        svc.VerifyAll();
    }

    [Fact]
    public async Task Profile_奖章账号_返回Ok()
    {
        var vm = new ArenaProfileViewModel { PersonaId = "per1" };
        var svc = new Mock<IHallOfTuringService>(MockBehavior.Strict);
        svc.Setup(x => x.ProfileAsync("per1", It.IsAny<CancellationToken>())).ReturnsAsync(vm);
        var ctrl = new ArenaHallOfTuringController(svc.Object);

        var result = await ctrl.Profile("per1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(vm, ok.Value);
        svc.VerifyAll();
    }

    [Fact]
    public async Task Profile_非奖章或幸存者_返回404_不泄露存在性()
    {
        var svc = new Mock<IHallOfTuringService>(MockBehavior.Strict);
        svc.Setup(x => x.ProfileAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((ArenaProfileViewModel?)null);
        var ctrl = new ArenaHallOfTuringController(svc.Object);

        var result = await ctrl.Profile("ghost", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFound.Value);
        svc.VerifyAll();
    }

    [Fact]
    public async Task AiqiTrend_返回Ok_且携带序列()
    {
        var trend = new List<AiqiTrendPoint> { new() { WeekKey = "2026-11-10", Aiqi = 0.82, ActiveUsers = 150 } };
        var svc = new Mock<IHallOfTuringService>(MockBehavior.Strict);
        svc.Setup(x => x.AiqiTrendAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(trend);
        var ctrl = new ArenaHallOfTuringController(svc.Object);

        var result = await ctrl.AiqiTrend(12, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(trend, ok.Value);
        svc.VerifyAll();
    }

    [Fact]
    public async Task AiqiTrend_显式limitWeeks_透传至服务()
    {
        var trend = new List<AiqiTrendPoint>();
        var svc = new Mock<IHallOfTuringService>(MockBehavior.Strict);
        svc.Setup(x => x.AiqiTrendAsync(8, It.IsAny<CancellationToken>())).ReturnsAsync(trend);
        var ctrl = new ArenaHallOfTuringController(svc.Object);

        await ctrl.AiqiTrend(8, CancellationToken.None);

        svc.VerifyAll();
    }
}
