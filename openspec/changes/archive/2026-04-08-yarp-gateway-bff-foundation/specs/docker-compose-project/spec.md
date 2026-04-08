## ADDED Requirements

### Requirement: Compose includes gateway-tier services

The Docker Compose project SHALL provide service definitions or clearly documented placeholders for the gateway-tier components required by this architecture, including at least the YARP gateway and BFF when they are part of the runnable stack.

#### Scenario: Gateway tier visible in compose
- **WHEN** a developer inspects the Compose configuration for the architecture stack
- **THEN** they SHALL be able to identify how the gateway and BFF are started or where their inclusion is documented

### Requirement: Environment template covers gateway ports

The environment template SHALL include non-secret defaults for gateway and BFF ports, downstream service URLs, and related routing configuration needed for local development.

#### Scenario: Local env resolves gateway endpoints
- **WHEN** a developer copies the env template for local use
- **THEN** the compose and application settings SHALL resolve gateway-tier variables consistently
