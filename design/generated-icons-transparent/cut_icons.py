"""Cut icon sheets into individual transparent PNGs.

Both sheets are 1536x1024 → 4 cols x 4 rows of 384x256 cells. For each
cell we:
  1. Crop the cell rect from the source sheet.
  2. Find the non-transparent bounding box of the icon inside.
  3. Pad to a square (centered) with a 10% margin so all icons feel
     visually consistent regardless of original aspect ratio.
  4. Resize to 256x256 with high-quality LANCZOS filtering.
  5. Save as an RGBA PNG under src/WireBound.Avalonia/Assets/Icons/.

Run from the repo root:
    python design/generated-icons-transparent/cut_icons.py
"""
from pathlib import Path

from PIL import Image

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
SHEETS_DIR = REPO_ROOT / "design" / "generated-icons-transparent"
OUT_DIR = REPO_ROOT / "src" / "WireBound.Avalonia" / "Assets" / "Icons"

OUT_DIR.mkdir(parents=True, exist_ok=True)

CELL_W, CELL_H = 384, 256
COLS, ROWS = 4, 4
FINAL_SIZE = 256
MARGIN_PCT = 0.10  # 10% breathing room on each side

# (sheet, row, col, output_name) — order matches the on-canvas grids
LAYOUT = [
    # Sheet 1
    (1, 0, 0, "wb-brand-wirebound"),
    (1, 0, 1, "wb-nav-overview"),
    (1, 0, 2, "wb-nav-live"),
    (1, 0, 3, "wb-nav-apps"),
    (1, 1, 0, "wb-nav-connections"),
    (1, 1, 1, "wb-nav-system"),
    (1, 1, 2, "wb-nav-settings"),
    (1, 1, 3, "wb-action-tune"),
    (1, 2, 0, "wb-action-refresh"),
    (1, 2, 1, "wb-action-auto"),
    (1, 2, 2, "wb-action-search"),
    (1, 2, 3, "wb-action-time-range"),
    (1, 3, 0, "wb-metric-download"),
    (1, 3, 1, "wb-metric-upload"),
    (1, 3, 2, "wb-metric-analytics"),
    (1, 3, 3, "wb-metric-insight"),
    # Sheet 2
    (2, 0, 0, "wb-entity-cpu"),
    (2, 0, 1, "wb-entity-memory"),
    (2, 0, 2, "wb-entity-thermal"),
    (2, 0, 3, "wb-entity-cores"),
    (2, 1, 0, "wb-entity-correlation"),
    (2, 1, 1, "wb-entity-network-node"),
    (2, 1, 2, "wb-entity-helper"),
    (2, 1, 3, "wb-entity-preview"),
    (2, 2, 0, "wb-status-warning"),
    (2, 2, 1, "wb-status-error"),
    (2, 2, 2, "wb-status-success"),
    (2, 2, 3, "wb-entity-disconnected"),
    (2, 3, 0, "wb-adapter-wifi"),
    (2, 3, 1, "wb-adapter-ethernet"),
    (2, 3, 2, "wb-adapter-vpn"),
    (2, 3, 3, "wb-adapter-loopback"),
]


def extract(sheet: Image.Image, row: int, col: int) -> Image.Image:
    left = col * CELL_W
    top = row * CELL_H
    cell = sheet.crop((left, top, left + CELL_W, top + CELL_H))

    # Clear an inner border so any bleed from adjacent cells' chroma-stripped
    # edges doesn't get included in the bbox below. The OpenAI-generated
    # sheets sometimes have a few stray cyan pixels right at the cell seam.
    pad = 12
    mask_color = (0, 0, 0, 0)
    for y in range(cell.height):
        for x in range(cell.width):
            if x < pad or x >= cell.width - pad or y < pad or y >= cell.height - pad:
                cell.putpixel((x, y), mask_color)

    bbox = cell.getbbox()
    if bbox is None:
        return Image.new("RGBA", (FINAL_SIZE, FINAL_SIZE), (0, 0, 0, 0))
    trimmed = cell.crop(bbox)

    # Pad to a square with a margin so icons all feel optically similar.
    tw, th = trimmed.size
    side = max(tw, th)
    margin = int(side * MARGIN_PCT)
    canvas_side = side + 2 * margin
    canvas = Image.new("RGBA", (canvas_side, canvas_side), (0, 0, 0, 0))
    canvas.paste(trimmed, ((canvas_side - tw) // 2, (canvas_side - th) // 2))

    return canvas.resize((FINAL_SIZE, FINAL_SIZE), Image.LANCZOS)


def main() -> None:
    sheets = {
        1: Image.open(SHEETS_DIR / "wire-trace-sheet-1-transparent.png").convert("RGBA"),
        2: Image.open(SHEETS_DIR / "wire-trace-sheet-2-transparent.png").convert("RGBA"),
    }

    for sheet_idx, row, col, name in LAYOUT:
        icon = extract(sheets[sheet_idx], row, col)
        out_path = OUT_DIR / f"{name}.png"
        icon.save(out_path, "PNG", optimize=True)
        print(f"  {out_path.relative_to(REPO_ROOT)}")

    print(f"\nWrote {len(LAYOUT)} icons to {OUT_DIR.relative_to(REPO_ROOT)}/")


if __name__ == "__main__":
    main()


