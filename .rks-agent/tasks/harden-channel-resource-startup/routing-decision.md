# Routing Decision

- TaskContract: `.rks-agent/tasks/harden-channel-resource-startup/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The observed late `FileNotFoundException`, packaged resource,
  and accepted permanent outcome identify one stable startup/resource boundary.
- Requirement stability: Working-directory independence, startup failure for missing required
  resources, the complete Channel handshake, diagnostics, and runbook correction are explicit.
- Execution direction: Normalize Host root/config loading, validate and load the packaged
  resource before readiness, and protect it with production-shaped tests.
- Remaining uncertainty: Only local implementation shape and test harness details remain.

## Marginal Control Value

- User precision signal: The user explicitly called out that this is the second escaped
  initialization failure and accepted a permanent multi-layer fix.
- Direct failure mode: A narrow path fix could again omit startup timing, publish layout,
  arbitrary working directories, or the full TCP flow while focused tests remain green.
- Useful control: A durable Spec plus independent complete-result review checks every layer
  before fresh full verification.
- Time-for-quality rationale: The added review cost is small relative to another real-client
  test cycle ending at a late missing-resource error.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: Host root, channel resource, startup validation, tests, diagnostics and
  documentation form one indivisible acceptance outcome and rollback unit.
- Why Direct is insufficient: It lacks a durable completeness boundary for the several
  mutually reinforcing safeguards the user explicitly requested.
- Why Multi is unnecessary: No component has an independently useful or acceptable outcome;
  all parts must converge before the server is considered fixed.
- Reroute conditions: Evidence of a material deployment model decision or separate resource
  ownership requiring independent acceptance would require Standard or Multi Spec.

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-bugfix`
- Why this is sufficient: The failure and desired behavior are clear; behavior-first
  implementation, complete-result review and fresh verification provide the useful controls.
- Why lighter is insufficient: Direct execution does not durably protect all seven success
  conditions against omission.
- Why heavier is unnecessary: No consequential unresolved design sequence needs a plan and no
  safety, authorization, data-loss, or audit hard floor requires Governed controls.
