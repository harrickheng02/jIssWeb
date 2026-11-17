using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一：LLM 判定结果解析。任何异常形态（非 JSON / 缺字段 / verdict 越界）
/// 都必须回落 ok，保证「判定不可靠时绝不影响发布」。
/// </summary>
public class LlmContentModeratorTests
{
    [Fact]
    public void Parses_valid_json()
    {
        var v = LlmContentModerator.Parse("""{"verdict":"spam","confidence":0.95,"reason":"含推广链接"}""");

        Assert.Equal(ForumModerationVerdicts.Spam, v.Verdict);
        Assert.Equal(0.95, v.Confidence, 3);
        Assert.Equal("含推广链接", v.Reason);
    }

    [Fact]
    public void Tolerates_markdown_fence_and_extra_prose()
    {
        var v = LlmContentModerator.Parse("判定如下：\n```json\n{\"verdict\":\"sexual\",\"confidence\":0.91}\n```\n以上。");

        Assert.Equal(ForumModerationVerdicts.Sexual, v.Verdict);
        Assert.Equal(0.91, v.Confidence, 3);
        Assert.Null(v.Reason);
    }

    [Fact]
    public void Unknown_verdict_falls_back_to_ok()
    {
        var v = LlmContentModerator.Parse("""{"verdict":"boring","confidence":0.99}""");

        Assert.Equal(ForumModerationVerdicts.Ok, v.Verdict);
        Assert.Equal(0d, v.Confidence, 3);
    }

    [Fact]
    public void Missing_confidence_falls_back_to_ok()
    {
        var v = LlmContentModerator.Parse("""{"verdict":"spam"}""");

        Assert.Equal(ForumModerationVerdicts.Ok, v.Verdict);
    }

    [Fact]
    public void Confidence_is_clamped_to_unit_interval()
    {
        var high = LlmContentModerator.Parse("""{"verdict":"spam","confidence":7}""");
        var low = LlmContentModerator.Parse("""{"verdict":"spam","confidence":-3}""");

        Assert.Equal(1d, high.Confidence, 3);
        Assert.Equal(0d, low.Confidence, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("抱歉，我无法判断")]
    [InlineData("{not json}")]
    [InlineData("[1,2,3]")]
    public void Non_json_or_empty_falls_back_to_ok(string? raw)
    {
        var v = LlmContentModerator.Parse(raw);

        Assert.Equal(ForumModerationVerdicts.Ok, v.Verdict);
        Assert.Equal(0d, v.Confidence, 3);
    }

    [Fact]
    public void Ok_verdict_keeps_confidence_zero()
    {
        var v = LlmContentModerator.Parse("""{"verdict":"ok","confidence":0.3}""");

        Assert.Equal(ForumModerationVerdicts.Ok, v.Verdict);
        Assert.Equal(0d, v.Confidence, 3);
    }
}
