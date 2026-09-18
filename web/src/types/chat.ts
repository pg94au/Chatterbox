export type ConnectionState =
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'failed'

export interface UserPresence {
  displayName: string
  connectedAt: number
}

export type ChatDirection = 'incoming' | 'outgoing' | 'system'

export interface ChatEntry {
  id: string
  from: string
  to: string
  text: string
  timestamp: number
  direction: ChatDirection
}

export interface BaseEvent {
  type: string
}

export interface RegisteredEvent extends BaseEvent {
  type: 'registered'
  displayName: string
}

export interface UsersEvent extends BaseEvent {
  type: 'users'
  users: UserPresence[]
}

export interface UserJoinedEvent extends BaseEvent {
  type: 'userJoined'
  displayName: string
}

export interface UserLeftEvent extends BaseEvent {
  type: 'userLeft'
  displayName: string
}

export interface MessageEvent extends BaseEvent {
  type: 'message'
  from: string
  text: string
}

export interface ErrorEvent extends BaseEvent {
  type: 'error'
  error: string
}

export interface KickedEvent extends BaseEvent {
  type: 'kicked'
  reason: string
}

export type ServerEvent =
  | RegisteredEvent
  | UsersEvent
  | UserJoinedEvent
  | UserLeftEvent
  | MessageEvent
  | ErrorEvent
  | KickedEvent

export interface RegisterCommand {
  action: 'register'
  displayName: string
}

export interface ListUsersCommand {
  action: 'listUsers'
}

export interface SendMessageCommand {
  action: 'message'
  to: string
  text: string
}

export type ClientCommand =
  | RegisterCommand
  | ListUsersCommand
  | SendMessageCommand
