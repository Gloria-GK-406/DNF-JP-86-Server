# Table Ownership Ruling

Where every table and every column of the character database goes when the database is cut by
concept instead of by protocol segment, and what each ruling rests on.

The shape being ruled on is the one the database had when this was written — 53 tables and 420
columns, declared in `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` as of
commit `9c535a9`. That is a starting point, not a description of the database today: as each block
of work lands, the declaration moves toward the target shape given below, and the counts here stop
matching it. Read the counts as history and the target shape as the destination.
Counting a table means counting `CREATE TABLE IF NOT EXISTS` in `TableDeclarations`; counting a
column means splitting one declaration on its top-level commas and dropping the `PRIMARY KEY`,
`UNIQUE`, `FOREIGN KEY` and `CHECK` lines. (A plain `grep` of the file finds 54 — the extra one is
inside an XML comment.)

This is a ruling, not a plan. Only one ordering conclusion appears here, and only because two
blocks of work cannot both decide it; see [The gold limit](#the-gold-limit-and-the-ordering-it-settles).
Everything else says where a thing belongs and why, so that the work which follows implements a
settled decision instead of deciding as it goes.

## Why the boundaries are where they are today

The tables were not derived from the character. They were derived from the packet the server sends
when a character is selected: wherever that packet holds a repeatable segment, a table was created
for it. `CharacterEntryAssembly` alone reads 33 of them into one record, and the whole of selecting
a character reads all 53 — 51 unconditionally, plus `character_pvp_skills` and
`character_pvp_skill_state` when the channel's policy calls for them.

Two consequences show up as soon as the tables are read side by side. The same concept sits in
several tables — items are spread across twelve, and two generations of item storage exist at once.
And one entity is cut across tables by packet segment rather than by meaning: the character is
`characters` plus `character_subtype0_fields` plus `character_subtype1_fields`, so operational
state, derived numbers and identity are mixed together by which part of the packet they were read
out of.

## The three tests

Two tables may become one only when all three hold:

1. **Same key.** The same values locate one row in each.
2. **Same lifetime.** Rows appear together, disappear together, and are cleared together.
3. **Same writer.** One use case changes them. Put precisely: when a use case changes one column,
   the others in that row are context it cares about, not unrelated neighbours.

The three tests are necessary, not sufficient. They cannot say two tables *are* one concept; they
can only say they are not. A ruling still has to name the concept.

Note what the third test is about: **columns sharing a row**. Two tables with identical columns and
disjoint row sets do not put anything in front of a use case that it did not ask for, so the third
test has nothing to refuse there. Two tables that would put a wallet and a keyboard preference in
one row do.

### Observed and inferred

**14 of the 53 tables have a production write path.** The other 39 are read and never written.

| Table | Writes it has |
|---|---|
| `accounts` | register; set lucky star; set honour and capsule experience |
| `characters` | create; set level and experience; mark deleted; set position |
| `character_subtype0_fields` | insert-if-absent at creation |
| `character_subtype1_fields` | insert-if-absent at creation, carrying 21 of its 33 columns |
| `character_init_flags` | insert a row carrying `character_id` and `pc_room_state` |
| `character_skills` | insert, set level, remove |
| `character_pvp_skills` | insert |
| `character_pvp_skill_state` | insert a bare row |
| `character_active_quests` | insert, update trigger, delete |
| `character_creatures` | update one condition field |
| `character_daily_counters` | insert a claim |
| `character_new_items` | insert (three paths), update, delete, set stack |
| `character_avatar_detail` | upsert, delete-except |
| `dungeon_persistent_effect_outbox` | claim a lease, settle |

Evidence: every `INSERT INTO`, `UPDATE … SET` and `DELETE FROM` under `Server/src` naming a
declared table; eight files issue them.

So for most pairs the third test cannot be settled by observation: neither table has a writer, and
the question is what will write them once the feature that owns them exists. Where that is the
case this document marks the judgement **inferred**, and two rules apply in both directions:

- **The key test must be observed.** A merge may not rest on an inferred key.
- **An inferred test may neither carry nor refuse a merge on its own.** Where lifetime or writer can
  only be inferred, the ruling names the concept and says what would disprove it. A refusal must
  rest on an observed test, on the red line, or on a recorded want of evidence — never on a guess
  about a future writer alone.

### One-row-per-owner tables

Besides the character's and the account's own rows — and the two subtype tables, which
[M-6](#m-6-the-characters-own-three-tables-become-five-cut-by-concept) handles as part of the
character itself — eight tables can only ever hold one row per owner: six on the character side, and
`account_settings` and `account_cargo_state` on the account side. A table like that is a column
group wearing a table's clothes. Whether it should keep being a
table is decided by its columns, not its shape:

> If its columns belong to a concept that already has a home, they move there and the table stops
> existing. If they are a concept with no other home, the table stays — being narrow is not a reason
> to dissolve something.

The three tests still govern the move itself: they say whether those columns may join the home
proposed for them.

## Ruling summary

**53 tables become 43.** Fifteen stop existing, one becomes two, and six are new.

| Change | Count | Tables |
|---|---:|---|
| Retired outright | 2 | `character_items`, `account_cargo_items` |
| Merged away | 13 | `account_cargo_new_items`, `character_new_titlebook`, `character_avatar_detail`, `rental_items`, `character_subtype0_fields`, `character_subtype1_fields`, `character_init_flags`, `character_gold_limits`, `character_crystal_contract`, `character_dimension_flags`, `character_pvp_skill_state`, `character_pvp_skills`, `character_daily_challenge_claims` |
| Split in two | 1 | `character_item_values` → `character_item_cooldowns` + `character_item_effects` |
| Renamed | 4 | `character_new_items`→`items`, `character_rental_items`→`character_rentals`, `character_daily_challenge_groups`→`character_daily_challenges`, `character_invisible_falgs`→`character_invisible_flags` |
| Kept, contents changed | 3 | `characters`, `character_skills`, `character_expert_job` |
| New | 6 | `character_stats`, `character_condition`, `character_pvp`, `character_settings`, `character_item_cooldowns`, `character_item_effects` |
| Unchanged | 30 | — |

The rows account for the 53 exactly: 2 + 13 + 1 + 4 + 3 + 30. The 43 that remain are the 4 renamed,
3 kept, 6 new and 30 unchanged — the two halves of the split being two of the six new.

Three things this ruling does **not** do, against the sketch it started from:

- **Nothing folds into `accounts`, and the account side ends up with no change at all.** Its five
  tables all stay as they are; the two cargo tables that do change belong to the item work. What
  the root contract set up as a fourth block of work therefore has nothing in it — a fact the
  controller needs before it decomposes the implementation.
- **The item side collapses to six tables, not three.** Locks, the collect box, and the two halves
  that per-item values splits into each have a key of their own, so `item_states` as sketched cannot
  exist.
- **Mail attachments stay with mail.**

Five rulings contradict the task contract as literally written. Each is argued in place and all five
are collected in [Where this ruling differs from the task contract](#where-this-ruling-differs-from-the-task-contract).

## Per-table ruling

Cardinality is read off the accessor: a method returning one row means at most one row per owner.
"Writes" is production `INSERT`/`UPDATE`/`DELETE`, not tests.

| # | Table | Key | Card. | Writes | Ruling |
|---:|---|---|---|---|---|
| 1 | `accounts` | `account_id` | 1 | yes | stays |
| 2 | `account_settings` | `account_id` | 1 | no | stays — [S-1](#where-similar-tables-stay-apart) |
| 3 | `account_premiums` | `(account_id, premium_type)` | N | no | stays |
| 4 | `account_increase_chance_lottery_progress` | `(account_id, item_template_id, reward_index)` | N | no | stays |
| 5 | `account_cargo_state` | `account_id` | 1 | no | stays — [S-2](#where-similar-tables-stay-apart) |
| 6 | `account_cargo_new_items` | `item_uid`; `(account_id, slot_index)` | N | no | → `items` — [M-1](#m-1-items--character_new_items--account_cargo_new_items--character_new_titlebook) |
| 7 | `account_cargo_items` | `item_uid` | N | no | **retired** |
| 8 | `characters` | `character_id` | 1 | yes | → `characters`, `character_pvp` — [M-6](#m-6-the-characters-own-three-tables-become-five-cut-by-concept) |
| 9 | `character_subtype0_fields` | `character_id` | 1 | yes | → `character_condition`, `characters`, `character_pvp`, `character_expert_job`, `character_settings` — [M-6](#m-6-the-characters-own-three-tables-become-five-cut-by-concept) |
| 10 | `character_subtype1_fields` | `character_id` | 1 | yes | → `character_stats`, `characters` — [M-6](#m-6-the-characters-own-three-tables-become-five-cut-by-concept) |
| 11 | `character_init_flags` | `character_id` | 1 | yes | → `character_condition`, `character_settings` — [M-7](#m-7-the-five-one-row-per-character-tables-that-fold) |
| 12 | `character_gold_limits` | `character_id` | 1 | no | → `character_stats` — [M-7](#m-7-the-five-one-row-per-character-tables-that-fold) |
| 13 | `character_crystal_contract` | `character_id` | 1 | no | → `character_condition` — [M-7](#m-7-the-five-one-row-per-character-tables-that-fold) |
| 14 | `character_dimension_flags` | `character_id` | 1 | no | → `character_condition` — [M-7](#m-7-the-five-one-row-per-character-tables-that-fold) |
| 15 | `character_expert_job` | `character_id` | 1 | no | stays, absorbing two columns — [S-3](#where-similar-tables-stay-apart) |
| 16 | `character_pvp_skill_state` | `character_id` | 1 | yes | → `character_pvp` as one flag; all three columns retired — [M-7](#m-7-the-five-one-row-per-character-tables-that-fold) |
| 17 | `character_skills` | `(character_id, page_index, slot)` | N | yes | stays, absorbing `character_pvp_skills` — [M-4](#m-4-character_skills--character_pvp_skills) |
| 18 | `character_pvp_skills` | `(character_id, page_index, slot)` | N | yes | → `character_skills` — [M-4](#m-4-character_skills--character_pvp_skills) |
| 19 | `character_dark_knight_combo_skill_pages` | `(character_id, page_index)` | N | no | stays — [S-4](#where-similar-tables-stay-apart) |
| 20 | `character_knight_shield_deck` | `(character_id, slot_index)` | N | no | stays — [S-4](#where-similar-tables-stay-apart) |
| 21 | `character_new_items` | `item_uid`; `(owner_scope, owner_id, list_type, slot_index)` | N | yes | → `items` (renamed) — [M-1](#m-1-items--character_new_items--account_cargo_new_items--character_new_titlebook) |
| 22 | `character_new_titlebook` | `(character_id, category, slot_index)` | N | no | → `items` — [M-1](#m-1-items--character_new_items--account_cargo_new_items--character_new_titlebook) |
| 23 | `character_avatar_detail` | `item_uid`, which is the avatar UID — not the item row id | 1 per item | yes | → `items` — [M-2](#m-2-items--character_avatar_detail) |
| 24 | `character_items` | `item_uid` | N | no | **retired** |
| 25 | `character_collectbox_slots` | `(character_id, box_index, slot_index)` | N | no | stays — [S-5](#where-similar-tables-stay-apart) |
| 26 | `character_item_locks` | `(character_id, equipment_lock_id)` | N | no | stays — [S-6](#where-similar-tables-stay-apart) |
| 27 | `character_item_values` | `(character_id, list_kind, sort_order)` | N | no | **split** into `character_item_cooldowns` and `character_item_effects` — [X-1](#where-one-table-becomes-two) |
| 28 | `character_rental_items` | none declared; `(character_id, shop_entry_id)` | N | no | → `character_rentals` (renamed) — [M-3](#m-3-character_rentals--character_rental_items--rental_items) |
| 29 | `rental_items` | none declared; `(character_id, item_id)` | N | no | → `character_rentals` — [M-3](#m-3-character_rentals--character_rental_items--rental_items) |
| 30 | `character_container_state` | `(character_id, list_type)` | N | no | stays — [S-2](#where-similar-tables-stay-apart) |
| 31 | `character_active_quests` | `(character_id, slot)` | N | yes | stays — [S-8](#where-similar-tables-stay-apart) |
| 32 | `character_quest_notify_selections` | `(character_id, slot_index)` | N | no | stays — [S-8](#where-similar-tables-stay-apart) |
| 33 | `character_achievement_complete` | `(character_id, achievement_id)` | N | no | stays |
| 34 | `character_daily_challenge_groups` | `(character_id, group_index)` | N | no | → `character_daily_challenges` (renamed) — [M-5](#m-5-character_daily_challenges--groups--claims) |
| 35 | `character_daily_challenge_claims` | `(character_id, group_index)` | N | no | → `character_daily_challenges` — [M-5](#m-5-character_daily_challenges--groups--claims) |
| 36 | `character_daily_challenge_entries` | `(character_id, group_index, entry_index)` | N | no | stays — [S-9](#where-similar-tables-stay-apart) |
| 37 | `character_daily_challenge_tail_ids` | `(character_id, sort_order)` | N | no | stays — [S-9](#where-similar-tables-stay-apart) |
| 38 | `character_daily_counters` | `(character_id, counter_key)` | N | yes | stays |
| 39 | `character_daily_schedule_states` | `(character_id, sort_order)` | N | no | stays |
| 40 | `character_dungeon_permissions` | `(character_id, sort_order)` | N | no | stays |
| 41 | `character_creatures` | `(character_id, sort_order)` | N | yes | stays — [S-10](#where-similar-tables-stay-apart) |
| 42 | `character_dimensions` | `(character_id, sort_order)` | N | no | stays — [S-11](#where-similar-tables-stay-apart) |
| 43 | `character_growth_weapon_stages` | `(character_id, sort_order)` | N | no | stays |
| 44 | `character_expert_job_recipes` | `(character_id, recipe_id)` | N | no | stays — [S-3](#where-similar-tables-stay-apart) |
| 45 | `character_hotkey_slots` | `(character_id, slot_index)` | N | no | stays — [S-12](#where-similar-tables-stay-apart) |
| 46 | `character_invisible_falgs` | `(character_id, slot_index)` | N | no | stays, renamed `character_invisible_flags` — [S-12](#where-similar-tables-stay-apart) |
| 47 | `character_buy_restrict_items` | `(character_id, sort_order)` | N | no | stays |
| 48 | `character_mercenary_support` | `(owner_character_id, slot)` | N | no | stays |
| 49 | `character_pvp_missions` | `(character_id, sort_order)` | N | no | stays |
| 50 | `mailbox_messages` | `message_id` | N | no | stays — [S-13](#where-similar-tables-stay-apart) |
| 51 | `mailbox_recipients` | `recipient_id`; `(message_id, character_id, folder)` | N | no | stays — [S-13](#where-similar-tables-stay-apart) |
| 52 | `mailbox_attachments` | `attachment_id`; read by `(message_id, ordinal)` | N | no | stays — [Mail attachments](#mail-attachments) |
| 53 | `dungeon_persistent_effect_outbox` | `(source_event_id, effect_kind, effect_scope, scope_target)` | N | yes | stays |

Every table appears once. Rows 8, 9, 10 and 11 name more than one destination because those tables
are split by concept, and row 27 names two because its table splits in half. Every *column* still
has exactly one destination, which the [appendix](#appendix-every-reallocated-column) shows one row
at a time.

## Where tables merge

### M-1 `items` ← `character_new_items` + `account_cargo_new_items` + `character_new_titlebook`

The concept: **one thing, in one slot of one container, whoever owns the container.**

- **Key. Observed, holds.** `character_new_items` is already keyed
  `(owner_scope, owner_id, list_type, slot_index)` by its own `UNIQUE` constraint. Cargo is
  `(account_id, slot_index)` with a `list_type` column that exists but was left out of the
  constraint — and `list_type` is not decoration there: the load parses it as a container type and
  refuses the row unless it is the account cargo (`InventoryEntryAssembly.ReadCargoItemsAsync`). It
  is the same key with one part missing from the constraint, not a different key. The title book is
  `(character_id, category, slot_index)`, where `category` plays the part `list_type` plays
  elsewhere. Its `item_core` is checked against the same 82 bytes, though as an upper bound rather
  than an exact length — joining `items`, whose check is `= 82`, tightens it. Nothing writes that
  table, so nothing can be holding a shorter core. **`category` is not a container
  type and must be transformed on the way in.** It is validated as 0..4, while `list_type` holds a
  container type — `Main = 0`, `Avatar = 1`, `PersonalCargo = 2`, `Equipment = 3`, `Pet = 7` — so
  carrying `category` across unchanged would give a title-book row the same key as a main-inventory
  row and break the `UNIQUE` constraint on the first character who has both. The container type for
  a title book already exists: `TitleBookGeneral = 19` through `TitleBookEvent = 23`. The transform
  is `list_type = 19 + category`, and the same enum already governs the reverse direction where
  locks name a title-book container. Reading a title book back out of `items` needs the inverse,
  `category = list_type - 19`, filtered to the five title-book container types. It cuts the other
  way too: the character's own inventory read must now **exclude** those five, because it does not
  filter by list type today and the container-type parse it feeds accepts only 0, 1, 2, 3 and 7 —
  a title-book row reaching it would throw.
- **Lifetime. Observed, holds.** All three hold a thing until it is moved out of that slot or
  destroyed.
- **Writer. Inferred.** Only `character_new_items` has one today: moving an item. What will write
  the other two is the same act — putting a thing into a different container — and after the merge
  they are rows of one table, so no use case sees a column it did not ask for.

Two tests observed, the concept named, the third inferred and not carrying the merge alone.

`character_new_items.character_id` is retired. All **three** insert paths write
`owner_scope='character', owner_id=@cid, character_id=@cid` — `InsertOwnAsync` and
`InsertStackAsync` in `InventoryItemTables.cs`, and `FillAsync` in `SqliteItemStore.cs` — and
neither update touches either column. The two are the same value by construction.

### M-2 `items` ← `character_avatar_detail`

The concept: **the extra facts an avatar carries, which only an avatar has.**

- **Key. Observed, holds — but not on the column its name suggests.** `character_avatar_detail`
  is keyed `item_uid`, and that is **not** the item row's `item_uid`. It is the avatar UID, which
  the item carries inside its own `item_core` at offset 5..8 (`InventoryItemCore` `Encode`/`Decode`,
  the `Value` field). Every access agrees: the load calls `FindAsync(item.Core.Value)`, the write
  calls `UpsertOwnAsync(characterId, item.Core.Value, item.Core.ItemId, …)`, and the sweep calls
  `DeleteOwnExceptAsync(…, avatars.Select(v => v.Core.Value))`. The two columns are even different
  widths — `int` on the detail side, `long` on the item row side. So the test holds because the key
  of the detail row is a value the item already owns, not because two columns happen to share a
  name. **Implementation must join on `item_core[5..9]`, never on `items.item_uid`.**
- **Lifetime. Observed, holds.** The detail exists exactly as long as that item does;
  `DeleteOwnExceptAsync` clears details for avatars the character no longer holds.
- **Writer. Observed, holds.** `UpsertOwnAsync` runs in the same pass that persists the item.

All three observed. The cost is six sparse columns on `items` that only avatar rows fill; in SQLite
a NULL in a variable-length record costs nothing, so this is not a reason to keep a second table.

Four of the detail's ten columns retire: `item_uid` (the avatar UID is already in the item's core),
`owner_id` and `character_id` (both written as the same character id that `items` already records),
and `item_id` (the avatar's template id, already in the core — the load itself asserts
`detail.ItemId == item.Core.ItemId`).

**What retiring them removes, and why that is safe here.** The load today checks that a detail row
agrees with its item on owner, character and template id, and throws when it does not. Those three
checks exist because the detail lives in a separate table and can be wired to the wrong item. After
the merge the detail *is* columns of the item row, so a disagreement has nowhere to live. The
checks are not being dropped; the condition they test becomes unrepresentable. The remaining
detail checks — a jewel socket of exactly 30 bytes, and both colours within 0..65535 — are about the
value, not the wiring, and must survive.

### M-3 `character_rentals` ← `character_rental_items` + `rental_items`

The concept: **a thing rented until a moment.**

- **Key. Observed, holds.** Neither declares a primary key. Both are read as
  `(character_id, <a template-or-item id>, inventory_template_id, expire_time)` through **the same
  reader**: `SqliteLegacyRentalTable` reuses `SqliteCharacterRentalTable.Rental`. `shop_entry_id`
  and `item_id` land in the same field of the same row type.
- **Lifetime. Observed, holds.** A rental lasts until `expire_time`.
- **Writer. Observed by consumption, holds.** Neither has a writer, but the domain reads both into
  one list and runs its duplicate checks *across both* — on the item id and, separately, on any
  non-zero `inventory_template_id` (`InventoryEntryAssembly.ReadRentalsAsync`). The domain already
  treats them as one set; anything writing one would have to respect the other.

The strongest merge in this document: the domain merged them already and only the storage has not
caught up.

### M-4 `character_skills` ← `character_pvp_skills`

The concept: **what this character has learned, on whichever page.**

- **Key. Observed, holds.** `(character_id, page_index, slot)` in both, column for column, and the
  five non-BLOB columns are the same types.
- **Lifetime. Observed, holds.** Both live with the character.
- **Writer. Observed, holds — because no row is shared.** PvE rows are written by spending skill
  points; PvP rows by seeding the PvP pages when a duel channel's policy asks for them
  (`SelectCharacterPersistenceServices` through `PvpSkillProfileService`). Those are two acts, but
  they touch disjoint sets of rows, and after a `mode` column exists each writes rows of its own
  mode. The third test asks whether a use case is shown columns it did not ask for in the row it is
  changing; here neither act ever meets the other's rows. Contrast `account_settings`, where the
  two acts would share one row — that is what the test refuses.

For implementation: the PvP table carries **four** guarantees the PvE table does not — `NOT NULL`
on its five non-BLOB columns, `CHECK (page_index BETWEEN 0 AND 1)`,
`FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE`, and
`UNIQUE (character_id, page_index, skill_id)`. Each must survive, qualified by `mode` where it is
per-page. Dropping any of them is a silent loss of a constraint the PvP side has today.

### M-5 `character_daily_challenges` ← groups + claims

The concept: **one daily challenge group, and whether its reward has been taken.**

- **Key. Observed, holds.** `(character_id, group_index)` in both, identically.
- **Lifetime. Inferred.** Both should belong to the current daily-challenge period, but nothing
  creates or clears either table today, so this is not observed.
- **Writer. Inferred.** Generating today's groups would write `group_id`; claiming a group's reward
  would write `claimed_at` on the row generation made. The second act marks the row the first made,
  which is a related column and not a neighbouring one.

The key is observed and the concept is named, so the merge does not rest on inference alone. **What
would disprove it:** a generation path that rebuilds `character_daily_challenge_groups` on a
different cycle from the one that expires claims. If the daily-challenge feature is built and works
that way, this merge reverts. Recorded as [D-8](#deferred).

A group with no claim keeps `claimed_at` NULL, which reads the way a missing claims row reads
today: not claimed.

One thing that needs saying, because implementing the merge found it. Today the claim is carried by
the claims **row existing**; `claimed_at` is nullable and no production statement ever writes it, and
the only read asks for `group_index` alone. After the merge the claim is carried by `claimed_at`
being present. The two agree on every row a writer would produce, but they disagree on a row that
already exists with a NULL `claimed_at` — such a row means claimed today and would read as unclaimed
afterwards. Under the project's wipe-don't-migrate rule no such row survives the change, so nothing
is lost; what matters is the other end, and it is a requirement rather than an observation:
**whatever comes to write a claim must set `claimed_at`**, because after this merge that column is
the claim rather than a note beside it.

### M-6 The character's own three tables become five, cut by concept

`characters`, `character_subtype0_fields` and `character_subtype1_fields` pass all three tests
against each other — same key, same lifetime, and all three are written by character creation.
They *could* be one row. They
should not be, because they hold more than one concept between them, and merging them would replace
a bad cut with no cut. (`CharacterRosterService` names three rows as the ones without which a
character cannot be entered — subtype-0, subtype-1 and `character_init_flags`; the `characters` row
itself is inserted earlier in the same method.)

The concepts, and what goes where:

- **`characters` — who this character is, where they are, how far they have come.** Identity, slot,
  deletion, appearance blob, level and experience, position, guild, name tag, skill tree, manage
  level.
- **`character_stats` — the numbers derived from level and equipment.** Every `stat_*` column,
  current HP/MP percentage, and the two carry limits, plus seven columns of subtype-1 whose meaning
  is unsettled and which land here because this is where they are read from ([D-4](#deferred)).
- **`character_condition` — what has happened to this character and what it is in the middle of.**
  Fatigue, mood, the hardcore mode and its outcome, chat ban, returning-user, PC room, disguise,
  channel, dimension flags, crystal contract, champion break, and the four display flags.
- **`character_pvp` — the duel-arena state that is one row per character.** Grade, rating grade,
  rank point, the two streaks, whether the PvP skill pages have been seeded, and `pvp_stat_a`, whose
  name says only that it is a statistic ([D-4](#deferred)).
- **`character_settings` — what this character's player chose.** Exactly what the code already
  calls settings: `CharacterSettingsEntryState` is built from the hotkey slots, the client option
  block and `user_state_bits`. The option block and `user_state_bits` are the one-row-per-character
  half and become this table; `character_hotkey_slots` is the one-row-per-slot half and stays a
  table beside it, the same arrangement as `character_expert_job` and its recipes. `user_state_bits`
  carries this table: it is the only column here whose membership rests on something other than a
  name. That evidence is not one-sided — the same column is also read into the character's public
  state and written to the wire from subtype-0 — so it satisfies the test used to admit it *and*
  the signal used to refuse the display flags. Recorded as [D-13](#deferred); if it is ruled public
  state, this table holds the option block alone and should be reconsidered.
- **`character_expert_job` stays a table** and absorbs `expert_job_type` and `expert_job_exp` from
  subtype-0, repairing a concept that was split across two tables.

`character_settings` exists because of the doctrine above rather than in spite of it: preferences
are a concept, they have no other home, and `account_settings` is the same concept one level up.
Its membership is taken from the code rather than from column names — an earlier draft of this
ruling put the four display flags here on the strength of their names, and the code does not
support that: they are passed through opaquely to the wire beside `emotion_index` and `action_byte`,
and the record the code does call settings does not contain them. They stay in
`character_condition`, which is where they are read from. Likewise `is_event_character` and
`is_hardcore_mode` read like attributes fixed at creation, but nothing writes them — the only
insert into subtype-0 writes `character_id` alone — so that reading has no evidence behind it and
they stay where they are read too. Both readings are recorded in [D-11](#deferred) rather than
acted on.

### M-7 The five one-row-per-character tables that fold

Each is a column group whose columns have a home. The three tests are given per fold.

| Fold | Key | Lifetime | Writer | Concept |
|---|---|---|---|---|
| `character_gold_limits` → `character_stats` | `character_id` in both. Observed, holds. | Both live and die with the character. Observed, holds. | No writer today. Whatever recomputes a cap from level recomputes the other numbers derived from level. Inferred. | Caps derived from level, the same kind of thing as `stat_inventory_limit` and `stat_weight`. |
| `character_crystal_contract` → `character_condition` | `character_id` in both. Observed, holds. | Lives with the character; the contract's grade changes within that life. Observed, holds. | No writer today. What would write it is whatever grants or expires the contract, and what it would read alongside is the rest of what the character is currently under. Inferred. | An entitlement the character is currently under — the same kind of thing as `is_premium_pc_room`, which already lives there. |
| `character_dimension_flags` → `character_condition` | `character_id` in both. Observed, holds. | Lives with the character. Observed, holds. | No writer today. Inferred. | Four per-character flags of the dimension feature whose per-entry rows are `character_dimensions`. Their meaning is not settled; see [D-4](#deferred). |
| `character_init_flags` → `character_condition` + `character_settings` | `character_id` in both destinations. Observed, holds. | Written at creation, lives with the character. Observed, holds. | The one writer inserts `character_id` and `pc_room_state`; the rest are read-only. Observed for the columns going to `character_condition`; inferred for the option block. | Split: `pc_room_state` and the three `champion_break_*` columns are things that happened to the character; `character_option_blob` is a preference. |
| `character_pvp_skill_state` → `character_pvp` | `character_id` in both. Observed, holds — but it does not become `character_pvp`'s key: that table has a row for every character and this one only for characters whose pages were seeded, so the key is declared afresh and this column retires with the rest. | Lives with the character. Observed, holds. | Its writer seeds the PvP skill pages. What it would read alongside in `character_pvp` is the rest of this character's duel-arena state. Observed, holds. | Whether the PvP pages have been seeded — a PvP fact, which is why it lands in the PvP table rather than among things that happened to the character. |

The row's existence is the whole of `character_pvp_skill_state`'s meaning: production writes only
`character_id` and reads only `SELECT 1`, and `initialized_at` and `updated_at` are named by no
production statement at all. It becomes one boolean on a row that always exists, and all three of
its columns retire — the two timestamps because nothing reads them, and `character_id` because the
table it folds into does not have the same row set.

Column names collide on the way in and are qualified: `character_dimension_flags.flag1..4` become
`dimension_flag1..4`, and `character_crystal_contract.cube_type`/`cube_grade` become
`crystal_cube_type`/`crystal_cube_grade`.

`character_condition` ends up with 48 columns. That is wide, and the fair objection is that it might
be a residue rather than a concept — everything left over once the nameable parts were taken out.
The answer has two halves, and the second is a concession.

Every group that could be named **on evidence** was taken out: PvP to `character_pvp`, the sub-job
to `character_expert_job`, the settings the code itself names to `character_settings`, derived
numbers to `character_stats`, and subtype-1's identity columns to `characters`. What remains mostly
answers one question — what has happened to this character and what is it in the middle of — and
reads that way column by column: fatigue, mood, a chat ban's end, a hardcore death count, a PC
room, a disguise, a channel.

The concession is that some of it does not answer that question, and this ruling could not name
where it belongs. Three groups, recorded rather than dressed up: the five equipped-creature columns
([D-2](#deferred)); the columns whose meaning is unsettled ([D-4](#deferred)); and six columns —
`costume_flag`, `aura_flag`, `title_display_flag`, `pet_display_flag`, `emotion_index` and
`action_byte` — that the server passes to the wire without interpreting, so nothing here can say
whether they are things that happened or things the player chose ([D-11](#deferred)). An earlier
draft moved four of them to `character_settings` on the strength of their names; that was withdrawn
when the code refused to support it. Sixteen of forty-eight — five, five and six — are a residue,
and saying so is better than a concept name that would have to stretch to cover them.

## Where one table becomes two

### X-1 `character_item_cooldowns` + `character_item_effects` ← `character_item_values`

The three tests say when two tables are one concept. This is the other direction: one table holding
two.

`character_item_values` is keyed `(character_id, list_kind, sort_order)` where `list_kind` is
`'cooltime'` or `'effect'`. The load turns that string into two different domain list types and
carries them onward as two separate lists, and `value` is a remaining cooldown in one and an effect
magnitude in the other. A column whose value decides what another column means is precisely what the
red line forbids, and this is the clearest instance in the database — clearer than
`character_daily_counters`, whose `period` and `value` mean the same thing for every key.

Splitting puts the answer in the table name. Each half keeps `(character_id, sort_order)` as its key
and `item_id` and `value` as its payload; `list_kind` retires, because which table a row is in says
what it used to say.

This is inside the item work's own territory — the same block that rewrites locks, rentals and the
collect box — so it is ruled here rather than deferred. An earlier draft left it in place and
recorded it as a contract deviation; that was the wrong call, because it left two readings of the
same table in one document: a target shape that kept it and a deferred entry that removed it.

The ordering it fixes: nothing. Neither half is read by any other block.

## Where similar tables stay apart

Each of these looks like a merge and is not. Each names what the refusal rests on, which the rules
above allow to be one of three things: an observed test, the red line, or a recorded want of
evidence. Twelve of the fifteen name one of the three tests; S-4 rests on the red line and the
`tail_ids` half of S-9 together with S-8 rest on want of evidence, and each of those is registered
— C-3 for the red line, D-9 for want of evidence. That is wider than "name which of the three tests
fails", and deliberately so: a refusal that is honest about resting on something else is worth more
than one that picks the nearest test to blame.

| # | Pair | Refused on | Why |
|---|---|---|---|
| S-1 | `accounts` / `account_settings` | writer | Same key, same lifetime, both observed. But the two would share one row, and saving a client preference — quick-chat banks, hotkeys, the main game option — is not the act that registers an account or moves currency. When the settings screen writes, the wallet is an unrelated neighbour in the row it is changing. Contrast [M-4](#m-4-character_skills--character_pvp_skills), where the two acts never meet in a row. |
| S-2 | `account_cargo_state` / `character_container_state` | key | `account_id` against `(character_id, list_type)`: one row about the cargo as a whole against one row per list. Different arity, different owner. |
| S-3 | `character_expert_job` / `character_expert_job_recipes` | key | `character_id` against `(character_id, recipe_id)`. One character, many recipes. `character_expert_job` therefore stays a table rather than folding: it is a concept of its own with a 1:N half beside it. |
| S-4 | `character_dark_knight_combo_skill_pages` / `character_knight_shield_deck` | red line | Both keys have arity two but index different spaces, and the payloads are of different kinds: a page body BLOB against a shield's item id. Merging them needs a column whose value decides what the payload column means, which is exactly the shape the red line forbids. This refusal rests on the red line and on the observed payload types, not on a guess about future writers. |
| S-5 | `items` / `character_collectbox_slots` | key | The shape `(character_id, box_index, slot_index)` matches `items`', but the two keys locate different kinds of object: the collect box's payload is `item_id`, a template id, where `items` holds `item_core`, an instance. A key that locates a kind of thing is not the key that locates a thing. |
| S-6 | `items` / `character_item_locks` | key | `(character_id, equipment_lock_id)`. A lock is located by lock id; `inventory_list_type` and `slot` are payload, not key. |
| S-7 | `items` / `character_item_cooldowns`, `character_item_effects` | key | `(character_id, sort_order)`, and `item_id` is a template id. Located by position within a list, not by container and slot. These are cooldowns and effects attached to a kind of item, not the state of one item. |
| S-8 | `character_active_quests` / `character_quest_notify_selections` | want of evidence | `(character_id, slot)` against `(character_id, slot_index)`. Same arity, and nothing shows the two slot spaces are the same coordinate — one indexes the quest bar, the other indexes notification choices, and no code relates them. Refused for want of evidence rather than against evidence; recorded as [D-9](#deferred). |
| S-9 | `character_daily_challenges` / `character_daily_challenge_entries`, and `/ …_tail_ids` | key; want of evidence | Entries carry a third key part, `entry_index`: one group, many entries. The tail ids are keyed `(character_id, sort_order)` and read as a bare ordered list of values with no reference to a group; nothing shows `sort_order` and `group_index` are the same coordinate. The first is refused on an observed key, the second for want of evidence; recorded as [D-9](#deferred). |
| S-10 | `character_condition` / `character_creatures` | key | The five equipped-creature columns are one row per character; `character_creatures` is `(character_id, sort_order)`. They belong to the same concept and cannot share a table. See [D-2](#deferred). |
| S-11 | `character_condition` / `character_dimensions` | key | The same shape of problem: four per-character flags against `(character_id, sort_order)` rows. |
| S-12 | `character_hotkey_slots` / `character_invisible_flags` | key | Both are `(character_id, slot_index)` with the same arity, but the two slot spaces are unrelated — one indexes hotkeys, the other indexes whatever "invisible" applies to. Same problem as S-5: matching shape, different space. |
| S-13 | `mailbox_messages` / `mailbox_recipients` | key | `message_id` against `(message_id, character_id, folder)`. One message, many recipients. |
| S-14 | `character_settings` / `character_hotkey_slots` | key | `character_id` against `(character_id, slot_index)`. One concept — what the player chose — with a one-row half and a one-row-per-slot half, exactly like S-3. The narrow half is a table because its concept has no other home; the wide half cannot join it. |
| S-15 | `items` / `character_rentals` | key | Both concern a thing the character has, and this is the pair most likely to be mistaken for one. But a rental is keyed `(character_id, item_id)` — a character and a *template*, with no container and no slot — while an item is keyed by where it sits. A rental says what the character is entitled to hold; an item says what is in a slot. They are not two halves of one thing. |

The three expiries below (S-15) are worth one more sentence. `items`, `character_rentals` and
`mailbox_attachments` each carry an expiry, which looks like the
"per-item expiry" the contract asks to be gathered into one place. They are three different
expiries: when this avatar stops being wearable, when this rental entitlement lapses, and when this
posted attachment can no longer be claimed. Each belongs to the row it is on, and gathering them
would need a key that none of the three shares with the others.

## The two named rulings

### Mail attachments

`mailbox_attachments` **stays with mail**. It does not become part of `items`.

- **Key. Not observed.** Attachments are located by `(message_id, ordinal)`; items by
  `(owner_scope, owner_id, list_type, slot_index)`. These match only by admitting a third kind of
  owner — a message — into `owner_id`, which today holds an account or a character. The key test
  must be observed, and this one is conditional on a change to `items` itself.
- **Lifetime. Not observed.** An attachment exists while its message does. If a message is a
  container, that is an item's lifetime; the "if" is the same admission.
- **Writer. Inferred.** Neither has one: there is no send path and no claim path.

The key test fails on the rule that it must be observed, so the merge cannot proceed on the tests.
The refusal does not rest on the inferred writer.

There is a second reason, observed. The row carries four things that are not part of an item:
`source_list_type`, `source_slot_index`, `source_item_uid` — where this thing sat before it was
posted — and `detail_json`. All four are read and reach the domain
(`CharacterMailboxTables` into `CharacterMailboxAttachmentStorageRow`, consumed in
`CharacterEntryStateServices`). Folding the row into `items` means carrying that provenance on every
item or creating a per-attachment side table, which trades one table for another and blurs both
concepts.

One thing is worth recording even though it does not change the ruling: `mailbox_attachments` is
the **last live instance of the older decomposed item shape** — it carries `item_template_id`,
`item_kind`, `item_count`, `instance_value`, `durability`, `seal_flag`, `option_value`,
`expire_time`, `marker_16` and `pet_serial_or_handle` alongside a nullable `item_core`. The other
two tables of that generation are retired here; this one survives because it is still read.
Converting it belongs to whoever builds sending and claiming, because only then is there an answer
to what "a thing inside a letter" is. Recorded as [D-1](#deferred).

### The gold limit, and the ordering it settles

`character_gold_limits` **goes to `character_stats`**. It is a pair of caps computed from level —
the same kind of thing as `stat_inventory_limit` or `stat_weight`: derived, recomputable, cached so
the packet can be built without recomputing. Its `updated_at` retires with the table; no production
statement names it.

What this settles about ordering: the gold limit is read from **both** sides.
`CharacterEntryAssembly` reads it and feeds it to the gold-limit rule service;
`InventoryEntryAssembly` reads it again for the inventory record. The table it moves into is created
by the character-side work. Whichever order is chosen, the item side's read has to be rewritten
once; the question is whether it gets rewritten twice.

> **The character body goes first; the item side follows.**

With the character body done, `character_stats` exists and the gold limit is already there, so the
item work rewrites its read once, as part of rewriting its own reads. In the other order the item
work rewrites that read against `character_gold_limits`, and the character work comes back and
rewrites it again. Two readers rather than one makes this stronger, not weaker: the character-side
read is being rewritten anyway by the work that creates the table.

### A second coupling, between the character body and skills

The gold limit is not the only place two blocks meet. `PvpSkillProfileService` is one method that
reads and writes `character_pvp_skill_state` — which this ruling folds into `character_pvp`, the
**character body's** work — and in the same sequence inserts and reads `character_pvp_skills`, which
becomes `mode` rows of `character_skills`, the **skills** work. The class says so itself: reading
the marker, writing the skills and writing the marker is a sequence, and a sequence is a decision.

The same argument as for the gold limit applies, and gives the same answer:

> **The character body goes first; skills follow.**

With `character_pvp` in place, the skills work rewrites that method once while it is rewriting the
skill reads it already owns. In the other order the method is rewritten twice.

So the character body goes first, and the item side and skills each follow it; they do not touch each
other and may go in either order between themselves. The account side has no work of its own — three
blocks, not four.

Two tables that change belong to none of the three as the root contract drew them:
`character_daily_challenges` (the merge in [M-5](#m-5-character_daily_challenges--groups--claims))
and `character_invisible_flags` (a spelling corrected). Both are character-side tables read by the
character-side load, so the character body's block is where they belong; neither is named in the
contract's scope, which is why they are also recorded under
[Beyond the contract's scope](#beyond-the-contracts-scope).

## The red line

The task contract forbids a target table that carries several concepts behind a discriminator
column — a row whose meaning depends on the value of one of its own columns. Checking it means
looking at every table in the target shape, not only the new ones, because the red line is a
property of the shape this change leaves behind.

### The red line, checked table by table

All 43 tables of the target shape, one row each. Listing every table rather than only the ones
with a candidate is deliberate: four review rounds found a table that had been passed over, each
time under a sentence claiming the rest had nothing. A table with no candidate says so here.

| Table | Candidate | Verdict |
|---|---|---|
| `account_cargo_state` | `selection_key` | **Clear**, for a structural reason: at most one row per account, so no value in it can select among meanings across rows. `value32` and `item_count` are that one cargo's state. |
| `account_increase_chance_lottery_progress` | — | Three key columns and nothing else. |
| `account_premiums` | `premium_type` | **Clear.** `end_time` is when it ends, for every type. |
| `account_settings` | `hotkey_key_type` | **Unresolved.** It sits beside the `hotkey_slots` BLOB and may decide how that BLOB is read, which would be reinterpretation. The reader takes it as a plain integer and settles nothing. Already the subject of [D-3](#deferred). |
| `accounts` | — | Identity and balances, each meaning itself. |
| `character_achievement_complete` | `achievement_id` | **Unresolved.** `p1`..`p4` are read as four bare integers and handed straight up. Whether they mean the same thing for every achievement is the question `character_dimensions` raises, and the code answers neither. Recorded as [D-14](#deferred). |
| `character_active_quests` | `quest_id` | **Clear.** `trigger_value` is that quest's trigger for every quest — one number with one meaning. |
| `character_buy_restrict_items` | `entry_id`, `flag` | **Unresolved.** `sentinel_or_value` names the problem itself, and the domain calls the whole list `Unknown730Entries` — the code says outright that it does not know what this is. Recorded as [D-14](#deferred). |
| `character_collectbox_slots` | — | A box, a slot and what kind of thing was collected into it. |
| `character_condition` | `champion_break_mode`, with `champion_break_key_id` | **Unresolved.** `champion_break_value` sits beside them and the reader takes all three as bare integers. [D-4](#deferred) already says these names do not say what they count or break; whether the mode reinterprets the value is part of the same unsettled question. |
| `character_container_state` | `list_type` | **Clear.** `list_param16` is that list's parameter, the same kind of number for every list. |
| `character_creatures` | `mode_flag` | **Clear.** It decides whether `mode1_field0a` and `mode1_field0b` are sent at all, not what they mean when they are — the payload codec writes the pair only when the flag is 1. |
| `character_daily_challenge_entries` | `track_like_id` | **Unresolved.** `value_a` and `value_b` sit beside it and are read as bare integers, the same shape as `character_dimensions`. Recorded as [D-10](#deferred). |
| `character_daily_challenge_tail_ids` | — | An order and a value. |
| `character_daily_challenges` | — | A group and whether it was claimed. |
| `character_daily_counters` | `counter_key` | **Clear.** `period` is a period and `value` is a count, for every key. |
| `character_daily_schedule_states` | `mode_or_state` | **Unresolved.** The column's own name says it is one thing or another, and `param_a` and `param_b` beside it are generic payload. Recorded as [D-12](#deferred). |
| `character_dark_knight_combo_skill_pages` | — | A page and its body. |
| `character_dimensions` | `dim_key` | **Unresolved.** `val1` and `val2` may or may not mean the same thing for every key; nothing in the code says. Recorded as [D-10](#deferred). |
| `character_dungeon_permissions` | `dungeon_id` | **Clear.** `clear_state` is how far that dungeon is cleared, for every dungeon. |
| `character_expert_job` | `expert_job_type` | **Clear, and also this ruling's own creation** — M-6 moves this column in from subtype-0. It decides which of `enchanter_endurance` and the two `disjoint_machine_*` columns reaches the wire, not what any of them means. Absent-or-present, like `character_creatures.mode_flag`. |
| `character_expert_job_recipes` | — | A character and a recipe. |
| `character_growth_weapon_stages` | — | An order and a stage. |
| `character_hotkey_slots` | — | A slot and its value. |
| `character_invisible_flags` | — | A slot and its flag. |
| `character_item_cooldowns` | — | **Created by this ruling to remove a violation.** The old `character_item_values` used `list_kind` to say whether `value` was a cooldown or an effect magnitude; splitting the table puts the answer in the table name. |
| `character_item_effects` | — | The other half of the same split. |
| `character_item_locks` | `state` | **Clear.** `remaining_seconds` is a duration in every state; the reader coalesces it to zero when absent. |
| `character_knight_shield_deck` | — | A slot and a shield. |
| `character_mercenary_support` | — | A slot, a supporter and two skills. |
| `character_pvp` | — | Grades, points and streaks, each meaning itself. |
| `character_pvp_missions` | `mission_id` | **Clear.** `progress_value` is that mission's progress, for every mission. |
| `character_quest_notify_selections` | — | A slot and the quest chosen for it. |
| `character_rentals` | — | A character, a template, a template and a time. |
| `character_settings` | — | Two columns, neither selecting for the other. |
| `character_skills` | `mode` | **Clear, and it is this ruling's own creation.** `page_index`, `slot`, `skill_id`, `level` and `extra_values` mean the same thing in both modes. What differs is which constraints apply, not what a column means — and `mode` is part of the key, so it extends the coordinate space rather than reinterpreting a payload. |
| `character_stats` | — | Every column is a number in its own right. |
| `characters` | — | No column whose value could change what another means. |
| `dungeon_persistent_effect_outbox` | `effect_kind` | **Clear by kind.** It is a work queue, and a queue row's payload is polymorphic by definition; `payload_json` and `result_json` are declared opaque rather than reinterpreted. A queue is not a place where a concept lives. |
| `items` | `owner_scope` | **Clear.** It decides which table `owner_id` points at, and nothing else. `slot_index` and `item_core` mean the same thing for a character, a cargo and a title book, and `list_type` does too once the title book's `category` is transformed into its container type (M-1). The sparse columns — `cargo_character_id` and the seven `avatar_*` — are absent-or-present, not reinterpreted. |
| `mailbox_attachments` | `item_type`, `item_kind` | **Unresolved.** This is the older decomposed item shape, with `instance_value`, `option_value`, `pet_serial_or_handle` and `marker_16` beside a nullable `item_core`. Whether `item_kind` reinterprets those cannot be settled while the table has no writer. Already the subject of [D-1](#deferred). |
| `mailbox_messages` | `mail_type` | **Clear.** A title, a body and an amount, for every kind of mail. |
| `mailbox_recipients` | — | A recipient, a folder and four flags. |

**No table in the target shape is judged a violation.** Of the 43, 21 have no candidate column at all, 14 have one that turns out to select presence or point at an owner rather than change a meaning, and 8 are unresolved — the code does not settle whether the candidate reinterprets its neighbours, and each is recorded in [Deferred](#deferred) rather than assumed harmless.

The one table that *was* judged a violation, `character_item_values`, is not in this list because this ruling splits it; see [X-1](#where-one-table-becomes-two).

## Deferred

Recorded, not resolved. Each names who resolves it.

| # | Item | Resolved by |
|---|---|---|
| D-1 | `mailbox_attachments` still carries the older decomposed item shape beside a nullable `item_core`. Converting it needs a definition of what an item inside a letter is, which needs send and claim paths to exist. | Whoever builds mail sending and claiming. |
| D-2 | Five equipped-creature columns (`creature_field1`..`4` and `creature_buffer`) sit in `character_condition` though they belong with creatures. They cannot move to `character_creatures`, whose key is `(character_id, sort_order)` (S-10). | A later cut, if a one-row-per-character equipped-creature concept earns its own table. |
| D-3 | `account_settings.hotkey_slots` is a BLOB while `character_hotkey_slots` is one row per slot — the same idea stored two ways. This ruling changes neither. | A later cut on the account side. |
| D-4 | Thirteen columns land where they are read from rather than where their meaning puts them, because their meaning is not settled. Seven into `character_stats`: `progress1`, `progress2`, `equip_list_trailing`, `flag_byte`, `server_timestamp`, `quest_shop_count`, `equipped_creature_level`. Five into `character_condition`: `trailing_byte`, `black_count`, and the three `champion_break_*` columns, whose names do not say what they count or break. One into `character_pvp`: `pvp_stat_a`, whose name says only that it is a statistic. Every one of these homes is provisional, not ruled. | Whoever settles what those packet fields mean. The character-side implementation carries the provisional homes in the meantime. |
| D-5 | `account_cargo_new_items.character_id` becomes `items.deposited_by_character_id`. The name is a reading, not a fact: the code carries the nullable value into `InventoryStorageItem.CharacterId` without interpreting it. If it turns out to mean something else, the column keeps its place and changes its name. | **Resolved** by the item-side implementation — see [the resolutions](#resolved-by-the-item-side-implementation). |
| D-6 | `character_new_items.character_id` is retired on the premise that it always equals `owner_id`. Proven for all three insert paths and both updates. Two things go with it and are mechanical only because the equality holds: the column is still **read** — it is in a `SELECT` list and in a `WHERE character_id = @cid AND list_type = 0` — and there is an index `idx_character_new_items_character_space(character_id, list_type, slot_index)` that must be rebuilt on `owner_id`. The implementation should confirm no fourth writer has appeared before relying on the equality for either. | **Resolved** by the item-side implementation — see [the resolutions](#resolved-by-the-item-side-implementation). |
| D-7 | Retiring `character_avatar_detail`'s `owner_id`, `character_id` and `item_id` removes three agreement checks the load performs today. The merge makes disagreement unrepresentable, which is the argument for removing them — the implementation must confirm that claim on the merged shape rather than assume it, and must keep the checks that are about the value rather than the wiring: a jewel socket of exactly 30 bytes, and both colours within 0..65535. | **Resolved** by the item-side implementation — see [the resolutions](#resolved-by-the-item-side-implementation). |
| D-8 | M-5 rests on an observed key with an inferred lifetime and writer. A daily-challenge generation path that rebuilds groups on a different cycle from the one that expires claims would disprove it and revert the merge. | Whoever builds daily challenges. |
| D-9 | Two pairs are refused for want of evidence rather than against it: `character_active_quests` / `character_quest_notify_selections` (S-8), and `character_daily_challenges` / `character_daily_challenge_tail_ids` (S-9). Both would merge if their slot spaces turn out to be one coordinate. | Whoever builds quests and daily challenges. |
| D-10 | Two unchanged tables may carry a discriminator that reinterprets its neighbours, and nothing in the code settles either: `character_dimensions.dim_key` over `val1`/`val2`, and `character_daily_challenge_entries.track_like_id` over `value_a`/`value_b`. If either does, that table needs the same split as X-1. | Whoever settles what the dimension feature and the daily challenges store. |
| D-11 | Six columns in `character_condition` are passed to the wire without the server interpreting them — `costume_flag`, `aura_flag`, `title_display_flag`, `pet_display_flag`, `emotion_index`, `action_byte` — so nothing says whether they are things that happened or things the player chose. Two readings were considered and not acted on for want of evidence: that the four display flags are preferences, and that `is_event_character` and `is_hardcore_mode` are attributes fixed at creation. Both are plausible from the names and unsupported by any code. If either is confirmed, those columns move — the display flags to `character_settings`, the two attributes to `characters`. | The character-side implementation. |
| D-12 | `character_daily_schedule_states.mode_or_state` may be a discriminator that reinterprets `param_a` and `param_b`; nothing in the code settles it. If it is, the table needs the same split as X-1. | Whoever settles what the daily schedule stores. |
| D-13 | `character_settings` rests on `user_state_bits`, and that column is also read into the character's public state and written to the wire from subtype-0. If it is ruled public state rather than a setting, `character_settings` holds the client option block alone and should be reconsidered — either kept as a one-column table or folded back. | The character-side implementation. |
| D-14 | Two more unchanged tables carry a column that may reinterpret its neighbours and nothing settles it: `character_achievement_complete.achievement_id` over `p1`..`p4`, and `character_buy_restrict_items.entry_id`/`flag` over `sentinel_or_value`. If either does, that table needs the same split as X-1. | Whoever settles what achievements and buy restrictions store. |
| D-15 | `character_avatar_detail`'s `PRIMARY KEY (item_uid)` has no successor: after the merge nothing stops two item rows carrying the same avatar UID in their cores. Offset 5..8 is a general-purpose field with no allocator and no uniqueness check, so the loss cannot be argued away. Either something upstream makes duplicates impossible, or `items` needs a stored avatar-UID column and a partial unique index. | **Resolved** by the item-side implementation — see [the resolutions](#resolved-by-the-item-side-implementation). |
| D-16 | `items` inherits a weaker uniqueness than `account_cargo_new_items` declared: `UNIQUE(account_id, slot_index)` becomes, for account rows, `UNIQUE(account_id, list_type, slot_index)`. Harmless while the cargo has one list type. A partial unique index on `(owner_id, slot_index) WHERE owner_scope = 'account'` restores it. | **Resolved** by the item-side implementation — see [the resolutions](#resolved-by-the-item-side-implementation). |

## Resolved by the item-side implementation

The five entries above that name the item-side implementation as their owner are answered here.
Each says what was checked and where it can be checked again.

### D-6 — the retired `character_id` holds

Re-counted every write statement under `Server/src` that names `character_new_items`: three inserts
(`InsertOwnAsync` and `InsertStackAsync` in `InventoryItemTables.cs`, `FillAsync` in
`SqliteItemStore.cs`), three deletes (`DeleteOwnExceptAsync`'s two statement forms and
`SqliteItemStore.EmptyAsync`) and two updates (`UpdateOwnAsync`, `SetStackCountAsync`). All three
inserts write `owner_scope='character', owner_id=@cid, character_id=@cid`; neither update names
either column. **No fourth writer has appeared.** The two reads that name the column become reads
of `owner_id`, and `idx_character_new_items_character_space` is rebuilt as
`items(owner_id, list_type, slot_index)`.

### D-5 — the cargo's character id keeps its place and loses the reading

The column is carried into `InventoryStorageItem.CharacterId` and, for a cargo row, **never
checked and never written to the wire**: `NormalizeAccountCargoItems` validates owner type, owner id
and container type and not this value, and `ToItemState` drops it before the item reaches the
protocol. So the code supports two facts and no more — it is a character id, and only a cargo row
ever has one. `deposited_by_character_id` claims a third thing the code does not say.

The column is therefore named **`cargo_character_id`**. It survives because dropping it would change
what the whole-record characterization dumps, and it is named for what is observable. Whoever builds
depositing into the cargo learns what it means and renames it then.

### D-7 — the three agreement checks lose their subject, and two value checks stay

The load today refuses a detail row whose `owner_id`, `character_id` or `item_id` disagrees with its
item. Confirmed on the merged shape: after the merge the avatar columns *are* columns of the item
row, so "the detail's owner" and "the item's owner" are one value, and the detail's `item_id` is the
core's own `ItemId`. There is no second row to disagree, and no place for a disagreement to be
stored. The checks are not dropped; the state they test cannot be represented.

Four checks are about the value rather than the wiring and **must survive**, and two of them are
easy to lose because their source changes shape:

- a jewel socket of exactly 30 bytes — now `CHECK (avatar_jewel_socket IS NULL OR
  length(avatar_jewel_socket) = 30)` in the declaration and still checked on the way out;
- both colours within 0..65535;
- an item of kind 8 whose `item_core[5..9]` is zero or negative is refused;
- an item of kind 8 belonging to a character with no avatar columns is refused, while the same item
  in the account cargo is carried with none.

### D-16 — the cargo's slot uniqueness is restored

`account_cargo_new_items` declares `UNIQUE(account_id, slot_index)`, which `items`' four-part
constraint weakens for account rows. Restoring it costs one partial unique index and takes the
merged table back to exactly today's strength — no tighter, no looser:

```sql
CREATE UNIQUE INDEX IF NOT EXISTS idx_items_account_slot
    ON items(owner_id, slot_index) WHERE owner_scope = 'account';
```

**Restored.**

### D-15 — the avatar detail's primary key is restored, because a test already constructs the case

The first working of this said not to restore it: `character_avatar_detail`'s
`PRIMARY KEY (item_uid)` guarantees at most one *description* per avatar UID rather than at most one
*item*, and a unique index on a stored avatar-UID column would be strictly stronger — refusing a
state today's database accepts. Taking the census of what the item side's rules are covered by found
the case already built and already required to fail:

`SqliteEntryInventoryMutationRepositoryTests.DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision`
gives character 70 an avatar of UID 9000, then has character 60's lease try to insert an item
carrying the same UID. Today that reaches `UpsertOwnAsync`, whose `ON CONFLICT (item_uid)` update is
conditioned on the row belonging to the writer, so it touches nothing, the save throws, the whole
lease rolls back, and the test then asserts that **character 70's avatar is untouched**.

So the guarantee is neither theoretical nor unreachable: one character claiming another's avatar UID
is a state the server refuses today, and a covered one. Merging without a successor would make that
insert succeed — a behaviour change, and a covered assertion lost. The contract forbids both.

**Restored**, by the means D-15 itself named:

- `items.avatar_uid`, nullable, non-null in exactly the rows that carry avatar columns;
- `CREATE UNIQUE INDEX IF NOT EXISTS idx_items_avatar_uid ON items(avatar_uid)
  WHERE avatar_uid IS NOT NULL;`

This reproduces today's strength on the case that matters and is stricter on one that does not.
An avatar in the account cargo has no detail row today and gets no `avatar_uid` after the merge, so
it stays outside the index — a cargo avatar sharing a UID with a character's avatar is permitted
today and stays permitted. Two *different* characters claiming one UID are refused today and stay
refused, which is the case the test builds.

The one it tightens: **one character holding two item rows that carry the same UID**. Today both
resolve to the single detail row and the load succeeds; afterwards the second row is refused. That
is a real difference and worth stating rather than glossing, because the first working of this
section claimed the restoration was exact and it is not. It is accepted deliberately — the two rows
would be two items each claiming to be the same avatar, nothing needs them, and the alternative is
to drop a refusal a test relies on.

The cost is the column [M-2](#m-2-items--character_avatar_detail) retires, put back on purpose. M-2
retires it because the value is already inside `item_core`; that is true and irrelevant to this,
because a value inside a BLOB cannot be indexed. `items` therefore has **16** columns, not 15, and
`character_avatar_detail.item_uid` lands rather than retiring.

### Two consequences carried over from the merge itself

Neither is a ruling; both are things M-2 makes true and did not say.

**A row updated out of being an avatar must lose its avatar columns.** Today a stale detail is swept
away by `DeleteOwnExceptAsync` when the character stops holding that avatar. After the merge the
avatar columns travel with the item row, so a row *updated* from an avatar core to a non-avatar core
would keep them. That is not a cosmetic leftover: the protocol encoder throws when an item of a kind
other than 8 arrives carrying avatar detail. The item write must therefore set the avatar columns
from what the caller holds and null them otherwise, rather than leaving them alone.

**Moving an item through the item store must carry its avatar columns with it.** M-2 says the detail
lives exactly as long as the item, which is true of the item *concept* and not of the item *row*:
`SqliteItemStoreSession` writes a slot by deleting whatever row held it and inserting a new one, and
says so outright — "that gives the item a new row identity, which is deliberate and safe: nothing
outside these tables is keyed on it, and the one satellite table there is hangs off the avatar UID
carried inside the item core." After the merge the avatar facts are keyed on the row, so the reason
that made it safe is gone. `SqliteItemStoreTests.Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`
asserts an avatar keeps its facts across a move, so the store must read them by avatar UID before it
deletes and write them back on the insert.

## Where this ruling differs from the task contract

Five rulings contradict the root contract as literally written. All five are the same kind of
disagreement: the contract states a mechanism where its own outcome is broader, and applying the
tests reaches the outcome by a different mechanism.

Who reconciles them is not this ruling's to decide. Each names the success condition it touches and
what the outcome behind it would be; the root controller owns whether to restate those conditions or
to reject the ruling. **The blocks of implementation should not start against a contradicted
success condition until it has.**

| # | Contract | This ruling | Why |
|---|---|---|---|
| C-1 | SC-6 and the account line of Scope/In: "the account-side at-most-one-row settings are folded into the account body". | `account_settings` stays a table. | The third test refuses it (S-1). The outcome behind SC-6 — each account-side concept has exactly one place to live — holds: settings in one place, entitlements in one, lottery progress in one. Restate SC-6 at the outcome level. |
| C-2 | SC-4 and its evidence line: the six at-most-one-row character tables "no longer exist, their columns appearing on the character body". | Five fold; `character_expert_job` stays. | It is a concept of its own with a 1:N half beside it (S-3), and folding it would dissolve a table for its shape rather than for its columns. The outcome — no at-most-one-row table that is merely a column group — holds. |
| C-3 | SC-5 and Scope/In: "job-specific skill pages are not split by job". | `character_dark_knight_combo_skill_pages` and `character_knight_shield_deck` stay apart. | Merging them requires a discriminator that reinterprets the payload column, which the red line forbids (S-4). The contract asks for something its own non-goal excludes. The red line is the more fundamental instruction and wins; the conflict is recorded rather than resolved silently. |
| C-4 | SC-1 and Scope/In name the collect box among the containers whose items should share one home. | `character_collectbox_slots` stays. | It does not hold items. Its payload is a template id, so the sentence's premise — that these are the same kind of object — does not hold for it (S-5). |
| C-5 | Scope/In: the per-item expiry states are to be "gathered into one place". | Three expiries stay where they are. | They are three different facts with three different keys, not one fact in three places (S-15). |

Nothing else in this ruling contradicts the contract. C-1 and C-2 are one disagreement seen twice:
both SC-4 and SC-6 fix a mechanism ("fold it in") where the outcome is "one concept, one home", and
this ruling applies the outcome consistently on both sides — which is why `account_settings` and
`character_expert_job` stay while five other one-row tables fold.

### Beyond the contract's scope

The contract requires separate authorization for a table-shape change outside its scope. Two of the
rulings above are outside it — not against it, which is what the five deviations are, but not named
by it either:

| Change | Why it is here |
|---|---|
| `character_daily_challenge_groups` + `character_daily_challenge_claims` become `character_daily_challenges` ([M-5](#m-5-character_daily_challenges--groups--claims)) | The contract's scope names the item side, the character body, skills and the account side. Daily challenges are none of those. The merge passes the tests and repairs a concept split in two, but it is an addition to the work, not part of it |
| `character_invisible_falgs` becomes `character_invisible_flags` | A misspelling in a table name. Correcting it costs nothing and is not asked for |

Both are small, and neither changes what any other ruling depends on. If either is declined, the
table stays as it is and nothing else moves.

## Target shape

43 tables. Tables not listed here are unchanged, keeping their present name and columns; they appear
in the [column ledger](#column-ledger) as carried across unchanged.

### What the per-table lists do and do not say

Each list below gives a table's columns and where each comes from. Three things are governed by rule
rather than repeated per table, so that the lists are enough to write the declarations from:

- **Type, nullability and default.** A column keeps those of the source column it comes from. A
  column with more than one source keeps the strictest of them. Nine columns have sources that
  disagree. Five are `character_skills`', which `character_pvp_skills` declares `NOT NULL` and
  `character_skills` does not; the keys table below writes those out and says the tightening binds
  PvE rows. Four are `items`': `item_core` carries an 82-byte check from the two new-items tables
  that the title book does not (also written out below, though the title book declares it
  `NOT NULL` like the others), and `owner_id`, `list_type` and `slot_index` are `NOT NULL` on the
  two new-items tables while the title book's corresponding columns are nullable — a composite
  primary key does not imply `NOT NULL` in SQLite. Taking the strictest makes all three `NOT NULL`,
  which the keys table now states rather than leaving to be derived.

  A separate thing, which is not a disagreement and needs no rule: a column can land beside
  *neighbours* declared differently from itself. `character_init_flags.pc_room_state` is nullable
  and lands beside columns that are `NOT NULL DEFAULT 0`; `character_subtype0_fields.expert_job_type`
  is `NOT NULL DEFAULT 0` and lands beside nullable ones. A neighbour's declaration is not a reason
  to change a column's own.
- **Sparseness beats the rule above.** A column that only some rows of its new table can have
  becomes nullable, whatever its source said, **and drops the source's `DEFAULT`**, and a `CHECK` on
  it is rewritten to hold only when it is present. Dropping the default matters as much as the
  nullability: five of the six `avatar_*` columns are `NOT NULL DEFAULT 0`, and a column that
  defaults to zero on every ordinary item does not say absent, it says zero. Absent must mean
  absent.
- **A narrow table's `NOT NULL` does not survive a fold into a wide one.** A table whose every row
  is the thing it describes can require its columns; the table it folds into cannot, because most of
  its rows are not that thing. Two cases arise, and both would otherwise produce a table nothing
  could be inserted into:
  - `character_avatar_detail.jewel_socket` is `BLOB NOT NULL CHECK(length(...) = 30)` with **no
    default**. Every row of that table is an avatar; most rows of `items` are not. It becomes
    nullable, with the check rewritten (see the `items` row below).
  - `character_gold_limits.gold_carry_limit` and `.auction_gold_limit` are `INTEGER NOT NULL` with
    **no default**. That table has no writer and no rows; the table they fold into, `character_stats`,
    gets a row for every character at creation, and that write cannot supply what it does not have.
    They become `INTEGER NOT NULL DEFAULT 0` — which is also what the two readers already substitute
    when the row is missing.
  Two more are the same shape, and they are hazardous for the opposite reason — not that nothing
  can be inserted, but that what is inserted reads differently from what is there today. Both are
  columns of tables that may have no row now and will have one for every character after
  ([which tables have one row per character](#which-tables-have-one-row-per-character-and-who-writes-it)):
  - `character_dimension_flags.flag1`..`flag4` are nullable with no default, while that table's own
    contract says all four read as zero when there is no row.
  - `character_expert_job`'s `giveup_count`, `disjoint_machine_grade`, `disjoint_machine_endurance`
    and `enchanter_endurance` are nullable with no default, and their reader substitutes four zeros
    for a missing row — but reads them with `GetInt32`, which throws on a NULL. A created row with
    four NULLs would fail the very load a missing row survives today.

  All eight become `INTEGER NOT NULL DEFAULT 0`, so the absent row and the created row say the same
  thing. `character_crystal_contract` needs nothing: its `DEFAULT 0` and `DEFAULT 255` already equal
  what its reader substitutes for a missing row.
- **Constraints.** Every constraint on a source table survives on the table its columns land in,
  qualified where the merge makes that necessary. Keys are given per table below because a merge
  can change them; checks, uniqueness and foreign keys are not repeated, and dropping one is a
  silent loss of a guarantee the database has today.
- **Indexes.** The database declares six, and each has a stated fate rather than a rule to infer
  one from. `idx_characters_account` stays as it is. `idx_character_new_items_character_space` names
  a retired column and becomes an index on `items(owner_id, list_type, slot_index)`.
  `idx_character_rental_items_char` and `idx_rental_items_char` both become
  `character_rentals(character_id)` — one index, not two, and it is a prefix of that table's new key,
  so it may simply be dropped. `idx_account_cargo_items_owner` and `idx_character_items_character` go
  with the two retired tables they index. One index is added rather than carried:
  `idx_items_account_slot`, a partial unique index restoring the cargo's own slot uniqueness
  ([D-16](#d-16--the-cargos-slot-uniqueness-is-restored)), and `idx_items_avatar_uid`, another
  restoring the avatar detail's primary key
  ([D-15](#d-15--the-avatar-detail-s-primary-key-is-restored-because-a-test-already-constructs-the-case)).

### The four columns with no source

Every other column inherits its declaration. These four are new, so this ruling has to give them
one:

| Column | Declaration | Why |
|---|---|---|
| `character_pvp.character_id` | `INTEGER PRIMARY KEY` | The same declaration every one-row-per-character table already uses |
| `character_skills.mode` | `TEXT NOT NULL CHECK (mode IN ('pve', 'pvp'))` | Two values and no third; text rather than a number so a row says which set of pages it belongs to without a lookup, the way `items.owner_scope` already does |
| `character_pvp.skills_initialized` | `INTEGER NOT NULL DEFAULT 0` | It replaces the presence of a row, and the absence of that row means not seeded — which is what a default of zero says |
| `character_settings.character_id` | `INTEGER PRIMARY KEY` | The same declaration every one-row-per-character table already uses |

### Which tables have one row per character, and who writes it

Six target tables hold at most one row per character, and after this cut all six hold **exactly**
one, written when the character is created. This has to be said, because three of them are assembled
from tables that today may have no row at all, and a fold that does not say who writes the row turns
"no row" into a question nobody answers.

| Table | Today | After |
|---|---|---|
| `characters` | written at creation | the same write, now also carrying subtype-1's `skill_tree_index` and its `DEFAULT -1` — the creation write does not name that column, so the default is what every character gets |
| `character_stats` | subtype-1 written at creation, 21 of its 33 columns supplied | the same write, now also supplying the two gold limits by their new defaults |
| `character_condition` | subtype-0 and `character_init_flags` written at creation; the crystal contract and dimension flags may have no row | one row at creation, still naming `pc_room_state` — it stays nullable, as its source is, and its reader throws on a NULL, so the write has to supply the zero the `character_init_flags` insert supplies today; the columns from the two optional tables take the defaults that equal what a missing row reads as today |
| `character_settings` | assembled from `character_init_flags` (written at creation) and subtype-0 (written at creation) | one row at creation |
| `character_pvp` | assembled from `characters` (always) and subtype-0 (always); `character_pvp_skill_state` may have no row | one row at creation, with `skills_initialized` starting at 0 — which is what a missing marker row means today |
| `character_expert_job` | may have no row; the reader substitutes zeros | one row at creation, its four columns defaulting to 0 |

Eight defaults among these six tables are not zero, and each has to be carried across exactly. Five
of them reach the wire, so getting one wrong changes a byte the client receives — which the contract
forbids outright. Two of the eight are what every character actually holds only because the creation
write does not name the column; the rest are named by a writer or read from an absent row:

| Column | Default | Why it matters |
|---|---:|---|
| `characters.skill_tree_index` | **-1** | It reaches the wire through a mapping that turns -1 into `0xFF` and 0 into `0x00`. Every character has -1 today, because the creation write does not name this column and the default supplies it |
| `character_settings.user_state_bits` | **3** | Written to the wire as a byte |
| `character_condition.return_user_flag` | **1** | Written to the wire |
| `character_condition.pc_room_id` | **65537** | Does not reach the wire, but is range-checked on the way through |
| `character_condition.crystal_cube_grade` | **255** | Matches both its source declaration and what its reader substitutes for a missing row |
| `characters.direction` | **5** | Reaches the wire. The creation write does not name it, so the default is what every character faces |
| `characters.level` | **1** | Reaches the wire, but the creation write names it, so the default only matters if a later writer stops naming it |
| `characters.area_state` | **3** | Not read anywhere today; a writer sets it to 3 on every position update. Listed so the declaration is not quietly changed |

The rest default to zero, including the ten columns the rule above gives a default to — the two
gold limits, the four dimension flags and the sub-job's four.

### Keys of the tables that change

| Table | Key | Other constraints it must carry |
|---|---|---|
| `items` | `item_uid`, autoincrementing | `UNIQUE(owner_scope, owner_id, list_type, slot_index)`, from `character_new_items` and now covering cargo and the title book; `CHECK (owner_scope IN ('character', 'account'))`; `CHECK (length(item_core) = 82)`; and the avatar columns relaxed to nullable by the sparseness rule, with `CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)` in place of `character_avatar_detail`'s unconditional 30-byte check. `cargo_character_id` is nullable, as its source is. `owner_id`, `list_type` and `slot_index` are `NOT NULL`, the strictest of their sources. **Two source constraints have no direct successor, and each is settled in [the resolutions](#resolved-by-the-item-side-implementation): `account_cargo_new_items`'s `UNIQUE(account_id, slot_index)` is restored as a partial unique index (D-16), and `character_avatar_detail`'s `PRIMARY KEY (item_uid)` is restored as one too, over a stored `avatar_uid` (D-15).** |
| `character_rentals` | `(character_id, item_id)` | declared for the first time; neither source declares a key, and the domain already refuses duplicates on this pair |
| `characters` | `character_id` | as today |
| `character_stats` | `character_id` | none beyond the key; subtype-1 declares none |
| `character_condition` | `character_id` | none beyond the key |
| `character_pvp` | `character_id` | **`FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE`**, carried from `character_pvp_skill_state`, which is the only thing that table declares besides its key |
| `character_settings` | `character_id` | none beyond the key |
| `character_expert_job` | `character_id` | none beyond the key; its four own columns take `NOT NULL DEFAULT 0` under the rule above |
| `character_skills` | `(character_id, mode, page_index, slot)` | `UNIQUE(character_id, mode, page_index, skill_id)`; `CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)`; `NOT NULL` on `character_id`, `mode`, `page_index`, `slot`, `skill_id` and `level` — all five of `character_pvp_skills`' non-BLOB columns plus `mode` itself, and `character_id` has to be named because a composite primary key does not imply `NOT NULL` in SQLite; `FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE`. All of it from `character_pvp_skills`, which declares every one and `character_skills` none. The `NOT NULL` and the cascade now bind PvE rows too, which is a tightening; nothing writes a PvE row without those values, and there is no existing data to contradict it |
| `character_daily_challenges` | `(character_id, group_index)` | none beyond the key |
| `character_invisible_flags` | `(character_id, slot_index)` | none beyond the key |
| `character_item_cooldowns` | `(character_id, sort_order)` | none beyond the key |
| `character_item_effects` | `(character_id, sort_order)` | none beyond the key |

### `items` — 16 columns

| Column | From |
|---|---|
| `item_uid` | `character_new_items.item_uid`; also `account_cargo_new_items.item_uid` |
| `owner_scope` | `character_new_items.owner_scope`; `'account'` for cargo rows, `'character'` for title-book rows |
| `owner_id` | `character_new_items.owner_id`; also `account_cargo_new_items.account_id`, `character_new_titlebook.character_id` |
| `list_type` | `character_new_items.list_type`; also `account_cargo_new_items.list_type`, and `character_new_titlebook.category` **transformed to `19 + category`** — the title-book container types, without which a title-book row collides with a main-inventory row |
| `slot_index` | `character_new_items.slot_index`; also `account_cargo_new_items.slot_index`, `character_new_titlebook.slot_index` |
| `item_core` | `character_new_items.item_core`; also `account_cargo_new_items.item_core`, `character_new_titlebook.item_core` |
| `cargo_character_id` | `account_cargo_new_items.character_id`; NULL elsewhere. Named for what the code supports and no more — see [D-5](#d-5--the-cargos-character-id-keeps-its-place-and-loses-the-reading) |
| `created_at` | `character_new_items.created_at`; also `account_cargo_new_items.created_at` |
| `updated_at` | `character_new_items.updated_at`; also `account_cargo_new_items.updated_at` |
| `avatar_uid` | `character_avatar_detail.item_uid` — the avatar UID, stored rather than read out of `item_core` because only a stored column can be indexed ([D-15](#d-15--the-avatar-detail-s-primary-key-is-restored-because-a-test-already-constructs-the-case)) |
| `avatar_expire_date` | `character_avatar_detail.expire_date` |
| `avatar_clear_id` | `character_avatar_detail.clear_avatar_id` |
| `avatar_jewel_socket` | `character_avatar_detail.jewel_socket` |
| `avatar_color1` | `character_avatar_detail.color1` |
| `avatar_color2` | `character_avatar_detail.color2` |
| `avatar_delete_date` | `character_avatar_detail.delete_date` |

Key: `item_uid`, with `UNIQUE(owner_scope, owner_id, list_type, slot_index)` — the constraint
`character_new_items` already carries, now covering cargo and the title book as well. The avatar
columns are reached through the avatar UID inside `item_core`, never through `item_uid`;
`avatar_uid` repeats that value so a unique index can name it, and the two must agree.

### `character_rentals` — 4 columns

| Column | From |
|---|---|
| `character_id` | `character_rental_items.character_id`; also `rental_items.character_id` |
| `item_id` | `character_rental_items.shop_entry_id`; also `rental_items.item_id` |
| `inventory_template_id` | `character_rental_items.inventory_template_id`; also `rental_items.inventory_template_id` |
| `expire_time` | `character_rental_items.expire_time`; also `rental_items.expire_time` |

Key: `(character_id, item_id)` — declared for the first time. Neither source declares one, and the
domain already refuses duplicates on this pair.

### `characters` — 28 columns

Carried from `characters`: `character_id`, `account_id`, `name`, `name_bytes`, `job`, `grow_type`,
`level`, `exp`, `clone_title_item_id`, `slot_index`, `delete_flag`, `town_id`, `area_id`, `pos_x`,
`pos_y`, `direction`, `area_state`, `appearance_blob`, `bonus_sp`, `bonus_tp`,
`ex_equip_slot_stat`, `user_state`.

Joined by:

| Column | From |
|---|---|
| `skill_tree_index` | `character_subtype1_fields.skill_tree_index` |
| `name_tag_item_id` | `character_subtype1_fields.name_tag_item_id` |
| `name_tag_expire_time` | `character_subtype1_fields.name_tag_expire_time` |
| `manage_level` | `character_subtype1_fields.manage_level` |
| `guild_power_war` | `character_subtype1_fields.guild_power_war` |
| `guild_level` | `character_subtype0_fields.guild_level` |

### `character_stats` — 30 columns

| Column | From |
|---|---|
| `character_id` | `character_subtype1_fields.character_id`; also `character_gold_limits.character_id` |
| the 19 `stat_*` columns | the same 19 columns of `character_subtype1_fields` |
| `current_hp_mp_percent` | `character_subtype1_fields.current_hp_mp_percent` |
| `progress1`, `progress2`, `equipped_creature_level`, `equip_list_trailing`, `flag_byte`, `server_timestamp`, `quest_shop_count` | `character_subtype1_fields` — provisional homes, [D-4](#deferred) |
| `gold_carry_limit` | `character_gold_limits.gold_carry_limit` |
| `auction_gold_limit` | `character_gold_limits.auction_gold_limit` |

### `character_condition` — 48 columns

| Column | From |
|---|---|
| `character_id` | `character_subtype0_fields.character_id`; also `character_init_flags.character_id`, `character_crystal_contract.character_id`, `character_dimension_flags.character_id` |
| `stamina`, `fatigue_penalty`, `fatigue_update`, `fatigue_display_update` | `character_subtype0_fields` |
| `is_event_character`, `is_hardcore_mode`, `is_hardcore_dead`, `hardcore_death_count` | `character_subtype0_fields` — [D-11](#deferred) for the first two |
| `is_private_store`, `chat_ban_end_time`, `mood_value`, `chaos_point` | `character_subtype0_fields` |
| `black_count`, `trailing_byte` | `character_subtype0_fields` — [D-4](#deferred) |
| `creature_field1`, `creature_field2`, `creature_field3`, `creature_field4`, `creature_buffer` | `character_subtype0_fields` — [D-2](#deferred) |
| `pc_room_id`, `is_premium_pc_room` | `character_subtype0_fields` |
| `server_group_id`, `channel_display_mode`, `channel_type`, `is_return_user`, `return_user_flag` | `character_subtype0_fields` |
| `disguise_kind`, `is_disguised` | `character_subtype0_fields` |
| `emotion_index`, `action_byte` | `character_subtype0_fields` — [D-11](#deferred) |
| `costume_flag`, `aura_flag`, `title_display_flag`, `pet_display_flag` | `character_subtype0_fields` — [D-11](#deferred) |
| `link_slot_enabled`, `link_type_a`, `link_type_b` | `character_subtype0_fields` |
| `pc_room_state` | `character_init_flags.pc_room_state` |
| `champion_break_key_id`, `champion_break_mode`, `champion_break_value` | `character_init_flags` — [D-4](#deferred) |
| `crystal_cube_type`, `crystal_cube_grade` | `character_crystal_contract.cube_type`, `.cube_grade` |
| `dimension_flag1`..`dimension_flag4` | `character_dimension_flags.flag1`..`.flag4` |

`pet_display_flag` sits with the other three display flags rather than with the five
equipped-creature columns; both groups stay in `character_condition`, the creature columns under
[D-2](#deferred).

### `character_pvp` — 8 columns

| Column | From |
|---|---|
| `character_id` | new — the folded `character_pvp_skill_state` cannot supply it, because that table has a row only for characters whose PvP pages were seeded and this one has a row for every character |
| `skills_initialized` | new — the existence of a `character_pvp_skill_state` row, which becomes a flag on a row that always exists |
| `pvp_grade` | `characters.pvp_grade` |
| `pvp_rating_grade` | `characters.pvp_rating_grade` |
| `pvp_rank_point` | `character_subtype0_fields.pvp_rank_point` |
| `pvp_stat_a` | `character_subtype0_fields.pvp_stat_a` — [D-4](#deferred) |
| `pvp_win_streak` | `character_subtype0_fields.pvp_win_streak` |
| `pvp_lose_streak` | `character_subtype0_fields.pvp_lose_streak` |

### `character_settings` — 3 columns

| Column | From |
|---|---|
| `character_id` | new — no source column is left over for it: each folded table's own `character_id` merges into the key of the table it folds into, and this table is assembled from two columns of tables that fold elsewhere |
| `character_option_blob` | `character_init_flags.character_option_blob` |
| `user_state_bits` | `character_subtype0_fields.user_state_bits` |

`character_hotkey_slots` is the one-row-per-slot half of the same concept and stays its own table
(S-14). Together they are what `CharacterSettingsEntryState` is built from.

### `character_expert_job` — 7 columns

| Column | From |
|---|---|
| `character_id` | `character_expert_job.character_id` |
| `giveup_count`, `disjoint_machine_grade`, `disjoint_machine_endurance`, `enchanter_endurance` | `character_expert_job` |
| `expert_job_type` | `character_subtype0_fields.expert_job_type` |
| `expert_job_exp` | `character_subtype0_fields.expert_job_exp` |

### `character_skills` — 7 columns

| Column | From |
|---|---|
| `character_id` | `character_skills.character_id`; also `character_pvp_skills.character_id` |
| `mode` | new — which set of pages this row belongs to |
| `page_index` | `character_skills.page_index`; also `character_pvp_skills.page_index` |
| `slot` | `character_skills.slot`; also `character_pvp_skills.slot` |
| `skill_id` | `character_skills.skill_id`; also `character_pvp_skills.skill_id` |
| `level` | `character_skills.level`; also `character_pvp_skills.level` |
| `extra_values` | `character_skills.extra_values`; also `character_pvp_skills.extra_values` |

Key: `(character_id, mode, page_index, slot)`, with `UNIQUE(character_id, mode, page_index,
skill_id)`, `CHECK (page_index BETWEEN 0 AND 1)` for PvP rows, `NOT NULL` on the five non-BLOB
columns plus `mode`, and the cascading foreign key — all carried over from `character_pvp_skills`,
which declares every one of them where `character_skills` declares none.

### `character_daily_challenges` — 4 columns

| Column | From |
|---|---|
| `character_id` | `character_daily_challenge_groups.character_id`; also `..._claims.character_id` |
| `group_index` | `character_daily_challenge_groups.group_index`; also `..._claims.group_index` |
| `group_id` | `character_daily_challenge_groups.group_id` |
| `claimed_at` | `character_daily_challenge_claims.claimed_at` |

### `character_invisible_flags` — 3 columns

`character_id`, `slot_index`, `flag_value`, all from `character_invisible_falgs`. Name only; the
misspelling is not carried forward.

### `character_item_cooldowns` — 4 columns

| Column | From |
|---|---|
| `character_id` | `character_item_values.character_id`, for its `'cooltime'` rows |
| `sort_order` | `character_item_values.sort_order`, for its `'cooltime'` rows |
| `item_id` | `character_item_values.item_id`, for its `'cooltime'` rows |
| `value` | `character_item_values.value`, for its `'cooltime'` rows — a remaining cooldown |

### `character_item_effects` — 4 columns

| Column | From |
|---|---|
| `character_id` | `character_item_values.character_id`, for its `'effect'` rows |
| `sort_order` | `character_item_values.sort_order`, for its `'effect'` rows |
| `item_id` | `character_item_values.item_id`, for its `'effect'` rows |
| `value` | `character_item_values.value`, for its `'effect'` rows — an effect magnitude |

`character_item_values.list_kind` retires: which of the two tables a row is in says what it used to
say. The ordering the load applies today — cooldowns first, then effects, then by position within
each — becomes two reads in that order rather than one `CASE`.

### Unchanged — 30 tables

`accounts` (18), `account_settings` (6), `account_premiums` (4),
`account_increase_chance_lottery_progress` (3), `account_cargo_state` (5),
`character_achievement_complete` (7), `character_active_quests` (4),
`character_buy_restrict_items` (5), `character_collectbox_slots` (4),
`character_container_state` (3), `character_creatures` (11),
`character_daily_challenge_entries` (6), `character_daily_challenge_tail_ids` (3),
`character_daily_counters` (4), `character_daily_schedule_states` (6),
`character_dark_knight_combo_skill_pages` (3), `character_dimensions` (5),
`character_dungeon_permissions` (4), `character_expert_job_recipes` (2),
`character_growth_weapon_stages` (3), `character_hotkey_slots` (3), `character_item_locks` (6),
`character_knight_shield_deck` (3), `character_mercenary_support` (6),
`character_pvp_missions` (4), `character_quest_notify_selections` (3),
`dungeon_persistent_effect_outbox` (19), `mailbox_attachments` (19), `mailbox_messages` (13),
`mailbox_recipients` (9) — 191 columns in total, carried across untouched.

## Column ledger

Every one of the 420 columns has exactly one destination. A column that merges with another into a
single target column still has a destination; it is not lost.

| | Columns |
|---|---:|
| Carried across in unchanged tables | 191 |
| Reallocated by the rulings above | 229 |
| **Total** | **420** |

Of the 229 reallocated, **185 land somewhere** and **44 retire**. The target shape's 166 columns are
those 185 sources resolved into 162 target columns — merged where several sources become one column,
doubled where the split sends one source into both halves — plus 4 that have no source
(`character_pvp.character_id`, `character_pvp.skills_initialized`,
`character_settings.character_id`, `character_skills.mode`).

### Retired columns — 44

| Column(s) | Count | Why |
|---|---:|---|
| all of `character_items` | 20 | Previous-generation decomposed storage. No production write path; read only by an emptiness probe. |
| all of `account_cargo_items` | 15 | The same, for the cargo. |
| `character_new_items.character_id` | 1 | All three insert paths write it equal to `owner_id`; neither update touches it. |
| `character_avatar_detail.owner_id`, `.character_id` | 2 | Both written as the same character id; `items` already records the owner. |
| `character_avatar_detail.item_id` | 1 | The avatar's template id, already inside `item_core`; the load itself asserts the two are equal. |
| `character_gold_limits.updated_at` | 1 | Named by no production statement. |
| all of `character_pvp_skill_state` | 3 | Its two timestamps are named by no production statement, and its key cannot become `character_pvp`'s: that table has a row for every character and this one only for characters whose PvP pages were seeded. What the row meant survives as `character_pvp.skills_initialized`. |
| `character_item_values.list_kind` | 1 | Which of the two tables the split puts a row in says what this column used to say ([X-1](#where-one-table-becomes-two)). |

### Three rules `items` carries that are not constraints

None can be written as a `CHECK` without reaching inside `item_core`, so all three are stated here
and belong to the code that reads and writes the table.

- **Which rows have avatar columns.** An item carries them when `item_core[0]` — its kind — is 8.
  That is the condition the load already uses before it goes looking for a detail row.
- **A cargo row never has them.** The load says so outright: an avatar sitting in the account cargo
  is carried with no detail at all, while a character's avatar with no detail is an error. After the
  merge that is one rule over two columns — `owner_scope` and `item_core[0]` — and the error it
  raises on the character side has to survive as a check on the avatar columns being present.
- **A character's avatar must carry a non-zero UID.** The load refuses an item of kind 8 whose
  `item_core[5..9]` is zero or negative, before it goes looking for the detail. That is the
  precondition of the join M-2 requires, so it survives with it.

### What merging the avatar detail also removes

`character_avatar_detail` is keyed `PRIMARY KEY (item_uid)`, and `item_uid` there is the avatar UID
([M-2](#m-2-items--character_avatar_detail)). That key says: at most one detail for any one avatar
UID. After the merge the detail is columns of the item row, and nothing stops two item rows carrying
the same avatar UID inside their cores and each holding its own avatar columns.

The guarantee could be kept, but only by storing the avatar UID as a column of `items` so that a
partial unique index can name it — which puts back the column M-2 retires, on the grounds that its
value is already in `item_core`.

**The loss is not obviously harmless, and an earlier draft of this section claimed it was.** That
claim rested on the avatar UID coming from the item's own core, which is true and proves nothing:
offset 5..8 is a general-purpose `Value` field, not an avatar-UID field. A stackable item stores its
count there. There is no allocator for avatar UIDs anywhere in the server and no uniqueness check on
that field, so nothing observable rules out two item rows carrying the same one.

So this is left open rather than decided: recorded as [D-15](#deferred) for the item work, which is
the only place with enough of the picture to say whether the guarantee is worth a stored column and
a partial index, or whether nothing can produce a duplicate in the first place.

### What `items` loses from the cargo's own constraint

`account_cargo_new_items` declares `UNIQUE(account_id, slot_index)`. On `items` the corresponding
constraint is `UNIQUE(owner_scope, owner_id, list_type, slot_index)`, which for an account row means
`UNIQUE(account_id, list_type, slot_index)` — **weaker**, because it permits two rows in the same
cargo slot under different list types.

Today's cargo has one list type, so nothing can produce that pair; the weakening is theoretical
until a second cargo list exists. Keeping the old strength costs one partial unique index on
`(owner_id, slot_index) WHERE owner_scope = 'account'`. Recorded as [D-16](#deferred) with the same
owner, and named here rather than left to be discovered.

### What retiring the two legacy item tables also removes

The only production use of `character_items` and `account_cargo_items` is a guard:
`InventoryEntryAssembly` refuses to load when the new table is empty and the old one has rows,
throwing "migration required before entry loading". The guard exists to stop an old-format inventory
being silently ignored.

The project's standing rule for this stage is wipe, don't migrate: no backfill or repair path is to
be written for existing rows. The migration this guard demands will therefore never exist, and this
change makes an old-shaped database unreadable anyway. So the guard goes with the tables.

**What is lost:** point the server at a database from before this change and the result is an empty
inventory rather than a loud refusal. There is no version marker to replace the guard with, and
inventing one is outside this work. Recorded so the loss is a decision rather than an accident.

## Where the facts can be checked

| Fact | Where |
|---|---|
| 53 tables, 420 columns | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`, `TableDeclarations` |
| Keys, constraints, and the PvP skill table's four extra guarantees | the same declarations |
| Which tables have production writes | every `INSERT INTO` / `UPDATE … SET` / `DELETE FROM` under `Server/src` naming a declared table; eight files issue them |
| Cardinality | the accessor's return type in `Server/src/Dfo.Server.Persistence/**/*Tables.cs`, `SqliteCharacterStore.cs` and `SqliteItemStore.cs` — one row or a list |
| `CharacterEntryAssembly` reads 33 tables; selecting a character reads all 53 | the class comment on `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs`, and `SelectCharacterPreparationService.PrepareAsync`, which also runs both inventory loads, the account load, dungeon-effect recovery and the PvP skill load |
| `character_new_items.character_id` equals `owner_id` in all three insert paths | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` (`InsertOwnAsync`, `InsertStackAsync`) and `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs` (`FillAsync`) |
| The item core's layout — template id at 1..4, avatar UID at 5..8 | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemCore.cs`, `Encode` and `Decode` |
| `character_avatar_detail` is keyed by the avatar UID, not the item row id | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` — `FindAsync(item.Core.Value)`, `UpsertOwnAsync(…, item.Core.Value, item.Core.ItemId, …)`, `DeleteOwnExceptAsync(…, avatars.Select(v => v.Core.Value))` |
| The load asserts the detail agrees with its item on owner, character and template id | the same file, the avatar-detail check |
| The two rental tables share one reader, and the domain reads them as one set with duplicate checks spanning both | `Server/src/Dfo.Server.Persistence/Inventory/InventoryStateTables.cs` (`SqliteLegacyRentalTable` reusing `SqliteCharacterRentalTable.Rental`) and `InventoryEntryAssembly.ReadRentalsAsync` |
| The legacy item tables are read only as an emptiness probe, and what that probe guards | `InventoryStateTables.cs` (`SqliteLegacyCharacterItemTable`, `SqliteLegacyAccountCargoTable`) and the two guards in `InventoryEntryAssembly` |
| The gold limit is read by both the character side and the item side | `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs` (into the gold-limit rule service) and `InventoryEntryAssembly.ReadGoldLimitsAsync` |
| `character_pvp_skill_state` is a presence marker, and who writes it | `Server/src/Dfo.Server.Persistence/Characters/CharacterSkillTables.cs` (`ExistsAsync`, `InsertAsync`) and `Server/src/Dfo.Server.Application/Characters/SelectCharacterPersistenceServices.cs` through `PvpSkillProfileService` |
| `character_item_values.list_kind` becomes two domain list types | `InventoryStateTables.cs` (the ordering `CASE`) and `InventoryEntryAssembly.ReadItemValuesAsync` |
| Cargo `list_type` is parsed as a container type and refused unless it is the account cargo | `InventoryEntryAssembly.ReadCargoItemsAsync` |
| Attachment provenance reaches the domain | `Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs` into `CharacterMailboxAttachmentStorageRow`, consumed in `Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs` |
| The three mandatory one-to-one rows are written together at character creation | `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs` — "The three rows without which the character cannot be entered" |
| `return_user_flag` is written to the wire, and `pc_room_id` is range-checked on the way through | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs` and `Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs` |
| The character's own inventory read does not filter by list type, and the container-type parse it feeds accepts only five values | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` (`ReadByCharacterAsync`) and `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` (`ParseContainerType`, and `ParseLockContainerType` for the title-book range) |
| An avatar with a non-positive UID is refused before the detail is looked for | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`, just above the detail read |
| A cargo avatar is carried with no detail, while a character's avatar without one is an error | the same file, the note at the end of the cargo read and the character-side detail check |
| `character_condition`'s `pc_room_state` is refused when NULL | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`, the PC-room read |
| The dimension flags read as four zeros when there is no row | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`, the dimension-flag read; the same promise is written on `ICharacterDimensionFlagTable` in `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs` |
| Offset 5..8 is a general-purpose field: a stackable item stores its count there | `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`, `StackableItemCore.Create` |
| What each optional one-row table reads as when it has no row — four zeros for the sub-job, `(0, 255)` for the crystal contract, `(0, 0)` for the gold limits | `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs`, the substitutions beside each `Find…` call, and `InventoryEntryAssembly.ReadGoldLimitsAsync` |
| The sub-job's four columns are read with `GetInt32`, which throws on a NULL | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`, the expert-job read |
| `skill_tree_index` reaches the wire through a mapping, and the creation write does not name it | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs` (`SkillTreeIndexOnTheWire`) and `Server/src/Dfo.Server.Persistence/Characters/CharacterSkillTables.cs` (the 21 columns subtype-1 is created with) |
| The two pairs named as merge candidates before this ruling | `.rks-agent/tasks/schema-cut-by-domain/routing-decision.md`, under remaining uncertainty — not the task contract |
| The title book's `category` is validated as 0..4 | `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs`, the title-book row check — the schema declares no `CHECK` for it |
| The title-book container types, and that `category` maps to them in order | `Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs` (`TitleBookGeneral = 19` … `TitleBookEvent = 23`), and the `category` → `list_type` table in `Docs/GM工具_新背包表结构与ItemCore语义.md` |
| The title book's `item_core` is bounded at 82 bytes rather than fixed at it | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`, the title-book read through `EntryBlob.Bounded`, which throws only above the maximum |
| What the code itself calls a character's settings | `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs` (`CharacterSettingsEntryState`) and its construction in `Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs` |
| `user_state_bits` is also read into the character's public state and written to the wire | the same construction, and `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs` |
| The four display flags reach the wire without the server interpreting them | `CharacterEntryStateServices.cs`, where they enter `CharacterSubtype0EntryState` beside `emotion_index` and `action_byte`, and the payload codec that writes them |
| The only insert into subtype-0 writes `character_id` alone | `Server/src/Dfo.Server.Persistence/Characters/CharacterSkillTables.cs`, `SqliteCharacterSubtype0Table` |
| The avatar detail's surviving value checks — 30-byte jewel socket, both colours in 0..65535 | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`, the avatar-detail check |
| `character_creatures.mode_flag` decides whether two columns are sent, not what they mean | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs`, the pet section |
| `character_new_items.character_id` is still read, and an index names it | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` (the `SELECT` list and the main-inventory `WHERE`) and `IndexDeclarations` in the schema |

## Appendix: every reallocated column

The 229 columns of the 23 tables whose contents move. Each appears once and has one
destination. The 191 columns of the 30 unchanged tables are not repeated here; they keep their
table and their name.

The destination is where the column lands, not the transform it needs on the way. Two rows need
one: `character_new_titlebook.category` becomes `19 + category` (M-1), and the four
`character_dimension_flags.flagN` are renamed `dimension_flagN` (M-7). Four rows name two
destinations because [X-1](#where-one-table-becomes-two) splits their table in half; that is one
destination expressed as a row partition, not two homes for one column.

Four target columns have no row below because no source column becomes them:
`character_skills.mode`, `character_pvp.character_id`, `character_pvp.skills_initialized`
(from the presence of a `character_pvp_skill_state` row) and `character_settings.character_id`.

| Source column | Destination |
|---|---|
| `character_new_items.item_uid` | `items.item_uid` |
| `character_new_items.owner_scope` | `items.owner_scope` |
| `character_new_items.owner_id` | `items.owner_id` |
| `character_new_items.character_id` | retired |
| `character_new_items.list_type` | `items.list_type` |
| `character_new_items.slot_index` | `items.slot_index` |
| `character_new_items.item_core` | `items.item_core` |
| `character_new_items.created_at` | `items.created_at` |
| `character_new_items.updated_at` | `items.updated_at` |
| `account_cargo_new_items.item_uid` | `items.item_uid` |
| `account_cargo_new_items.account_id` | `items.owner_id` |
| `account_cargo_new_items.character_id` | `items.cargo_character_id` |
| `account_cargo_new_items.list_type` | `items.list_type` |
| `account_cargo_new_items.slot_index` | `items.slot_index` |
| `account_cargo_new_items.item_core` | `items.item_core` |
| `account_cargo_new_items.created_at` | `items.created_at` |
| `account_cargo_new_items.updated_at` | `items.updated_at` |
| `character_new_titlebook.character_id` | `items.owner_id` |
| `character_new_titlebook.category` | `items.list_type` |
| `character_new_titlebook.slot_index` | `items.slot_index` |
| `character_new_titlebook.item_core` | `items.item_core` |
| `character_avatar_detail.item_uid` | `items.avatar_uid` |
| `character_avatar_detail.owner_id` | retired |
| `character_avatar_detail.character_id` | retired |
| `character_avatar_detail.item_id` | retired |
| `character_avatar_detail.expire_date` | `items.avatar_expire_date` |
| `character_avatar_detail.clear_avatar_id` | `items.avatar_clear_id` |
| `character_avatar_detail.jewel_socket` | `items.avatar_jewel_socket` |
| `character_avatar_detail.color1` | `items.avatar_color1` |
| `character_avatar_detail.color2` | `items.avatar_color2` |
| `character_avatar_detail.delete_date` | `items.avatar_delete_date` |
| `character_rental_items.character_id` | `character_rentals.character_id` |
| `character_rental_items.shop_entry_id` | `character_rentals.item_id` |
| `character_rental_items.inventory_template_id` | `character_rentals.inventory_template_id` |
| `character_rental_items.expire_time` | `character_rentals.expire_time` |
| `rental_items.character_id` | `character_rentals.character_id` |
| `rental_items.item_id` | `character_rentals.item_id` |
| `rental_items.inventory_template_id` | `character_rentals.inventory_template_id` |
| `rental_items.expire_time` | `character_rentals.expire_time` |
| `character_items.item_uid` | retired |
| `character_items.owner_scope` | retired |
| `character_items.owner_id` | retired |
| `character_items.character_id` | retired |
| `character_items.list_type` | retired |
| `character_items.slot_index` | retired |
| `character_items.item_template_id` | retired |
| `character_items.item_kind` | retired |
| `character_items.stack_count` | retired |
| `character_items.instance_value` | retired |
| `character_items.durability` | retired |
| `character_items.seal_flag` | retired |
| `character_items.option_value` | retired |
| `character_items.equipment_lock_id` | retired |
| `character_items.expire_time` | retired |
| `character_items.marker_16` | retired |
| `character_items.pet_serial_or_handle` | retired |
| `character_items.extra_json` | retired |
| `character_items.created_at` | retired |
| `character_items.updated_at` | retired |
| `account_cargo_items.item_uid` | retired |
| `account_cargo_items.account_id` | retired |
| `account_cargo_items.slot_index` | retired |
| `account_cargo_items.item_template_id` | retired |
| `account_cargo_items.item_kind` | retired |
| `account_cargo_items.stack_count` | retired |
| `account_cargo_items.instance_value` | retired |
| `account_cargo_items.durability` | retired |
| `account_cargo_items.seal_flag` | retired |
| `account_cargo_items.option_value` | retired |
| `account_cargo_items.expire_time` | retired |
| `account_cargo_items.marker_16` | retired |
| `account_cargo_items.extra_json` | retired |
| `account_cargo_items.created_at` | retired |
| `account_cargo_items.updated_at` | retired |
| `characters.character_id` | `characters.character_id` |
| `characters.account_id` | `characters.account_id` |
| `characters.name` | `characters.name` |
| `characters.name_bytes` | `characters.name_bytes` |
| `characters.job` | `characters.job` |
| `characters.grow_type` | `characters.grow_type` |
| `characters.level` | `characters.level` |
| `characters.clone_title_item_id` | `characters.clone_title_item_id` |
| `characters.slot_index` | `characters.slot_index` |
| `characters.delete_flag` | `characters.delete_flag` |
| `characters.town_id` | `characters.town_id` |
| `characters.area_id` | `characters.area_id` |
| `characters.pos_x` | `characters.pos_x` |
| `characters.pos_y` | `characters.pos_y` |
| `characters.direction` | `characters.direction` |
| `characters.area_state` | `characters.area_state` |
| `characters.appearance_blob` | `characters.appearance_blob` |
| `characters.bonus_sp` | `characters.bonus_sp` |
| `characters.bonus_tp` | `characters.bonus_tp` |
| `characters.exp` | `characters.exp` |
| `characters.ex_equip_slot_stat` | `characters.ex_equip_slot_stat` |
| `characters.pvp_grade` | `character_pvp.pvp_grade` |
| `characters.pvp_rating_grade` | `character_pvp.pvp_rating_grade` |
| `characters.user_state` | `characters.user_state` |
| `character_subtype0_fields.character_id` | `character_condition.character_id` |
| `character_subtype0_fields.stamina` | `character_condition.stamina` |
| `character_subtype0_fields.fatigue_penalty` | `character_condition.fatigue_penalty` |
| `character_subtype0_fields.is_event_character` | `character_condition.is_event_character` |
| `character_subtype0_fields.is_private_store` | `character_condition.is_private_store` |
| `character_subtype0_fields.expert_job_type` | `character_expert_job.expert_job_type` |
| `character_subtype0_fields.expert_job_exp` | `character_expert_job.expert_job_exp` |
| `character_subtype0_fields.is_hardcore_mode` | `character_condition.is_hardcore_mode` |
| `character_subtype0_fields.is_hardcore_dead` | `character_condition.is_hardcore_dead` |
| `character_subtype0_fields.hardcore_death_count` | `character_condition.hardcore_death_count` |
| `character_subtype0_fields.user_state_bits` | `character_settings.user_state_bits` |
| `character_subtype0_fields.chat_ban_end_time` | `character_condition.chat_ban_end_time` |
| `character_subtype0_fields.mood_value` | `character_condition.mood_value` |
| `character_subtype0_fields.pvp_rank_point` | `character_pvp.pvp_rank_point` |
| `character_subtype0_fields.creature_field1` | `character_condition.creature_field1` |
| `character_subtype0_fields.creature_field2` | `character_condition.creature_field2` |
| `character_subtype0_fields.creature_field3` | `character_condition.creature_field3` |
| `character_subtype0_fields.creature_field4` | `character_condition.creature_field4` |
| `character_subtype0_fields.creature_buffer` | `character_condition.creature_buffer` |
| `character_subtype0_fields.pc_room_id` | `character_condition.pc_room_id` |
| `character_subtype0_fields.is_premium_pc_room` | `character_condition.is_premium_pc_room` |
| `character_subtype0_fields.server_group_id` | `character_condition.server_group_id` |
| `character_subtype0_fields.black_count` | `character_condition.black_count` |
| `character_subtype0_fields.guild_level` | `characters.guild_level` |
| `character_subtype0_fields.chaos_point` | `character_condition.chaos_point` |
| `character_subtype0_fields.disguise_kind` | `character_condition.disguise_kind` |
| `character_subtype0_fields.is_disguised` | `character_condition.is_disguised` |
| `character_subtype0_fields.fatigue_update` | `character_condition.fatigue_update` |
| `character_subtype0_fields.return_user_flag` | `character_condition.return_user_flag` |
| `character_subtype0_fields.channel_display_mode` | `character_condition.channel_display_mode` |
| `character_subtype0_fields.channel_type` | `character_condition.channel_type` |
| `character_subtype0_fields.is_return_user` | `character_condition.is_return_user` |
| `character_subtype0_fields.link_slot_enabled` | `character_condition.link_slot_enabled` |
| `character_subtype0_fields.link_type_a` | `character_condition.link_type_a` |
| `character_subtype0_fields.link_type_b` | `character_condition.link_type_b` |
| `character_subtype0_fields.emotion_index` | `character_condition.emotion_index` |
| `character_subtype0_fields.action_byte` | `character_condition.action_byte` |
| `character_subtype0_fields.fatigue_display_update` | `character_condition.fatigue_display_update` |
| `character_subtype0_fields.costume_flag` | `character_condition.costume_flag` |
| `character_subtype0_fields.aura_flag` | `character_condition.aura_flag` |
| `character_subtype0_fields.pet_display_flag` | `character_condition.pet_display_flag` |
| `character_subtype0_fields.title_display_flag` | `character_condition.title_display_flag` |
| `character_subtype0_fields.pvp_stat_a` | `character_pvp.pvp_stat_a` |
| `character_subtype0_fields.pvp_win_streak` | `character_pvp.pvp_win_streak` |
| `character_subtype0_fields.pvp_lose_streak` | `character_pvp.pvp_lose_streak` |
| `character_subtype0_fields.trailing_byte` | `character_condition.trailing_byte` |
| `character_subtype1_fields.character_id` | `character_stats.character_id` |
| `character_subtype1_fields.stat_hp_max` | `character_stats.stat_hp_max` |
| `character_subtype1_fields.stat_mp_max` | `character_stats.stat_mp_max` |
| `character_subtype1_fields.stat_physical_attack` | `character_stats.stat_physical_attack` |
| `character_subtype1_fields.stat_physical_defense` | `character_stats.stat_physical_defense` |
| `character_subtype1_fields.stat_magical_attack` | `character_stats.stat_magical_attack` |
| `character_subtype1_fields.stat_magical_defense` | `character_stats.stat_magical_defense` |
| `character_subtype1_fields.stat_move_speed` | `character_stats.stat_move_speed` |
| `character_subtype1_fields.stat_attack_speed` | `character_stats.stat_attack_speed` |
| `character_subtype1_fields.stat_cast_speed` | `character_stats.stat_cast_speed` |
| `character_subtype1_fields.stat_inventory_limit` | `character_stats.stat_inventory_limit` |
| `character_subtype1_fields.skill_tree_index` | `characters.skill_tree_index` |
| `character_subtype1_fields.progress1` | `character_stats.progress1` |
| `character_subtype1_fields.progress2` | `character_stats.progress2` |
| `character_subtype1_fields.stat_fire_resistance` | `character_stats.stat_fire_resistance` |
| `character_subtype1_fields.stat_water_resistance` | `character_stats.stat_water_resistance` |
| `character_subtype1_fields.stat_dark_resistance` | `character_stats.stat_dark_resistance` |
| `character_subtype1_fields.stat_light_resistance` | `character_stats.stat_light_resistance` |
| `character_subtype1_fields.stat_hp_regen_speed` | `character_stats.stat_hp_regen_speed` |
| `character_subtype1_fields.stat_mp_regen_speed` | `character_stats.stat_mp_regen_speed` |
| `character_subtype1_fields.stat_hit_recovery` | `character_stats.stat_hit_recovery` |
| `character_subtype1_fields.stat_jump_power` | `character_stats.stat_jump_power` |
| `character_subtype1_fields.stat_weight` | `character_stats.stat_weight` |
| `character_subtype1_fields.current_hp_mp_percent` | `character_stats.current_hp_mp_percent` |
| `character_subtype1_fields.name_tag_item_id` | `characters.name_tag_item_id` |
| `character_subtype1_fields.name_tag_expire_time` | `characters.name_tag_expire_time` |
| `character_subtype1_fields.equipped_creature_level` | `character_stats.equipped_creature_level` |
| `character_subtype1_fields.equip_list_trailing` | `character_stats.equip_list_trailing` |
| `character_subtype1_fields.flag_byte` | `character_stats.flag_byte` |
| `character_subtype1_fields.guild_power_war` | `characters.guild_power_war` |
| `character_subtype1_fields.server_timestamp` | `character_stats.server_timestamp` |
| `character_subtype1_fields.quest_shop_count` | `character_stats.quest_shop_count` |
| `character_subtype1_fields.manage_level` | `characters.manage_level` |
| `character_init_flags.character_id` | `character_condition.character_id` |
| `character_init_flags.champion_break_key_id` | `character_condition.champion_break_key_id` |
| `character_init_flags.champion_break_mode` | `character_condition.champion_break_mode` |
| `character_init_flags.champion_break_value` | `character_condition.champion_break_value` |
| `character_init_flags.character_option_blob` | `character_settings.character_option_blob` |
| `character_init_flags.pc_room_state` | `character_condition.pc_room_state` |
| `character_gold_limits.character_id` | `character_stats.character_id` |
| `character_gold_limits.gold_carry_limit` | `character_stats.gold_carry_limit` |
| `character_gold_limits.auction_gold_limit` | `character_stats.auction_gold_limit` |
| `character_gold_limits.updated_at` | retired |
| `character_crystal_contract.character_id` | `character_condition.character_id` |
| `character_crystal_contract.cube_type` | `character_condition.crystal_cube_type` |
| `character_crystal_contract.cube_grade` | `character_condition.crystal_cube_grade` |
| `character_dimension_flags.character_id` | `character_condition.character_id` |
| `character_dimension_flags.flag1` | `character_condition.dimension_flag1` |
| `character_dimension_flags.flag2` | `character_condition.dimension_flag2` |
| `character_dimension_flags.flag3` | `character_condition.dimension_flag3` |
| `character_dimension_flags.flag4` | `character_condition.dimension_flag4` |
| `character_expert_job.character_id` | `character_expert_job.character_id` |
| `character_expert_job.giveup_count` | `character_expert_job.giveup_count` |
| `character_expert_job.disjoint_machine_grade` | `character_expert_job.disjoint_machine_grade` |
| `character_expert_job.disjoint_machine_endurance` | `character_expert_job.disjoint_machine_endurance` |
| `character_expert_job.enchanter_endurance` | `character_expert_job.enchanter_endurance` |
| `character_pvp_skill_state.character_id` | retired |
| `character_pvp_skill_state.initialized_at` | retired |
| `character_pvp_skill_state.updated_at` | retired |
| `character_pvp_skills.character_id` | `character_skills.character_id` |
| `character_pvp_skills.page_index` | `character_skills.page_index` |
| `character_pvp_skills.slot` | `character_skills.slot` |
| `character_pvp_skills.skill_id` | `character_skills.skill_id` |
| `character_pvp_skills.level` | `character_skills.level` |
| `character_pvp_skills.extra_values` | `character_skills.extra_values` |
| `character_skills.character_id` | `character_skills.character_id` |
| `character_skills.page_index` | `character_skills.page_index` |
| `character_skills.slot` | `character_skills.slot` |
| `character_skills.skill_id` | `character_skills.skill_id` |
| `character_skills.level` | `character_skills.level` |
| `character_skills.extra_values` | `character_skills.extra_values` |
| `character_daily_challenge_groups.character_id` | `character_daily_challenges.character_id` |
| `character_daily_challenge_groups.group_index` | `character_daily_challenges.group_index` |
| `character_daily_challenge_groups.group_id` | `character_daily_challenges.group_id` |
| `character_daily_challenge_claims.character_id` | `character_daily_challenges.character_id` |
| `character_daily_challenge_claims.group_index` | `character_daily_challenges.group_index` |
| `character_daily_challenge_claims.claimed_at` | `character_daily_challenges.claimed_at` |
| `character_invisible_falgs.character_id` | `character_invisible_flags.character_id` |
| `character_invisible_falgs.slot_index` | `character_invisible_flags.slot_index` |
| `character_invisible_falgs.flag_value` | `character_invisible_flags.flag_value` |
| `character_item_values.character_id` | `character_item_cooldowns.character_id` / `character_item_effects.character_id` |
| `character_item_values.list_kind` | retired |
| `character_item_values.sort_order` | `character_item_cooldowns.sort_order` / `character_item_effects.sort_order` |
| `character_item_values.item_id` | `character_item_cooldowns.item_id` / `character_item_effects.item_id` |
| `character_item_values.value` | `character_item_cooldowns.value` / `character_item_effects.value` |
