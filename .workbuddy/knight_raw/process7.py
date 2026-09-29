import numpy as np
from PIL import Image
from scipy import ndimage
import os, sys

SRC = sys.argv[1] if len(sys.argv) > 1 else r"d:/youxicongtoulai/My project (1)/generated-images/Redraw_this_exact_4x2_sprite_s_2026-09-27T05-06-10.png"
OUT_DIR = r"D:/youxicongtoulai/My project (1)/Assets/IMAGE/Knight"
PREVIEW = r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw"

im = Image.open(SRC).convert("RGBA")
a = np.array(im).astype(np.int16)
h, w = a.shape[:2]

mx, mn = a[..., :3].max(axis=2), a[..., :3].min(axis=2)
light = (mn > 195) & ((mx - mn) < 14)
lab, _ = ndimage.label(light, structure=np.ones((3, 3), int))
border_labels = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
border_labels = border_labels[border_labels != 0]
bg = np.isin(lab, border_labels)
fg = ~bg
lab2, n2 = ndimage.label(fg, structure=np.ones((3, 3), int))
sizes = ndimage.sum(fg, lab2, range(1, n2 + 1))
keep = np.zeros(n2 + 1, bool); keep[1:] = sizes > 800
fg &= keep[lab2]
print("bg:", round(bg.mean() * 100, 1), "%")

vis_px = a[fg][:, :3]
pal_q = Image.fromarray(vis_px.astype(np.uint8).reshape(-1, 1, 3)).quantize(colors=48, method=0)
palette = np.array(pal_q.getpalette()[:48 * 3], np.int32).reshape(-1, 3)
print("palette:", len(palette))

# chunked nearest-palette mapping (memory safe)
idx = np.full((h, w), -1, np.int16)
ys, xs = np.where(fg)
px = a[ys, xs, :3].astype(np.int32)
CH = 20000
for s in range(0, px.shape[0], CH):
    chunk = px[s:s + CH]
    dd = ((chunk.astype(np.int32)[:, None, :] - palette[None, :, :]) ** 2).sum(axis=2)
    idx[ys[s:s + CH], xs[s:s + CH]] = dd.argmin(axis=1).astype(np.int16)
del px, dd

import os as _os
F = int(_os.environ.get("PIXEL_F", "16"))
best, best_phase = -1.0, (0, 0)
region = idx[:512, :512]
for py in range(F):
    for pxx in range(F):
        sub = region[py:512:1, pxx:512:1]
        Hs, Ws = sub.shape
        Hb, Wb = Hs // F, Ws // F
        blocks = sub[:Hb * F, :Wb * F].reshape(Hb, F, Wb, F)
        nonneg = blocks >= 0
        same = (blocks == blocks[:, :1, :1, :1]) | ~nonneg[:, :1, :1, :] | (blocks < 0).all(axis=(1, 3), keepdims=True)
        uni = (np.all(blocks == blocks[:, :1, :1, :], axis=(1, 3)) | np.all(blocks < 0, axis=(1, 3))).mean()
        if uni > best:
            best, best_phase = uni, (py, pxx)
print("best phase:", best_phase, "uniform:", round(float(best), 3))
py, pxx = best_phase

H2, W2 = (h - py) // F, (w - pxx) // F
small = np.full((H2, W2), -1, np.int16)
blk = idx[py:py + H2 * F, pxx:pxx + W2 * F].reshape(H2, F, W2, F).transpose(0, 2, 1, 3).reshape(H2, W2, F * F)
for i in range(H2):
    for j in range(W2):
        v = blk[i, j]
        vv = v[v >= 0]
        if vv.size >= F * F * 0.4:
            small[i, j] = np.bincount(vv, minlength=len(palette)).argmax()
print("small size:", W2, "x", H2)

CW_CELL, CH_CELL = W2 // 4, H2 // 2
frames = [small[r * CH_CELL:(r + 1) * CH_CELL, c * CW_CELL:(c + 1) * CW_CELL] for r in range(2) for c in range(4)]

BOT = 1
cws, chs, foot_cx = [], [], []
trimmed = []
for f in frames:
    ys2, xs2 = np.where(f >= 0)
    t = f[ys2.min():ys2.max() + 1, xs2.min():xs2.max() + 1]
    trimmed.append(t)
    ys3, xs3 = np.where(t >= 0)
    fx = xs3[ys3 >= ys3.max() - BOT]
    foot_cx.append(int(fx.mean()))
    cws.append(xs3.max() - xs3.min() + 1); chs.append(ys3.max() + 1)
CW = max(cws) + 2; CW += CW % 2
CH = max(chs) + 2; CH += CH % 2
print("cell:", CW, "x", CH)

sheet = np.zeros((CH * 2, CW * 4, 4), np.uint8)
for i, f in enumerate(trimmed):
    r, c = divmod(i, 4)
    ys3, xs3 = np.where(f >= 0)
    ox = c * CW + CW // 2 - foot_cx[i]
    oy = (r + 1) * CH - 1 - ys3.max()
    x0t, y0t = ox + xs3.min(), oy + ys3.min()
    x1t, y1t = ox + xs3.max() + 1, oy + ys3.max() + 1
    sx0, sy0 = max(c * CW, x0t), max(r * CH, y0t)
    sx1 = min((c + 1) * CW, x1t); sy1 = min((r + 1) * CH, y1t)
    bx0, by0 = sx0 - x0t, sy0 - y0t
    blk2 = f[ys3.min() + by0:ys3.min() + by0 + (sy1 - sy0), xs3.min() + bx0:xs3.min() + bx0 + (sx1 - sx0)]
    reg = sheet[sy0:sy1, sx0:sx1]
    m = blk2 >= 0
    reg[..., :3][m] = palette[blk2[m]]
    reg[..., 3][m] = 255

img = Image.fromarray(sheet)
os.makedirs(OUT_DIR, exist_ok=True)
img.save(os.path.join(OUT_DIR, f"knight_f{F}.png"))
print("sheet saved:", img.size)

def gif(indices_, path, scale):
    fr = []
    for i in indices_:
        f = trimmed[i]
        img2 = np.zeros((f.shape[0], f.shape[1], 4), np.uint8)
        m = f >= 0
        img2[..., :3][m] = palette[f[m]]; img2[..., 3][m] = 255
        pil = Image.fromarray(img2)
        pil = pil.resize((pil.width * scale, pil.height * scale), Image.NEAREST)
        fr.append(pil)
    fr[0].save(path, save_all=True, append_images=fr[1:], duration=220, loop=0, disposal=2)

gif([0, 1, 2, 3], os.path.join(PREVIEW, f"idle_f{F}.gif"), 10)
gif([4, 5, 6, 7], os.path.join(PREVIEW, f"walk_f{F}.gif"), 10)
print("previews saved")
