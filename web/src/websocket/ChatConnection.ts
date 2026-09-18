import { useChatStore } from '../state/chatStore'
import type {
  ChatEntry,
  MessageEvent,
  ServerEvent,
  UserPresence,
} from '../types/chat'
import {
  isEchoOfOutgoingSelfMessage,
  listUsers,
  parseServerEvent,
  register,
  sendMessage,
  serializeCommand,
} from './protocol'

class ChatConnection {
  private socket: WebSocket | null = null
  private reconnectTimer: number | null = null
  private manuallyDisconnected = true

  connect(wsUrl: string, displayName: string): void {
    this.manuallyDisconnected = false
    this.openSocket(wsUrl.trim(), displayName.trim(), false)
  }

  disconnect(): void {
    this.manuallyDisconnected = true
    this.clearReconnectTimer()

    if (this.socket && this.socket.readyState < WebSocket.CLOSING) {
      this.socket.close(1000, 'Client disconnected')
    }

    this.socket = null
    useChatStore.getState().resetPresence()
  }

  requestUsers(): void {
    this.send(listUsers())
  }

  sendChatMessage(to: string, text: string): void {
    const state = useChatStore.getState()
    const sender = state.displayName
    const normalizedText = text.trim()

    if (!sender || !normalizedText) {
      return
    }

    const outgoing: ChatEntry = {
      id: this.messageId('out'),
      from: sender,
      to,
      text: normalizedText,
      timestamp: Date.now(),
      direction: 'outgoing',
    }

    state.addMessage(outgoing)
    this.send(sendMessage(to, normalizedText))
  }

  private openSocket(
    wsUrl: string,
    displayName: string,
    isReconnect: boolean,
  ): void {
    const state = useChatStore.getState()

    if (!wsUrl || !displayName) {
      state.setError('WebSocket URL and display name are required.')
      state.setConnectionState('failed')
      return
    }

    if (
      this.socket &&
      (this.socket.readyState === WebSocket.OPEN ||
        this.socket.readyState === WebSocket.CONNECTING)
    ) {
      return
    }

    state.setError(null)
    state.setKicked(null)
    state.setWsUrl(wsUrl)
    state.setDisplayName(displayName)
    state.setConnectionState(isReconnect ? 'reconnecting' : 'connecting')

    const socket = new WebSocket(wsUrl)
    this.socket = socket

    socket.onopen = () => {
      const store = useChatStore.getState()
      store.setConnectionState('connected')
      store.setReconnectAttempt(0)
      this.send(register(displayName))
    }

    socket.onmessage = (event) => {
      if (typeof event.data !== 'string') {
        return
      }

      const parsed = parseServerEvent(event.data)
      if (!parsed) {
        return
      }

      this.handleServerEvent(parsed)
    }

    socket.onerror = () => {
      useChatStore
        .getState()
        .setError('WebSocket connection error. Trying to recover...')
    }

    socket.onclose = () => {
      this.socket = null
      const store = useChatStore.getState()
      store.setRegistered(false)

      if (this.manuallyDisconnected) {
        store.resetPresence()
        return
      }

      this.scheduleReconnect()
    }
  }

  private scheduleReconnect(): void {
    const state = useChatStore.getState()
    const wsUrl = state.wsUrl
    const displayName = state.displayName

    if (!wsUrl || !displayName) {
      state.setConnectionState('failed')
      return
    }

    const nextAttempt = state.reconnectAttempt + 1
    state.setReconnectAttempt(nextAttempt)
    state.setConnectionState('reconnecting')

    const delayMs = Math.min(1000 * 2 ** (nextAttempt - 1), 10000)

    this.clearReconnectTimer()
    this.reconnectTimer = window.setTimeout(() => {
      this.openSocket(wsUrl, displayName, true)
    }, delayMs)
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) {
      window.clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
  }

  private send(command: ReturnType<typeof register> | ReturnType<typeof listUsers> | ReturnType<typeof sendMessage>): void {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      useChatStore.getState().setError('Not connected to Chatterbox.')
      return
    }

    this.socket.send(serializeCommand(command))
  }

  private handleServerEvent(event: ServerEvent): void {
    const state = useChatStore.getState()

    switch (event.type) {
      case 'registered': {
        const registeredName = event.displayName

        state.setRegistered(true)
        state.setError(null)

        if (registeredName) {
          state.setDisplayName(registeredName)
        }

        this.requestUsers()
        break
      }
      case 'users': {
        const users = event.users as UserPresence[]

        state.setUsers(users)
        if (
          !state.selectedUser &&
          users.length > 0
        ) {
          const firstCandidate = users.find(
            (user) => user.displayName !== state.displayName,
          )
          state.selectUser(firstCandidate?.displayName ?? null)
        }
        break
      }
      case 'userJoined': {
        const displayName = event.displayName

        if (displayName) {
          state.upsertUser({
            displayName,
            connectedAt: Date.now(),
          })
        }
        break
      }
      case 'userLeft': {
        const displayName = event.displayName

        if (displayName) {
          state.removeUser(displayName)
        }
        break
      }
      case 'message': {
        const messageEvent = event as MessageEvent
        const displayName = state.displayName

        if (
          isEchoOfOutgoingSelfMessage(messageEvent, displayName) &&
          this.hasRecentMatchingOutgoing(messageEvent.text)
        ) {
          return
        }

        const incoming: ChatEntry = {
          id: this.messageId('in'),
          from: messageEvent.from,
          to: displayName ?? '',
          text: messageEvent.text,
          timestamp: Date.now(),
          direction: 'incoming',
        }

        state.addMessage(incoming)
        break
      }
      case 'error': {
        state.setError(event.error)
        break
      }
      case 'kicked': {
        const reason = event.reason

        state.setKicked(reason)
        state.setError(reason)
        this.manuallyDisconnected = true

        if (this.socket && this.socket.readyState < WebSocket.CLOSING) {
          this.socket.close(1000, reason)
        }

        break
      }
      default:
        break
    }
  }

  private hasRecentMatchingOutgoing(text: string): boolean {
    const state = useChatStore.getState()
    const self = state.displayName

    if (!self) {
      return false
    }

    const thread = state.conversations[self] ?? []
    const lastMessage = thread[thread.length - 1]
    if (!lastMessage || lastMessage.direction !== 'outgoing') {
      return false
    }

    return lastMessage.text === text && Date.now() - lastMessage.timestamp < 2000
  }

  private messageId(prefix: string): string {
    return `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`
  }
}

export const chatConnection = new ChatConnection()
