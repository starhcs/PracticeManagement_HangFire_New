# Technology versions

Versions documented for the development branch, verified on 2026-09-18.

| Technology | Version | Source |
| --- | --- | --- |
| .NET target framework | net6.0 (.NET 6) | HangfireNew/HangfireNew.csproj |
| .NET SDK for PR validation | 6.0.x | .github/workflows/pr-validation.yml |
| Hangfire | 1.8.2 | HangfireNew/HangfireNew.csproj |
| Hangfire.AspNetCore | 1.8.2 | HangfireNew/HangfireNew.csproj |
| Hangfire.Core | 1.8.2 | HangfireNew/HangfireNew.csproj |
| Hangfire.SqlServer | 1.8.2 | HangfireNew/HangfireNew.csproj |

CI restores and builds HangfireNew.sln in Release configuration. actions/setup-dotnet selects the SDK patch version dynamically within 6.0.x. This CI setting does not establish the runtime version used by deployed environments.

This repository is a .NET application and does not use React or Node.js for its PR-validation build. Update this document when changing package references, the target framework, or the CI SDK version.
