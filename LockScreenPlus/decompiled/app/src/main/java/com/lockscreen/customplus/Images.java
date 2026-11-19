package com.lockscreen.customplus;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

/* loaded from: classes3.dex */
final class Images {
    static Bitmap bg;
    static Bitmap cut;
    private static long stamp = -2;

    private Images() {
    }

    static synchronized void ensure(Context c) {
        synchronized (Images.class) {
            long s = Store.prefs(c).getLong("imgStamp", 0L);
            boolean has = Store.hasBackground(c);
            if (s != stamp || (bg == null && has)) {
                stamp = s;
                bg = null;
                cut = null;
                if (has) {
                    try {
                        BitmapFactory.Options o = new BitmapFactory.Options();
                        o.inJustDecodeBounds = true;
                        BitmapFactory.decodeFile(Store.bgFile(c).getPath(), o);
                        int sample = 1;
                        while (o.outWidth / (sample * 2) >= 900) {
                            sample *= 2;
                        }
                        BitmapFactory.Options d = new BitmapFactory.Options();
                        d.inSampleSize = sample;
                        bg = BitmapFactory.decodeFile(Store.bgFile(c).getPath(), d);
                        if (Store.hasCut(c)) {
                            cut = BitmapFactory.decodeFile(Store.cutFile(c).getPath(), d);
                        }
                    } catch (Throwable th) {
                    }
                }
            }
        }
    }
}
