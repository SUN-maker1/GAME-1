import numpy as np
from PIL import Image
from scipy import ndimage
import os

SRC = r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw/2D_game_sprite_sheet_in_retro__2026-09-27T04-59-51.png"
OUT_DIR = r"D:/youxicongtoulai/My project (1)/Assets/IMAGE/Knight"
PREVIEW = r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw"

im = Image.open(SRC).convert("RGBA")
a = np.array(im)
h, w = a.shape[:2]

mx, mn = a[..., :3].max(axis=2).astype(int), a[..., :3].min(axis=2).astype(int)
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

# crops (full res, binary alpha)
CELL_W, CELL_H = w // 4, h // 2
crops = []
for r in range(2):
    for c in range(4):
        sub = fg[r * CELL_H:(r + 1) * CELL_H, c * CELL_W:(c + 1) * CELL_W]
        ys, xs = np.where(sub)
        x0 = max(0, c * CELL_W + xs.min() - 2); x1 = min(w - 1, c * CELL_W + xs.max() + 2)
        y0 = max(0, r * CELL_H + ys.min() - 2); y1 = min(h - 1, r * CELL_H + ys.max() + 2)
        crop = a[y0:y1 + 1, x0:x1 + 1].copy()
        crop[..., 3] = np.where(fg[y0:y1 + 1, x0:x1 + 1], 255, 0)
        crops.append(crop)

# global palette at FULL res from visible pixels
vis_px = np.concatenate([cr[cr[..., 3] > 128][:, :3] for cr in crops]).astype(np.uint8)
pal_q = Image.fromarray(vis_px.reshape(-1, 1, 3)).quantize(colors=40, method=0)
palette = np.array(pal_q.getpalette()[:40 * 3], np.int16).reshape(-1, 3)

def to_indices(crop):
    vis = crop[..., 3] > 128
    idx = np.full(crop.shape[:2], -1, np.int16)
    px = crop[vis][:, :3].astype(np.int16)
    d = ((px[:, None, :] - palette[None, :, :]) ** 2).sum(axis=2)
    idx[vis] = d.argmin(axis=1)
    return idx

indices = [to_indices(cr) for cr in crops]
print("global palette:", len(palette), "colors")

def majority_down(idx, F):
    H, W = idx.shape
    ph, pw = H // F, W // F
    idx2 = idx[:ph * F, :pw * F].reshape(ph, F, pw, F)
    out = np.full((ph, pw), -1, np.int16)
    flat = idx2.transpose(0, 2, 1, 3).reshape(ph, pw, F * F)
    for i in range(ph):
        for j in range(pw):
            blk = flat[i, j]
            v = blk[blk >= 0]
            if v.size:
                cnt = np.bincount(v, minlength=len(palette))
                if cnt.max() >= max(2, v.size * 0.4):
                    out[i, j] = cnt.argmax()
    return out

def build_sheet(frames_idx, CW_target=None):
    BOT = 3
    cws, chs, foot_cx = [], [], []
    for f in frames_idx:
        ys, xs = np.where(f >= 0)
        fx = xs[ys >= ys.max() - BOT]
        foot_cx.append(int(fx.mean()))
        cws.append(xs.max() - xs.min() + 1); chs.append(ys.max() + 1)
    CW = max(cws) + 2; CH = max(chs) + 2
    CW += CW % 2; CH += CH % 2
    sheet = np.full((CH * 2, CW * 4, 4), 0, np.uint8)
    for i, f in enumerate(frames_idx):
        r, c = divmod(i, 4)
        ys, xs = np.where(f >= 0)
        ox = c * CW + CW // 2 - foot_cx[i]
        oy = (r + 1) * CH - 1 - ys.max()
        blk = f[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        reg = sheet[oy + ys.min():oy + ys.max() + 1, ox + xs.min():ox + xs.max() + 1]
        m = blk >= 0
        reg[..., :3][m] = palette[blk[m]]
        reg[..., 3][m] = 255
    return Image.fromarray(sheet), CW, CH

def gif(fr_idx, path, scale=5):
    fr = []
    for i in fr_idx:
        f = frames_idx_list[i]
        img = np.full((f.shape[0], f.shape[1], 4), 0, np.uint8)
        m = f >= 0
        img[..., :3][m] = palette[f[m]]; img[..., 3][m] = 255
        fr.append(Image.fromarray(img).resize((img.shape[1] * scale, img.shape[0] * scale), Image.NEAREST))
    fr[0].save(path, save_all=True, append_images=fr[1:], duration=200, loop=0, disposal=2)

os.makedirs(OUT_DIR, exist_ok=True)
for F in (4, 5):
    frames_idx_list = [majority_down(t, F) for t in indices]
    sheet, CW, CH = build_sheet(frames_idx_list)
    print(f"F={F}: cell {CW}x{CH}, sheet {sheet.size}")
    sheet.save(os.path.join(OUT_DIR, f"knight_F{F}.png"))
    frames_idx_list = frames_idx_list  # for gif
    globals()['frames_idx_list'] = frames_idx_list
    gif([0, 1, 2, 3], os.path.join(PREVIEW, f"idle_F{F}.gif"))
    gif([4, 5, 6, 7], os.path.join(PREVIEW, f"walk_F{F}.gif"))
print("done")
