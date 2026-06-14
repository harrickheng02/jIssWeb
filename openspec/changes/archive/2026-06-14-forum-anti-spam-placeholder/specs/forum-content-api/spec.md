## ADDED Requirements

### Requirement: Create post and reply anti-spam gates

Before persisting a new post via `POST /api/forum/posts` or a new reply via `POST /api/forum/posts/{postId}/replies`, the Model service SHALL enforce post/reply rate limits and blocked-word rules defined in `openspec/specs/forum-anti-spam-placeholder/spec.md`.

Processing order for create endpoints SHALL be: authentication → mute check (`BlockForumMuted`) → empty-field validation → rate-limit check → blocked-word check → remaining business validation → persist → rate-limit increment on success only.

Rate-limit rejection SHALL take precedence over blocked-word evaluation when both would apply.

#### Scenario: Create post consults anti-spam spec

- **WHEN** a client needs normative behavior for blocked words or create-post rate limits
- **THEN** the system SHALL treat `openspec/specs/forum-anti-spam-placeholder/spec.md` as the source of truth

#### Scenario: Create reply consults anti-spam spec

- **WHEN** a client needs normative behavior for blocked words or create-reply rate limits
- **THEN** the system SHALL treat `openspec/specs/forum-anti-spam-placeholder/spec.md` as the source of truth

#### Scenario: Rate limit before blocked word on same request

- **WHEN** a user exceeds the post create rate limit and also submits content that would hit a blocked word
- **THEN** the response SHALL be HTTP 429 with code `RATE_LIMITED`
