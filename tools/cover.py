"""Vampire Rounds's cover art (tools/cover.html), painted in code: no file of either game.

usage: python -I tools/cover.py [out dir]        render with headless Microsoft Edge (default out: media/art)
       python -I tools/cover.py --raw <out dir>  the mod's adventure_bg.rgba from media/art/adventure_bg.png
The render writes card.png (Melty's game card, 1920x1080) and adventure_bg.png (the adventure's menu backdrop in VS,
640x360 pixel art shown 3x); it needs Edge, Pillow and the internet (Google Fonts: Cinzel and Lilita One). Run it
from PowerShell or cmd: a sandboxed shell may stop Edge. --raw needs only Pillow (tools/package.py runs it).
"""
import base64, io, os, re, subprocess, sys, tempfile
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
BG_SIZE = (640, 360)
EDGE = [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"]
sys.path.insert(0, str(ROOT / "tools"))
from art import write_raw  # noqa: E402


def render(scenes):
    edge = next(p for p in EDGE if os.path.exists(p))
    url = (ROOT / "tools" / "cover.html").as_uri() + "?out=" + ",".join(scenes)
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as prof:
        dom = subprocess.run([edge, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
                              "--user-data-dir=" + prof, "--virtual-time-budget=60000", "--dump-dom", url],
                             capture_output=True, text=True, timeout=600).stdout
    status = re.search(r'<pre id="status">(.*?)</pre>', dom, re.S)
    print("cover.html:", status.group(1) if status else "(no status)")
    out = {}
    for name in scenes:
        m = re.search(r'<pre id="png-%s">data:image/png;base64,([^<]+)</pre>' % name, dom)
        if not m:
            sys.exit("no picture for " + name)
        out[name] = Image.open(io.BytesIO(base64.b64decode(m.group(1)))).convert("RGBA")
    return out


def raw_backdrop(out):
    """The backdrop's own pixels (the PNG is them shown 3x, nearest neighbour), in the mod's RawImage format."""
    big = Image.open(ROOT / "media" / "art" / "adventure_bg.png").convert("RGBA")
    write_raw(big.resize(BG_SIZE, Image.NEAREST), Path(out) / "adventure_bg.rgba")


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "--raw":
        raw_backdrop(sys.argv[2])
        return
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "media" / "art"
    out.mkdir(parents=True, exist_ok=True)
    pics = render(["card", "bg"])
    pics["card"].convert("RGB").save(out / "card.png")
    # VS's own menu backdrops are pixel art at about a third of the screen: the mod shows this one point-filtered.
    small = pics["bg"].resize(BG_SIZE, Image.LANCZOS)
    small.resize((BG_SIZE[0] * 3, BG_SIZE[1] * 3), Image.NEAREST).convert("RGB").save(out / "adventure_bg.png")
    for f in ("card.png", "adventure_bg.png"):
        print("wrote", out / f)


if __name__ == "__main__":
    main()
