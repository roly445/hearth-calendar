Feature: Live calendar updates

  The calendar workspace uses server push notifications to keep the visible UI
  current after approved events or review queue items change.

  Background:
    Given an authenticated admin session exists
    And the calendar workspace is open
    And the browser is online
    And live updates are connected

  Scenario: A calendar update refreshes visible data
    Given the review queue is visible
    When a calendar update notification is received
    Then the review queue refreshes
    And the upcoming events panel refreshes

  Scenario: Reconnecting live updates refreshes visible data
    Given live updates were temporarily disconnected
    When live updates reconnect
    Then the workspace refreshes visible data
    And I can see that the workspace is online

  Scenario: Live update connection failure keeps the workspace usable
    When live updates cannot connect for this session
    Then I see a non-blocking live update availability message
    And I can still refresh the workspace manually

  Scenario: Live updates do not expose raw secrets
    When a calendar update notification is received
    Then the notification payload contains only the update type, entity identifier, and occurrence time
    And the notification payload does not contain raw credentials, raw feed tokens, or authorization headers
