"""Vampire Rounds's own art, drawn in code (no game files): the adventure title and the ROUNDS gun's VS icon.

usage: python -I tools/art.py <out dir>
Writes title.rgba / gun_icon.rgba (RawImage format: "VRI1", width, height, RGBA rows bottom-up) and a .png of each
for people to look at. Needs Pillow.
"""
import os, struct, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT = r"C:\Windows\Fonts\ariblk.ttf"


def write_raw(img, path):
    img = img.convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM)
    with open(path, "wb") as fh:
        fh.write(b"VRI1" + struct.pack("<ii", img.width, img.height) + img.tobytes())


def title():
    w, h = 720, 200
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    big = ImageFont.truetype(FONT, 84)
    small = ImageFont.truetype(FONT, 34)
    lines = [("VAMPIRE", big, (200, 24, 40)), ("ROUNDS", big, None)]
    y = 6
    # shadow and outline layer
    shadow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    for text, font, col in lines:
        tw = d.textlength(text, font=font)
        x = (w - tw) / 2
        sd.text((x + 5, y + 6), text, font=font, fill=(0, 0, 0, 200))
        for ox in (-4, 0, 4):
            for oy in (-4, 0, 4):
                sd.text((x + ox, y + oy), text, font=font, fill=(10, 8, 14, 255))
        y += 92
    img = Image.alpha_composite(img, shadow.filter(ImageFilter.GaussianBlur(1)))
    d = ImageDraw.Draw(img)
    y = 6
    for text, font, col in lines:
        tw = d.textlength(text, font=font)
        x = (w - tw) / 2
        if col is not None:
            d.text((x, y), text, font=font, fill=col + (255,))
        else:
            # ROUNDS in its two players' colours: orange then blue
            half = len(text) // 2
            d.text((x, y), text[:half], font=font, fill=(244, 122, 33, 255))
            d.text((x + d.textlength(text[:half], font=font), y), text[half:], font=font, fill=(46, 125, 224, 255))
        y += 92
    return img


def gun_icon():
    s = 8
    img = Image.new("RGBA", (32 * s, 32 * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    def poly(pts, fill):
        d.polygon([(x * s, y * s) for x, y in pts], fill=fill)
    outline = (255, 255, 255, 255)
    body = [(5, 11), (27, 11), (27, 16), (14, 16), (12, 25), (6, 25), (8, 16), (5, 16)]
    # white outline (grown), then the black gun, ROUNDS style
    for ox in (-1, 0, 1):
        for oy in (-1, 0, 1):
            poly([(x + ox * 0.6, y + oy * 0.6) for x, y in body], outline)
    poly(body, (20, 20, 24, 255))
    poly([(21, 12), (26, 12), (26, 13.2), (21, 13.2)], (80, 80, 90, 255))
    d.ellipse([(9 * s, 20 * s), (11 * s, 22 * s)], fill=(244, 122, 33, 255))
    return img.resize((64, 64), Image.LANCZOS)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "art"
    os.makedirs(out, exist_ok=True)
    for name, img in (("title", title()), ("gun_icon", gun_icon())):
        write_raw(img, os.path.join(out, name + ".rgba"))
        img.save(os.path.join(out, name + ".png"))
        print("drew", name, img.size)


if __name__ == "__main__":
    main()
