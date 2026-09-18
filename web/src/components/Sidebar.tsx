import type { UserPresence } from '../types/chat'

interface SidebarProps {
  users: UserPresence[]
  currentUser: string
  selectedUser: string | null
  onSelectUser: (displayName: string) => void
  onRefreshUsers: () => void
}

export function Sidebar({
  users,
  currentUser,
  selectedUser,
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
            return (
              <li key={user.displayName}>
                <button
                  type="button"
                  className={isSelected ? 'user-row selected' : 'user-row'}
                  onClick={() => onSelectUser(user.displayName)}
                >
                  <strong>{user.displayName}</strong>
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
