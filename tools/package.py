"""Build Vampire Rounds and write dist/VampireRounds-<version>.zip, the package Melty installs.

usage: python -I tools/package.py <version>
Runs the sheet preflight (must be clean), builds src/Engine and src/VsMod, stages every file listed in
sheets/components.json and checks the zip against it. Needs: the .NET SDK, Pillow (for tools/art.py), and
BepInEx_win_x64_5.4.23.2.zip unpacked in ~/vsr-work/dl/bepinex with the license texts in ~/vsr-work/dl/licenses
(VSR_WORK sets another work folder). Nothing from either game goes into the package.
"""
import os, shutil, subprocess, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORK = os.environ.get("VSR_WORK", os.path.join(os.path.expanduser("~"), "vsr-work"))
DL = os.path.join(WORK, "dl")
STAGE = os.path.join(ROOT, "dist", "stage")
PY = sys.executable

BEPINEX_CFG = """[Logging.Console]
Enabled = false

[Logging.Disk]
Enabled = true
WriteUnityLog = true
"""

README = """Vampire Rounds {version}
=====================

A Vampire Survivors adventure where ROUNDS's Orange and Blue fight with ROUNDS's gun, and every level-up and treasure
chest is a ROUNDS card pick. Made for Melty (melty.gg): Melty installs it and starts it with one press of Play.

Needs your own Vampire Survivors and your own ROUNDS (on its "old-rounds-for-mods" Steam beta branch), both on Steam.
Your ROUNDS runs hidden in the background while you play: it draws Orange and Blue, runs their gun, cards and block,
and shows its card pick. Nothing from either game is in this package.

Playing: open Adventures in Vampire Survivors and pick Vampire Rounds. Choose Orange (or Blue). The gun aims at the
nearest enemy and fires by itself; move the mouse to aim it yourself. Right mouse or Space blocks (gamepad: left
trigger). A second player on a gamepad can join as the other colour on the same PC.

Files: Mods/VampireRounds.dll (the Vampire Survivors mod, MelonLoader), Plugins/VampireRounds.InteropFix.dll (lets
MelonLoader mods run on Vampire Survivors's Unity 6 build), UserData/VampireRounds/rounds (BepInEx 5 and
the ROUNDS engine, which ROUNDS loads from there), and version.dll in the ROUNDS folder (UnityDoorstop, which loads the
engine only when Vampire Rounds starts ROUNDS). Logs: MelonLoader/Latest.log in the Vampire Survivors folder and
UserData/VampireRounds/rounds/BepInEx/LogOutput.log.

License: MIT (see licenses/Vampire-Rounds-LICENSE.txt). The ROUNDS engine is adapted from Roundhead (MIT, the same
creator). Third-party: BepInEx 5.4.23.2 (MIT), UnityDoorstop 4 (LGPL-2.1), HarmonyX (MIT), MonoMod (MIT),
Mono.Cecil (MIT); their license texts are in licenses/.
"""


def run(args, cwd=ROOT):
    print("> " + " ".join(args))
    subprocess.check_call(args, cwd=cwd)


def copy(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    version = sys.argv[1]
    run([PY, "-I", os.path.join(ROOT, "tools", "sheets.py"), "gen"])
    if subprocess.call([PY, "-I", os.path.join(ROOT, "tools", "sheets.py"), "preflight", "--code"], cwd=ROOT) != 0:
        sys.exit("preflight is not clean: fix the sheets first")
    run(["dotnet", "build", "-c", "Release", "-nologo", "-v", "q", "-p:Version=" + version], cwd=os.path.join(ROOT, "src", "Engine"))
    run(["dotnet", "build", "-c", "Release", "-nologo", "-v", "q", "-p:Version=" + version], cwd=os.path.join(ROOT, "src", "VsMod"))
    run(["dotnet", "build", "-c", "Release", "-nologo", "-v", "q", "-p:Version=" + version], cwd=os.path.join(ROOT, "src", "InteropFix"))
    if os.path.exists(STAGE):
        shutil.rmtree(STAGE)
    ud = os.path.join(STAGE, "UserData", "VampireRounds")
    copy(os.path.join(ROOT, "src", "VsMod", "bin", "Release", "net6.0", "VampireRounds.dll"), os.path.join(STAGE, "Mods", "VampireRounds.dll"))
    copy(os.path.join(ROOT, "src", "InteropFix", "bin", "Release", "net6.0", "VampireRounds.InteropFix.dll"), os.path.join(STAGE, "Plugins", "VampireRounds.InteropFix.dll"))
    copy(os.path.join(ROOT, "src", "Engine", "bin", "Release", "net472", "VampireRounds.Engine.dll"),
         os.path.join(ud, "rounds", "BepInEx", "plugins", "VampireRounds.Engine", "VampireRounds.Engine.dll"))
    core = os.path.join(DL, "bepinex", "BepInEx", "core")
    for f in sorted(os.listdir(core)):
        copy(os.path.join(core, f), os.path.join(ud, "rounds", "BepInEx", "core", f))
    # No BepInEx.cfg: BepInEx writes its own (defaults: no console, a disk log) beside the plugins at its first start.
    copy(os.path.join(DL, "bepinex", "winhttp.dll"), os.path.join(STAGE, "proxy-rounds", "version.dll"))
    run([PY, "-I", os.path.join(ROOT, "tools", "art.py"), os.path.join(ud, "art")])
    for f in os.listdir(os.path.join(ud, "art")):
        if f.endswith(".png"):
            os.remove(os.path.join(ud, "art", f))  # people look at media/art; the mod reads .rgba
    lic = os.path.join(DL, "licenses")
    for f in ("BepInEx-LICENSE.txt", "UnityDoorstop-LICENSE.txt", "HarmonyX-LICENSE.txt", "MonoMod-LICENSE.txt", "Mono.Cecil-LICENSE.txt"):
        copy(os.path.join(lic, f), os.path.join(ud, "licenses", f))
    copy(os.path.join(ROOT, "LICENSE.txt"), os.path.join(ud, "licenses", "Vampire-Rounds-LICENSE.txt"))
    with open(os.path.join(ud, "README.txt"), "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write(README.format(version=version))
    out = os.path.join(ROOT, "dist", "VampireRounds-%s.zip" % version)
    if os.path.exists(out):
        os.remove(out)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for d, _, fs in os.walk(STAGE):
            for f in sorted(fs):
                p = os.path.join(d, f)
                z.write(p, os.path.relpath(p, STAGE).replace(os.sep, "/"))
    if subprocess.call([PY, "-I", os.path.join(ROOT, "tools", "sheets.py"), "preflight", "--code", "--package", out], cwd=ROOT) != 0:
        sys.exit("the package does not match sheets/components.json")
    print("wrote " + out + " (%d bytes)" % os.path.getsize(out))


if __name__ == "__main__":
    main()
