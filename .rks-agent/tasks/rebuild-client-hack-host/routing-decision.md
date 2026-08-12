# Routing decision

## Readiness and value

The outcome is observable: capture repeatable pre-mutation runtime state from an isolated copy, enable only validated behavior, and prove the copy starts with all required plugins initialized. Direct execution could easily confuse already-patched bytes with originals or lose the boundary between source and lab clients. A durable Spec, ordered capture/implementation Plan, independent complete-change review, and fresh runtime verification materially reduce that risk and justify their time.

## Structural route

This remains one coherent acceptance boundary because capture, generated signatures, plugin enablement, and startup verification are not independently useful deliverables. Route: Single Spec.

## Controller selection

Use SDD Standard with the code-feature Profile. Lite would not preserve the dependency ordering from clean capture through implementation and repeated runtime verification. Governed adds challenge/drift controls beyond the current local isolated-copy risk.

## Reroute conditions

Reroute only if runtime evidence proves the supported executable cannot reach the unpacked capture point in an isolated copy, or if completing network behavior requires an independently acceptable server/protocol change outside this contract.
