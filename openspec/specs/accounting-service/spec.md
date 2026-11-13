## Purpose

本规范为 `accounting-service` 能力的归档基线，汇总历次 OpenSpec 变更产出的 3 条需求及其验收场景（Scenario），作为后续变更的 MODIFIED / ADDED delta 的合并目标。

## Requirements

### Requirement: Accounting domain API shell

The accounting (账款) service SHALL expose a standalone Web API with Swagger in Development and separate launch profile port from other services.

#### Scenario: Independent run

- **WHEN** only the accounting API process is started
- **THEN** it SHALL listen on its configured URL without requiring other domain processes to be running

### Requirement: JWT validation

The accounting service SHALL use JWT bearer authentication with parameters aligned to the user service and SHALL not issue tokens.

#### Scenario: Authorized sample call

- **WHEN** a client presents a valid Bearer token to a protected placeholder controller
- **THEN** the request SHALL pass authentication

### Requirement: Health endpoint

The accounting service SHALL implement `GET /api/health` (or agreed equivalent) with the unified response format.

#### Scenario: Health returns OK

- **WHEN** the health endpoint is invoked
- **THEN** the HTTP status SHALL be 200 and the envelope SHALL indicate success
