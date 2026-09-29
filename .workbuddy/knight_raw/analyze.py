import numpy as np
from PIL import Image

src = r"D:/youxicongtoulai/My project (1)/.workbuddy/knight_raw/2D_game_sprite_sheet_in_retro__2026-09-27T04-59-51.png"
im = Image.open(src).convert("RGBA")
a = np.array(im)
print("size:", im.size, "shape:", a.shape)

alpha = a[..., 3]
print("alpha min/max/mean:", alpha.min(), alpha.max(), round(alpha.mean(), 2))
print("fully transparent px:", int((alpha == 0).sum()), "/", alpha.size)

# corner colors (checkerboard candidates)
h, w = a.shape[:2]
print("corner colors:", a[0, 0], a[0, -1], a[-1, 0], a[-1, -1])

# top colors of the whole image (quantized)
q = (a[..., :3] // 8 * 8).reshape(-1, 3)
vals, counts = np.unique(q, axis=0, return_counts=True)
order = np.argsort(-counts)[:8]
for i in order:
    print("color", vals[i], "count", counts[i])
