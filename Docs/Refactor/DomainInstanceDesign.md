# Domain Instance Design

How business logic is written above the persistence layer: which objects exist, which of them holds
a rule, and where a use case gets them from.

This is a companion to [Table Ownership Ruling](TableOwnershipRuling.md) and applies the same cut
one layer up. That document decided which concepts the database has; this one decides which objects
those concepts become in code. Where the two would disagree, the table ruling wins — the object
boundaries follow the concept boundaries, not the other way round.

This is a design ruling, not a plan. It says what the shape is and what each rule rests on, so that
the work which follows implements a settled decision instead of deciding as it goes. Line numbers
are from commit `531c0bb`.

## The scale this is designed for

A hundred to two hundred players on one server, one local SQLite database in WAL mode, read-heavy
and write-light, and the heaviest read path — assembling the select-character packet — running once
per player per entry rather than per tick.

At this scale **performance is not the tiebreaker**, and no rule below is justified by it. Measured
on this machine against a database shaped like the current one — 43 tables, 200 characters, 8 rows
per table each, 4.6 MB — one indexed point query costs 8.6 μs, a whole select-character projection
of 43 per-character queries costs **0.9 ms**, and the same work amplified tenfold still costs under
10 ms. Choose by layering, not by speed.

This is a premise, not a law. If it stops holding — a different database, a few thousand players, a
read path that runs every tick — the rules below have to be re-judged against the new premise. That
would be the premise expiring, not the rules having been wrong.

## Two shapes already coexist

Creating a character and moving an item are written in two different ways today.

**Creation.** `CreateCharacterCommandHandler`
(`Server/src/Dfo.Server.Protocol/Game/CreateCharacter/CreateCharacterSlice.cs:71`) is a single
forwarding call. Every rule — job id bound, name shape, slot limit, duplicate name, lowest free
slot, starting stats, starting skill page — lives in `CharacterDomainService.CreateCharacterAsync`
(`Server/src/Dfo.Server.Application/Characters/CharacterDomainService.cs:97`). The handler knows
none of them. It receives a `CharacterCreationStatus` and translates it into a client code.

**Moving an item.** `MoveItemSpaceCommandHandler`
(`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:223`) holds its rules
itself, in `PlanAsync` at line 313: placement lookup, slot band, stack limit, the refusal of a
partial swap, and the displaced item's own band. That method sits in `Dfo.Server.Protocol`.

The second shape has already cost something measurable. The three-way `ItemArrival` switch that
decides where an arriving item lands is written twice — once in
`Server/src/Dfo.Server.Protocol/Game/TaggedIdReport/GrantTutorialRewardCommand.cs:70` and once in
`Server/src/Dfo.Server.Application/Inventory/DailyRefillService.cs:100`. Two entry points for one
rule, each assembling it again, because no object owns "something arrived, put it away". Drops,
shop purchases, mail collection and quest rewards would each assemble a third and a fourth.

The ruling below is that the first shape is the shape.

## Three kinds of object

**Repository classes** answer *which one*. They find, create, delete and list, they own operations
that span two instances, and they hand back two kinds of product:

- a **snapshot** — a set of records, for reading out; and
- an **instance** — for doing something.

They hold no rule beyond what answering those questions needs.

**Instance classes** answer *what can I do*. One instance is one thing in the game — this character,
this bag. Its methods are the vocabulary of the domain: level up, change job, equip, move, add. It
calls rule services to decide whether an operation is allowed and then carries it out. Its caller
never learns what the rules were.

**Rule services** answer *is this allowed* and *what does the archive say*. They are injected,
stateless, and shared across domains — which is the point of keeping them separate rather than
folding them into the instances that call them.

A use case is then: ask the repository for what it needs — a snapshot to read out, an instance to
act on — do the one thing, translate the result. Nothing else.

### Snapshots and instances

A snapshot is a set of records taken at one moment. An instance is a thing you operate. A repository
returns whichever the caller needs and both are first-class: initialization reads a snapshot,
business acts through an instance.

The split is not new here. `IItemContainer` already has both halves inside one object:
`SnapshotAsync` (`IItemContainer.cs:119`) is "for projection to the client", while `ReadAllAsync`
(`:131`) is the container's own contents as the running use case has left them. What this document
does is promote that split from inside the bag up to the repository, and give the character the same
two products the bag already has.

### What exists today

| Kind | Concept | Today |
|---|---|---|
| Repository | character | `ICharacterDomainService` (`Server/src/Dfo.Server.Abstractions/Characters/ICharacterServices.cs:31`) — roster, load, check name, create, delete |
| Repository | bag | `ICharacterContainerDomainService` (`Server/src/Dfo.Server.Application/Inventory/CharacterContainerDomainService.cs:29-41`) — which bags exist, which is bound, switch the binding, get the bound one |
| Instance | bag | `IItemContainer`; one implementation, `PersistentItemContainer` (`ItemContainers.cs:286`) |
| Snapshot | bag | `IItemContainer.SnapshotAsync` (`IItemContainer.cs:119`) |
| Snapshot | character | `ActiveCharacterState` from `LoadForSelectionAsync` and `LoadByRosterPositionAsync`; `AccountCharacterRoster` from `GetAccountRosterAsync` — exists, but packet-shaped; see R-5 |
| Instance | character | **missing** — the repository returns snapshots today and nothing you can act on |
| Rule | slot bands | `IItemSlotRuleService.CanOccupyMainSlot(placement, slotIndex)` (`ItemSlotRuleService.cs:31`) |
| Rule | arrival | `IItemArrivalRuleService` |
| Rule | archive content | `IItemPlacementDefinitionSource`, `IInitialSkillDefinitionSource`, and the rest of the PVF sources |

The bag repository already matches this design method for method, and the character repository
already matches it on the snapshot side. Two pieces are missing: the character instance — which is
why "the character levels up" cannot be written today, and why every character use case instead
takes a `CharacterId` and a pile of services — and a character snapshot cut the way the domain is
cut rather than the way the packet is.

## The rules

### R-1 — An instance holds identity and dependencies, not state

Every read goes to the store. An instance caches nothing it could be asked for again.

*Why.* A command runs inside one transaction (`DomainTransactionScope`). When that transaction rolls
back, an instance that cached its own state is now ahead of the database and nothing reports it.
Holding no state makes rollback correct without anyone having to maintain it.

*Rests on.* `StoreBackedItemContainer` (`ItemContainers.cs:181`) already works this way, and it is
why the move use case no longer has a lost-race refusal to report.

### R-2 — An operation spanning two instances belongs to the repository class

`bag.MoveTo(otherBag)` is not written. `bags.MoveBetween(from, to, …)` is.

*Why.* Neither instance owns the outcome, so neither can answer for a failure halfway through.
Placing the operation above both gives that failure one owner.

*Not hypothetical.* `MOVE_ITEMSPACE` already carries both `SourceListType` and
`DestinationListType`; both are currently refused with `ListNotServed` because only the main list is
served. Bag-to-cargo is the next feature on this very request, and trade, mail and party each arrive
the same way.

### R-3 — The character instance is an entry point, not a container

It holds what belongs to the character's own body — level, job, position, operational state.
Everything else is reached *through* it and lives in its own instance: `character.Skills`,
`character.Quests`, `character.Bags`.

*Why.* "Take a quest, equip, level up, change job, change outfit, use an item, speak, join a party"
on one class is a two-thousand-line object within a year.

*Where the seams go.* The same places the tables were cut. Those concepts are already settled in the
table ruling; a second set of boundaries here would mean two answers to one question.

### R-4 — Instances are made when a character is selected, not when a command arrives

*Why.* A repository that constructs instances has to hold every rule service those instances need —
for the character, an estimated eight to ten. That cost is acceptable once per selection and
wasteful once per command.

*Rests on.* This is already how bags are made:
`Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs:61`
constructs the container and registers it at selection time. The character instance goes in the same
place at the same moment.

### R-5 — A repository returns snapshots as well as instances, and a snapshot never decides anything

Initialization — selecting a character, sending the roster — asks the repository for a **snapshot**:
a set of domain records, which travels back through the command and is turned into the packet's
segments by the handler. Business asks the same repository for an **instance**.

**A snapshot is read out, never judged on.** Deciding from a snapshot and then acting through an
instance is a check against one moment and a write against another, with nothing in between to
notice the difference. Every decision reads through the instance, which under R-1 holds nothing and
therefore sees the present.

*A snapshot is a set of domain records, not one packet-shaped record.* This is the half of the rule
that carries it. `ActiveCharacterState` today is one large record cut the way the packet is cut —
its own comments call its halves "the subtype-0 record" and "the subtype-1 record"
(`CharacterEntryAssembly.cs:104` and `:130`), and subtype-0 and subtype-1 are the names of protocol
segments. A snapshot under this design is instead the same concepts the tables were cut into — body,
derived numbers, operational state, items, skills — and choosing, ordering and packing them into
segments is the handler's work, next to the encoder. The domain says what a character is; the
protocol says how it is laid out in a packet.

*Why the projection is not routed through instance calls instead.* It would work, and the premise
above says it would be fast enough. But the instance would grow methods existing only to feed a
packet, which is the packet-shaped cut this project spent a refactor removing from the tables.
Making the snapshot its own product avoids that without an exception to the architecture.

### R-6 — A rule service resolves no identity and reads no database

It takes the data it judges and returns a verdict.

*Mechanical check.* **A rule service whose signature contains `CharacterId` or `AccountId` has not
been separated.** It is performing a lookup as well as a judgement.

`IItemSlotRuleService.CanOccupyMainSlot(placement, slotIndex)` passes.
`IItemUsabilityDomainService.CanUseAsync(characterId, itemId, ct)`
(`ItemUsabilityDomainService.cs:28`) does not: it resolves the character at line 71 — session
directory, then runtime directory — before it judges anything. Under this design it becomes
`character.CanUse(item)`: the instance supplies the identity, the rule service judges, and what is
left of the service is pure.

### R-7 — A verb makes what it decided readable before it returns

A store holds a use case's writes until that use case settles, and shows them meanwhile only to
reads that go through the same session. Anything else in the use case — the entry loader reading the
same rows through the tables, or the container's own snapshot, which is taken from that loader
because the client is shown facts the store's vocabulary does not name — is shown the state before
the writes.

So a verb applies what it decided before answering. Once per verb rather than once per write, so a
move is still one batch. Nothing outside the use case sees anything sooner and a rollback still
takes it all back: this is about who can read inside one use case, not about isolation.

*Why it is a rule and not an implementation detail.* Before the verbs existed, the one caller that
wrote and then read this way wrote through the tables, which reach the database at once — so the
difference had no way to show itself, and no test had a reason to pin it. Every verb added from here
inherits the same trap, and the shape that hides it is ordinary: a test that puts a use case around
each call separately makes the two write channels indistinguishable.

*What this rule does not yet cover.* Only the verbs apply what they decided. A caller that composes
its own sequence out of the bare reads and writes does not, and one such caller exists: consuming an
item on use is a compound action in every way except that it is not a verb —
`ItemContainerOperations.ConsumeAsync`, driven from `ItemUseEffectDomainService`. Nothing reads it
back through the tables in the same use case today, so it is a trap rather than a defect; making it
a verb is the obvious fix and belongs with the outcome that takes item use apart under R-6.

## What does not move

**Client error codes stay in the protocol.** An instance says why an operation was refused in the
domain's own words; the protocol decides which sentence the player is shown.
`MoveItemSpaceHandler.ErrorCodeFor` (`MoveItemSpaceSlice.cs:490`) is that translation and stays
where it is. `UseStackableCommandHandler` already splits it this way, and that split is correct.

**No parallel `canX` query beside the `doX` method.** The doing method returns its refusal; that is
the answer. Two entry points to one rule are two copies of it, and they drift without anything going
red. `CanUseAsync` may keep existing where a caller genuinely needs the verdict *before* deciding to
act; move and add have no such caller.

**The persistence shape.** Forty-three tables, one persistence class naming one table, cross-table
assembly in the domain layer. Nothing here changes any of it. In particular an instance is not given
a database of its own: it reaches persistence through the same per-table classes and the same
ambient transaction as everything else. An instance that named several tables would undo the shape
the table ruling just established.

**`ItemContainerOperations` stays rule-free.** Its four operations say a move as the clears and
writes it is made of, and decide nothing. What changes is only *who* decides before calling them:
the protocol handler before, the bag after.

Two remarks said otherwise and both had to be rewritten, not one. `ItemContainers.cs` said those
questions "are the protocol's questions and are answered before any of this is called". The
`IItemContainer` interface itself went further: "reads and writes are all there is … the
putting-together belongs above", on the grounds that a container offering a move would drag the
store into knowing what a move is. That second reason was already untrue when it was written —
`ItemContainerOperations` composes a move without the store learning anything — and it is the
better argument for the change rather than against it. Both now say where the judgement lives and
why the store was never involved.

## Settled by the item-side implementation

The first outcome (`spec-001`, item rules in the bag) put steps 1 and 2 below into the code. Four
things it settled that this document had left open or said imprecisely:

**Putting an item away is two verbs, not one.** This document spoke of `bag.addItem(item)` as a
single thing. It is two, and neither can stand in for the other: an ordinary grant fills piles only
to the item's stack limit and opens a slot for what will not fit, while a daily refill joins
whatever pile is there however large, because its quantity was already bounded by the rule that
produced it. What the two share — and what was genuinely written twice before — is not the rule but
the *landing*: turning a decided arrival into the write it calls for. That is the one place now.

**A refill's band and item kind arrive as data.** They come from the daily-refill rule, not from
the item's placement, so the bag is told them rather than looking them up. Passing data is not
passing a rule: which slot of that band, and which pile, is still the bag's decision.

**The verbs answer; they do not throw.** "There was no room" and "the archive declares no such item"
are ordinary answers, and the two callers treat them oppositely — the tutorial reward leaves the
item out of its acknowledgement, the daily refill fails the whole entry. Putting the reaction in the
callers is what let both keep their own behaviour while sharing one landing.

**Writing by slot instead of by row changes a surrogate key, and nothing outward depends on it.**
The refill used to update the row it had found by its `item_uid`; it now writes to a slot, and the
store deliberately hands out no identity for an item. The key therefore moves. It reaches no frame —
in `Dfo.Server.Protocol` it appears only inside exception message strings — and although it is a
secondary sort key in `InventoryEntryServices`, the primary key there is the slot, which is unique
within a container. `DailyRefill_LeavesTheProjectionAloneEvenIfTheRowsSurrogateKeyMoves` pins all of
that, including the uniqueness the argument rests on.

**A verb applies what it decided before it returns.** This one was not foreseen and is the most
generalizable thing the outcome produced, so it is written up as R-7 below rather than only here.
The refill used to write through the tables, which reach the database at once; through the bag the
writes are held until the use case settles, and the entry reload — which reads the same rows through
the tables — was handed the inventory as it stood before. On a top-up day the client was shown stale
counts; on a day that opened a slot the reload could not find the row it had just been told about
and took the whole entry down with it, rolling the counter back so the next entry failed the same
way. The full suite stayed green, because every refill test drove the refill with one transaction
per call, a shape under which the two write channels are indistinguishable.

**Opening a slot without an item kind is now the bag's refusal, not the refill's.** The check moved
with the landing. Its type (`InvalidDataException`), its timing (before any write) and its effect
(the entry fails) are unchanged; the message text is the bag's. Production cannot reach it — a
placement the archive returns always carries a kind of at least one — so the reachable surface is
unchanged too, and it is now stated once for every caller instead of once for the refill.

R-2 was not exercised: a move within one bag spans no second instance. It stays a ruling about work
that has not started.

## What this implies, in order

1. **`bag.AddAsync(itemId, count)`.** Collapses the duplicated `ItemArrival` switch into one place
   and gives drops, purchases, mail and quest rewards something to call. Highest certainty of
   benefit: the duplication already exists and is measurable.
2. **`bag.MoveToAsync(…)`.** Moves `PlanAsync` out of `Dfo.Server.Protocol` into the bag; the
   handler keeps only `ErrorCodeFor`.
3. **The character instance.** The missing piece, made at selection alongside the bag.
4. **Splitting `ItemUsabilityDomainService`** along R-6, once there is a character instance to hold
   the identity.
5. **Reshaping the character snapshot** from one packet-shaped record into a set of domain records,
   and moving the segment layout to the handler. The largest single piece: `CharacterEntryAssembly`
   is 624 lines, `InventoryEntryAssembly` 578, and the three whole-record characterization tests pin
   exactly what they produce today.

Steps 1 and 2 do not depend on step 3 and can be done first. Step 5 depends on none of the others
and should be its own piece of work rather than folded into one of them.

## What this rests on, and what is still open

Half of this design is already running, and that is the evidence for the rest of it: both repository
classes exist with the methods described above, the bag instance exists and holds its own
persistence, and the create use case is already written in the target shape. What is being decided
is that everything else follows it.

One justification has already been withdrawn. An earlier draft of R-5 kept the projection away from
instances on the grounds that scattered reads would be too slow. Measured, they are not: the figures
in the premise section are what a whole select-character projection actually costs, and they are two
to three orders of magnitude short of mattering at this scale. The rule survives, but on the
layering argument alone. That measurement was taken with Python's `sqlite3` against a synthetic
database of the current shape rather than through `Microsoft.Data.Sqlite` against the real schema —
close enough to settle a question of this margin, not close enough to quote as a benchmark.

Adding rule services to the bag costs nothing in polymorphism today. `StoreBackedItemContainer` was
written as an abstraction over two stores, and its remark (`ItemContainers.cs:177`) still says "both
of the ones below" — but the transient one was retired and one concrete subclass remains. That is a
stale comment rather than a second implementation. Worth correcting when the file is next touched,
and worth knowing before assuming the abstraction is load-bearing.

Three things this document deliberately does not decide:

- **Whether the character instance is also the runtime session state.**
  `ISelectedSessionRuntimeDirectory` already holds a per-selection in-memory `ActiveCharacterState`,
  and `ItemUsabilityDomainService` reads the character out of it rather than out of the database.
  R-1 says an instance holds no state; that directory plainly does. Whether the two merge, and which
  is authoritative if they do, is the first question the character instance has to answer. It should
  be answered where it can be judged against the packet path, not assumed here.
- **How many rule services the character repository ends up holding.** Eight to ten is an estimate
  from the services today's character use cases name, not a count.
- **What becomes of `ItemUseEffectDomainService.ApplyAsync`**, which takes both a `CharacterId` and
  an `IItemContainer`. It spans two instances, so R-2 points at a repository — but its two operands
  are a character and that same character's bag, which may make it an instance method after all.
  Decide it when step 3 lands, with the character instance actually in front of you.
