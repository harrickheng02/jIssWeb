# yarp-api-gateway Specification

## Purpose
TBD - created by archiving change yarp-gateway-bff-foundation. Update Purpose after archive.
## Requirements
### Requirement: Unified gateway entry

The system SHALL provide a YARP-based API Gateway process that receives inbound application API traffic and forwards requests to downstream services according to configured routes and clusters.

#### Scenario: Gateway forwards to downstream service
- **WHEN** a client sends a request to a configured gateway route
- **THEN** the gateway SHALL forward that request to the mapped downstream service and return the downstream response

### Requirement: Gateway preserves authentication context

The gateway SHALL preserve or forward authentication headers and request context needed by downstream services, without requiring the frontend to know direct service addresses.

#### Scenario: Bearer token forwarded
- **WHEN** a client sends an authenticated request through the gateway with `Authorization: Bearer`
- **THEN** the gateway SHALL pass the authentication header to the downstream route unless an explicit policy overrides it

### Requirement: Gateway route governance

The gateway SHALL define route and cluster configuration in a maintainable form that can be updated per environment for local development, container networking, and future production deployment.

#### Scenario: Environment-specific upstreams
- **WHEN** the application runs in a different environment such as local host mode or Docker bridge mode
- **THEN** the gateway SHALL be configurable to target the appropriate downstream hosts without changing frontend code

### Requirement: Gateway forwards forum API to model service

The gateway SHALL register a reverse-proxy route that forwards HTTP requests under `/api/forum` to the model service cluster destination used for forum content.

#### Scenario: Forum path reaches model service

- **WHEN** a client sends a request to `/api/forum/{**remainder}` through the gateway
- **THEN** the gateway SHALL forward the request to the configured model service base address preserving path and query, and SHALL forward `Authorization` when present

#### Scenario: Forum route configurable per environment

- **WHEN** the deployment target uses Docker bridge or different hostnames for the model service
- **THEN** the forum route cluster destination SHALL be configurable without changing frontend request paths

