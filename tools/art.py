"""Vampire Rounds's own art, drawn in code (no game files): the adventure title and the ROUNDS gun's VS icon (16 px).

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
    """ROUNDS's pistol as a VS weapon icon: VS sizes weapon icons by their pixels (3.2 UI units a pixel in character
    select; its own are 16 px, the Whip's too), so it is drawn at 16 px, pixel by pixel, black with a white outline."""
    n = 16
    body = Image.new("1", (n, n), 0)
    d = ImageDraw.Draw(body)
    # the 32-unit drawing of the gun, scaled into the 16-px square and centred
    pts = [(5, 11), (27, 11), (27, 16), (14, 16), (12, 25), (6, 25), (8, 16), (5, 16)]
    d.polygon([((x - 5) * 0.56 + 1.5, (y - 11) * 0.56 + 4.1) for x, y in pts], fill=1)
    m = body.load()
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    px = img.load()
    for y in range(n):
        for x in range(n):
            if m[x, y]:
                px[x, y] = (20, 20, 24, 255)
            elif any(0 <= x + dx < n and 0 <= y + dy < n and m[x + dx, y + dy] for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                px[x, y] = (255, 255, 255, 255)
    for x in range(9, 13):   # the slide's highlight
        if m[x, 5]:
            px[x, 5] = (80, 80, 90, 255)
    px[4, 9] = (244, 122, 33, 255)   # ROUNDS's orange dot on the grip
    return img


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "art"
    os.makedirs(out, exist_ok=True)
    for name, img in (("title", title()), ("gun_icon", gun_icon())):
        write_raw(img, os.path.join(out, name + ".rgba"))
        img.save(os.path.join(out, name + ".png"))
        print("drew", name, img.size)


if __name__ == "__main__":
    main()
