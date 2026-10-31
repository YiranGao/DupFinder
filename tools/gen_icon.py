#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 DupFinder 图标 (assets/DupFinder.ico)。

纯标准库实现（zlib + struct），不依赖 Pillow 等任何第三方包：
  - 用 SDF（有向距离场）+ 超采样绘制扁平化图标：蓝色圆角底 + 两个叠放文件夹 + 放大镜
  - 每个尺寸编码为 PNG(RGBA) 后按 ICO 容器格式打包（Windows Vista+ 原生支持 PNG 图标）

用法： python tools/gen_icon.py
"""
import math
import os
import struct
import zlib

# ---------------------------------------------------------------- 颜色
BG_TOP = (0x3D, 0x97, 0xE8)
BG_BOT = (0x13, 0x53, 0x94)
BACK_FOLDER = (0x8C, 0xB6, 0xE7)
FRONT_OUTLINE = (0x0C, 0x36, 0x62)
FRONT_FOLDER = (0xFF, 0xFF, 0xFF)
PAGE_LINE = (0xB4, 0xCB, 0xE7)
LENS_OUTLINE = (0x0C, 0x36, 0x62)
LENS_RING = (0xFF, 0xB2, 0x1E)
LENS_GLASS = (0xFF, 0xFF, 0xFF)

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


# ---------------------------------------------------------------- SDF
def sd_round_rect(x, y, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) * 0.5, (y0 + y1) * 0.5
    hx, hy = (x1 - x0) * 0.5, (y1 - y0) * 0.5
    dx = abs(x - cx) - (hx - r)
    dy = abs(y - cy) - (hy - r)
    return math.hypot(max(dx, 0.0), max(dy, 0.0)) + min(max(dx, dy), 0.0) - r


def sd_circle(x, y, cx, cy, r):
    return math.hypot(x - cx, y - cy) - r


def sd_segment(x, y, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    wx, wy = x - ax, y - ay
    L2 = vx * vx + vy * vy
    t = 0.0 if L2 <= 0 else max(0.0, min(1.0, (wx * vx + wy * vy) / L2))
    return math.hypot(x - (ax + vx * t), y - (ay + vy * t))


def sd_folder(x, y, x0, y0, x1, y1, r=0.035):
    """文件夹：主体矩形 + 左上角标签凸起（两者取并集）"""
    h = y1 - y0
    tabh = h * 0.17
    tabw = (x1 - x0) * 0.44
    body = sd_round_rect(x, y, x0, y0 + tabh * 0.55, x1, y1, r)
    tab = sd_round_rect(x, y, x0, y0, x0 + tabw, y0 + tabh, r * 0.7)
    return min(body, tab)


def sd_ring(x, y, cx, cy, r, w):
    return abs(sd_circle(x, y, cx, cy, r)) - w * 0.5


# ---------------------------------------------------------------- 合成
def over(dst, rgb, a):
    """dst = (r,g,b,a) 0..1，src 覆盖其上"""
    dr, dg, db, da = dst
    if a <= 0.0:
        return dst
    oa = a + da * (1.0 - a)
    if oa <= 0.0:
        return (rgb[0], rgb[1], rgb[2], 0.0)
    k = da * (1.0 - a)
    return ((rgb[0] * a + dr * k) / oa,
            (rgb[1] * a + dg * k) / oa,
            (rgb[2] * a + db * k) / oa,
            oa)


def norm(c):
    return (c[0] / 255.0, c[1] / 255.0, c[2] / 255.0)


# 归一化坐标下的图形布局
BG = (0.025, 0.025, 0.975, 0.975, 0.225)
BACK = (0.275, 0.150, 0.790, 0.645)
FRONT = (0.150, 0.300, 0.665, 0.800)
LENS_C = (0.650, 0.658)
LENS_R = 0.155
LENS_W = 0.050
HANDLE_A = (0.732, 0.740)
HANDLE_B = (0.828, 0.836)
HANDLE_W = 0.062
LINE_YS = (0.495, 0.585)
LINE_X = (0.215, 0.545)
LINE_H = 0.038


def shade(px, py, size):
    """在归一化坐标 (px,py) 处做一次采样合成，返回 (r,g,b,a)"""
    # 背景铺满整块圆角方形；前景整体缩小并略向左上挪，避免放大镜贴边
    S = 0.90
    fx = 0.5 + (px - 0.5) / S + 0.014
    fy = 0.5 + (py - 0.5) / S + 0.012
    cov_bg = lambda d: max(0.0, min(1.0, 0.5 - d * size))
    cov = lambda d: max(0.0, min(1.0, 0.5 - d * size * S))
    out = (0.0, 0.0, 0.0, 0.0)

    # 1. 渐变圆角底
    t = max(0.0, min(1.0, (py - BG[1]) / (BG[3] - BG[1])))
    bg = tuple(BG_TOP[i] + (BG_BOT[i] - BG_TOP[i]) * t for i in range(3))
    out = over(out, norm(bg), cov_bg(sd_round_rect(px, py, *BG)))

    # 2. 后侧文件夹（浅蓝）
    out = over(out, norm(BACK_FOLDER), cov(sd_folder(fx, fy, *BACK)) * 0.95)

    # 3. 前侧文件夹：深色描边 + 白色本体 + 内容横线
    out = over(out, norm(FRONT_OUTLINE), cov(sd_folder(fx, fy, FRONT[0] - 0.012,
                                                       FRONT[1] - 0.012,
                                                       FRONT[2] + 0.012,
                                                       FRONT[3] + 0.012)))
    out = over(out, norm(FRONT_FOLDER), cov(sd_folder(fx, fy, *FRONT)))
    # 小尺寸下横线会糊成一团，只保留一条
    for ly in (LINE_YS if size >= 32 else LINE_YS[:1]):
        out = over(out, norm(PAGE_LINE),
                   cov(sd_round_rect(fx, fy, LINE_X[0], ly - LINE_H * 0.5,
                                     LINE_X[1], ly + LINE_H * 0.5, LINE_H * 0.5)) * 0.9)

    # 4. 放大镜：深色描边 + 橙色镜框 + 半透明镜片
    ring = sd_ring(fx, fy, LENS_C[0], LENS_C[1], LENS_R, LENS_W)
    handle = sd_segment(fx, fy, HANDLE_A[0], HANDLE_A[1], HANDLE_B[0], HANDLE_B[1]) - HANDLE_W * 0.5
    glass = sd_circle(fx, fy, LENS_C[0], LENS_C[1], LENS_R - LENS_W * 0.5)
    body = min(ring, handle)

    out = over(out, norm(LENS_OUTLINE), cov(min(ring - 0.014, handle - 0.014)))
    out = over(out, norm(LENS_RING), cov(body))
    out = over(out, norm(LENS_GLASS), cov(glass) * 0.22)

    return out


def render(size):
    """超采样渲染，返回 RGBA 字节串（自上而下）"""
    ss = 3 if size <= 48 else 2
    inv = 1.0 / ss
    rows = []
    for j in range(size):
        row = bytearray()
        for i in range(size):
            sr = sg = sb = sa = 0.0
            for sj in range(ss):
                py = (j + (sj + 0.5) * inv) / size
                for si in range(ss):
                    px = (i + (si + 0.5) * inv) / size
                    r, g, b, a = shade(px, py, size)
                    sr += r * a
                    sg += g * a
                    sb += b * a
                    sa += a
            if sa <= 0.0:
                row += b"\x00\x00\x00\x00"
            else:
                row += bytes((int(round(max(0.0, min(1.0, sr / sa)) * 255)),
                              int(round(max(0.0, min(1.0, sg / sa)) * 255)),
                              int(round(max(0.0, min(1.0, sb / sa)) * 255)),
                              int(round(max(0.0, min(1.0, sa / (ss * ss))) * 255))))
        rows.append(bytes(row))
    return b"".join(rows)


# ---------------------------------------------------------------- PNG / ICO
def chunk(tag, data):
    c = struct.pack(">I", len(data)) + tag + data
    return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)


def png_bytes(size, rgba):
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw += b"\x00" + rgba[y * stride:(y + 1) * stride]
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def ico_bytes(images):
    """images: [(size, png_bytes)] -> ICO 文件字节"""
    n = len(images)
    out = bytearray(struct.pack("<HHH", 0, 1, n))
    offset = 6 + 16 * n
    entries, blobs = [], []
    for size, data in images:
        w = 0 if size >= 256 else size
        entries.append(struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset))
        blobs.append(data)
        offset += len(data)
    for e in entries:
        out += e
    for b in blobs:
        out += b
    return bytes(out)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(here)
    assets = os.path.join(root, "assets")
    os.makedirs(assets, exist_ok=True)

    images = []
    for s in SIZES:
        rgba = render(s)
        images.append((s, png_bytes(s, rgba)))
        print("  rendered %3dx%-3d  png %6d bytes" % (s, s, len(images[-1][1])))
        if s == 256:
            with open(os.path.join(assets, "icon_preview.png"), "wb") as f:
                f.write(images[-1][1])

    ico = ico_bytes(images)
    path = os.path.join(assets, "DupFinder.ico")
    with open(path, "wb") as f:
        f.write(ico)
    print("  -> %s (%d bytes, %d sizes)" % (path, len(ico), len(images)))


if __name__ == "__main__":
    main()
