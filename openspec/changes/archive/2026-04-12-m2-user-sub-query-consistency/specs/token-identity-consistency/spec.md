## ADDED Requirements

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
