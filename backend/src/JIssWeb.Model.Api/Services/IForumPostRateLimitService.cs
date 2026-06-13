namespace JIssWeb.Model.Api.Services;

public interface IForumPostRateLimitService
{
    bool IsPostCreateRateLimited(string sub, string clientIp);

    void RecordSuccessfulPostCreate(string sub, string clientIp);

    bool IsReplyCreateRateLimited(string sub, string clientIp);

    void RecordSuccessfulReplyCreate(string sub, string clientIp);
}
