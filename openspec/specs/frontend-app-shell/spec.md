## Purpose

Vue 3 SPA: dev proxy to backends, auth client, customer/profile flows, and verification UX.
## Requirements
### Requirement: SPA shell with router and state

The frontend SHALL use Vue 3 with TypeScript, Vite, Pinia, vue-router, and Element Plus, and SHALL mount a root layout that renders routed views.

#### Scenario: Dev server starts

- **WHEN** the developer runs the frontend dev script
- **THEN** the application SHALL load without runtime errors in the default view

### Requirement: Multi-service proxy configuration

The Vite development server SHALL define proxy rules mapping distinct path prefixes to each backend service base URL (ports per design), so that browser calls avoid CORS for same-origin paths during development.

#### Scenario: Proxy to user service

- **WHEN** the SPA requests a resource under the user-service proxy prefix
- **THEN** the request SHALL be forwarded to the user service host and port configured in `vite.config`

### Requirement: Authenticated API client

The frontend SHALL provide an HTTP client (axios or equivalent) that attaches `Authorization: Bearer <token>` when a token is present in application state, for calls to protected APIs.

#### Scenario: Token attached

- **WHEN** a token is stored after login placeholder flow and a protected API is invoked
- **THEN** the outgoing request SHALL include the Bearer header

### Requirement: Surface for service smoke checks

The frontend SHALL include at least one view or dev-only panel that can trigger requests to each backend health endpoint (or document manual steps in tasks if automated UI is deferred).

#### Scenario: Manual verification path

- **WHEN** a developer follows the tasks document to open the smoke view or run listed URLs
- **THEN** they SHALL be able to confirm each service responds successfully

### Requirement: Customer records UI entry

The frontend SHALL provide a routed view or section that performs authenticated calls to the customer service CRUD API (via the configured proxy prefix), including at least list and create (or equivalent minimal flow) to validate the login-to-customer pipeline.

#### Scenario: Authenticated customer list request

- **WHEN** a logged-in user opens the customer records view
- **THEN** the application SHALL request the customer list endpoint with `Authorization: Bearer` when a token is present

#### Scenario: Unauthenticated user cannot load protected customer data

- **WHEN** no token is present and the user attempts the same protected action
- **THEN** the UI SHALL avoid sending the request or SHALL handle 401 without exposing other users' data

### Requirement: Email verification routes

The SPA SHALL provide routed views for the email verification success path and for the pending-verification state that only allows resending verification email and static guidance, consistent with `email-verification-registration`. On successful verification, the SPA SHALL integrate returned session credentials the same way as after login so the user does not need a separate password entry step.

#### Scenario: Success view after verification

- **WHEN** the user completes verification via the backend flow and lands on the SPA success route with session credentials available per `email-verification-registration`
- **THEN** the UI SHALL persist tokens or equivalent credentials using the same client storage and auth state path as the login success flow
- **AND** the UI SHALL present a confirmation state and SHALL navigate to an authenticated destination without requiring navigation to the login password form solely to obtain a session

#### Scenario: Authenticated after verify without redundant login

- **WHEN** access and refresh tokens (or equivalent) are present after verification completion
- **THEN** subsequent navigation SHALL honor the same route guards as a post-login session for protected shell features allowed for verified users

### Requirement: Remember-me and silent refresh

The SPA SHALL support a user-controlled option to persist refresh tokens for automatic re-authentication on subsequent visits and SHALL attempt silent token refresh on startup when persisted credentials exist, as documented in `email-verification-profile-auth` design.

#### Scenario: Startup refresh attempt

- **WHEN** a refresh token is present in the chosen storage and remember-me semantics apply
- **THEN** the application SHALL request token refresh without requiring password entry on first load

### Requirement: Unverified users cannot access protected shell features

Until the account is verified, the SPA SHALL route users to the pending-verification experience and SHALL NOT call protected customer or profile APIs except where explicitly allowed by the user service for resend operations.

#### Scenario: Guarded navigation

- **WHEN** a logged-in user is unverified (if the client can detect this state)
- **THEN** the UI SHALL block or redirect away from protected business views until verification completes

### Requirement: Frontend uses unified backend entry

The frontend SHALL progressively use a unified backend entry domain or path model instead of directly depending on per-service development prefixes for long-term architecture.

#### Scenario: Frontend does not need downstream topology
- **WHEN** the SPA performs authenticated or domain API calls
- **THEN** the request model SHALL not require the browser code to know the concrete host or permanent public prefix of each backend service

### Requirement: Gateway or BFF aware client configuration

The frontend client configuration SHALL support routing requests through the gateway and, where needed, BFF endpoints, while preserving authenticated request behavior.

#### Scenario: Token still attached through unified entry
- **WHEN** a protected request is sent through the unified backend entry
- **THEN** the outgoing request SHALL still include the Bearer token when one is present in application state

### Requirement: Unified auth page shell

The frontend SHALL provide a unified authentication page shell that groups branding/header content, login or registration form content, state switching controls, and footer links into a consistent routed experience.

#### Scenario: Auth shell presents a single entry page
- **WHEN** a user lands on the authentication route
- **THEN** the UI SHALL present login and registration as coordinated states of the same page shell

### Requirement: Inline validation and request feedback

The frontend SHALL display inline field-level validation for authentication inputs and SHALL expose request-level feedback for server-side failures such as wrong password, missing account, invalid verification code, throttling, or captcha escalation when provided by the backend.

#### Scenario: Server error displayed clearly
- **WHEN** an authentication request fails with a known backend error code
- **THEN** the UI SHALL show an understandable error message without forcing the user to infer what went wrong

### Requirement: Authentication form affordances

The frontend SHALL support password visibility toggles, remember-me selection, forgot-password entry placement, agreement confirmation for registration, and loading or disabled submit buttons consistent with `auth-page-experience`.

#### Scenario: Registration requires agreement
- **WHEN** the user attempts registration without confirming the required agreement checkbox
- **THEN** the UI SHALL block submission and present clear feedback

