using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;

namespace JIssWeb.Model.Api.Tests;

public sealed class AgentSchedulerServiceTests
{
    [Fact]
    public void Prompt_builder_includes_custom_prompt_and_latest_content_with_limit()
    {
        var persona = new AgentPersonaRecord
        {
            Nickname = "Ada",
            Prompt = "冷静、爱聊数学，说话短。",
        };
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions { RecentMemoryLimit = 1 })
            .BuildPostPrompt(persona, new[] { "较新内容", "较旧内容" }, "general");

        Assert.Contains("Ada", prompt);
        Assert.Contains("冷静、爱聊数学", prompt);
        Assert.DoesNotContain("性格：", prompt);
        Assert.DoesNotContain("兴趣：", prompt);
        Assert.Contains("较新内容", prompt);
        Assert.DoesNotContain("较旧内容", prompt);
        Assert.Contains("不要写", prompt);
    }

    [Fact]
    public void Sanitize_strips_title_body_labels()
    {
        var cleaned = AgentPromptBuilder.SanitizeGeneratedText("标题：你好\n正文：这是回复内容");
        Assert.Equal("你好\n这是回复内容", cleaned);

        var replyPrompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildReplyPrompt(
                new AgentPersonaRecord { Nickname = "Ada" },
                Array.Empty<string>(),
                new ForumPostRecord { Title = "原帖标题", Body = "原帖正文" });
        Assert.Contains("原帖标题", replyPrompt);
        Assert.Contains("原帖正文", replyPrompt);
        Assert.Contains("禁止出现", replyPrompt);
        Assert.DoesNotContain("标题：原帖标题", replyPrompt);
        Assert.DoesNotContain("正文：原帖正文", replyPrompt);
    }

    [Fact]
    public void Reply_target_prefers_own_post_with_unanswered_human_reply_over_hotter_other_post()
    {
        var now = DateTime.UtcNow;
        var own = new ForumPostRecord
        {
            Id = "own-post",
            AuthorSubId = "agent-a",
            Title = "我的帖",
            Body = "自述",
            State = "published",
            CreatedAtUtc = now.AddHours(-2),
            CommentCount = 1,
        };
        var otherHot = new ForumPostRecord
        {
            Id = "other-hot",
            AuthorSubId = "human-b",
            Title = "别人热帖",
            Body = "热门",
            State = "published",
            CreatedAtUtc = now.AddHours(-1),
            CommentCount = 50,
            LikeCount = 50,
        };
        var humanOnOwn = new ForumReplyRecord
        {
            Id = "r1",
            PostId = "own-post",
            AuthorSubId = "human-a",
            Body = "传送门2好玩吗？",
            State = "published",
            CreatedAtUtc = now.AddMinutes(-5),
        };

        var target = new AgentHotPostSelector(new AgentSchedulerOptions { HotPostWindowDays = 7 })
            .SelectReplyTarget(
                new[] { own, otherHot },
                new[] { humanOnOwn },
                "agent-a",
                now);

        Assert.NotNull(target);
        Assert.Equal("own-post", target!.Post.Id);
        Assert.Equal("r1", target.FocusReply!.Id);
        Assert.True(target.IsOwnPost);
    }

    [Fact]
    public void Reply_target_skips_own_post_when_agent_already_answered_after_human()
    {
        var now = DateTime.UtcNow;
        var own = new ForumPostRecord
        {
            Id = "own-post",
            AuthorSubId = "agent-a",
            State = "published",
            CreatedAtUtc = now.AddHours(-2),
        };
        var other = new ForumPostRecord
        {
            Id = "other",
            AuthorSubId = "human-b",
            State = "published",
            CreatedAtUtc = now.AddHours(-1),
            CommentCount = 3,
        };
        var replies = new[]
        {
            new ForumReplyRecord
            {
                Id = "h1", PostId = "own-post", AuthorSubId = "human-a", Body = "问一句",
                State = "published", CreatedAtUtc = now.AddMinutes(-20),
            },
            new ForumReplyRecord
            {
                Id = "a1", PostId = "own-post", AuthorSubId = "agent-a", Body = "已答",
                State = "published", CreatedAtUtc = now.AddMinutes(-10),
            },
        };

        var target = new AgentHotPostSelector(new AgentSchedulerOptions { HotPostWindowDays = 7 })
            .SelectReplyTarget(new[] { own, other }, replies, "agent-a", now);

        Assert.NotNull(target);
        Assert.Equal("other", target!.Post.Id);
        Assert.False(target.IsOwnPost);
    }

    [Fact]
    public void Reply_prompt_includes_focus_reply_and_requires_on_topic()
    {
        var prompt = new AgentPromptBuilder(new AgentSchedulerOptions())
            .BuildReplyPrompt(
                new AgentPersonaRecord { Nickname = "Ada" },
                Array.Empty<string>(),
                new ForumPostRecord { Title = "原帖", Body = "正文内容" },
                focusReply: new ForumReplyRecord { Body = "传送门2还值得玩吗？" },
                recentThread: new[]
                {
                    new ForumReplyRecord { AuthorSubId = "human-a", Body = "传送门2还值得玩吗？" },
                },
                isOwnPost: true);

        Assert.Contains("传送门2还值得玩吗？", prompt);
        Assert.Contains("紧扣", prompt);
        Assert.Contains("你的帖", prompt);
        Assert.Contains("答非所问", prompt);
    }

    [Fact]
    public void Hot_post_selector_uses_comment_and_like_weight()
    {
        var now = DateTime.UtcNow;
        var selected = new AgentHotPostSelector(new AgentSchedulerOptions { HotPostWindowDays = 7 })
            .Select(new[]
            {
                new ForumPostRecord { Id = "likes", AuthorSubId = "human-a", LikeCount = 8, CommentCount = 0, State = "published", CreatedAtUtc = now.AddMinutes(-5) },
                new ForumPostRecord { Id = "comments", AuthorSubId = "human-b", LikeCount = 2, CommentCount = 9, State = "published", CreatedAtUtc = now.AddMinutes(-10) },
            }, "agent-a", Array.Empty<string>(), now);

        Assert.Equal("comments", selected!.Id);
    }

    [Fact]
    public void Hot_post_selector_window_zero_includes_old_posts_and_skips_self()
    {
        var now = DateTime.UtcNow;
        var selector = new AgentHotPostSelector(new AgentSchedulerOptions { HotPostWindowDays = 7 });
        var oldHuman = new ForumPostRecord
        {
            Id = "old-human",
            AuthorSubId = "human-a",
            LikeCount = 1,
            CommentCount = 0,
            State = "published",
            CreatedAtUtc = now.AddDays(-40),
        };
        var selfRecent = new ForumPostRecord
        {
            Id = "self",
            AuthorSubId = "agent-a",
            LikeCount = 99,
            CommentCount = 99,
            State = "published",
            CreatedAtUtc = now.AddMinutes(-1),
        };

        Assert.Null(selector.Select(new[] { oldHuman, selfRecent }, "agent-a", Array.Empty<string>(), now));
        Assert.Equal("old-human", selector.Select(new[] { oldHuman, selfRecent }, "agent-a", Array.Empty<string>(), now, windowDays: 0)!.Id);
    }

    [Fact]
    public void Next_due_uses_seconds_when_default_seconds_set_and_no_per_agent_minutes()
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var due = AgentSchedulerQuota.NextDue(
            new AgentSchedulerStateRecord(),
            new AgentSchedulerOptions { DefaultMinIntervalSeconds = 5, DefaultMinIntervalMinutes = 180, DefaultJitterMinutes = 0 },
            now);
        Assert.Equal(now.AddSeconds(5), due);
    }

    [Fact]
    public void Quota_helper_resets_usage_on_new_utc_date()
    {
        var state = new AgentSchedulerStateRecord { QuotaDateUtc = "2026-07-12", PostsUsed = 2, RepliesUsed = 4 };
        AgentSchedulerQuota.ResetForUtcDate(state, new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("2026-07-13", state.QuotaDateUtc);
        Assert.Equal(0, state.PostsUsed);
        Assert.Equal(0, state.RepliesUsed);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, null, false)]
    public void Scheduler_enabled_is_options_or_runtime(bool optionsEnabled, bool? runtimeEnabled, bool expected)
    {
        Assert.Equal(expected, AgentLlmConfigResolver.CombineSchedulerEnabled(optionsEnabled, runtimeEnabled));
    }
}
