## ADDED Requirements

### Requirement: Global forum role claim

Forum-related authorization SHALL use a single string claim named `forumRole` on the access token. Allowed values are exactly `member`, `moderator`, or `admin`. The claim represents a **global** forum role for the installation (no per-board scope in this requirement). User identity remains the `sub` claim; `forumRole` SHALL NOT replace `sub` as the user primary key.

#### Scenario: Valid forumRole values are accepted for parsing

- **WHEN** a validated access token includes `forumRole` with value `member`, `moderator`, or `admin`
- **THEN** resource services MAY use that value for forum governance authorization decisions

#### Scenario: Omitted forumRole defaults to member

- **WHEN** a validated access token does not include `forumRole`
- **THEN** resource services SHALL treat the effective forum role as `member` for authorization purposes

#### Scenario: Invalid forumRole rejects the request

- **WHEN** a validated access token includes `forumRole` with a value other than `member`, `moderator`, or `admin`
- **THEN** the resource service SHALL reject the request with HTTP 401
