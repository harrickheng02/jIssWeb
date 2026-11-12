namespace JIssWeb.Model.Api.Services;

public interface IForumPostRateLimitService
{
    Task<bool> IsPostCreateRateLimitedAsync(string sub, string clientIp, bool isAgent = false);

    Task RecordSuccessfulPostCreateAsync(string sub, string clientIp, bool isAgent = false);

    Task<bool> IsReplyCreateRateLimitedAsync(string sub, string clientIp, bool isAgent = false);

    Task RecordSuccessfulReplyCreateAsync(string sub, string clientIp, bool isAgent = false);
}
