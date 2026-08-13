# Routing decision

## Readiness and value

The outcome is observable: a buildable Host whose IAT policy accepts the supported client across system ASLR relocations, proven by CTest with relocated bases and by an isolated lab launch into the game window. Direct execution risks reintroducing a latent fail-closed regression by confusing "matches on this boot" with "matches on identity," and the prior task already showed this exact code path produced a changes_required review. A durable Spec, ordered Plan, independent complete-change review, and fresh runtime verification materially reduce that risk and justify their time.

## Direct failure mode avoided

Under Direct, the absolute-address table could be incompletely removed, a test could still assert against same-boot addresses while the production path rejects on the next boot, or the msvcp60 stable region could be wrongly collapsed with system-DLL semantics. Spec + complete-result review + relocated-base CTest materially catches each.

## Structural route

One coherent acceptance boundary: the IAT policy, its tests, and the lab launch are not independently useful deliverables. Route: Single Spec.

## Controller selection

Use SDD Standard with the code-feature Profile. Lite would not preserve the ordering from policy change through relocated-base test coverage to fresh lab verification. Governed adds challenge/drift controls beyond this bounded local remediation.

## Why adjacent routes are wrong

- Lite: no plan durability for the test+lab ordering; risks shipping a same-boot-only fix.
- Multi Spec: only one acceptance boundary; splitting test-coverage from lab-launch would create non-independently-useful child specs.

## Reroute conditions

Reroute only if runtime evidence shows the supported executable cannot reach the unpacked readiness point in an isolated copy after the policy change, or if normalization proves to require accepting an unsigned slot the prior capture treated as evidence-backed (which would reopen the capture boundary and force a return to intent alignment).
