## ADDED Requirements

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
