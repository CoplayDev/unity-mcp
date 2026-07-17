---
id: releases
slug: /releases
title: Release Notes
sidebar_label: Releases
description: Full version-by-version change history for MCP for Unity.
---

# Release Notes

Latest releases land in [`beta`](https://github.com/CoplayDev/unity-mcp/tree/beta) before promotion to [`main`](https://github.com/CoplayDev/unity-mcp/tree/main). Major breaking changes get a dedicated migration guide under [Migrations](/migrations/v5).

For the canonical changelog with PR links, see [GitHub Releases](https://github.com/CoplayDev/unity-mcp/releases).

> Auto-generated from the GitHub Releases API by `tools/sync_release_notes.py`. Do not hand-edit — changes will be overwritten on the next sync.


## v10.0 series

### [v10.0.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v10.0.0) — 2026-06-30

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.7.3) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1205
* feat(asset-gen): AI Asset Generation — 3D gen/import + 2D image (BYO-key) by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1218
* chore: update Unity package to beta version 9.7.4-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1221
* Revamp brand, docs, distribution, and analytics by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1222
* chore: update Unity package to beta version 9.7.4-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1223
* chore: bump version to 10.0.0 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1224

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.7.3...v10.0.0

</details>


## v9.7 series

### [v9.7.3](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.7.3) — 2026-06-15

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.7.1) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1159
* fix(build): avoid compile-time VisionOS enum references by @JMartinezRuiz in https://github.com/CoplayDev/unity-mcp/pull/1113
* chore: update Unity package to beta version 9.7.2-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1161
* fix(compat): keep Runtime helpers compiling when built-in modules are disabled (#1160) by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1162
* chore: update Unity package to beta version 9.7.2-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1163
* fix(screenshot): wait for end-of-frame before composited capture by @KamilDev in https://github.com/CoplayDev/unity-mcp/pull/1132
* chore: update Unity package to beta version 9.7.2-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1165
* fix(prefabs): wire missing C# handler for editor/prefab-stage resource by @slikk66 in https://github.com/CoplayDev/unity-mcp/pull/1136
* chore: update Unity package to beta version 9.7.2-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1166
* fix: declare required Unity module dependencies by @sean2077 in https://github.com/CoplayDev/unity-mcp/pull/1122
* chore: update Unity package to beta version 9.7.2-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1167
* refactor: drop defensive scaffolding made obsolete by module dep declarations by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1168
* chore: update Unity package to beta version 9.7.2-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1169
* fix(execute_code): route CodeDom references through a response file (#1144) by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1170
* chore: update Unity package to beta version 9.7.2-beta.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1171
* feat: add Kimi Code CLI client configurator by @namquang93 in https://github.com/CoplayDev/unity-mcp/pull/1119
* chore: update Unity package to beta version 9.7.2-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1174
* fix: skip unsafe Fusion serialization types by @cyanxwh in https://github.com/CoplayDev/unity-mcp/pull/1127
* chore: update Unity package to beta version 9.7.2-beta.10 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1190
* fix(stdio): retry same port on bind race instead of silent fallback (#1173) by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1198
* chore: update Unity package to beta version 9.7.2-beta.11 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1200
* Patch: headless http server launch, Kilo Code (#1120), e2e bridge harness by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1201
* chore: update Unity package to beta version 9.7.2-beta.12 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1202
* Fix Memory Profiler snapshot actions on Unity 6 by @DLSinnocence in https://github.com/CoplayDev/unity-mcp/pull/1125
* chore: update Unity package to beta version 9.7.2-beta.13 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1203
* chore: bump version to 9.7.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1204

## New Contributors
* @KamilDev made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1132
* @slikk66 made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1136
* @sean2077 made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1122
* @namquang93 made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1119
* @DLSinnocence made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1125

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.7.1...v9.7.3

</details>

### [v9.7.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.7.1) — 2026-05-24

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.7.0) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1149
* fix(clients): point Antigravity at ~/.gemini/config/ after the 2.x migration by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1151
* chore: update Unity package to beta version 9.7.1-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1153
* fix(ui): default the per-client setup foldout to expanded so Unregister is visible by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1152
* chore: update Unity package to beta version 9.7.1-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1154
* fix(roslyn): install missing System.Runtime.CompilerServices.Unsafe v6 + surface inner errors by @sMartz1 in https://github.com/CoplayDev/unity-mcp/pull/1116
* chore: update Unity package to beta version 9.7.1-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1156
* docs: clarify development setup and package-source checks by @JMartinezRuiz in https://github.com/CoplayDev/unity-mcp/pull/1101
* [UPDATE] Wiki/Doc by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1157
* chore: bump version to 9.7.1 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1158

## New Contributors
* @JMartinezRuiz made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1101

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.7.0...v9.7.1

</details>

### [v9.7.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.7.0) — 2026-05-22

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.6.8) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1087
* feat: Add configurable init_timeout for PlayMode test initialization by @Emerix in https://github.com/CoplayDev/unity-mcp/pull/1021
* chore: update Unity package to beta version 9.6.9-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1093
* chore: update Unity package to beta version 9.6.9-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1094
* Update0503 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1097
* chore: update Unity package to beta version 9.6.9-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1098
* fix: include UI Toolkit overlays in game_view screenshots with include_image by @KennerMiner in https://github.com/CoplayDev/unity-mcp/pull/1040
* chore: update Unity package to beta version 9.6.9-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1099
* fix: unblock beta compile and gate releases on test success by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/1103
* chore: update Unity package to beta version 9.6.9-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1104
* fix: close 2022.3 compile gap in UnityFindObjectsCompat.FindAll by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/1106
* ci(unity-tests): include MCPForUnity/Runtime/** in trigger paths by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/1108
* chore: update Unity package to beta version 9.6.9-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1109
* Readme-cleanup by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1131
* fix: enable project-scoped custom tools in stdio mode by @Warlander in https://github.com/CoplayDev/unity-mcp/pull/1111
* chore: update Unity package to beta version 9.6.9-beta.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1133
* ci: tier Unity test matrix + local parity check (#1107) by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1139
* ci: rename 'run-wide-matrix' label to 'full-matrix' by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1141
* ci: surface failing test details + fix Unity 6.4 USS log assertion by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1140
* ci: fire unity-tests on every PR (mirrors python-tests pattern) by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1143
* feat+fix: one-click client connection + autotest bug-fix batch by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1142
* chore: update Unity package to beta version 9.6.9-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1145
* ci: stop firing unity-tests/python-tests twice on beta and main pushes by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1147
* chore: bump version to 9.7.0 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1148

## New Contributors
* @Emerix made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1021
* @KennerMiner made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1040
* @Warlander made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1111

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.6.8...v9.7.0

</details>


## v9.6 series

### [v9.6.8](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.8) — 2026-04-27

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.6.6) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1048
* Fix/restore openclaw by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1053
* chore: update Unity package to beta version 9.6.7-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1054
* Pr 1051 unity65 compat by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1058
* Fix Unity 6.5 GetInstanceID compile breaks by @tomicz in https://github.com/CoplayDev/unity-mcp/pull/1051
* chore: update Unity package to beta version 9.6.7-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1059
* docs(camera): clarify screenshot camera param behavior and UI capture limitation by @sssooonnnggg in https://github.com/CoplayDev/unity-mcp/pull/1060
* chore: update Unity package to beta version 9.6.7-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1061
* fix(build): guard BuildTarget.VisionOS with UNITY_2023_2_OR_NEWER by @sMartz1 in https://github.com/CoplayDev/unity-mcp/pull/1063
* chore: update Unity package to beta version 9.6.7-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1066
* Remove AssetOrigin from GeminiCliConfigurator meta by @EllieTellie in https://github.com/CoplayDev/unity-mcp/pull/1074
* chore: update Unity package to beta version 9.6.7-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1078
* Fix create_script validator false-positive inside method bodies by @justinpbarnett in https://github.com/CoplayDev/unity-mcp/pull/1076
* chore: update Unity package to beta version 9.6.7-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1079
* calling TestRunStatus.MarkFinished() to trigger callbacks by @miketalley in https://github.com/CoplayDev/unity-mcp/pull/1067
* chore: update Unity package to beta version 9.6.7-beta.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1080
* fix: save prefab stage edits using the correct prefab-stage workflow by @jacklaplante in https://github.com/CoplayDev/unity-mcp/pull/1056
* chore: update Unity package to beta version 9.6.7-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1081
* Fix MCP main window reopening logic and remove profiler warning by @chenkunqing in https://github.com/CoplayDev/unity-mcp/pull/1077
* chore: update Unity package to beta version 9.6.7-beta.10 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1082
* Improve python bridge log destination by @gibertoni in https://github.com/CoplayDev/unity-mcp/pull/1072
* chore: update Unity package to beta version 9.6.7-beta.11 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1083
* Patch-Fix-04-26 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1084
* chore: update Unity package to beta version 9.6.7-beta.12 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1085
* chore: bump version to 9.6.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1086

## New Contributors
* @tomicz made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1051
* @sssooonnnggg made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1060
* @sMartz1 made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1063
* @EllieTellie made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1074
* @miketalley made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1067
* @jacklaplante made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1056
* @chenkunqing made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1077
* @gibertoni made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1072

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.6.6...v9.6.8

</details>

### [v9.6.6](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.6) — 2026-04-07

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.6.5) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1028
* Patch by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1033
* chore: update Unity package to beta version 9.6.6-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1034
* fix: Unity 2021.3 compat — compile errors, Mono crash, 19 test failures by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/1036
* chore: update Unity package to beta version 9.6.6-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1038
* Fix create_script validator false-positive on constructor invocations by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/1045
* chore: update Unity package to beta version 9.6.6-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1046
* chore: bump version to 9.6.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1047

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.6.5...v9.6.6

</details>

### [v9.6.5](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.5) — 2026-04-03

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.6.4) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1009
* Bug-fix by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1011
* chore: update Unity package to beta version 9.6.5-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1012
* Prefab stages integration by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1013
* chore: update Unity package to beta version 9.6.5-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1015
* feat(manage_gameobject): add is_static parameter to modify action by @Sibirius in https://github.com/CoplayDev/unity-mcp/pull/1005
* chore: update Unity package to beta version 9.6.5-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1016
* feat: add execute_code tool for running arbitrary C# in Unity Editor by @zaferdace in https://github.com/CoplayDev/unity-mcp/pull/1001
* chore: update Unity package to beta version 9.6.5-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1017
* Fix on #837 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1025
* chore: bump version to 9.6.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1027

## New Contributors
* @Sibirius made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/1005

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.6.4...v9.6.5

</details>

### [v9.6.4](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.4) — 2026-03-31

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.6.2) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/970
* perf: avoid blocking update checks in editor window by @jiajunfeng in https://github.com/CoplayDev/unity-mcp/pull/954
* chore: update Unity package to beta version 9.6.3-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/971
* feat: add open_prefab_stage action to manage_editor by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/968
* chore: update Unity package to beta version 9.6.3-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/972
* fix: remove nullable annotation causing CS8632 warning by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/974
* chore: update Unity package to beta version 9.6.3-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/975
* test: add unit tests for manage_packages tool and CLI by @galofilip in https://github.com/CoplayDev/unity-mcp/pull/973
* chore: update Unity package to beta version 9.6.3-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/977
* feat: add set_import_settings action to manage_texture tool by @zaferdace in https://github.com/CoplayDev/unity-mcp/pull/982
* chore: update Unity package to beta version 9.6.3-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/986
* Manage_physics by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/983
* chore: update Unity package to beta version 9.6.3-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/989
* Pr 980 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/993
* chore: update Unity package to beta version 9.6.3-beta.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/994
* When there are multiple Editor folders, fix the issue by retrieving t… by @EternalSunLhx in https://github.com/CoplayDev/unity-mcp/pull/995
* chore: update Unity package to beta version 9.6.3-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/996
* feat(manage_editor): add save_prefab_stage action by @zaferdace in https://github.com/CoplayDev/unity-mcp/pull/990
* chore: update Unity package to beta version 9.6.3-beta.10 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/997
* feat: add manage_profiler tool with session control, counters, memory snapshots, and Frame Debugger by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/1000
* chore: update Unity package to beta version 9.6.3-beta.11 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1006
* chore: bump version to 9.6.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/1008

## New Contributors
* @galofilip made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/973
* @zaferdace made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/982
* @EternalSunLhx made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/995

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.6.2...v9.6.4

</details>

### [v9.6.2](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.2) — 2026-03-23

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.6.0) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/944
* Manage build by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/957
* chore: update Unity package to beta version 9.6.1-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/959
* Bug-fix by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/960
* chore: update Unity package to beta version 9.6.1-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/961
* Quality of Life update by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/964
* chore: update Unity package to beta version 9.6.1-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/967
* chore: bump version to 9.6.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/969

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.6.0...v9.6.2

</details>

### [v9.6.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.0) — 2026-03-16

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.5.3) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/905
* [bug-fix]Duplicate signature fix by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/906
* chore: update Unity package to beta version 9.5.4-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/907
* Plugin eviction, CLI camera/graphics, minor fixes by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/911
* chore: update Unity package to beta version 9.5.4-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/912
* bug-fix on Manage Script name-path conflict by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/913
* chore: update Unity package to beta version 9.5.4-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/914
* feat(mcpforunity): support atlas sprite resolution by guid+spriteName/fileID by @jiajunfeng in https://github.com/CoplayDev/unity-mcp/pull/873
* chore: update Unity package to beta version 9.5.4-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/915
* Update MCPForUnityEditorWindow.cs by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/917
* chore: update Unity package to beta version 9.5.4-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/918
* Bug fix and log feature update by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/919
* chore: update Unity package to beta version 9.5.4-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/920
* Fix on config and log by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/921
* chore: update Unity package to beta version 9.5.4-beta.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/922
* auto-start server option by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/923
* chore: update Unity package to beta version 9.5.4-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/924
* UIFix by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/925
* chore: update Unity package to beta version 9.5.4-beta.10 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/926
* UI and urp material assignmetn fix by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/929
* chore: update Unity package to beta version 9.5.4-beta.11 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/930
* Bug-Fix and Doc-Update by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/934
* chore: update Unity package to beta version 9.5.4-beta.12 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/935
* Update TestRunnerService.cs by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/936
* chore: update Unity package to beta version 9.5.4-beta.13 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/937
* feat(camera): add scene view screenshot capture support by @jiajunfeng in https://github.com/CoplayDev/unity-mcp/pull/927
* chore: update Unity package to beta version 9.5.4-beta.14 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/938
* Feature: API verification via doc/asset by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/939
* chore: update Unity package to beta version 9.5.4-beta.15 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/942
* chore: bump version to 9.6.0 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/943

## New Contributors
* @jiajunfeng made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/873

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.5.3...v9.6.0

</details>


## v9.5 series

### [v9.5.3](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.5.3) — 2026-03-09

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.5.2) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/887
* Skill update by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/888
* Update QwenCodeConfigurator.cs.meta by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/893
* chore: update Unity package to beta version 9.5.3-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/894
* [Feature] Manage_graphics by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/890
* chore: update Unity package to beta version 9.5.3-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/895
* Feature/manage packages by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/897
* chore: update Unity package to beta version 9.5.3-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/900
* Add OpenClaw client configurator and transport-aware setup by @lokyeye in https://github.com/CoplayDev/unity-mcp/pull/896
* Revert "Add OpenClaw client configurator and transport-aware setup" by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/901
* PR 896 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/902
* chore: update Unity package to beta version 9.5.3-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/903
* chore: bump version to 9.5.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/904

## New Contributors
* @lokyeye made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/896

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.5.2...v9.5.3

</details>

### [v9.5.2](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.5.2) — 2026-03-07

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.5.1) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/881
* [Feature] manage_camera with cinemachine support by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/882
* chore: update Unity package to beta version 9.5.2-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/883
* update for resources-bug-fix by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/884
* chore: update Unity package to beta version 9.5.2-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/885
* chore: bump version to 9.5.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/886

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.5.1...v9.5.2

</details>

### [v9.5.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.5.1) — 2026-03-07

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.5.0) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/879
* chore: bump version to 9.5.1 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/880

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.5.0...v9.5.1

</details>


## v9.4 series

### [v9.4.8](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.4.8) — 2026-03-06

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.4.7) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/809
* Update on skill/UI/camera by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/818
* chore: update Unity package to beta version 9.4.8-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/819
* fix: add pyenv shims to PATH on macOS by @whatevertogo in https://github.com/CoplayDev/unity-mcp/pull/813
* chore: update Unity package to beta version 9.4.8-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/823
* Add Gemini CLI client configurator by @sergeiwallace in https://github.com/CoplayDev/unity-mcp/pull/825
* chore: update Unity package to beta version 9.4.8-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/831
* [Feature] New UI system by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/829
* chore: update Unity package to beta version 9.4.8-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/832
* Fix constructor name for GeminiCliConfigurator by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/833
* chore: update Unity package to beta version 9.4.8-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/834
* feat: Add Qwen Code client configurator by @WeLizard in https://github.com/CoplayDev/unity-mcp/pull/835
* chore: update Unity package to beta version 9.4.8-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/836
* Feature/multi-view screenshot by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/840
* chore: update Unity package to beta version 9.4.8-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/842
* Feature/roslyn installer (set to 4.2.0) by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/841
* chore: update Unity package to beta version 9.4.8-beta.10 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/843
* chore: update Unity package to beta version 9.4.8-beta.11 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/848
* feat(editor): add GitHub-based skill sync window with safe incremental mirroring by @BaronCyrus in https://github.com/CoplayDev/unity-mcp/pull/845
* chore: update Unity package to beta version 9.4.8-beta.12 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/849
* docs: fix incorrect tab reference for Claude Desktop setup by @Lulubellelll in https://github.com/CoplayDev/unity-mcp/pull/852
* chore: update Unity package to beta version 9.4.8-beta.13 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/855
* Fix issue853 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/856
* chore: update Unity package to beta version 9.4.8-beta.14 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/857
* [Feature] Meta tool: toggle tool context in realtime by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/854
* chore: update Unity package to beta version 9.4.8-beta.15 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/858
* Fix 0303 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/859
* chore: update Unity package to beta version 9.4.8-beta.17 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/860
* Readme-update by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/868
* chore: update Unity package to beta version 9.4.8-beta.19 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/869
* Pro builder by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/870
* chore: update Unity package to beta version 9.4.8-beta.20 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/871
* chore: bump version to 9.4.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/875

## New Contributors
* @sergeiwallace made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/825
* @WeLizard made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/835
* @BaronCyrus made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/845
* @Lulubellelll made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/852

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.4.7...v9.4.8

</details>

### [v9.4.7](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.4.7) — 2026-02-21

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.4.6) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/755
* Fix UnityEvent wiring via manage_components set_property by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/757
* chore: update Unity package to beta version 9.4.7-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/758
* Sync claude-nl-suite.yml from proven main run by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/759
* Fix read_console crash on Unity 6.5 and batch-mode test stability (#761) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/762
* chore: update Unity package to beta version 9.4.7-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/764
* Fix misleading 'Property not found' error and add SerializedProperty fallback (#765) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/766
* chore: update Unity package to beta version 9.4.7-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/767
* Add --offline to uvx launches for faster startup (#760) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/768
* chore: update Unity package to beta version 9.4.7-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/769
* Remove unimplemented since_timestamp from read_console by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/770
* chore: update Unity package to beta version 9.4.7-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/771
* Add per-call unity_instance routing via tool arguments by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/772
* chore: update Unity package to beta version 9.4.7-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/774
* Fix focus nudge launching Electron instead of restoring VS Code on macOS by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/783
* chore: update Unity package to beta version 9.4.7-beta.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/784
* Fix stdio bridge stalls when Unity is backgrounded during domain reload by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/787
* chore: update Unity package to beta version 9.4.7-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/788
* Fix editor state always reporting stale when Unity is backgrounded by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/789
* chore: update Unity package to beta version 9.4.7-beta.10 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/791
* Fix script edit retry duplication during reload by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/792
* chore: update Unity package to beta version 9.4.7-beta.11 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/794
* Fix script mutation tools retrying and failing after domain reload by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/796
* chore: update Unity package to beta version 9.4.7-beta.12 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/797
* HTTP transport: respect retry_on_reload and add stale connection detection by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/798
* chore: update Unity package to beta version 9.4.7-beta.13 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/799
* Log evicted WebSocket close errors instead of silently swallowing by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/800
* chore: update Unity package to beta version 9.4.7-beta.14 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/801
* feat: add component_properties to manage_prefabs modify_contents (#793) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/802
* chore: update Unity package to beta version 9.4.7-beta.15 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/803
* fix: post-merge fixes for #802 (tests, StdioBridge flakiness, focus nudge) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/804
* chore: update Unity package to beta version 9.4.7-beta.16 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/805
* fix: guard against null progress in nudge check by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/806
* chore: update Unity package to beta version 9.4.7-beta.17 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/807
* chore: bump version to 9.4.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/808

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.4.6...v9.4.7

</details>

### [v9.4.6](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.4.6) — 2026-02-15

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.4.4) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/721
* [skill update] Update for UI instructions in the skills by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/726
* Bug fix and batch customization by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/727
* chore: update Unity package to beta version 9.4.5-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/728
* Fix execute_custom_tool bypassed when unity_instance is specified by @whatevertogo in https://github.com/CoplayDev/unity-mcp/pull/724
* chore: update Unity package to beta version 9.4.5-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/729
* [feature] Animation and AnimController by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/696
* chore: update Unity package to beta version 9.4.5-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/730
* fix: harden localhost resolution and reload transport resilience on Windows by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/688
* chore: update Unity package to beta version 9.4.5-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/732
* fix: improve tool descriptions with explicit cross-references (rebased #694) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/733
* chore: update Unity package to beta version 9.4.5-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/734
* fix: preserve tool toggle state and filter tools in client listings (rebased #723) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/735
* Fix: #709 - Preserve tool enabled/disabled state and filter tools in client listings by @whatevertogo in https://github.com/CoplayDev/unity-mcp/pull/723
* chore: update Unity package to beta version 9.4.5-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/736
* fix: accept JSON strings for list parameters in manage_gameobject and manage_texture by @whatevertogo in https://github.com/CoplayDev/unity-mcp/pull/731
* chore: update Unity package to beta version 9.4.5-beta.8 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/737
* fix(test): align clip name with asset filename in ClipGetInfo test by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/738
* Add Cline configurator and auto-select server channel by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/739
* chore: update Unity package to beta version 9.4.5-beta.9 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/740
* fix: enforce tool toggle checks in batch_execute by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/741
* chore: update Unity package to beta version 9.4.5-beta.10 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/742
* fix: handle bare Assets/ path in manage_shader by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/743
* chore: update Unity package to beta version 9.4.5-beta.11 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/744
* fix: catch ArgumentException from Path.IsPathRooted in IsLocalServerPath by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/746
* chore: update Unity package to beta version 9.4.5-beta.12 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/747
* Fix ManageScript delimiter checking for C# string variants by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/745
* chore: update Unity package to beta version 9.4.5-beta.13 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/748
* Harden HTTP defaults and improve stop-server fallback by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/751
* chore: update Unity package to beta version 9.4.5-beta.14 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/752
* chore: bump version to 9.4.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/754

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.4.4...v9.4.6

</details>

### [v9.4.4](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.4.4) — 2026-02-11

<details>
<summary>Show release notes</summary>

## What's Changed
* fix: Claude Code registration, thread-safety, and auto-detect beta server (#664) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/667
* chore: trigger beta workflow by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/668
* chore: update Unity package to beta version 9.4.0-beta.1 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/669
* fix: Beta mode status validation and EditorPrefs Manager improvements by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/671
* Unity-MCP skills by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/659
* fix: beta workflow no longer auto-bumps minor version by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/673
* chore: update Unity package to beta version 9.4.0-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/674
* chore: sync main (v9.3.2) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/677
* chore: sync main (v9.4.0) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/680
* fix: prevent main branch deletion in sync_beta step by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/681
* fix: speed up Claude Code config check by reading JSON directly by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/682
* chore: update Unity package to beta version 9.4.1-beta.1 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/683
* fix(run_tests): support snake_case params and handle double-serialized arrays by @bruno1308 in https://github.com/CoplayDev/unity-mcp/pull/690
* fix: remove vestigial find/component params from manage_gameobject by @HivemindMinion in https://github.com/CoplayDev/unity-mcp/pull/693
* chore: update Unity package to beta version 9.4.1-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/701
* Auto-merge version bump PRs in beta release workflow by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/703
* chore: update Unity package to beta version 9.4.1-beta.3 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/702
* Merge directly because it's failing w/ the auto flag and no status checks by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/705
* chore: update Unity package to beta version 9.4.1-beta.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/704
* chore: update Unity package to beta version 9.4.1-beta.5 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/706
* [fixes] Fixes and Skill upload by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/707
* chore: update Unity package to beta version 9.4.1-beta.6 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/708
* fix: make release sync_beta deterministic and bump beta post-release by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/713
* chore: update Unity package to beta version 9.4.1-beta.7 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/714
* test: fix PackageUpdateServiceTests override signature by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/715
* chore: sync main (v9.4.2) into beta + set 9.4.3-beta.1 by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/717
* chore: update Unity package to beta version 9.4.3-beta.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/718
* fix(ci): backport deterministic sync_beta logic to main release workflow by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/719
* chore: bump version to 9.4.4 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/720

## New Contributors
* @bruno1308 made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/690
* @HivemindMinion made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/693

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.4.2...v9.4.4

</details>

### [v9.4.2](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.4.2) — 2026-02-10

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: bump version to 9.4.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/716

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.4.1...v9.4.2

</details>

### [v9.4.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.4.1) — 2026-02-10

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: bump version to 9.4.1 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/710

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.4.0...v9.4.1

</details>

### [v9.4.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.4.0) — 2026-02-03

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: bump version to 9.4.0 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/679

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.3.2...v9.4.0

</details>


## v9.3 series

### [v9.3.2](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.3.2) — 2026-02-03

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: bump version to 9.3.2 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/676

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.3.1...v9.3.2

</details>

### [v9.3.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.3.1) — 2026-01-31

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: sync main (v9.2.0) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/618
* feat:  improve editor window UI + add transport mismatch warning by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/613
* fix: improve manage_scene screenshot capture by @toxifly in https://github.com/CoplayDev/unity-mcp/pull/600
* [FEATURE] Procedural Texture2D/Sprite Generation by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/621
* fix: resolve Unknown pseudo class last-child USS warnings by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/624
* Token Optimization for VFX by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/626
* feat: Prefab Feature Updates by @whatevertogo in https://github.com/CoplayDev/unity-mcp/pull/611
* fix: prefab stage dirty flag, root rename, test fix, and prefab resources by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/627
* Add missing Unity .meta file for PrefabUtilityHelper by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/630
* Upgrade Microsoft.CodeAnalysis and SQLitePCLRaw versions by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/632
* Upgrade Microsoft.CodeAnalysis to v5.0 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/633
* feat: replace prefab stage actions with headless modify_contents by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/635
* Update for CLI by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/636
* Fix:  Fix vector and color parameter validation to accept JSON string inputs by @whatevertogo in https://github.com/CoplayDev/unity-mcp/pull/625
* Update the manual commands by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/638
* feat: add beta release workflow for TestPyPI by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/628
* feat: Add beta server mode with PyPI pre-release support by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/640
* Large Cleanup and Refactor + Many new Tests added by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/642
* Fix/uvx logassert by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/645
* Add create_child parameter to manage_prefabs modify_contents by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/646
* fix: Windows Claude Desktop stdio + Codex beta/timeout config by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/650
* Fix test failures by invalidating EditorConfigurationCache by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/651
* Fix CodexConfigHelperTests for new prerelease argument order by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/652
* Add GitHub Copilot CLI support by @GeekTrainer in https://github.com/CoplayDev/unity-mcp/pull/641
* Remote server auth by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/644
* Display resources by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/658
* Fix WebSocket connection reliability and domain reload recovery by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/656
* Asset store updates by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/660
* Update warning message for Camera Capture by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/661
* chore: sync main (v9.3.0) into beta by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/663

## New Contributors
* @toxifly made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/600
* @GeekTrainer made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/641

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.3.0...v9.3.1

</details>

### [v9.3.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.3.0) — 2026-01-31

<details>
<summary>Show release notes</summary>

## What's Changed
* chore: bump version to 9.3.0 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/662

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.2.0...v9.3.0

</details>


## v9.2 series

### [v9.2.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.2.0) — 2026-01-23

<details>
<summary>Show release notes</summary>

## What's Changed
* Marcus/update readme on release by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/607
* feat: Add OpenCode (opencode.ai) client configurator by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/608
* feat: Add MCPB bundle for Claude Desktop installation by @triepod-ai in https://github.com/CoplayDev/unity-mcp/pull/580
* Update CI flow so that we bump from beta to main, and sync back by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/614
* Update main for new workflow by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/615
* chore: bump version to 9.2.0 by @github-actions[bot] in https://github.com/CoplayDev/unity-mcp/pull/616

## New Contributors
* @github-actions[bot] made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/616

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.1.0...v9.2.0

</details>


## v9.1 series

### [v9.1.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.1.0) — 2026-01-22

<details>
<summary>Show release notes</summary>

## What's Changed
* fix: Add Prefab Stage support for GameObject lookup by @cyanxwh in https://github.com/CoplayDev/unity-mcp/pull/573
* fix: search inactive objects when setActive=true in modify by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/581
* fix: Filter EditorApplication.isCompiling false positives in Play mode by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/582
* docs: Streamline README for faster onboarding by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/583
* fix: Add special handling for UIDocument serialization to prevent infinite loops by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/586
* fix: comprehensive performance optimizations, claude code config, and stability improvements (issue #577) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/595
* Project scoped tools by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/596
* Update README with citation by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/591
* Use localhost to ping server if server binds to 0.0.0.0 by @kripergvg in https://github.com/CoplayDev/unity-mcp/pull/542
* Fix connection field sizing and add URL error hints by @hvesuk in https://github.com/CoplayDev/unity-mcp/pull/587
* Minor fixes by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/602
* Docker mcp gateway by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/603
* fix: Rider config path and add MCP registry manifest by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/604
* Add CLI by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/606

## New Contributors
* @cyanxwh made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/573
* @kripergvg made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/542
* @hvesuk made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/587

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.8...v9.1.0

</details>


## v9.0 series

### [v9.0.8](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.8) — 2026-01-19

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix local HTTP server UI check by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/556
* Guard refresh wait nudge during compile by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/558
* fix: Prevent infinite compilation loop in Unity 6 when using wait_for_ready by @BlindsidedGames in https://github.com/CoplayDev/unity-mcp/pull/559
* Replace asmdef GUID references by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/564
* fix: parse and validate read_console types by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/565
* Workflow cleanup by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/568
* Fix ULF detection in Claude licensing by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/569
* fix: resolve UV path override not being detected in System Requirements .fix: The configuration automatically added by Claude Code is broken. #550 by @whatevertogo in https://github.com/CoplayDev/unity-mcp/pull/546

## New Contributors
* @BlindsidedGames made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/559
* @whatevertogo made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/546

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.7...v9.0.8

</details>

### [v9.0.7](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.7) — 2026-01-15

<details>
<summary>Show release notes</summary>

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.6...v9.0.7

</details>

### [v9.0.6](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.6) — 2026-01-14

<details>
<summary>Show release notes</summary>

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.5...v9.0.6

</details>

### [v9.0.5](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.5) — 2026-01-14

<details>
<summary>Show release notes</summary>

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.4...v9.0.5

</details>

### [v9.0.4](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.4) — 2026-01-14

<details>
<summary>Show release notes</summary>

## What's Changed
* CI Updates by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/536
* Update URL for v9 by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/537
* feat: add test coverage tracking with pytest-cov by @Nonanti in https://github.com/CoplayDev/unity-mcp/pull/512
* Refactor ClaudeCodeConfigurator to use JsonFileMcpConfigurator by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/545
* Clean up Unity and Python tests by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/548
* Fix manage_components set_property for object references by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/551
* Fix PlayMode tests stalling when unfocused (python refresh utility), improve domain reload recovery and refresh tool by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/554

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.3...v9.0.4

</details>

### [v9.0.3](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.3) — 2026-01-08

<details>
<summary>Show release notes</summary>

## What's Changed
* fix: replace Editor-only McpLog with Debug.LogWarning in Runtime asse… by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/535

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.2...v9.0.3

</details>

### [v9.0.2](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.2) — 2026-01-08

<details>
<summary>Show release notes</summary>

## What's Changed
* Fixes Windows installation failures caused by long path issues when cloning the full repository via git URL (MAX_PATH 260 char limit exceeded by files in TestProjects/). by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/534

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.1...v9.0.2

</details>

### [v9.0.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.1) — 2026-01-08

<details>
<summary>Show release notes</summary>

## What's Changed
* feat(batch_execute): improve error handling with success detection an… by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/531

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v9.0.0...v9.0.1

</details>

### [v9.0.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.0.0) — 2026-01-08

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix read_console default to include 'log' messages by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/515
* fix: Multi-session UI improvements and HTTP instance recognition by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/517
* feat: Add tool annotations for improved LLM tool understanding by @triepod-ai in https://github.com/CoplayDev/unity-mcp/pull/480
* 🎮 GameObject Toolset Redesign and Streamlining by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/518
* 🔧 Clean up & Consolidate Shared Services Across MCP Tools by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/519
* Asset store helper script + updated README by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/521
* [FEATURE]: Manage VFX function by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/520
* Harden `manage_scriptable_object` Tool by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/522
* v9 pre-release pruning by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/528
* Fix issue #525: Save dirty scenes for all test modes by @sjennings in https://github.com/CoplayDev/unity-mcp/pull/527
* feat: Mark setup as completed when user clicks Done button by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/529
* Fix HTTP/Stdio Transport UX and Test Bug by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/530

## New Contributors
* @triepod-ai made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/480
* @sjennings made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/527

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.7.1...v9.0.0

</details>


## v8.7 series

### [v8.7.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.7.1) — 2026-01-05

<details>
<summary>Show release notes</summary>

## What's Changed
* Add Cherry Studio MCP client support by @Nonanti in https://github.com/CoplayDev/unity-mcp/pull/505
* Codex/implement bounded retry policy for unity by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/510
* Codex/optimize and paginate read console tool by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/511

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.7.0...v8.7.1

</details>

### [v8.7.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.7.0) — 2026-01-03

<details>
<summary>Show release notes</summary>

## What's Changed
* Async Test Infrastructure & Editor Readiness Status + new refresh_unity tool by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/507

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.6.0...v8.7.0

</details>


## v8.6 series

### [v8.6.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.6.0) — 2026-01-02

<details>
<summary>Show release notes</summary>

## What's Changed
* Simplify default HTTP by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/495
* HTTP setup overhaul: transport selection (HTTP local/remote vs stdio), safer lifecycle, cleaner UI, better Claude Code integration by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/499
* Feature/run tests summary clean by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/501
* ManageGameObject/Material improvements + auto-select sole Unity instance by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/502

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.5.0...v8.6.0

</details>


## v8.5 series

### [v8.5.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.5.0) — 2025-12-29

<details>
<summary>Show release notes</summary>

## What's Changed
* Add EditorPrefs management window for MCP configuration debugging by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/491
* Edit editor configs by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/493
* Optimise so startup is fast again by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/494

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.4.0...v8.5.0

</details>


## v8.4 series

### [v8.4.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.4.0) — 2025-12-29

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix on Issue #465 by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/477
* Test/478 matrix4x4 serialization crash by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/481
* Fix/ci cleanup by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/484
* Fix test teardown to avoid dropping MCP bridge by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/487
* feature/Add new manage_scriptable_object tool by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/489
* Payload-safe paging for hierarchy/components + safer asset search + docs by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/490

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.3.0...v8.4.0

</details>


## v8.3 series

### [v8.3.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.3.0) — 2025-12-20

<details>
<summary>Show release notes</summary>

## What's Changed
* Minor Fix on Advanced Setting UI by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/459
* feat: Add Intelij Rider for Autoconfig support by @DeTandtThibaut in https://github.com/CoplayDev/unity-mcp/pull/448
* Add debug logging for legacy configuration migration details by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/463
* Feat: Add test filtering options to run_tests tool by @voonfoo in https://github.com/CoplayDev/unity-mcp/pull/462
* feat: add Kilo Code configurator for AutoConfig support by @Nonanti in https://github.com/CoplayDev/unity-mcp/pull/438
* Publish to pypi by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/466
* Publish to Docker Hub by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/468
* Bump dep versions by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/469
* Improve legacy configuration migration error handling and cleanup by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/470
* Add .meta files by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/472
* Remove distribution settings scriptable object by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/473

## New Contributors
* @DeTandtThibaut made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/448
* @voonfoo made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/462
* @Nonanti made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/438

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.2.3...v8.3.0

</details>


## v8.2 series

### [v8.2.3](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.2.3) — 2025-12-11

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix/script path assets prefix and ctx warn bug by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/453

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.2.2...v8.2.3

</details>

### [v8.2.2](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.2.2) — 2025-12-10

<details>
<summary>Show release notes</summary>

## What's Changed
* [FEATURE] Camera Capture by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/449
* [FEATURE] Deployment of local source code to Unity by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/450
* 修复ArrayPool可能会产生报错的问题 by @xinyeu in https://github.com/CoplayDev/unity-mcp/pull/451
* Unity MCP CI Test Improvements by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/452

## New Contributors
* @xinyeu made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/451

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.2.1...v8.2.2

</details>

### [v8.2.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.2.1) — 2025-12-08

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix/websocket queue to main thread by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/443

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.2.0...v8.2.1

</details>

### [v8.2.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.2.0) — 2025-12-08

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix: Add middleware support for resource context injection(#431) by @MyNameisPI in https://github.com/CoplayDev/unity-mcp/pull/432
* [FEATURE] Batch Commands by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/418
* [FEATURE] Custom Tool Fix and Add inspection window for all the tools by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/414
* feat: Add `manage_material` tool for dedicated material manipulation by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/440

## New Contributors
* @MyNameisPI made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/432

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.1.6...v8.2.0

</details>


## v8.1 series

### [v8.1.6](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.1.6) — 2025-12-04

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix: Rename UnityMCP to unityMCP in README by @sjjeong in https://github.com/CoplayDev/unity-mcp/pull/424
* [FEATURE] Update GameObject for two new features by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/427
* Support GitHub Copilot in VSCode Insiders + robustness improvements and bug fixes by @Jordonh18 in https://github.com/CoplayDev/unity-mcp/pull/425
* Fix: Python Detection, Port Conflicts, and Script Creation Reliability by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/428

## New Contributors
* @sjjeong made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/424

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.1.5...v8.1.6

</details>

### [v8.1.5](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.1.5) — 2025-12-04

<details>
<summary>Show release notes</summary>

## What's Changed
* fix: Changed flag management to EditorPrefs by @Hashibutogarasu in https://github.com/CoplayDev/unity-mcp/pull/408
* Fix duplicate connection verification logs: add debounce and state-ch… by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/413
* Claude Skill Example Upload by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/380
* [FIX] Temp Update on Material Assignment by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/420
* Fix: HTTP/Stdio transport routing and middleware session persistence by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/422

## New Contributors
* @Hashibutogarasu made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/408

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.1.4...v8.1.5

</details>

### [v8.1.4](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.1.4) — 2025-12-02

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix Claude Windows config and CLI status refresh by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/412

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.1.3...v8.1.4

</details>

### [v8.1.3](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.1.3) — 2025-12-01

<details>
<summary>Show release notes</summary>

## What's Changed
* fix: restrict fastmcp version to avoid potential KeyError by @Jordonh18 in https://github.com/CoplayDev/unity-mcp/pull/411

## New Contributors
* @Jordonh18 made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/411

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.1.2...v8.1.3

</details>

### [v8.1.2](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.1.2) — 2025-11-29

<details>
<summary>Show release notes</summary>

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.1.1...v8.1.2

</details>

### [v8.1.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.1.1) — 2025-11-29

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix manage prefabs by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/405
* Fix CLI entry point path in pyproject.toml by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/407

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.1.0...v8.1.1

</details>

### [v8.1.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.1.0) — 2025-11-28

<details>
<summary>Show release notes</summary>

## What's Changed
* Simplify MCP client configs by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/401
* Fix stdio reloads by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/402
* Add CodeBuddy CLI configurator by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/403
* Add distribution settings for Asset Store vs git defaults by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/404

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.0.1...v8.1.0

</details>


## v8.0 series

### [v8.0.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.0.1) — 2025-11-26

<details>
<summary>Show release notes</summary>

## What's Changed
* Enable the `rmcp_client` feature so it works with Codex CLI by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/395
* Harden PlayMode test runs by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/396

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v8.0.0...v8.0.1

</details>

### [v8.0.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v8.0.0) — 2025-11-25

<details>
<summary>Show release notes</summary>

## What's Changed
* [CUSTOM TOOLS] Roslyn Runtime Compilation Feature by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/371
* HTTP Server, uvx, C# only custom tools by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/375

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v7.0.0...v8.0.0

</details>


## v7.0 series

### [v7.0.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v7.0.0) — 2025-11-05

<details>
<summary>Show release notes</summary>

## What's Changed
* fix: JSON material property handling + tests (manage_asset) #90 by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/349
* tests(editmode): pre-create texture asset in texture assignment test … by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/352
* Copy the MCP server to the top level by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/354
* Update .Bat file and Bug fix on ManageScript by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/355
* feat: lower minimum Python requirement to 3.10+ by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/362
* Server: Robust shutdown on stdio detach (signals, stdin/parent monitor, forced exit) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/363
* Revert "Server: Robust shutdown on stdio detach (signals, stdin/paren… by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/364
* It's time to let go, all dev for the plugin happens in MCPForUnity by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/365
* Feature/session based instance routing by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/369
* Move Get commands to editor resources + Run Python tests every update by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/368

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v6.3.0...v7.0.0

</details>


## v6.3 series

### [v6.3.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v6.3.0) — 2025-10-24

<details>
<summary>Show release notes</summary>

## What's Changed
* Remove old UI and do lots of cleanup by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/340
* server instruction cleanup by @JosvanderWesthuizen in https://github.com/CoplayDev/unity-mcp/pull/345

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v6.2.5...v6.3.0

</details>


## v6.2 series

### [v6.2.5](https://github.com/CoplayDev/unity-mcp/releases/tag/v6.2.5) — 2025-10-24

<details>
<summary>Show release notes</summary>

## What's Changed
* fix: Port Discovery Protocol Mismatch by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/341
* Harden MCP tool parameter handling + add material workflow tests (TDD) by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/343
* Material tools: support direct shader property keys + add EditMode coverage by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/344

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v6.2.4...v6.2.5

</details>

### [v6.2.4](https://github.com/CoplayDev/unity-mcp/releases/tag/v6.2.4) — 2025-10-23

<details>
<summary>Show release notes</summary>

## What's Changed
* test: Consolidate pytest suite to MCPForUnity and improve test infrastructure by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/332
* Fix/replace pytest anyio with pytest asyncio by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/333
* Allow the MCP server to be run by `uvx` remotely by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/336
* Update to support Trae by @bilal-arikan in https://github.com/CoplayDev/unity-mcp/pull/337
* Update logo, use it locally by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/338
* Harden MCP tool parameter handling to eliminate “invalid param” errors by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/339

## New Contributors
* @bilal-arikan made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/337

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v6.2.1...v6.2.4

</details>

### [v6.2.1](https://github.com/CoplayDev/unity-mcp/releases/tag/v6.2.1) — 2025-10-21

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix material mesh instantiation warnings by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/331
* Update certain file GUIDs to prevent conflict by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/commit/15c35ae17459b1305666b4deefa5d720aa3e22c3

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v6.2.0...v6.2.1

</details>

### [v6.2.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v6.2.0) — 2025-10-19

<details>
<summary>Show release notes</summary>

## What's Changed
* Fix version label + use secure unity version by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/321
* Update to support Codex CLI by @Scriptwonder in https://github.com/CoplayDev/unity-mcp/pull/325
* Allow users to easily add tools in the Asset folder by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/324
* test: remove unused tests for stale file cleanup in Python tools syncing by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/327
* refactor: use Tommy TOML library directly for config file manipulation by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/328
* Notify users when there's a new version by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/329

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v6.1.0...v6.2.0

</details>


## v6.1 series

### [v6.1.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v6.1.0) — 2025-10-13

<details>
<summary>Show release notes</summary>

Release v6.1.0

## What's Changed
* docs: replace "Unity MCP" with "MCP for Unity" in all text strings by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/314
* Add testing and move menu items to resources by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/316

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v6.0.0...v6.1.0

</details>


## v6.0 series

### [v6.0.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v6.0.0) — 2025-10-11

<details>
<summary>Show release notes</summary>

Release v6.0.0

## What's Changed
* Update README.md by @JosvanderWesthuizen in https://github.com/CoplayDev/unity-mcp/pull/310
* New UI and work without MCP server embedded by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/313

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v5.0.0...v6.0.0

</details>


## v5.0 series

### [v5.0.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v5.0.0) — 2025-10-06

<details>
<summary>Show release notes</summary>

## What's Changed
* Autoformat code by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/297
* feat: Unity Asset Store compliance with post-installation dependency setup by @justinpbarnett in https://github.com/CoplayDev/unity-mcp/pull/281
* Make it easier to add tools by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/301
* Fix read_console includeStacktrace parameter behavior by @dsarno in https://github.com/CoplayDev/unity-mcp/pull/304
* Rename plugin folder to MCPForUnity by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/303

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v4.1.1...v5.0.0

</details>


## v4.0 series

### [v4.0.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v4.0.0) — 2025-09-26

<details>
<summary>Show release notes</summary>

## What's Changed
* Allow the LLMs to read menu items, not just execute them by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/263
* Specify version for Microsoft.CodeAnalysis.CSharp package in README by @alexbagnolini in https://github.com/CoplayDev/unity-mcp/pull/278
* Replace command dispatcher with CommandRegistry, allow to add custom command handlers. by @Seng-Jik in https://github.com/CoplayDev/unity-mcp/pull/261
* Add Codex to autoconfig options by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/288
* Open and close prefabs in the stage view + create them by @msanatan in https://github.com/CoplayDev/unity-mcp/pull/283

## New Contributors
* @alexbagnolini made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/278
* @Seng-Jik made their first contribution in https://github.com/CoplayDev/unity-mcp/pull/261

**Full Changelog**: https://github.com/CoplayDev/unity-mcp/compare/v3.4.0...v4.0.0

</details>


## Migration guides

Breaking changes from prior major versions live under [Migrations](/migrations/v5):

- [v5 — UnityMcpBridge → MCPForUnity](/migrations/v5)
- [v6 — New Editor Window (UI Toolkit + service architecture)](/migrations/v6)
- [v8 — HTTP and Stdio support](/migrations/v8)
- [v10 — Asset Generation and Docs Refresh](/migrations/v10)
