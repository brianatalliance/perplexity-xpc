# PerplexityXPC.Tray — Icon Resources

This directory holds embedded icon resources for the tray application.

## Required Files

| File | Purpose | Format |
|------|---------|--------|
| `tray_icon.ico` | Application / taskbar icon | ICO (multi-size: 16, 32, 48, 256 px) |
| `tray_green.ico` | Service running (green dot) | ICO 16×16 |
| `tray_yellow.ico` | Service connecting (yellow dot) | ICO 16×16 |
| `tray_red.ico` | Service stopped / disconnected (red dot) | ICO 16×16 |

> **Note:** The tray status icons (green/yellow/red) are generated at runtime by
> `TrayApplicationContext.CreateCircleIcon()` so the app compiles and runs even
> when these files are absent.  Providing proper `.ico` files will give crisper
> results on HiDPI monitors.

## Icon Design Specification

### `tray_icon.ico` — Application Icon

- **Concept:** A stylised capital "P" inside a filled circle.
- **Background:** Perplexity purple `#6c63ff`, fully opaque.
- **Letter "P":** White `#ffffff`, bold weight, centred inside the circle.
- **Shape:** Circle occupies 90 % of the canvas; 5 % transparent padding on all sides.
- **Sizes to include in the `.ico` bundle:**
  - 16×16 px — system tray (96 DPI default)
  - 32×32 px — normal HiDPI tray / small taskbar
  - 48×48 px — large taskbar icons
  - 256×256 px — Windows Explorer / installer

### `tray_green.ico`

- **Background:** Transparent
- **Shape:** Filled circle, colour `#4CAF50` (Material Green 500)
- **Size:** 16×16 px (single frame)

### `tray_yellow.ico`

- **Background:** Transparent
- **Shape:** Filled circle, colour `#FFC107` (Material Amber 500)
- **Size:** 16×16 px (single frame)

### `tray_red.ico`

- **Background:** Transparent
- **Shape:** Filled circle, colour `#F44336` (Material Red 500)
- **Size:** 16×16 px (single frame)

## How to Generate

You can generate these icons with any of the following tools:

- **Inkscape** (free, cross-platform): create SVG → export to PNG at required sizes → bundle with `png2ico`.
- **GIMP** (free): File → Export As → `.ico`, select multiple sizes.
- **IcoFX** (Windows, shareware): batch-create from a single source image.
- **online-convert.com**: upload a 256×256 PNG and download a multi-size ICO.

## Embedding in the Project

The `.csproj` already includes:

```xml
<EmbeddedResource Include="Resources\*.ico" />
```

Place the `.ico` files here and rebuild.  The `ApplicationIcon` property also
references `Resources\tray_icon.ico` for the Windows Explorer shell icon:

```xml
<ApplicationIcon>Resources\tray_icon.ico</ApplicationIcon>
```
