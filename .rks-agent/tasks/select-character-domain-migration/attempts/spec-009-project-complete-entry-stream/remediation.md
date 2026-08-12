# Spec 009 complete-review remediation

## Scope

This report records the same-Spec remediation of `reviews/change-review.md`. It does
not change any completed task-unit state or prior review history.

## F-001 — production-complete static projection

- Added the narrow `ISkillPointDefinitionSource` boundary and a PVF implementation that
  projects the legacy `Etc/spTable.etc`, character free-grant and learned `.skl` cost
  ingredients. Application owns the pure per-page SP/TP ledger and writes the resulting
  `CharacterSkillProtocolMirrorState` before publishing the selected character.
- Preserved collection-box maximum expiry in the G5 world answer. Application derives
  each protocol remaining-time/status pair once from that value and the single
  `GameWorldEntryState.PreparedAt` timestamp; Networking still fails on missing state.
- Behavioral evidence covers a learned/free-baseline ledger vector and a real Script.pvf
  probe (job 0, grow 1, level 86: total SP 11010, TP 37, sixteen free grants, skill 1
  purchase cost 20).

## F-002 — populated rows 38 and 42

- Row 38 now selects only the active character's owner relation. Application resolves its
  support character through existing character, character-entry and inventory domain
  services into an explicit neutral projection. Networking encodes the legacy mapped
  identity, 82-byte stat limb, equipment, clone title and active skill page; no relation
  retains the evidenced two-byte empty body, while an incomplete relation fails.
- Row 42 now encodes up to eight account lottery records and their normalized claimed
  indexes in the fixed 204-byte legacy layout. Loaded-empty remains the evidenced all-zero
  body. Coverage documentation no longer calls either row an observed constant.

## F-003 — callable typed supplemental delivery

- Public `ISelectCharacterProjectionComposer` accepts ordered typed supplemental values;
  its implementation and all frame construction remain in Networking.
- `SelectCharacterProjectionDelivery` and explicit targets carry typed projections, not
  `byte[]`. The runtime sender composes required-direct and best-effort target batches,
  preserving the previously accepted order and failure isolation.
- An Application-project contract test constructs and sends a typed delivery without a
  Networking reference or raw frame, while Networking behavior tests prove real composer
  order and runtime delivery order/isolation.

## Verification

- `dotnet build Server/86JP.Server.slnx --no-restore`: passed, 0 warnings / 0 errors.
- `dotnet test Server/86JP.Server.slnx --no-restore`: passed 415, skipped 2. The
  operator-supplied root `Script.pvf` was discovered and all real-PVF tests, including the
  new skill-point golden probe, executed rather than skipped.
- Focused Application character/projection tests: passed 16/16. Focused Networking
  SelectCharacter tests: passed 29/29, plus the Application-produced result-to-complete-
  stream regression passed 1/1. Focused real-PVF skill probe: passed 1/1.
- Dependency scans: Networking has no repository, SQLite, PVF or Infrastructure reference;
  Application has no concrete SQLite or PVF reference.
- Stale raw-delivery scan finds no `RequiredDirectFrames` or `OrderedFrames`; all live
  delivery constructors carry typed projections. `git diff --check` passed.

## R2 — retained row-38 wire/ownership remediation

The complete re-review retained F-002R because the first remediation reused the static
84/126-byte item-list layout and validated only one support identity limb. R2 replaces
that reuse with the exact legacy NOTI-2 equipped-entry shape:

- a one-byte slot, item/value/attribute/durability and four-byte clear-avatar-or-seal;
- the slot-0..10 avatar limb with its dynamic jewel-socket data and colors;
- the slot-24 creature marker;
- dynamic Chronicle, emblem and random-option sections followed by the NOTI-2 tail.

The encoder now rejects any populated relation unless the selected owner matches the
relation owner and `relation.SupportCharacterId`, `Support.Character.CharacterId`,
`Support.Entry.CharacterId` and `Support.Inventory.CharacterId` are identical. A typed
projection attached to a different owner also fails rather than becoming a false
loaded-empty case.

The populated row-38 vector now asserts the exact 320-byte body and decisive offsets for
an ordinary item (u8 slot and dynamic Chronicle/emblem/random sections), an avatar
(remaining time, clear-avatar ID, 30-byte socket and colors), and a slot-24 creature
marker. Separate vectors reject missing, entry-cross-owner, inventory-cross-owner and
relation-cross-owner projections; genuine relation absence still emits exactly two zero
bytes.

Fresh R2 verification:

- `dotnet test ...Dfo.Server.Networking.Tests.csproj --no-build --filter FullyQualifiedName~SelectCharacter`:
  passed 31/31.
- `dotnet test Server/86JP.Server.slnx --no-restore`: passed 415, skipped 2 (real PVF
  archive present; only archive-present negative checks skipped).
- Networking/Application concrete dependency scans and `git diff --check`: passed.

## R3 — retained row-38 dynamic emptiness and creature marker remediation

The final complete re-review retained F-002R2 for two legacy NOTI-2 details. R3 ports
both details without adding a database column or allowing Networking to discover content:

- Dynamic Chronicle membership is keyed strictly by `OptionId != 0`, and dynamic random-
  option membership strictly by `Type != 0`. Auxiliary bytes on a zero key no longer
  create an entry or shift the remaining body. The exact row-38 vector now includes a
  zero-ID Chronicle and a zero-Type random option with non-zero trailing bytes while
  retaining the exact 320-byte expected body. The sibling mailbox item-summary writer
  had the same random-option predicate and now has a clean-versus-stale-tail equality
  regression; that mailbox shape has no dynamic Chronicle count.
- Application resolves slot-24's final typed `Noti2CreatureMarker` through the narrow
  `ICreatureEquipmentLifetimeSource`. Its priority is the legacy static equipment
  `[expiration date]` remaining value, then the effective loaded CreatureDetail value
  derived from core `ExpireTime` or an absolute core marker, then a stored relative core
  marker. Networking only validates and writes that final typed marker. The existing
  creature table/schema remains unchanged because the legacy codec never hydrated an
  authoritative persisted CreatureDetail expiry.

Behavioral vectors cover static-PVF override, core expiry, absolute-marker and relative-
marker fallbacks. PVF parser vectors preserve the legacy rule that every positive numeric
value below `1_000_000_000` is a relative duration, while absolute Unix and server-local
date values are evaluated against the single selection `PreparedAt` time.

Fresh R3 verification:

- Focused lifetime/content, Application marker and Networking regressions: passed 4/4,
  4/4 and 1/1 respectively.
- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore`:
  passed 424, skipped exactly the two archive-present negative checks.
- `dotnet build Server/86JP.Server.slnx --no-restore`: passed with 0 warnings / 0 errors.
- Networking repository/PVF/SQLite scan, Application concrete PVF/SQLite scan, invented
  creature-expiry schema scan, and `git diff --check`: all passed with zero matches/errors.

## R4 — retained usable-period creature policy remediation

The fresh complete review retained F-002R3 because the R3 content source preserved only
the writer's direct `[expiration date]` probe and not the effective CreatureDetail policy
fallback. R4 extends the same narrow boundary to return two resolved values: direct
remaining seconds and detail-policy fallback remaining seconds. No PVF data crosses into
Application or Networking.

The Infrastructure source now reads both `[expiration date]` and `[usable period]` from
equipment definitions and ports `EquipmentExpirationPolicyResolver` semantics:

- the direct pet probe retains its legacy duration/absolute-date parsing;
- policy parsing rejects malformed or negative usable periods and malformed declared
  expiration values;
- a positive usable period produces a prepared-time-plus-days expiry (including the
  legacy `Int32.MaxValue` clamp), taking priority over a valid policy absolute expiry;
- absent/zero usable period retains the valid policy absolute expiry.

Application preserves the complete writer order: positive direct remaining time first;
then the effective CreatureDetail selected from core `ExpireTime`, absolute core marker,
or content policy; finally the stored core marker/expiry fallback. The final value remains
the only value exposed to Networking as `Noti2CreatureMarker`.

R4 behavior-first evidence:

- The first focused run failed to compile on the intentionally missing
  `ResolveMetadata` policy projection. Synthetic vectors now pass for usable-period-only,
  explicit zero expiration plus usable period, and simultaneous valid absolute expiration
  plus usable period while preserving both outputs.
- A real-archive vector finds an actual `Script.pvf` equipment definition with positive
  usable period and absent/zero direct expiration, then verifies direct `0` and detail
  fallback `days * 86400` through the production source.
- Application vectors prove direct override, core-expiry/absolute-marker precedence over
  policy, policy precedence over a relative stored marker, and the final stored fallback.

Fresh R4 verification:

- Focused real/synthetic Infrastructure lifetime tests: passed 8/8; focused Application
  marker tests: passed 5/5; focused Networking mailbox regression: passed 1/1.
- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore`:
  passed 429, skipped exactly the two archive-present negative checks.
- `dotnet build Server/86JP.Server.slnx --no-restore`: passed with 0 warnings / 0 errors.
- Networking repository/PVF/SQLite scan, Application concrete PVF/SQLite scan, invented
  expiry-schema and stale lifetime-API scans, and `git diff --check`: all passed.
