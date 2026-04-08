## Purpose

Repository-local Docker Compose layout, env templates, and build-context hygiene.
## Requirements
### Requirement: Compose file location and naming

The repository SHALL include at least one Compose file at a documented path (e.g., repository root `docker-compose.yml` or `compose.yaml`) that can be used to start the dependency stack without additional undocumented steps.

#### Scenario: Discover compose file

- **WHEN** a new contributor opens the repository root
- **THEN** they SHALL find the Compose file or a pointer to it in the same change’s tasks or design documentation

### Requirement: Environment variable template

The change SHALL include an `.env.example` (or equivalent sample file) listing non-secret defaults for ports, image tags, and volume names where applicable, without committing real secrets.

#### Scenario: Copy env for local use

- **WHEN** a developer copies the example env file to a local `.env` following documentation
- **THEN** `docker compose` commands SHALL resolve variables consistently on their platform

### Requirement: Docker ignore for build contexts

If Dockerfiles are added that build from the repository tree, the repository SHALL include a `.dockerignore` that excludes `bin/`, `obj/`, `node_modules/`, `dist/`, and other large or sensitive paths from the build context.

#### Scenario: Build context excludes artifacts

- **WHEN** a Docker image build is run from the documented context
- **THEN** the context SHALL not include `node_modules` or `bin`/`obj` directories from the repository

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

