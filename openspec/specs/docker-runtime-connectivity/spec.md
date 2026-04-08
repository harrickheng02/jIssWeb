## Purpose

Document connection strings for host vs in-network Docker runs.
## Requirements
### Requirement: Document host vs bridge networking

The design or tasks SHALL document how to set MongoDB and Redis connection strings when APIs run on the host machine while databases run in Docker with published ports, versus when APIs run in the same Compose network as databases.

#### Scenario: Host APIs connect to published ports

- **WHEN** APIs run on the host and databases run in Compose with ports published to localhost
- **THEN** the documented connection strings SHALL use `localhost` (or `127.0.0.1`) and the published ports

#### Scenario: In-network service names

- **WHEN** APIs run as containers in the same Compose project as MongoDB and Redis
- **THEN** the documented connection strings SHALL use Compose service names as hostnames and internal ports, not `localhost` for cross-container access

### Requirement: No mandatory application code change

This Docker capability SHALL NOT require changing existing API route contracts or JWT behavior; only configuration, environment variables, or optional launch documentation may be added.

#### Scenario: Existing health endpoints unchanged

- **WHEN** the Docker stack is used
- **THEN** existing HTTP health and sample endpoints SHALL remain callable without semantic change solely due to this Docker configuration

### Requirement: Document ingress chain connectivity

The architecture documentation and configuration SHALL describe how requests traverse the ingress chain from Nginx to YARP, from YARP to BFF or downstream services, and from BFF to business services in both host-run and container-network scenarios.

#### Scenario: Developer understands each hop
- **WHEN** a developer configures the stack locally or in Docker
- **THEN** they SHALL be able to identify the expected hostnames, ports, and routing hop for Nginx, YARP, BFF, and downstream services

### Requirement: Service-name routing in container mode

When the gateway, BFF, and downstream services run inside the same Compose network, their route and upstream configuration SHALL use service names and internal ports rather than localhost-based assumptions.

#### Scenario: In-network gateway resolves service names
- **WHEN** the architecture stack runs fully inside Compose
- **THEN** YARP and BFF upstream targets SHALL reference Compose service names for cross-container communication

