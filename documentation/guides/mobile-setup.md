# Mobile Development Setup

## Prerequisites

- Install the chosen framework SDK (e.g., .NET 8 with MAUI workloads, React Native CLI, or Flutter SDK).
- Ensure Xcode and Android Studio command-line tools are available for iOS and Android builds.
- Configure environment variables pointing to the MT Transport API base URL (staging or local).

## Local API Access

1. Run the ASP.NET backend from `src/Program.cs` using `dotnet run`.
2. Update the mobile app `.env` or configuration file to reference `https://localhost:5001` (or your HTTPS dev port).
3. Trust development certificates on emulator/simulator devices to avoid TLS errors.

## Authentication

- Request a test user per role (`Owner`, `MinistryOfficer`, etc.) from the identity admin.
- For local work, enable password-based login; plan OAuth flows before production rollout.
- When implementing refresh tokens, verify expiry handling by simulating network drops.

## Testing Strategy

- Use platform emulators plus at least one physical device per platform for push notifications and camera features.
- Integrate unit tests for view models / state containers, and UI snapshot tests where supported.
- Automate end-to-end scenarios against the staging API before submitting mobile releases.

## Release Checklist

- Increment app version codes and names.
- Validate feature flags and API URLs for the target environment.
- Run accessibility audits and capture updated screenshots required by app stores.
