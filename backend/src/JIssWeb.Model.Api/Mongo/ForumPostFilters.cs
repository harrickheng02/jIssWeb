using JIssWeb.Model.Api.Models;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Mongo;

internal static class ForumPostFilters
{
    internal static FilterDefinition<ForumPostRecord> Published() =>
        Builders<ForumPostRecord>.Filter.Eq(x => x.State, ForumContentStates.Published);

    internal static FilterDefinition<ForumReplyRecord> ReplyPublished() =>
        Builders<ForumReplyRecord>.Filter.Eq(x => x.State, ForumContentStates.Published);
}
