Feature: Household metadata

  Household metadata lets an admin maintain generic people and relationship
  labels that drive calendar review and virtual calendar views.

  Background:
    Given an authenticated admin session exists
    And the household metadata page is open

  Scenario: The admin can see the generic default household metadata
    Then I can see active Adult A and Adult B member records
    And I can see the default parent or guardian relationships

  Scenario: The admin can add a member and relationship
    When I create a generic child member
    And I create a parent or guardian relationship from Adult A to that member
    Then the new member is listed as active
    And the new relationship is listed as active

  Scenario: The admin can archive household metadata
    Given a generic household member and relationship exist
    When I archive the relationship
    And I archive the member
    Then both records remain visible as archived metadata
