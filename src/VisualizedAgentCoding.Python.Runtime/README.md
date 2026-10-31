# Python execution runtime (API 1)

This product component is distributed beside the desktop app under `python-runtime/`.
It is separate from the read-only static analysis Helper. `vac_runtime` provides validated
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
runtime API), resolves start, message and click bindings, initializes all instances, opens message
routing, then invokes each object's
explicit start binding or `_vac_on_start` fallback. The bridge requires the active execution
thread, permits only one invocation per session, and clears its state even on failure.
Restart creates fresh
instances. Start and click handlers run sequentially. Desktop runs then remain in the managed
event loop; animation APIs, cloning and parallel event scheduling are not supplied.

Initialization failure aborts the remaining initializations and all start handlers. User-level
project logic belongs in object start regions and user modules called by those regions; user
modules do not run automatically. The entry bridge owns a synchronous message router for the
session. Desktop runs remain active after start handlers complete until Stop or an error.
Low-level batch requests without keepAlive finish after start handlers and non-daemon workers.
For compatibility with existing low-level process clients, unmarked submitted entry sources
retain the previous initialize / custom entry / start ordering. Desktop project preparation
does not expose that path: only the exact managed template is submitted.

## Synchronous messages

The public API is `from vac_runtime import broadcast, timer, switch_backdrop` (managed import source `runtime`).
Object regions must add it via the import manager; user modules may also use the normal
Python import. `timer()` returns the seconds elapsed since the current run started; object
scripts may also read the same value from `self.timer`. `switch_backdrop(name)` switches the current
backdrop from any object; a backdrop can use `self.activate()` for its own identity. Internal
entry, router, clock and base APIs remain unavailable through user imports.

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


## Stage state API

`ElementBase` and `BackdropBase` expose `x`, `y`, `size`, `direction`, `visible`, `layer`, and `click_enabled` (default `True`). `click_enabled` controls click interception only and is independent of `visible`.
Coordinates use a 480x360 stage, center origin, X right and Y up; direction 90 points right.
Ranges match the scene contract: x/y +/-100000, size 1..1000, direction -180..180,
integer layer +/-10000, strict boolean visibility. Invalid assignments raise ValueError
without replacing the previous valid value. Both bases accept optional keyword initial values.
Class declarations are also explicit initial values. The desktop never infers omission by
comparing values to defaults: explicit zero and False override the saved layout.

```python
# In a role's class-definition region:
x = 0
visible = False

# In its initialization region (runs after the layout/parameter merge):
self.y = 30
self.visible = True

# In start or message event regions:
self.x += 10
self.size = 150
self.direction = -90

# In a background's start/message region:
self.activate()
```

### Instant movement

`ElementBase` also provides instant motion methods. They change only runtime state and do not
block the event loop:

```python
self.move(10)                  # move along the current direction
self.turn(15)                  # clockwise degrees; negatives turn counter-clockwise
self.goto(0, 0)                # set x/y directly
self.point_in_direction(90)    # set direction, normalized to -180..180
self.change_x(5)
self.change_y(-5)
self.point_towards_pointer()   # point at the latest stage pointer position
self.move_to_pointer()         # move to the latest stage pointer position
```

`move` uses `x += steps * sin(direction)` and `y += steps * cos(direction)`. `goto` and `move`
validate both coordinates before writing, so an invalid Y does not leave a partial X update.
Coordinates may leave the 480x360 stage but are clipped when drawn; the accepted range is
+/-100000. The pointer position is the latest position received while the stage was visible;
if no position has ever been received, both pointer methods leave the role unchanged.
`BackdropBase` does not expose motion methods.

Non-blocking position animations are also available:

```python
self.glide_to(x, y, seconds)
self.glide_by(dx, dy, seconds)
```

`glide_to` interpolates from the current position to the target; `glide_by` adds the delta to
the current position first. `seconds <= 0` applies the position immediately. Only one position
animation is kept per role; a new glide replaces the old one. A direct `self.x`/`self.y`
assignment cancels the active animation. Animations advance on the main-thread event loop and
publish through the normal stage sampler; they do not block clicks, keys, timers, or broadcasts.

For a full ordinary Python subclass, `super().__init__(x=0, visible=False)` supplies explicit
constructor parameters. Layout is fallback only; initialize assignments take final priority.
The UI retains runtime transforms as a separate preview and never saves them into stage.json.
Runtime sampling continues through non-daemon workers and flushes at exit (including errors).
Low-level callers may omit `initialStage` for legacy execution without stage binding.
Desktop runs include it; custom local bases must inherit the product bases, and are never
rewritten automatically. See [scene and runtime wire contract](../VisualizedAgentCoding.Domain/Projects/stage-format.md).
Role clicks, backdrop (`stage-click`) clicks, backdrop switch (`backdrop-switch`) events, key events, timer events and a persistent project event loop are implemented.


## Persistent execution and sprite clicks

Desktop requests set `keepAlive: true`. Managed main constructs/initializes all objects,
executes start handlers in order, then enters a main-thread event loop. Runs stay active even
without any click handler until Stop, an uncaught error, or an explicit process exit. Low-level
requests default to `keepAlive: false` to retain finite batch execution compatibility.

The runner emits `interactive-ready` only after all start handlers return. Click input before
that point is ignored. Host-to-runtime commands on the existing authenticated control socket
are newline-delimited JSON: `{"kind":"sprite-click","sessionId":"...","objectId":"..."}` and
`{"kind":"stage-click","sessionId":"..."}`, `{"kind":"key","sessionId":"...","key":"a"}`, and
`{"kind":"pointer","sessionId":"...","x":120,"y":-30}`. The stage click carries no object identity;
the runtime dispatches it to the backdrop that is active when the queue item is processed. A key
event runs every object bound to that key, plus `any` bindings, in binding order. Pointer commands
replace the latest pointer state instead of entering the bounded click queue; they do not publish
a stage frame on their own and are throttled by the host.
The existing `stop` line remains supported and is parsed independently of user stdin. Commands
are limited to 8192 bytes; malformed commands, wrong sessions and unsupported kinds are ignored.
The host serializes writes, checks the current session and known role identity, and disables
click submission during preparation/stopping. No user stdout parsing or main.py edits occur.

Each role may have one source region with `kind=event`, `eventName=sprite-click`, and a stable
region ID; its generated handler is `_vac_event_<id>`. Preparation derives click bindings from
verified source regions and replaces manifest click bindings for v2/v3 objects. Backgrounds
cannot own role-click regions, and roles cannot own `stage-click` regions; each backdrop may
own one `kind=event` region with `eventName=stage-click`. Any object may own `eventName=key:<name>`
regions for lower-case letters, digits, or preset names such as `space`/`up`/`any`, and
`eventName=timer:<seconds>` or `timer:every:<seconds>` timer regions, plus
`backdrop-switch:<targetObjectId>` regions. A backdrop switch runs every object/backdrop handler
bound to the new backdrop, in binding order, after the switch and before the next stage frame.
Deleting a backdrop reports `switch_backdrop` call sites and `backdrop-switch` regions in other
scripts with file/line locations; their source is left for the user to edit.
Click callbacks run sequentially on the main thread, through the
managed-entry lifecycle; synchronous broadcast within them keeps its existing nested ordering.
Uncaught callback errors terminate the session and use the frozen source/user-region error maps.

The input queue holds at most 64 pending clicks; excess clicks are discarded. Input or long
handlers block following callbacks, while stage publication and Stop remain separate. Timer
deadlines are checked on each event-loop turn, so a long callback delays later timers until it
returns; missed repeating intervals collapse into one immediate run. Dispatch
rechecks target identity and `click_enabled`, ignoring disabled/deleted/unknown recipients. A
`stage-click` resolves the active backdrop at dispatch time and is dropped when no backdrop is
active or the recipient has no handler. Stop clears
the queue and interrupts the main thread; forced process-tree cleanup remains the fallback for
non-cooperative Python. Restarts create a fresh inbox and session. The latest preview remains
separate from saved initial state.

The UI routes a left press to the topmost role with `click_enabled` true in render layer order, using
its transformed rectangular bounds (transparent image pixels are not an alpha mask). Editing
continues to use selection/dragging; running uses clicks, and an empty-stage press dispatches a
`stage-click` to the active backdrop instead of selecting. Hidden roles keep intercepting when
`click_enabled` is true, because a hidden element still hit-tests.
The inspector tiles remain available for browsing. No concurrent callback execution or
dedicated animation API is added.

Runtime inbox checks: `python -B -m unittest discover -s tests/python-runtime -v`.
