package com.lockscreen.customplus;

import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.media.ExifInterface;
import android.net.Uri;
import androidx.core.app.NotificationCompat;
import androidx.core.view.ViewCompat;
import com.google.android.gms.common.moduleinstall.ModuleInstall;
import com.google.android.gms.common.moduleinstall.ModuleInstallRequest;
import com.google.android.gms.tasks.Tasks;
import com.google.mlkit.common.MlKitException;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.segmentation.Segmentation;
import com.google.mlkit.vision.segmentation.SegmentationMask;
import com.google.mlkit.vision.segmentation.Segmenter;
import com.google.mlkit.vision.segmentation.selfie.SelfieSegmenterOptions;
import com.google.mlkit.vision.segmentation.subject.SubjectSegmentation;
import com.google.mlkit.vision.segmentation.subject.SubjectSegmentationResult;
import com.google.mlkit.vision.segmentation.subject.SubjectSegmenter;
import com.google.mlkit.vision.segmentation.subject.SubjectSegmenterOptions;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.ByteBuffer;
import java.util.Locale;
import java.util.concurrent.TimeUnit;
import org.json.JSONArray;
import org.json.JSONObject;

/* loaded from: classes3.dex */
final class Store {
    static final int MAX_SIDE = 2560;
    private static long batteryAt;
    private static String batteryCache = "";

    private Store() {
    }

    static SharedPreferences prefs(Context c) {
        return c.getApplicationContext().getSharedPreferences("settings", 0);
    }

    // Какой экран сейчас настраивается в приложении: 0 — обычный (вертикальный), 1 — «на боку»
    static int editProfile(Context c) {
        return prefs(c).getInt("editProfile", 0) == 1 ? 1 : 0;
    }

    static void setEditProfile(Context c, int p) {
        prefs(c).edit().putInt("editProfile", p == 1 ? 1 : 0).apply();
    }

    static boolean landscapeOn(Context c) {
        return prefs(c).getBoolean("landOn", true);
    }

    static boolean usbOn(Context c) {
        return prefs(c).getBoolean("usbOn", true);
    }

    static File bgFile(Context c) {
        return new File(c.getFilesDir(), "bg.jpg");
    }

    static File cutFile(Context c) {
        return new File(c.getFilesDir(), "cut.png");
    }

    static File rawCutFile(Context c) {
        return new File(c.getFilesDir(), "cutraw.png");
    }

    static boolean hasBackground(Context c) {
        return bgFile(c).exists();
    }

    static boolean hasCut(Context c) {
        return cutFile(c).exists();
    }

    private static void touch(Context c) {
        prefs(c).edit().putLong("imgStamp", System.nanoTime()).apply();
    }

    private static File copyToCache(Context c, Uri uri) throws IOException {
        File f = new File(c.getCacheDir(), "pick-" + System.nanoTime() + ".tmp");
        try {
            InputStream in = c.getContentResolver().openInputStream(uri);
            try {
                FileOutputStream out = new FileOutputStream(f);
                try {
                    if (in == null) {
                        throw new IOException("Не удалось открыть выбранный файл");
                    }
                    byte[] buf = new byte[65536];
                    long total = 0;
                    do {
                        int n = in.read(buf);
                        if (n > 0) {
                            out.write(buf, 0, n);
                            total += n;
                        } else {
                            out.close();
                            if (in != null) {
                                in.close();
                            }
                            return f;
                        }
                    } while (total <= 209715200);
                    throw new IOException("Файл слишком большой (больше 200 МБ)");
                } finally {
                }
            } finally {
            }
        } catch (IOException e) {
            f.delete();
            throw e;
        } catch (SecurityException e2) {
            f.delete();
            throw new IOException("Нет доступа к файлу. Выберите картинку ещё раз");
        }
    }

    static void importBackground(Context c, Uri picked) throws IOException {
        File tmp = copyToCache(c, picked);
        try {
            importBackgroundFile(c, Uri.fromFile(tmp));
        } finally {
            tmp.delete();
        }
    }

    private static void importBackgroundFile(Context c, Uri uri) throws IOException {
        int[] size = bounds(c, uri);
        int rot = exifRotation(c, uri);
        boolean swap = rot == 90 || rot == 270;
        int ow = swap ? size[1] : size[0];
        int oh = swap ? size[0] : size[1];
        float k = Math.min(1.0f, 2560.0f / Math.max(ow, oh));
        Bitmap b = decodeScaled(c, uri, k, rot);
        try {
            savePng(b, bgFile(c), false);
            removeCutFiles(c);
            prefs(c).edit().putInt("bgW", b.getWidth()).putInt("bgH", b.getHeight()).putFloat("scale", k).apply();
            b.recycle();
            touch(c);
        } catch (Throwable th) {
            b.recycle();
            throw th;
        }
    }

    static String importCut(Context c, Uri picked) throws IOException {
        if (!hasBackground(c)) {
            throw new IOException("Сначала выберите фото");
        }
        File tmp = copyToCache(c, picked);
        try {
            return importCutFile(c, Uri.fromFile(tmp));
        } finally {
            tmp.delete();
        }
    }

    private static String importCutFile(Context c, Uri uri) throws IOException {
        boolean z;
        String str;
        String msg;
        int x;
        int y;
        SharedPreferences p = prefs(c);
        float k = p.getFloat("scale", 1.0f);
        Bitmap cut = decodeScaled(c, uri, k, exifRotation(c, uri));
        try {
            int bw = p.getInt("bgW", 0);
            int bh = p.getInt("bgH", 0);
            int cw = cut.getWidth();
            int ch = cut.getHeight();
            if (cw > bw + 1 || ch > bh + 1) {
                throw new IOException("Вырез больше фото. Нужен PNG, сделанный из этого же фото");
            }
            if (Math.abs(cw - bw) <= 1 && Math.abs(ch - bh) <= 1) {
                msg = "Вырез того же размера, что и фото — поставил как есть";
                z = true;
                x = 0;
                y = 0;
            } else {
                Bitmap bg = BitmapFactory.decodeFile(bgFile(c).getPath());
                if (bg == null) {
                    throw new IOException("Не удалось прочитать фото");
                }
                int[] bp = new int[bg.getWidth() * bg.getHeight()];
                bg.getPixels(bp, 0, bg.getWidth(), 0, 0, bg.getWidth(), bg.getHeight());
                int[] cp = new int[cw * ch];
                cut.getPixels(cp, 0, cw, 0, 0, cw, ch);
                int[] r = Align.find(bp, bg.getWidth(), bg.getHeight(), cp, cw, ch);
                bg.recycle();
                if (r == null) {
                    int x2 = (bw - cw) / 2;
                    int y2 = (bh - ch) / 2;
                    msg = "Не смог совместить сам — поставил по центру. Подвиньте стрелками";
                    x = x2;
                    y = y2;
                    z = true;
                } else {
                    int x3 = r[0];
                    z = true;
                    int y3 = r[1];
                    if (r[2] <= 25) {
                        str = "Вырез совмещён с фото автоматически";
                    } else {
                        str = "Не уверен в совмещении. Проверьте и подвиньте стрелками";
                    }
                    msg = str;
                    x = x3;
                    y = y3;
                }
            }
            savePng(cut, rawCutFile(c), z);
            p.edit().putInt("cutX", x).putInt("cutY", y).apply();
            compose(c);
            return msg;
        } finally {
            cut.recycle();
        }
    }

    static void nudge(Context c, int dx, int dy) throws IOException {
        if (rawCutFile(c).exists()) {
            SharedPreferences p = prefs(c);
            int[] cs = fileSize(rawCutFile(c));
            int bw = p.getInt("bgW", 0);
            int bh = p.getInt("bgH", 0);
            int x = Math.max(0, Math.min(Math.max(0, bw - cs[0]), p.getInt("cutX", 0) + dx));
            int y = Math.max(0, Math.min(Math.max(0, bh - cs[1]), p.getInt("cutY", 0) + dy));
            p.edit().putInt("cutX", x).putInt("cutY", y).apply();
            compose(c);
        }
    }

    static void removeCut(Context c) {
        removeCutFiles(c);
        touch(c);
    }

    private static void removeCutFiles(Context c) {
        cutFile(c).delete();
        rawCutFile(c).delete();
    }

    private static void compose(Context c) throws IOException {
        SharedPreferences p = prefs(c);
        Bitmap raw = BitmapFactory.decodeFile(rawCutFile(c).getPath());
        if (raw == null) {
            return;
        }
        Bitmap out = null;
        try {
            out = Bitmap.createBitmap(p.getInt("bgW", raw.getWidth()), p.getInt("bgH", raw.getHeight()), Bitmap.Config.ARGB_8888);
            new Canvas(out).drawBitmap(raw, p.getInt("cutX", 0), p.getInt("cutY", 0), (Paint) null);
            savePng(out, cutFile(c), true);
            touch(c);
        } finally {
            raw.recycle();
            if (out != null) {
                out.recycle();
            }
        }
    }

    private static int[] bounds(Context c, Uri uri) throws IOException {
        BitmapFactory.Options o = new BitmapFactory.Options();
        o.inJustDecodeBounds = true;
        InputStream in = c.getContentResolver().openInputStream(uri);
        try {
            if (in == null) {
                throw new IOException("Не удалось открыть файл");
            }
            BitmapFactory.decodeStream(in, null, o);
            if (in != null) {
                in.close();
            }
            if (o.outWidth <= 0 || o.outHeight <= 0) {
                throw new IOException("Это не картинка");
            }
            return new int[]{o.outWidth, o.outHeight};
        } catch (Throwable th) {
            if (in != null) {
                try {
                    in.close();
                } catch (Throwable th2) {
                    th.addSuppressed(th2);
                }
            }
            throw th;
        }
    }

    private static int[] fileSize(File f) {
        BitmapFactory.Options o = new BitmapFactory.Options();
        o.inJustDecodeBounds = true;
        BitmapFactory.decodeFile(f.getPath(), o);
        return new int[]{o.outWidth, o.outHeight};
    }

    private static int exifRotation(Context c, Uri uri) {
        InputStream in = null;
        try {
            in = c.getContentResolver().openInputStream(uri);
            if (in == null) {
                return 0;
            }
            int o = new ExifInterface(in).getAttributeInt(ExifInterface.TAG_ORIENTATION, 1);
            if (o == 6) {
                return 90;
            }
            if (o == 3) {
                return 180;
            }
            if (o == 8) {
                return 270;
            }
            return 0;
        } catch (Exception e) {
            return 0;
        } finally {
            if (in != null) {
                try {
                    in.close();
                } catch (IOException e2) {
                }
            }
        }
    }

    private static Bitmap decodeScaled(Context c, Uri uri, float k, int rot) throws IOException {
        int[] size = bounds(c, uri);
        int sample = 1;
        while (1.0f / (sample * 2) >= k) {
            sample *= 2;
        }
        BitmapFactory.Options o = new BitmapFactory.Options();
        o.inSampleSize = sample;
        InputStream in = c.getContentResolver().openInputStream(uri);
        try {
            if (in == null) {
                throw new IOException("Не удалось открыть файл");
            }
            Bitmap b = BitmapFactory.decodeStream(in, null, o);
            if (in != null) {
                in.close();
            }
            if (b == null) {
                throw new IOException("Не удалось прочитать картинку");
            }
            int tw = Math.max(1, Math.round(size[0] * k));
            int th = Math.max(1, Math.round(size[1] * k));
            if (b.getWidth() != tw || b.getHeight() != th) {
                Bitmap n = Bitmap.createScaledBitmap(b, tw, th, true);
                if (n != b) {
                    b.recycle();
                }
                b = n;
            }
            if (rot != 0) {
                Matrix m = new Matrix();
                m.postRotate(rot);
                Bitmap n2 = Bitmap.createBitmap(b, 0, 0, b.getWidth(), b.getHeight(), m, true);
                if (n2 != b) {
                    b.recycle();
                }
                return n2;
            }
            return b;
        } finally {
        }
    }

    private static void savePng(Bitmap b, File f, boolean png) throws IOException {
        boolean ok;
        File tmp = new File(f.getPath() + ".tmp");
        FileOutputStream out = new FileOutputStream(tmp);
        try {
            if (png) {
                ok = b.compress(Bitmap.CompressFormat.PNG, 100, out);
            } else {
                ok = b.compress(Bitmap.CompressFormat.JPEG, 92, out);
            }
            if (!ok) {
                throw new IOException("Не удалось сохранить картинку");
            }
            out.close();
            if (f.exists() && !f.delete()) {
                throw new IOException("Не удалось заменить файл");
            }
            if (!tmp.renameTo(f)) {
                throw new IOException("Не удалось сохранить файл");
            }
        } catch (Throwable th) {
            try {
                out.close();
            } catch (Throwable th2) {
                th.addSuppressed(th2);
            }
            throw th;
        }
    }

    /* JADX WARN: Removed duplicated region for block: B:24:0x005b  */
    /* JADX WARN: Removed duplicated region for block: B:26:0x005e  */
    /*
        Code decompiled incorrectly, please refer to instructions dump.
    */
    static synchronized String batteryText(Context c) {
        boolean charging;
        synchronized (Store.class) {
            long now = System.currentTimeMillis();
            if (now - batteryAt < 30000 && !batteryCache.isEmpty()) {
                return batteryCache;
            }
            batteryAt = now;
            Intent i = c.getApplicationContext().registerReceiver(null, new IntentFilter("android.intent.action.BATTERY_CHANGED"));
            if (i != null) {
                int level = i.getIntExtra("level", -1);
                int scale = i.getIntExtra("scale", 100);
                int status = i.getIntExtra(NotificationCompat.CATEGORY_STATUS, -1);
                if (status != 2 && status != 5) {
                    charging = false;
                    if (level >= 0 && scale > 0) {
                        batteryCache = (!charging ? "⚡ " : "") + ((level * 100) / scale) + "%";
                    }
                }
                charging = true;
                if (level >= 0) {
                    batteryCache = (!charging ? "⚡ " : "") + ((level * 100) / scale) + "%";
                }
            }
            return batteryCache;
        }
    }

    static JSONArray presets(Context c) {
        try {
            return new JSONArray(prefs(c).getString("presets", "[]"));
        } catch (Exception e) {
            return new JSONArray();
        }
    }

    static void addPreset(Context c, String name, Design d) throws Exception {
        JSONArray a = presets(c);
        a.put(new JSONObject().put("name", name).put("design", d.toJson()));
        prefs(c).edit().putString("presets", a.toString()).apply();
    }

    static void deletePreset(Context c, int index) {
        JSONArray old = presets(c);
        JSONArray a = new JSONArray();
        for (int i = 0; i < old.length(); i++) {
            if (i != index) {
                a.put(old.opt(i));
            }
        }
        prefs(c).edit().putString("presets", a.toString()).apply();
    }

    private static String describe(Throwable e) {
        String m = e.getMessage();
        Throwable cause = e.getCause();
        if (cause != null && cause.getMessage() != null) {
            m = (m == null ? "" : m + " / ") + cause.getMessage();
        }
        return e.getClass().getSimpleName() + (m != null ? ": " + m : "");
    }

    static String autoCut(Context c) throws Exception {
        if (!hasBackground(c)) {
            throw new IOException("Сначала выберите фото");
        }
        Bitmap bg = BitmapFactory.decodeFile(bgFile(c).getPath());
        if (bg == null) {
            throw new IOException("Не удалось прочитать фото");
        }
        Bitmap small = bg;
        int m = Math.max(bg.getWidth(), bg.getHeight());
        if (m > 1024) {
            float k = 1024.0f / m;
            small = Bitmap.createScaledBitmap(bg, Math.round(bg.getWidth() * k), Math.round(bg.getHeight() * k), true);
        }
        Bitmap fg = null;
        String subjectError = null;
        try {
            fg = subjectCut(c, small);
        } catch (Throwable e) {
            subjectError = describe(e);
        }
        String how = "Объект вырезан автоматически";
        if (fg == null) {
            try {
                fg = selfieCut(small);
                how = "Вырезан человек на фото (модель «объект» от Google недоступна)";
            } catch (Throwable e2) {
                throw new IOException("Не удалось вырезать.\n\nМодель «объект»: " + subjectError + "\nМодель «человек»: " + describe(e2));
            }
        }
        Bitmap full = fg;
        if (fg.getWidth() != bg.getWidth() || fg.getHeight() != bg.getHeight()) {
            full = Bitmap.createScaledBitmap(fg, bg.getWidth(), bg.getHeight(), true);
        }
        savePng(full, rawCutFile(c), true);
        prefs(c).edit().putInt("cutX", 0).putInt("cutY", 0).apply();
        compose(c);
        return how + ". Если вырез неточный, подвиньте или выберите свой PNG";
    }

    private static Bitmap subjectCut(Context c, Bitmap small) throws Exception {
        SubjectSegmenter seg = SubjectSegmentation.getClient(new SubjectSegmenterOptions.Builder().enableForegroundBitmap().build());
        try {
            ModuleInstallRequest req = ModuleInstallRequest.newBuilder().addApi(seg).build();
            Tasks.await(ModuleInstall.getClient(c).installModules(req), 240L, TimeUnit.SECONDS);
            SubjectSegmentationResult r = (SubjectSegmentationResult) Tasks.await(seg.process(InputImage.fromBitmap(small, 0)), 120L, TimeUnit.SECONDS);
            Bitmap fg = r.getForegroundBitmap();
            if (fg == null) {
                throw new IOException("пустой результат");
            }
            return fg;
        } finally {
            seg.close();
        }
    }

    /* JADX WARN: Incorrect condition in loop: B:5:0x004c */
    /*
        Code decompiled incorrectly, please refer to instructions dump.
    */
    private static Bitmap selfieCut(Bitmap small) throws Exception {
        Segmenter seg = Segmentation.getClient(new SelfieSegmenterOptions.Builder().setDetectorMode(2).build());
        try {
            SegmentationMask mask = (SegmentationMask) Tasks.await(seg.process(InputImage.fromBitmap(small, 0)), 60L, TimeUnit.SECONDS);
            int mw = mask.getWidth();
            int mh = mask.getHeight();
            ByteBuffer buf = mask.getBuffer();
            Bitmap scaled = Bitmap.createScaledBitmap(small, mw, mh, true);
            int[] px = new int[mw * mh];
            scaled.getPixels(px, 0, mw, 0, 0, mw, mh);
            int found = 0;
            for (int i = 0; i < found; i++) {
                float conf = buf.getFloat();
                int a = Math.max(0, Math.min(255, Math.round(((conf - 0.25f) / 0.5f) * 255.0f)));
                if (a > 128) {
                    found++;
                }
                px[i] = (px[i] & ViewCompat.MEASURED_SIZE_MASK) | (a << 24);
            }
            if (found < px.length / MlKitException.CODE_SCANNER_UNAVAILABLE) {
                throw new IOException("человек на фото не найден");
            }
            Bitmap out = Bitmap.createBitmap(mw, mh, Bitmap.Config.ARGB_8888);
            out.setPixels(px, 0, mw, 0, 0, mw, mh);
            return out;
        } finally {
            seg.close();
        }
    }

    static final class Place {
        double lat;
        double lon;
        String name;

        Place() {
        }
    }

    static String httpGet(String urlStr, int maxBytes) throws IOException {
        URL u = new URL(urlStr);
        if (!"https".equals(u.getProtocol())) {
            throw new IOException("Нужна ссылка https://");
        }
        HttpURLConnection h = (HttpURLConnection) u.openConnection();
        h.setConnectTimeout(8000);
        h.setReadTimeout(8000);
        h.setRequestProperty("User-Agent", "LockScreenCustomPlus/1.0");
        try {
            int code = h.getResponseCode();
            if (code != 200) {
                throw new IOException("Сервер ответил кодом " + code);
            }
            ByteArrayOutputStream o = new ByteArrayOutputStream();
            byte[] buf = new byte[4096];
            int total = 0;
            InputStream in = h.getInputStream();
            while (true) {
                try {
                    int n = in.read(buf);
                    if (n <= 0 || total >= maxBytes) {
                        break;
                    }
                    o.write(buf, 0, n);
                    total += n;
                } finally {
                }
            }
            if (in != null) {
                in.close();
            }
            return o.toString("UTF-8");
        } finally {
            h.disconnect();
        }
    }

    static Place geocode(String city) throws IOException {
        try {
            String u = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&format=json&name=" + URLEncoder.encode(city.trim(), "UTF-8");
            JSONArray r = new JSONObject(httpGet(u, 60000)).optJSONArray("results");
            if (r == null || r.length() == 0) {
                throw new IOException("Такой город не найден");
            }
            JSONObject o = r.getJSONObject(0);
            Place p = new Place();
            p.name = o.optString("name", city);
            p.lat = o.getDouble("latitude");
            p.lon = o.getDouble("longitude");
            return p;
        } catch (IOException e) {
            throw e;
        } catch (Exception e2) {
            throw new IOException("Не удалось найти город");
        }
    }

    static double[] fetchWeather(double lat, double lon) throws IOException {
        try {
            String u = String.format(Locale.US, "https://api.open-meteo.com/v1/forecast?latitude=%.4f&longitude=%.4f&current=weather_code,temperature_2m", Double.valueOf(lat), Double.valueOf(lon));
            JSONObject cur = new JSONObject(httpGet(u, 60000)).getJSONObject("current");
            return new double[]{cur.getInt("weather_code"), cur.getDouble("temperature_2m")};
        } catch (IOException e) {
            throw e;
        } catch (Exception e2) {
            throw new IOException("Не удалось получить погоду");
        }
    }

    static void saveWeather(Context c, double[] w) {
        prefs(c).edit().putInt("weatherCode", (int) w[0]).putFloat("weatherTemp", (float) w[1]).putLong("weatherAt", System.currentTimeMillis()).apply();
    }

    static String weatherName(int code) {
        if (code == 0) {
            return "Ясно";
        }
        if (code == 1 || code == 2) {
            return "Малооблачно";
        }
        if (code == 3) {
            return "Пасмурно";
        }
        if (code == 45 || code == 48) {
            return "Туман";
        }
        if (code >= 51 && code <= 57) {
            return "Морось";
        }
        if (code >= 61 && code <= 67) {
            return "Дождь";
        }
        if (code >= 71 && code <= 77) {
            return "Снег";
        }
        if (code >= 80 && code <= 82) {
            return "Ливень";
        }
        if (code == 85 || code == 86) {
            return "Снегопад";
        }
        return (code < 95 || code > 99) ? "Без осадков" : "Гроза";
    }

    static String fetchText(String urlStr) throws IOException {
        URL u = new URL(urlStr);
        if (!"https".equals(u.getProtocol())) {
            throw new IOException("Нужна ссылка, начинающаяся с https://");
        }
        HttpURLConnection h = (HttpURLConnection) u.openConnection();
        h.setConnectTimeout(8000);
        h.setReadTimeout(8000);
        h.setRequestProperty("User-Agent", "LockScreenCustomPlus/1.0");
        try {
            int code = h.getResponseCode();
            if (code != 200) {
                throw new IOException("Сайт ответил кодом " + code);
            }
            ByteArrayOutputStream o = new ByteArrayOutputStream();
            byte[] buf = new byte[4096];
            int total = 0;
            InputStream in = h.getInputStream();
            while (true) {
                try {
                    int n = in.read(buf);
                    if (n <= 0 || total >= 40000) {
                        break;
                    }
                    o.write(buf, 0, n);
                    total += n;
                } finally {
                }
            }
            if (in != null) {
                in.close();
            }
            String s = o.toString("UTF-8");
            if (s.trim().startsWith("<")) {
                s = s.replaceAll("(?s)<(script|style)[^>]*>.*?</\\1>", " ").replaceAll("(?s)<[^>]+>", " ");
            }
            String s2 = s.replaceAll("\\s+", " ").trim();
            if (s2.length() > 300) {
                s2 = s2.substring(0, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE).trim() + "…";
            }
            if (s2.isEmpty()) {
                throw new IOException("По ссылке нет текста");
            }
            return s2;
        } finally {
            h.disconnect();
        }
    }
}
