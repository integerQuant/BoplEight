# Changelog

All notable BoplEight changes are documented here.

## 1.0.7 - 2026-07-23

- Fixed character-selection layout using an already-animated remote position, which placed Click to Join above the screen.

## 1.0.6 - 2026-07-22

- Fixed Click to Join remaining off-screen under either Unity component initialization order.

## 1.0.5 - 2026-07-22

- Restored the initial Click to Join control while keeping unentered team and item selectors hidden.

## 1.0.4 - 2026-07-22

- Fixed the initial lobby showing an uninitialized team/item selector template until the local player navigated the menu.

## 1.0.3 - 2026-07-22

- Restored fully hidden local and remote character-card states while retaining the compact eight-column layout.
- Fixed player five through eight cards showing no choosing status before readying.
- Compacted all-player score portrait cards without overlapping them.
- Reduced five-through-eight-player round transition delay by exiting the winner splash during roster synchronization.

## 1.0.2 - 2026-07-22

- Fixed ready and selection updates being rejected when players used valid cosmetic colors 8 through 11.
- Fixed high-color rosters failing during initial match start and next-level validation.
- Fixed asynchronous Steam avatars disappearing when an earlier player's avatar was still loading.
- Changed character, avatar, and ability selectors to a fitted eight-column layout.

## 1.0.1 - 2026-07-22

- Fixed Steam friend invites being rejected before their BoplEight lobby metadata was downloaded.
- Added a bounded metadata refresh before joining while preserving vanilla and mismatched-lobby rejection.

## 1.0.0 - 2026-07-22

- Added synchronized two-through-eight-player Steam lobbies.
- Added protocol v3 peer compatibility, exact-roster prepare/commit transactions, acknowledgements, retries, and fail-stop teardown.
- Replaced four-slot frame aggregation and simulation input application while retaining vanilla per-player transport.
- Expanded character selection, team palettes, player spawning, winner UI, ability UI, and physics hit buffers to eight slots.
- Disabled incompatible four-slot replay recording during BoplEight matches.
- Added the self-contained BepInEx and BoplEight Windows installer.
- Added protocol, roster, layout, compatibility, palette, installer, and packaged-payload tests.
