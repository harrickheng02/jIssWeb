## MODIFIED Requirements

### Requirement: Blocked words configuration

The system SHALL load blocked-word settings from configuration section `Forum:BlockedWords` with at least `Enabled` (boolean), `Words` (string array), and `Handling` (string: `reject` or `local`, default **`local`**). When `Enabled` is false or `Words` is empty or absent, blocked-word filtering SHALL be a no-op with no change to existing create-post or create-reply behavior.

#### Scenario: Empty word list behaves as no filter

- **WHEN** `Forum:BlockedWords:Enabled` is true but `Words` is empty
- **THEN** `POST /api/forum/posts` and `POST /api/forum/posts/{id}/replies` SHALL succeed under existing validation rules when content would have succeeded before this change

#### Scenario: Disabled blocked words

- **WHEN** `Forum:BlockedWords:Enabled` is false
- **THEN** blocked-word filtering SHALL NOT reject any request regardless of `Words` content

#### Scenario: Handling defaults to local

- **WHEN** `Forum:BlockedWords:Enabled` is true, `Words` is non-empty, and `Handling` is omitted from configuration
- **THEN** blocked-word hits on create post or reply SHALL follow local-only handling per `forum-blocked-word-local-only`

### Requirement: Blocked word matching on create post and reply

When blocked-word filtering is active (`Enabled` true and `Words` non-empty), the Model service SHALL evaluate content before persisting new posts or replies:

- For `POST /api/forum/posts`: scan trimmed `title` and trimmed `body`.
- For `POST /api/forum/posts/{id}/replies`: scan trimmed reply `body`.

Matching SHALL be case-insensitive substring match. If any configured word matches any scanned field, behavior SHALL depend on `Forum:BlockedWords:Handling`:

- When `Handling` is **`reject`**: reject with HTTP **400**, unified error envelope, error code **`BLOCKED_CONTENT`**, and a generic message that SHALL NOT reveal which word matched.
- When `Handling` is **`local`**: SHALL NOT persist the post or reply; return HTTP **2xx** with unified success envelope including **`localOnly: true`**, a routable **`id`** with `local:` prefix, and **`state: "local"`** per `forum-blocked-word-local-only`; SHALL NOT increment post/reply rate-limit counters.

When no configured word matches, the request SHALL proceed under existing authorization and validation rules with normal server persistence and `State: "published"`.

#### Scenario: Post title hits blocked word with reject handling

- **WHEN** `Handling` is `reject` and a configured blocked word appears as a substring of the post title (any casing)
- **THEN** the response SHALL be HTTP 400 with code `BLOCKED_CONTENT`

#### Scenario: Post body hits blocked word with local handling

- **WHEN** `Handling` is `local` and a configured blocked word appears as a substring of the post body
- **THEN** the Model service SHALL NOT insert a post document
- **AND** the response SHALL be HTTP 2xx with `localOnly: true` and an id starting with `local:`

#### Scenario: Reply body hits blocked word with reject handling

- **WHEN** `Handling` is `reject` and a configured blocked word appears as a substring of the reply body
- **THEN** the response SHALL be HTTP 400 with code `BLOCKED_CONTENT`

#### Scenario: Reply body hits blocked word with local handling

- **WHEN** `Handling` is `local` and a configured blocked word appears as a substring of the reply body
- **THEN** the Model service SHALL NOT insert a reply document
- **AND** the response SHALL be HTTP 2xx with `localOnly: true`

#### Scenario: Clean content passes

- **WHEN** none of the configured words match any scanned field
- **THEN** the request SHALL proceed under existing authorization and validation rules with `State: "published"`

#### Scenario: Hit word not echoed on reject

- **WHEN** a request is rejected for blocked content under `Handling: reject`
- **THEN** the response message and body SHALL NOT include the matched blocked word or a substring hint sufficient to infer the word list

#### Scenario: Hit word not echoed on local

- **WHEN** a request is handled as local-only due to blocked content
- **THEN** the response message and body SHALL NOT include the matched blocked word or an explanation of blocked-word interception

#### Scenario: Local create does not consume rate limit

- **WHEN** `Handling` is `local` and a create post or reply matches a blocked word
- **THEN** post/reply rate-limit counters SHALL NOT increment for that request

## ADDED Requirements

### Requirement: Blocked word matching on draft publish

When blocked-word filtering is active (`Enabled` true and `Words` non-empty), the Model service SHALL evaluate the draft's title and body before publishing it. The evaluation SHALL use the same case-insensitive substring matching as create-post. On a match, the publish request SHALL be rejected with HTTP **400**, unified error envelope, and error code **`BLOCKED_CONTENT`**, regardless of the current `Forum:BlockedWords:Handling` value (publish has no meaningful local-only semantic because the draft content is already persisted).

The blocked-word check for publish SHALL occur after authentication, mute check, draft existence and ownership check, and required-field validation, and before the rate-limit check.

#### Scenario: Draft publish blocked by word in title

- **WHEN** `Forum:BlockedWords:Enabled` is true, `Words` is non-empty, and the draft's stored title contains a configured word (any casing)
- **THEN** `POST /api/forum/posts/drafts/{draftId}/publish` SHALL return HTTP 400 with code `BLOCKED_CONTENT`
- **AND** the draft's `State` SHALL remain `"draft"`

#### Scenario: Draft publish blocked by word in body

- **WHEN** `Forum:BlockedWords:Enabled` is true, `Words` is non-empty, and the draft's stored body contains a configured word
- **THEN** `POST /api/forum/posts/drafts/{draftId}/publish` SHALL return HTTP 400 with code `BLOCKED_CONTENT`

#### Scenario: Draft publish not blocked when word list disabled

- **WHEN** `Forum:BlockedWords:Enabled` is false
- **THEN** `POST /api/forum/posts/drafts/{draftId}/publish` SHALL NOT be rejected with code `BLOCKED_CONTENT` solely due to blocked-word configuration

#### Scenario: Draft publish with clean content succeeds

- **WHEN** `Forum:BlockedWords:Enabled` is true, `Words` is non-empty, and neither the draft's title nor body contains any configured word
- **THEN** `POST /api/forum/posts/drafts/{draftId}/publish` SHALL proceed to rate-limit evaluation and, if not rate-limited, succeed with HTTP 200 and `State: "published"`

### Requirement: Rate limiting on draft publish

The Model service SHALL apply the existing post rate-limit check to `POST /api/forum/posts/drafts/{draftId}/publish`, sharing the same `post:{sub}` and `post:ip:{ip}` counter keys and `Forum:PostRateLimit:MaxPosts` / `WindowSeconds` configuration used by `POST /api/forum/posts`.

The rate-limit evaluation for publish SHALL occur after the blocked-word check and before the Mongo state transition. Counters SHALL increment only after a successful publish (HTTP 200 response and `State` transition to `"published"`).

#### Scenario: Publish counted against create quota

- **WHEN** an authenticated user successfully publishes a draft via `POST /api/forum/posts/drafts/{draftId}/publish`
- **THEN** the `post:{sub}` and `post:ip:{ip}` counters SHALL increment by 1, sharing the same window as direct-create posts

#### Scenario: Publish blocked when post quota exhausted

- **WHEN** an authenticated user has already created or published posts at or above `MaxPosts` within `WindowSeconds`
- **THEN** `POST /api/forum/posts/drafts/{draftId}/publish` SHALL return HTTP 429 with code `RATE_LIMITED`

#### Scenario: Blocked publish does not increment counter

- **WHEN** a publish attempt is rejected due to blocked content (HTTP 400) or any other error before the Mongo state transition
- **THEN** the `post:{sub}` and `post:ip:{ip}` counters SHALL NOT increment

### Requirement: Blocked word matching on post self-edit

When blocked-word filtering is active (`Enabled` true and `Words` non-empty), the Model service SHALL evaluate the new title and/or body submitted to `PUT /api/forum/posts/{postId}` before persisting the edit. On a match, the request SHALL be rejected with HTTP **400**, unified error envelope, and error code **`BLOCKED_CONTENT`**, regardless of `Forum:BlockedWords:Handling` (the local-only semantic does not apply to self-edit; the content is already stored and an edit that contains a blocked word is rejected outright to prevent silent corruption of stored content).

Only fields provided in the request body are evaluated: if `title` is absent the existing title is not re-evaluated; if `body` is absent the existing body is not re-evaluated.

#### Scenario: Post self-edit blocked by word in new title

- **WHEN** `Forum:BlockedWords:Enabled` is true, a configured word appears in the submitted `title` field (any casing), and the author sends `PUT /api/forum/posts/{postId}`
- **THEN** the response SHALL be HTTP 400 with code `BLOCKED_CONTENT`
- **AND** the stored post title SHALL remain unchanged

#### Scenario: Post self-edit blocked by word in new body

- **WHEN** `Forum:BlockedWords:Enabled` is true, a configured word appears in the submitted `body` field
- **THEN** the response SHALL be HTTP 400 with code `BLOCKED_CONTENT`
- **AND** the stored post body SHALL remain unchanged

#### Scenario: Post self-edit with only tags update not blocked

- **WHEN** `Forum:BlockedWords:Enabled` is true, the request contains only a `tags` update (no `title` or `body` fields), and the existing stored title and body contain a configured word
- **THEN** the response SHALL NOT be rejected with code `BLOCKED_CONTENT` (only submitted fields are evaluated)

#### Scenario: Post self-edit passes when new content is clean

- **WHEN** `Forum:BlockedWords:Enabled` is true and neither the submitted title nor body contains a configured word
- **THEN** `PUT /api/forum/posts/{postId}` SHALL succeed under existing authorization and validation rules

### Requirement: Blocked word matching on reply self-edit

When blocked-word filtering is active (`Enabled` true and `Words` non-empty), the Model service SHALL evaluate the new `body` submitted to `PUT /api/forum/posts/{postId}/replies/{replyId}` before persisting the edit. On a match, the request SHALL be rejected with HTTP **400**, unified error envelope, and error code **`BLOCKED_CONTENT`**, regardless of `Forum:BlockedWords:Handling`.

#### Scenario: Reply self-edit blocked by word in new body

- **WHEN** `Forum:BlockedWords:Enabled` is true and a configured word appears in the submitted reply body
- **THEN** `PUT /api/forum/posts/{postId}/replies/{replyId}` SHALL return HTTP 400 with code `BLOCKED_CONTENT`
- **AND** the stored reply body SHALL remain unchanged

#### Scenario: Reply self-edit passes when new body is clean

- **WHEN** `Forum:BlockedWords:Enabled` is true and the submitted body contains no configured word
- **THEN** the request SHALL succeed under existing authorization and validation rules

### Requirement: Post and reply rate limit configuration

The system SHALL expose configuration section `Forum:PostRateLimit`, separate from `Forum:SearchRateLimit`, with at least:

- `MaxPosts` — maximum successful post-create attempts per window per key (default **10**)
- `MaxReplies` — maximum successful reply-create attempts per window per key (default **30**)
- `WindowSeconds` — sliding window length in seconds (default **60**)

Only **successful** persists (HTTP 2xx after insert) SHALL increment counters. Failed requests (validation errors, blocked content, mute, not found, etc.) SHALL NOT increment counters.

Search rate-limit settings SHALL NOT affect post or reply create quotas.

#### Scenario: Config sections are independent

- **WHEN** an operator sets a low `Forum:SearchRateLimit:MaxRequests` and default post rate limits
- **THEN** post and reply create endpoints SHALL still allow creates up to `MaxPosts` / `MaxReplies` within `WindowSeconds` for the same user

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

## MODIFIED Requirements

### Requirement: Out of scope for anti-spam placeholder

The following SHALL NOT apply blocked-word filtering or post/reply rate limits in this capability:

- Any moderation (`/api/mod/**`) or admin endpoints

The following **previously excluded** paths are now covered by the added requirements above and are **no longer excluded**:

- `POST /api/forum/posts/drafts/{draftId}/publish` — now covered by blocked-word and rate-limit requirements above
- `PUT /api/forum/posts/{postId}` (author self-edit) — now covered by blocked-word requirement above
- `PUT /api/forum/posts/{postId}/replies/{replyId}` (author reply self-edit) — now covered by blocked-word requirement above

#### Scenario: Mod endpoints not rate limited

- **WHEN** a moderator calls any endpoint under `/api/mod/**`
- **THEN** the forum post rate limiter and blocked-word filter SHALL NOT apply

### Requirement: Agent accounts exempt from search rate limiting

Requests carrying JWT claim `accountType: agent` SHALL bypass the `ForumSearchRateLimitMiddleware` entirely. The middleware SHALL check for this claim before consuming any counter.

#### Scenario: Agent search request bypasses rate limit

- **WHEN** a request to `GET /api/forum/posts` with `q` parameter is authenticated with `accountType: agent`
- **THEN** the search rate-limit middleware SHALL NOT consume a counter and SHALL pass the request to the next handler

#### Scenario: Human search request still rate limited

- **WHEN** a request to `GET /api/forum/posts` with `q` parameter is authenticated with `accountType: human` or unauthenticated
- **THEN** the search rate-limit middleware SHALL apply existing IP-based rate limiting as before
