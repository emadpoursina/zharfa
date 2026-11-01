# Zharfa desktop shell — `Zharfa.Desktop` (WPF + WebView2, net10.0-windows)

Thin Windows shell: one window hosting the shared Persian RTL web frontend.
Confirmed (FR-015): The shell only navigates a WebView2 control to `ZHARFA_FRONTEND_URL`
(default `http://localhost:5173` dev, `http://localhost:8080` against Compose), holding
NO business logic, NO authorization, NO database access, and NO secrets. All authorization,
KPI calculations, and data access remain strictly server-side.

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

The Windows-only run verification (WebView2 Runtime) is executed on a Windows host or CI per the locked plan-around; this Linux host compiles it only.
