## ADDED Requirements

### Requirement: Internal forum sanction APIs for service-to-service use

The User service SHALL provide internal HTTP endpoints for forum sanction status query and sanction record creation/revocation invoked only by trusted peer services using a configured shared internal API key. These endpoints SHALL NOT be exposed through the public gateway routes documented for browser clients. Sanction creation SHALL NOT alter JWT issuance claims in this change; login and refresh behavior SHALL remain unchanged.

#### Scenario: Internal endpoints reject browser-facing gateway paths in documentation

- **WHEN** deployment configures gateway public routes
- **THEN** internal sanction routes SHALL be reachable only from configured Model service base URL or equivalent private network path

#### Scenario: JWT issuance unchanged for muted user

- **WHEN** a muted user refreshes their access token
- **THEN** the issued token SHALL follow existing `forumRole` rules without embedding mute state in claims
