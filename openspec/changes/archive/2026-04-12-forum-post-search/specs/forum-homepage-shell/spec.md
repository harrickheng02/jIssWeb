## ADDED Requirements

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
