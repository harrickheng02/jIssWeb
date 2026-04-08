## ADDED Requirements

### Requirement: Unified auth page shell

The frontend SHALL provide a unified authentication page shell that groups branding/header content, login or registration form content, state switching controls, and footer links into a consistent routed experience.

#### Scenario: Auth shell presents a single entry page
- **WHEN** a user lands on the authentication route
- **THEN** the UI SHALL present login and registration as coordinated states of the same page shell

### Requirement: Inline validation and request feedback

The frontend SHALL display inline field-level validation for authentication inputs and SHALL expose request-level feedback for server-side failures such as wrong password, missing account, invalid verification code, throttling, or captcha escalation when provided by the backend.

#### Scenario: Server error displayed clearly
- **WHEN** an authentication request fails with a known backend error code
- **THEN** the UI SHALL show an understandable error message without forcing the user to infer what went wrong

### Requirement: Authentication form affordances

The frontend SHALL support password visibility toggles, remember-me selection, forgot-password entry placement, agreement confirmation for registration, and loading or disabled submit buttons consistent with `auth-page-experience`.

#### Scenario: Registration requires agreement
- **WHEN** the user attempts registration without confirming the required agreement checkbox
- **THEN** the UI SHALL block submission and present clear feedback
