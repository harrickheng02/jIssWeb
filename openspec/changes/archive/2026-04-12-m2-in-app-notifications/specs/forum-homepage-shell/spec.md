## ADDED Requirements

### Requirement: Authenticated header exposes notification entry

The forum homepage shell SHALL expose a visible notification entry control in the top header when the user is authenticated, linking to the in-app notification list view or route.

#### Scenario: Logged-in user sees notification entry

- **WHEN** the homepage shell renders for a user with a valid session token
- **THEN** the header SHALL include a control to open notifications (e.g. icon or label)

#### Scenario: Guest does not see notification entry

- **WHEN** the homepage shell renders without an authenticated session
- **THEN** the header SHALL NOT present the authenticated-only notification entry described above

### Requirement: Notification list view states

The application SHALL provide a notification list experience that can present loading, empty, and error outcomes for the notification list request, consistent with `in-app-notifications` empty and failure handling.

#### Scenario: Empty notifications

- **WHEN** the notification list API returns success with no items for the user
- **THEN** the UI SHALL show an empty state distinct from loading and error states

#### Scenario: Notification list failure

- **WHEN** the notification list request fails (non-success HTTP or client error)
- **THEN** the UI SHALL show a failure state that is distinct from the empty list state
