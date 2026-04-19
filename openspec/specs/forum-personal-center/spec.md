# forum-personal-center Specification

## Purpose
TBD - created by archiving change m2-forum-personal-center-minimal. Update Purpose after archive.
## Requirements
### Requirement: Authenticated personal center hub route

The SPA SHALL expose at least one authenticated route (for example `/me`) that acts as a personal center hub from which the user can navigate to my posts, my replies, my favorites, profile (or equivalent profile maintenance), and a settings or placeholder section.

#### Scenario: Guest redirected from personal center

- **WHEN** a user without a valid session navigates to the personal center hub route
- **THEN** the application SHALL apply the same authentication guard behavior as other `requiresAuth` routes (for example redirect to auth)

#### Scenario: Authenticated user sees hub navigation

- **WHEN** a logged-in user opens the personal center hub
- **THEN** the UI SHALL present navigation affordances to my posts, my replies, my favorites, and profile or settings areas described in this capability

### Requirement: My posts list experience

The personal center SHALL include a view that loads the current user's posts using the authenticated forum API (`GET /api/forum/me/posts` or documented equivalent), with pagination, loading state, empty state when the user has no posts, and a distinct error state when the request fails.

#### Scenario: My posts uses server-side identity only

- **WHEN** the my posts view loads data
- **THEN** the client SHALL NOT rely on a client-supplied user identifier to choose the owner filter beyond what the backend derives from the Bearer token per `openspec/specs/token-identity-consistency`

#### Scenario: My posts empty state

- **WHEN** the my posts API returns success with zero items for the user
- **THEN** the UI SHALL show an empty state distinct from loading and error states

### Requirement: My replies list experience

The personal center SHALL include a view that loads the current user's replies using the authenticated forum API (`GET /api/forum/me/replies` or documented equivalent), with pagination, loading state, empty state, and error state distinct from empty.

#### Scenario: Reply links to originating post when identifiers exist

- **WHEN** a reply list item includes a post identifier suitable for deep linking
- **THEN** the UI SHALL provide navigation to that post's detail view

### Requirement: My favorites list experience

The personal center SHALL include a view that loads the current user's favorited posts using `GET /api/forum/me/favorites` (or the documented equivalent path under `openspec/specs/forum-post-like-favorite`), with pagination, loading state, empty state, and error state distinct from empty.

#### Scenario: Favorites list item contract

- **WHEN** the favorites list returns items for display
- **THEN** each item SHALL use the same post summary field contract as the public posts list where applicable, or a documented field mapping demonstrable in review

#### Scenario: Unauthenticated favorites view

- **WHEN** a session expires while viewing my favorites and a refetch occurs without a valid token
- **THEN** the application SHALL surface failure consistent with other authenticated forum calls (for example 401 handling or redirect to auth)

### Requirement: Profile integration from personal center

The personal center SHALL provide a clear path for the user to open the existing profile maintenance experience (same routes and APIs as today, for example `/profile` with `getProfile` / `updateProfile`), without introducing a second conflicting profile editor unless explicitly documented.

#### Scenario: Navigate to profile from hub

- **WHEN** the user selects profile from the personal center navigation
- **THEN** the application SHALL navigate to the established profile route or render the same profile editor component used elsewhere

### Requirement: Settings or placeholder section

The personal center SHALL expose a settings area or tab that includes at minimum a demonstrable sign-out path consistent with the rest of the app, and either a user-visible theme control if the shell already supports theme switching, or an explicit placeholder indicating deferred settings, plus distinguishable empty or failure messaging where applicable.

#### Scenario: Sign-out reachable from personal center

- **WHEN** an authenticated user opens the settings or hub area
- **THEN** they SHALL be able to trigger logout through an affordance documented in tasks (reusing existing auth store or header logout behavior)

