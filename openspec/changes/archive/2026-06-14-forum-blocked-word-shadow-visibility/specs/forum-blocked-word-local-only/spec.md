## ADDED Requirements

### Requirement: Local-only blocked content storage

When blocked-word handling is `local`, the system SHALL NOT persist matched post or reply body content in the Model service database. The authenticated client SHALL store the full content in browser `localStorage`, scoped by JWT `sub`, and SHALL merge that content into the author's forum views on the same browser only.

#### Scenario: Local post stored in browser after blocked-word hit

- **WHEN** blocked-word filtering is active, `Forum:BlockedWords:Handling` is `local`, and an authenticated user submits a new post that matches a configured blocked word
- **THEN** the Model service SHALL NOT insert a post record
- **AND** the HTTP response SHALL be success (2xx) with `localOnly: true` and a client-routable id whose string form starts with `local:`
- **AND** the client SHALL persist title, body, board, tags, author identity aligned with `sub`, and timestamps in `localStorage` under that user's bucket

#### Scenario: Local reply stored in browser after blocked-word hit

- **WHEN** blocked-word handling is `local` and an authenticated user submits a reply on an existing server or local post that matches a configured blocked word
- **THEN** the Model service SHALL NOT insert a reply record
- **AND** the HTTP response SHALL be success (2xx) with `localOnly: true` and a `local:` id
- **AND** the client SHALL persist the reply body and `postId` reference in `localStorage` for that `sub`

#### Scenario: Cleared browser storage removes local content

- **WHEN** a user clears site data or `localStorage` for the forum origin
- **THEN** previously stored local-only posts and replies SHALL no longer appear in merged forum views
- **AND** no server recovery endpoint is required

### Requirement: Author feed and detail merge local posts

The frontend SHALL merge the current user's local-only posts into homepage feed lists and post detail navigation for that user on the same browser, ordered by creation time together with server `published` posts. Other users and anonymous clients SHALL NOT receive local-only items from any server endpoint.

#### Scenario: Author sees local post on homepage feed after posting

- **WHEN** an authenticated author creates a local-only post and then views the homepage posts feed on the same browser without clearing storage
- **THEN** the feed presentation SHALL include that local post among list items visible to the author

#### Scenario: Anonymous feed excludes local posts

- **WHEN** an anonymous client requests the homepage posts feed
- **THEN** the response and rendered list SHALL include only server-persisted posts

#### Scenario: Local post detail by local id

- **WHEN** an authenticated author navigates to post detail for a `local:` id present in their storage bucket
- **THEN** the detail view SHALL render content from `localStorage` without calling server post detail for that id

### Requirement: Reply list merges author's local replies

On a post detail view, the frontend SHALL merge the current user's local-only replies for that `postId` into the reply list visible to that user on the same browser. Server reply list endpoints SHALL remain limited to persisted `published` replies only.

#### Scenario: Author sees own local reply on a server post

- **WHEN** an authenticated user creates a local-only reply on a server-published post and views that post's detail on the same browser
- **THEN** the reply list shown to that user SHALL include the local reply
- **AND** other users viewing the same server post SHALL see only server-published replies

#### Scenario: Local replies on local post

- **WHEN** an authenticated user adds local-only replies to a local-only post
- **THEN** those replies SHALL appear only in that user's detail view for the local post on the same browser

### Requirement: Local content isolation and non-goals

Local-only posts and replies SHALL NOT participate in server-side search, notifications, comment counters, likes, favorites, reports, or moderation read APIs. Create responses for local handling SHALL NOT echo matched blocked words or name the interception mechanism in the message field.

#### Scenario: Local reply does not change server comment count

- **WHEN** a local-only reply is created on a server-published post
- **THEN** the server-persisted `CommentCount` on that post SHALL remain unchanged

#### Scenario: Neutral success message on local create

- **WHEN** a post or reply is handled as local-only due to blocked words
- **THEN** the API success message SHALL NOT reference blocked words or moderation
