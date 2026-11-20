/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  android.content.res.AssetManager
 *  android.graphics.Typeface
 *  android.graphics.Typeface$Builder
 *  android.net.Uri
 *  org.json.JSONObject
 */
package com.lockscreen.customplus;

import android.content.Context;
import android.content.res.AssetManager;
import android.graphics.Typeface;
import android.net.Uri;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.Store;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.json.JSONObject;

final class Fonts {
    private static final String[] SYSTEM_NAMES = new String[]{"\u0422\u043e\u043d\u043a\u0438\u0439", "\u0421\u0432\u0435\u0440\u0445\u0442\u043e\u043d\u043a\u0438\u0439", "\u0416\u0438\u0440\u043d\u044b\u0439", "\u0423\u0437\u043a\u0438\u0439 \u0436\u0438\u0440\u043d\u044b\u0439", "\u0421 \u0437\u0430\u0441\u0435\u0447\u043a\u0430\u043c\u0438", "\u041c\u043e\u043d\u043e\u0448\u0438\u0440\u0438\u043d\u043d\u044b\u0439"};
    private static Context app;
    private static final Map<String, Typeface> cache;

    static {
        cache = new HashMap<String, Typeface>();
    }

    private Fonts() {
    }

    private static Typeface build(String object, String string2) {
        String string3;
        block30: {
            Object object2;
            Typeface typeface;
            String string4;
            block31: {
                block28: {
                    block29: {
                        block27: {
                            string4 = object;
                            if (object == null) {
                                string4 = "sys:0";
                            }
                            if ((typeface = cache.get(string3 = string4 + "|" + string2)) != null) {
                                return typeface;
                            }
                            object2 = typeface;
                            if (!string4.startsWith("sys:")) break block27;
                            object2 = typeface;
                            object = Fonts.system(Integer.parseInt(string4.substring(4)));
                        }
                        object2 = typeface;
                        if (!string4.startsWith("asset:")) break block28;
                        object2 = typeface;
                        if (app == null) break block28;
                        object2 = typeface;
                        object2 = typeface;
                        object = new StringBuilder();
                        object2 = typeface;
                        string4 = ((StringBuilder)object).append("fonts/").append(string4.substring(6)).toString();
                        object2 = typeface;
                        if (!string2.isEmpty()) break block29;
                        object2 = typeface;
                        object = Typeface.createFromAsset((AssetManager)app.getAssets(), (String)string4);
                    }
                    object2 = typeface;
                    object2 = typeface;
                    object = new Typeface.Builder(app.getAssets(), string4);
                    object2 = typeface;
                    string2 = object.setFontVariationSettings(string2).build();
                    object = string2;
                    if (string2 != null) break block30;
                    object2 = string2;
                    object = Typeface.createFromAsset((AssetManager)app.getAssets(), (String)string4);
                }
                object = typeface;
                object2 = typeface;
                if (!string4.startsWith("user:")) break block30;
                object = typeface;
                object2 = typeface;
                if (app == null) break block30;
                object2 = typeface;
                object2 = typeface;
                object = new File(Fonts.userDir(), string4.substring(5));
                object2 = typeface;
                object2 = typeface;
                string4 = new Typeface.Builder((File)object);
                object2 = typeface;
                if (string2.isEmpty()) break block31;
                object2 = typeface;
                string4.setFontVariationSettings(string2);
            }
            object2 = typeface;
            try {
                object = string4.build();
            }
            catch (Exception exception) {
                object = object2;
            }
        }
        string2 = object;
        if (object == null) {
            string2 = Fonts.system(0);
        }
        cache.put(string3, (Typeface)string2);
        if (cache.size() > 200) {
            cache.clear();
        }
        return string2;
    }

    /*
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    static void deleteUser(Context context, String string2) {
        synchronized (Fonts.class) {
            if (string2 != null && string2.startsWith("user:")) {
                string2 = string2.substring(5);
                File file = new File(Fonts.userDir(), string2);
                file.delete();
                cache.clear();
                try {
                    file = new JSONObject(Store.prefs(context).getString("userFonts", "{}"));
                    file.remove(string2);
                    Store.prefs(context).edit().putString("userFonts", file.toString()).apply();
                }
                catch (Exception exception) {
                    // empty catch block
                }
                return;
            }
            return;
        }
    }

    /*
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    static Typeface get(Design.FontSpec fontSpec) {
        synchronized (Fonts.class) {
            if (fontSpec != null) return Fonts.build(fontSpec.id, fontSpec.variation());
            return Fonts.get("sys:0");
        }
    }

    /*
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    static Typeface get(String string2) {
        synchronized (Fonts.class) {
            return Fonts.build(string2, "");
        }
    }

    /*
     * WARNING - Removed back jump from a try to a catch block - possible behaviour change.
     * Unable to fully structure code
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    static String importFont(Context var0, Uri var1_5, String var2_7) throws IOException {
        synchronized (Fonts.class) {
            Fonts.init((Context)var0);
            var6_9 = var2_7 == null ? "" : var2_7.toLowerCase(Locale.ROOT);
            var6_9 = var6_9.endsWith(".otf") != false ? "otf" : (var6_9.endsWith(".ttc") != false ? "ttc" : "ttf");
            var9_12 = Fonts.userDir();
            var8_13 = new StringBuilder();
            var7_11 = new File(var9_12, var8_13.append("f").append(System.currentTimeMillis()).append(".").append((String)var6_9).toString());
            var1_5 = var0.getContentResolver().openInputStream((Uri)var1_5);
            var6_9 = new FileOutputStream(var7_11);
            if (var1_5 != null) {
            }
            ** GOTO lbl-1000
            {
                block27: {
                    catch (Throwable var0_4) {
                        throw var0_4;
                    }
                    try {
                        var8_13 = new byte[16384];
                        var4_14 = 0L;
                        while ((var3_15 = var1_5.read((byte[])var8_13)) > 0) {
                            var6_9.write((byte[])var8_13, 0, var3_15);
                            if ((var4_14 += (long)var3_15) <= 0x1E00000L) continue;
                            var0 = new IOException("\u0424\u0430\u0439\u043b \u0441\u043b\u0438\u0448\u043a\u043e\u043c \u0431\u043e\u043b\u044c\u0448\u043e\u0439");
                            throw var0;
                        }
                    }
                    catch (Throwable var0_3) {
                        break block27;
                    }
                    ** try [egrp 3[TRYBLOCK] [10 : 205->210)] { 
lbl28:
                    // 1 sources

                    var6_9.close();
                    var1_5 = null;
                    try {
                        var6_9 = new Typeface.Builder(var7_11);
                        var1_5 = var6_9 = var6_9.build();
                    }
                    catch (Exception var6_10) {
                        // empty catch block
                    }
                    if (var1_5 == null) {
                        var7_11.delete();
                        var0 = new IOException("\u042d\u0442\u043e \u043d\u0435 \u0448\u0440\u0438\u0444\u0442, \u043a\u043e\u0442\u043e\u0440\u044b\u0439 \u043f\u043e\u043d\u0438\u043c\u0430\u0435\u0442 Android. \u041d\u0443\u0436\u0435\u043d \u0444\u0430\u0439\u043b TTF, OTF \u0438\u043b\u0438 TTC");
                        throw var0;
                    }
                    if (var2_7 == null) ** GOTO lbl-1000
                    try {
                        if (!var2_7.isEmpty()) {
                            var1_5 = var2_7.replaceAll("\\.[A-Za-z0-9]+$", "");
                        } else lbl-1000:
                        // 2 sources

                        {
                            var1_5 = "\u0421\u0432\u043e\u0439 \u0448\u0440\u0438\u0444\u0442";
                        }
                        var2_7 = new JSONObject(Store.prefs((Context)var0).getString("userFonts", "{}"));
                        var2_7.put(var7_11.getName(), var1_5);
                        Store.prefs((Context)var0).edit().putString("userFonts", var2_7.toString()).apply();
                        var0 = new StringBuilder();
                        return var0.append("user:").append(var7_11.getName()).toString();
                    }
                    catch (Exception var0_1) {
                        var0_2 = new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u0448\u0440\u0438\u0444\u0442");
                        throw var0_2;
                    }
lbl-1000:
                    // 1 sources

                    {
                        var0 = new IOException("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043e\u0442\u043a\u0440\u044b\u0442\u044c \u0444\u0430\u0439\u043b");
                        throw var0;
                    }
                }
                try {
                    var6_9.close();
                    throw var0_3;
                }
                catch (Throwable var2_8) {}
                {
                    var0_3.addSuppressed(var2_8);
                    throw var0_3;
                }
            }
lbl65:
            // 1 sources

            finally {
                if (var1_5 != null) {
                    var1_5.close();
                }
            }
        }
    }

    static void init(Context context) {
        app = context.getApplicationContext();
    }

    static List<Entry> list() {
        Object object;
        ArrayList<Entry> arrayList;
        block8: {
            arrayList = new ArrayList<Entry>();
            for (int i = 0; i < SYSTEM_NAMES.length; ++i) {
                arrayList.add(new Entry(Fonts.systemId(i), SYSTEM_NAMES[i]));
            }
            if (app == null) {
                return arrayList;
            }
            Object[] objectArray = app.getAssets().list("fonts");
            if (objectArray == null) break block8;
            Arrays.sort(objectArray);
            for (Object object2 : objectArray) {
                try {
                    String string2 = ((String)object2).replaceAll("\\.(ttf|otf|ttc)$", "").replaceAll("([a-z])([A-Z0-9])", "$1 $2");
                    StringBuilder stringBuilder = new StringBuilder();
                    object = new Entry(stringBuilder.append("asset:").append((String)object2).toString(), string2);
                    arrayList.add((Entry)object);
                }
                catch (IOException iOException) {
                    // empty catch block
                    break;
                }
            }
        }
        object = Fonts.userFonts().iterator();
        while (object.hasNext()) {
            arrayList.add(object.next());
        }
        return arrayList;
    }

    private static Typeface system(int n) {
        switch (n) {
            default: {
                return Typeface.create((String)"sans-serif-light", (int)0);
            }
            case 5: {
                return Typeface.create((String)"monospace", (int)1);
            }
            case 4: {
                return Typeface.create((String)"serif", (int)1);
            }
            case 3: {
                return Typeface.create((String)"sans-serif-condensed", (int)1);
            }
            case 2: {
                return Typeface.create((String)"sans-serif", (int)1);
            }
            case 1: 
        }
        return Typeface.create((String)"sans-serif-thin", (int)0);
    }

    static String systemId(int n) {
        return "sys:" + n;
    }

    private static File userDir() {
        File file = new File(app.getFilesDir(), "fonts");
        file.mkdirs();
        return file;
    }

    static List<Entry> userFonts() {
        ArrayList<Entry> arrayList = new ArrayList<Entry>();
        if (app == null) {
            return arrayList;
        }
        try {
            JSONObject jSONObject = new JSONObject(Store.prefs(app).getString("userFonts", "{}"));
            Iterator iterator2 = jSONObject.keys();
            while (iterator2.hasNext()) {
                String string2 = (String)iterator2.next();
                Object object = new File(Fonts.userDir(), string2);
                if (!((File)object).exists()) continue;
                StringBuilder stringBuilder = new StringBuilder();
                object = new Entry(stringBuilder.append("user:").append(string2).toString(), jSONObject.optString(string2, string2));
                arrayList.add((Entry)object);
            }
        }
        catch (Exception exception) {
            // empty catch block
        }
        return arrayList;
    }

    static final class Entry {
        final String id;
        final String name;

        Entry(String string2, String string3) {
            this.id = string2;
            this.name = string3;
        }
    }
}

