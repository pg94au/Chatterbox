Feature: Registering the first user to an empty chatroom

Scenario: First user can register to an empty chatroom
	Given the cloud formation stack is deployed
	And a websocket connection is established
	When a register request is sent for "Paul"
	Then the user joined event is received for "Paul"
	And the registered event is received for "Paul"
	And the list users request shows only "Paul"
