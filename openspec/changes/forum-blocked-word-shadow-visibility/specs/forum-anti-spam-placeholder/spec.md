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
