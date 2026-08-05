Feature: Offline event queue

  The calendar workspace lets an admin capture event intent while offline and
  sync it later when the browser reconnects.

  Background:
    Given an authenticated admin session exists
    And the calendar workspace is open

  Scenario: Creating an event while offline queues it for later sync
    Given the browser is offline
    When I enter "Shared planning on 12 August" in the event details
    And I submit the event for review
    Then the event appears in the queued list
    And the queued event status is pending
    And I can see that the workspace is offline

  Scenario: Queued events sync after reconnecting
    Given the browser has a queued event for "Shared planning on 12 August"
    When the browser reconnects
    Then the queued event is submitted for review
    And the queued list no longer contains "Shared planning on 12 August"
    And I see a confirmation that the queued event synced

  Scenario: Failed queue sync keeps the event retryable
    Given the browser has a queued event for "Shared planning on 12 August"
    And the server rejects the sync attempt
    When the browser reconnects
    Then the queued event remains in the queued list
    And the queued event status is retry
    And I can see a safe retry message

  Scenario: Cached upcoming events are available while offline
    Given upcoming events were cached during a previous online session
    When the browser is offline
    Then I can still see cached upcoming events
    And I can see that the workspace data is stale
