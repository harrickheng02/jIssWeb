## MODIFIED Requirements

### Requirement: Email verification routes

The SPA SHALL provide routed views for the email verification success path and for the pending-verification state that only allows resending verification email and static guidance, consistent with `email-verification-registration`. On successful verification, the SPA SHALL integrate returned session credentials the same way as after login so the user does not need a separate password entry step.

#### Scenario: Success view after verification

- **WHEN** the user completes verification via the backend flow and lands on the SPA success route with session credentials available per `email-verification-registration`
- **THEN** the UI SHALL persist tokens or equivalent credentials using the same client storage and auth state path as the login success flow
- **AND** the UI SHALL present a confirmation state and SHALL navigate to an authenticated destination without requiring navigation to the login password form solely to obtain a session

#### Scenario: Authenticated after verify without redundant login

- **WHEN** access and refresh tokens (or equivalent) are present after verification completion
- **THEN** subsequent navigation SHALL honor the same route guards as a post-login session for protected shell features allowed for verified users
