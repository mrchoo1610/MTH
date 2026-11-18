"""Generate the DiskCleaner app icon (multi-size .ico + .png).

Usage: python scripts/generate-icon.py
Output: src/DiskCleaner/Assets/app.ico, src/DiskCleaner/Assets/app.png
Design: rounded square with the app's accent gradient (#6D5EF6 -> #A35BF0),
white disk-drive glyph and a sparkle (= "clean").
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

S = 1024  # master canvas, downsampled for every icon size
ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "src" / "DiskCleaner" / "Assets"

C1 = (0x6D, 0x5E, 0xF6)
C2 = (0xA3, 0x5B, 0xF0)
WHITE = (255, 255, 255, 255)


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient(size):
    """Diagonal gradient top-left -> bottom-right."""
    g = Image.new("RGBA", (size, size))
    px = g.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * (size - 1))
            px[x, y] = (*lerp(C1, C2, t), 255)
    return g


def sparkle(draw, cx, cy, r, fill):
    """4-point star."""
    k = r * 0.22
    pts = [(cx, cy - r), (cx + k, cy - k), (cx + r, cy), (cx + k, cy + k),
           (cx, cy + r), (cx - k, cy + k), (cx - r, cy), (cx - k, cy - k)]
    draw.polygon(pts, fill=fill)


def master():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Soft shadow under the tile
    pad = 64
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([pad, pad + 24, S - pad, S - pad + 24], radius=220,
                                             fill=(20, 10, 60, 110))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))

    # Gradient tile
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, S - pad, S - pad], radius=220, fill=255)
    tile = gradient(S)
    img.paste(tile, (0, 0), mask)

    d = ImageDraw.Draw(img)

    # Disk drive glyph (white outline body + slot + LED)
    x0, y0, x1, y1 = 230, 470, 794, 760
    d.rounded_rectangle([x0, y0, x1, y1], radius=70, fill=WHITE)
    # inner face
    d.rounded_rectangle([x0 + 44, y0 + 44, x1 - 44, y1 - 44], radius=36, fill=(*lerp(C1, C2, 0.6), 255))
    # slot line + LED
    d.rounded_rectangle([x0 + 100, y0 + 128, x0 + 380, y0 + 162], radius=17, fill=WHITE)
    d.ellipse([x1 - 170, y0 + 115, x1 - 100, y0 + 185], fill=(0x22, 0xC5, 0x5E, 255))

    # Sparkles (clean)
    sparkle(d, 690, 300, 120, WHITE)
    sparkle(d, 470, 250, 58, (255, 255, 255, 230))
    sparkle(d, 820, 440, 40, (255, 255, 255, 210))
    return img


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    m = master()
    m.resize((256, 256), Image.LANCZOS).save(OUT_DIR / "app.png")
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    frames = [m.resize((s, s), Image.LANCZOS) for s in sizes]
    frames[-1].save(OUT_DIR / "app.ico", format="ICO", sizes=[(s, s) for s in sizes],
                    append_images=frames[:-1])
    print("wrote", OUT_DIR / "app.ico", "and app.png")


if __name__ == "__main__":
    main()
