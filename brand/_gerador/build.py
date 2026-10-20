"""Gera todos os assets v2 do ClientSSH (logo, PNGs, MSIX, .ico, Lottie).
uso: python3 build.py <saida>"""
import json, os, sys, math
from mark import *

OUT = sys.argv[1]
for d in ["logo", "png", "msix", "ico", "anim"]:
    os.makedirs(f"{OUT}/{d}", exist_ok=True)


def save(p, t):
    with open(f"{OUT}/{p}", "w") as f:
        f.write(t)


# ---------- SVGs ----------
svgs = {
    "logo/clientssh-icon.svg": icon_svg("dark"),
    "logo/clientssh-icon-light.svg": icon_svg("light"),
    "logo/clientssh-icon-black.svg": icon_svg("black"),
    "logo/clientssh-icon-small.svg": icon_svg("dark", small=True),
    "logo/clientssh-mark-blue.svg": mark_svg(BLUE),
    "logo/clientssh-mark-black.svg": mark_svg(BLACK),
    "logo/clientssh-mark-white.svg": mark_svg(WHITE),
    "logo/clientssh-mark-mono.svg": mark_svg(None, current=True),
    "logo/clientssh-wordmark.svg": wordmark_svg("light"),
    "logo/clientssh-wordmark-on-dark.svg": wordmark_svg("dark"),
    "logo/clientssh-wordmark-tagline.svg": wordmark_svg("light", tagline=True),
    "logo/clientssh-lockup-stacked.svg": stacked_svg("light"),
    "logo/clientssh-lockup-stacked-on-dark.svg": stacked_svg("dark"),
    "logo/social-preview.svg": social_svg(),
    "logo/favicon.svg": icon_svg("dark", 32, small=True, pad_frac=0),
}
for p, t in svgs.items():
    save(p, t)


def dims(s):
    return int(float(s.split('width="')[1].split('"')[0])), int(float(s.split('height="')[1].split('"')[0]))


# ---------- PNGs ----------
for sz in [16, 24, 32, 48, 64, 128, 256, 512, 1024]:
    for th, nm in [("dark", "icon"), ("light", "icon-light"), ("black", "icon-black")]:
        png(icon_svg(th, small=sz <= 32), sz).save(f"{OUT}/png/{nm}-{sz}.png")
for n in ["clientssh-wordmark", "clientssh-wordmark-on-dark", "clientssh-wordmark-tagline",
          "clientssh-lockup-stacked", "clientssh-lockup-stacked-on-dark"]:
    s = svgs[f"logo/{n}.svg"]; W, H = dims(s)
    for k in (1, 2):
        png(s, W * k, H * k).save(f"{OUT}/png/{n}@{k}x.png")
png(svgs["logo/social-preview.svg"], 1280, 640).convert("RGB").save(f"{OUT}/png/social-preview-1280x640.png")

# ---------- MSIX ----------
ICON, ICON_S, GLYPH = icon_svg("dark"), icon_svg("dark", small=True), icon_svg("dark", plate=False)
for s in [100, 125, 150, 200, 400]:
    q = lambda b: round(b * s / 100)
    png(ICON_S if q(44) < 60 else ICON, q(44)).save(f"{OUT}/msix/Square44x44Logo.scale-{s}.png")
    png(ICON_S, q(24)).save(f"{OUT}/msix/LockScreenLogo.scale-{s}.png")
    png(ICON_S if q(50) < 60 else ICON, q(50)).save(f"{OUT}/msix/StoreLogo.scale-{s}.png")
    for base, frac in [(71, .62), (150, .5), (310, .5)]:
        compose(q(base), q(base), ICON, round(q(base) * frac)).save(f"{OUT}/msix/Square{base}x{base}Logo.scale-{s}.png")
    compose(q(310), q(150), ICON, round(q(150) * .55)).save(f"{OUT}/msix/Wide310x150Logo.scale-{s}.png")
    compose(q(620), q(300), GLYPH, round(q(300) * .6)).save(f"{OUT}/msix/SplashScreen.scale-{s}.png")
for t in [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256]:
    im = png(ICON_S if t <= 32 else ICON, t)
    im.save(f"{OUT}/msix/Square44x44Logo.targetsize-{t}.png")
    im.save(f"{OUT}/msix/Square44x44Logo.targetsize-{t}_altform-unplated.png")
    png(icon_svg("light", small=t <= 32), t).save(f"{OUT}/msix/Square44x44Logo.targetsize-{t}_altform-lightunplated.png")

# ---------- ICO ----------
zs = [16, 20, 24, 32, 40, 48, 64, 128, 256]
ims = [png(ICON_S if z <= 32 else ICON, z) for z in zs]
ims[-1].save(f"{OUT}/ico/ClientSSH.ico", format="ICO", sizes=[(z, z) for z in zs], append_images=ims[:-1])
ims = [png(icon_svg("dark", small=True, pad_frac=0), z) for z in [16, 32, 48]]
ims[-1].save(f"{OUT}/ico/favicon.ico", format="ICO", sizes=[(16, 16), (32, 32), (48, 48)], append_images=ims[:-1])

# =====================================================================
#                              LOTTIE
# =====================================================================
def rgb(h):
    h = h.lstrip("#"); return [round(int(h[i:i + 2], 16) / 255, 4) for i in (0, 2, 4)] + [1]

EASE, LIN, OUTB = (0.33, 0, 0.2, 1), (0, 0, 1, 1), (0.3, 0, 0.3, 1.4)
def st(v): return {"a": 0, "k": v}
def an(keys):
    out = []
    for i, k in enumerate(keys):
        t, v = k[0], k[1]; e = k[2] if len(k) > 2 else EASE
        kf = {"t": t, "s": v if isinstance(v, list) else [v]}
        if i < len(keys) - 1:
            if e == "hold": kf["h"] = 1
            else: kf["o"] = {"x": e[0], "y": e[1]}; kf["i"] = {"x": e[2], "y": e[3]}
        out.append(kf)
    return {"a": 1, "k": out}
W_ = lambda v: v if isinstance(v, dict) else st(v)

def path(v, closed=False, i=None, o=None):
    n = len(v)
    return {"ty": "sh", "ks": st({"i": i or [[0, 0]] * n, "o": o or [[0, 0]] * n, "v": v, "c": closed})}
def ell(d, p=(0, 0)): return {"ty": "el", "p": st(list(p)), "s": st([d, d]), "d": 1}
def rect(w, h, r, p=(0, 0)): return {"ty": "rc", "p": W_(list(p)) if not isinstance(p, dict) else p, "s": W_([w, h]) if not isinstance(w, dict) else w, "r": st(r), "d": 1}
def stroke(c, w, o=100, dash=None):
    s = {"ty": "st", "c": st(rgb(c)), "o": W_(o), "w": W_(w), "lc": 2, "lj": 2, "ml": 4}
    if dash: s["d"] = dash
    return s
def fill(c, o=100): return {"ty": "fl", "c": st(rgb(c)), "o": W_(o), "r": 1}
def gfill(c1, c2, a, b):
    return {"ty": "gf", "o": st(100), "r": 1, "t": 1, "s": st(list(a)), "e": st(list(b)),
            "g": {"p": 2, "k": st([0] + rgb(c1)[:3] + [1] + rgb(c2)[:3])}}
def trim(s=0, e=100, o=0): return {"ty": "tm", "s": W_(s), "e": W_(e), "o": W_(o), "m": 1}
def tr(p=(0, 0), r=0, s=100, o=100):
    return {"ty": "tr", "p": W_(list(p)) if not isinstance(p, dict) else p, "a": st([0, 0]),
            "s": W_([s, s]) if not isinstance(s, dict) else s, "r": W_(r), "o": W_(o), "sk": st(0), "sa": st(0)}
def grp(items, **t): return {"ty": "gr", "nm": "g", "it": items + [tr(**t)]}

class Comp:
    def __init__(self, nm, op, loop=True):
        self.d = {"v": "5.7.4", "fr": 60, "ip": 0, "op": op, "w": 240, "h": 240, "nm": nm, "ddd": 0,
                  "assets": [], "layers": [], "markers": []}
        self.loop = loop
    def layer(self, nm, shapes, p=(120, 120), o=100, r=0, s=100):
        ks = {"o": W_(o), "r": W_(r), "a": st([0, 0, 0]),
              "p": p if isinstance(p, dict) else st([p[0], p[1], 0]),
              "s": s if isinstance(s, dict) else st([s, s, 100])}
        if isinstance(s, dict):
            for kf in s["k"]: kf["s"] = [kf["s"][0], kf["s"][0], 100]
        self.d["layers"].insert(0, {"ddd": 0, "ty": 4, "nm": nm, "sr": 1, "ks": ks, "ao": 0, "shapes": shapes,
                                    "ip": 0, "op": self.d["op"], "st": 0, "bm": 0})
        return self
    def save(self, name):
        for i, l in enumerate(self.d["layers"]): l["ind"] = i + 1
        save(f"anim/{name}.json", json.dumps(self.d, separators=(",", ":")))
        META[name.replace(".dark", "")] = self.loop

META = {}
K = 0.5523

def arc_shape(r, a0, a1):
    """Arco (graus, 0 = direita, sentido horário na tela) aproximado por cúbicas."""
    n = max(1, math.ceil(abs(a1 - a0) / 90)); da = (a1 - a0) / n
    v, ii, oo = [], [], []
    h = 4 / 3 * math.tan(math.radians(da) / 4) * r
    for j in range(n + 1):
        a = math.radians(a0 + da * j)
        x, y = r * math.cos(a), r * math.sin(a)
        tx, ty = -math.sin(a), math.cos(a)
        v.append([round(x, 2), round(y, 2)])
        ii.append([round(-tx * h, 2), round(-ty * h, 2)])
        oo.append([round(tx * h, 2), round(ty * h, 2)])
    return path(v, i=ii, o=oo)

# geometria da marca no canvas 240
G = 7.0
def mp(x, y, ox=11.1): return [round((x - ox) * G, 2), round((y - 12) * G, 2)]
MR = R * G
MC = mp(12, 12)            # centro do anel, relativo ao centro do canvas
CHEV_P = [mp(8.5, 9.25), mp(11.25, 12), mp(8.5, 14.75)]
CUR_P = [mp(13.25, 14.75), mp(16.25, 14.75)]
MSW = 2 * G


def build(theme):
    dark = theme == "dark"
    FG = WHITE if dark else BLACK
    AC = WHITE if dark else BLUE
    SOFT = 30 if dark else 14  # opacidade de elementos de fundo
    sfx = ".dark" if dark else ""

    def ring_group(extra=None, o=100):
        return grp([arc_shape(MR, GAP, 360 - GAP), stroke(AC, MSW, o)] + (extra or []), p=MC)

    # --- marca: hover ---
    c = Comp("logo-hover", 60)
    c.layer("ring", [ring_group()], r=an([(0, 0), (18, -14), (42, 0, OUTB), (60, 0)]))
    c.layer("chev", [grp([path(CHEV_P), stroke(AC, MSW)])], p=an([(0, [120, 120]), (18, [130, 120]), (42, [120, 120], OUTB), (60, [120, 120])]))
    c.layer("cursor", [grp([path(CUR_P), stroke(AC, MSW), trim(0, an([(0, 100), (14, 30), (40, 100, OUTB), (60, 100)]))])])
    c.save("logo-hover" + sfx)

    # --- carregando com a marca: anel gira, cursor pisca ---
    c = Comp("loading-logo", 90)
    c.layer("ring", [grp([arc_shape(MR, 0, 360), stroke(AC, MSW, SOFT)], p=MC)])
    c.layer("arc", [grp([arc_shape(MR, 0, 360), stroke(AC, MSW), trim(0, an([(0, 22), (45, 62), (90, 22)]))], p=MC)],
            p=(120 + MC[0], 120 + MC[1]), r=an([(0, 0), (90, 360, LIN)]))
    c.layer("chev", [grp([path(CHEV_P), stroke(AC, MSW)])])
    c.layer("cursor", [grp([path(CUR_P), stroke(AC, MSW)])], o=an([(0, 100, "hold"), (23, 0, "hold"), (45, 100, "hold"), (68, 0, "hold"), (90, 100)]))
    # o grupo do arco já está deslocado; compensa a posição da camada
    c.d["layers"][2]["shapes"][0]["it"][-1]["p"] = st([0, 0])
    c.save("loading-logo" + sfx)

    # --- anel simples ---
    c = Comp("loading-ring", 60)
    c.layer("track", [grp([ell(132), stroke(AC, 12, SOFT)])])
    c.layer("arc", [grp([ell(132), stroke(AC, 12), trim(0, an([(0, 18), (30, 40), (60, 18)]))])], r=an([(0, 0), (60, 360, LIN)]))
    c.save("loading-ring" + sfx)

    # --- pontos ---
    c = Comp("loading-dots", 60)
    for j, x in enumerate([80, 120, 160]):
        d = j * 8
        c.layer(f"dot{j}", [grp([ell(22), fill(AC)])],
                p=an([(0, [x, 120]), (d, [x, 120]), (d + 14, [x, 98]), (d + 28, [x, 120]), (60, [x, 120])]),
                o=an([(0, 40), (d, 40), (d + 14, 100), (d + 28, 40), (60, 40)]))
    c.save("loading-dots" + sfx)

    # --- conectando ---
    c = Comp("connecting", 120)
    c.layer("client", [grp([rect(62, 54, 12), stroke(FG, 7)]),
                       grp([path([[-14, -9], [-5, 0], [-14, 9]]), stroke(AC, 6)]),
                       grp([path([[1, 9], [13, 9]]), stroke(AC, 6)])], p=(50, 120))
    c.layer("server", [grp([rect(54, 62, 10), stroke(FG, 7)])] +
            [grp([path([[-6, y], [12, y]]), stroke(FG, 5)]) for y in (-14, 0, 14)] +
            [grp([ell(5, (-15, y)), fill(AC)]) for y in (-14, 0, 14)], p=(190, 120))
    c.layer("link", [grp([path([[-32, 0], [32, 0]]), stroke(AC, 5, 60, [{"n": "d", "nm": "d", "v": st(1)}, {"n": "g", "nm": "g", "v": st(11)},
                                                                       {"n": "o", "nm": "o", "v": an([(0, 0), (120, -48, LIN)])}])])])
    c.layer("packet", [grp([ell(14), fill(AC)])],
            p=an([(0, [90, 120]), (50, [150, 120]), (60, [150, 120]), (110, [90, 120]), (120, [90, 120])]),
            s=an([(0, 60), (25, 115), (50, 60), (60, 60), (85, 115), (110, 60), (120, 60)]))
    c.save("connecting" + sfx)

    # --- autenticando: cadeado fecha ---
    c = Comp("authenticating", 120)
    shackle = path([[-22, 0], [-22, -18], [0, -40], [22, -18], [22, 0]],
                   i=[[0, 0], [0, 0], [-12.15, 0], [0, -12.15], [0, 0]],
                   o=[[0, 0], [0, -12.15], [12.15, 0], [0, 0], [0, 0]])
    c.layer("shackle", [grp([shackle, stroke(FG, 11)])],
            p=an([(0, [120, 92]), (20, [120, 92]), (40, [120, 112], OUTB), (100, [120, 112]), (120, [120, 92])]))
    c.layer("body", [grp([rect(88, 70, 14), fill(AC)])], p=(120, 140),
            s=an([(0, 100), (38, 100), (46, 104), (56, 100), (120, 100)]))
    hole = WHITE if not dark else BLUE_DEEP
    c.layer("keyhole", [grp([ell(15, (0, -6)), fill(hole)]), grp([path([[0, -2], [0, 12]]), stroke(hole, 7)])], p=(120, 140),
            s=an([(0, 100), (40, 100), (52, 125), (64, 100), (120, 100)]))
    c.save("authenticating" + sfx)

    # --- host key: escudo ---
    sh_v = [[0, -54], [44, -38], [44, 4], [0, 54], [-44, 4], [-44, -38]]
    sh_i = [[0, 0], [0, 0], [0, 0], [26, -12], [0, 28], [0, 0]]
    sh_o = [[0, 0], [0, 0], [0, 28], [-26, -12], [0, 0], [0, 0]]
    c = Comp("host-key-verified", 100, loop=False)
    c.layer("shield-bg", [grp([path(sh_v, True, sh_i, sh_o), fill(AC, an([(0, 0), (30, 0), (45, SOFT), (100, SOFT)]))])])
    c.layer("shield", [grp([path(sh_v, True, sh_i, sh_o), stroke(AC, 10), trim(0, an([(0, 0), (34, 100, (0.5, 0, 0.2, 1)), (100, 100)]))])],
            s=an([(0, 100), (34, 100), (44, 107), (58, 100), (100, 100)]))
    c.layer("check", [grp([path([[-20, 2], [-6, 16], [22, -14]]), stroke(FG, 10), trim(0, an([(0, 0), (34, 0), (56, 100, (0.5, 0, 0.2, 1)), (100, 100)]))])],
            s=an([(0, 100), (34, 100), (44, 107), (58, 100), (100, 100)]))
    c.save("host-key-verified" + sfx)

    c = Comp("host-key-warning", 90)
    c.layer("shield", [grp([path(sh_v, True, sh_i, sh_o), stroke(AC, 10)]), grp([path(sh_v, True, sh_i, sh_o), fill(AC, an([(0, 0), (20, SOFT + 10), (45, 0), (90, 0)]))])],
            s=an([(0, 100), (12, 106), (30, 100), (90, 100)]))
    c.layer("mark", [grp([path([[0, -26], [0, 4]]), stroke(FG, 11)]), grp([ell(12, (0, 22)), fill(FG)])],
            r=an([(0, 0), (8, 0), (12, -10), (16, 10), (20, -8), (24, 6), (28, 0), (90, 0)]))
    c.save("host-key-warning" + sfx)

    # --- conectado ---
    c = Comp("connected", 90, loop=False)
    c.layer("disc", [grp([ell(150), fill(AC, an([(0, 0), (30, 0), (45, SOFT), (90, SOFT)]))])])
    c.layer("ring", [grp([ell(150), stroke(AC, 11), trim(0, an([(0, 0), (30, 100, (0.5, 0, 0.2, 1)), (90, 100)]))])],
            s=an([(0, 100), (30, 100), (40, 107), (54, 100), (90, 100)]))
    c.layer("check", [grp([path([[-32, 2], [-9, 25], [34, -20]]), stroke(FG, 12), trim(0, an([(0, 0), (26, 0), (50, 100, (0.5, 0, 0.2, 1)), (90, 100)]))])],
            s=an([(0, 100), (30, 100), (40, 107), (54, 100), (90, 100)]))
    c.save("connected" + sfx)

    # --- erro ---
    c = Comp("error", 90, loop=False)
    shake = an([(0, [120, 120]), (48, [120, 120]), (52, [108, 120]), (56, [131, 120]), (60, [112, 120]),
                (64, [126, 120]), (68, [117, 120]), (72, [120, 120]), (90, [120, 120])])
    c.layer("disc", [grp([ell(150), fill(AC, an([(0, 0), (30, 0), (45, SOFT), (90, SOFT)]))])], p=shake)
    c.layer("ring", [grp([ell(150), stroke(FG, 11), trim(0, an([(0, 0), (30, 100, (0.5, 0, 0.2, 1)), (90, 100)]))])], p=shake)
    c.layer("x", [grp([path([[-26, -26], [26, 26]]), stroke(AC if not dark else FG, 12), trim(0, an([(0, 0), (24, 0), (38, 100), (90, 100)]))]),
                  grp([path([[26, -26], [-26, 26]]), stroke(AC if not dark else FG, 12), trim(0, an([(0, 0), (34, 0), (48, 100), (90, 100)]))])], p=shake)
    c.save("error" + sfx)

    # --- reconectando: duas setas circulares ---
    def arrow_arc(a0, a1, r=56):
        a = math.radians(a1)
        tip = [r * math.cos(a), r * math.sin(a)]
        t = [-math.sin(a), math.cos(a)]  # tangente no sentido horário
        def rot(v, deg):
            q = math.radians(deg); return [v[0] * math.cos(q) - v[1] * math.sin(q), v[0] * math.sin(q) + v[1] * math.cos(q)]
        b1, b2 = rot([-t[0], -t[1]], 40), rot([-t[0], -t[1]], -40)
        L = 18
        head = [[round(tip[0] + b1[0] * L, 2), round(tip[1] + b1[1] * L, 2)], [round(tip[0], 2), round(tip[1], 2)],
                [round(tip[0] + b2[0] * L, 2), round(tip[1] + b2[1] * L, 2)]]
        return [grp([arc_shape(r, a0, a1), stroke(AC, 11)]), grp([path(head), stroke(AC, 11)])]
    c = Comp("reconnecting", 90)
    c.layer("arrows", arrow_arc(200, 330) + arrow_arc(20, 150), r=an([(0, 0), (70, 360, (0.5, 0, 0.3, 1)), (90, 360)]))
    c.save("reconnecting" + sfx)

    # --- desconectado: o cabo se separa ---
    c = Comp("disconnected", 120)
    left = [grp([path([[-70, 0], [-20, 0]]), stroke(FG, 10)]), grp([rect(26, 34, 7, (-12, 0)), fill(FG)]),
            grp([path([[0, -9], [10, -9]]), stroke(FG, 6)]), grp([path([[0, 9], [10, 9]]), stroke(FG, 6)])]
    right = [grp([path([[20, 0], [70, 0]]), stroke(AC, 10)]), grp([rect(26, 34, 7, (12, 0)), fill(AC)])]
    c.layer("left", left, p=an([(0, [118, 120]), (20, [118, 120]), (40, [98, 120], OUTB), (90, [98, 120]), (115, [118, 120]), (120, [118, 120])]),
            r=an([(0, 0), (20, 0), (40, -8), (90, -8), (115, 0), (120, 0)]))
    c.layer("right", right, p=an([(0, [118, 120]), (20, [118, 120]), (40, [142, 120], OUTB), (90, [142, 120]), (115, [118, 120]), (120, [118, 120])]),
            r=an([(0, 0), (20, 0), (40, 8), (90, 8), (115, 0), (120, 0)]))
    sparks = [grp([path([[0, -30], [0, -44]]), stroke(FG, 6)]), grp([path([[14, -26], [24, -36]]), stroke(FG, 6)]),
              grp([path([[-14, 26], [-24, 36]]), stroke(FG, 6)]), grp([path([[0, 30], [0, 44]]), stroke(FG, 6)])]
    c.layer("sparks", sparks, p=(120, 120), o=an([(0, 0), (34, 0), (42, 100), (80, 100), (90, 0), (120, 0)]),
            s=an([(0, 70), (34, 70), (46, 100), (120, 100)]))
    c.save("disconnected" + sfx)

    # --- sem rede ---
    c = Comp("offline", 120)
    for j, r in enumerate([30, 56, 82]):
        c.layer(f"wave{j}", [grp([arc_shape(r, 225, 315), stroke(AC, 11)])], p=(120, 150),
                o=an([(0, 100), (10 + j * 8, 100), (24 + j * 8, 25), (100, 25), (112, 100), (120, 100)]))
    c.layer("dot", [grp([ell(18), fill(FG)])], p=(120, 150))
    c.layer("slash", [grp([path([[-62, -62], [62, 62]]), stroke(FG, 12), trim(0, an([(0, 0), (40, 0), (60, 100), (100, 100), (112, 0), (120, 0)]))])], p=(120, 110))
    c.save("offline" + sfx)

    # --- upload / download ---
    for nm, up in [("upload", True), ("download", False)]:
        c = Comp(nm, 70)
        c.layer("tray", [grp([path([[-50, -10], [-50, 22], [50, 22], [50, -10]]), stroke(FG, 11)])], p=(120, 150))
        a0, a1 = ((40, -40) if up else (-40, 30))
        arrow = [grp([path([[0, -30], [0, 30]]), stroke(AC, 11)]),
                 grp([path([[-20, -10], [0, -30], [20, -10]] if up else [[-20, 10], [0, 30], [20, 10]]), stroke(AC, 11)])]
        c.layer("arrow", arrow, p=an([(0, [120, 104 + a0]), (40, [120, 104 + a1], (0.4, 0, 0.2, 1)), (70, [120, 104 + a1])]),
                o=an([(0, 0), (12, 100), (40, 100), (60, 0), (70, 0)]))
        c.save(nm + sfx)

    # --- copiado ---
    c = Comp("copied", 100, loop=False)
    c.layer("back", [grp([rect(64, 78, 12), stroke(FG, 7)])], p=(104, 104))
    c.layer("front", [grp([rect(64, 78, 12), fill(WHITE if not dark else BLUE_DEEP)]), grp([rect(64, 78, 12), stroke(AC, 9)])],
            p=an([(0, [104, 104]), (8, [104, 104]), (30, [134, 134], OUTB), (100, [134, 134])]))
    c.layer("check", [grp([path([[-14, 0], [-4, 10], [16, -10]]), stroke(AC, 9), trim(0, an([(0, 0), (30, 0), (50, 100), (100, 100)]))])], p=(134, 134))
    c.save("copied" + sfx)

    # --- salvando: anel de progresso + seta para o disco ---
    c = Comp("saving", 120)
    c.layer("track", [grp([ell(150), stroke(AC, 10, SOFT)])])
    c.layer("progress", [grp([ell(150), stroke(AC, 10), trim(0, an([(0, 0), (80, 100, (0.4, 0, 0.3, 1)), (105, 100), (106, 0, "hold"), (120, 0)]))])])
    c.layer("tray", [grp([path([[-26, 6], [-26, 22], [26, 22], [26, 6]]), stroke(FG, 9)])], p=(120, 120))
    c.layer("arrow", [grp([path([[0, -26], [0, 8]]), stroke(AC, 9)]), grp([path([[-13, -4], [0, 9], [13, -4]]), stroke(AC, 9)])],
            p=an([(0, [120, 110]), (30, [120, 118]), (60, [120, 110]), (90, [120, 118]), (120, [120, 110])]))
    c.save("saving" + sfx)

    # --- cursor do terminal ---
    c = Comp("terminal-cursor", 60)
    c.layer("prompt", [grp([path([[-14, -22], [8, 0], [-14, 22]]), stroke(AC, 13)])], p=(78, 112))
    c.layer("cursor", [grp([path([[-22, 0], [22, 0]]), stroke(FG, 13)])], p=(140, 136),
            o=an([(0, 100, "hold"), (30, 0, "hold"), (60, 100)]))
    c.save("terminal-cursor" + sfx)

    # --- executando comando: linhas aparecem numa janela ---
    c = Comp("running-command", 150)
    c.layer("window", [grp([rect(170, 130, 16), stroke(FG, 8)]), grp([path([[-85, -38], [85, -38]]), stroke(FG, 6)]),
                       grp([ell(8, (-66, -51)), fill(FG)]), grp([ell(8, (-52, -51)), fill(FG)]), grp([ell(8, (-38, -51)), fill(FG)])])
    lines = [(-18, 92, AC, 0), (2, 64, FG, 26), (22, 104, FG, 46), (42, 40, AC, 72)]
    for j, (y, w, col, t0) in enumerate(lines):
        c.layer(f"line{j}", [grp([path([[-64, y], [-64 + w, y]]), stroke(col, 8),
                                  trim(0, an([(0, 0), (t0, 0), (t0 + 18, 100, (0.3, 0, 0.3, 1)), (130, 100), (150, 0)]))])],
                o=an([(0, 100), (126, 100), (146, 0), (150, 0)]))
    c.save("running-command" + sfx)

build("light")
build("dark")

# --- abertura da logo (tile + traço) ---
c = Comp("logo-reveal", 130, loop=False)
TW = 200
c.layer("tile", [grp([rect(TW, TW, TW * .23), gfill(BLUE_TOP, BLUE_DEEP, (-TW / 2, -TW / 2), (TW / 3, TW / 2))])],
        s=an([(0, 40), (26, 106, (0.3, 0, 0.3, 1)), (40, 100), (130, 100)]), o=an([(0, 0), (14, 100), (130, 100)]))
k = 0.82
ring_c = [MC[0] * k, MC[1] * k]
c.layer("ring", [grp([arc_shape(MR * k, GAP, 360 - GAP), stroke(WHITE, MSW * k), trim(an([(0, 50), (28, 50), (64, 0, (0.3, 0, 0.1, 1)), (130, 0)]),
                                                                                   an([(0, 50), (28, 50), (64, 100, (0.3, 0, 0.1, 1)), (130, 100)]))], p=ring_c)])
kk = lambda pts: [[round(x * k, 2), round(y * k, 2)] for x, y in pts]
c.layer("chev", [grp([path(kk(CHEV_P)), stroke(WHITE, MSW * k)])],
        p=an([(0, [100, 120]), (50, [100, 120]), (72, [120, 120], (0.2, 0, 0.1, 1)), (130, [120, 120])]),
        o=an([(0, 0), (50, 0), (60, 100), (130, 100)]))
c.layer("cursor", [grp([path(kk(CUR_P)), stroke(WHITE, MSW * k)])],
        o=an([(0, 0), (70, 0, "hold"), (78, 100, "hold"), (94, 0, "hold"), (110, 100, "hold"), (130, 100)]))
c.save("logo-reveal")
save("anim/_meta.json", json.dumps(META))
print("ok", len(os.listdir(f"{OUT}/anim")))
