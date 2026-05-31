## ADDED Requirements

### Requirement: Forum warning sanction notifies the target user

When a warning sanction is successfully created through the moderation flow, the Model service SHALL insert an in-app notification to the sanctioned user's `sub` with `Type = "ForumWarning"`, `ActorSubId` empty (system behavior), and a summary message that a community rule violation was recorded without disclosing moderator identity or report adjudication details.

#### Scenario: Warning creates notification

- **WHEN** a moderator successfully issues `type=warning` against user U
- **THEN** a notification SHALL exist with `RecipientSubId=U` and `Type=ForumWarning`

#### Scenario: Warning notification lists in inbox

- **WHEN** user U fetches their notification list after a warning
- **THEN** the item SHALL render with system actor labeling consistent with other system notifications
