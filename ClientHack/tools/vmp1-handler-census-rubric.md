# `.vmp1` handler semantics — judgment protocol

You are recovering the instruction set of a VMProtect-style virtual machine
embedded in a 32-bit Windows game client. Each unit is one VM handler: a short
x86 fragment that implements one virtual operation and then dispatches to the
next. Your job is to say **what virtual operation it implements**.

## The machine, as measured

Everything here was measured against the live client, not assumed.

* The VM is **direct-threaded**. There is no dispatch loop and no handler table:
  the bytecode stream stores handler pointers, and each handler jumps to the
  next one itself.
* **`esi` is the virtual instruction pointer (VIP).** The stream layout is
  `[next-handler pointer : 4][this handler's operand : 0..n]`, so the pointer
  for step *k+1* sits ahead of step *k*'s operand.
* **`edi` is the VM context pointer.** `[edi]` holds the image delta (added to
  every decoded handler pointer). The **virtual register file is
  `[edi + idx*4]`** — you will see `shl eax,2` then `add eax,edi` a lot; that is
  a virtual register index being turned into its slot address.
* The **VM stack is the native stack**: handlers `push`/`pop` directly, so a
  `pop dword ptr [eax]` with `eax = edi + idx*4` is "pop the VM stack into
  virtual register idx".
* Dispatch tail, in one of two styles. Either
  `lodsd` (load next pointer, VIP += 4) then `add eax,[edi]`, add the handler's
  **own** additive bias, `jmp eax`; or `mov ebx,[esi]` / `lea esi,[esi+4]`,
  add the bias, stage it on the stack and leave through `ret imm`. **Both are
  dispatch, not control flow you need to explain.**
* The per-handler bias and the `call`-used-as-`jmp` trampolines
  (`call X` where X does `lea esp,[esp+4]`) are **obfuscation**. They carry no
  semantics. Ignore them when naming the operation.

## What each unit gives you

* the collapsed trace, every instruction with its verbatim bytes;
* `operand bytes` — how wide this handler's inline operand is (0, 1, 2 or 4);
* `dispatch refs` — how often the walk over 961 real VM functions reached it;
* the symbolic register effect and memory writes, with `VIP` and `CTX` naming
  the entry values of `esi` and `edi`.

**Caveat on the symbolic state:** instructions listed as "not modelled"
(commonly `lodsd`) did execute but their effect is absent from that summary.
`lodsd` loads `[esi]` into `eax` and adds 4 to `esi` — so a VIP advance shown as
`+1` in the register effect is really `+5`. **The trace is the ground truth;
the symbolic state is a convenience.**

## How to name the operation

Work from what the handler does to the three state components — virtual
registers `[CTX + idx*4]`, the native stack (the VM stack), and the VIP —
and ignore everything that only serves dispatch or obfuscation.

Use these names where they fit, and invent a clear one in the same style when
they do not:

```
VM_PUSH_REG idx        push [CTX + idx*4]
VM_POP_REG idx         pop into [CTX + idx*4]
VM_PUSH_IMM imm        push an inline constant from the bytecode
VM_ADD / VM_SUB / VM_AND / VM_OR / VM_XOR / VM_NOR / VM_SHL / VM_SHR ...
                       pop one or two operands, compute, push the result
                       (VMProtect usually also pushes the resulting EFLAGS)
VM_LOAD  width         pop an address, push [address]
VM_STORE width         pop an address and a value, store
VM_JMP / VM_JCC        set the VIP from the stack (a VM branch)
VM_CALL_NATIVE         transfer to native code
VM_EXIT                restore the saved registers and leave the VM
VM_NOP                 no state change beyond dispatch
```

State the operand meaning explicitly when there is one — a 1-byte operand is
almost always a virtual register index (watch for the `shl 2`), a 4-byte one is
usually an immediate or an address.

**Name the operation, not its width.** Encoding detail belongs in `operand` and
`stack_effect`, not in the name: a handler that pushes a 16-bit immediate is
`VM_PUSH_IMM` with `operand` saying "2-byte immediate, pushed as a word via
`66 50`", not `VM_PUSH_IMM16`. Widths are how the same operation is encoded;
folding them into names splits one opcode into several and makes the resulting
instruction table wrong. The same applies to operand size on arithmetic.

## Verdicts

For each handler give:

* `op` — the operation name;
* `operand` — what the inline operand means, or `none`;
* `stack_effect` — net change in dwords on the VM stack (e.g. `-1`, `0`, `+1`),
  or `?` if the trace does not settle it;
* `confidence` — `high` when the trace shows the whole operation;
  `medium` when part of it is carried by an unmodelled instruction or a value
  the dump cannot supply; `low` when you are guessing. **Say `low` rather than
  inventing a plausible name** — a wrong opcode silently corrupts every
  devirtualized function that uses it, and the top handlers here appear in
  hundreds of functions each.
* `evidence_hex` — **a byte-exact, space-separated substring of one of that
  unit's own instruction hex fields.** Copy it from the trace; never type bytes
  from memory. Quote the instructions that carry the semantics, not the
  dispatch tail.
* `why` — one sentence tying those bytes to the operation.

## Discipline

* Judge only from the unit in front of you.
* The dispatch tail looks nearly identical in every handler. It is never the
  answer; if the only thing a handler does is dispatch, that is `VM_NOP`.
* Junk is normal: `mov bl,bl`, `mov edx,edx`, arithmetic on values nothing
  reads. Ignore it rather than explaining it.
* Two handlers can implement the same operation — the mutation layer emits many
  variants. Naming two units identically is expected and correct.
