using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumNotifications")]
public class ForumNotificationsTests
{
    private readonly ForumNotificationsIntegrationFixture _fx;

    public ForumNotificationsTests(ForumNotificationsIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Notifications_EndToEnd()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _fx.Client.GetAsync("/api/forum/notifications")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _fx.Client.GetAsync("/api/forum/notifications/unread-count")).StatusCode);

        var postReply = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts/nf-post-1/replies")
        {
            Content = new StringContent("{\"body\":\"from-b\"}", Encoding.UTF8, "application/json"),
        };
        postReply.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
        var replyRes = await _fx.Client.SendAsync(postReply);
        Assert.Equal(HttpStatusCode.OK, replyRes.StatusCode);

        var listA = new HttpRequestMessage(HttpMethod.Get, "/api/forum/notifications?page=1&pageSize=20");
        listA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var listARes = await _fx.Client.SendAsync(listA);
        Assert.Equal(HttpStatusCode.OK, listARes.StatusCode);
        string notifId;
        using (var doc = JsonDocument.Parse(await listARes.Content.ReadAsStringAsync()))
        {
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            var items = root.GetProperty("data").GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal("ReplyToPost", items[0].GetProperty("type").GetString());
            Assert.False(items[0].GetProperty("read").GetBoolean());
            notifId = items[0].GetProperty("id").GetString()!;
        }

        var unreadReq = new HttpRequestMessage(HttpMethod.Get, "/api/forum/notifications/unread-count");
        unreadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        var unreadRes = await _fx.Client.SendAsync(unreadReq);
        using (var doc = JsonDocument.Parse(await unreadRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("count").GetInt32());
        }

        var listB = new HttpRequestMessage(HttpMethod.Get, "/api/forum/notifications?page=1&pageSize=20");
        listB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-b"));
        var listBRes = await _fx.Client.SendAsync(listB);
        using (var doc = JsonDocument.Parse(await listBRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        }

        var selfReply = new HttpRequestMessage(HttpMethod.Post, "/api/forum/posts/nf-post-1/replies")
        {
            Content = new StringContent("{\"body\":\"from-a-self\"}", Encoding.UTF8, "application/json"),
        };
        selfReply.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(selfReply)).StatusCode);

        var listA2Req = new HttpRequestMessage(HttpMethod.Get, "/api/forum/notifications?page=1&pageSize=20");
        listA2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        using (var doc = JsonDocument.Parse(await (await _fx.Client.SendAsync(listA2Req)).Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        }

        var mark = new HttpRequestMessage(HttpMethod.Post, $"/api/forum/notifications/{notifId}/read");
        mark.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(mark)).StatusCode);

        var unread2 = new HttpRequestMessage(HttpMethod.Get, "/api/forum/notifications/unread-count");
        unread2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        using (var doc = JsonDocument.Parse(await (await _fx.Client.SendAsync(unread2)).Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("count").GetInt32());
        }

        var markAll = new HttpRequestMessage(HttpMethod.Post, "/api/forum/notifications/read-all");
        markAll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a"));
        Assert.Equal(HttpStatusCode.OK, (await _fx.Client.SendAsync(markAll)).StatusCode);
    }
}
