# Chatterbox Web Client

React + TypeScript client for the existing Chatterbox WebSocket backend.

This project is intentionally isolated in its own folder and dependency graph:
- no shared code with the backend
- not part of the Visual Studio solution
- ephemeral in-browser message history only

## Features implemented

- Prompt for display name at startup
- Send `register` command over WebSocket
- Automatically send `listUsers` after registration
- Render online user list
- Per-user chat thread in memory
- Send `message` command to selected user
- Disconnect button that closes WebSocket (triggers server `$disconnect`)
- Connection state indicator with basic reconnect behavior

## Protocol expected by backend

Client commands:
- `{ "action": "register", "displayName": "Alice" }`
- `{ "action": "listUsers" }`
- `{ "action": "message", "to": "Bob", "text": "Hello" }`

Server events:
- `registered`
- `users`
- `userJoined`
- `userLeft`
- `message`
- `error`
- `kicked`

## Quick start

1. Install dependencies:

```bash
npm install
```

2. Configure WebSocket endpoint:

```bash
cp .env.example .env
```

Set `VITE_CHATTERBOX_WS_URL` in `.env`.

3. Start development server:

```bash
npm run dev
```

4. Build production bundle:

```bash
npm run build
```

## Project structure

```text
src/
  components/
  state/
  websocket/
  types/
```

Architecture:
- UI components dispatch intent
- Zustand store holds application state
- Singleton `ChatConnection` manages socket lifecycle and protocol traffic
