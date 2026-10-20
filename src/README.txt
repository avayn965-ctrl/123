RefreshAncient compatibility/features patch for STS2 v0.111.0.

Requirements: .NET SDK 9, Python 3 for packaging, Mono.Cecil 0.11.6
(automatically restored by dotnet build), ORIGINAL RefreshAncient 1.4.3 ZIP,
and the user's sts2.dll v0.111.0. Input game/mod DLLs are not in this source.

From the repository root, build, validate and package compat.6:
  python tools/build_release.py --game-dll PATH/TO/sts2.dll --original-mod PATH/TO/ORIGINAL.zip

Optional: --dotnet PATH/TO/dotnet --output-dir PATH/TO/OUTPUT
Native window tests: --godot PATH/TO/Godot_v4.5.1-stable_mono_linux.x86_64
Use Godot 4.5.1 Mono (the .NET build). Both source and merged helpers are
tested in the engine, using the fake game API from tests/Fixtures.cs.
The UI test project is ui-tests/UiTests.csproj; it is built automatically
when --godot is supplied. Without that argument native tests are marked unrun.
The script produces the binary ZIP, full source ZIP and validation report.

Run the new feature tests without game files:
  dotnet run --project tests/FeatureTests.csproj

The script applies the compat.1 fix to the original input first and verifies it,
then merges the dependency-free runtime helper into the mod. Feature tests run
against both helper source and the helper embedded in the final mod DLL.
No additional helper DLL is required for installation.

Manual commands after building src, runtime and tests with -c Release:
  dotnet src/bin/Release/net9.0/Patcher.dll patch ORIGINAL.dll GAME_sts2.dll BASELINE.dll
  dotnet src/bin/Release/net9.0/Patcher.dll verify BASELINE.dll GAME_sts2.dll ORIGINAL.dll
  dotnet src/bin/Release/net9.0/Patcher.dll features BASELINE.dll GAME_sts2.dll OUTPUT.dll runtime/bin/Release/net9.0/RuntimeFeatures.dll
  dotnet src/bin/Release/net9.0/Patcher.dll verify-features OUTPUT.dll GAME_sts2.dll BASELINE.dll
  dotnet tests/bin/Release/net9.0/sts2.dll OUTPUT.dll

The test executable is deliberately named sts2.dll and defines tiny substitutes
for the game API. It must NEVER be placed in the actual game directory.
It is not the uploaded game DLL and is not included in the binary mod ZIP.

27 logic cases and 63 optional native Godot UI checks run in both modes.
These checks do not launch the real game or test gameplay/networking.
Option history is in memory per event instance. Ancient history is per
run/player/room, surviving model replacements. Game/save reload resets both.
Pre-update choices cannot be recovered from the existing save.
Assembly MVID is regenerated, so independent builds may have different hashes.

Visual catalogs copy only the live event option background and rich-text
visual nodes, using Godot Duplicate(0) to omit scripts and signal connections.
Relic icons use Icon/IconOutline; Ancient miniatures use RunHistoryIcon and
RunHistoryIconOutline, matching the top-bar room icon (not the map portrait).
Game custom text effects, effective fonts and outline/shadow colors are kept.
The actual reward-selection button is never duplicated or reparented.

The popup header has a bounded height to prevent the initial zero-width
FitContent pass from forcing the Window thousands of pixels off-screen.
Window input is processed independently of paused gameplay. The scroll area
receives initial focus after layout and supports Page Up/Down and Home/End.
Option cards have hover-only input and pass wheel events to the scroller.
NHoverTipSet receives the original option HoverTips. Its output is reparented
to a CanvasLayer in the catalog Window so it is visible above the exclusive
modal. Cleanup runs on mouse exit, scroll, close and card removal.
The native harness uses a stand-in for that game factory; its real signatures
are audited against the supplied game DLL. Actual game scenes are untested.
H is polled through the original per-frame UI update hook, with rising-edge
detection and text-editor focus protection. Both paid click guards honor it.

ButtonPlacement saves normalized coordinates for each of the four buttons
in user://refreshancient-button-positions.cfg, using an atomic temp-file move.
Native save/read probes run as separate Godot processes, testing real restart
persistence and viewport resizing. J edits placement; K removes overrides.
The keyboard settings and existing price fields are added through the original
ModConfig BuildEntries hook. Editing a price activates Custom. Labels and both
original price resolvers use ModSettings.Cost; host-supplied resolved costs
and the existing protocol/refunds remain intact. Matching prices are needed
on all peers; placement/keybindings are local.
Catalog order is stable within seen/current/available/unavailable groups.
Reward frames use red/green/yellow/gray borders; underlying models stay paired
with their original icons, descriptions and hover-tip data.
