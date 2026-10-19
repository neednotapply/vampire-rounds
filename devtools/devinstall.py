"""Developer install of the staged build into this PC's games (never used by players: Melty installs the release).

usage: python -I devtools/devinstall.py on|off
on:  turns MelonLoader on in Vampire Survivors (its Melty-switched-off proxy), sets other mashups' mods aside, copies
     dist/stage's Mods and UserData into VS and the Doorstop proxy into ROUNDS (when ROUNDS has none). Every change is
     recorded in ~/vsr-work/devinstall.json.
off: undoes exactly what "on" recorded, so Melty finds the folders as it left them.
"""
import json, os, shutil, sys, filecmp

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGE = os.path.join(ROOT, "dist", "stage")
VS = os.environ.get("VSR_VS_DIR", r"D:\SteamLibrary\steamapps\common\Vampire Survivors")
ROUNDS = os.environ.get("VSR_ROUNDS_DIR", r"D:\SteamLibrary\steamapps\common\ROUNDS")
STATE = os.path.join(os.path.expanduser("~"), "vsr-work", "devinstall.json")
ASIDE = ".vrdev-off"


def load():
    st = json.load(open(STATE)) if os.path.exists(STATE) else {}
    for k in ("renamed", "copied", "created", "backed"):
        st.setdefault(k, [])
    return st


def save(st):
    json.dump(st, open(STATE, "w"), indent=1)


def on():
    st = load()
    ml = os.path.join(VS, "version.dll")
    if not os.path.exists(ml) and os.path.exists(ml + ".melty-off"):
        shutil.copy2(ml + ".melty-off", ml)
        st["created"].append(ml)
        print("MelonLoader on (copied its proxy)")
    for sub in ("Mods", "Plugins"):
        d = os.path.join(VS, sub)
        for f in os.listdir(d) if os.path.isdir(d) else []:
            p = os.path.join(d, f)
            if f.lower().endswith(".dll") and not f.startswith("VampireRounds") and p not in st["renamed"]:
                os.rename(p, p + ASIDE)
                st["renamed"].append(p)
                print("set aside " + p)
    for top in ("Mods", "Plugins", "UserData"):
        src = os.path.join(STAGE, top)
        for d, _, fs in os.walk(src):
            for f in fs:
                s = os.path.join(d, f)
                t = os.path.join(VS, top, os.path.relpath(s, src))
                os.makedirs(os.path.dirname(t), exist_ok=True)
                if not os.path.exists(t) and t not in st["created"]:
                    st["created"].append(t)
                elif os.path.exists(t) and t not in st["created"] and t not in st["backed"]:
                    shutil.copy2(t, t + ".vrdev-bak")  # someone else's copy (Melty's install): put back on "off"
                    st["backed"].append(t)
                shutil.copy2(s, t)
    proxy = os.path.join(ROUNDS, "version.dll")
    ours = os.path.join(STAGE, "proxy-rounds", "version.dll")
    if os.path.exists(proxy):
        print("ROUNDS proxy present (" + ("identical" if filecmp.cmp(proxy, ours, shallow=False) else "DIFFERENT") + ")")
    else:
        shutil.copy2(ours, proxy)
        st["created"].append(proxy)
        print("ROUNDS proxy placed")
    save(st)
    print("dev install on")


def off():
    st = load()
    for p in reversed(st["created"]):
        if os.path.isfile(p):
            os.remove(p)
    for p in st["renamed"]:
        if os.path.exists(p + ASIDE):
            os.rename(p + ASIDE, p)
    for p in st["backed"]:
        if os.path.exists(p + ".vrdev-bak"):
            shutil.copy2(p + ".vrdev-bak", p)
            os.remove(p + ".vrdev-bak")
    d = os.path.join(VS, "UserData", "VampireRounds")
    if os.path.isdir(d) and not any(fs for _, _, fs in os.walk(d)):
        shutil.rmtree(d)
    os.remove(STATE) if os.path.exists(STATE) else None
    print("dev install off: %d files removed, %d set back" % (len(st["created"]), len(st["renamed"])))


if __name__ == "__main__":
    {"on": on, "off": off}[sys.argv[1] if len(sys.argv) > 1 else ""]()
