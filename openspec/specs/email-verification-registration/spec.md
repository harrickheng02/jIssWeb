# email-verification-registration Specification

## Purpose

Email-based registration, signed verification links, resend/rate limits, and verified-only login.
## Requirements
### Requirement: Email as sole account identifier

The user service SHALL treat email as the unique human-facing login identifier; duplicate normalized emails SHALL be rejected at registration.

#### Scenario: Duplicate email rejected

- **WHEN** a client submits registration with an email that already exists in normalized form
- **THEN** the service SHALL return a conflict or error response with a stable error code and SHALL NOT create a second account for the same email

### Requirement: Registration does not complete until email is verified

The system SHALL persist a pending registration state until the user completes email verification; until then the account SHALL NOT be treated as fully registered for normal authenticated API access.

#### Scenario: No full session before verification

- **WHEN** a user has submitted registration but not completed email verification
- **THEN** the service SHALL NOT issue access and refresh tokens that grant access to protected business APIs as defined in implementation tasks, OR SHALL issue only explicitly scoped tokens that cannot access those APIs

### Requirement: Signed verification links

Verification links SHALL be generated and validated only on the server using a cryptographic signature or HMAC over a bounded payload that includes expiration and purpose; client-provided payloads SHALL NOT be trusted without verification.

#### Scenario: Tampered link rejected

- **WHEN** a client presents a modified or replayed verification token after consumption or after expiration
- **THEN** the service SHALL reject verification and SHALL NOT mark the email as verified

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

### Requirement: Resend verification email

The user service SHALL expose a rate-limited endpoint to resend verification email for a pending account; this SHALL be one of the few operations allowed before verification completes.

#### Scenario: Resend rate limited

- **WHEN** resend requests exceed configured per-email or per-IP limits within a window
- **THEN** the service SHALL return HTTP 429 or equivalent with unified envelope and SHALL NOT enqueue unbounded outbound mail work

### Requirement: Abuse controls on registration and resend

Registration and resend endpoints SHALL be protected with application-level rate limiting backed by bounded Redis keys with TTL; limits SHALL be documented in tasks.

#### Scenario: Limits prevent unbounded Redis growth

- **WHEN** attackers send many registration or resend requests
- **THEN** counter or window keys SHALL expire automatically and SHALL NOT require storing every request permanently

### Requirement: Login requires verified email

Login with email and password SHALL succeed only if the account email is verified; otherwise the service SHALL return an error that allows the client to route to resend verification without issuing tokens.

#### Scenario: Unverified login blocked

- **WHEN** a user attempts login before email verification
- **THEN** the service SHALL not issue normal session tokens and SHALL return a response that distinguishes unverified state from bad credentials as agreed in tasks

