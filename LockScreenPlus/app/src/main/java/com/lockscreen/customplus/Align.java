package com.lockscreen.customplus;

/* loaded from: classes3.dex */
final class Align {
    private Align() {
    }

    /**
     * Находит, где на фото лежит вырез (PNG того же снимка, но меньше или равного размера): ищет смещение, при котором
     * яркость выреза лучше всего совпадает с фото. Сначала грубо на уменьшенных копиях, потом уточняет рядом с пятью лучшими местами.
     * Возвращает {x, y, ошибка 0..255} или null, если вырез больше фото или в нём нечего сравнивать.
     */
    static int[] find(int[] bg, int bw, int bh, int[] cut, int cw, int ch) {
        if (cw > bw || ch > bh || cw < 1 || ch < 1) {
            return null;
        }
        int d = Math.max(1, Math.min(Math.max(1, (Math.max(bw, bh) + 319) / 320), Math.min(cw, ch) / 8));
        int sbw = bw / d;
        int sbh = bh / d;
        int scw = cw / d;
        int sch = ch / d;
        int[] bgSmall = lumDown(bg, bw, bh, d, null);
        int[] alphaSmall = new int[scw * sch];
        int[] cutSmall = lumDown(cut, cw, ch, d, alphaSmall);
        int[] idx = maskIndexes(alphaSmall, 250, 4000);
        if (idx.length == 0) {
            return null;
        }
        int nx = sbw - scw + 1;
        int ny = sbh - sch + 1;
        long[] sums = new long[nx * ny];
        long best = Long.MAX_VALUE;
        for (int y = 0; y < ny; y++) {
            for (int x = 0; x < nx; x++) {
                long sum = 0;
                for (int k = 0; k < idx.length; k++) {
                    int i = idx[k];
                    int px = i % scw;
                    int py = i / scw;
                    sum += Math.abs(bgSmall[((y + py) * sbw) + (x + px)] - cutSmall[i]);
                    if (sum > 3 * best) {
                        break;
                    }
                }
                sums[(y * nx) + x] = sum;
                if (sum < best) {
                    best = sum;
                }
            }
        }
        // уточнение на настоящих размерах рядом с лучшими местами грубого поиска
        int[] bgL = lum(bg);
        int[] cutL = lum(cut);
        int[] cutA = new int[cw * ch];
        for (int i = 0; i < cutA.length; i++) {
            cutA[i] = cut[i] >>> 24;
        }
        int[] fidx = maskIndexes(cutA, 250, 6000);
        if (fidx.length == 0) {
            return null;
        }
        long fbest = Long.MAX_VALUE;
        int fx = 0;
        int fy = 0;
        int minGap = Math.max(2, Math.min(scw, sch) / 4);
        for (int round = 0; round < 5; round++) {
            long m = Long.MAX_VALUE;
            int mi = -1;
            for (int k = 0; k < sums.length; k++) {
                if (sums[k] < m) {
                    m = sums[k];
                    mi = k;
                }
            }
            if (mi < 0 || m == Long.MAX_VALUE) {
                break;
            }
            int sx = mi % nx;
            int sy = mi / nx;
            // это место и соседние больше не берём
            for (int yy = Math.max(0, sy - minGap); yy <= Math.min(ny - 1, sy + minGap); yy++) {
                for (int xx = Math.max(0, sx - minGap); xx <= Math.min(nx - 1, sx + minGap); xx++) {
                    sums[(yy * nx) + xx] = Long.MAX_VALUE;
                }
            }
            int cx = sx * d;
            int cy = sy * d;
            int x0 = Math.max(0, (cx - d) - 2);
            int x1 = Math.min(bw - cw, cx + d + 2);
            int y0 = Math.max(0, (cy - d) - 2);
            int y1 = Math.min(bh - ch, cy + d + 2);
            for (int yy = y0; yy <= y1; yy++) {
                for (int xx = x0; xx <= x1; xx++) {
                    long sum = 0;
                    for (int k = 0; k < fidx.length && sum < fbest; k++) {
                        int i = fidx[k];
                        sum += Math.abs(bgL[((yy + (i / cw)) * bw) + (xx + (i % cw))] - cutL[i]);
                    }
                    if (sum < fbest) {
                        fbest = sum;
                        fx = xx;
                        fy = yy;
                    }
                }
            }
        }
        int err = fbest == Long.MAX_VALUE ? 255 : (int) (fbest / fidx.length);
        return new int[]{fx, fy, err};
    }

    private static int[] lum(int[] px) {
        int[] out = new int[px.length];
        for (int i = 0; i < px.length; i++) {
            out[i] = lumOf(px[i]);
        }
        return out;
    }

    private static int lumOf(int argb) {
        int r = (argb >> 16) & 255;
        int g = (argb >> 8) & 255;
        int b = argb & 255;
        return (((r * 299) + (g * 587)) + (b * 114)) / 1000;
    }

    private static int[] lumDown(int[] px, int w, int h, int d, int[] alphaOut) {
        int sw = w / d;
        int sh = h / d;
        int[] out = new int[sw * sh];
        for (int y = 0; y < sh; y++) {
            for (int x = 0; x < sw; x++) {
                int sum = 0;
                int minA = 255;
                for (int yy = 0; yy < d; yy++) {
                    int row = (((y * d) + yy) * w) + (x * d);
                    for (int xx = 0; xx < d; xx++) {
                        int p = px[row + xx];
                        sum += lumOf(p);
                        int a = p >>> 24;
                        if (a < minA) {
                            minA = a;
                        }
                    }
                }
                int yy2 = y * sw;
                out[yy2 + x] = sum / (d * d);
                if (alphaOut != null) {
                    alphaOut[(y * sw) + x] = minA;
                }
            }
        }
        return out;
    }

    private static int[] maskIndexes(int[] alpha, int threshold, int maxCount) {
        int count = 0;
        for (int a : alpha) {
            if (a >= threshold) {
                count++;
            }
        }
        int stride = Math.max(1, count / maxCount);
        int[] tmp = new int[(count / stride) + 1];
        int n = 0;
        int seen = 0;
        for (int i = 0; i < alpha.length; i++) {
            if (alpha[i] >= threshold) {
                if (seen % stride == 0 && n < tmp.length) {
                    tmp[n] = i;
                    n++;
                }
                seen++;
            }
        }
        int[] out = new int[n];
        System.arraycopy(tmp, 0, out, 0, n);
        return out;
    }
}
