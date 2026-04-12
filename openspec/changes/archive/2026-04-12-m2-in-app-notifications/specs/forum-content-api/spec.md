## ADDED Requirements

### Requirement: Successful reply create satisfies in-app notification delivery

The forum API implementation hosted on the model service SHALL ensure that a successful reply creation triggers in-app notification persistence for the post author when required by `openspec/specs/in-app-notifications`, including suppression when the reply author is the post author.

#### Scenario: Forum reply contract implies notification side effect

- **WHEN** a client completes `POST` reply creation with 2xx and a persisted reply
- **THEN** the system's notification state SHALL match the scenarios in `in-app-notifications` for reply-to-post delivery
