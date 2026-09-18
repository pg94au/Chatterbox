import { useState } from 'react'
import type { FormEvent } from 'react'

import { useChatStore } from '../state/chatStore'
import { chatConnection } from '../websocket/ChatConnection'
import { StatusBadge } from './StatusBadge'

export function ConnectionPanel() {
  const [displayName, setDisplayName] = useState('')
  const [wsUrlInput, setWsUrlInput] = useState(
    useChatStore.getState().wsUrl ?? '',
  )

  const connectionState = useChatStore((state) => state.connectionState)
  const reconnectAttempt = useChatStore((state) => state.reconnectAttempt)
  const error = useChatStore((state) => state.lastError)
  const kickedReason = useChatStore((state) => state.kickedReason)

  const isBusy = connectionState === 'connecting' || connectionState === 'reconnecting'

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    chatConnection.connect(wsUrlInput, displayName)
  }

  return (
    <section className="connection-shell">
      <div className="connection-card">
        <p className="eyebrow">Chatterbox</p>
        <h1>Sign in to the room</h1>
        <p className="lede">
          Pick a display name, then register over WebSocket to start chatting.
        </p>

        <form className="connection-form" onSubmit={onSubmit}>
          <label htmlFor="displayName">Display name</label>
          <input
            id="displayName"
            placeholder="e.g. Alice"
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
            autoComplete="off"
            maxLength={40}
            required
          />

          <label htmlFor="wsUrl">WebSocket URL</label>
          <input
            id="wsUrl"
            placeholder="wss://.../prod"
            value={wsUrlInput}
            onChange={(event) => setWsUrlInput(event.target.value)}
            autoComplete="off"
            required
          />

          <button type="submit" disabled={isBusy}>
            {isBusy ? 'Connecting...' : 'Connect'}
          </button>
        </form>

        <div className="connection-meta">
          <StatusBadge state={connectionState} reconnectAttempt={reconnectAttempt} />
          {error ? <p className="error-text">Error: {error}</p> : null}
          {kickedReason ? <p className="error-text">Session ended: {kickedReason}</p> : null}
        </div>
      </div>
    </section>
  )
}
