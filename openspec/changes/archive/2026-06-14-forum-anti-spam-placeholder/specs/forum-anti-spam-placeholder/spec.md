## ADDED Requirements

### Requirement: Blocked words configuration

The system SHALL load blocked-word settings from configuration section `Forum:BlockedWords` with at least `Enabled` (boolean) and `Words` (string array). When `Enabled` is false or `Words` is empty or absent, blocked-word filtering SHALL be a no-op with no change to existing create-post or create-reply behavior.

#### Scenario: Empty word list behaves as no filter

- **WHEN** `Forum:BlockedWords:Enabled` is true but `Words` is empty
- **THEN** `POST /api/forum/posts` and `POST /api/forum/posts/{id}/replies` SHALL succeed under existing validation rules when content would have succeeded before this change

#### Scenario: Disabled blocked words

- **WHEN** `Forum:BlockedWords:Enabled` is false
- **THEN** blocked-word filtering SHALL NOT reject any request regardless of `Words` content

### Requirement: Blocked word matching on create post and reply

When blocked-word filtering is active (`Enabled` true and `Words` non-empty), the Model service SHALL evaluate content before persisting new posts or replies:

- For `POST /api/forum/posts`: scan trimmed `title` and trimmed `body`.
- For `POST /api/forum/posts/{id}/replies`: scan trimmed reply `body`.

Matching SHALL be case-insensitive substring match. If any configured word matches any scanned field, the request SHALL be rejected with HTTP **400**, unified error envelope, error code **`BLOCKED_CONTENT`**, and a generic message that SHALL NOT reveal which word matched.

#### Scenario: Post title hits blocked word

- **WHEN** a configured blocked word appears as a substring of the post title (any casing)
- **THEN** the response SHALL be HTTP 400 with code `BLOCKED_CONTENT`

#### Scenario: Post body hits blocked word

- **WHEN** a configured blocked word appears as a substring of the post body
- **THEN** the response SHALL be HTTP 400 with code `BLOCKED_CONTENT`

#### Scenario: Reply body hits blocked word

- **WHEN** a configured blocked word appears as a substring of the reply body
- **THEN** the response SHALL be HTTP 400 with code `BLOCKED_CONTENT`

#### Scenario: Clean content passes

- **WHEN** none of the configured words match any scanned field
- **THEN** the request SHALL proceed under existing authorization and validation rules

#### Scenario: Hit word not echoed

- **WHEN** a request is rejected for blocked content
- **THEN** the response message and body SHALL NOT include the matched blocked word or a substring hint sufficient to infer the word list

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

Limits SHALL use a sliding-window counter in-process (same algorithm family as forum search rate limiting). Primary counter keys SHALL be derived from the authenticated JWT `sub` (`post:{sub}` for posts, `reply:{sub}` for replies). Secondary counter keys SHALL be derived from client IP using the same IP resolution rules as search rate limiting (`X-Forwarded-For` first hop when present, else connection IP), formatted as `post:ip:{ip}` and `reply:ip:{ip}`. A request SHALL be rejected when **either** the primary or secondary counter for that operation type is at or over its limit **before** a successful persist.

Rate-limit evaluation SHALL occur after authentication and mute checks and **before** blocked-word evaluation when both would apply. Counters SHALL increment only after a successful persist.

When exceeded, the response SHALL be HTTP **429** with the unified error envelope and error code **`RATE_LIMITED`**.

#### Scenario: Under post quota succeeds

- **WHEN** an authenticated user creates posts at or below `MaxPosts` within `WindowSeconds` for both sub and IP keys
- **THEN** normal create-post responses apply

#### Scenario: Over post quota rejected

- **WHEN** an authenticated user exceeds `MaxPosts` within `WindowSeconds` on the sub or IP post counter
- **THEN** the response SHALL be HTTP 429 with code `RATE_LIMITED`

#### Scenario: Under reply quota succeeds

- **WHEN** an authenticated user creates replies at or below `MaxReplies` within `WindowSeconds` for both sub and IP keys
- **THEN** normal create-reply responses apply

#### Scenario: Over reply quota rejected

- **WHEN** an authenticated user exceeds `MaxReplies` within `WindowSeconds` on the sub or IP reply counter
- **THEN** the response SHALL be HTTP 429 with code `RATE_LIMITED`

#### Scenario: Post and reply counters are separate

- **WHEN** a user reaches `MaxPosts` but is under `MaxReplies` within the same window
- **THEN** further `POST /api/forum/posts` SHALL be rejected with 429
- **AND** `POST /api/forum/posts/{id}/replies` SHALL still succeed if under the reply quota

### Requirement: Out of scope for anti-spam placeholder

The following SHALL NOT apply blocked-word filtering or post/reply rate limits in this capability:

- Author self-edit (`PUT`) of posts or replies
- Draft create, update, or publish endpoints under `/api/forum/posts/drafts`
- Any moderation (`/api/mod/**`) or admin endpoints

#### Scenario: Draft publish not rate limited

- **WHEN** an authenticated user calls `POST /api/forum/posts/drafts/{draftId}/publish` repeatedly within `WindowSeconds`
- **THEN** requests SHALL NOT be rejected with HTTP 429 solely due to `Forum:PostRateLimit`

#### Scenario: Self-edit PUT not blocked-word filtered

- **WHEN** an author edits a published post via `PUT /api/forum/posts/{id}` with content containing a configured blocked word
- **THEN** the request SHALL NOT be rejected with code `BLOCKED_CONTENT` solely due to this capability
