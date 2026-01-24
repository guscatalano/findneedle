# Timeline Animation Language (TAL) - JSON Schema

Version: 1.1

## Overview

A JSON-based language for defining animated visualizations of windows moving across monitors over time, using real timestamps.

## Structure

```json
{
  "version": "1.1",
  "metadata": { ... },
  "timeline": { ... },
  "monitors": [ ... ],
  "windows": [ ... ],
  "events": [ ... ]
}
```

---

## `metadata` (required)

| Field | Type | Description |
|-------|------|-------------|
| `title` | string | Display title for the animation |
| `description` | string | Description of what this animation shows |
| `author` | string | Creator of the animation |
| `created` | string | ISO date (YYYY-MM-DD) |

---

## `timeline` (required)

Defines the time range and playback settings.

| Field | Type | Description |
|-------|------|-------------|
| `start` | string | ISO 8601 timestamp for animation start |
| `end` | string | ISO 8601 timestamp for animation end |
| `playbackSpeed` | number | Speed multiplier (1.0 = realtime, 2.0 = 2x speed) |
| `loop` | boolean | Whether to loop when reaching the end |

**Note:** The timeline automatically calculates duration from `end - start`.

---

## `monitors[]` (required)

Defines the display configuration. Monitors can appear/disappear and resize over time.

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique identifier |
| `label` | string | Display name shown on the monitor |
| `keyframes` | array | Position/size/visibility over time |

### Monitor Keyframe

| Field | Type | Description |
|-------|------|-------------|
| `time` | string | ISO 8601 timestamp |
| `x` | number | X position in virtual desktop |
| `y` | number | Y position in virtual desktop |
| `width` | number | Width in pixels |
| `height` | number | Height in pixels |
| `visible` | boolean | Whether monitor is connected (optional, default: true) |

---

## `windows[]` (required)

Defines windows/applications that move across monitors.

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique identifier |
| `label` | string | Display name shown in window title bar |
| `style` | object | Visual styling (optional) |
| `keyframes` | array | Position/size/visibility over time |

### Window Style

| Field | Type | Description |
|-------|------|-------------|
| `gradient` | array[2] | Start and end colors for gradient |
| `color` | string | Solid background color (if no gradient) |
| `borderRadius` | number | Corner radius in pixels |
| `opacity` | number | 0.0 to 1.0 |

### Window Keyframe

| Field | Type | Description |
|-------|------|-------------|
| `time` | string | ISO 8601 timestamp |
| `x` | number | X position in virtual desktop |
| `y` | number | Y position in virtual desktop |
| `width` | number | Width in pixels |
| `height` | number | Height in pixels |
| `visible` | boolean | Whether window is open (optional, default: inherits) |
| `opacity` | number | Override opacity (optional) |
| `zIndex` | number | Stack order (optional) |

---

## `events[]` (optional)

Annotations or markers on the timeline.

| Field | Type | Description |
|-------|------|-------------|
| `time` | string | ISO 8601 timestamp |
| `type` | string | Event type: `annotation`, `marker`, `action` |
| `text` | string | Description text |

---

## Timestamp Format

All timestamps use ISO 8601 format with timezone:

```
YYYY-MM-DDTHH:mm:ss.sssZ    (UTC)
YYYY-MM-DDTHH:mm:ss.sss±HH:mm  (with offset)
```

Examples:
- `"2024-01-15T10:30:00.000Z"` - UTC
- `"2024-01-15T10:30:00.500Z"` - UTC with milliseconds
- `"2024-01-15T02:30:00.000-08:00"` - Pacific time

---

## Coordinate System

- Origin (0,0) is top-left
- Coordinates are in **virtual desktop pixels**
- Monitors define regions in this virtual space
- Windows can span multiple monitors
- Automatically scaled to fit the viewer

---

## Examples

### Simple dual monitor setup
```json
"timeline": {
  "start": "2024-01-15T10:30:00.000Z",
  "end": "2024-01-15T10:30:10.000Z"
},
"monitors": [
  { "id": "top", "label": "Top", "keyframes": [
    { "time": "2024-01-15T10:30:00.000Z", "x": 0, "y": 0, "width": 1920, "height": 1080 }
  ]},
  { "id": "bottom", "label": "Bottom", "keyframes": [
    { "time": "2024-01-15T10:30:00.000Z", "x": 0, "y": 1080, "width": 1920, "height": 1080 }
  ]}
]
```

### Window appearing mid-animation
```json
"keyframes": [
  { "time": "2024-01-15T10:30:00.000Z", "visible": false },
  { "time": "2024-01-15T10:30:05.000Z", "x": 100, "y": 100, "width": 400, "height": 300, "visible": true },
  { "time": "2024-01-15T10:30:10.000Z", "x": 500, "y": 200, "width": 400, "height": 300 }
]
```

### Monitor disconnecting
```json
"keyframes": [
  { "time": "2024-01-15T10:30:00.000Z", "x": 0, "y": 1080, "width": 1920, "height": 1080, "visible": true },
  { "time": "2024-01-15T10:30:08.000Z", "x": 0, "y": 1080, "width": 1920, "height": 1080, "visible": false }
]
```

---

## Time Display

The viewer will display actual timestamps:
- Current time: `2024-01-15 10:30:02.500`
- Range: `10:30:00.000 ? 10:30:10.000`

When scrubbing, you see exactly what time you're viewing.

---

## Future Extensions (v1.2+)

- `connections[]` - Lines/arrows between windows
- `annotations[]` - Text labels on screen  
- `audio` - Sound cues at events
- `camera` - Zoom/pan the viewport
- `themes` - Predefined color schemes
- `relativeTimes` - Support for relative offsets like `"+500ms"`
