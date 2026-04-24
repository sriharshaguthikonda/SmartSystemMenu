# TODO — SmartSystemMenu

Index of GitHub Issues. Source of truth: [GitHub Issues](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues).

Regenerate:
```bash
GITHUB_TOKEN=$GH_CLASSIC gh issue list --repo sriharshaguthikonda/SmartSystemMenu \
  --state open --limit 100 --json number,title,labels \
  --jq '.[] | "- [#\(.number)](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues/\(.number)) \(.title) — `\([.labels[].name] | join(", "))`"'
```

## P0 — Critical

- [#1](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues/1) Settings file race: concurrent `Save()` corrupts XML — `type:bug`

## P1 — Important

- [#2](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues/2) WM_COPYDATA unmanaged allocations leaked in dimmer send path — `type:bug, type:perf`
- [#3](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues/3) Shared WM_COPYDATA pointer reused for two sends, compounds leak — `type:bug`
- [#4](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues/4) Hidden-window rule matches process name only → spoofable — `type:security`
- [#5](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues/5) Per-window rule matching does expensive `GetMainModuleFileName` on every shell event — `type:perf`
- [#6](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues/6) `_64BitProcess` handle leaks on early-exception path — `type:bug`

## P2 — Deferred (next audit run)

_Style nits, dead code, doc polish, dependency updates — next sweep._

---

_Generated 2026-04-24 by cross-repo audit. Branch: `codex/window-target-hide-picker`._
