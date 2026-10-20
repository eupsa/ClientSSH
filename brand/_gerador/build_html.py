import json, os, base64, glob, sys, re
from PIL import Image
A = sys.argv[1]
D = {"svg": {}, "anim": {}, "bin": {}, "msix": [], "meta": json.load(open(f"{A}/anim/_meta.json"))}
for p in glob.glob(f"{A}/logo/**/*.svg", recursive=True):
    D["svg"][os.path.relpath(p, A)] = open(p).read()
for p in glob.glob(f"{A}/anim/*.json"):
    if "_meta" in p: continue
    D["anim"][os.path.relpath(p, A)] = json.load(open(p))
def b64(p, mime="image/png"): return f"data:{mime};base64," + base64.b64encode(open(p, "rb").read()).decode()
for p in glob.glob(f"{A}/msix/*.png") + [f"{A}/png/social-preview-1280x640.png", f"{A}/png/icon-48.png"]:
    D["bin"][os.path.relpath(p, A)] = b64(p)
order = ["Square44x44Logo", "Square71x71Logo", "Square150x150Logo", "Square310x310Logo", "Wide310x150Logo", "StoreLogo", "SplashScreen", "LockScreenLogo"]
def key(f):
    g = f.split(".")[0]; n = [int(x) for x in re.findall(r"\d+", f.split(".", 1)[1])]
    return (order.index(g), "targetsize" in f, n, f)
for f in sorted((os.path.basename(p) for p in glob.glob(f"{A}/msix/*.png")), key=key):
    w, h = Image.open(f"{A}/msix/{f}").size
    D["msix"].append([f, w, h])
js = json.dumps(D, separators=(",", ":")).replace("</", "<\\/")
t = open(os.path.join(os.path.dirname(__file__), "template.html")).read().replace("__DATA__", js)
lot = open(os.path.join(os.path.dirname(__file__), "..", "vendor/package/build/player/lottie.min.js")).read().replace("</script", "<\\/script")
ti = t.replace('<script src="https://cdnjs.cloudflare.com/ajax/libs/lottie-web/5.12.2/lottie.min.js"></script>', "<script>" + lot + "</script>")
assert ti != t
t, tart = ti, t
open(f"{A}/index.html", "w").write('<!doctype html>\n<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">\n' + t.replace("<nav class=\"top\">", "</head><body>\n<nav class=\"top\">", 1) + "\n</body></html>")
open(os.path.join(os.path.dirname(__file__), "..", "brandkit.html"), "w").write(tart)
print(len(t) / 1e6, "MB")
