# Mobile Application Architecture

## Goals

- Provide a native-quality experience for vehicle owners and ministry personnel on iOS and Android.
- Reuse existing ASP.NET backend services for authentication, role management, and vehicle registration workflows.
- Maintain consistent authorization policies with the web portal while tailoring UX for mobile usage contexts.

## High-Level Diagram

```
Mobile Client (iOS / Android)
        |
        |  HTTPS + OAuth2 / JWT
        v
ASP.NET Backend (Controllers, Identity, EF Core)
        |
        |  SQL Queries via EF Core
        v
SQL Server Database
```

## Client Architecture Considerations

- **Framework:** Decide between cross-platform (e.g., .NET MAUI, React Native, Flutter) or fully native (Swift/Kotlin) based on team skillset and UI requirements. See the evaluation table below for initial guidance.
- **State Management:** Adopt a predictable pattern (MVVM, Redux-style store, BLoC) to keep identity state and vehicle registration data in sync with backend responses.
- **Offline Mode:** Identify read-only screens (dashboard metrics, submitted applications) that benefit from caching. Define synchronization policies when connectivity is restored.
- **Security:** Store tokens using platform secure storage (Keychain / Keystore). Enforce device-level protection (PIN/biometrics) where applicable.

## Backend Integration

- Reuse existing Identity roles (`Admin`, `DocumentVerifier`, `FinalApprover`, `MinistryOfficer`, `Owner`) to drive feature availability in the mobile app.
- Expose API endpoints parallel to the MVC controllers (`VehicleController`, `UsersController`, etc.) or introduce dedicated `/api/*` controllers returning JSON.
- Add mobile-specific scopes or audiences to the authentication pipeline if using JWT tokens. Ensure refresh token support for long-lived sessions.
- Implement rate limiting and telemetry (Application Insights or similar) to monitor mobile traffic separately from web.

## Feature Parity Plan

1. **Phase 1 – Read-Only:** Vehicle status lookups, pending application lists, dashboard summaries.
2. **Phase 2 – Interactive Workflows:** Submit new vehicle registrations, upload documents, approve or reject submissions.
3. **Phase 3 – Device Capabilities:** Push notifications for status updates, camera integration for document capture, biometric login.

## Deployment Strategy

- Establish CI pipelines that build the mobile app alongside web deployments. Use environment variables to target staging vs production APIs.
- For cross-platform frameworks, generate separate artifacts (`.ipa`, `.aab`) and publish through App Store / Google Play.
- Tag backend releases when API contracts change, and communicate version compatibility to the mobile team.

## Framework Evaluation

| Option        | Strengths | Risks | Notes |
|---------------|-----------|-------|-------|
| .NET MAUI     | Shared .NET skills with existing backend; single codebase for desktop/tablet; native UI wrappers | Still maturing; limited third-party control ecosystem compared to React Native / Flutter | Strong alignment with current stack; verify required mobile controls are stable. |
| React Native  | Large ecosystem; fast iteration with Metro bundler; broad hiring pool | Requires JavaScript/TypeScript proficiency; bridging native modules can be complex | Consider if front-end team prefers JS/TS; evaluate integration with existing .NET auth flows. |
| Flutter       | Consistent UI across platforms; excellent performance with Skia; strong tooling | Dart learning curve; heavier app bundle sizes; platform-specific integrations require channel plumbing | Good for custom UI; assess whether team can invest in Dart expertise. |
| Native (Swift/Kotlin) | Maximum platform fidelity; granular access to device APIs; long-term stability | Two codebases to staff; higher initial velocity cost | Consider if regulatory or UX reasons demand platform-specific experiences. |

### Decision Checklist

- Which option minimizes skill gaps for current developers?
- Does the framework offer first-class support for required native capabilities (camera, secure storage, push notifications)?
- What is the long-term maintenance cost and community support outlook?
- Can the CI/CD pipeline compile and sign artifacts for both stores without manual steps?

Document the final decision, rationale, and onboarding materials once the framework is selected.

## Team Findings (Populate as evaluations complete)

| Criteria | .NET MAUI | React Native | Flutter | Native |
|----------|-----------|--------------|---------|--------|
| Team familiarity | _(Pending)_ | _(Pending)_ | _(Pending)_ | _(Pending)_ |
| Prototype outcome | _(Pending)_ | _(Pending)_ | _(Pending)_ | _(Pending)_ |
| Required device integrations | _(Pending)_ | _(Pending)_ | _(Pending)_ | _(Pending)_ |
| Estimated delivery timeline | _(Pending)_ | _(Pending)_ | _(Pending)_ | _(Pending)_ |
| Notable risks / blockers | _(Pending)_ | _(Pending)_ | _(Pending)_ | _(Pending)_ |

## Open Questions

- Which mobile framework aligns best with the current team's expertise?
- Do we need separate API rate limits or throttling rules for mobile clients?
- What analytics platform will capture mobile usage and crash reports?
- Are there regulatory requirements for storing or transmitting user data on mobile devices?
