import type { UserPresence } from '../types/chat'

interface SidebarProps {
  users: UserPresence[]
  currentUser: string
  selectedUser: string | null
  unreadByUser: Record<string, number>
  onSelectUser: (displayName: string) => void
  onRefreshUsers: () => void
}

export function Sidebar({
  users,
  currentUser,
  selectedUser,
  unreadByUser,
  onSelectUser,
  onRefreshUsers,
}: SidebarProps) {
  const onlineUsers = users.filter((user) => user.displayName !== currentUser)

  return (
    <aside className="chat-sidebar">
      <div className="sidebar-header">
        <h2>Online users</h2>
        <button type="button" className="ghost" onClick={onRefreshUsers}>
          Refresh
        </button>
      </div>

      {onlineUsers.length === 0 ? (
        <p className="empty-state">No other users online right now.</p>
      ) : (
        <ul className="user-list">
          {onlineUsers.map((user) => {
            const isSelected = selectedUser === user.displayName
            const unreadCount = unreadByUser[user.displayName] ?? 0
            const hasUnread = unreadCount > 0 && !isSelected
            return (
              <li key={user.displayName}>
                <button
                  type="button"
                  className={`user-row${isSelected ? ' selected' : ''}${hasUnread ? ' unread' : ''}`}
                  onClick={() => onSelectUser(user.displayName)}
                >
                  <div className="user-row-title">
                    <strong>{user.displayName}</strong>
                    {hasUnread ? <span className="unread-badge">{unreadCount}</span> : null}
                  </div>
                  <small>
                    online since {new Date(user.connectedAt).toLocaleTimeString()}
                  </small>
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </aside>
  )
}
