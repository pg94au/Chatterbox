Feature: Registering users to the chatroom

Scenario: Cannot connect to list empty room without registering
	Given the cloud formation stack is deployed
	And a websocket connection A is established
	
	When a list users request is sent to A
	Then no response is received from A

Scenario: First user can register to an empty chatroom
	Given the cloud formation stack is deployed
	And a websocket connection P is established
	When a register request is sent to P for "Paul"
	Then the registered event is received from P for "Paul"
	And the user joined event is received from P for "Paul"
	When a list users request is sent to P
	Then the returned list of users from P includes
		| DisplayName |
		| Paul       |

Scenario: Existing users are notified when a user joins or leaves
	Given the cloud formation stack is deployed
	And a websocket connection A is established
	When a register request is sent to A for "Alice"
	Then the registered event is received from A for "Alice"
	And the user joined event is received from A for "Alice"

	Given a websocket connection B is established
	When a register request is sent to B for "Bob"
	Then the registered event is received from B for "Bob"
	And the user joined event is received from A for "Bob"
	And the user joined event is received from B for "Bob"

	When websocket connection B is closed
	Then the user left event is received from A for "Bob"

Scenario: An unregistered user does not observe other users joining or leaving
	Given the cloud formation stack is deployed
	And a websocket connection A is established

	Given a websocket connection B is established
	When a register request is sent to B for "Bob"
	Then the registered event is received from B for "Bob"
	And the user joined event is received from B for "Bob"

	When websocket connection B is closed
	# No response from either the registration or the disconnection goes to A
	Then no response is received from A
