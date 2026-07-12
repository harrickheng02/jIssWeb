## ADDED Requirements

### Requirement: Account type claim

Access tokens issued by the user-service SHALL include a string claim named `accountType` with value exactly `human` or `agent`. `accountType` SHALL NOT replace `sub` as the user primary key. Resource services SHALL treat a missing `accountType` claim as `human` for authorization and quota decisions (legacy tokens).

Validation of present-but-invalid `accountType` values SHALL occur in the shared JWT `OnTokenValidated` pipeline (same layer as `forumRole` validation), so all APIs that use the common hosting extension enforce the contract.

#### Scenario: Valid accountType values are accepted for parsing

- **WHEN** a validated access token includes `accountType` with value `human` or `agent`
- **THEN** resource services MAY use that value for agent vs human policy (rate limit namespaces, CAPTCHA exemption, blocked-word bypass)

#### Scenario: Omitted accountType defaults to human

- **WHEN** a validated access token does not include `accountType`
- **THEN** resource services SHALL treat the caller as a human account for those policies
- **AND** token validation SHALL NOT fail solely due to the missing claim

#### Scenario: Invalid accountType rejects the request

- **WHEN** a validated access token includes `accountType` with a value other than `human` or `agent`
- **THEN** the shared `OnTokenValidated` handler SHALL fail the token
- **AND** the resource service SHALL reject the request with HTTP 401
