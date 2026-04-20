using System.Text;
using System.Text.Json;
using JIssWeb.Common;
using JIssWeb.Common.Options;
using JIssWeb.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace JIssWeb.Common.Hosting;

public static class WebApiHostExtensions
{
    public static WebApplicationBuilder UseJIssWebHttpPort(this WebApplicationBuilder builder, int port)
    {
        builder.WebHost.UseSetting("urls", $"http://0.0.0.0:{port}");
        return builder;
    }

    public static IServiceCollection AddJIssWebCoreApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtIdentityValidation");
                        var sub = context.Principal?.FindFirst("sub")?.Value;
                        var userId = context.Principal?.FindFirst("userId")?.Value;
                        if (string.IsNullOrWhiteSpace(sub))
                        {
                            logger.LogWarning("JWT missing sub claim");
                            context.Fail("invalid_token_sub_missing");
                            return Task.CompletedTask;
                        }

                        if (!string.IsNullOrWhiteSpace(userId) && !string.Equals(sub, userId, StringComparison.Ordinal))
                        {
                            logger.LogWarning("JWT sub/userId mismatch sub={Sub} userId={UserId}", sub, userId);
                            context.Fail("invalid_token_identity_mismatch");
                            return Task.CompletedTask;
                        }

                        var forumRole = context.Principal?.FindFirst(ForumRoleClaim.Name)?.Value;
                        if (!string.IsNullOrWhiteSpace(forumRole))
                        {
                            var fr = forumRole.Trim().ToLowerInvariant();
                            if (fr != ForumRoleClaim.Member && fr != ForumRoleClaim.Moderator && fr != ForumRoleClaim.Admin)
                            {
                                logger.LogWarning("JWT invalid forumRole claim value={Value}", forumRole);
                                context.Fail("invalid_token_forum_role");
                                return Task.CompletedTask;
                            }

                            if (fr == ForumRoleClaim.Moderator)
                            {
                                var rawBoards = context.Principal?.FindFirst(ForumBoardIdsClaim.Name)?.Value;
                                if (rawBoards is not null && !ForumBoardIdsClaimJson.TryDeserialize(rawBoards, out _))
                                {
                                    logger.LogWarning("JWT invalid forumBoardIds claim");
                                    context.Fail("invalid_token_forum_board_ids");
                                    return Task.CompletedTask;
                                }
                            }
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        var body = JsonSerializer.Serialize(ApiResult.Fail("未授权", "UNAUTHORIZED"));
                        await context.Response.WriteAsync(body);
                    }
                };
            });

        services.AddAuthorization();
        services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        return services;
    }
}
