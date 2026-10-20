Reproducible binary compatibility patch for RefreshAncient 1.4.3.

Requirements: .NET SDK 9, Mono.Cecil 0.11.6 (restored by dotnet build),
the ORIGINAL RefreshAncient.dll and the user's sts2.dll v0.111.0.
Neither input DLL is included in this source archive.

Build:
  dotnet build -c Release

Patch (write to a separate path; keep the original mod):
  dotnet bin/Release/net9.0/Patcher.dll patch ORIGINAL_MOD.dll GAME_sts2.dll OUTPUT_MOD.dll

Verify:
  dotnet bin/Release/net9.0/Patcher.dll audit OUTPUT_MOD.dll GAME_sts2.dll
  dotnet bin/Release/net9.0/Patcher.dll verify OUTPUT_MOD.dll GAME_sts2.dll ORIGINAL_MOD.dll

The patcher refuses unexpected source API findings and expects exactly two
callers of the replacement implementation. Verification executes extracted
patched IL against isolated API substitutes, including an actual JIT failure.
It does not load/execute the uploaded game or mod or test gameplay/networking.

Packaging: retain the original RefreshAncient.pck; set the JSON manifest version
to 1.4.3-compat.1 and min_game_version to 0.111.0. Assembly MVID is regenerated,
so independently produced binaries can have different SHA-256 hashes.
