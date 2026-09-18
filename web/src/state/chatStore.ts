import { create } from 'zustand'

import type { ChatEntry, ConnectionState, UserPresence } from '../types/chat'

interface ChatState {
  wsUrl: string
  connectionState: ConnectionState
  displayName: string | null
  registered: boolean
  users: UserPresence[]
  selectedUser: string | null
  conversations: Record<string, ChatEntry[]>
  lastError: string | null
  kickedReason: string | null
  reconnectAttempt: number

  setWsUrl: (wsUrl: string) => void
  setConnectionState: (state: ConnectionState) => void
  setDisplayName: (displayName: string | null) => void
  setRegistered: (registered: boolean) => void
  setUsers: (users: UserPresence[]) => void
  upsertUser: (user: UserPresence) => void
  removeUser: (displayName: string) => void
  selectUser: (displayName: string | null) => void
  addMessage: (message: ChatEntry) => void
  setError: (error: string | null) => void
  setKicked: (reason: string | null) => void
  setReconnectAttempt: (attempt: number) => void
  resetPresence: () => void
}

function normalizeUsers(users: UserPresence[]): UserPresence[] {
  const byName = new Map<string, UserPresence>()
  for (const user of users) {
    byName.set(user.displayName, user)
  }

  return [...byName.values()].sort((a, b) =>
    a.displayName.localeCompare(b.displayName),
  )
}

export const useChatStore = create<ChatState>((set) => ({
  wsUrl: import.meta.env.VITE_CHATTERBOX_WS_URL ?? '',
  connectionState: 'disconnected',
  displayName: null,
  registered: false,
  users: [],
  selectedUser: null,
  conversations: {},
  lastError: null,
  kickedReason: null,
  reconnectAttempt: 0,

  setWsUrl: (wsUrl) => set({ wsUrl }),
  setConnectionState: (connectionState) => set({ connectionState }),
  setDisplayName: (displayName) => set({ displayName }),
  setRegistered: (registered) => set({ registered }),
  setUsers: (users) => set({ users: normalizeUsers(users) }),
  upsertUser: (user) =>
    set((state) => {
      const users = normalizeUsers([
        ...state.users.filter((u) => u.displayName !== user.displayName),
        user,
      ])

      return { users }
    }),
  removeUser: (displayName) =>
    set((state) => {
      const users = state.users.filter((u) => u.displayName !== displayName)
      const selectedUser =
        state.selectedUser === displayName ? null : state.selectedUser

      return { users, selectedUser }
    }),
  selectUser: (selectedUser) => set({ selectedUser }),
  addMessage: (message) =>
    set((state) => {
      const key =
        message.direction === 'incoming'
          ? message.from
          : message.direction === 'outgoing'
            ? message.to
            : message.to

      return {
        conversations: {
          ...state.conversations,
          [key]: [...(state.conversations[key] ?? []), message],
        },
      }
    }),
  setError: (lastError) => set({ lastError }),
  setKicked: (kickedReason) => set({ kickedReason }),
  setReconnectAttempt: (reconnectAttempt) => set({ reconnectAttempt }),
  resetPresence: () =>
    set({
      connectionState: 'disconnected',
      registered: false,
      users: [],
      selectedUser: null,
      reconnectAttempt: 0,
    }),
}))
