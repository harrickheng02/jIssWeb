## ADDED Requirements

### Requirement: Gateway forwards forum API to model service

The gateway SHALL register a reverse-proxy route that forwards HTTP requests under `/api/forum` to the model service cluster destination used for forum content.

#### Scenario: Forum path reaches model service

- **WHEN** a client sends a request to `/api/forum/{**remainder}` through the gateway
- **THEN** the gateway SHALL forward the request to the configured model service base address preserving path and query, and SHALL forward `Authorization` when present

#### Scenario: Forum route configurable per environment

- **WHEN** the deployment target uses Docker bridge or different hostnames for the model service
- **THEN** the forum route cluster destination SHALL be configurable without changing frontend request paths
