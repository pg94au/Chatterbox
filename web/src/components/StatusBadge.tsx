import type { ConnectionState } from '../types/chat'

interface StatusBadgeProps {
  state: ConnectionState
  reconnectAttempt: number
}

const labels: Record<ConnectionState, string> = {
  disconnected: 'Disconnected',
  connecting: 'Connecting',
  connected: 'Connected',
  reconnecting: 'Reconnecting',
  failed: 'Connection failed',
}

export function StatusBadge({ state, reconnectAttempt }: StatusBadgeProps) {
  return (
    <div className={`status-badge status-${state}`}>
      <span className="status-dot" aria-hidden="true" />
      <span>{labels[state]}</span>
      {state === 'reconnecting' && reconnectAttempt > 0 ? (
        <small>attempt {reconnectAttempt}</small>
      ) : null}
    </div>
  )
}
