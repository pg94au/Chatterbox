import type {
  ClientCommand,
  ListUsersCommand,
  MessageEvent,
  RegisterCommand,
  SendMessageCommand,
  ServerEvent,
} from '../types/chat'

export function register(displayName: string): RegisterCommand {
  return {
    action: 'register',
    displayName,
  }
}

export function listUsers(): ListUsersCommand {
  return {
    action: 'listUsers',
  }
}

export function sendMessage(to: string, text: string): SendMessageCommand {
  return {
    action: 'message',
    to,
    text,
  }
}

export function serializeCommand(command: ClientCommand): string {
  return JSON.stringify(command)
}

export function parseServerEvent(rawData: string): ServerEvent | null {
  try {
    const data = JSON.parse(rawData) as ServerEvent
    if (!data || typeof data !== 'object' || typeof data.type !== 'string') {
      return null
    }

    return data
  } catch {
    return null
  }
}

export function isEchoOfOutgoingSelfMessage(
  event: MessageEvent,
  displayName: string | null,
): boolean {
  return Boolean(displayName && event.from === displayName)
}
