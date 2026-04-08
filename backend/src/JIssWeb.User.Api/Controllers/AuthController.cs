using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
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
    private static readonly Regex PasswordStrongRegex = new("^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).{8,}$", RegexOptions.Compiled);

    private readonly JwtSettings _jwt;
    private readonly RedisSettings _redisSettings;
    private readonly EmailVerificationSettings _emailVerification;
    private readonly LoginSecuritySettings _loginSecurity;
    private readonly IMongoCollection<UserAccount> _users;
    private readonly IMongoCollection<RefreshSession> _refreshSessions;
    private readonly IDatabase _redis;
    private readonly IVerificationEmailSender _emailSender;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IOptions<JwtSettings> jwtOptions,
        IOptions<MongoSettings> mongoOptions,
        IOptions<RedisSettings> redisOptions,
        IOptions<EmailVerificationSettings> emailVerificationOptions,
        IOptions<LoginSecuritySettings> loginSecurityOptions,
        IMongoClient mongoClient,
        IConnectionMultiplexer redis,
        IVerificationEmailSender emailSender,
        ILogger<AuthController> logger)
    {
        _jwt = jwtOptions.Value;
        _redisSettings = redisOptions.Value;
        _emailVerification = emailVerificationOptions.Value;
        _loginSecurity = loginSecurityOptions.Value;
        var database = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _users = database.GetCollection<UserAccount>("users");
        _refreshSessions = database.GetCollection<RefreshSession>("refresh_sessions");
        _redis = redis.GetDatabase();
        _emailSender = emailSender;
        _logger = logger;
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

        var existing = await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (existing is not null)
        {
            if (existing.EmailVerifiedAtUtc.HasValue)
                return Conflict(ApiResult<string>.Fail("邮箱已注册", "EMAIL_EXISTS"));

            var resendGuard = await ValidateResendPolicyAsync(email);
            if (resendGuard is not null)
                return resendGuard;

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
        if (user is null)
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
            new System.Security.Claims.Claim("email", user.Email),
            new System.Security.Claims.Claim("emailVerified", "true")
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

    private string GetVerificationOneTimeKey(string tokenHash)
    {
        return $"{_redisSettings.KeyPrefix}verify:once:{tokenHash}";
    }

    private string GetVerifyExchangeKey(string code)
    {
        return $"{_redisSettings.KeyPrefix}verify:exchange:{Sha256(code)}";
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
