"""Render the KRATE mark to PNGs and assemble a multi-size .ico.

Every shape is an axis-aligned rectangle (one rounded), so this rasterises them directly with 4x4
supersampling instead of pulling in an image library — which also makes it possible to hand-tune the
small sizes, and they need it.

At 16px the faithful geometry straddles pixel boundaries: the three tool bars land on x=3.94..5.56,
7.19..8.81, 10.44..12.06 and smear into grey double-marks. SMALL is the same design snapped to a
16-unit grid so every edge falls on a whole pixel at 16x16. That is what icon sets mean by hinting
the small sizes, and it is the size a Windows app icon actually lives at.
"""
import struct
import sys
import zlib

TILE = (8, 8, 240, 240, 48, (0x1A, 0x1A, 0x19))

# Faithful to test.svg — used at 24px and up, where it resolves cleanly.
FULL = [
    (63, 52, 26, 34, 0), (115, 40, 26, 46, 0), (167, 60, 26, 26, 0),
    (44, 112, 168, 30, 0),
    (52, 168, 152, 54, 4),
]

# Snapped to multiples of 16, so at 16x16 every edge is a whole pixel.
#   tools  -> cols 4-5, 7-8, 10-11   (2px wide, 1px gaps)
#   rail   -> rows 7-8               (2px tall)
#   body   -> rows 10-13             (4px tall)
SMALL = [
    (64, 64, 32, 32, 0), (112, 48, 32, 48, 0), (160, 64, 32, 32, 0),
    (48, 112, 160, 32, 0),
    (48, 160, 160, 64, 0),
]
WHITE = (0xFF, 0xFF, 0xFF)


def inside(px, py, x, y, w, h, r):
    if px < x or py < y or px > x + w or py > y + h:
        return False
    if r <= 0:
        return True
    cx = x + r if px < x + r else (x + w - r if px > x + w - r else None)
    cy = y + r if py < y + r else (y + h - r if py > y + h - r else None)
    if cx is None or cy is None:
        return True
    return (px - cx) ** 2 + (py - cy) ** 2 <= r * r


def render(size):
    shapes = SMALL if size <= 20 else FULL
    ss = 4
    scale = size / 256.0
    px = bytearray(size * size * 4)
    for iy in range(size):
        for ix in range(size):
            tile_hits = mark_hits = 0
            for sy in range(ss):
                for sx in range(ss):
                    ux = (ix + (sx + 0.5) / ss) / scale
                    uy = (iy + (sy + 0.5) / ss) / scale
                    if inside(ux, uy, *TILE[:5]):
                        tile_hits += 1
                        for s in shapes:
                            if inside(ux, uy, *s):
                                mark_hits += 1
                                break
            if not tile_hits:
                continue
            total = ss * ss
            a_tile = tile_hits / total
            frac = mark_hits / tile_hits
            tr, tg, tb = TILE[5]
            o = (iy * size + ix) * 4
            px[o] = int(tr * (1 - frac) + WHITE[0] * frac + 0.5)
            px[o + 1] = int(tg * (1 - frac) + WHITE[1] * frac + 0.5)
            px[o + 2] = int(tb * (1 - frac) + WHITE[2] * frac + 0.5)
            px[o + 3] = int(a_tile * 255 + 0.5)
    return bytes(px)


def png(size, rgba):
    raw = b"".join(b"\x00" + rgba[y * size * 4:(y + 1) * size * 4] for y in range(size))

    def chunk(kind, data):
        c = kind + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def ico(entries):
    out = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    for size, data in entries:
        dim = 0 if size >= 256 else size          # 0 means 256 in an ICONDIRENTRY
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    return out + b"".join(d for _, d in entries)


def preview(size, rgba):
    ramp = " .:-=+*#%@"
    rows = []
    for y in range(size):
        row = ""
        for x in range(size):
            o = (y * size + x) * 4
            if rgba[o + 3] < 40:
                row += " "
                continue
            lum = (rgba[o] * 0.299 + rgba[o + 1] * 0.587 + rgba[o + 2] * 0.114) / 255
            row += ramp[min(len(ramp) - 1, int(lum * (len(ramp) - 1) + 0.5))]
        rows.append(row)
    return "\n".join(rows)


if __name__ == "__main__":
    out_dir = sys.argv[1]
    sizes = [16, 20, 24, 32, 48, 64, 128, 256]
    entries = []
    for s in sizes:
        rgba = render(s)
        entries.append((s, png(s, rgba)))
        if s in (16, 24):
            print(f"--- {s}x{s} ---")
            print(preview(s, rgba))
            print()
    with open(out_dir + "/krate.ico", "wb") as f:
        f.write(ico(entries))

    # A PNG for showing the logo inside the app itself (the nav pane header). 256px so it stays
    # crisp when drawn at ~20px logical size even on a 200% display.
    with open(out_dir + "/krate-logo-256.png", "wb") as f:
        f.write(png(256, render(256)))
    import os
    print(f"krate.ico: {os.path.getsize(out_dir + '/krate.ico')} bytes, sizes {sizes}")
