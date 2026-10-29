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

Lifecycle: load object modules, construct every object, inject `_vac_context` (stable script/object
IDs, project root, runtime API), initialize all instances, execute `scripts/main.py` once as
`__main__`, then invoke each object's explicit start binding or `_vac_on_start` fallback.
Initialization failure aborts before the main entry and start handlers. Restart creates fresh
instances. Start handlers currently run sequentially; no stage event loop, animation, clone,
parallel event scheduling, or public stage API is supplied yet.

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
