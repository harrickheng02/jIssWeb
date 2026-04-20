## ADDED Requirements

### Requirement: Shell exposes moderation entry for moderators and admins
The application shell SHALL expose a visible moderation entry point when the user is authenticated and has an effective forum role of moderator or admin.

#### Scenario: Moderator sees moderation entry
- **WHEN** the shell renders for an authenticated user whose effective forum role is moderator
- **THEN** the UI SHALL expose a moderation entry control that navigates to a moderation affordance (for example a post-detail moderation panel entry)

#### Scenario: Admin sees moderation entry
- **WHEN** the shell renders for an authenticated user whose effective forum role is admin
- **THEN** the UI SHALL expose the same moderation entry control

#### Scenario: Member does not see moderation entry
- **WHEN** the shell renders for an authenticated user whose effective forum role is member
- **THEN** the moderation entry control SHALL not be shown

