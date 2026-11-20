package com.lockscreen.customplus;

import android.content.Context;
import android.graphics.Typeface;
import android.net.Uri;
import com.lockscreen.customplus.Design;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.json.JSONObject;

/* loaded from: classes3.dex */
final class Fonts {
    private static Context app;
    private static final String[] SYSTEM_NAMES = {"Тонкий", "Сверхтонкий", "Жирный", "Узкий жирный", "С засечками", "Моноширинный"};
    private static final Map<String, Typeface> cache = new HashMap();

    private Fonts() {
    }

    static final class Entry {
        final String id;
        final String name;

        Entry(String id, String name) {
            this.id = id;
            this.name = name;
        }
    }

    static void init(Context c) {
        app = c.getApplicationContext();
    }

    private static Typeface system(int i) {
        switch (i) {
            case 1:
                return Typeface.create("sans-serif-thin", 0);
            case 2:
                return Typeface.create("sans-serif", 1);
            case 3:
                return Typeface.create("sans-serif-condensed", 1);
            case 4:
                return Typeface.create("serif", 1);
            case 5:
                return Typeface.create("monospace", 1);
            default:
                return Typeface.create("sans-serif-light", 0);
        }
    }

    static synchronized Typeface get(String id) {
        Typeface build;
        synchronized (Fonts.class) {
            build = build(id, "");
        }
        return build;
    }

    static synchronized Typeface get(Design.FontSpec spec) {
        synchronized (Fonts.class) {
            if (spec == null) {
                return get("sys:0");
            }
            return build(spec.id, spec.variation());
        }
    }

    private static Typeface build(String id, String variation) {
        if (id == null) {
            id = "sys:0";
        }
        String key = id + "|" + variation;
        Typeface t = cache.get(key);
        if (t != null) {
            return t;
        }
        try {
            if (id.startsWith("sys:")) {
                t = system(Integer.parseInt(id.substring(4)));
            } else if (id.startsWith("asset:") && app != null) {
                String path = "fonts/" + id.substring(6);
                if (variation.isEmpty()) {
                    t = Typeface.createFromAsset(app.getAssets(), path);
                } else {
                    t = new Typeface.Builder(app.getAssets(), path).setFontVariationSettings(variation).build();
                    if (t == null) {
                        t = Typeface.createFromAsset(app.getAssets(), path);
                    }
                }
            } else if (id.startsWith("user:") && app != null) {
                File f = new File(userDir(), id.substring(5));
                Typeface.Builder b = new Typeface.Builder(f);
                if (!variation.isEmpty()) {
                    b.setFontVariationSettings(variation);
                }
                t = b.build();
            }
        } catch (Exception e) {
        }
        if (t == null) {
            t = system(0);
        }
        cache.put(key, t);
        if (cache.size() > 200) {
            cache.clear();
        }
        return t;
    }

    static String systemId(int i) {
        return "sys:" + i;
    }

    private static File userDir() {
        File d = new File(app.getFilesDir(), "fonts");
        d.mkdirs();
        return d;
    }

    static List<Entry> list() {
        List<Entry> out = new ArrayList<>();
        for (int i = 0; i < SYSTEM_NAMES.length; i++) {
            out.add(new Entry(systemId(i), SYSTEM_NAMES[i]));
        }
        if (app == null) {
            return out;
        }
        try {
            String[] files = app.getAssets().list("fonts");
            if (files != null) {
                Arrays.sort(files);
                for (String f : files) {
                    String n = f.replaceAll("\\.(ttf|otf|ttc)$", "").replaceAll("([a-z])([A-Z0-9])", "$1 $2");
                    out.add(new Entry("asset:" + f, n));
                }
            }
        } catch (IOException e) {
        }
        for (Entry e2 : userFonts()) {
            out.add(e2);
        }
        return out;
    }

    static List<Entry> userFonts() {
        List<Entry> out = new ArrayList<>();
        if (app == null) {
            return out;
        }
        try {
            JSONObject names = new JSONObject(Store.prefs(app).getString("userFonts", "{}"));
            Iterator<String> it = names.keys();
            while (it.hasNext()) {
                String file = it.next();
                if (new File(userDir(), file).exists()) {
                    out.add(new Entry("user:" + file, names.optString(file, file)));
                }
            }
        } catch (Exception e) {
        }
        return out;
    }

    static synchronized String importFont(Context c, Uri uri, String displayName) throws IOException {
        init(c);
        String lower = displayName == null ? "" : displayName.toLowerCase(Locale.ROOT);
        String ext = lower.endsWith(".otf") ? "otf" : lower.endsWith(".ttc") ? "ttc" : "ttf";
        File dest = new File(userDir(), "f" + System.currentTimeMillis() + "." + ext);
        InputStream in = c.getContentResolver().openInputStream(uri);
        if (in == null) {
            throw new IOException("Не удалось открыть файл");
        }
        try {
            FileOutputStream out = new FileOutputStream(dest);
            try {
                byte[] buf = new byte[16384];
                long total = 0;
                int n;
                while ((n = in.read(buf)) > 0) {
                    out.write(buf, 0, n);
                    total += n;
                    if (total > 31457280) { // 30 МБ
                        out.close();
                        dest.delete();
                        throw new IOException("Файл слишком большой");
                    }
                }
            } finally {
                try {
                    out.close();
                } catch (IOException e) {
                }
            }
        } finally {
            try {
                in.close();
            } catch (IOException e) {
            }
        }
        Typeface t = null;
        try {
            t = new Typeface.Builder(dest).build();
        } catch (Exception e) {
        }
        if (t == null) {
            dest.delete();
            throw new IOException("Это не шрифт, который понимает Android. Нужен файл TTF, OTF или TTC");
        }
        String shown = (displayName != null && !displayName.isEmpty()) ? displayName.replaceAll("\\.[A-Za-z0-9]+$", "") : "Свой шрифт";
        try {
            JSONObject names = new JSONObject(Store.prefs(c).getString("userFonts", "{}"));
            names.put(dest.getName(), shown);
            Store.prefs(c).edit().putString("userFonts", names.toString()).apply();
        } catch (Exception e) {
            throw new IOException("Не удалось сохранить шрифт");
        }
        return "user:" + dest.getName();
    }

    static synchronized void deleteUser(Context c, String id) {
        synchronized (Fonts.class) {
            if (id != null) {
                if (id.startsWith("user:")) {
                    String file = id.substring(5);
                    new File(userDir(), file).delete();
                    cache.clear();
                    try {
                        JSONObject names = new JSONObject(Store.prefs(c).getString("userFonts", "{}"));
                        names.remove(file);
                        Store.prefs(c).edit().putString("userFonts", names.toString()).apply();
                    } catch (Exception e) {
                    }
                }
            }
        }
    }
}
