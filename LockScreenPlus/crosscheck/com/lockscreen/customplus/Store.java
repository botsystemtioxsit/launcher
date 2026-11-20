/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  android.content.IntentFilter
 *  android.content.SharedPreferences
 *  android.graphics.Bitmap
 *  android.graphics.Bitmap$CompressFormat
 *  android.graphics.Bitmap$Config
 *  android.graphics.BitmapFactory
 *  android.graphics.BitmapFactory$Options
 *  android.graphics.Canvas
 *  android.graphics.Matrix
 *  android.media.ExifInterface
 *  android.net.Uri
 *  org.json.JSONArray
 *  org.json.JSONObject
 */
package com.lockscreen.customplus;

import android.content.Context;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.media.ExifInterface;
import android.net.Uri;
import com.google.android.gms.common.moduleinstall.ModuleInstall;
import com.google.android.gms.common.moduleinstall.ModuleInstallRequest;
import com.google.android.gms.tasks.Tasks;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.segmentation.Segmentation;
import com.google.mlkit.vision.segmentation.SegmentationMask;
import com.google.mlkit.vision.segmentation.Segmenter;
import com.google.mlkit.vision.segmentation.selfie.SelfieSegmenterOptions;
import com.google.mlkit.vision.segmentation.subject.SubjectSegmentation;
import com.google.mlkit.vision.segmentation.subject.SubjectSegmenter;
import com.google.mlkit.vision.segmentation.subject.SubjectSegmenterOptions;
import com.lockscreen.customplus.Align;
import com.lockscreen.customplus.Design;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URLEncoder;
import java.nio.ByteBuffer;
import java.util.Locale;
import java.util.concurrent.TimeUnit;
import org.json.JSONArray;
import org.json.JSONObject;

final class Store {
    static final int MAX_SIDE = 2560;
    private static long batteryAt;
    private static String batteryCache;

    static {
        batteryCache = "";
    }

    private Store() {
    }

    static void addPreset(Context context, String string2, Design design) throws Exception {
        JSONArray jSONArray = Store.presets(context);
        jSONArray.put((Object)new JSONObject().put("name", (Object)string2).put("design", (Object)design.toJson()));
        Store.prefs(context).edit().putString("presets", jSONArray.toString()).apply();
    }

    static String autoCut(Context context) throws Exception {
        if (Store.hasBackground(context)) {
            Bitmap bitmap = BitmapFactory.decodeFile((String)Store.bgFile(context).getPath());
            if (bitmap != null) {
                Bitmap bitmap2;
                Bitmap bitmap3 = bitmap;
                int n = Math.max(bitmap.getWidth(), bitmap.getHeight());
                if (n > 1024) {
                    float f = 1024.0f / (float)n;
                    bitmap3 = Bitmap.createScaledBitmap((Bitmap)bitmap, (int)Math.round((float)bitmap.getWidth() * f), (int)Math.round((float)bitmap.getHeight() * f), (boolean)true);
                }
                Bitmap bitmap4 = null;
                String string2 = null;
                try {
                    bitmap4 = bitmap2 = Store.subjectCut(context, bitmap3);
                }
                catch (Throwable throwable) {
                    string2 = Store.describe(throwable);
                }
                String string3 = "\u041e\u0431\u044a\u0435\u043a\u0442 \u0432\u044b\u0440\u0435\u0437\u0430\u043d \u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438";
                bitmap2 = bitmap4;
                if (bitmap4 == null) {
                    try {
                        bitmap2 = Store.selfieCut(bitmap3);
                        string3 = "\u0412\u044b\u0440\u0435\u0437\u0430\u043d \u0447\u0435\u043b\u043e\u0432\u0435\u043a \u043d\u0430 \u0444\u043e\u0442\u043e (\u043c\u043e\u0434\u0435\u043b\u044c \u00ab\u043e\u0431\u044a\u0435\u043a\u0442\u00bb \u043e\u0442 Google \u043d\u0435\u0434\u043e\u0441\u0442\u0443\u043f\u043d\u0430)";
                    }
                    catch (Throwable throwable) {
                        throw new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0432\u044b\u0440\u0435\u0437\u0430\u0442\u044c.\n\n\u041c\u043e\u0434\u0435\u043b\u044c \u00ab\u043e\u0431\u044a\u0435\u043a\u0442\u00bb: " + string2 + "\n\u041c\u043e\u0434\u0435\u043b\u044c \u00ab\u0447\u0435\u043b\u043e\u0432\u0435\u043a\u00bb: " + Store.describe(throwable));
                    }
                }
                bitmap4 = bitmap2;
                if (bitmap2.getWidth() != bitmap.getWidth() || bitmap2.getHeight() != bitmap.getHeight()) {
                    bitmap4 = Bitmap.createScaledBitmap((Bitmap)bitmap2, (int)bitmap.getWidth(), (int)bitmap.getHeight(), (boolean)true);
                }
                Store.savePng(bitmap4, Store.rawCutFile(context), true);
                Store.prefs(context).edit().putInt("cutX", 0).putInt("cutY", 0).apply();
                Store.compose(context);
                return string3 + ". \u0415\u0441\u043b\u0438 \u0432\u044b\u0440\u0435\u0437 \u043d\u0435\u0442\u043e\u0447\u043d\u044b\u0439, \u043f\u043e\u0434\u0432\u0438\u043d\u044c\u0442\u0435 \u0438\u043b\u0438 \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0441\u0432\u043e\u0439 PNG";
            }
            throw new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0447\u0438\u0442\u0430\u0442\u044c \u0444\u043e\u0442\u043e");
        }
        throw new IOException("\u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0444\u043e\u0442\u043e");
    }

    /*
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    static String batteryText(Context object) {
        synchronized (Store.class) {
            long l = System.currentTimeMillis();
            if (l - batteryAt < 30000L && !batteryCache.isEmpty()) {
                return batteryCache;
            }
            batteryAt = l;
            object = object.getApplicationContext();
            Object object2 = new IntentFilter("android.intent.action.BATTERY_CHANGED");
            if ((object = object.registerReceiver(null, (IntentFilter)object2)) == null) return batteryCache;
            int n = object.getIntExtra("level", -1);
            int n2 = object.getIntExtra("scale", 100);
            int n3 = object.getIntExtra("status", -1);
            n3 = n3 != 2 && n3 != 5 ? 0 : 1;
            if (n < 0) return batteryCache;
            if (n2 <= 0) return batteryCache;
            object2 = new StringBuilder();
            object = n3 != 0 ? "\u26a1 " : "";
            batteryCache = ((StringBuilder)object2).append((String)object).append(n * 100 / n2).append("%").toString();
            return batteryCache;
        }
    }

    static File bgFile(Context context) {
        return new File(context.getFilesDir(), "bg.jpg");
    }

    /*
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    private static int[] bounds(Context object, Uri object2) throws IOException {
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inJustDecodeBounds = true;
        if ((object = object.getContentResolver().openInputStream(object2)) != null) {
            block6: {
                BitmapFactory.decodeStream((InputStream)object, null, (BitmapFactory.Options)options);
                if (object == null) break block6;
                ((InputStream)object).close();
            }
            if (options.outWidth <= 0) throw new IOException("\u042d\u0442\u043e \u043d\u0435 \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0430");
            if (options.outHeight <= 0) throw new IOException("\u042d\u0442\u043e \u043d\u0435 \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0430");
            return new int[]{options.outWidth, options.outHeight};
        }
        try {
            object2 = new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043e\u0442\u043a\u0440\u044b\u0442\u044c \u0444\u0430\u0439\u043b");
            throw object2;
        }
        catch (Throwable throwable) {}
        if (object == null) throw throwable;
        try {
            ((InputStream)object).close();
            throw throwable;
        }
        catch (Throwable throwable) {
            throwable.addSuppressed(throwable);
        }
        throw throwable;
    }

    private static void compose(Context context) throws IOException {
        Bitmap bitmap;
        SharedPreferences sharedPreferences = Store.prefs(context);
        Bitmap bitmap2 = BitmapFactory.decodeFile((String)Store.rawCutFile(context).getPath());
        if (bitmap2 == null) {
            return;
        }
        Bitmap bitmap3 = null;
        try {
            bitmap3 = bitmap = Bitmap.createBitmap((int)sharedPreferences.getInt("bgW", bitmap2.getWidth()), (int)sharedPreferences.getInt("bgH", bitmap2.getHeight()), (Bitmap.Config)Bitmap.Config.ARGB_8888);
            bitmap3 = bitmap;
        }
        catch (Throwable throwable) {
            bitmap2.recycle();
            if (bitmap3 != null) {
                bitmap3.recycle();
            }
            throw throwable;
        }
        Canvas canvas = new Canvas(bitmap);
        bitmap3 = bitmap;
        canvas.drawBitmap(bitmap2, (float)sharedPreferences.getInt("cutX", 0), (float)sharedPreferences.getInt("cutY", 0), null);
        bitmap3 = bitmap;
        Store.savePng(bitmap, Store.cutFile(context), true);
        bitmap2.recycle();
        if (bitmap != null) {
            bitmap.recycle();
        }
        Store.touch(context);
    }

    /*
     * Loose catch block
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    private static File copyToCache(Context object, Uri object2) throws IOException {
        block17: {
            int n;
            File file = new File(object.getCacheDir(), "pick-" + System.nanoTime() + ".tmp");
            object = object.getContentResolver().openInputStream((Uri)object2);
            object2 = new FileOutputStream(file);
            if (object == null) break block17;
            Object object3 = new byte[65536];
            long l = 0L;
            while ((n = ((InputStream)object).read((byte[])object3)) > 0) {
                ((FileOutputStream)object2).write((byte[])object3, 0, n);
                if ((l += (long)n) <= 0xC800000L) continue;
                object3 = new IOException;
                object3("\u0424\u0430\u0439\u043b \u0441\u043b\u0438\u0448\u043a\u043e\u043c \u0431\u043e\u043b\u044c\u0448\u043e\u0439 (\u0431\u043e\u043b\u044c\u0448\u0435 200 \u041c\u0411)");
                throw object3;
            }
            ((FileOutputStream)object2).close();
            if (object == null) return file;
            {
                catch (Throwable throwable) {
                    if (object == null) throw throwable;
                    try {
                        ((InputStream)object).close();
                        throw throwable;
                    }
                    catch (Throwable throwable2) {
                        try {
                            throwable.addSuppressed(throwable2);
                            throw throwable;
                        }
                        catch (SecurityException securityException) {
                            file.delete();
                            throw new IOException("\u041d\u0435\u0442 \u0434\u043e\u0441\u0442\u0443\u043f\u0430 \u043a \u0444\u0430\u0439\u043b\u0443. \u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0443 \u0435\u0449\u0451 \u0440\u0430\u0437");
                        }
                        catch (IOException iOException) {
                            file.delete();
                            throw iOException;
                        }
                    }
                }
            }
            ((InputStream)object).close();
            return file;
        }
        try {
            IOException iOException = new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043e\u0442\u043a\u0440\u044b\u0442\u044c \u0432\u044b\u0431\u0440\u0430\u043d\u043d\u044b\u0439 \u0444\u0430\u0439\u043b");
            throw iOException;
        }
        catch (Throwable throwable) {}
        try {
            ((FileOutputStream)object2).close();
            throw throwable;
        }
        catch (Throwable throwable) {
            throwable.addSuppressed(throwable);
            throw throwable;
        }
    }

    static File cutFile(Context context) {
        return new File(context.getFilesDir(), "cut.png");
    }

    /*
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    private static Bitmap decodeScaled(Context object, Uri object2, float f, int n) throws IOException {
        Throwable throwable2;
        block11: {
            block13: {
                int n2;
                int n3;
                block12: {
                    int[] nArray;
                    block10: {
                        nArray = Store.bounds((Context)object, object2);
                        n3 = 1;
                        while (1.0f / (float)(n3 * 2) >= f) {
                            n3 *= 2;
                        }
                        BitmapFactory.Options options = new BitmapFactory.Options();
                        options.inSampleSize = n3;
                        if ((object = object.getContentResolver().openInputStream(object2)) == null) break block11;
                        try {
                            object2 = BitmapFactory.decodeStream((InputStream)object, null, (BitmapFactory.Options)options);
                            if (object == null) break block10;
                        }
                        catch (Throwable throwable2) {}
                        ((InputStream)object).close();
                    }
                    if (object2 == null) throw new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0447\u0438\u0442\u0430\u0442\u044c \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0443");
                    n2 = Math.max(1, Math.round((float)nArray[0] * f));
                    n3 = Math.max(1, Math.round((float)nArray[1] * f));
                    if (object2.getWidth() != n2) break block12;
                    object = object2;
                    if (object2.getHeight() == n3) break block13;
                }
                if ((object = Bitmap.createScaledBitmap((Bitmap)object2, (int)n2, (int)n3, (boolean)true)) != object2) {
                    object2.recycle();
                }
            }
            object2 = object;
            if (n == 0) return object2;
            object2 = new Matrix();
            object2.postRotate((float)n);
            object2 = Bitmap.createBitmap((Bitmap)object, (int)0, (int)0, (int)object.getWidth(), (int)object.getHeight(), (Matrix)object2, (boolean)true);
            if (object2 == object) return object2;
            object.recycle();
            return object2;
        }
        object2 = new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043e\u0442\u043a\u0440\u044b\u0442\u044c \u0444\u0430\u0439\u043b");
        throw object2;
        if (object == null) throw throwable2;
        try {
            ((InputStream)object).close();
            throw throwable2;
        }
        catch (Throwable throwable3) {
            throwable2.addSuppressed(throwable3);
        }
        throw throwable2;
    }

    static void deletePreset(Context context, int n) {
        JSONArray jSONArray = Store.presets(context);
        JSONArray jSONArray2 = new JSONArray();
        for (int i = 0; i < jSONArray.length(); ++i) {
            if (i == n) continue;
            jSONArray2.put(jSONArray.opt(i));
        }
        Store.prefs(context).edit().putString("presets", jSONArray2.toString()).apply();
    }

    private static String describe(Throwable object) {
        CharSequence charSequence = ((Throwable)object).getMessage();
        Throwable throwable = ((Throwable)object).getCause();
        String string2 = "";
        String string3 = charSequence;
        if (throwable != null) {
            string3 = charSequence;
            if (throwable.getMessage() != null) {
                StringBuilder stringBuilder = new StringBuilder();
                string3 = charSequence == null ? "" : (String)charSequence + " / ";
                string3 = stringBuilder.append(string3).append(throwable.getMessage()).toString();
            }
        }
        charSequence = new StringBuilder().append(object.getClass().getSimpleName());
        object = string3 == null ? string2 : ": " + string3;
        return ((StringBuilder)charSequence).append((String)object).toString();
    }

    /*
     * Loose catch block
     * Enabled force condition propagation
     * Lifted jumps to return sites
     */
    private static int exifRotation(Context object, Uri uri) {
        int n;
        block15: {
            block14: {
                object = object.getContentResolver().openInputStream(uri);
                if (object != null) break block14;
                if (object == null) return 0;
                ((InputStream)object).close();
                return 0;
            }
            uri = new ExifInterface((InputStream)object);
            n = uri.getAttributeInt("Orientation", 1);
            if (n != 6) break block15;
            if (object == null) return 90;
            ((InputStream)object).close();
            return 90;
        }
        if (n == 3) {
            if (object == null) return 180;
            ((InputStream)object).close();
            return 180;
        }
        if (n == 8) {
            if (object == null) return 270;
            ((InputStream)object).close();
            return 270;
        }
        if (object == null) return 0;
        ((InputStream)object).close();
        return 0;
        catch (Throwable throwable) {
            if (object == null) throw throwable;
            try {
                ((InputStream)object).close();
                throw throwable;
            }
            catch (Throwable throwable2) {
                try {
                    throwable.addSuppressed(throwable2);
                    throw throwable;
                }
                catch (Exception exception) {
                    // empty catch block
                }
            }
        }
        return 0;
    }

    /*
     * Exception decompiling
     */
    static String fetchText(String var0) throws IOException {
        /*
         * This method has failed to decompile.  When submitting a bug report, please provide this stack trace, and (if you hold appropriate legal rights) the relevant class file.
         * 
         * org.benf.cfr.reader.util.ConfusedCFRException: Started 2 blocks at once
         *     at org.benf.cfr.reader.bytecode.analysis.opgraph.Op04StructuredStatement.getStartingBlocks(Op04StructuredStatement.java:412)
         *     at org.benf.cfr.reader.bytecode.analysis.opgraph.Op04StructuredStatement.buildNestedBlocks(Op04StructuredStatement.java:487)
         *     at org.benf.cfr.reader.bytecode.analysis.opgraph.Op03SimpleStatement.createInitialStructuredBlock(Op03SimpleStatement.java:736)
         *     at org.benf.cfr.reader.bytecode.CodeAnalyser.getAnalysisInner(CodeAnalyser.java:850)
         *     at org.benf.cfr.reader.bytecode.CodeAnalyser.getAnalysisOrWrapFail(CodeAnalyser.java:278)
         *     at org.benf.cfr.reader.bytecode.CodeAnalyser.getAnalysis(CodeAnalyser.java:201)
         *     at org.benf.cfr.reader.entities.attributes.AttributeCode.analyse(AttributeCode.java:94)
         *     at org.benf.cfr.reader.entities.Method.analyse(Method.java:531)
         *     at org.benf.cfr.reader.entities.ClassFile.analyseMid(ClassFile.java:1055)
         *     at org.benf.cfr.reader.entities.ClassFile.analyseTop(ClassFile.java:942)
         *     at org.benf.cfr.reader.Driver.doJarVersionTypes(Driver.java:257)
         *     at org.benf.cfr.reader.Driver.doJar(Driver.java:139)
         *     at org.benf.cfr.reader.CfrDriverImpl.analyse(CfrDriverImpl.java:76)
         *     at org.benf.cfr.reader.Main.main(Main.java:54)
         */
        throw new IllegalStateException("Decompilation failed");
    }

    static double[] fetchWeather(double d, double d2) throws IOException {
        try {
            String string2 = String.format(Locale.US, "https://api.open-meteo.com/v1/forecast?latitude=%.4f&longitude=%.4f&current=weather_code,temperature_2m", d, d2);
            JSONObject jSONObject = new JSONObject(Store.httpGet(string2, 60000));
            string2 = jSONObject.getJSONObject("current");
            d = string2.getInt("weather_code");
            d2 = string2.getDouble("temperature_2m");
        }
        catch (Exception exception) {
            throw new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u043e\u043b\u0443\u0447\u0438\u0442\u044c \u043f\u043e\u0433\u043e\u0434\u0443");
        }
        catch (IOException iOException) {
            throw iOException;
        }
        return new double[]{d, d2};
    }

    private static int[] fileSize(File file) {
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inJustDecodeBounds = true;
        BitmapFactory.decodeFile((String)file.getPath(), (BitmapFactory.Options)options);
        return new int[]{options.outWidth, options.outHeight};
    }

    static Place geocode(String object) throws IOException {
        block5: {
            Object object2 = new StringBuilder();
            String string2 = ((StringBuilder)object2).append("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&format=json&name=").append(URLEncoder.encode(((String)object).trim(), "UTF-8")).toString();
            object2 = new JSONObject(Store.httpGet(string2, 60000));
            object2 = object2.optJSONArray("results");
            if (object2 == null) break block5;
            if (object2.length() == 0) break block5;
            string2 = object2.getJSONObject(0);
            object2 = new Place();
            ((Place)object2).name = string2.optString("name", (String)object);
            ((Place)object2).lat = string2.getDouble("latitude");
            ((Place)object2).lon = string2.getDouble("longitude");
            return object2;
        }
        try {
            object = new IOException("\u0422\u0430\u043a\u043e\u0439 \u0433\u043e\u0440\u043e\u0434 \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d");
            throw object;
        }
        catch (Exception exception) {
            throw new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043d\u0430\u0439\u0442\u0438 \u0433\u043e\u0440\u043e\u0434");
        }
        catch (IOException iOException) {
            throw iOException;
        }
    }

    static boolean hasBackground(Context context) {
        return Store.bgFile(context).exists();
    }

    static boolean hasCut(Context context) {
        return Store.cutFile(context).exists();
    }

    /*
     * Exception decompiling
     */
    static String httpGet(String var0, int var1_1) throws IOException {
        /*
         * This method has failed to decompile.  When submitting a bug report, please provide this stack trace, and (if you hold appropriate legal rights) the relevant class file.
         * 
         * org.benf.cfr.reader.util.ConfusedCFRException: Tried to end blocks [8[CATCHBLOCK], 9[CATCHBLOCK]], but top level block is 7[TRYBLOCK]
         *     at org.benf.cfr.reader.bytecode.analysis.opgraph.Op04StructuredStatement.processEndingBlocks(Op04StructuredStatement.java:435)
         *     at org.benf.cfr.reader.bytecode.analysis.opgraph.Op04StructuredStatement.buildNestedBlocks(Op04StructuredStatement.java:484)
         *     at org.benf.cfr.reader.bytecode.analysis.opgraph.Op03SimpleStatement.createInitialStructuredBlock(Op03SimpleStatement.java:736)
         *     at org.benf.cfr.reader.bytecode.CodeAnalyser.getAnalysisInner(CodeAnalyser.java:850)
         *     at org.benf.cfr.reader.bytecode.CodeAnalyser.getAnalysisOrWrapFail(CodeAnalyser.java:278)
         *     at org.benf.cfr.reader.bytecode.CodeAnalyser.getAnalysis(CodeAnalyser.java:201)
         *     at org.benf.cfr.reader.entities.attributes.AttributeCode.analyse(AttributeCode.java:94)
         *     at org.benf.cfr.reader.entities.Method.analyse(Method.java:531)
         *     at org.benf.cfr.reader.entities.ClassFile.analyseMid(ClassFile.java:1055)
         *     at org.benf.cfr.reader.entities.ClassFile.analyseTop(ClassFile.java:942)
         *     at org.benf.cfr.reader.Driver.doJarVersionTypes(Driver.java:257)
         *     at org.benf.cfr.reader.Driver.doJar(Driver.java:139)
         *     at org.benf.cfr.reader.CfrDriverImpl.analyse(CfrDriverImpl.java:76)
         *     at org.benf.cfr.reader.Main.main(Main.java:54)
         */
        throw new IllegalStateException("Decompilation failed");
    }

    static void importBackground(Context context, Uri object) throws IOException {
        object = Store.copyToCache(context, (Uri)object);
        try {
            Store.importBackgroundFile(context, Uri.fromFile((File)object));
            return;
        }
        finally {
            ((File)object).delete();
        }
    }

    private static void importBackgroundFile(Context context, Uri uri) throws IOException {
        int[] nArray = Store.bounds(context, uri);
        int n = Store.exifRotation(context, uri);
        int n2 = n != 90 && n != 270 ? 0 : 1;
        int n3 = n2 != 0 ? nArray[1] : nArray[0];
        n2 = n2 != 0 ? nArray[0] : nArray[1];
        float f = Math.min(1.0f, 2560.0f / (float)Math.max(n3, n2));
        uri = Store.decodeScaled(context, uri, f, n);
        try {
            Store.savePng((Bitmap)uri, Store.bgFile(context), false);
            Store.removeCutFiles(context);
            Store.prefs(context).edit().putInt("bgW", uri.getWidth()).putInt("bgH", uri.getHeight()).putFloat("scale", f).apply();
            Store.touch(context);
            return;
        }
        finally {
            uri.recycle();
        }
    }

    static String importCut(Context object, Uri object2) throws IOException {
        if (Store.hasBackground(object)) {
            object2 = Store.copyToCache(object, (Uri)object2);
            try {
                object = Store.importCutFile(object, Uri.fromFile((File)object2));
                return object;
            }
            finally {
                ((File)object2).delete();
            }
        }
        throw new IOException("\u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0444\u043e\u0442\u043e");
    }

    /*
     * WARNING - combined exceptions agressively - possible behaviour change.
     * WARNING - void declaration
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    private static String importCutFile(Context object, Uri object2) throws IOException {
        void var1_4;
        SharedPreferences sharedPreferences = Store.prefs(object);
        Bitmap bitmap = Store.decodeScaled(object, (Uri)var1_4, sharedPreferences.getFloat("scale", 1.0f), Store.exifRotation(object, (Uri)var1_4));
        try {
            int n = sharedPreferences.getInt("bgW", 0);
            int n2 = sharedPreferences.getInt("bgH", 0);
            int n3 = bitmap.getWidth();
            int n4 = bitmap.getHeight();
            if (n3 <= n + 1 && n4 <= n2 + 1) {
                void var1_10;
                if (Math.abs(n3 - n) <= 1 && Math.abs(n4 - n2) <= 1) {
                    String string2 = "\u0412\u044b\u0440\u0435\u0437 \u0442\u043e\u0433\u043e \u0436\u0435 \u0440\u0430\u0437\u043c\u0435\u0440\u0430, \u0447\u0442\u043e \u0438 \u0444\u043e\u0442\u043e \u2014 \u043f\u043e\u0441\u0442\u0430\u0432\u0438\u043b \u043a\u0430\u043a \u0435\u0441\u0442\u044c";
                    n3 = 0;
                    n4 = 0;
                } else {
                    Bitmap bitmap2 = BitmapFactory.decodeFile((String)Store.bgFile(object).getPath());
                    if (bitmap2 == null) {
                        IOException iOException = new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0447\u0438\u0442\u0430\u0442\u044c \u0444\u043e\u0442\u043e");
                        throw iOException;
                    }
                    int[] nArray = new int[bitmap2.getWidth() * bitmap2.getHeight()];
                    bitmap2.getPixels(nArray, 0, bitmap2.getWidth(), 0, 0, bitmap2.getWidth(), bitmap2.getHeight());
                    int[] nArray2 = new int[n3 * n4];
                    bitmap.getPixels(nArray2, 0, n3, 0, 0, n3, n4);
                    nArray2 = Align.find(nArray, bitmap2.getWidth(), bitmap2.getHeight(), nArray2, n3, n4);
                    bitmap2.recycle();
                    if (nArray2 == null) {
                        n3 = (n - n3) / 2;
                        n4 = (n2 - n4) / 2;
                        String string3 = "\u041d\u0435 \u0441\u043c\u043e\u0433 \u0441\u043e\u0432\u043c\u0435\u0441\u0442\u0438\u0442\u044c \u0441\u0430\u043c \u2014 \u043f\u043e\u0441\u0442\u0430\u0432\u0438\u043b \u043f\u043e \u0446\u0435\u043d\u0442\u0440\u0443. \u041f\u043e\u0434\u0432\u0438\u043d\u044c\u0442\u0435 \u0441\u0442\u0440\u0435\u043b\u043a\u0430\u043c\u0438";
                    } else {
                        n3 = nArray2[0];
                        n4 = nArray2[1];
                        if (nArray2[2] <= 25) {
                            String string4 = "\u0412\u044b\u0440\u0435\u0437 \u0441\u043e\u0432\u043c\u0435\u0449\u0451\u043d \u0441 \u0444\u043e\u0442\u043e \u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438";
                        } else {
                            String string5 = "\u041d\u0435 \u0443\u0432\u0435\u0440\u0435\u043d \u0432 \u0441\u043e\u0432\u043c\u0435\u0449\u0435\u043d\u0438\u0438. \u041f\u0440\u043e\u0432\u0435\u0440\u044c\u0442\u0435 \u0438 \u043f\u043e\u0434\u0432\u0438\u043d\u044c\u0442\u0435 \u0441\u0442\u0440\u0435\u043b\u043a\u0430\u043c\u0438";
                        }
                    }
                }
                Store.savePng(bitmap, Store.rawCutFile(object), true);
                sharedPreferences.edit().putInt("cutX", n3).putInt("cutY", n4).apply();
                Store.compose(object);
                return var1_10;
            }
            IOException iOException = new IOException("\u0412\u044b\u0440\u0435\u0437 \u0431\u043e\u043b\u044c\u0448\u0435 \u0444\u043e\u0442\u043e. \u041d\u0443\u0436\u0435\u043d PNG, \u0441\u0434\u0435\u043b\u0430\u043d\u043d\u044b\u0439 \u0438\u0437 \u044d\u0442\u043e\u0433\u043e \u0436\u0435 \u0444\u043e\u0442\u043e");
            throw iOException;
        }
        finally {
            bitmap.recycle();
        }
    }

    static void nudge(Context context, int n, int n2) throws IOException {
        if (!Store.rawCutFile(context).exists()) {
            return;
        }
        SharedPreferences sharedPreferences = Store.prefs(context);
        int[] nArray = Store.fileSize(Store.rawCutFile(context));
        int n3 = sharedPreferences.getInt("bgW", 0);
        int n4 = sharedPreferences.getInt("bgH", 0);
        n = Math.max(0, Math.min(Math.max(0, n3 - nArray[0]), sharedPreferences.getInt("cutX", 0) + n));
        n2 = Math.max(0, Math.min(Math.max(0, n4 - nArray[1]), sharedPreferences.getInt("cutY", 0) + n2));
        sharedPreferences.edit().putInt("cutX", n).putInt("cutY", n2).apply();
        Store.compose(context);
    }

    static SharedPreferences prefs(Context context) {
        return context.getApplicationContext().getSharedPreferences("settings", 0);
    }

    static JSONArray presets(Context context) {
        try {
            context = new JSONArray(Store.prefs(context).getString("presets", "[]"));
            return context;
        }
        catch (Exception exception) {
            return new JSONArray();
        }
    }

    static File rawCutFile(Context context) {
        return new File(context.getFilesDir(), "cutraw.png");
    }

    static void removeCut(Context context) {
        Store.removeCutFiles(context);
        Store.touch(context);
    }

    private static void removeCutFiles(Context context) {
        Store.cutFile(context).delete();
        Store.rawCutFile(context).delete();
    }

    /*
     * Unable to fully structure code
     * Could not resolve type clashes
     */
    private static void savePng(Bitmap var0, File var1_2, boolean var2_4) throws IOException {
        block7: {
            block8: {
                var4_5 = new File(var1_2.getPath() + ".tmp");
                var3_6 = new FileOutputStream(var4_5);
                if (!var2_4) ** GOTO lbl7
                var2_4 = var0 /* !! */ .compress(Bitmap.CompressFormat.PNG, 100, (OutputStream)var3_6);
                break block8;
lbl7:
                // 1 sources

                var2_4 = var0 /* !! */ .compress(Bitmap.CompressFormat.JPEG, 92, (OutputStream)var3_6);
            }
            if (!var2_4) break block7;
            var3_6.close();
            if (var1_2.exists() && !var1_2.delete()) {
                throw new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0437\u0430\u043c\u0435\u043d\u0438\u0442\u044c \u0444\u0430\u0439\u043b");
            }
            if (var4_5.renameTo(var1_2)) {
                return;
            }
            throw new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u0444\u0430\u0439\u043b");
        }
        try {
            var0 /* !! */  = new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0443");
            throw var0 /* !! */ ;
        }
        catch (Throwable var0_1) {
            try {
                var3_6.close();
            }
            catch (Throwable var1_3) {
                var0_1.addSuppressed(var1_3);
            }
            throw var0_1;
        }
    }

    static void saveWeather(Context context, double[] dArray) {
        Store.prefs(context).edit().putInt("weatherCode", (int)dArray[0]).putFloat("weatherTemp", (float)dArray[1]).putLong("weatherAt", System.currentTimeMillis()).apply();
    }

    private static Bitmap selfieCut(Bitmap object) throws Exception {
        Segmenter segmenter = Segmentation.getClient(new SelfieSegmenterOptions.Builder().setDetectorMode(2).build());
        Object object2 = Tasks.await(segmenter.process(InputImage.fromBitmap(object, 0)), 60L, TimeUnit.SECONDS);
        int n = ((SegmentationMask)object2).getWidth();
        int n2 = ((SegmentationMask)object2).getHeight();
        object2 = ((SegmentationMask)object2).getBuffer();
        Bitmap bitmap = Bitmap.createScaledBitmap((Bitmap)object, (int)n, (int)n2, (boolean)true);
        object = new int[n * n2];
        bitmap.getPixels((int[])object, 0, n, 0, 0, n, n2);
        int n3 = 0;
        int n4 = 0;
        while (true) {
            int n5;
            int n6;
            block8: {
                if (n3 >= ((Bitmap)object).length) break;
                n6 = Math.max(0, Math.min(255, Math.round((((ByteBuffer)object2).getFloat() - 0.25f) / 0.5f * 255.0f)));
                n5 = n4;
                if (n6 <= 128) break block8;
                n5 = n4 + 1;
            }
            object[n3] = (Bitmap)(object[n3] & 0xFFFFFF | n6 << 24);
            ++n3;
            n4 = n5;
        }
        try {
            if (n4 >= ((Bitmap)object).length / 200) {
                object2 = Bitmap.createBitmap((int)n, (int)n2, (Bitmap.Config)Bitmap.Config.ARGB_8888);
                object2.setPixels((int[])object, 0, n, 0, 0, n, n2);
                return object2;
            }
            object = new IOException("\u0447\u0435\u043b\u043e\u0432\u0435\u043a \u043d\u0430 \u0444\u043e\u0442\u043e \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d");
            throw object;
        }
        catch (Throwable throwable) {
            throw throwable;
        }
        finally {
            segmenter.close();
        }
    }

    private static Bitmap subjectCut(Context object, Bitmap bitmap) throws Exception {
        block3: {
            SubjectSegmenter subjectSegmenter = SubjectSegmentation.getClient(new SubjectSegmenterOptions.Builder().enableForegroundBitmap().build());
            try {
                ModuleInstallRequest moduleInstallRequest = ModuleInstallRequest.newBuilder().addApi(subjectSegmenter).build();
                Tasks.await(ModuleInstall.getClient(object).installModules(moduleInstallRequest), 240L, TimeUnit.SECONDS);
                object = Tasks.await(subjectSegmenter.process(InputImage.fromBitmap(bitmap, 0)), 120L, TimeUnit.SECONDS).getForegroundBitmap();
                if (object == null) break block3;
                subjectSegmenter.close();
                return object;
            }
            catch (Throwable throwable) {
                subjectSegmenter.close();
                throw throwable;
            }
        }
        object = new IOException("\u043f\u0443\u0441\u0442\u043e\u0439 \u0440\u0435\u0437\u0443\u043b\u044c\u0442\u0430\u0442");
        throw object;
    }

    private static void touch(Context context) {
        Store.prefs(context).edit().putLong("imgStamp", System.nanoTime()).apply();
    }

    static String weatherName(int n) {
        if (n == 0) {
            return "\u042f\u0441\u043d\u043e";
        }
        if (n != 1 && n != 2) {
            if (n == 3) {
                return "\u041f\u0430\u0441\u043c\u0443\u0440\u043d\u043e";
            }
            if (n != 45 && n != 48) {
                if (n >= 51 && n <= 57) {
                    return "\u041c\u043e\u0440\u043e\u0441\u044c";
                }
                if (n >= 61 && n <= 67) {
                    return "\u0414\u043e\u0436\u0434\u044c";
                }
                if (n >= 71 && n <= 77) {
                    return "\u0421\u043d\u0435\u0433";
                }
                if (n >= 80 && n <= 82) {
                    return "\u041b\u0438\u0432\u0435\u043d\u044c";
                }
                if (n != 85 && n != 86) {
                    if (n >= 95 && n <= 99) {
                        return "\u0413\u0440\u043e\u0437\u0430";
                    }
                    return "\u0411\u0435\u0437 \u043e\u0441\u0430\u0434\u043a\u043e\u0432";
                }
                return "\u0421\u043d\u0435\u0433\u043e\u043f\u0430\u0434";
            }
            return "\u0422\u0443\u043c\u0430\u043d";
        }
        return "\u041c\u0430\u043b\u043e\u043e\u0431\u043b\u0430\u0447\u043d\u043e";
    }

    static final class Place {
        double lat;
        double lon;
        String name;

        Place() {
        }
    }
}

