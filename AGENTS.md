# Repository Guidelines

## Project Structure & Module Organization
- `src/Server`: ASP.NET Core API + WebSocket hub (HTTP endpoints to validate and signal tasks).
- `src/Client`: WPF app (MVVM with `CommunityToolkit.MVVM`) hosted by .NET Generic Host; background WebSocket client.
- `src/Shared`: Contracts, DTOs, messages, and common utilities.
- `tests/Server.Tests`, `tests/Client.Tests`: xUnit test projects.
- `config/appsettings*.json`: Environment-specific settings (override with `appsettings.Development.json`).

## Build, Test, and Development Commands
- `dotnet build`: Compile all projects.
- `dotnet run --project src/Server/Server.csproj`: Start HTTP API and WebSocket hub.
- `dotnet run --project src/Client/Client.csproj`: Start WPF client with background host.
- `dotnet test`: Run all xUnit tests (Serilog logs included).
- `dotnet test /p:CollectCoverage=true`: Generate coverage (via coverlet, if configured).
- `dotnet format`: Apply code style and fix whitespace/imports.

## Coding Style & Naming Conventions
- C# 10+, nullable enabled; 4 spaces, no tabs.
- Types/methods: PascalCase; locals/params: camelCase; private fields: `_camelCase`.
- WPF: Views end with `View`, view-models end with `ViewModel`. Commands end with `Command`. Messages/DTOs end with `Request`/`Response`.
- Use Serilog for logging; prefer structured logs over comments (e.g., `Log.Information("Starting sync for {Directory}", path)`).

## Testing Guidelines
- Framework: xUnit; test files end with `*Tests.cs`; test names `Should_DoThing_When_Condition`.
- Server: tests for Task A endpoints (e.g., `ValidateUserCommand` placeholder returns true) and signaling.
- Client: tests for message handling, `service.run()` result routing, screenshot workflow, and sync logic.
- Prefer integration tests using `WebApplicationFactory` for server; use in-memory or test WebSocket clients.

## Commit & Pull Request Guidelines
- Use Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`.
- PRs include: concise description, linked issues, steps to validate, and screenshots/log excerpts when relevant.
- Keep changes small; add/adjust tests and update `appsettings.example.json` when config changes.

## Security & Configuration Tips
- Do not commit secrets; use user-secrets or environment variables. Example: `Serilog__MinimumLevel__Default=Information`.
- Windows services: configure restart-on-failure; ensure clients auto-reconnect to the hub.

