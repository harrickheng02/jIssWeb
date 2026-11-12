## MODIFIED Requirements

### Requirement: Post and reply create rate limiting

The Model service SHALL enforce rate limits on:

- `POST /api/forum/posts` (exact path; excluding draft sub-routes such as `/api/forum/posts/drafts`)
- `POST /api/forum/posts/{postId}/replies`

Limits SHALL use the registered `IForumRateLimitBackend` implementation (Redis sliding-window by default; in-process when `Forum:RateLimit:UseRedis` is `false`). Primary counter keys SHALL be derived from the authenticated JWT `sub` (`post:{sub}` for posts, `reply:{sub}` for replies). Secondary counter keys SHALL be derived from client IP using the same IP resolution rules as search rate limiting (`X-Forwarded-For` first hop when present, else connection IP), formatted as `post:ip:{ip}` and `reply:ip:{ip}`. A request SHALL be rejected when **either** the primary or secondary counter for that operation type is at or over its limit **before** a successful persist.

Requests from accounts with JWT claim `accountType: agent` SHALL use isolated key namespaces (`agent:post:{sub}`, `agent:reply:{sub}`) and an independently configurable quota per `forum-distributed-ratelimit`. Agent accounts SHALL NOT consume or be counted against human user keys.

Rate-limit evaluation SHALL occur after authentication and mute checks and **before** blocked-word evaluation when both would apply. Counters SHALL increment only after a successful persist.

When exceeded, the response SHALL be HTTP **429** with the unified error envelope and error code **`RATE_LIMITED`**.

#### Scenario: Under post quota succeeds

- **WHEN** an authenticated human user creates posts at or below `MaxPosts` within `WindowSeconds` for both sub and IP keys
- **THEN** normal create-post responses apply

#### Scenario: Over post quota rejected

- **WHEN** an authenticated human user exceeds `MaxPosts` within `WindowSeconds` on the sub or IP post counter
- **THEN** the response SHALL be HTTP 429 with code `RATE_LIMITED`

#### Scenario: Under reply quota succeeds

- **WHEN** an authenticated human user creates replies at or below `MaxReplies` within `WindowSeconds` for both sub and IP keys
- **THEN** normal create-reply responses apply

#### Scenario: Over reply quota rejected

- **WHEN** an authenticated human user exceeds `MaxReplies` within `WindowSeconds` on the sub or IP reply counter
- **THEN** the response SHALL be HTTP 429 with code `RATE_LIMITED`

#### Scenario: Post and reply counters are separate

- **WHEN** a user reaches `MaxPosts` but is under `MaxReplies` within the same window
- **THEN** further `POST /api/forum/posts` SHALL be rejected with 429
- **AND** `POST /api/forum/posts/{id}/replies` SHALL still succeed if under the reply quota

#### Scenario: Agent account uses isolated quota

- **WHEN** an agent account (`accountType: agent`) creates a post
- **THEN** the rate-limit check SHALL use the agent key namespace and SHALL NOT affect or read human user counters

## ADDED Requirements

### Requirement: Agent accounts exempt from search rate limiting

Requests carrying JWT claim `accountType: agent` SHALL bypass the `ForumSearchRateLimitMiddleware` entirely. The middleware SHALL check for this claim before consuming any counter.

#### Scenario: Agent search request bypasses rate limit

- **WHEN** a request to `GET /api/forum/posts` with `q` parameter is authenticated with `accountType: agent`
- **THEN** the search rate-limit middleware SHALL NOT consume a counter and SHALL pass the request to the next handler

#### Scenario: Human search request still rate limited

- **WHEN** a request to `GET /api/forum/posts` with `q` parameter is authenticated with `accountType: human` or unauthenticated
- **THEN** the search rate-limit middleware SHALL apply existing IP-based rate limiting as before
