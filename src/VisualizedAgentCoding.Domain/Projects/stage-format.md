# Static initial scene, version 1

The stage is 480 × 360. Its origin is the center, X increases rightward and Y upward.
Sprites are positioned by image center. Direction 90 is the original image; clockwise
rotation is `direction - 90`. Viewport resizing scales uniformly without changing coordinates.

`scenes/stage.json` stores editable initial state, separately from Python execution:

```json
{
  "version": 1,
  "currentBackdropId": "object_backdrop_id",
  "objects": [
    {
      "objectId": "object_sprite_id",
      "x": 40, "y": 20, "size": 100, "direction": 90,
      "visible": true, "layer": 0, "clickEnabled": true,
      "imagePath": "assets/images/unique-name.png"
    }
  ]
}
```

Object identity comes from `project.json`. An absent scene means defaults: centered,
size 100%, direction 90, visible, layer 0, clickEnabled true, no image, first backdrop. A scene is written
after layout changes on save/navigation/project close/exit. Opening an unchanged legacy
project does not create a scene. Unknown versions/fields, duplicate identities, nonfinite
transforms, and external image paths are rejected. Removed objects are omitted on the next
scene save. Current backdrop remains independent of editor selection.

Sprite X/Y range is ±10000; size 1–1000%, direction −180–180, layer ±10000. Higher
layers draw in front; object-list order breaks ties. Hidden sprites do not draw. `ClickEnabled` (default true) alone decides whether a sprite intercepts
stage clicks, regardless of visibility. Hidden sprites can be selected and shown in the object manager. New backdrop images fit
inside the stage without cropping, centered without upscaling. Only the current backdrop is drawn; sprite transforms do
not apply to backdrops.

PNG/JPEG/BMP/WebP imports copy a validated snapshot into `assets/images/` under a unique
name. Limits are 20 MB, 8192 pixels per dimension, and 16 million pixels. Headers are checked
before full bitmap allocation. Every replacement resets size to 100%. New images use
`min(1, 480 / width, 360 / height)` as their base scale, so both axes fit without upscaling.
100% refers to this fitted size, not the original pixel size; original bytes are retained for
quality. `normalizeImage=true` records the new interpretation. Old records lacking the flag
keep their previous geometry until an explicit replacement. Relative references survive project moves and source-image
removal. Missing assets retain their references and show a placeholder and reimport hint.
Replacing an image collects unreferenced app-managed GUID image files. Current unsaved and
last-saved scene references, other project Python/JSON text references, and the open editor
buffer are retained. Thus saved old images survive until replacement layout is saved, while
discarded intermediate imports can be removed earlier. Shared images and manually named
files remain. Revision conflicts or incomplete reference checks skip cleanup; deletion failures
report a retry hint. Cleanup is deferred while Python is active. Dynamic computed file paths
are not a resource dependency contract; reusable assets should retain explicit scene/source
references. `assets/objects` is a reserved, currently unused directory, not an image destination.

Saving checks the exact loaded source revision and replaces one JSON file through a temporary
file. External changes block overwriting and retain local values. Explicit reload confirms
discarding unsaved stage edits in the same in-window overlay style as project management;
cancel, close, and Escape preserve the current layout. Failed parsing leaves it intact. Scene and
script saves are not a cross-file atomic transaction.

New projects no longer create empty `runtime/`; existing project-local runtime files remain
untouched. Runtime transforms use a separate transient preview, never this persisted model.
Tests use temporary projects and never modify the two retained manual projects.

## Runtime preview

Each desktop run freezes the saved scene as `initialStage` alongside its source request.
The host checks the scene revision again after preparing sources. Python merges, from lowest
to highest priority: system defaults, saved layout, explicit class/constructor parameters,
then assignments in `initialize()`. Omitted values differ from explicit `0` and `False`.
Class declarations such as `x = 0` and constructor keyword arguments such as
`super().__init__(visible=False)` are explicit. Constructor assignments take priority over
class declarations. All initialization completes before the first normal preview and start
handlers. Assignments in start/message handlers update the runtime instance.

`stage` events on the authenticated protocol-v2 channel carry a `stage` object with
`sessionId`, monotonically increasing `sequence`, `objects` (stable `objectId` plus x/y/size/
direction/visible/layer/clickEnabled), and `currentBackdropId`. Changed snapshots are sampled at 30 Hz;
initialization completion and exit flush immediately. The host retains at most one pending
stage frame, validates object membership/values/session/sequence, and updates UI bindings on
its normal 50 ms dispatch timer. No user stdout parsing is involved. Multiple assignments
between frames are coalesced; this is a preview, not an atomic multi-property animation API.

`BackdropBase.activate()` selects that background by stable identity. The current background's
visibility affects its preview; background transforms are retained but not used to position
the fixed stage background. Images remain the initial project assets, without a runtime
costume replacement API. Runtime size uses the imported image's logical 100% base dimensions.

During execution, object-manager edits and stage dragging are disabled. Completion, failure,
and stop retain the latest preview; saving still writes only initial layout. The inspector
displays preview X/Y/size/direction/layer/visibility and is read-only only while Python runs.
After stop, completion or failure, direct property edits are enabled. The first explicit edit
clears the transient preview and applies only that requested property to the initial layout;
other runtime values are never promoted. A binding echo of the current display value does
not count as an edit. Visibility/layer commands use the same transition. Tiles and tabs remain available to inspect other objects/backgrounds;
while a run is active this selection does not open scripts or change the active background. Once the run has
stopped, selecting an object opens its script while the retained preview stays visible until an explicit edit or
"返回初始布局".
The "返回初始布局" button is available after execution ends, clears all transient transforms
and restores the initial background and initial inspector values together. It does not reread disk,
write files, or discard unsaved initial layout edits. Another run or changing projects also
clears the preview. The next run creates
fresh instances and merges initial values again. A managed-main event loop now keeps desktop
runs active after startup, dispatching role clicks on the main thread until Stop or error.
Role click events use stable object identities and a bounded per-session queue. A press that
misses every `clickEnabled` role is sent as one `stage-click` command and dispatched to the
backdrop that is active at dispatch time; hidden roles still intercept when `clickEnabled` is
true. The winning backdrop decides `clickEnabled` gating, and a missing handler is ignored.
Key events use `eventName=key:<name>` with lower-case letters, digits, or preset names such as
`space`/`up`/`any`; one keystroke runs every object bound to that key, in binding order, and
`any` matches every key.
See the Python runtime README for the control protocol. Existing custom project-local bases are not overwritten; to opt into
desktop stage synchronization they must inherit the product bases.
