## ADDED Requirements

### Requirement: Model domain API shell

The model (模型) service SHALL provide a runnable Web API with Swagger in Development and SHALL register MongoDB and Redis clients via dependency injection using service-specific configuration keys.

#### Scenario: Service starts with infrastructure registration

- **WHEN** the application starts with valid connection strings in configuration
- **THEN** resolution of MongoDB and Redis clients SHALL not throw during host build for skeleton registration

### Requirement: JWT validation

The model service SHALL validate Bearer tokens from the user service and SHALL not implement token issuance.

#### Scenario: Invalid token rejected

- **WHEN** a client calls a protected route with an expired or malformed JWT
- **THEN** the response SHALL be 401

### Requirement: Health endpoint

The model service SHALL expose a health check endpoint returning the unified `ApiResult` shape.

#### Scenario: Health success

- **WHEN** a client requests the health endpoint
- **THEN** `success` in the JSON body SHALL be true
