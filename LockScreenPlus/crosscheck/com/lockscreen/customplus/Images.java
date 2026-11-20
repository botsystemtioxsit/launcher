/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  android.graphics.Bitmap
 *  android.graphics.BitmapFactory
 *  android.graphics.BitmapFactory$Options
 */
package com.lockscreen.customplus;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import com.lockscreen.customplus.Store;

final class Images {
    static Bitmap bg;
    static Bitmap cut;
    private static long stamp;

    static {
        stamp = -2L;
    }

    private Images() {
    }

    /*
     * WARNING - Removed back jump from a try to a catch block - possible behaviour change.
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    static void ensure(Context context) {
        synchronized (Images.class) {
            block10: {
                int n;
                Bitmap bitmap;
                long l = Store.prefs(context).getLong("imgStamp", 0L);
                boolean bl = Store.hasBackground(context);
                if (!(l != stamp || (bitmap = bg) == null && bl)) {
                    return;
                }
                stamp = l;
                bg = null;
                cut = null;
                if (!bl) {
                    return;
                }
                try {
                    bitmap = new BitmapFactory.Options();
                    bitmap.inJustDecodeBounds = true;
                    BitmapFactory.decodeFile((String)Store.bgFile(context).getPath(), (BitmapFactory.Options)bitmap);
                    n = 1;
                    while (bitmap.outWidth / (n * 2) >= 900) {
                        n *= 2;
                    }
                }
                catch (Throwable throwable) {
                    // empty catch block
                    break block10;
                }
                {
                    bitmap = new BitmapFactory.Options();
                    bitmap.inSampleSize = n;
                    bg = BitmapFactory.decodeFile((String)Store.bgFile(context).getPath(), (BitmapFactory.Options)bitmap);
                    if (!Store.hasCut(context)) break block10;
                    cut = BitmapFactory.decodeFile((String)Store.cutFile(context).getPath(), (BitmapFactory.Options)bitmap);
                }
            }
            return;
        }
    }
}

