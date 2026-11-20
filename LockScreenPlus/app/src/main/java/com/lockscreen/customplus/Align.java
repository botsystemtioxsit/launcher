package com.lockscreen.customplus;

/* loaded from: classes3.dex */
final class Align {
    private Align() {
    }

    static int[] find(int[] bg, int bw, int bh, int[] cut, int cw, int ch) {
        int sch;
        int ny;
        int y0;
        int[] cutSmall;
        int d;
        int[] idx;
        int i = ch;
        if (cw > bw || i > bh || cw < 1 || i < 1) {
            return null;
        }
        int d2 = Math.max(1, Math.min(Math.max(1, (Math.max(bw, bh) + 319) / 320), Math.min(cw, ch) / 8));
        int sbw = bw / d2;
        int sbh = bh / d2;
        int scw = cw / d2;
        int sch2 = i / d2;
        int[] bgSmall = lumDown(bg, bw, bh, d2, null);
        int[] alphaSmall = new int[scw * sch2];
        int[] cutSmall2 = lumDown(cut, cw, i, d2, alphaSmall);
        int[] idx2 = maskIndexes(alphaSmall, 250, 4000);
        if (idx2.length == 0) {
            return null;
        }
        int nx = (sbw - scw) + 1;
        int ny2 = (sbh - sch2) + 1;
        long[] sums = new long[nx * ny2];
        long best = Long.MAX_VALUE;
        for (int y = 0; y < ny2; y++) {
            int x = 0;
            while (x < nx) {
                long sum = 0;
                int k = 0;
                while (true) {
                    d = d2;
                    if (k >= idx2.length) {
                        idx = idx2;
                        break;
                    }
                    int i2 = idx2[k];
                    int px = i2 % scw;
                    int py = i2 / scw;
                    idx = idx2;
                    sum += Math.abs(bgSmall[((y + py) * sbw) + (x + px)] - cutSmall2[i2]);
                    if (sum > 3 * best) {
                        break;
                    }
                    k++;
                    d2 = d;
                    idx2 = idx;
                }
                int k2 = y * nx;
                sums[k2 + x] = sum;
                if (sum < best) {
                    best = sum;
                }
                x++;
                d2 = d;
                idx2 = idx;
            }
        }
        int d3 = d2;
        int[] bgL = lum(bg);
        int[] cutL = lum(cut);
        int[] cutA = new int[cw * i];
        for (int i3 = 0; i3 < cutA.length; i3++) {
            cutA[i3] = cut[i3] >>> 24;
        }
        int[] fidx = maskIndexes(cutA, 250, 6000);
        if (fidx.length == 0) {
            return null;
        }
        long fbest = Long.MAX_VALUE;
        int fx = 0;
        int minGap = Math.max(2, Math.min(scw, sch2) / 4);
        int fy = 0;
        int round = 0;
        while (true) {
            int sbw2 = sbw;
            if (round >= 5) {
                break;
            }
            long m = Long.MAX_VALUE;
            int mi = -1;
            int scw2 = scw;
            int scw3 = 0;
            while (true) {
                sch = sch2;
                if (scw3 >= sums.length) {
                    break;
                }
                if (sums[scw3] < m) {
                    m = sums[scw3];
                    mi = scw3;
                }
                scw3++;
                sch2 = sch;
            }
            if (mi >= 0 && m != Long.MAX_VALUE) {
                int sx = mi % nx;
                int sy = mi / nx;
                int mi2 = sy - minGap;
                int[] bgSmall2 = bgSmall;
                int y2 = Math.max(0, mi2);
                while (true) {
                    ny = ny2;
                    if (y2 > Math.min(ny2 - 1, sy + minGap)) {
                        break;
                    }
                    int x2 = Math.max(0, sx - minGap);
                    while (true) {
                        cutSmall = cutSmall2;
                        if (x2 <= Math.min(nx - 1, sx + minGap)) {
                            sums[(y2 * nx) + x2] = Long.MAX_VALUE;
                            x2++;
                            cutSmall2 = cutSmall;
                        }
                    }
                    y2++;
                    ny2 = ny;
                    cutSmall2 = cutSmall;
                }
                int[] cutSmall3 = cutSmall2;
                int y3 = sx * d3;
                int cy = sy * d3;
                int x0 = Math.max(0, (y3 - d3) - 2);
                int minGap2 = minGap;
                int minGap3 = y3 + d3 + 2;
                int x1 = Math.min(bw - cw, minGap3);
                int nx2 = nx;
                int y02 = Math.max(0, (cy - d3) - 2);
                int y1 = Math.min(bh - i, cy + d3 + 2);
                int y4 = y02;
                while (y4 <= y1) {
                    int y12 = y1;
                    int y13 = x0;
                    while (y13 <= x1) {
                        long sum2 = 0;
                        int x12 = x1;
                        int x13 = 0;
                        while (true) {
                            y0 = y02;
                            if (x13 >= fidx.length || sum2 >= fbest) {
                                break;
                            }
                            int i4 = fidx[x13];
                            int px2 = i4 % cw;
                            int py2 = i4 / cw;
                            sum2 += Math.abs(bgL[((y4 + py2) * bw) + (y13 + px2)] - cutL[i4]);
                            x13++;
                            y02 = y0;
                            cutL = cutL;
                            bgL = bgL;
                        }
                        int[] bgL2 = bgL;
                        int[] cutL2 = cutL;
                        if (sum2 < fbest) {
                            long fbest2 = sum2;
                            int fx2 = y13;
                            fbest = fbest2;
                            fx = fx2;
                            fy = y4;
                        }
                        y13++;
                        y02 = y0;
                        x1 = x12;
                        cutL = cutL2;
                        bgL = bgL2;
                    }
                    y4++;
                    y1 = y12;
                }
                round++;
                i = ch;
                sbw = sbw2;
                scw = scw2;
                sch2 = sch;
                bgSmall = bgSmall2;
                ny2 = ny;
                cutSmall2 = cutSmall3;
                minGap = minGap2;
                nx = nx2;
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
