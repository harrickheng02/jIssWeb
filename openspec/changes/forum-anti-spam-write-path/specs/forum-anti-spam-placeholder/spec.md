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
