import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'

import type { ChatEntry } from '../types/chat'

interface ChatWindowProps {
  currentUser: string
  selectedUser: string | null
  conversation: ChatEntry[]
  onSendMessage: (text: string) => void
}

export function ChatWindow({
  currentUser,
  selectedUser,
  conversation,
  onSendMessage,
}: ChatWindowProps) {
  const [draft, setDraft] = useState('')
  const messageListRef = useRef<HTMLDivElement>(null)

  const title = selectedUser
    ? `Chat with ${selectedUser}`
    : 'Choose a user to start chatting'

  const sortedConversation = useMemo(
    () => [...conversation].sort((a, b) => a.timestamp - b.timestamp),
    [conversation],
  )

  useEffect(() => {
    const messageList = messageListRef.current
    if (messageList) {
      messageList.scrollTop = messageList.scrollHeight
    }
  }, [sortedConversation])

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()

    const text = draft.trim()
    if (!text || !selectedUser) {
      return
    }

    onSendMessage(text)
    setDraft('')
  }

  return (
    <section className="chat-window" aria-live="polite">
      <header className="chat-window-header">
        <h2>{title}</h2>
        {selectedUser ? <p>Signed in as {currentUser}</p> : null}
      </header>

      <div className="message-list" role="log" ref={messageListRef}>
        {selectedUser && sortedConversation.length === 0 ? (
          <p className="empty-state">No messages yet. Say hello.</p>
        ) : null}

        {!selectedUser ? <p className="empty-state">Select a user from the list.</p> : null}

        {sortedConversation.map((message) => (
          <article
            key={message.id}
            className={
              message.direction === 'incoming' ? 'message incoming' : 'message outgoing'
            }
          >
            <header>
              <strong>{message.direction === 'incoming' ? message.from : 'You'}</strong>
              <time>{new Date(message.timestamp).toLocaleTimeString()}</time>
            </header>
            <p>{message.text}</p>
          </article>
        ))}
      </div>

      <form className="message-form" onSubmit={onSubmit}>
        <input
          type="text"
          placeholder={selectedUser ? `Message ${selectedUser}` : 'Select a user first'}
          value={draft}
          disabled={!selectedUser}
          onChange={(event) => setDraft(event.target.value)}
          maxLength={1000}
        />
        <button type="submit" disabled={!selectedUser || draft.trim().length === 0}>
          Send
        </button>
      </form>
    </section>
  )
}
