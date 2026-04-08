## 1. Auth page structure

- [x] 1.1 Refactor the current auth route into a centered card-based page with explicit header, form area, mode switcher, and footer regions
- [x] 1.2 Add responsive layout rules for desktop and mobile widths, including spacing, typography, and tap-target sizing
- [x] 1.3 Add footer content placeholders for user agreement, privacy policy, and copyright information

## 2. Login and registration UX

- [x] 2.1 Update login fields to support the chosen identifier label, password visibility toggle, remember-me, forgot-password entry, and submit-state feedback
- [x] 2.2 Update registration fields to include password confirmation, agreement checkbox, and reserved verification-code area if needed by the chosen flow
- [x] 2.3 Implement blur or real-time inline validation with red field-level error text for email, password, and other required inputs
- [x] 2.4 Add tab or toggle transitions between login and registration without breaking form state expectations

## 3. User service coordination

- [x] 3.1 Add or standardize machine-readable authentication error codes for common failures used by the auth page
- [x] 3.2 Implement login failure counting and throttling or captcha-escalation signaling in the user service
- [x] 3.3 Ensure frontend maps backend security responses into clear request-level error messaging

## 4. Reserved auth extensions

- [x] 4.1 Add visible placeholders or disabled entries for forgot-password and third-party login actions
- [x] 4.2 Document which reserved actions are UI-only in this change versus backed by real API flows

## 5. Verification and rollout

- [x] 5.1 Manually verify desktop and mobile auth page behavior for login, registration, validation, loading, and disabled states
- [x] 5.2 Verify error messaging for wrong password, missing account, throttling, and validation failures
