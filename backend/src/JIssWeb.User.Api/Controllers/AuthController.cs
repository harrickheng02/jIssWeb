using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JIssWeb.Common;
using JIssWeb.Common.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace JIssWeb.User.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly JwtSettings _jwt;

    public AuthController(IOptions<JwtSettings> jwtOptions)
    {
        _jwt = jwtOptions.Value;
    }

    [HttpPost("token")]
    [AllowAnonymous]
    public ActionResult<ApiResult<string>> Token()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: new[] { new Claim(ClaimTypes.Name, "dev-user") },
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);
        var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(ApiResult<string>.Ok(tokenStr));
    }
}
