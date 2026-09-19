import { useState } from 'react'
import type { FormEvent } from 'react'

import { useChatStore } from '../state/chatStore'
import { chatConnection } from '../websocket/ChatConnection'
import { StatusBadge } from './StatusBadge'

export function ConnectionPanel() {
  const [displayName, setDisplayName] = useState('')
  const wsUrl = useChatStore((state) => state.wsUrl)

  const connectionState = useChatStore((state) => state.connectionState)
  const reconnectAttempt = useChatStore((state) => state.reconnectAttempt)
  const error = useChatStore((state) => state.lastError)
  const kickedReason = useChatStore((state) => state.kickedReason)

  const isBusy = connectionState === 'connecting' || connectionState === 'reconnecting'
  const hasConfiguredEndpoint = wsUrl.trim().length > 0

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!hasConfiguredEndpoint) {
      return
    }

    chatConnection.connect(wsUrl, displayName)
  }

  return (
    <section className="connection-shell">
      <div className="connection-card">
        <p className="eyebrow">Chatterbox</p>
        <h1>Sign in to the room</h1>
        <p className="lede">
          Pick a display name, then register to start chatting.
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

          <button type="submit" disabled={isBusy || !hasConfiguredEndpoint}>
            {isBusy ? 'Connecting...' : 'Connect'}
          </button>
        </form>

        <div className="connection-meta">
          <StatusBadge state={connectionState} reconnectAttempt={reconnectAttempt} />
          {!hasConfiguredEndpoint ? (
            <p className="error-text">
              This deployment is missing a configured WebSocket endpoint.
            </p>
          ) : null}
          {error ? <p className="error-text">Error: {error}</p> : null}
          {kickedReason ? <p className="error-text">Session ended: {kickedReason}</p> : null}
        </div>
      </div>
    </section>
  )
}
