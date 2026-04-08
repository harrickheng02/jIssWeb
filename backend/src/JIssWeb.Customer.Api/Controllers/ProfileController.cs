using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Customer.Api.Models;
using JIssWeb.Customer.Api.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Customer.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IMongoCollection<ProfileRecord> _profiles;

    public ProfileController(IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _profiles = db.GetCollection<ProfileRecord>(CustomerMongoSetup.ProfileCollectionName);
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<ProfileRecord>>> Get()
    {
        var owner = User.GetUserId();
        var profile = await _profiles.Find(x => x.OwnerUserId == owner).FirstOrDefaultAsync();
        if (profile is null)
        {
            var now = DateTime.UtcNow;
            profile = new ProfileRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                OwnerUserId = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            await _profiles.InsertOneAsync(profile);
        }

        return Ok(ApiResult<ProfileRecord>.Ok(profile));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResult<ProfileRecord>>> Upsert([FromBody] UpdateProfileRequest request)
    {
        var owner = User.GetUserId();
        var profile = await _profiles.Find(x => x.OwnerUserId == owner).FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new ProfileRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                OwnerUserId = owner,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        profile.Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? null : request.Nickname.Trim();
        profile.BirthDate = request.BirthDate;
        profile.Gender = string.IsNullOrWhiteSpace(request.Gender) ? null : request.Gender.Trim();
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await _profiles.ReplaceOneAsync(x => x.OwnerUserId == owner, profile, new ReplaceOptions { IsUpsert = true });
        return Ok(ApiResult<ProfileRecord>.Ok(profile));
    }
}

public class UpdateProfileRequest
{
    public string? Nickname { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Gender { get; set; }
}
