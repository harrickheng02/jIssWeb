using System.Security.Cryptography;
using System.Text;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using StackExchange.Redis;

namespace JIssWeb.User.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly TimeSpan AccessTokenTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);

    private readonly JwtSettings _jwt;
    private readonly RedisSettings _redisSettings;
    private readonly IMongoCollection<UserAccount> _users;
    private readonly IMongoCollection<RefreshSession> _refreshSessions;
    private readonly IDatabase _redis;

    public AuthController(
        IOptions<JwtSettings> jwtOptions,
        IOptions<MongoSettings> mongoOptions,
        IOptions<RedisSettings> redisOptions,
        IMongoClient mongoClient,
        IConnectionMultiplexer redis)
    {
        _jwt = jwtOptions.Value;
        _redisSettings = redisOptions.Value;
        var database = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _users = database.GetCollection<UserAccount>("users");
        _refreshSessions = database.GetCollection<RefreshSession>("refresh_sessions");
        _redis = redis.GetDatabase();
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AuthTokenPair>>> Register([FromBody] RegisterRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("邮箱或密码无效", "INVALID_INPUT"));

        var exists = await _users.Find(x => x.Email == email).AnyAsync();
        if (exists)
            return Conflict(ApiResult<AuthTokenPair>.Fail("邮箱已注册", "EMAIL_EXISTS"));

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashPassword(request.Password, salt);
        var user = new UserAccount
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Email = email,
            PasswordSalt = salt,
            PasswordHash = hash,
            CreatedAtUtc = DateTime.UtcNow
        };
        await _users.InsertOneAsync(user);
        var pair = await IssueTokenPairAsync(user);
        return Ok(ApiResult<AuthTokenPair>.Ok(pair));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AuthTokenPair>>> Login([FromBody] LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("邮箱或密码无效", "INVALID_INPUT"));

        var user = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (user is null || !VerifyPassword(request.Password, user.PasswordSalt, user.PasswordHash))
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("邮箱或密码错误", "LOGIN_FAILED"));

        var pair = await IssueTokenPairAsync(user);
        return Ok(ApiResult<AuthTokenPair>.Ok(pair));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AuthTokenPair>>> Refresh([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("refreshToken 不能为空", "INVALID_INPUT"));

        var tokenHash = Sha256(request.RefreshToken);
        if (await _redis.KeyExistsAsync(GetRefreshBlacklistKey(tokenHash)))
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("refreshToken 已失效", "REFRESH_REVOKED"));

        var session = await _refreshSessions.Find(x => x.TokenHash == tokenHash).FirstOrDefaultAsync();
        if (session is null || session.RevokedAtUtc != null || session.ExpiresAtUtc <= DateTime.UtcNow)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("refreshToken 无效", "REFRESH_INVALID"));

        var user = await _users.Find(x => x.Id == session.UserId).FirstOrDefaultAsync();
        if (user is null)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("用户不存在", "USER_NOT_FOUND"));

        var newRefreshToken = GenerateToken();
        var newRefreshHash = Sha256(newRefreshToken);
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(RefreshTokenTtl);

        session.RevokedAtUtc = now;
        session.ReplacedByTokenHash = newRefreshHash;
        await _refreshSessions.ReplaceOneAsync(x => x.Id == session.Id, session);

        var ttl = session.ExpiresAtUtc - now;
        if (ttl > TimeSpan.Zero)
            await _redis.StringSetAsync(GetRefreshBlacklistKey(tokenHash), "1", ttl);

        await _refreshSessions.InsertOneAsync(new RefreshSession
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = user.Id,
            TokenHash = newRefreshHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt
        });

        var accessToken = CreateAccessToken(user);
        return Ok(ApiResult<AuthTokenPair>.Ok(new AuthTokenPair
        {
            AccessToken = accessToken.Token,
            AccessTokenExpiresAtUtc = accessToken.ExpiresAtUtc,
            RefreshToken = newRefreshToken,
            RefreshTokenExpiresAtUtc = expiresAt
        }));
    }

    [HttpPost("revoke")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ApiResult<string>>> Revoke([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(ApiResult<string>.Fail("refreshToken 不能为空", "INVALID_INPUT"));

        var callerUserId = User.GetUserId();
        var tokenHash = Sha256(request.RefreshToken);
        var session = await _refreshSessions.Find(x => x.TokenHash == tokenHash).FirstOrDefaultAsync();
        if (session is null)
            return Ok(ApiResult<string>.Ok("ok"));
        if (!string.Equals(session.UserId, callerUserId, StringComparison.Ordinal))
            return Unauthorized(ApiResult<string>.Fail("无权吊销此 token", "REVOKE_FORBIDDEN"));

        if (session.RevokedAtUtc == null)
        {
            session.RevokedAtUtc = DateTime.UtcNow;
            await _refreshSessions.ReplaceOneAsync(x => x.Id == session.Id, session);
        }

        var ttl = session.ExpiresAtUtc - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            await _redis.StringSetAsync(GetRefreshBlacklistKey(tokenHash), "1", ttl);

        return Ok(ApiResult<string>.Ok("revoked"));
    }

    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AuthTokenPair>>> Token([FromBody] LoginRequest request)
    {
        return await Login(request);
    }

    [HttpPost("dev-mismatch-token")]
    [AllowAnonymous]
    public ActionResult<ApiResult<string>> DevMismatchToken()
    {
        if (!HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return NotFound(ApiResult<string>.Fail("not_found", "NOT_FOUND"));
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new System.Security.Claims.Claim("sub", "user-a"),
            new System.Security.Claims.Claim("userId", "user-b")
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: creds);
        return Ok(ApiResult<string>.Ok(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token)));
    }

    private async Task<AuthTokenPair> IssueTokenPairAsync(UserAccount user)
    {
        var refreshToken = GenerateToken();
        var refreshHash = Sha256(refreshToken);
        var now = DateTime.UtcNow;
        var refreshExpiresAt = now.Add(RefreshTokenTtl);
        await _refreshSessions.InsertOneAsync(new RefreshSession
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiresAt
        });
        var accessToken = CreateAccessToken(user);
        return new AuthTokenPair
        {
            AccessToken = accessToken.Token,
            AccessTokenExpiresAtUtc = accessToken.ExpiresAtUtc,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc = refreshExpiresAt
        };
    }

    private (string Token, DateTime ExpiresAtUtc) CreateAccessToken(UserAccount user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(AccessTokenTtl);
        var claims = new[]
        {
            new System.Security.Claims.Claim("sub", user.Id),
            new System.Security.Claims.Claim("userId", user.Id),
            new System.Security.Claims.Claim("email", user.Email)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: creds);
        return (new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        return email.Trim().ToLowerInvariant();
    }

    private static string HashPassword(string password, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string saltBase64, string expectedHashBase64)
    {
        var actualHash = Convert.FromBase64String(HashPassword(password, saltBase64));
        var expectedHash = Convert.FromBase64String(expectedHashBase64);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private string GetRefreshBlacklistKey(string tokenHash)
    {
        return $"{_redisSettings.KeyPrefix}refresh:blacklist:{tokenHash}";
    }
}

public class RegisterRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class LoginRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class RefreshRequest
{
    public string? RefreshToken { get; set; }
}

public class AuthTokenPair
{
    public string AccessToken { get; set; } = "";
    public DateTime AccessTokenExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = "";
    public DateTime RefreshTokenExpiresAtUtc { get; set; }
}

public class UserAccount
{
    [BsonId]
    public string Id { get; set; } = "";

    public string Email { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public class RefreshSession
{
    [BsonId]
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
