## MODIFIED Requirements

### Requirement: Post summary cards expose forum metadata

The system SHALL render post summary cards with clickable title, short excerpt, author and time, tag list, and summary counters for likes, comments, and views, and SHALL visually indicate when a post is sticky.

#### Scenario: User reads a post card

- **WHEN** a post card is shown in the feed
- **THEN** the card SHALL include title, excerpt, author/time metadata, tags, like count, comment count, and view count
- **AND** when the post list item indicates the post is sticky, the card SHALL render a visible sticky marker (e.g. a label such as "置顶")

