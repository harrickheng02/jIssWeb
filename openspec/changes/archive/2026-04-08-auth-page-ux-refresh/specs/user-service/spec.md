## ADDED Requirements

### Requirement: Stable authentication failure feedback

The user service SHALL return stable error codes or equivalent machine-readable outcomes for common authentication failures so the frontend can distinguish wrong credentials, missing account, throttling, verification-required states, or captcha escalation paths.

#### Scenario: Wrong password distinguished from throttling
- **WHEN** login fails because credentials are wrong and the caller is not rate-limited
- **THEN** the service SHALL return an authentication failure response that is distinguishable from rate limit or captcha-required responses

### Requirement: Login abuse protection integration

The user service SHALL support login failure counting, throttling, or equivalent brute-force mitigation behavior, and SHALL expose a response shape that allows the frontend to escalate to captcha or temporary blocking UX when policy requires it.

#### Scenario: Repeated failures trigger stronger response
- **WHEN** a client exceeds the configured login failure threshold for an identity or IP window
- **THEN** the service SHALL return a response that indicates throttling, temporary blocking, or captcha-required handling according to service policy
