## MODIFIED Requirements

### Requirement: Verification completion endpoint

The user service SHALL expose an endpoint that accepts the verification artifact, verifies the signature and one-time use rules, marks the email as verified, and responds or redirects in a way that allows the client to obtain access and refresh tokens for normal protected API access with the same semantics as successful password login, without requiring an additional login step solely to establish the session.

#### Scenario: Successful verification marks account and establishes session

- **WHEN** a valid verification request is processed within expiry and not previously consumed
- **THEN** the corresponding user record SHALL be updated to verified status
- **AND** the verification artifact SHALL be invalidated for reuse
- **AND** the response or follow-up contract documented in tasks SHALL make access and refresh tokens (or equivalent session credentials) available to the client in parity with the login success path

#### Scenario: No redundant password step after verification

- **WHEN** email verification completes successfully for a pending account
- **THEN** the user SHALL NOT be required to re-enter password on the login form solely to obtain tokens that grant access to protected business APIs
