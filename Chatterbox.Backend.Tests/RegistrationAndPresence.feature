Feature: Registering users to the chatroom and listing users in the chatroom

Scenario: Cannot list empty room without registering
	Given the cloud formation stack is deployed
	And a websocket connection A is established

	When a list users request is sent to A
	Then no response is received from A


Scenario: Cannot list users without registering
	Given the cloud formation stack is deployed
	And a websocket connection A is established

	When a register request is sent to A for "Alice"
	Then a registered event is received from A for "Alice"
	And a user joined event is received from A for "Alice"

	Given a websocket connection B is established
	When a list users request is sent to B
	Then no response is received from B


Scenario: Cannot register with a blank display name
	Given the cloud formation stack is deployed
	And a websocket connection A is established

	When a register request is sent to A for ""
	Then an error event is received from A with reason "displayName_required"


Scenario: Cannot register with the same display name as the AI agent
	Given the cloud formation stack is deployed
	And a websocket connection A is established

	# Registration as the AI agent is not allowed
	When a register request is sent to A for "Agent"
	Then an error event is received from A with reason "displayName_reserved"

	# Registration as the AI agent is not allowed, even with different casing
	When a register request is sent to A for "agent"
	Then an error event is received from A with reason "displayName_reserved"


Scenario: First user can register to an empty chatroom and list users
	Given the cloud formation stack is deployed
	And a websocket connection A is established
	When a register request is sent to A for "Alice"
	Then a registered event is received from A for "Alice"
	And a user joined event is received from A for "Alice"
	When a list users request is sent to A
	# The returned list of users from A includes the AI agent and Alice
	Then the returned list of users from A includes
		| DisplayName |
		| Agent       |
		| Alice       |


Scenario: When multiple users are in the chat room, they can see each other in the list of users
	Given the cloud formation stack is deployed
	And a websocket connection A is established
	And a websocket connection B is established

	When a register request is sent to A for "Alice"
	Then a registered event is received from A for "Alice"
	And a user joined event is received from A for "Alice"

	When a register request is sent to B for "Bob"
	Then a registered event is received from B for "Bob"
	And a user joined event is received from A for "Bob"
	And a user joined event is received from B for "Bob"

	When a list users request is sent to A
	# The returned list of users from A includes the AI agent, Alice, and Bob
	Then the returned list of users from A includes
		| DisplayName |
		| Agent       |
		| Alice       |
		| Bob         |


Scenario: Existing users are notified when a user joins or leaves
	Given the cloud formation stack is deployed
	And a websocket connection A is established
	When a register request is sent to A for "Alice"
	Then a registered event is received from A for "Alice"
	And a user joined event is received from A for "Alice"

	Given a websocket connection B is established
	When a register request is sent to B for "Bob"
	Then a registered event is received from B for "Bob"
	And a user joined event is received from A for "Bob"
	And a user joined event is received from B for "Bob"

	When websocket connection B is closed
	Then the user left event is received from A for "Bob"


Scenario: An unregistered user does not observe other users joining or leaving
	Given the cloud formation stack is deployed
	And a websocket connection A is established

	Given a websocket connection B is established
	When a register request is sent to B for "Bob"
	Then a registered event is received from B for "Bob"
	And a user joined event is received from B for "Bob"

	When websocket connection B is closed
	# No response from either the registration or the disconnection goes to A
	Then no response is received from A


Scenario: When a user re-registers to a new connection, the old connection is kicked
	Given the cloud formation stack is deployed
	And a websocket connection A is established
	And a websocket connection B is established

	When a register request is sent to A for "Alice"
	Then a registered event is received from A for "Alice"
	And a user joined event is received from A for "Alice"

	When a register request is sent to B for "Alice"
	Then a registered event is received from B for "Alice"

	And the kicked event is received from A


Scenario: Cannot register a second time on the same connection
	Given the cloud formation stack is deployed
	And a websocket connection A is established
	When a register request is sent to A for "Alice"

	Then a registered event is received from A for "Alice"
	And a user joined event is received from A for "Alice"

	When a register request is sent to A for "Alice"
	Then an error event is received from A with reason "already_registered"

	When a register request is sent to A for "Bob"
	Then an error event is received from A with reason "already_registered"
