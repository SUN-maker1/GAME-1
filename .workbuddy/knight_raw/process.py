import numpy as np
from PIL import Image
from scipy import ndimage
import os

SRC = r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw/2D_game_sprite_sheet_in_retro__2026-09-27T04-59-51.png"
OUT_DIR = r"D:/youxicongtoulai/My project (1)/Assets/IMAGE/Knight"
PREVIEW = r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw"

im = Image.open(SRC).convert("RGBA")
a = np.array(im).astype(np.int16)
h, w = a.shape[:2]
rgb = a[..., :3]

mx, mn = rgb.max(axis=2), rgb.min(axis=2)
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

CELL_W, CELL_H = w // 4, h // 2
F = 4
frames = []
for r in range(2):
    for c in range(4):
        sub = fg[r * CELL_H:(r + 1) * CELL_H, c * CELL_W:(c + 1) * CELL_W]
        ys, xs = np.where(sub)
        x0 = max(0, c * CELL_W + xs.min() - 2); x1 = min(w - 1, c * CELL_W + xs.max() + 2)
        y0 = max(0, r * CELL_H + ys.min() - 2); y1 = min(h - 1, r * CELL_H + ys.max() + 2)
        crop = np.array(im)[y0:y1 + 1, x0:x1 + 1].copy()
        crop[..., 3] = np.where(fg[y0:y1 + 1, x0:x1 + 1], 255, 0)
        small = Image.fromarray(crop).resize((max(1, crop.shape[1] // F), max(1, crop.shape[0] // F)), Image.BOX)
        frames.append(np.array(small))

# global palette from VISIBLE pixels only, 48 colors
vis_px = np.concatenate([f[f[..., 3] > 128][:, :3] for f in frames if (f[..., 3] > 128).any()]).astype(np.uint8)
pal_img = Image.fromarray(vis_px.reshape(-1, 1, 3))
pal_q = pal_img.quantize(colors=48, method=Image.MEDIANCUT)
palette = np.array(pal_q.getpalette()[:48 * 3], np.int16).reshape(-1, 3)
print("palette colors:", len(palette))

uni = []
for f in frames:
    vis = f[..., 3] > 128
    out = np.zeros_like(f)
    if vis.any():
        px = f[vis][:, :3].astype(np.int16)
        d = ((px[:, None, :] - palette[None, :, :]) ** 2).sum(axis=2)
        out[vis, :3] = palette[d.argmin(axis=1)]
        out[vis, 3] = 255
    uni.append(out)

BOT = 8
cws, chs, foot_cx = [], [], []
for f in uni:
    ys, xs = np.where(f[..., 3] > 128)
    fx = xs[ys >= ys.max() - BOT]
    foot_cx.append(int(fx.mean()))
    cws.append(xs.max() - xs.min() + 1); chs.append(ys.max() + 1)
CW = max(cws) + 4; CW += CW % 2
CH = max(chs) + 4; CH += CH % 2
print("cell:", CW, "x", CH)

sheet = np.zeros((CH * 2, CW * 4, 4), np.uint8)
for i, f in enumerate(uni):
    r, c = divmod(i, 4)
    ys, xs = np.where(f[..., 3] > 128)
    ox = c * CW + CW // 2 - foot_cx[i]
    oy = (r + 1) * CH - 2 - ys.max()
    sheet[oy + ys.min():oy + ys.max() + 1, ox + xs.min():ox + xs.max() + 1] = f[ys.min():ys.max() + 1, xs.min():xs.max() + 1]

os.makedirs(OUT_DIR, exist_ok=True)
img = Image.fromarray(sheet)
img.save(os.path.join(OUT_DIR, "knight.png"))
print("sheet saved:", img.size)

def gif(idx, path):
    fr = [Image.fromarray(uni[i]).resize((uni[i].shape[1] * 4, uni[i].shape[0] * 4), Image.NEAREST) for i in idx]
    fr[0].save(path, save_all=True, append_images=fr[1:], duration=200, loop=0, disposal=2)
gif([0, 1, 2, 3], os.path.join(PREVIEW, "idle_preview.gif"))
gif([4, 5, 6, 7], os.path.join(PREVIEW, "walk_preview.gif"))
print("previews saved")
