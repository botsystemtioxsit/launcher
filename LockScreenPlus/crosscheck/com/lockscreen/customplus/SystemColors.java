/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  android.content.res.Resources
 *  android.os.Build$VERSION
 */
package com.lockscreen.customplus;

import android.content.Context;
import android.content.res.Resources;
import android.os.Build;

final class SystemColors {
    static final String[] GROUPS = new String[]{"\u0410\u043a\u0446\u0435\u043d\u0442 1", "\u0410\u043a\u0446\u0435\u043d\u0442 2", "\u0410\u043a\u0446\u0435\u043d\u0442 3", "\u041d\u0435\u0439\u0442\u0440\u0430\u043b\u044c\u043d\u044b\u0439 1", "\u041d\u0435\u0439\u0442\u0440\u0430\u043b\u044c\u043d\u044b\u0439 2"};
    private static final String[] KEYS = new String[]{"accent1", "accent2", "accent3", "neutral1", "neutral2"};
    static final int[] TONES = new int[]{0, 10, 50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000};

    private SystemColors() {
    }

    static int[] auto(Context context) {
        if (!SystemColors.available()) {
            return null;
        }
        int n = SystemColors.get(context, 0, 100);
        int n2 = SystemColors.get(context, 0, 300);
        int n3 = SystemColors.get(context, 0, 800);
        int n4 = SystemColors.get(context, 2, 400);
        if (n != 0 && n2 != 0 && n3 != 0 && n4 != 0) {
            return new int[]{n, n2, n3, n4};
        }
        return null;
    }

    static boolean available() {
        boolean bl = Build.VERSION.SDK_INT >= 31;
        return bl;
    }

    static int get(Context context, int n, int n2) {
        block4: {
            if (!SystemColors.available()) {
                return 0;
            }
            try {
                Resources resources = context.getResources();
                StringBuilder stringBuilder = new StringBuilder();
                n = resources.getIdentifier(stringBuilder.append("system_").append(KEYS[n]).append("_").append(n2).toString(), "color", "android");
                if (n != 0) break block4;
                return 0;
            }
            catch (Exception exception) {
                return 0;
            }
        }
        n = context.getResources().getColor(n, context.getTheme());
        return n;
    }
}

