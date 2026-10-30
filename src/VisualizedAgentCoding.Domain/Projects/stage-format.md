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
      "visible": true, "layer": 0,
      "imagePath": "assets/images/unique-name.png"
    }
  ]
}
```

Object identity comes from `project.json`. An absent scene means defaults: centered,
size 100%, direction 90, visible, layer 0, no image, first backdrop. A scene is written
after layout changes on save/navigation/project close/exit. Opening an unchanged legacy
project does not create a scene. Unknown versions/fields, duplicate identities, nonfinite
transforms, and external image paths are rejected. Removed objects are omitted on the next
scene save. Current backdrop remains independent of editor selection.

Sprite X/Y range is ±10000; size 1–1000%, direction −180–180, layer ±10000. Higher
layers draw in front; object-list order breaks ties. Hidden sprites do not draw or intercept
stage clicks, but can be selected and shown in the object manager. New backdrop images fit
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
untouched. This feature does not change Python bases, synchronize execution transforms,
dispatch stage input events, or add an animation/event loop. Future runtime state needs a
separate model. Tests use temporary projects and never modify the two retained manual projects.
