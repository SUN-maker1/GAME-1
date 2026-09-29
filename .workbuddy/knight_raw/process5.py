import numpy as np
from PIL import Image
from scipy import ndimage
import os, sys

SRC = sys.argv[3] if len(sys.argv) > 3 else r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw/2D_game_sprite_sheet_in_retro__2026-09-27T04-59-51.png"
OUT_DIR = r"D:/youxicongtoulai/My project (1)/Assets/IMAGE/Knight"
PREVIEW = r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw"
F = int(sys.argv[1]) if len(sys.argv) > 1 else 1
TAG = sys.argv[2] if len(sys.argv) > 2 else "full"
USE_MEDIAN = len(sys.argv) > 4 and sys.argv[4] == "median"

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
print("bg:", round(bg.mean() * 100, 1), "% | specks dropped:", n2 - int(keep.sum()))

CELL_W, CELL_H = w // 4, h // 2
crops = []
for r in range(2):
    for c in range(4):
        sub = fg[r * CELL_H:(r + 1) * CELL_H, c * CELL_W:(c + 1) * CELL_W]
        ys, xs = np.where(sub)
        x0 = max(0, c * CELL_W + xs.min() - 2); x1 = min(w - 1, c * CELL_W + xs.max() + 2)
        y0 = max(0, r * CELL_H + ys.min() - 2); y1 = min(h - 1, r * CELL_H + ys.max() + 2)
        crop = np.array(im)[y0:y1 + 1, x0:x1 + 1].copy()
        crop[..., 3] = np.where(fg[y0:y1 + 1, x0:x1 + 1], 255, 0)
        if F > 1:
            pil = Image.fromarray(crop)
            if USE_MEDIAN:
                arr = np.array(pil)
                med = np.stack([ndimage.median_filter(arr[..., k], size=5) for k in range(4)], axis=-1)
                pil = Image.fromarray(med)
            small = pil.resize((max(1, crop.shape[1] // F), max(1, crop.shape[0] // F)), Image.BOX)
            crop = np.array(small)
        crops.append(crop)

vis_px = np.concatenate([cr[cr[..., 3] > 128][:, :3] for cr in crops]).astype(np.uint8)
pal_q = Image.fromarray(vis_px.reshape(-1, 1, 3)).quantize(colors=40, method=0)
palette = np.array(pal_q.getpalette()[:40 * 3], np.int16).reshape(-1, 3)

idx_frames = []
for cr in crops:
    vis = cr[..., 3] > 128
    idx = np.full(cr.shape[:2], -1, np.int16)
    if vis.any():
        px = cr[vis][:, :3].astype(np.int16)
        d = ((px[:, None, :] - palette[None, :, :]) ** 2).sum(axis=2)
        idx[vis] = d.argmin(axis=1)
    idx_frames.append(idx)

BOT = 6 if F == 1 else 3
cws, chs, foot_cx = [], [], []
for f in idx_frames:
    ys, xs = np.where(f >= 0)
    fx = xs[ys >= ys.max() - BOT]
    foot_cx.append(int(fx.mean()))
    cws.append(xs.max() - xs.min() + 1); chs.append(ys.max() + 1)
CW = max(cws) + 10; CW += CW % 2
CH = max(chs) + 4; CH += CH % 2
print(f"[{TAG}] cell: {CW} x {CH}")

sheet = np.zeros((CH * 2, CW * 4, 4), np.uint8)
for i, f in enumerate(idx_frames):
    r, c = divmod(i, 4)
    ys, xs = np.where(f >= 0)
    ox = c * CW + CW // 2 - foot_cx[i]
    oy = (r + 1) * CH - 2 - ys.max()
    x0t, y0t = ox + xs.min(), oy + ys.min()
    x1t, y1t = ox + xs.max() + 1, oy + ys.max() + 1
    sx0, sy0 = max(c * CW, x0t), max(r * CH, y0t)
    sx1 = min((c + 1) * CW, x1t); sy1 = min((r + 1) * CH, y1t)
    bx0, by0 = sx0 - x0t, sy0 - y0t
    bx1 = blk_w = (sx1 - sx0); by1 = (sy1 - sy0)
    blk = f[ys.min() + by0:ys.min() + by0 + (sy1 - sy0), xs.min() + bx0:xs.min() + bx0 + (sx1 - sx0)]
    reg = sheet[sy0:sy1, sx0:sx1]
    m = blk >= 0
    reg[..., :3][m] = palette[blk[m]]
    reg[..., 3][m] = 255

img = Image.fromarray(sheet)
os.makedirs(OUT_DIR, exist_ok=True)
img.save(os.path.join(OUT_DIR, f"knight_{TAG}.png"))
print("sheet saved:", img.size)

def gif(indices_, path, scale):
    fr = []
    for i in indices_:
        f = idx_frames[i]
        img2 = np.zeros((f.shape[0], f.shape[1], 4), np.uint8)
        m = f >= 0
        img2[..., :3][m] = palette[f[m]]; img2[..., 3][m] = 255
        pil = Image.fromarray(img2)
        pil = pil.resize((pil.width * scale, pil.height * scale), Image.NEAREST)
        fr.append(pil)
    fr[0].save(path, save_all=True, append_images=fr[1:], duration=200, loop=0, disposal=2)

sc = 2 if F == 1 else 5
gif([0, 1, 2, 3], os.path.join(PREVIEW, f"idle_{TAG}.gif"), sc)
gif([4, 5, 6, 7], os.path.join(PREVIEW, f"walk_{TAG}.gif"), sc)
print("previews saved")
