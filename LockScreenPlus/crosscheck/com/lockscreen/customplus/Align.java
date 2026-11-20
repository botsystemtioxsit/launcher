/*
 * Decompiled with CFR 0.152.
 */
package com.lockscreen.customplus;

final class Align {
    private Align() {
    }

    static int[] find(int[] nArray, int n, int n2, int[] nArray2, int n3, int n4) {
        if (n3 <= n && n4 <= n2 && n3 >= 1 && n4 >= 1) {
            long l;
            int n5;
            int n6;
            long l2;
            int n7;
            int n8;
            int n9 = Math.max(1, Math.min(Math.max(1, (Math.max(n, n2) + 319) / 320), Math.min(n3, n4) / 8));
            int n10 = n / n9;
            int n11 = n2 / n9;
            int n12 = n3 / n9;
            int n13 = n4 / n9;
            int[] nArray3 = Align.lumDown(nArray, n, n2, n9, null);
            int[] nArray4 = new int[n12 * n13];
            int[] nArray5 = Align.lumDown(nArray2, n3, n4, n9, nArray4);
            if ((nArray4 = Align.maskIndexes(nArray4, 250, 4000)).length == 0) {
                return null;
            }
            int n14 = n10 - n12 + 1;
            int n15 = n11 - n13 + 1;
            long[] lArray = new long[n14 * n15];
            long l3 = Long.MAX_VALUE;
            for (n8 = 0; n8 < n15; ++n8) {
                n11 = n9;
                for (n7 = 0; n7 < n14; ++n7) {
                    l2 = 0L;
                    for (n6 = 0; n6 < nArray4.length && (l2 += (long)Math.abs(nArray3[(n8 + (n5 = nArray4[n6]) / n12) * n10 + (n7 + n5 % n12)] - nArray5[n5])) <= 3L * l3; ++n6) {
                    }
                    lArray[n8 * n14 + n7] = l2;
                    l = l3;
                    if (l2 < l3) {
                        l = l2;
                    }
                    l3 = l;
                }
                n9 = n11;
            }
            int[] nArray6 = Align.lum(nArray);
            nArray4 = Align.lum(nArray2);
            nArray = new int[n3 * n4];
            for (n11 = 0; n11 < nArray.length; ++n11) {
                nArray[n11] = nArray2[n11] >>> 24;
            }
            int[] nArray7 = Align.maskIndexes(nArray, 250, 6000);
            if (nArray7.length == 0) {
                return null;
            }
            l2 = Long.MAX_VALUE;
            n6 = 0;
            n7 = Math.max(2, Math.min(n12, n13) / 4);
            n5 = 0;
            int n16 = 0;
            nArray = nArray5;
            n11 = n13;
            n8 = n15;
            n13 = n7;
            nArray2 = nArray6;
            for (n15 = n16; n15 < 5; ++n15) {
                l3 = Long.MAX_VALUE;
                n16 = -1;
                for (n7 = 0; n7 < lArray.length; ++n7) {
                    l = l3;
                    if (lArray[n7] < l3) {
                        l = lArray[n7];
                        n16 = n7;
                    }
                    l3 = l;
                }
                if (n16 < 0 || l3 == Long.MAX_VALUE) break;
                int n17 = n16 % n14;
                int n18 = n16 / n14;
                for (n7 = Math.max(0, n18 - n13); n7 <= Math.min(n8 - 1, n18 + n13); ++n7) {
                    for (n16 = Math.max(0, n17 - n13); n16 <= Math.min(n14 - 1, n17 + n13); ++n16) {
                        lArray[n7 * n14 + n16] = Long.MAX_VALUE;
                    }
                }
                n16 = n17 * n9;
                n7 = n18 * n9;
                int n19 = Math.max(0, n16 - n9 - 2);
                n18 = Math.min(n - n3, n16 + n9 + 2);
                int n20 = Math.max(0, n7 - n9 - 2);
                n16 = Math.min(n2 - n4, n7 + n9 + 2);
                for (n7 = n20; n7 <= n16; ++n7) {
                    int n21 = n19;
                    n17 = n5;
                    l3 = l2;
                    int n22 = n6;
                    n5 = n20;
                    for (n6 = n21; n6 <= n18; ++n6) {
                        l2 = 0L;
                        for (n20 = 0; n20 < nArray7.length && l2 < l3; l2 += (long)Math.abs(nArray2[(n7 + n21 / n3) * n + (n6 + n21 % n3)] - nArray4[n21]), ++n20) {
                            n21 = nArray7[n20];
                        }
                        l = l3;
                        if (l2 < l3) {
                            n22 = n6;
                            n17 = n7;
                            l = l2;
                        }
                        l3 = l;
                    }
                    n20 = n5;
                    n6 = n22;
                    l2 = l3;
                    n5 = n17;
                }
            }
            n = l2 == Long.MAX_VALUE ? 255 : (int)(l2 / (long)nArray7.length);
            return new int[]{n6, n5, n};
        }
        return null;
    }

    private static int[] lum(int[] nArray) {
        int[] nArray2 = new int[nArray.length];
        for (int i = 0; i < nArray.length; ++i) {
            nArray2[i] = Align.lumOf(nArray[i]);
        }
        return nArray2;
    }

    private static int[] lumDown(int[] nArray, int n, int n2, int n3, int[] nArray2) {
        int n4 = n / n3;
        int n5 = n2 / n3;
        int[] nArray3 = new int[n4 * n5];
        for (n2 = 0; n2 < n5; ++n2) {
            for (int i = 0; i < n4; ++i) {
                int n6 = 0;
                int n7 = 255;
                for (int j = 0; j < n3; ++j) {
                    for (int k = 0; k < n3; ++k) {
                        int n8 = nArray[(n2 * n3 + j) * n + i * n3 + k];
                        n6 += Align.lumOf(n8);
                        int n9 = n8 >>> 24;
                        n8 = n7;
                        if (n9 < n7) {
                            n8 = n9;
                        }
                        n7 = n8;
                    }
                }
                nArray3[n2 * n4 + i] = n6 / (n3 * n3);
                if (nArray2 == null) continue;
                nArray2[n2 * n4 + i] = n7;
            }
        }
        return nArray3;
    }

    private static int lumOf(int n) {
        return ((n >> 16 & 0xFF) * 299 + (n >> 8 & 0xFF) * 587 + (n & 0xFF) * 114) / 1000;
    }

    private static int[] maskIndexes(int[] nArray, int n, int n2) {
        int n3;
        int n4;
        int n5 = 0;
        int n6 = nArray.length;
        for (n4 = 0; n4 < n6; ++n4) {
            n3 = n5;
            if (nArray[n4] >= n) {
                n3 = n5 + 1;
            }
            n5 = n3;
        }
        int n7 = Math.max(1, n5 / n2);
        int[] nArray2 = new int[n5 / n7 + 1];
        n2 = 0;
        n3 = 0;
        for (n4 = 0; n4 < nArray.length; ++n4) {
            n5 = n2;
            n6 = n3;
            if (nArray[n4] >= n) {
                n5 = n2;
                if (n3 % n7 == 0) {
                    n5 = n2;
                    if (n2 < nArray2.length) {
                        nArray2[n2] = n4;
                        n5 = n2 + 1;
                    }
                }
                n6 = n3 + 1;
            }
            n2 = n5;
            n3 = n6;
        }
        nArray = new int[n2];
        System.arraycopy(nArray2, 0, nArray, 0, n2);
        return nArray;
    }
}

