## ADDED Requirements

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
