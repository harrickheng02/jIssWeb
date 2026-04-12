## Purpose

Forum-style homepage shell: content discovery, posting entry, community structure; identity in header when authenticated.

## Requirements

### Requirement: Forum homepage uses a content-first shell

The system SHALL present `/` as a forum-style homepage shell that prioritizes content discovery, posting entry, and community structure over authentication forms.

#### Scenario: User opens homepage

- **WHEN** a user visits the root route
- **THEN** the page SHALL render a forum homepage shell instead of the login or registration form

### Requirement: Homepage header exposes core community navigation

The system SHALL provide a top header with brand entry, primary navigation items, search input, a primary post entry action, and a user area that changes with authentication state.

#### Scenario: Unauthenticated header state

- **WHEN** the homepage renders for a user without a token
- **THEN** the header SHALL provide a clear entry to authentication (e.g. avatar or control that navigates to the unified login/registration route), optional global theme control, and SHALL NOT show the authenticated user menu

#### Scenario: Authenticated header state

- **WHEN** the homepage renders for a user with a token
- **THEN** the header SHALL show a user avatar or equivalent authenticated identity area and a visible post entry action

#### Scenario: Authenticated user community context in header

- **WHEN** the user opens the authenticated avatar menu (e.g. hover or focus)
- **THEN** the product MAY present community-side summary information there (e.g. level, points, check-in, unread counts) when such features exist; the homepage right column SHALL NOT duplicate that identity block solely for the same purpose

### Requirement: Homepage provides community classification and post feed

The system SHALL provide a left classification area and a central post feed area containing at least category shortcuts, feed filters, and post summary cards.

#### Scenario: User scans homepage content

- **WHEN** the homepage finishes rendering
- **THEN** the user SHALL be able to see category shortcuts and a list of post summary cards without additional navigation

### Requirement: Post summary cards expose forum metadata

The system SHALL render post summary cards with clickable title, short excerpt, author and time, tag list, and summary counters for likes, comments, and views.

#### Scenario: User reads a post card

- **WHEN** a post card is shown in the feed
- **THEN** the card SHALL include title, excerpt, author/time metadata, tags, like count, comment count, and view count

### Requirement: Homepage provides right-side community context

The system SHALL provide a right-side information area containing at least hot content list, hot tags, and announcement content. Identity and per-user community stats are not required in this column when the header avatar menu provides them.

#### Scenario: User checks community context

- **WHEN** the homepage renders on a desktop-width viewport
- **THEN** the right-side area SHALL expose hot content, hot tags, and announcement modules

### Requirement: Homepage supports responsive forum layout

The system SHALL adapt the homepage layout across desktop, tablet, and mobile widths while preserving access to the main content feed.

#### Scenario: Mobile homepage remains usable

- **WHEN** a user opens the homepage on a narrow mobile viewport
- **THEN** the layout SHALL collapse to a single-column content-first arrangement and SHALL hide or reorder side areas as needed

### Requirement: Header search drives forum post search

The forum homepage SHALL connect the header search input to the forum post search capability (`forum-post-search`): user-visible text changes SHALL trigger search requests only after debouncing, except that activating a primary submit action (e.g. pressing Enter in the search field) SHALL trigger a search immediately when the trimmed query is non-empty.

#### Scenario: Debounced input reduces requests

- **WHEN** the user types in the header search field with pauses shorter than the debounce interval
- **THEN** the client SHALL not send a search request on every keystroke

#### Scenario: Enter submits without waiting for debounce

- **WHEN** the user presses Enter with a non-empty trimmed search query
- **THEN** the client SHALL issue a search request without waiting for the debounce delay for that submission

#### Scenario: Empty query does not search

- **WHEN** the trimmed search query is empty
- **THEN** the client SHALL NOT send a keyword search request solely due to debounced input

#### Scenario: Search outcomes are visible

- **WHEN** a search request completes, fails, returns no results, or is rate limited
- **THEN** the homepage SHALL present a distinguishable loading, empty, error, or rate-limited state for the search-driven content area

### Requirement: Authenticated header exposes notification entry

The forum homepage shell SHALL expose a visible notification entry control in the top header when the user is authenticated, linking to the in-app notification list view or route.

#### Scenario: Logged-in user sees notification entry

- **WHEN** the homepage shell renders for a user with a valid session token
- **THEN** the header SHALL include a control to open notifications (e.g. icon or label)

#### Scenario: Guest does not see notification entry

- **WHEN** the homepage shell renders without an authenticated session
- **THEN** the header SHALL NOT present the authenticated-only notification entry described above

### Requirement: Notification list view states

The application SHALL provide a notification list experience that can present loading, empty, and error outcomes for the notification list request, consistent with `in-app-notifications` empty and failure handling.

#### Scenario: Empty notifications

- **WHEN** the notification list API returns success with no items for the user
- **THEN** the UI SHALL show an empty state distinct from loading and error states

#### Scenario: Notification list failure

- **WHEN** the notification list request fails (non-success HTTP or client error)
- **THEN** the UI SHALL show a failure state that is distinct from the empty list state
