## ADDED Requirements

### Requirement: Shell SHALL expose personal center entry when authenticated

The application shell SHALL expose a visible navigation entry to the personal center hub route (for example `/me`) when the user is authenticated, in addition to any existing profile or customer entries.

#### Scenario: Logged-in user sees personal center entry

- **WHEN** the shell renders for a user with a valid session token
- **THEN** the user menu or equivalent header region SHALL include a control labeled or clearly identifiable as personal center that navigates to the hub route

#### Scenario: Guest does not see personal center entry

- **WHEN** the shell renders without an authenticated session
- **THEN** the shell SHALL NOT present the authenticated-only personal center entry described above

### Requirement: Router SHALL register protected personal center routes

The SPA router SHALL register the personal center hub and its child or sibling routes used for my posts, my replies, my favorites, and settings (or equivalent structure), each protected with the same `requiresAuth` meta or equivalent guard as other authenticated views.

#### Scenario: Deep link to my favorites requires auth

- **WHEN** a guest navigates directly to a protected personal center child route
- **THEN** the router SHALL redirect or block access consistent with existing `requiresAuth` behavior
