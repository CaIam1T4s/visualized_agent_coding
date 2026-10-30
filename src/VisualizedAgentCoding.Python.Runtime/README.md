# Python execution runtime (API 1)

This product component is distributed beside the desktop app under `python-runtime/`.
It is separate from the read-only static analysis Helper. `vac_runtime` provides minimal
object state, while `runtime/element_base.py` and `runtime/backdrop_base.py` are compatibility
imports for v1/v2/v3 templates. Project-local base files retain priority and are never overwritten.
Existing custom bases must remain constructible without arguments and permit context injection;
incompatible bases fail explicitly. No automatic project migration is performed.

The app saves the current buffer and validates all collected project sources before launching.
The runner compiles the captured sources, uses a snapshot importer, and reports tracebacks using
the original file names and line cache. Script edits after launch affect the next run. Assets and
other non-source data are read from the original project working directory.

The desktop owns `scripts/main.py` (entry template version 1); it is not a user script.
New projects receive a fixed template that calls private `vac_runtime._entry.run_project()`.
Before runtime analysis, the application upgrades only the exact original empty `main()`
template (LF or CRLF). Modified entries and unknown versions block execution without being
overwritten. The captured entry is rechecked with the other sources before launch, excluded
from user import rules, and never included among navigable user error locations. This is an
ordinary file update with a pre-write check, not a crash-safe transaction or security sandbox.

Lifecycle: execute the managed entry once as `__main__`; its runtime bridge loads object
modules, constructs every object, injects `_vac_context` (stable script/object IDs, project root,
runtime API), resolves start and message bindings, initializes all instances, opens message
routing, then invokes each object's
explicit start binding or `_vac_on_start` fallback. The bridge requires the active execution
thread, permits only one invocation per session, and clears its state even on failure.
Restart creates fresh
instances. Start handlers currently run sequentially; no stage event loop, animation, clone,
parallel event scheduling, or public stage API is supplied yet.

Initialization failure aborts the remaining initializations and all start handlers. User-level
project logic belongs in object start regions and user modules called by those regions; user
modules do not run automatically. The entry bridge owns a synchronous message router for the
session. Runs finish after start handlers and non-daemon workers complete; message handlers
alone do not keep the process alive. A persistent event loop is not implemented.
For compatibility with existing low-level process clients, unmarked submitted entry sources
retain the previous initialize / custom entry / start ordering. Desktop project preparation
does not expose that path: only the exact managed template is submitted.

## Synchronous messages

The public API is `from vac_runtime import broadcast` (managed import source `runtime`).
Object regions must add it via the import manager; user modules may also use the normal
Python import. Internal entry, router, and base APIs remain unavailable through user imports.

`broadcast("start")` enters the main-thread router established by the managed project entry,
calls every matching object/backdrop handler in manifest object order, and returns only after
all receivers finish. Matching is case-sensitive, and an unknown message returns normally.
Names use 1–248 UTF-16 units with no surrounding whitespace or control characters. No payload,
directed recipient, asynchronous scheduling, or queue is provided. Nested broadcasts complete
before the outer receiver continues, with a depth limit of 32. Errors abort the current run;
the existing structured exception pipeline maps the sender and receiver to frozen user regions.

Broadcast is forbidden before all initializations finish and from worker threads, including
while the main thread is waiting in `input()`. A receiver can itself call `input()`; subsequent
receivers then wait. Stop and process-tree cleanup apply to receivers just like startup code.
Bindings and routing availability are cleared on normal exit, failure, and cancellation.

v3 message regions use existing `kind=event`, independent region IDs, and an `eventName` of
`message:<name>`. The editor supports add, rename, confirmed deletion with a code preview, and
native structural undo/redo. Rename preserves the region ID and does not rewrite senders.
Runtime preparation derives broadcast bindings from the verified frozen source, replacing
manifest broadcast declarations for v2/v3 objects so deleted regions cannot leave ghost handlers.
Other event identities are preserved without implying execution support for those events.

The desktop launches an unbuffered UTF-8 child with stdin/stdout/stderr pipes. An ephemeral
loopback socket authenticated with a per-session token carries ready/exception/finished events;
printed JSON never becomes a control message. Stdin remains available after ready until exit
or stop. Stop closes stdin and requests KeyboardInterrupt, then kills the child process tree
if it fails to exit within 500 ms. Independent processes are not a security sandbox.

The request snapshot is written to a unique system temporary directory and removed after the
session. Nothing is installed in the user project and no system PATH is changed. Python 3.10+
is required; both execution and the static Helper use the interpreter selected in General
settings. Third-party package installation and a bundled interpreter are outside this stage.

## Error protocol and presentation (protocol 2)

All control messages carry `protocolVersion: 2`; an incompatible runtime is rejected. Exception
messages contain `exception` (type, message, source frames, cause/context, suppressContext,
exception-group children, notes and truncation status) and `rawTraceback`. Legacy top-level
`frames` is retained for diagnostic consumers. Frame rows are original one-based source rows;
optional columns/end columns are one-based UTF-16 converted from CPython's UTF-8 byte offsets.
SyntaxError character offsets are converted separately. No locals are captured. Python 3.10
provides line-only frames; groups and expression ranges are available on supporting versions.

The runner captures unhandled main-thread and worker-thread exceptions over this control channel,
without also printing its traceback to stderr. It waits for non-daemon workers before closing
the diagnostic channel; an unhandled worker error makes the run fail. User-written stdout/stderr,
including an explicit `traceback.print_exc()`, remain untouched. Protocol writes are serialized.
Structured capture is bounded to 128 nodes, 12 nesting levels and 256 frames per node. Raw
diagnostics are capped at 256,000 characters, with explicit truncation markers.

Before execution, Application freezes each recognized script's full source, user view, fingerprint,
stable IDs, region metadata and line/column mapping. These snapshots stay in the host and are not
serialized into the execution request. The runtime reports actual execution coordinates; the
Application presentation service translates them using these frozen maps. Managed imports lead
to the import summary; hidden wrappers and unknown coordinates have no invented user line number.
Display, snippet and navigation originate from the same captured source. No editor mutation or
fixed line subtraction is involved. Preparation syntax/import diagnostics use the same pipeline.

The terminal has a separate error card with user frames and a selected innermost user location.
Product/library frames and original absolute paths are available in collapsed diagnostic details.
Copy Error copies the user report; Copy Full Diagnostics copies raw diagnostics. User-provided
exception messages/notes are preserved and may themselves contain paths. Editing, renaming,
deleting or switching/reopening projects invalidates old navigation; unchanged buffers retain
their exact original coordinate for the editor to map once. External changes are checked again
before navigating, and source identity is checked after asynchronous script opening.
