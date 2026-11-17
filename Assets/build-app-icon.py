#!/usr/bin/env python3
"""Repack Assets/app.ico from the two SVG masters in this directory.

Usage: python3 Assets/build-app-icon.py [输出路径]   (缺省原地写 Assets/app.ico)

Plan: 256 as PNG, then 64/48/40/32 BMP from app-icon.svg and 24/20/16 BMP from
app-icon-small.svg, in that order. BMP entries are BI_RGB 32bpp bottom-up BGRA plus a
1bpp AND mask whose bit is set only where alpha is exactly 0.

Byte-for-byte reproducible on a given Pillow version (nothing in the output carries a
timestamp), but the 256 PNG entry's bytes do depend on that version's encoder — a Pillow
upgrade shows up as a diff on Assets/app.ico, and the sizes of the seven BMP entries must
not move. AppIconTests reconciles this file's PLAN with the shipped container.

Renders geometry off the SVG (rect + rx, paths, stroke-width) so the masters stay the
single source of truth; a gradient in a master is refused rather than sampled, because
DESIGN.md §1 原则 1 says 无渐变.
"""
import hashlib
import io
import os
import re
import struct
import sys

from PIL import Image, ImageDraw

SS = 8  # supersample factor, then LANCZOS down

HERE = os.path.dirname(os.path.abspath(__file__))
BIG = os.path.join(HERE, "app-icon.svg")
SMALL = os.path.join(HERE, "app-icon-small.svg")
SHIPPED = os.path.join(HERE, "app.ico")

# (边长, 母版, 容器) — 32px 及以上取大母版，24 / 20 / 16 取小母版。
PLAN = [(256, BIG, "png"), (64, BIG, "bmp"), (48, BIG, "bmp"), (40, BIG, "bmp"),
        (32, BIG, "bmp"), (24, SMALL, "bmp"), (20, SMALL, "bmp"), (16, SMALL, "bmp")]


def parse_svg(text, name):
    """Read viewBox side, tile rect, and the round-capped strokes out of one master."""
    if re.search(r"gradient", text, re.I):
        raise SystemExit(f"{name}：母版里出现渐变。DESIGN.md §1 原则 1 立的是无渐变。")
    rect = re.search(r"<rect\s+([^>]+)/>", text)
    if not rect:
        raise SystemExit(f"{name}：读不到 <rect>，这一份不是这一工具认识的母版。")
    attrs = dict(re.findall(r'([A-Za-z0-9_-]+)="([^"]*)"', rect.group(1)))
    tile = {k: float(v) for k, v in attrs.items() if k in ("x", "y", "width", "height", "rx")}
    tile["fill"] = attrs["fill"]
    if tile["fill"].startswith("url("):
        raise SystemExit(f"{name}：底板 fill 指向 {tile['fill']}，不是一个色值。")

    view = re.search(r'viewBox="([0-9.]+)\s+([0-9.]+)\s+([0-9.]+)\s+([0-9.]+)"', text)
    if not view:
        raise SystemExit(f"{name}：读不到 viewBox。")
    side = float(view.group(3))

    strokes = []
    for g in re.finditer(r"<g\s+([^>]*)>(.*?)</g>", text, re.S):
        ga = dict(re.findall(r'([A-Za-z0-9_-]+)="([^"]*)"', g.group(1)))
        width = float(ga["stroke-width"])
        if ga.get("stroke-linecap") != "round" or ga.get("stroke-linejoin") != "round":
            raise SystemExit(f"{name}：笔画不是圆头圆角接合，这份母版的形状口径变了。")
        for p in re.finditer(r'<path\s+([^>]+?)/>', g.group(2)):
            pa = dict(re.findall(r'([A-Za-z0-9_-]+)="([^"]*)"', p.group(1)))
            pts = [(float(x), float(y)) for x, y in
                   re.findall(r"([0-9.]+)\s+([0-9.]+)", pa["d"].replace("M", " ").replace("L", " "))]
            strokes.append({"width": width, "color": pa["stroke"], "points": pts})
    if not strokes:
        raise SystemExit(f"{name}：一笔 stroke 都没读到。")
    return side, tile, strokes


def render(svg_path, size):
    text = open(svg_path, encoding="utf-8").read()
    name = os.path.basename(svg_path)
    view, tile, strokes = parse_svg(text, name)
    scale = size / view
    canvas = size * SS

    img = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    box = [tile["x"] * scale * SS, tile["y"] * scale * SS,
           (tile["x"] + tile["width"]) * scale * SS, (tile["y"] + tile["height"]) * scale * SS]
    d.rounded_rectangle(box, radius=tile["rx"] * scale * SS, fill=tile["fill"] + "FF")

    for s in strokes:
        w = s["width"] * scale * SS
        pts = [(x * scale * SS, y * scale * SS) for x, y in s["points"]]
        color = s["color"] + "FF"
        d.line(pts, fill=color, width=max(1, round(w)), joint="curve")
        r = w / 2.0
        for cx, cy in (pts[0], pts[-1]):
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color)

    return img.resize((size, size), Image.LANCZOS)


def bmp_entry(img):
    size = img.width
    flipped = img.convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM)
    r, g, b, a = flipped.split()
    px = Image.merge("RGBA", (b, g, r, a)).tobytes()  # DIB order is B,G,R,A
    mask_rows = bytearray()
    stride = ((size + 31) // 32) * 4
    for y in range(size):
        row = bytearray(stride)
        for x in range(size):
            if a.getpixel((x, y)) == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask_rows += row
    hdr = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0,
                      len(px) + len(mask_rows), 0, 0, 0, 0)
    return hdr + bytes(px) + bytes(mask_rows)


def png_entry(img):
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def build(out_path):
    images = {}
    for size, master, _ in PLAN:
        img = render(master, size)
        assert img.size == (size, size) and img.mode == "RGBA"
        images[size] = img

    blobs = [(size, png_entry(images[size]) if kind == "png" else bmp_entry(images[size]))
             for size, _, kind in PLAN]

    offset = 6 + 16 * len(blobs)
    directory = payload = b""
    for size, blob in blobs:
        dim = 0 if size == 256 else size          # 256 does not fit in one byte
        directory += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        payload += blob
        offset += len(blob)

    data = struct.pack("<HHH", 0, 1, len(blobs)) + directory + payload
    with open(out_path, "wb") as f:
        f.write(data)
    print(f"wrote {out_path}: {len(blobs)} entries, {len(data)} bytes, "
          f"sha256 {hashlib.sha256(data).hexdigest()[:12]}")
    for size, blob in blobs:
        print(f"  {size}x{size}: {len(blob)} bytes  ({os.path.basename(dict((s, m) for s, m, _ in PLAN)[size])})")


if __name__ == "__main__":
    build(sys.argv[1] if len(sys.argv) > 1 else SHIPPED)
