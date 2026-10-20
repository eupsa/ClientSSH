"""Marca v2 do ClientSSH: monograma "C" aberto com prompt >_ dentro. Só azul-escuro, preto e branco."""
import math, io
import cairosvg
from PIL import Image
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

# ---------- paleta ----------
BLUE = "#1E3A8A"      # azul-escuro principal
BLUE_DEEP = "#0F1F57" # azul-noite (fim do gradiente)
BLUE_TOP = "#2747A6"  # topo do gradiente do tile
BLACK = "#05070D"
WHITE = "#FFFFFF"

# ---------- geometria na grade 24 ----------
R = 8.25
GAP = 42  # graus de abertura de cada lado do eixo horizontal direito


def arc_pts():
    a = math.radians(GAP)
    p1 = (12 + R * math.cos(a), 12 - R * math.sin(a))
    p2 = (12 + R * math.cos(a), 12 + R * math.sin(a))
    return p1, p2


P1, P2 = arc_pts()
RING = f"M{P1[0]:.3f} {P1[1]:.3f}A{R} {R} 0 1 0 {P2[0]:.3f} {P2[1]:.3f}"
CHEV = "M8.5 9.25 11.25 12 8.5 14.75"
CURSOR = "M13.25 14.75h3"


def mark_g(ring, prompt, sw, s, cx, cy, cursor=None, ox=12):
    cursor = cursor or prompt
    return (f'<g transform="translate({cx} {cy}) scale({s}) translate(-{ox} -12)" fill="none" '
            f'stroke-linecap="round" stroke-linejoin="round" stroke-width="{sw}">'
            f'<path d="{RING}" stroke="{ring}"/><path d="{CHEV}" stroke="{prompt}"/>'
            f'<path d="{CURSOR}" stroke="{cursor}"/></g>')


def icon_svg(theme="dark", size=256, small=False, pad_frac=1 / 16, plate=True):
    V = 256
    pad = V * pad_frac
    w = V - 2 * pad
    rx = w * 0.23
    sw = 2.5 if small else 2.0
    s = 8.4 if small else 7.6
    if theme == "dark":
        defs = (f'<linearGradient id="bg" x1="0" y1="0" x2=".6" y2="1"><stop offset="0" stop-color="{BLUE_TOP}"/>'
                f'<stop offset="1" stop-color="{BLUE_DEEP}"/></linearGradient>')
        fill, edge, fg = "url(#bg)", f'stroke="{WHITE}" stroke-opacity=".12"', WHITE
    elif theme == "black":
        defs = ""
        fill, edge, fg = BLACK, f'stroke="{WHITE}" stroke-opacity=".10"', WHITE
    else:
        defs = ""
        fill, edge, fg = WHITE, f'stroke="{BLUE}" stroke-opacity=".14"', BLUE
    body = ""
    if plate:
        body += (f'<rect x="{pad}" y="{pad}" width="{w}" height="{w}" rx="{rx}" fill="{fill}"/>'
                 f'<rect x="{pad+1}" y="{pad+1}" width="{w-2}" height="{w-2}" rx="{rx-1}" fill="none" {edge} stroke-width="2"/>')
    body += mark_g(fg, fg, sw, s, 128, 128, ox=11.1)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {V} {V}">'
            f'<defs>{defs}</defs>{body}</svg>')


def mark_svg(color, sw=2, size=24, current=False):
    c = "currentColor" if current else color
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 24 24">'
            f'{mark_g(c, c, sw, 1, 12, 12)}</svg>')


# ---------- texto em curvas ----------
INTER_B = TTFont("/usr/share/fonts/opentype/inter/Inter-Bold.otf")
INTER_M = TTFont("/usr/share/fonts/opentype/inter/Inter-Medium.otf")
INTER_SB = TTFont("/usr/share/fonts/opentype/inter/Inter-SemiBold.otf")
MONO = TTFont("/usr/share/fonts/truetype/jetbrains-mono/JetBrainsMono-Medium.ttf")


def text_path(font, text, size, x0, base, track=0.0):
    gs = font.getGlyphSet(); cmap = font.getBestCmap(); k = size / font["head"].unitsPerEm
    ds, x = [], x0
    for ch in text:
        gn = cmap[ord(ch)]
        pen = SVGPathPen(gs)
        gs[gn].draw(TransformPen(pen, (k, 0, 0, -k, x, base)))
        ds.append(pen.getCommands())
        x += gs[gn].width * k + track * size
    return " ".join(ds), x


def inner(svg):
    return svg.split(">", 1)[1].rsplit("</svg>", 1)[0]


def word(x0, base, size, c1, c2):
    d1, x = text_path(INTER_SB, "Client", size, x0, base, -0.02)
    d2, x = text_path(INTER_B, "SSH", size, x + size * 0.02, base, 0.02)
    return f'<path d="{d1}" fill="{c1}"/><path d="{d2}" fill="{c2}"/>', x


def wordmark_svg(theme="light", tagline=False):
    c1, c2 = (BLACK, BLUE) if theme == "light" else (WHITE, WHITE)
    ic = icon_svg("dark" if theme == "light" else "light", 256)
    t, x = word(150, 84, 62, c1, c2)
    W, H = int(x + 10), 128
    extra = ""
    if tagline:
        d3, x3 = text_path(MONO, "open source · sem nuvem · sem telemetria", 15, 153, 114, 0)
        extra = f'<path d="{d3}" fill="{c1}" fill-opacity=".6"/>'
        W, H = int(max(W, x3 + 10)), 128
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">'
            f'<svg width="128" height="128" viewBox="0 0 256 256">{inner(ic)}</svg>{t}{extra}</svg>')


def stacked_svg(theme="light"):
    c1, c2 = (BLACK, BLUE) if theme == "light" else (WHITE, WHITE)
    ic = icon_svg("dark" if theme == "light" else "light", 256)
    _, tw = word(0, 0, 54, c1, c2)
    W = int(max(tw, 200) + 40)
    t, _ = word((W - tw) / 2, 236, 54, c1, c2)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="256" viewBox="0 0 {W} 256">'
            f'<svg x="{(W-176)/2}" width="176" height="176" viewBox="0 0 256 256">{inner(ic)}</svg>{t}</svg>')


def social_svg():
    W, H = 1280, 640
    ic = icon_svg("light", 256, pad_frac=0)
    _, tw = word(0, 0, 116, WHITE, WHITE)
    T3, T4 = "Cliente SSH open source para Windows", "sem contas · sem nuvem · sem telemetria"
    _, w3 = text_path(INTER_M, T3, 30, 0, 0)
    _, w4 = text_path(MONO, T4, 22, 0, 0)
    total = 220 + 56 + max(tw, w3, w4)
    ox = (W - total) / 2; tx = ox + 276
    t, _ = word(tx, 316, 116, WHITE, WHITE)
    d3, _ = text_path(INTER_M, T3, 30, tx + 4, 374, 0)
    d4, _ = text_path(MONO, T4, 22, tx + 4, 418, 0)
    rings = "".join(f'<circle cx="{ox+110}" cy="320" r="{r}" fill="none" stroke="#fff" stroke-opacity="{o}"/>'
                    for r, o in [(190, .06), (270, .045), (360, .03), (460, .02)])
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">'
            f'<defs><linearGradient id="sg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{BLUE}"/>'
            f'<stop offset=".55" stop-color="{BLUE_DEEP}"/><stop offset="1" stop-color="{BLACK}"/></linearGradient></defs>'
            f'<rect width="{W}" height="{H}" fill="url(#sg)"/>{rings}'
            f'<svg x="{ox}" y="210" width="220" height="220" viewBox="0 0 256 256">{inner(ic)}</svg>'
            f'{t}<path d="{d3}" fill="#fff" fill-opacity=".78"/><path d="{d4}" fill="#fff" fill-opacity=".55"/></svg>')


def png(svg, w, h=None):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=w, output_height=h or w))).convert("RGBA")


def compose(cw, ch, svg, px):
    im = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    im.alpha_composite(png(svg, px), ((cw - px) // 2, (ch - px) // 2))
    return im
