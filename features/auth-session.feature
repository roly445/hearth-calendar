Feature: Authenticated admin session

  The browser UI is available to authenticated admin users while protected
  workspace data stays unavailable to unauthenticated users.

  Scenario: Unauthenticated users are sent to the sign-in flow
    Given no admin session exists
    When I open the calendar workspace
    Then I am asked to sign in
    And protected calendar workspace data is not shown

  Scenario: Authenticated admins can load the calendar workspace
    Given a valid admin session exists
    When I open the calendar workspace
    Then I can see the calendar workspace
    And the workspace loads protected review queue data
    And the workspace loads protected upcoming event data

  Scenario: Session loss stops protected actions
    Given an authenticated admin session exists
    And the calendar workspace is open
    When the admin session expires
    And I try to approve a review item
    Then the action is rejected safely
    And I am asked to sign in again

  Scenario: Signing out clears workspace access
    Given an authenticated admin session exists
    When I sign out
    Then the admin session is cleared
    And protected calendar workspace data is not shown
