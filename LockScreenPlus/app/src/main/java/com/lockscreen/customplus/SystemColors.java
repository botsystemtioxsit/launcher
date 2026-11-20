package com.lockscreen.customplus;

import android.content.Context;
import android.os.Build;
import com.google.mlkit.common.MlKitException;

/* loaded from: classes3.dex */
final class SystemColors {
    static final String[] GROUPS = {"Акцент 1", "Акцент 2", "Акцент 3", "Нейтральный 1", "Нейтральный 2"};
    private static final String[] KEYS = {"accent1", "accent2", "accent3", "neutral1", "neutral2"};
    static final int[] TONES = {0, 10, 50, 100, MlKitException.CODE_SCANNER_UNAVAILABLE, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE, 400, 500, 600, 700, 800, 900, 1000};

    private SystemColors() {
    }

    static boolean available() {
        return Build.VERSION.SDK_INT >= 31;
    }

    static int get(Context c, int group, int tone) {
        if (!available()) {
            return 0;
        }
        try {
            int id = c.getResources().getIdentifier("system_" + KEYS[group] + "_" + tone, "color", "android");
            if (id == 0) {
                return 0;
            }
            return c.getResources().getColor(id, c.getTheme());
        } catch (Exception e) {
            return 0;
        }
    }

    static int[] auto(Context c) {
        if (!available()) {
            return null;
        }
        int a = get(c, 0, 100);
        int b = get(c, 0, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE);
        int g1 = get(c, 0, 800);
        int g2 = get(c, 2, 400);
        if (a == 0 || b == 0 || g1 == 0 || g2 == 0) {
            return null;
        }
        return new int[]{a, b, g1, g2};
    }
}
