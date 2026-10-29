# Conquest regression checks

Build the main client first, restoring the solution as required:

```sh
dotnet restore DXClient.slnx -p:Configuration=UniversalGLRelease
dotnet build DXMainClient/DXMainClient.csproj -p:Configuration=UniversalGLRelease -f net8.0 --no-restore
dotnet run --project Tests/Conquest/Conquest.RegressionTests.csproj
```

This console harness runs against the built client assemblies without a test-framework dependency.
It checks purchases, ownership and level limits, AI purchases, neutral and human
defenses, capture, committed battle inventories, save compatibility, generated
fortifications, engine capability gates, and TS/YR adapter output. It does not
launch a game executable or client UI. Native TS/YR grants are checked against
bundled Terrace and Duel map terrain and overlay data, including object ownership,
reserved foundations, veterancy, save/load, malformed input, and native scoreboard
victory/defeat resolution. Live rendering and engine behavior require manual play.


Conquest performance budgets cover transient starting-force placement allocations
and repeated movement-preview queries. Print a warm placement benchmark without
launching any UI:

```sh
dotnet run --project Tests/Conquest/Conquest.RegressionTests.csproj -- --benchmark
```

The Pain Redefined regression reproduces the reported launch failure with the
three purchased Nod base buildings, then verifies both start locations, ground
buildability restrictions, and failed-launch save/retry behavior. Theater lookup
also checks TS/YR definition selection. No game process is started.
