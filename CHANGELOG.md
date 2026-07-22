# Changelog

All notable BoplEight changes are documented here.

## 1.0.0 - 2026-07-22

- Added synchronized two-through-eight-player Steam lobbies.
- Added protocol v3 peer compatibility, exact-roster prepare/commit transactions, acknowledgements, retries, and fail-stop teardown.
- Replaced four-slot frame aggregation and simulation input application while retaining vanilla per-player transport.
- Expanded character selection, team palettes, player spawning, winner UI, ability UI, and physics hit buffers to eight slots.
- Disabled incompatible four-slot replay recording during BoplEight matches.
- Added the self-contained BepInEx and BoplEight Windows installer.
- Added protocol, roster, layout, compatibility, palette, installer, and packaged-payload tests.
