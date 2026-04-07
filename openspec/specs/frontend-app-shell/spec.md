## ADDED Requirements

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
