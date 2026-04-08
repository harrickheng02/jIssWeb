using System.Net.Http.Json;
using JIssWeb.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JIssWeb.Frontend.Bff.Controllers;

[ApiController]
[Route("api/bff/bootstrap")]
[AllowAnonymous]
public class BootstrapController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DownstreamServicesOptions _options;

    public BootstrapController(IHttpClientFactory httpClientFactory, IOptions<DownstreamServicesOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<ApiResult<BootstrapResponse>> Get()
    {
        var client = _httpClientFactory.CreateClient();
        var user = await GetServiceStatusAsync(client, _options.UserApiBaseUrl, "user");
        var customer = await GetServiceStatusAsync(client, _options.CustomerApiBaseUrl, "customer");

        return ApiResult<BootstrapResponse>.Ok(new BootstrapResponse
        {
            Services = new List<ServiceStatus> { user, customer }
        });
    }

    private static async Task<ServiceStatus> GetServiceStatusAsync(HttpClient client, string baseUrl, string name)
    {
        try
        {
            var response = await client.GetFromJsonAsync<ApiResult<string>>($"{baseUrl.TrimEnd('/')}/api/health");
            return new ServiceStatus
            {
                Name = name,
                Available = response?.Success == true,
                Message = response?.Data ?? response?.Message
            };
        }
        catch (Exception ex)
        {
            return new ServiceStatus
            {
                Name = name,
                Available = false,
                Message = ex.Message
            };
        }
    }
}

public class BootstrapResponse
{
    public List<ServiceStatus> Services { get; set; } = [];
}

public class ServiceStatus
{
    public string Name { get; set; } = "";
    public bool Available { get; set; }
    public string? Message { get; set; }
}
