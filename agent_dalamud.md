# Agent briefing: Dalamud plugin creation (general)

Applies to any FFXIV Dalamud plugin (C#, .NET, Windows). Verified against
live XIVLauncher/Dalamud (API 15 era). This repo's plugin follows all of it.

## 1. Project setup

- SDK: .NET SDK (any recent; plugin targets `net10.0-windows` here, but the
  SHIPPED patcher targets `net8.0` because that's what XIVLauncher machines
  guarantee — same logic applies if you ship helper exes).
- Template: `dotnet new dalamud` (Dalamud.NET SDK) or hand-rolled csproj with
  `DalamudPackager` + references into `$(appdata)\XIVLauncher\addon\Hooks\dev\`
  (Dalamud.dll, Dalamud.Bindings.ImGui, FFXIVClientStructs, Lumina…).
  Those references are `Private:false` — never ship Dalamud's own dlls.
- Ship alongside the plugin dll: your `.json` manifest, `.pdb` (stack
  traces), `deps.json`, and third-party deps you actually load (ECommons).
  Never ship `bin/`, `obj/`, or dev-only files.

## 2. Plugin skeleton

- Implement `IDalamudPlugin` (`Name` property). Constructor injection:
  `IPluginInterface`, `ICommandManager`, `IFramework`, `IClientState`,
  `ICondition`, `IChatGui`, `IGameGui`, `IObjectTable`, `ISigScanner`,
  `IDataManager`, `IPluginLog`.
- `Dispose()` MUST unregister everything: `-= OnUpdate`, remove windows from
  `WindowSystem`, `CommandManager.RemoveHandler`, unhook every hook, dispose
  patch objects. Leaked hooks = crashes on reload.
- UI: `WindowSystem` + `Window` subclasses. **ImGui comes from
  `Dalamud.Bindings.ImGui` — never the vanilla ImGui NuGet** (version
  mismatch = instant crash). Prefer safe primitives; some payload/hover APIs
  are unstable across Dalamud builds.
- Config: `IPluginConfiguration` + `SavePluginConfig`. Version your schema;
  stamp provenance (build tag) on anything users will share.

## 3. Game interaction rules

- You run INSIDE the game process: `Environment.ProcessPath` is always the
  game dir (no install-path assumptions, ever — Steam/SE launcher, any drive).
- `LocalPlayer` is OFTEN null (title screen, loading, logout). Null-check
  every frame; gate world logic on logged-in + real territory + not
  `BetweenAreas`.
- `TerritoryType` flaps through 0 mid-load — debounce or explicitly ignore 0.
- `ConditionFlag` (ClientState.Conditions) is the blessed sensor API
  (InCombat, Mounted, Diving=81, InFlight=77, …). Verify member names against
  the Dalamud assembly/source; values change between expansions.
- Framework `Update` runs off the game thread: keep it fast, never block,
  never call game functions that aren't thread-safe. `dt` clamp (~0.5s);
  hitches exist — never assume wall-clock/frame parity.
- Memory (sig scan, hooks via GameInteropProvider/ECommons): VERIFY original
  bytes before touching anything; external tools (Weatherman etc.) reassert
  their patches every frame — loser-yields logic, never patch wars. ECommons
  verifies noisily inside constructors: pre-verify silently yourself first,
  construct lazily, only after your check passes.
- Chat: `ChatGui.Print` for command replies/errors only. Never spam state
  changes (zone/pause/load) — users hate it, and it buries real errors.

## 4. Commands, IPC, coexistence

- Slash commands via `CommandManager.AddHandler` (`/xyz`, help text).
  For GAME commands use `UIModule.ProcessChatBoxEntry`, not
  `CommandManager.ProcessCommand` (drops game commands).
- Cross-plugin talk: Dalamud IPC (`GetIpcSubscriber`) + CallGate IPC
  (QoLBar-style). External providers register LATE — never latch a failed
  subscribe; retry throttled. Names/ids can reorder — don't assume stability.
- Hotkeys: `GetAsyncKeyState` P/Invoke + modifier tracking; holds, not taps
  (polls miss 1-frame presses); suppress while typing (`WantTextInput`).

## 5. Dev loop

- Deploy to `%AppData%\XIVLauncher\devPlugins\<Name>\`, enable dev plugins,
  reload in `/xlplugins` (sometimes needs full game restart — stale builds
  fake success; byte-verify your build tag in the dll when the UI disagrees
  with the code).
- Logs: `%AppData%\XIVLauncher\dalamud.log`. Dalamud default level hides
  yours — log diagnostics at Warning+.
- One-shot diagnostics must self-remove or demote; log spam rots trust.

## 6. Publishing (custom repo)

- `repo.json` = bare JSON ARRAY at a raw URL (Blender-style `{"data":…}`
  objects fail to parse — must be `[...]`). Per-entry: Author, Name,
  Description, Punchline, InternalName (NEVER rename after shipping),
  AssemblyVersion (4-PART, e.g. `0.9.315.0`), RepoUrl, ApplicableVersion
  `"any"`, Tags, DalamudApiLevel (current: 15), IsHide/IsTestingExclusive
  (strings `"False"` work), LastUpdated (unix), DownloadLinkInstall/Testing/
  Update → GitHub Release zips. Optional: IconUrl (square PNG), Changelog,
  TestingAssemblyVersion.
- Version agreement is load-bearing: repo `AssemblyVersion` == dll assembly
  version == manifest `AssemblyVersion`, EXACT string match, or installs
  refuse (`Distributed plugin version does not match repo version`). Set the
  real version in csproj (`<Version>`) — the default `1.0.0.0` update-loops.
  CDN/proxy lag after replacing an asset can fake a stale failure; verify
  the live bytes before debugging further.
- Users: `/xlsettings` → Experimental → Custom Plugin Repositories → raw
  `repo.json` URL → `/xlplugins` install. Testing builds need the opt-in.
- Per release: bump versions (csproj + manifest + repo entry + links),
  fresh zip, new GitHub Release, update `repo.json`. Consider the official
  repo eventually (restrictions + approval + AI-use policy apply).

## 7. Failure etiquette

- Silent fallbacks for degraded states (missing files, unknown zones);
  visible status for user-fixable problems (one worst-issue line beats ten
  popups). Refusals (external override engaged, missing data) must say WHY
  and what to do. Debug UI behind a flag, default off.
