## ADDED Requirements

### Requirement: Subject-first identity claim contract
JWT payload MUST use `sub` as the primary and authoritative user identifier across services.

#### Scenario: Resource service resolves identity
- **WHEN** a resource service receives a valid bearer token
- **THEN** it MUST resolve user identity from `sub` as the primary key

### Requirement: userId semantic alias consistency
If `userId` claim is present, it MUST be exactly equal to `sub`.

#### Scenario: Token includes both claims
- **WHEN** a token contains both `sub` and `userId`
- **THEN** `userId` MUST equal `sub` with exact string equality

#### Scenario: Token includes only sub
- **WHEN** a token contains `sub` and omits `userId`
- **THEN** the token MUST still be considered structurally valid for identity extraction

### Requirement: Mismatch handling
Tokens with missing `sub`, or with `userId` present but not equal to `sub`, MUST be rejected as invalid identity tokens.

#### Scenario: Claim mismatch
- **WHEN** a token has `sub` and `userId` with different values
- **THEN** the service MUST reject the request with HTTP 401

### Requirement: User-scoped reads use server-derived sub only

Any endpoint that returns resources filtered to the authenticated caller's ownership or to a user identity SHALL derive the effective user primary key only from the validated JWT `sub`, in conformance with `openspec/specs/shared-foundation` identity parsing.

#### Scenario: Implicit current-user list has no client user id

- **WHEN** an endpoint is documented as listing resources for the current user only (for example current user's posts or replies)
- **THEN** the service SHALL NOT require the client to supply a user identifier to select the owner filter
- **AND** the filter SHALL use only `sub` from the validated token

#### Scenario: Client-supplied user id cannot override sub

- **WHEN** a request includes a path or query parameter that identifies a user for filtering user-scoped data
- **AND** the value is not equal to the caller's `sub` (exact string equality)
- **THEN** the service MUST NOT return private or user-scoped data for that identifier
- **AND** the response SHALL be HTTP 403 or 404 according to the service's uniform policy for unauthorized resource access

#### Scenario: Valid match allows query

- **WHEN** a request includes a user identifier parameter equal to `sub`
- **THEN** the service MAY apply the filter as for the current user

### Requirement: Global forum role claim

Forum-related authorization SHALL use a single string claim named `forumRole` on the access token. Allowed values are exactly `member`, `moderator`, or `admin`. User identity remains the `sub` claim; `forumRole` SHALL NOT replace `sub` as the user primary key. Per-board scope for moderators is carried in `forumBoardIds` (see below).

#### Scenario: Valid forumRole values are accepted for parsing

- **WHEN** a validated access token includes `forumRole` with value `member`, `moderator`, or `admin`
- **THEN** resource services MAY use that value for forum governance authorization decisions

#### Scenario: Omitted forumRole defaults to member

- **WHEN** a validated access token does not include `forumRole`
- **THEN** resource services SHALL treat the effective forum role as `member` for authorization purposes

#### Scenario: Invalid forumRole rejects the request

- **WHEN** a validated access token includes `forumRole` with a value other than `member`, `moderator`, or `admin`
- **THEN** the resource service SHALL reject the request with HTTP 401

### Requirement: Moderator board scope claim

When `forumRole` is `moderator`, the user-service SHALL issue a claim `forumBoardIds` with value a JSON array of board id strings (same ids as `Forum:Boards[].Id` on model-service, e.g. `["general","tech"]`). Configuration source on user-service is `Forum:Moderation:Moderators`. Model-service prefers a non-empty `forumBoardIds` list from the token; it MAY use model-service `Forum:Moderation:Moderators` when the claim is absent (legacy tokens) or when the claim is an empty array (transition / duplicate configuration).

#### Scenario: Invalid forumBoardIds rejects the request

- **WHEN** a validated access token has `forumRole` `moderator` and includes `forumBoardIds` that is not a JSON array of strings
- **THEN** the resource service SHALL reject the request with HTTP 401

#### Scenario: Admin omits board list

- **WHEN** `forumRole` is `admin`
- **THEN** `forumBoardIds` MAY be omitted
