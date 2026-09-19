import { useMemo } from 'react'

import { ChatWindow } from './components/ChatWindow'
import { ConnectionPanel } from './components/ConnectionPanel'
import { Sidebar } from './components/Sidebar'
import { StatusBadge } from './components/StatusBadge'
import { useChatStore } from './state/chatStore'
import { chatConnection } from './websocket/ChatConnection'
import './App.css'

function App() {
  const displayName = useChatStore((state) => state.displayName)
  const connectionState = useChatStore((state) => state.connectionState)
  const users = useChatStore((state) => state.users)
  const selectedUser = useChatStore((state) => state.selectedUser)
  const conversations = useChatStore((state) => state.conversations)
  const unreadByUser = useChatStore((state) => state.unreadByUser)
  const error = useChatStore((state) => state.lastError)
  const reconnectAttempt = useChatStore((state) => state.reconnectAttempt)
  const registered = useChatStore((state) => state.registered)
  const setSelectedUser = useChatStore((state) => state.selectUser)

  const activeConversation = useMemo(
    () => (selectedUser ? conversations[selectedUser] ?? [] : []),
    [conversations, selectedUser],
  )

  if (!registered || !displayName) {
    return <ConnectionPanel />
  }

  const handleDisconnect = () => {
    chatConnection.disconnect()
  }

  const handleRefreshUsers = () => {
    chatConnection.requestUsers()
  }

  const handleSendMessage = (text: string) => {
    if (!selectedUser) {
      return
    }

    chatConnection.sendChatMessage(selectedUser, text)
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">Connected as</p>
          <h1>{displayName}</h1>
        </div>

        <div>
          <StatusBadge state={connectionState} reconnectAttempt={reconnectAttempt} />
          {error ? <p className="error-text">Error: {error}</p> : null}
        </div>

        <button type="button" className="disconnect" onClick={handleDisconnect}>
          Disconnect
        </button>
      </header>

      <section className="chat-layout">
        <Sidebar
          users={users}
          currentUser={displayName}
          selectedUser={selectedUser}
          unreadByUser={unreadByUser}
          onSelectUser={setSelectedUser}
          onRefreshUsers={handleRefreshUsers}
        />
        <ChatWindow
          currentUser={displayName}
          selectedUser={selectedUser}
          conversation={activeConversation}
          onSendMessage={handleSendMessage}
        />
      </section>
    </main>
  )
}

export default App
