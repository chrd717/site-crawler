# Project Template

## Bootstrap commands

```bash
dotnet new globaljson --sdk-version <installed-10.0.x-sdk> --roll-forward latestPatch

dotnet new sln -n BrightCrawler

dotnet new console -n BrightCrawler.App -o src/BrightCrawler.App
dotnet new classlib -n BrightCrawler.Core -o src/BrightCrawler.Core
dotnet new classlib -n BrightCrawler.Infrastructure -o src/BrightCrawler.Infrastructure

dotnet new xunit -n BrightCrawler.UnitTests -o tests/BrightCrawler.UnitTests
dotnet new xunit -n BrightCrawler.IntegrationTests -o tests/BrightCrawler.IntegrationTests

dotnet sln add src/BrightCrawler.App
dotnet sln add src/BrightCrawler.Core
dotnet sln add src/BrightCrawler.Infrastructure
dotnet sln add tests/BrightCrawler.UnitTests
dotnet sln add tests/BrightCrawler.IntegrationTests

dotnet add src/BrightCrawler.App reference src/BrightCrawler.Core
dotnet add src/BrightCrawler.App reference src/BrightCrawler.Infrastructure
dotnet add src/BrightCrawler.Infrastructure reference src/BrightCrawler.Core

dotnet add tests/BrightCrawler.UnitTests reference src/BrightCrawler.Core
dotnet add tests/BrightCrawler.IntegrationTests reference src/BrightCrawler.Core
dotnet add tests/BrightCrawler.IntegrationTests reference src/BrightCrawler.Infrastructure
```

Run `dotnet --list-sdks`, choose an installed .NET 10 SDK, and replace the placeholder. Keep the selected SDK committed in `global.json`.

## Dependency rule

```text
App -> Core
App -> Infrastructure
Infrastructure -> Core
Core -> nothing in the solution
```

No project named `Shared`, `Common`, `Abstractions`, `Contracts`, `Repositories`, or `Services` should be added without a concrete second use case.

## `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

Do not enable preview language features. Pin package versions in `Directory.Packages.props` and commit the lock file if package locking is enabled.

## Runtime dependencies

Keep the production dependency list short:

```text
Microsoft.Extensions.Hosting
Microsoft.Extensions.Http
System.Threading.RateLimiting
Npgsql
AngleSharp
MetadataExtractor
PdfPig
```

Test-only:

```text
Microsoft.NET.Test.Sdk
xUnit
Testcontainers.PostgreSql
```

Do not add a package when the platform or a small local type already solves the requirement.
