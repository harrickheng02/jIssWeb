# user-profile-record Specification

## Purpose
TBD - created by archiving change email-verification-profile-auth. Update Purpose after archive.
## Requirements
### Requirement: Single profile document per user

The customer profile service SHALL persist at most one profile document per authenticated user, keyed by `ownerUserId` equal to JWT `sub`; creation SHALL be idempotent upsert or explicit conflict handling.

#### Scenario: Second create conflicts or updates

- **WHEN** an authenticated user requests creation of a profile when one already exists for that `sub`
- **THEN** the service SHALL return conflict or SHALL apply update semantics per tasks, but SHALL NOT create a second profile row for the same owner

### Requirement: Profile separate from customer records

Profile resources SHALL be exposed under distinct routes from customer record CRUD (for example `api/profile` vs `api/customers`); profile documents SHALL NOT be stored as rows in the customer records collection used for multi-entity CRM-style customers.

#### Scenario: Customer list excludes profile-only data

- **WHEN** a client lists customer records
- **THEN** the response SHALL contain only customer business records and SHALL NOT substitute the user's personal profile document as a customer row unless explicitly defined as product behavior in tasks

### Requirement: Profile fields for extensibility

The profile model SHALL support at minimum display-oriented fields needed for registration completion flows (such as nickname, birth date, gender) and SHALL allow extension for future forum, blog, and recommendation features without collapsing into the customer-record schema.

#### Scenario: Authenticated read of own profile

- **WHEN** an authenticated user requests their profile
- **THEN** the service SHALL return the profile for `ownerUserId` equal to `sub` or create a default empty profile per policy

### Requirement: Profile API envelope

Profile endpoints SHALL use the shared `ApiResult` envelope consistent with `shared-foundation`.

#### Scenario: Error shape consistent

- **WHEN** a profile request fails validation
- **THEN** the response SHALL use `ApiResult` with `success: false` and a stable `code` when applicable

