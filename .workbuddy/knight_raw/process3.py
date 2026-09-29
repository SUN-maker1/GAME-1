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

vis_px = np.concatenate([f[f[..., 3] > 128][:, :3] for f in frames if (f[..., 3] > 128).any()]).astype(np.uint8)
pal_q = Image.fromarray(vis_px.reshape(-1, 1, 3)).quantize(colors=40, method=0)
palette = np.array(pal_q.getpalette()[:40 * 3], np.int16).reshape(-1, 3)

def despeckle(idx, passes=2):
    K = np.ones((3, 3), int); K[1, 1] = 0
    for _ in range(passes):
        same = np.zeros(idx.shape, int)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy == 0 and dx == 0: continue
                sh = np.roll(np.roll(idx, dy, 0), dx, 1)
                same += (sh == idx) & (idx >= 0)
        ys, xs = np.where((idx >= 0) & (same <= 1))
        for y, x in zip(ys, xs):
            nb = []
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dy == 0 and dx == 0: continue
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < idx.shape[0] and 0 <= xx < idx.shape[1] and idx[yy, xx] >= 0:
                        nb.append(idx[yy, xx])
            if nb:
                vals, cnts = np.unique(nb, return_counts=True)
                if cnts.max() >= 4:
                    idx[y, x] = vals[cnts.argmax()]
    return idx

idx_frames = []
for f in frames:
    vis = f[..., 3] > 128
    idx = np.full(f.shape[:2], -1, np.int16)
    if vis.any():
        px = f[vis][:, :3].astype(np.int16)
        d = ((px[:, None, :] - palette[None, :, :]) ** 2).sum(axis=2)
        idx[vis] = d.argmin(axis=1)
    idx_frames.append(despeckle(idx.copy()))

BOT = 3
cws, chs, foot_cx = [], [], []
for f in idx_frames:
    ys, xs = np.where(f >= 0)
    fx = xs[ys >= ys.max() - BOT]
    foot_cx.append(int(fx.mean()))
    cws.append(xs.max() - xs.min() + 1); chs.append(ys.max() + 1)
CW = max(cws) + 2; CW += CW % 2
CH = max(chs) + 2; CH += CH % 2
print("cell:", CW, "x", CH)

sheet = np.zeros((CH * 2, CW * 4, 4), np.uint8)
for i, f in enumerate(idx_frames):
    r, c = divmod(i, 4)
    ys, xs = np.where(f >= 0)
    ox = c * CW + CW // 2 - foot_cx[i]
    oy = (r + 1) * CH - 1 - ys.max()
    blk = f[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    reg = sheet[oy + ys.min():oy + ys.max() + 1, ox + xs.min():ox + xs.max() + 1]
    m = blk >= 0
    reg[..., :3][m] = palette[blk[m]]
    reg[..., 3][m] = 255

img = Image.fromarray(sheet)
img.save(os.path.join(OUT_DIR, "knight.png"))
print("sheet saved:", img.size)

def gif(fr_list, indices_, path, scale=5):
    fr = []
    for i in indices_:
        f = fr_list[i]
        img2 = np.zeros((f.shape[0], f.shape[1], 4), np.uint8)
        m = f >= 0
        img2[..., :3][m] = palette[f[m]]; img2[..., 3][m] = 255
        fr.append(Image.fromarray(img2).resize((img2.shape[1] * scale, img2.shape[0] * scale), Image.NEAREST))
    fr[0].save(path, save_all=True, append_images=fr[1:], duration=200, loop=0, disposal=2)
gif(idx_frames, [0, 1, 2, 3], os.path.join(PREVIEW, "idle_v2.gif"))
gif(idx_frames, [4, 5, 6, 7], os.path.join(PREVIEW, "walk_v2.gif"))
print("previews saved")
