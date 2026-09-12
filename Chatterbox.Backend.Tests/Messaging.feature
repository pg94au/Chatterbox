Feature: The ability for registered users to send messages to each other

Scenario: A registered user can send a message to another registered user
	Given the cloud formation stack is deployed

	And a websocket connection A is established
	And a websocket connection B is established

	When a register request is sent to A for "Alice"
	Then the registered event is received from A for "Alice"
	And the user joined event is received from A for "Alice"
	
	When a register request is sent to B for "Bob"
	Then the registered event is received from B for "Bob"
	And the user joined event is received from A for "Bob"
	And the user joined event is received from B for "Bob"

	When a send message request is sent to A for "Bob" with the message "Hello, Bob!"
	Then the message event is received from B with the message "Hello, Bob!" from "Alice"

Scenario: A message is not delivered to non-target users or unregistered users
	Given the cloud formation stack is deployed

	And a websocket connection A is established
	And a websocket connection B is established
	And a websocket connection C is established
	And a websocket connection D is established

	When a register request is sent to A for "Alice"
	Then the registered event is received from A for "Alice"
	And the user joined event is received from A for "Alice"

	When a register request is sent to B for "Bob"
	Then the registered event is received from B for "Bob"
	And the user joined event is received from A for "Bob"
	And the user joined event is received from B for "Bob"

	When a register request is sent to C for "Charlie"
	Then the registered event is received from C for "Charlie"
	And the user joined event is received from A for "Charlie"
	And the user joined event is received from B for "Charlie"
	And the user joined event is received from C for "Charlie"

	When a send message request is sent to A for "Bob" with the message "Hello, Bob!"
	# Non-Bob does not receive the message event
	Then no response is received from C
	# Unregistered connection does not receive the message event
	Then no response is received from D

Scenario: Messages are not accepted from unregistered users
	Given the cloud formation stack is deployed

	And a websocket connection A is established
	And a websocket connection B is established

	When a register request is sent to A for "Alice"
	Then the registered event is received from A for "Alice"
	And the user joined event is received from A for "Alice"

	When a send message request is sent to B for "Alice" with the message "Hello, Alice!"
	Then an error event is received from B with reason "sender_not_registered"
	Then no response is received from A

Scenario: Messages are not accepted for users that are not online
	Given the cloud formation stack is deployed
	And a websocket connection A is established

	When a register request is sent to A for "Alice"
	Then the registered event is received from A for "Alice"
	And the user joined event is received from A for "Alice"

	When a send message request is sent to A for "Bob" with the message "Hello, Bob!"
	Then an error event is received from A with reason "user_not_online"
