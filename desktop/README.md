# Zharfa desktop shell — `Zharfa.Desktop` (WPF + WebView2, net10.0-windows)

Thin Windows shell: one window hosting the shared Persian RTL web frontend.
No business logic, no authorization, no database access — all of that stays in
the frontend and the backend API (Demo 1 PRD §3.4).

## Configuration

| Variable | Meaning | Default |
|---|---|---|
| `ZHARFA_FRONTEND_URL` | URL of the shared frontend | `http://localhost:5173` (dev) |

Against the Compose deployment set it to `http://localhost:8080`.

## Build

```bash
dotnet build desktop/Zharfa.Desktop    # compiles on any OS (EnableWindowsTargeting)
```

Running requires Windows with the WebView2 Runtime (Demo 1 risk noted in the
PRD §12 — validate packaging early). Packaging/installers are pilot scope.
