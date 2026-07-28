# ADR-0001 — UI Framework

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-28 |
| **Decision** | Avalonia UI 12.1.x |

---

## Context

We need a desktop UI technology that runs **natively** on Linux (X11 and Wayland), Windows and
macOS, uses little memory, and can render long virtualized lists and a custom-drawn commit graph
smoothly.

Critical requirements:

1. **Custom drawing must be first-class.** The commit graph is not a widget tree; it wants a
   drawing surface. The framework has to hand us a low-level `DrawingContext`.
2. **Virtualized list performance** — histories of 500k commits.
3. **No Chromium, no WebView.** This is the project's reason for existing.
4. .NET ecosystem.

---

## Alternatives considered

### Avalonia UI — **CHOSEN**

A cross-platform .NET UI framework with XAML, rendering through its own Skia-based pipeline.

**For**
- Genuinely first-class Linux support (X11 and Wayland). The only mature option on this list that
  can say that.
- `Control.Render(DrawingContext)` gives direct drawing — exactly what the commit graph needs.
- Because it renders itself, output is **pixel-identical across platforms**. For something as
  visually precise as a DAG, that matters.
- Mature: the 11.x line has been in production for a long time, with a company behind it.
- WPF/WinForms knowledge transfers directly.

**Against**
- Does not use native OS widgets; it will not feel exactly like a Mac app on macOS.
- Smaller ecosystem than WPF — we will write some controls ourselves.
- Accessibility support lags behind native frameworks.

### .NET MAUI — rejected

**Why:** No Linux desktop target. Community efforts exist; official support does not. A framework
that does not support this project's primary platform is out of scope.

### GTK4 (Gir.Core / gtk-sharp) — rejected

**Why:** Excellent on Linux, painful everywhere else. Shipping GTK on Windows and macOS is large,
fragile and looks foreign. The C# bindings are a continuous maintenance burden. We could not keep
the cross-platform promise.

### Qt (.NET bindings) — rejected

**Why:** Technically strong, but the .NET bindings are immature and under-maintained. The
commercial/LGPL licensing dynamic adds complexity for an open-source desktop app. Moving to C++
would mean abandoning the .NET foundation.

### Electron / Tauri — rejected

**Why:** Contrary to the project's purpose. Electron is what we are escaping. Tauri is lighter,
but its rendering layer is still a system WebView — WebKitGTK on Linux, which is both inconsistent
and unpredictable in performance. Drawing a commit graph on a canvas is also slower and more
awkward than drawing into a .NET `DrawingContext`.

### WinForms/WPF under Wine — rejected

**Why:** That is GitExtensions today. Running under Wine is not "Linux support".

---

## Decision

**Avalonia UI 12.1.x**, targeting `net10.0`.

### Why 12 and not 11.3?

- Avalonia 12's two stated themes are **performance and stability** — exactly what this project needs.
- We are starting fresh; beginning on 11.3 and migrating later is wasted work.
- 12.x is a stable release receiving fixes.

Known costs of being on 12:

- Requires SkiaSharp 3.0; 2.88 support was dropped.
- .NET Framework and .NET Standard support removed — irrelevant, we target `net10.0`.
- `SystemDecorations` was renamed to `WindowDecorations`.
- Tizen, Direct2D1, `Avalonia.Browser.Blazor` and `BinaryFormatter` support removed — none affect us.

### Rendering and text shaping

This trips people up, so it is worth stating precisely. Verified empirically:

| Configuration | `UseSkia()` | `UseHarfBuzz()` | Result |
|---|---|---|---|
| `UsePlatformDetect()` | automatic | automatic | Works; text renders correctly |
| `UseX11()` / `UseWayland()` (explicit) | **required** | **required** | Without them the app fails at startup with `InvalidOperationException: No rendering system configured` |

**Rule: if you select the windowing backend explicitly, you must also configure the rendering
system and text shaping explicitly.** `Program.ConfigurePlatform` handles this.

Text rendering correctness is verified continuously by a headless render test that rasterizes the
main window and asserts on the resulting pixels. It runs in CI and needs no desktop session.

### Linux backend

Avalonia 12.1 introduced a **native Wayland** backend, but it is **opt-in**: it requires the
`Avalonia.Wayland` package and a `UseWayland()` call. The Linux default is still **X11**, which
works fine on Wayland sessions through XWayland.

Our decision: **X11 stays the default**; native Wayland is selected with `GITEXT_BACKEND=wayland`.
X11 works everywhere, and the native Wayland backend only stabilized in 12.1. Whether it becomes
the default will be revisited with real usage data.

> Measured during scaffolding: the native Wayland backend uses noticeably more memory
> (~325 MB vs ~229 MB RSS). To be investigated during performance work.

### Fallback plan

If we hit an unsolvable problem on Avalonia 12 (particularly on Linux), we drop back to the 11.3.x
line. To keep that cheap:

- The Avalonia version is pinned in one place, `Directory.Packages.props` (ADR-0006).
- Avalonia-specific API usage stays inside `GitExt.UI`; `GitExt.Core` and `GitExt.Graph` reference
  no UI package at all (ADR-0003). Changing the framework would not touch the business logic.

---

## Consequences

- `GitExt.Core` and `GitExt.Graph` **may not contain any Avalonia reference.** This is enforced at
  build time (error `GITEXT001`), not left to code review.
- The commit graph will be written as a custom `Control` with `Render(DrawingContext)`, not as a
  templated `ListBox`.
- Accessibility is limited to what Avalonia provides; a dedicated a11y audit is needed before 1.0.
- Every Avalonia minor upgrade requires a smoke test on both Wayland and X11.

---

## References

- [Avalonia — What's New](https://avaloniaui.net/whats-new)
- [Breaking changes in Avalonia 12](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
- [Bringing Wayland Support to Avalonia](https://avaloniaui.net/blog/bringing-wayland-support-to-avalonia)
