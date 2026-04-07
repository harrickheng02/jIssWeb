## ADDED Requirements

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
