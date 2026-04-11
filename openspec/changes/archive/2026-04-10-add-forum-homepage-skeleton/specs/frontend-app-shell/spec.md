## ADDED Requirements

### Requirement: Root route serves the forum homepage
The frontend SHALL use the root route as the default forum homepage entry and SHALL keep authentication available on a dedicated routed page.

#### Scenario: Root route loads content shell
- **WHEN** the application resolves the root route
- **THEN** it SHALL render the forum homepage view

#### Scenario: Authentication remains available
- **WHEN** a user navigates to the authentication route
- **THEN** the application SHALL render the unified login and registration page shell

### Requirement: Existing protected routes remain guarded after homepage split
The frontend SHALL preserve route-guard behavior for protected pages after moving authentication away from the root route.

#### Scenario: Unauthenticated user opens protected page
- **WHEN** a user without a token navigates to a protected route such as customer or profile pages
- **THEN** the router SHALL block access and redirect to the authentication route instead of rendering protected content
