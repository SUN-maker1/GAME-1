import re
p = "process4.py"
s = open(p, encoding="utf-8").read()
s = s.replace("CW = max(cws) + 4; CW += CW % 2", "CW = max(cws) + 10; CW += CW % 2")
s = s.replace("""    blk = f[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    reg = sheet[oy + ys.min():oy + ys.max() + 1, ox + xs.min():ox + xs.max() + 1]
    m = blk >= 0
    reg[..., :3][m] = palette[blk[m]]
    reg[..., 3][m] = 255""",
"""    x0t, y0t = ox + xs.min(), oy + ys.min()
    x1t, y1t = ox + xs.max() + 1, oy + ys.max() + 1
    sx0, sy0 = max(0, x0t), max(0, y0t)
    sx1 = min(CW, x1t); sy1 = min(CH, y1t)
    bx0, by0 = sx0 - x0t, sy0 - y0t
    blk = f[ys.min() + by0:ys.max() + 1 - (y1t - sy1), xs.min() + bx0:xs.max() + 1 - (x1t - sx1)]
    reg = sheet[sy0:sy1, sx0:sx1]
    m = blk >= 0
    reg[..., :3][m] = palette[blk[m]]
    reg[..., 3][m] = 255""")
open(p, "w", encoding="utf-8").write(s)
print("patched")
