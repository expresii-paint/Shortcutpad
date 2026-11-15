#!/usr/bin/env python3
"""Generate the ShortcutPad icon (icon.png + multi-size app.ico).

Art: a rounded tile with a 2x2 pad of keycaps; the bottom-right key is the
accent colour and carries a return arrow, i.e. "a pad of shortcut keys, one of
which fires". Drawn at 4x and downscaled so every size stays crisp.
"""
import os
from PIL import Image, ImageDraw

S = 256          # final size
SS = 4           # supersample factor
W = S * SS

# palette
TOP = (124, 108, 255)      # violet
BOT = (74, 60, 208)        # deep indigo
KEY = (255, 255, 255)      # keycap white
ACC = (255, 182, 61)       # amber accent
ARROW = (58, 44, 150)      # arrow on the accent key


def rounded_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m


def gradient(size, top, bottom):
    g = Image.new("RGBA", (1, size))
    for y in range(size):
        t = y / max(1, size - 1)
        g.putpixel((0, y), tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    return g.resize((size, size))


def return_arrow(d, cx, cy, s, color):
    """Return/enter arrow centred at (cx,cy), overall size s (unit box 0..1).

    Same geometry as the Material 'keyboard-return' glyph: a left-pointing
    arrow whose shaft runs right and hooks UP at the far end.
    """
    pts = [
        (cx - 0.32 * s, cy),              # tip
        (cx - 0.04 * s, cy - 0.26 * s),   # head, upper
        (cx - 0.04 * s, cy - 0.11 * s),   # head base / shaft top
        (cx + 0.34 * s, cy - 0.11 * s),   # shaft top
        (cx + 0.34 * s, cy - 0.32 * s),   # hook, inner top
        (cx + 0.56 * s, cy - 0.32 * s),   # hook, top
        (cx + 0.56 * s, cy + 0.11 * s),   # hook, outer edge down to the shaft
        (cx - 0.04 * s, cy + 0.11 * s),   # shaft bottom
        (cx - 0.04 * s, cy + 0.26 * s),   # head, lower
    ]
    d.polygon(pts, fill=color)


def render(size):
    w = size * SS
    r = w * 0.22
    img = Image.new("RGBA", (w, w), (0, 0, 0, 0))

    # tile
    tile = gradient(w, TOP, BOT)
    mask = rounded_mask(w, r)
    img.paste(tile, (0, 0), mask)

    # soft top highlight
    hi = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi)
    hd.rounded_rectangle([w * 0.06, w * 0.03, w * 0.94, w * 0.5], radius=w * 0.18, fill=(255, 255, 255, 40))
    img = Image.alpha_composite(img, Image.composite(hi, Image.new("RGBA", (w, w), (0, 0, 0, 0)), mask))

    d = ImageDraw.Draw(img)

    # 2x2 keycaps
    m = w * 0.20
    gap = w * 0.075
    ks = (w - 2 * m - gap) / 2
    kr = ks * 0.26
    for row in range(2):
        for col in range(2):
            x0 = m + col * (ks + gap)
            y0 = m + row * (ks + gap)
            accent = (row == 1 and col == 1)
            d.rounded_rectangle([x0, y0, x0 + ks, y0 + ks], radius=kr,
                                fill=ACC if accent else KEY + (235,))
            if accent:
                return_arrow(d, x0 + ks / 2, y0 + ks / 2, ks * 0.62, ARROW)

    return img.resize((size, size), Image.LANCZOS)


out = os.path.dirname(os.path.abspath(__file__))
icon = render(S)
icon.save(os.path.join(out, "icon.png"))

sizes = [16, 24, 32, 48, 64, 128, 256]
icon.save(os.path.join(out, "app.ico"), sizes=[(s, s) for s in sizes])
print("wrote icon.png", icon.size, "and app.ico", sizes)
