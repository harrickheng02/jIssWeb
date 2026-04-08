## ADDED Requirements

### Requirement: Centered auth card layout

The system SHALL provide a dedicated authentication page that centers a single card for login and registration flows, with a clear separation between header, form area, state switcher, and footer.

#### Scenario: Auth page opens on desktop
- **WHEN** a user opens the authentication page on a desktop viewport
- **THEN** the page SHALL render a centered card-based layout with the primary authentication actions contained inside the card

### Requirement: Auth page header and footer

The authentication page SHALL provide a lightweight header area for branding or navigation affordances and a footer area for agreement, privacy, and copyright information.

#### Scenario: Footer links visible
- **WHEN** a user views the authentication page
- **THEN** the page SHALL expose visible footer text or links for user agreement and privacy policy, with copyright or equivalent attribution

### Requirement: Login and registration state switch

The authentication page SHALL support switching between login and registration states within the same page context by tab or equivalent toggle, and MAY use a lightweight transition effect.

#### Scenario: User switches mode
- **WHEN** a user switches from login to registration or back
- **THEN** the form content SHALL update without requiring navigation to a different unrelated page

### Requirement: Real-time and blur validation feedback

Authentication form fields SHALL provide immediate or blur-triggered validation feedback for key inputs such as email format and password rules, and validation messages SHALL be shown inline beneath or attached to the relevant field.

#### Scenario: Invalid email shows inline error
- **WHEN** a user leaves an email field with an invalid value
- **THEN** the UI SHALL show a red inline validation message associated with that field

### Requirement: Button state clarity

Primary authentication buttons SHALL support default, loading, and disabled states based on whether the required inputs are complete and whether a request is in progress.

#### Scenario: Submit disabled while incomplete
- **WHEN** required fields or mandatory confirmations are not complete
- **THEN** the submit button SHALL be disabled or blocked from submission with clear feedback

### Requirement: Reserved extension points for recovery and third-party login

The authentication page SHALL reserve space for forgot-password and third-party login entry points, even if their backend workflows are not fully implemented in this change.

#### Scenario: Future entry placement is stable
- **WHEN** a designer or developer adds password recovery or third-party login later
- **THEN** the page structure SHALL already define where those actions belong without requiring a full layout rewrite

### Requirement: Responsive mobile behavior

The authentication page SHALL remain usable on mobile viewports, preserving readable typography, accessible tap targets, and vertically stacked controls where necessary.

#### Scenario: Mobile auth page remains usable
- **WHEN** a user opens the authentication page on a narrow mobile viewport
- **THEN** the card, inputs, tabs, and footer content SHALL reflow without clipping the primary actions
