# Efficient Testing in BovineLabs.Timeline.PlayerInputs

### 1. Direct Compilation Check
```bash
dotnet build ../../BovineLabs.Timeline.PlayerInputs.csproj
```

### 2. Headless Test Execution
```bash
/home/i/Unity/Hub/Editor/6000.6.0a5/Editor/Unity \
    -runTests \
    -batchmode \
    -projectPath /home/i/GitHub/BovineLabs \
    -testResults TestResults_PlayerInputs.xml \
    -testFilter BovineLabs.Timeline.PlayerInputs.Tests \
    -testPlatform EditMode \
    -logFile -
```

### 3. Key Architecture Notes
- **InputRegistry:** Singleton mapping `PlayerId` (byte) to Provider Entity.
- **Join/Leave:** Handled via `PlayerJoined` and `PlayerLeft` singleton buffers.
- **InputAccess:** Static utility for resolving state/axes from the registry.
- **AI Handoff:** Managed via `Controllable` and `PlayerOverride` components.
