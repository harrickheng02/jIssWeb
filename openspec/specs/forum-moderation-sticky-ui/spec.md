# forum-moderation-sticky-ui Specification

## Purpose

版主帖子详情置顶/操作记录等前端行为与后端 `POST /api/mod/posts/{postId}/sticky` 等对齐。手工验收步骤见归档：`openspec/changes/archive/2026-04-20-frontend-moderation-sticky-ui-minset/manual-qa.md`。
## Requirements
### Requirement: Frontend derives effective forum role from JWT
The frontend SHALL derive an effective forum role from the access token claim `forumRole` for use in UI gating of moderation controls.

#### Scenario: Token with moderator role enables moderation UI
- **WHEN** a user has a valid access token whose `forumRole` claim equals `moderator`
- **THEN** the UI SHALL treat the user as a moderator for the purposes of showing moderation controls

#### Scenario: Token with admin role enables moderation UI
- **WHEN** a user has a valid access token whose `forumRole` claim equals `admin`
- **THEN** the UI SHALL treat the user as an admin for the purposes of showing moderation controls

#### Scenario: Missing forumRole behaves as member
- **WHEN** a user has a valid access token that omits `forumRole`
- **THEN** the UI SHALL treat the user as a member for the purposes of showing moderation controls

### Requirement: Post detail exposes sticky moderation controls to moderators and admins
The frontend SHALL expose sticky moderation actions on the post detail view for moderators and admins, and SHALL call the backend moderation endpoint to perform the action.

#### Scenario: Moderator toggles sticky on a post
- **WHEN** a user whose effective forum role is moderator or admin views a post detail page
- **THEN** the UI SHALL provide a control to set sticky when the current post is not sticky
- **AND** the UI SHALL provide a control to unset sticky when the current post is sticky

#### Scenario: Successful set sticky updates UI state
- **WHEN** a moderator/admin triggers the set-sticky action
- **THEN** the client SHALL call `POST /api/mod/posts/{postId}/sticky` with JSON body containing `isSticky=true`
- **AND** on success the UI SHALL update to reflect `isSticky=true` for the post

#### Scenario: Successful unset sticky updates UI state
- **WHEN** a moderator/admin triggers the unset-sticky action
- **THEN** the client SHALL call `POST /api/mod/posts/{postId}/sticky` with JSON body containing `isSticky=false`
- **AND** on success the UI SHALL update to reflect `isSticky=false` for the post

#### Scenario: Member cannot see moderation controls
- **WHEN** a user whose effective forum role is member views a post detail page
- **THEN** the UI SHALL NOT render the sticky moderation controls

### Requirement: Sticky moderation actions handle common error outcomes
The frontend SHALL provide user-visible feedback for moderation endpoint failures.

#### Scenario: Unauthenticated moderation request leads to auth flow
- **WHEN** a moderation request returns 401
- **THEN** the client SHALL follow the existing authentication recovery path defined by the frontend shell (refresh or re-login flow)

#### Scenario: Forbidden moderation request shows permission error
- **WHEN** a moderation request returns 403
- **THEN** the UI SHALL present a permission error message that indicates the user lacks authorization for that post

#### Scenario: Missing post shows not-found
- **WHEN** a moderation request returns 404
- **THEN** the UI SHALL present a not-found message for the target post

### Requirement: Post detail exposes moderation audit history panel
The frontend SHALL provide a panel on the post detail view that loads and displays moderation audit items for the post.

#### Scenario: Authorized user opens audit panel
- **WHEN** a moderator/admin opens the audit history panel for a post
- **THEN** the client SHALL call `GET /api/mod/audit?targetType=post&targetId={postId}`
- **AND** the UI SHALL render each returned item including a user-facing action label (for example 置顶帖子 / 取消置顶), operator display name (nickname from customer profile when available), and occurred-at timestamp

