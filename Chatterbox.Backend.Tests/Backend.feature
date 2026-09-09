Feature: Registering the first user to an empty chatroom

Scenario: Listing users in empty room returns no one
	Given the cloud formation stack is deployed
	And a websocket connection is established
	When a list users request is sent
	Then the returned list of users includes
		| DisplayName |

Scenario: First user can register to an empty chatroom
	Given the cloud formation stack is deployed
	And a websocket connection is established
	When a register request is sent for "Paul"
	Then the user joined event is received for "Paul"
	And the registered event is received for "Paul"
	When a list users request is sent
	Then the returned list of users includes
		| DisplayName |
		| Paul       |
