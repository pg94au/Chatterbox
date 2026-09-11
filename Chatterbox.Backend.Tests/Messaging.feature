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

# TODO: Test for the message event not being received by any other users.

# TODO: Test for the message event not being accepted from unregistered users.

# TODO: Test for the message event not being delivered to users that are not online.
