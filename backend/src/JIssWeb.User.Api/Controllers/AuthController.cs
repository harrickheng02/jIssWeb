using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using JIssWeb.User.Api;
using JIssWeb.User.Api.Authorization;
using JIssWeb.User.Api.Models;
using JIssWeb.User.Api.Options;
using JIssWeb.User.Api.Services;
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
    /// <summary>与 frontend/src/utils/passwordPolicy.ts <c>isStrongPassword</c> 一致。</summary>
    private static readonly Regex PasswordStrongRegex = new("^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).{8,}$", RegexOptions.Compiled);

    private readonly JwtSettings _jwt;
    private readonly RedisSettings _redisSettings;
    private readonly EmailVerificationSettings _emailVerification;
    private readonly PasswordResetSettings _passwordReset;
    private readonly LoginSecuritySettings _loginSecurity;
    private readonly IMongoCollection<UserAccount> _users;
    private readonly IMongoCollection<RefreshSession> _refreshSessions;
    private readonly IDatabase _redis;
    private readonly IVerificationEmailSender _emailSender;
    private readonly ILogger<AuthController> _logger;
    private readonly IMongoCollection<ProfileRecordDoc>? _customerProfiles;
    private readonly ForumOptions _forum;
    private readonly ICaptchaVerifier _captchaVerifier;
    private readonly CaptchaSettings _captchaSettings;

    public AuthController(
        IOptions<JwtSettings> jwtOptions,
        IOptions<MongoSettings> mongoOptions,
        IOptions<RedisSettings> redisOptions,
        IOptions<EmailVerificationSettings> emailVerificationOptions,
        IOptions<PasswordResetSettings> passwordResetOptions,
        IOptions<LoginSecuritySettings> loginSecurityOptions,
        IOptions<ForumOptions> forumOptions,
        IOptions<CaptchaSettings> captchaOptions,
        IMongoClient mongoClient,
        IConnectionMultiplexer redis,
        IVerificationEmailSender emailSender,
        ICaptchaVerifier captchaVerifier,
        ILogger<AuthController> logger)
    {
        _jwt = jwtOptions.Value;
        _redisSettings = redisOptions.Value;
        _emailVerification = emailVerificationOptions.Value;
        _passwordReset = passwordResetOptions.Value;
        _loginSecurity = loginSecurityOptions.Value;
        _forum = forumOptions.Value;
        _captchaSettings = captchaOptions.Value;
        var database = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _users = database.GetCollection<UserAccount>("users");
        _refreshSessions = database.GetCollection<RefreshSession>("refresh_sessions");
        _redis = redis.GetDatabase();
        _emailSender = emailSender;
        _captchaVerifier = captchaVerifier;
        _logger = logger;
        var customerDb = mongoOptions.Value.CustomerDatabaseName?.Trim();
        if (!string.IsNullOrEmpty(customerDb))
            _customerProfiles = mongoClient.GetDatabase(customerDb).GetCollection<ProfileRecordDoc>("profiles");
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<string>>> Register([FromBody] RegisterRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || !IsValidEmail(email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResult<string>.Fail("邮箱或密码无效", "INVALID_INPUT"));
        if (!PasswordStrongRegex.IsMatch(request.Password))
            return BadRequest(ApiResult<string>.Fail("密码强度不足", "WEAK_PASSWORD"));
        if (await IsRateLimitedAsync("register", email, _emailVerification.RegisterPerMinuteLimit))
            return StatusCode(429, ApiResult<string>.Fail("请求过于频繁", "RATE_LIMITED"));

        var captchaResult = await ValidateCaptchaAsync(request.CaptchaToken);
        if (captchaResult is not null)
            return captchaResult;

        var profileErr = TryParseRegisterProfile(request, out var requestedNickname, out var gender, out var birthDate);
        if (profileErr is not null)
            return BadRequest(ApiResult<string>.Fail(profileErr, "INVALID_INPUT"));

        if (_customerProfiles is not null && !string.IsNullOrEmpty(requestedNickname))
        {
            var taken = await _customerProfiles.Find(x => x.Nickname == requestedNickname).AnyAsync();
            if (taken)
                return Conflict(ApiResult<string>.Fail("昵称已被使用", "NICKNAME_TAKEN"));
        }

        var existing = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (existing is not null)
        {
            if (existing.EmailVerifiedAtUtc.HasValue)
                return Conflict(ApiResult<string>.Fail("邮箱已注册", "EMAIL_EXISTS"));

            var resendGuard = await ValidateResendPolicyAsync(email);
            if (resendGuard is not null)
                return resendGuard;

            await UpsertProfileOnRegisterAsync(existing.Id, requestedNickname, gender, birthDate);

            try
            {
                await SendVerificationEmailAsync(existing);
                await MarkVerificationEmailSentAsync(email);
                return Ok(ApiResult<string>.Ok("VERIFICATION_EMAIL_SENT"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification email during register retry email={Email}", email);
                return StatusCode(503, ApiResult<string>.Fail("验证邮件发送失败", "MAIL_SEND_FAILED"));
            }
        }

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashPassword(request.Password, salt);
        var user = new UserAccount
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Email = email,
            PasswordSalt = salt,
            PasswordHash = hash,
            CreatedAtUtc = DateTime.UtcNow,
            EmailVerifiedAtUtc = null
        };
        await _users.InsertOneAsync(user);
        await UpsertProfileOnRegisterAsync(user.Id, requestedNickname, gender, birthDate);
        try
        {
            await SendVerificationEmailAsync(user);
            await MarkVerificationEmailSentAsync(email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email during register email={Email}", email);
            return StatusCode(503, ApiResult<string>.Fail("验证邮件发送失败", "MAIL_SEND_FAILED"));
        }
        return Ok(ApiResult<string>.Ok("VERIFICATION_EMAIL_SENT"));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AuthTokenPair>>> Login([FromBody] LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("邮箱或密码无效", "INVALID_INPUT"));

        var guardState = await GetLoginGuardStateAsync(email);
        if (guardState.State == LoginGuardState.Blocked)
        {
            Response.Headers.RetryAfter = guardState.RetryAfterSeconds.ToString();
            return StatusCode(429, ApiResult<AuthTokenPair>.Fail("登录尝试过于频繁，请稍后再试", "LOGIN_RATE_LIMITED"));
        }
        if (guardState.State == LoginGuardState.Cooldown)
        {
            Response.Headers.RetryAfter = guardState.RetryAfterSeconds.ToString();
            return StatusCode(429, ApiResult<AuthTokenPair>.Fail("登录失败次数过多，请稍后再试", "LOGIN_COOLDOWN"));
        }

        var user = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (user is null || user.IsAgentAccount)
        {
            await RecordLoginFailureAsync(email);
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("账号或密码错误", "LOGIN_FAILED"));
        }
        if (!VerifyPassword(request.Password, user.PasswordSalt, user.PasswordHash))
        {
            await RecordLoginFailureAsync(email);
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("账号或密码错误", "LOGIN_FAILED"));
        }
        if (!user.EmailVerifiedAtUtc.HasValue)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("邮箱未验证", "EMAIL_NOT_VERIFIED"));

        await ClearLoginFailuresAsync(email);
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
        if (!user.EmailVerifiedAtUtc.HasValue)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("邮箱未验证", "EMAIL_NOT_VERIFIED"));

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

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<string>>> ResendVerification([FromBody] ResendVerificationRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || !IsValidEmail(email))
            return BadRequest(ApiResult<string>.Fail("邮箱无效", "INVALID_INPUT"));

        var resendGuard = await ValidateResendPolicyAsync(email);
        if (resendGuard is not null)
            return resendGuard;

        var user = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (user is not null && !user.EmailVerifiedAtUtc.HasValue)
        {
            try
            {
                await SendVerificationEmailAsync(user);
                await MarkVerificationEmailSentAsync(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification email during resend email={Email}", email);
                return StatusCode(503, ApiResult<string>.Fail("验证邮件发送失败", "MAIL_SEND_FAILED"));
            }
        }
        return Ok(ApiResult<string>.Ok("VERIFICATION_EMAIL_SENT"));
    }

    [HttpGet("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromQuery] string? token)
    {
        var result = await ValidateVerificationTokenAsync(token);
        if (result is null)
            return BadRequest(ApiResult<string>.Fail("验证链接无效或已过期", "VERIFY_TOKEN_INVALID"));

        var user = await _users.Find(x => x.Id == result.UserId).FirstOrDefaultAsync();
        if (user is null)
            return NotFound(ApiResult<string>.Fail("用户不存在", "USER_NOT_FOUND"));
        if (!user.EmailVerifiedAtUtc.HasValue)
        {
            user.EmailVerifiedAtUtc = DateTime.UtcNow;
            await _users.ReplaceOneAsync(x => x.Id == user.Id, user);
        }

        var exchangeCode = GenerateToken();
        await _redis.StringSetAsync(GetVerifyExchangeKey(exchangeCode), user.Id, TimeSpan.FromMinutes(5));
        var redirectUrl = AppendQueryParam(_emailVerification.SuccessRedirectUrl, "verify_session", exchangeCode);
        return Redirect(redirectUrl);
    }

    [HttpPost("exchange-verify-session")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AuthTokenPair>>> ExchangeVerifySession([FromBody] ExchangeVerifySessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VerifySession))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("verifySession 不能为空", "INVALID_INPUT"));

        var key = GetVerifyExchangeKey(request.VerifySession.Trim());
        var userIdStr = await GetAndDeleteStringAsync(key);
        if (userIdStr is null)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("验证会话无效或已过期", "VERIFY_SESSION_INVALID"));

        var user = await _users.Find(x => x.Id == userIdStr).FirstOrDefaultAsync();
        if (user is null)
            return NotFound(ApiResult<AuthTokenPair>.Fail("用户不存在", "USER_NOT_FOUND"));
        if (!user.EmailVerifiedAtUtc.HasValue)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("邮箱未验证", "EMAIL_NOT_VERIFIED"));

        var pair = await IssueTokenPairAsync(user);
        return Ok(ApiResult<AuthTokenPair>.Ok(pair));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<string>>> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || !IsValidEmail(email))
            return BadRequest(ApiResult<string>.Fail("邮箱无效", "INVALID_INPUT"));

        var cooldownTtl = await _redis.KeyTimeToLiveAsync(GetForgotCooldownKey(email));
        if (cooldownTtl.HasValue && cooldownTtl.Value > TimeSpan.Zero)
        {
            var retryAfterSeconds = (int)Math.Ceiling(cooldownTtl.Value.TotalSeconds);
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(429, ApiResult<string>.Fail($"请在 {retryAfterSeconds} 秒后再试", "FORGOT_PASSWORD_COOLDOWN"));
        }

        if (await IsRateLimitedAsync("forgot-pwd", email, _passwordReset.ForgotPerMinuteLimit))
            return StatusCode(429, ApiResult<string>.Fail("请求过于频繁", "RATE_LIMITED"));

        var user = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (user is not null && user.EmailVerifiedAtUtc.HasValue)
        {
            try
            {
                await SendPasswordResetEmailAsync(user);
                await MarkForgotPasswordSentAsync(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email email={Email}", email);
            }
        }

        return Ok(ApiResult<string>.Ok("PASSWORD_RESET_EMAIL_SENT"));
    }

    [HttpGet("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromQuery] string? token)
    {
        var result = await ValidatePasswordResetTokenAsync(token);
        if (result is null)
        {
            var url = AppendQueryParam(_passwordReset.SuccessRedirectUrl, "error", "RESET_TOKEN_INVALID");
            return Redirect(url);
        }

        var exchangeCode = GenerateToken();
        await _redis.StringSetAsync(GetResetExchangeKey(exchangeCode), result.UserId, TimeSpan.FromMinutes(5));
        var redirectUrl = AppendQueryParam(_passwordReset.SuccessRedirectUrl, "reset_session", exchangeCode);
        return Redirect(redirectUrl);
    }

    [HttpPost("complete-reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AuthTokenPair>>> CompleteResetPassword([FromBody] CompleteResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ResetSession))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("resetSession 不能为空", "INVALID_INPUT"));
        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("密码无效", "INVALID_INPUT"));
        if (!PasswordStrongRegex.IsMatch(request.Password))
            return BadRequest(ApiResult<AuthTokenPair>.Fail("密码强度不足", "WEAK_PASSWORD"));

        var key = GetResetExchangeKey(request.ResetSession.Trim());
        var userIdStr = await GetAndDeleteStringAsync(key);
        if (userIdStr is null)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("重置会话无效或已过期", "PWD_RESET_SESSION_INVALID"));

        var user = await _users.Find(x => x.Id == userIdStr).FirstOrDefaultAsync();
        if (user is null)
            return NotFound(ApiResult<AuthTokenPair>.Fail("账号不存在或已失效", "USER_NOT_FOUND"));
        if (!user.EmailVerifiedAtUtc.HasValue)
            return Unauthorized(ApiResult<AuthTokenPair>.Fail("邮箱未验证", "EMAIL_NOT_VERIFIED"));

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashPassword(request.Password, salt);
        user.PasswordSalt = salt;
        user.PasswordHash = hash;
        await _users.ReplaceOneAsync(x => x.Id == user.Id, user);

        try
        {
            await RevokeAllRefreshSessionsForUserAsync(user.Id);
            var pair = await IssueTokenPairAsync(user);
            return Ok(ApiResult<AuthTokenPair>.Ok(pair));
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Password reset: failed to revoke sessions or issue tokens after password update userId={UserId}", user.Id);
            return StatusCode(503, ApiResult<AuthTokenPair>.Fail("会话更新失败，请使用新密码登录", "PWD_RESET_SESSION_FAILED"));
        }
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

    [HttpPost("/api/internal/agents/accounts")]
    [RequireInternalApiKey]
    public async Task<ActionResult<ApiResult<AgentAccountCreatedDto>>> CreateAgentAccount(
        [FromBody] CreateAgentAccountRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || !IsValidEmail(email))
            return BadRequest(ApiResult<AgentAccountCreatedDto>.Fail("邮箱无效", "INVALID_INPUT"));

        var existing = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (existing is not null)
            return Conflict(ApiResult<AgentAccountCreatedDto>.Fail("邮箱已注册", "EMAIL_EXISTS"));

        var now = DateTime.UtcNow;
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var user = new UserAccount
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Email = email,
            PasswordSalt = salt,
            PasswordHash = HashPassword(GenerateToken(), salt),
            CreatedAtUtc = now,
            EmailVerifiedAtUtc = now,
            IsAgentAccount = true,
        };

        await _users.InsertOneAsync(user);
        var accessToken = CreateAccessToken(user);
        return Ok(ApiResult<AgentAccountCreatedDto>.Ok(new AgentAccountCreatedDto
        {
            AgentUserId = user.Id,
            AccessToken = accessToken.Token,
            AccessTokenExpiresAtUtc = accessToken.ExpiresAtUtc,
        }));
    }

    private async Task RevokeAllRefreshSessionsForUserAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var sessions = await _refreshSessions.Find(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync();
        foreach (var s in sessions)
        {
            s.RevokedAtUtc = now;
            await _refreshSessions.ReplaceOneAsync(x => x.Id == s.Id, s);
            var ttl = s.ExpiresAtUtc - now;
            if (ttl > TimeSpan.Zero)
                await _redis.StringSetAsync(GetRefreshBlacklistKey(s.TokenHash), "1", ttl);
        }
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
        var forumRole = ResolveEffectiveForumRole(user);
        var claims = new List<System.Security.Claims.Claim>
        {
            new("sub", user.Id),
            new("userId", user.Id),
            new("email", user.Email),
            new("emailVerified", "true"),
            new(ForumRoleClaim.Name, forumRole),
            new(AccountTypeClaim.Name, user.IsAgentAccount ? AccountTypeClaim.Agent : AccountTypeClaim.Human),
        };
        // Only embed board scope when non-empty so model-service can fall back to Forum:Moderation (e.g. Docker env) when absent.
        if (string.Equals(forumRole, ForumRoleClaim.Moderator, StringComparison.Ordinal))
        {
            var boardIds = ResolveModeratorBoardIdsForToken(user);
            if (boardIds.Count > 0)
                claims.Add(new System.Security.Claims.Claim(ForumBoardIdsClaim.Name, ForumBoardIdsClaimJson.Serialize(boardIds)));
        }
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

    private string ResolveEffectiveForumRole(UserAccount user)
    {
        if (_forum.RoleOverrides.TryGetValue(user.Id, out var mapped))
        {
            var adminOrNull = NormalizeForumRoleOrNull(mapped);
            if (adminOrNull == ForumRoleClaim.Admin)
                return ForumRoleClaim.Admin;
        }

        var asModeratorEntry = _forum.Moderation.Moderators
            .FirstOrDefault(x => string.Equals((x.Sub ?? "").Trim(), user.Id.Trim(), StringComparison.Ordinal));
        if (asModeratorEntry is not null)
            return ForumRoleClaim.Moderator;

        if (_forum.RoleOverrides.TryGetValue(user.Id, out mapped))
        {
            var fromMap = NormalizeForumRoleOrNull(mapped);
            if (fromMap is not null)
                return fromMap;
        }

        return NormalizeForumRoleOrNull(user.ForumRole) ?? ForumRoleClaim.Member;
    }

    /// <summary>Board ids stored in JWT <see cref="ForumBoardIdsClaim"/>; same scope model-service uses when the claim is non-empty.</summary>
    private IReadOnlyList<string> ResolveModeratorBoardIdsForToken(UserAccount user)
    {
        var entry = _forum.Moderation.Moderators
            .FirstOrDefault(x => string.Equals((x.Sub ?? "").Trim(), user.Id.Trim(), StringComparison.Ordinal));
        if (entry?.BoardIds is { Count: > 0 })
            return entry.BoardIds;
        return Array.Empty<string>();
    }

    private static string? NormalizeForumRoleOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.Trim().ToLowerInvariant();
        return v switch
        {
            ForumRoleClaim.Member or ForumRoleClaim.Moderator or ForumRoleClaim.Admin => v,
            _ => null
        };
    }

    private async Task<ActionResult<ApiResult<string>>?> ValidateCaptchaAsync(string? captchaToken)
    {
        if (!_captchaSettings.Enabled)
            return null;

        // agent 账号豁免桩：#30 落地后此分支自动激活（当前生产环境 accountType claim 从不签发）
        var accountTypeClaim = User.FindFirstValue("accountType");
        if (string.Equals(accountTypeClaim, "agent", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(captchaToken))
            return BadRequest(ApiResult<string>.Fail("人机验证未完成", "CAPTCHA_REQUIRED"));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var passed = await _captchaVerifier.VerifyAsync(captchaToken, ip);
        if (!passed)
            return BadRequest(ApiResult<string>.Fail("人机验证失败，请重试", "CAPTCHA_INVALID"));

        return null;
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

    private string GetVerificationOneTimeKey(string tokenHash)
    {
        return $"{_redisSettings.KeyPrefix}verify:once:{tokenHash}";
    }

    private string GetVerifyExchangeKey(string code)
    {
        return $"{_redisSettings.KeyPrefix}verify:exchange:{Sha256(code)}";
    }

    private string GetForgotCooldownKey(string email)
    {
        return $"{_redisSettings.KeyPrefix}pwdreset:forgot:cooldown:{Sha256(email)}";
    }

    private string GetPwdResetOnceKey(string tokenHash)
    {
        return $"{_redisSettings.KeyPrefix}pwdreset:once:{tokenHash}";
    }

    private string GetResetExchangeKey(string code)
    {
        return $"{_redisSettings.KeyPrefix}pwdreset:exchange:{Sha256(code)}";
    }

    private static string AppendQueryParam(string baseUrl, string name, string value)
    {
        var sep = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{baseUrl}{sep}{name}={Uri.EscapeDataString(value)}";
    }

    private async Task<string?> GetAndDeleteStringAsync(RedisKey key)
    {
        var value = await _redis.StringGetAsync(key);
        if (value.IsNullOrEmpty)
            return null;
        await _redis.KeyDeleteAsync(key);
        return value!;
    }

    private string GetRateLimitKey(string bucket, string key)
    {
        return $"{_redisSettings.KeyPrefix}rate:{bucket}:{key}";
    }

    private string GetLoginFailureKey(string scope, string key)
    {
        return $"{_redisSettings.KeyPrefix}login-fail:{scope}:{key}";
    }

    private string GetLoginPairFailureKey(string emailHash, string ipHash)
    {
        return $"{_redisSettings.KeyPrefix}login-fail:pair:{emailHash}:{ipHash}";
    }

    private string GetLoginCooldownKey(string emailHash, string ipHash)
    {
        return $"{_redisSettings.KeyPrefix}login-guard:cooldown:{emailHash}:{ipHash}";
    }

    private string GetLoginPairLockKey(string emailHash, string ipHash)
    {
        return $"{_redisSettings.KeyPrefix}login-guard:pair-lock:{emailHash}:{ipHash}";
    }

    private string GetLoginIpLockKey(string ipHash)
    {
        return $"{_redisSettings.KeyPrefix}login-guard:ip-lock:{ipHash}";
    }

    private string GetResendCooldownKey(string email)
    {
        return $"{_redisSettings.KeyPrefix}verify:resend:cooldown:{Sha256(email)}";
    }

    private string GetResendDailyLimitKey(string email)
    {
        return $"{_redisSettings.KeyPrefix}verify:resend:day:{Sha256(email)}";
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SendVerificationEmailAsync(UserAccount user)
    {
        var token = await CreateVerificationTokenAsync(user.Id);
        var link = $"{_emailVerification.LinkBaseUrl}?token={Uri.EscapeDataString(token)}";
        await _emailSender.SendVerificationEmailAsync(user.Email, link, DateTime.UtcNow.AddMinutes(_emailVerification.TokenTtlMinutes));
    }

    private async Task<ActionResult<ApiResult<string>>?> ValidateResendPolicyAsync(string email)
    {
        var cooldownTtl = await _redis.KeyTimeToLiveAsync(GetResendCooldownKey(email));
        if (cooldownTtl.HasValue && cooldownTtl.Value > TimeSpan.Zero)
        {
            var retryAfterSeconds = (int)Math.Ceiling(cooldownTtl.Value.TotalSeconds);
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(429, ApiResult<string>.Fail($"请在 {retryAfterSeconds} 秒后再试", "RESEND_COOLDOWN"));
        }

        if (await IsRateLimitedAsync("resend", email, _emailVerification.ResendPerMinuteLimit))
            return StatusCode(429, ApiResult<string>.Fail("请求过于频繁，请稍后再试", "RATE_LIMITED"));

        if (await IsRateLimitedWindowAsync(GetResendDailyLimitKey(email), _emailVerification.ResendPerDayLimit, TimeSpan.FromDays(1)))
            return StatusCode(429, ApiResult<string>.Fail("今日验证邮件发送次数过多，请明天再试", "RESEND_DAILY_LIMITED"));

        return null;
    }

    private Task MarkVerificationEmailSentAsync(string email)
    {
        return _redis.StringSetAsync(GetResendCooldownKey(email), "1", TimeSpan.FromSeconds(_emailVerification.ResendCooldownSeconds));
    }

    private async Task<string> CreateVerificationTokenAsync(string userId)
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(_emailVerification.TokenTtlMinutes).ToUnixTimeSeconds();
        var nonce = GenerateToken();
        var payload = $"{userId}.{exp}.{nonce}";
        var sig = CreateVerificationSignature(payload);
        var token = $"{payload}.{sig}";
        var ttl = TimeSpan.FromMinutes(_emailVerification.TokenTtlMinutes);
        await _redis.StringSetAsync(GetVerificationOneTimeKey(Sha256(token)), "1", ttl);
        return token;
    }

    private async Task<VerifyTokenResult?> ValidateVerificationTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 4)
            return null;

        var userId = parts[0];
        if (!long.TryParse(parts[1], out var exp))
            return null;
        var nonce = parts[2];
        var sig = parts[3];

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
            return null;

        var payload = $"{userId}.{exp}.{nonce}";
        var expected = CreateVerificationSignature(payload);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(sig)))
            return null;

        var oneTimeKey = GetVerificationOneTimeKey(Sha256(token));
        var consumed = await _redis.KeyDeleteAsync(oneTimeKey);
        if (!consumed)
            return null;

        return new VerifyTokenResult { UserId = userId };
    }

    private string CreateVerificationSignature(string payload)
    {
        var secret = Encoding.UTF8.GetBytes(_emailVerification.Secret);
        using var hmac = new HMACSHA256(secret);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(sig);
    }

    private async Task SendPasswordResetEmailAsync(UserAccount user)
    {
        var token = await CreatePasswordResetTokenAsync(user.Id);
        var link = $"{_passwordReset.LinkBaseUrl}?token={Uri.EscapeDataString(token)}";
        await _emailSender.SendPasswordResetEmailAsync(user.Email, link, DateTime.UtcNow.AddMinutes(_passwordReset.TokenTtlMinutes));
    }

    private Task MarkForgotPasswordSentAsync(string email)
    {
        return _redis.StringSetAsync(GetForgotCooldownKey(email), "1", TimeSpan.FromSeconds(_passwordReset.ForgotCooldownSeconds));
    }

    private async Task<string> CreatePasswordResetTokenAsync(string userId)
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(_passwordReset.TokenTtlMinutes).ToUnixTimeSeconds();
        var nonce = GenerateToken();
        var payload = $"{userId}.{exp}.{nonce}";
        var sig = CreatePasswordResetSignature(payload);
        var token = $"{payload}.{sig}";
        var ttl = TimeSpan.FromMinutes(_passwordReset.TokenTtlMinutes);
        await _redis.StringSetAsync(GetPwdResetOnceKey(Sha256(token)), "1", ttl);
        return token;
    }

    private async Task<VerifyTokenResult?> ValidatePasswordResetTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 4)
            return null;

        var userId = parts[0];
        if (!long.TryParse(parts[1], out var exp))
            return null;
        var nonce = parts[2];
        var sig = parts[3];

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
            return null;

        var payload = $"{userId}.{exp}.{nonce}";
        var expected = CreatePasswordResetSignature(payload);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(sig)))
            return null;

        var oneTimeKey = GetPwdResetOnceKey(Sha256(token));
        var consumed = await _redis.KeyDeleteAsync(oneTimeKey);
        if (!consumed)
            return null;

        return new VerifyTokenResult { UserId = userId };
    }

    private string CreatePasswordResetSignature(string payload)
    {
        var secret = Encoding.UTF8.GetBytes(_passwordReset.Secret);
        using var hmac = new HMACSHA256(secret);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(sig);
    }

    private async Task<bool> IsRateLimitedAsync(string bucket, string email, int limitPerMinute)
    {
        var emailKey = GetRateLimitKey(bucket, $"email:{Sha256(email)}");
        if (await IsRateLimitedWindowAsync(emailKey, limitPerMinute, TimeSpan.FromMinutes(1)))
            return true;

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            var ipKey = GetRateLimitKey(bucket, $"ip:{Sha256(ip)}");
            if (await IsRateLimitedWindowAsync(ipKey, limitPerMinute, TimeSpan.FromMinutes(1)))
                return true;
        }

        return false;
    }

    private async Task<bool> IsRateLimitedWindowAsync(string key, int limit, TimeSpan window)
    {
        var count = await _redis.StringIncrementAsync(key);
        if (count == 1)
            await _redis.KeyExpireAsync(key, window);
        return count > limit;
    }

    private async Task<LoginGuardDecision> GetLoginGuardStateAsync(string email)
    {
        var ip = GetClientIp();
        if (string.IsNullOrWhiteSpace(ip))
            return LoginGuardDecision.None;

        var emailHash = Sha256(email);
        var ipHash = Sha256(ip);

        var ipLockTtl = await _redis.KeyTimeToLiveAsync(GetLoginIpLockKey(ipHash));
        if (ipLockTtl.HasValue && ipLockTtl.Value > TimeSpan.Zero)
            return LoginGuardDecision.Blocked(ipLockTtl.Value);

        var pairLockTtl = await _redis.KeyTimeToLiveAsync(GetLoginPairLockKey(emailHash, ipHash));
        if (pairLockTtl.HasValue && pairLockTtl.Value > TimeSpan.Zero)
            return LoginGuardDecision.Blocked(pairLockTtl.Value);

        var cooldownTtl = await _redis.KeyTimeToLiveAsync(GetLoginCooldownKey(emailHash, ipHash));
        if (cooldownTtl.HasValue && cooldownTtl.Value > TimeSpan.Zero)
            return LoginGuardDecision.Cooldown(cooldownTtl.Value);

        return LoginGuardDecision.None;
    }

    private async Task RecordLoginFailureAsync(string email)
    {
        var window = TimeSpan.FromMinutes(_loginSecurity.WindowMinutes);
        var emailHash = Sha256(email);
        await IncrementCounterAsync(GetLoginFailureKey("email", emailHash), window);

        var ip = GetClientIp();
        if (string.IsNullOrWhiteSpace(ip))
            return;

        var ipHash = Sha256(ip);
        var ipCount = await IncrementCounterAsync(GetLoginFailureKey("ip", ipHash), window);
        var pairCount = await IncrementCounterAsync(GetLoginPairFailureKey(emailHash, ipHash), window);

        if (pairCount >= _loginSecurity.PairLockThreshold)
            await _redis.StringSetAsync(GetLoginPairLockKey(emailHash, ipHash), "1", TimeSpan.FromMinutes(_loginSecurity.PairLockMinutes));
        else if (pairCount >= _loginSecurity.CooldownThreshold)
            await _redis.StringSetAsync(GetLoginCooldownKey(emailHash, ipHash), "1", TimeSpan.FromSeconds(_loginSecurity.CooldownSeconds));

        if (ipCount >= _loginSecurity.IpLockThreshold)
            await _redis.StringSetAsync(GetLoginIpLockKey(ipHash), "1", TimeSpan.FromMinutes(_loginSecurity.IpLockMinutes));
    }

    private async Task ClearLoginFailuresAsync(string email)
    {
        var emailHash = Sha256(email);
        await _redis.KeyDeleteAsync(GetLoginFailureKey("email", emailHash));

        var ip = GetClientIp();
        if (string.IsNullOrWhiteSpace(ip))
            return;

        var ipHash = Sha256(ip);
        await _redis.KeyDeleteAsync(GetLoginPairFailureKey(emailHash, ipHash));
        await _redis.KeyDeleteAsync(GetLoginCooldownKey(emailHash, ipHash));
        await _redis.KeyDeleteAsync(GetLoginPairLockKey(emailHash, ipHash));
    }

    private static string? TryParseRegisterProfile(
        RegisterRequest request,
        out string? requestedNickname,
        out string? gender,
        out DateTime? birthDate)
    {
        requestedNickname = null;
        gender = null;
        birthDate = null;
        var n = request.Nickname?.Trim();
        if (!string.IsNullOrEmpty(n))
        {
            if (n.Length > 50)
                return "昵称过长";
            requestedNickname = n;
        }
        if (!string.IsNullOrWhiteSpace(request.Gender))
        {
            var g = request.Gender.Trim().ToLowerInvariant();
            if (g is not ("male" or "female" or "other"))
                return "性别无效";
            gender = g;
        }

        if (!string.IsNullOrWhiteSpace(request.BirthDate))
        {
            if (!DateTime.TryParse(request.BirthDate, out var bd))
                return "生日无效";
            birthDate = DateTime.SpecifyKind(bd.Date, DateTimeKind.Utc);
        }

        return null;
    }

    private async Task UpsertProfileOnRegisterAsync(string userId, string? requestedNickname, string? gender, DateTime? birthDate)
    {
        if (_customerProfiles is null) return;
        var now = DateTime.UtcNow;
        var filter = Builders<ProfileRecordDoc>.Filter.Eq(x => x.OwnerUserId, userId);
        var existing = await _customerProfiles.Find(filter).FirstOrDefaultAsync();
        var nickname = ResolveNicknameForRegister(userId, requestedNickname, existing);
        if (existing is null)
        {
            await _customerProfiles.InsertOneAsync(new ProfileRecordDoc
            {
                Id = ObjectId.GenerateNewId().ToString(),
                OwnerUserId = userId,
                Nickname = nickname,
                Gender = gender,
                BirthDate = birthDate,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            return;
        }

        await _customerProfiles.UpdateOneAsync(
            filter,
            Builders<ProfileRecordDoc>.Update
                .Set(x => x.Nickname, nickname)
                .Set(x => x.Gender, gender)
                .Set(x => x.BirthDate, birthDate)
                .Set(x => x.UpdatedAtUtc, now));
    }

    private static string ResolveNicknameForRegister(string userId, string? requested, ProfileRecordDoc? existing)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim();
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Nickname))
            return existing.Nickname!.Trim();
        return DefaultNicknameFor(userId);
    }

    private static string DefaultNicknameFor(string userId)
    {
        var s = $"用户{userId}";
        return s.Length <= 50 ? s : s[..50];
    }

    private string? GetClientIp()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private async Task<long> IncrementCounterAsync(string key, TimeSpan window)
    {
        var count = await _redis.StringIncrementAsync(key);
        if (count == 1)
            await _redis.KeyExpireAsync(key, window);
        return count;
    }
}

public class RegisterRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Nickname { get; set; }
    public string? Gender { get; set; }
    public string? BirthDate { get; set; }
    public string? CaptchaToken { get; set; }
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

public class ResendVerificationRequest
{
    public string? Email { get; set; }
}

public class ExchangeVerifySessionRequest
{
    public string? VerifySession { get; set; }
}

public class ForgotPasswordRequest
{
    public string? Email { get; set; }
}

public class CompleteResetPasswordRequest
{
    public string? ResetSession { get; set; }
    public string? Password { get; set; }
}

public class CreateAgentAccountRequest
{
    public string? Email { get; set; }
    public string? PersonaId { get; set; }
}

public class AgentAccountCreatedDto
{
    public string AgentUserId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public DateTime AccessTokenExpiresAtUtc { get; set; }
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
    public DateTime? EmailVerifiedAtUtc { get; set; }

    /// <summary>Global forum role persisted for the account: member, moderator, or admin.</summary>
    public string? ForumRole { get; set; }

    /// <summary>When true, JWT includes <c>accountType: agent</c>; password login is rejected.</summary>
    public bool IsAgentAccount { get; set; }
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

public class EmailVerificationSettings
{
    public string Secret { get; set; } = "dev-email-verify-secret";
    public string LinkBaseUrl { get; set; } = "http://localhost:5097/api/auth/verify-email";
    public string SuccessRedirectUrl { get; set; } = "http://localhost:5173/register/verified";
    public int TokenTtlMinutes { get; set; } = 30;
    public int RegisterPerMinuteLimit { get; set; } = 5;
    public int ResendPerMinuteLimit { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int ResendPerDayLimit { get; set; } = 20;
}

public class VerifyTokenResult
{
    public string UserId { get; set; } = "";
}

public class LoginSecuritySettings
{
    public int WindowMinutes { get; set; } = 15;
    public int CooldownThreshold { get; set; } = 3;
    public int CooldownSeconds { get; set; } = 60;
    public int PairLockThreshold { get; set; } = 5;
    public int PairLockMinutes { get; set; } = 5;
    public int IpLockThreshold { get; set; } = 10;
    public int IpLockMinutes { get; set; } = 30;
}

public enum LoginGuardState
{
    None = 0,
    Cooldown = 1,
    Blocked = 2
}

public class LoginGuardDecision
{
    public static LoginGuardDecision None => new() { State = LoginGuardState.None };

    public LoginGuardState State { get; set; }
    public int RetryAfterSeconds { get; set; }

    public static LoginGuardDecision Cooldown(TimeSpan ttl)
    {
        return new LoginGuardDecision
        {
            State = LoginGuardState.Cooldown,
            RetryAfterSeconds = (int)Math.Ceiling(ttl.TotalSeconds)
        };
    }

    public static LoginGuardDecision Blocked(TimeSpan ttl)
    {
        return new LoginGuardDecision
        {
            State = LoginGuardState.Blocked,
            RetryAfterSeconds = (int)Math.Ceiling(ttl.TotalSeconds)
        };
    }
}
