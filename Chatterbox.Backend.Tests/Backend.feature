Feature: Registering the first user to an empty chatroom

Scenario: Listing users in empty room returns no one
	Given the cloud formation stack is deployed
	And a websocket connection P is established
	When a list users request is sent to P
	Then the returned list of users from P includes
		| DisplayName |

Scenario: First user can register to an empty chatroom
	Given the cloud formation stack is deployed
	And a websocket connection P is established
	When a register request is sent to P for "Paul"
	Then the user joined event is received from P for "Paul"
	And the registered event is received from P for "Paul"
	When a list users request is sent to P
	Then the returned list of users from P includes
		| DisplayName |
		| Paul       |

Scenario: Subsequent users can register and see existing users
	Given the cloud formation stack is deployed
	And a websocket connection P is established
	When a register request is sent to P for "Paul"
	Then the user joined event is received from P for "Paul"
