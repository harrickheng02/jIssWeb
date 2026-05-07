## ADDED Requirements

### Requirement: Moderators and admins can delete a forum post within authorization

The system SHALL expose `DELETE /api/mod/posts/{postId}` for authenticated users whose effective forum role is moderator or administrator. Administrators SHALL be allowed for any existing post. Moderators SHALL succeed only when the post's configured board resolves into their moderator board scope using the same rules as other moderator post operations (`forum-moderation-post-ops`). The operation SHALL remove the post document after removing dependent engagement rows, replies belonging to that post, and notifications keyed by that **`postId`**. On success it SHALL respond with uniform success envelope; on missing post SHALL return **`404`**; when a moderator lacks board scope SHALL return **`403`**. Each successful deletion SHALL append a moderation audit record with action **`post.modDelete`** capturing operator `sub`, target post id, and board metadata suitable for dashboards.

#### Scenario: Administrator deletes arbitrary post

- **WHEN** an admin invokes `DELETE /api/mod/posts/{postId}` for an existing post
- **THEN** the post SHALL no longer appear in lookups by id
- **AND** replies for that post SHALL be absent
- **AND** an audit record with action `post.modDelete` SHALL exist

#### Scenario: Moderator blocked outside managed boards

- **WHEN** a moderator invokes `DELETE` for a post in a board outside their moderator scope
- **THEN** the response SHALL be HTTP **`403`** and the post SHALL remain stored

### Requirement: Moderators and admins can delete a forum reply within authorization

The system SHALL expose `DELETE /api/mod/replies/{replyId}` for authenticated moderators and administrators. Authorization SHALL derive from the parent post board (moderator applies to parent's board scope; admin unrestricted). Deletion SHALL remove the reply row, decrement the parent post **`CommentCount`** when it would stay non-negative with that decrement, and remove notifications keyed by **`replyId`**. On success it SHALL return uniform success; on unknown reply **`404`**; on forbidden moderator scope **`403`**. Each successful deletion SHALL append audit action **`reply.modDelete`**.

#### Scenario: Moderator deletes reply on scoped thread

- **WHEN** a moderator deletes a reply whose parent post is in a managed board
- **THEN** the reply SHALL no longer resolve by id
- **AND** the parent **`CommentCount`** SHALL reflect one fewer comment when decremented safely
