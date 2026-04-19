using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumEngagementService
{
    private readonly IMongoCollection<ForumPostRecord> _posts;
    private readonly IMongoCollection<ForumPostLikeRecord> _likes;
    private readonly IMongoCollection<ForumPostFavoriteRecord> _favorites;
    private readonly ForumEngagementLikeCountCache _likeCountCache;
    private readonly ILogger<ForumEngagementService> _logger;

    public ForumEngagementService(
        IMongoClient mongoClient,
        IOptions<MongoSettings> mongoOptions,
        ForumEngagementLikeCountCache likeCountCache,
        ILogger<ForumEngagementService> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _posts = db.GetCollection<ForumPostRecord>(ForumMongoSetup.PostsCollectionName);
        _likes = db.GetCollection<ForumPostLikeRecord>(ForumMongoSetup.LikesCollectionName);
        _favorites = db.GetCollection<ForumPostFavoriteRecord>(ForumMongoSetup.FavoritesCollectionName);
        _likeCountCache = likeCountCache;
        _logger = logger;
    }

    public async Task RemoveAllForPostAsync(string postId)
    {
        await _likeCountCache.RemoveLikeCountAsync(postId);
        await _likes.DeleteManyAsync(x => x.PostId == postId);
        await _favorites.DeleteManyAsync(x => x.PostId == postId);
    }

    private async Task SyncFavoriteCountAsync(string postId)
    {
        var count = (int)await _favorites.CountDocumentsAsync(x => x.PostId == postId);
        await _posts.UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update.Set(x => x.FavoriteCount, count));
    }

    public async Task<(HashSet<string> Liked, HashSet<string> Favorited)> GetEngagementSetsAsync(
        IReadOnlyList<string> postIds,
        string userSubId)
    {
        if (postIds.Count == 0)
            return (new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

        var inPosts = Builders<ForumPostLikeRecord>.Filter.In(x => x.PostId, postIds);
        var likeFilter = inPosts & Builders<ForumPostLikeRecord>.Filter.Eq(x => x.UserSubId, userSubId);
        var liked = (await _likes.Find(likeFilter).Project(x => x.PostId).ToListAsync()).ToHashSet(StringComparer.Ordinal);

        var inFav = Builders<ForumPostFavoriteRecord>.Filter.In(x => x.PostId, postIds);
        var favFilter = inFav & Builders<ForumPostFavoriteRecord>.Filter.Eq(x => x.UserSubId, userSubId);
        var fav = (await _favorites.Find(favFilter).Project(x => x.PostId).ToListAsync()).ToHashSet(StringComparer.Ordinal);

        return (liked, fav);
    }

    public async Task<PostEngagementSnapshot?> GetSnapshotAsync(string postId, string userSubId)
    {
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null) return null;
        var liked = await _likes.Find(x => x.PostId == postId && x.UserSubId == userSubId).AnyAsync();
        var favorited = await _favorites.Find(x => x.PostId == postId && x.UserSubId == userSubId).AnyAsync();
        var snap = new PostEngagementSnapshot(post.LikeCount, post.FavoriteCount, liked, favorited);
        await _likeCountCache.SetLikeCountAsync(postId, snap.LikeCount);
        return snap;
    }

    public async Task<PostEngagementSnapshot?> LikeAsync(string postId, string userSubId)
    {
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null) return null;

        var doc = new ForumPostLikeRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            PostId = postId,
            UserSubId = userSubId,
            CreatedAtUtc = DateTime.UtcNow,
        };

        try
        {
            await _likes.InsertOneAsync(doc);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            return await GetSnapshotAsync(postId, userSubId);
        }

        try
        {
            await _posts.UpdateOneAsync(x => x.Id == postId, Builders<ForumPostRecord>.Update.Inc(x => x.LikeCount, 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Like increment failed; compensating delete for like {LikeId}", doc.Id);
            try
            {
                await _likes.DeleteOneAsync(x => x.Id == doc.Id);
            }
            catch (Exception delEx)
            {
                _logger.LogError(delEx, "Compensating delete failed for like {LikeId}", doc.Id);
            }
            throw;
        }

        return await GetSnapshotAsync(postId, userSubId);
    }

    public async Task<PostEngagementSnapshot?> UnlikeAsync(string postId, string userSubId)
    {
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null) return null;

        var del = await _likes.DeleteOneAsync(x => x.PostId == postId && x.UserSubId == userSubId);
        if (del.DeletedCount > 0)
        {
            await _posts.UpdateOneAsync(
                x => x.Id == postId && x.LikeCount > 0,
                Builders<ForumPostRecord>.Update.Inc(x => x.LikeCount, -1));
        }

        return await GetSnapshotAsync(postId, userSubId);
    }

    public async Task<PostEngagementSnapshot?> FavoriteAsync(string postId, string userSubId)
    {
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null) return null;

        var doc = new ForumPostFavoriteRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            PostId = postId,
            UserSubId = userSubId,
            CreatedAtUtc = DateTime.UtcNow,
        };

        try
        {
            await _favorites.InsertOneAsync(doc);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // duplicate: relation already exists
        }

        await SyncFavoriteCountAsync(postId);
        return await GetSnapshotAsync(postId, userSubId);
    }

    public async Task<PostEngagementSnapshot?> UnfavoriteAsync(string postId, string userSubId)
    {
        var post = await _posts.Find(x => x.Id == postId).FirstOrDefaultAsync();
        if (post is null) return null;

        await _favorites.DeleteOneAsync(x => x.PostId == postId && x.UserSubId == userSubId);
        await SyncFavoriteCountAsync(postId);
        return await GetSnapshotAsync(postId, userSubId);
    }

    public async Task<(List<ForumPostRecord> Items, int TotalCount)> ListFavoritePostsAsync(
        string userSubId,
        int page,
        int pageSize)
    {
        var total = (int)await _favorites.CountDocumentsAsync(x => x.UserSubId == userSubId);
        var favDocs = await _favorites.Find(x => x.UserSubId == userSubId)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var orderedPosts = new List<ForumPostRecord>();
        foreach (var fav in favDocs)
        {
            var p = await _posts.Find(x => x.Id == fav.PostId).FirstOrDefaultAsync();
            if (p is null)
            {
                await _favorites.DeleteOneAsync(x => x.Id == fav.Id);
                continue;
            }
            orderedPosts.Add(p);
        }

        return (orderedPosts, total);
    }
}

public sealed record PostEngagementSnapshot(int LikeCount, int FavoriteCount, bool LikedByMe, bool FavoritedByMe);
