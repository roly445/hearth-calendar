Feature: Calendar workspace

  The calendar workspace is the admin user's day-to-day view for creating
  event intents, reviewing staged items, and scanning upcoming approved events.

  Background:
    Given an authenticated admin session exists
    And the calendar workspace is open
    And the browser is online

  Scenario: The workspace shows the main work areas
    Then I can see the create event panel
    And I can see the review queue panel
    And I can see the upcoming events panel
    And I can see that the workspace is online

  Scenario: Creating an event submits it for review
    When I enter "Shared planning on 12 August" in the event details
    And I submit the event for review
    Then I see a confirmation that the event is waiting for review
    And the review queue refreshes

  Scenario: Review actions are disabled while stale offline data is shown
    Given the workspace is showing cached upcoming events
    When the browser is offline
    Then review action buttons are disabled
    And I can see that the workspace data is stale

  Scenario: Upcoming approved events are shown in date order
    Given approved events exist for the next 60 days
    When the workspace refreshes upcoming events
    Then I can see the upcoming approved events
    And each event shows its date, title, time, category, participants, and calendar
