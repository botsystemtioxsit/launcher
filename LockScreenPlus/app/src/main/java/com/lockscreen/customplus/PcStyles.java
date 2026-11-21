package com.lockscreen.customplus;

import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import java.util.Locale;

/**
 * 10 стилей экрана «Компьютер». Каждый рисуется в «виртуальном» холсте 1000×460 (как широкий экран телефона на боку)
 * и масштабируется под экран. Данные приходят из PcLink; чего нет (NaN) — рисуем «—».
 */
final class PcStyles {
    static final String[] NAMES = {
        "Неон-карточки", "Кольца", "Игровой HUD", "Терминал", "Минимал",
        "Синтвейв", "Стрелочные приборы", "Графики", "Карбон", "Стекло"
    };
    static final int COUNT = 10;

    private static final int MUTED = 0xFFA08FE0;
    private static final int PINK = 0xFFFF2A9D;
    private static final int YELLOW = 0xFFFCEE0A;
    private static final int GREEN = 0xFF3CFF9A;
    private static final int CYAN = 0xFF00F0FF;
    private static final int WHITE = 0xFFFFFFFF;

    private static final int[] BASE = {
        0xFF070314, 0xFF04050D, 0xFF05070A, 0xFF020A04, 0xFF000000,
        0xFF12042E, 0xFF0B0B0D, 0xFF0D1117, 0xFF0A0A0A, 0xFF7B4FE0
    };

    private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF rf = new RectF();
    private final Path path = new Path();
    private final Typeface orb;
    private final Typeface russo;
    private final Typeface mono;
    private Canvas c;

    PcStyles() {
        Typeface o = null;
        Typeface r = null;
        Typeface m = null;
        try {
            o = Fonts.get("asset:Orbitron.ttf");
            r = Fonts.get("asset:RussoOne.ttf");
            m = Fonts.get("asset:ShareTechMono.ttf");
        } catch (Throwable t) {
        }
        orb = o != null ? o : Typeface.DEFAULT_BOLD;
        russo = r != null ? r : Typeface.DEFAULT_BOLD;
        mono = m != null ? m : Typeface.MONOSPACE;
    }

    void render(Canvas canvas, int W, int H, PcLink.Data d, int style, long now) {
        c = canvas;
        if (style < 0 || style >= COUNT) {
            style = 0;
        }
        c.drawColor(BASE[style]);
        float sx = W / 1000f;
        float sy = H / 460f;
        c.save();
        if (Math.abs(sx / sy - 1f) < 0.18f) {
            c.scale(sx, sy);
        } else {
            float s = Math.min(sx, sy);
            c.translate((W - 1000 * s) / 2f, (H - 460 * s) / 2f);
            c.scale(s, s);
        }
        switch (style) {
            case 1: rings(d); break;
            case 2: hud(d); break;
            case 3: terminal(d); break;
            case 4: minimal(d); break;
            case 5: synth(d); break;
            case 6: gauges(d); break;
            case 7: graphs(d); break;
            case 8: carbon(d); break;
            case 9: glass(d); break;
            default: neon(d); break;
        }
        c.restore();
    }

    // ---------- помощники ----------
    private static int a(int rgb, int alpha) {
        return (alpha << 24) | (rgb & 0xFFFFFF);
    }

    private static String f0(float v) {
        return Float.isNaN(v) ? "—" : String.valueOf(Math.round(v));
    }

    private static String f1(float v) {
        return Float.isNaN(v) ? "—" : String.format(Locale.US, "%.1f", v);
    }

    private static String f2(float v) {
        return Float.isNaN(v) ? "—" : String.format(Locale.US, "%.2f", v);
    }

    private static int tc(float t) {
        if (Float.isNaN(t)) {
            return MUTED;
        }
        return t >= 80 ? PINK : t >= 65 ? YELLOW : GREEN;
    }

    private static float frac(float v, float max) {
        if (Float.isNaN(v) || Float.isNaN(max) || max <= 0) {
            return 0f;
        }
        return Math.max(0f, Math.min(1f, v / max));
    }

    private float t(String s, float x, float y, float size, int col, Typeface tf, Paint.Align al) {
        p.setShader(null);
        p.setStyle(Paint.Style.FILL);
        p.setTypeface(tf);
        p.setTextSize(size);
        p.setColor(col);
        p.setTextAlign(al);
        c.drawText(s, x, y, p);
        return p.measureText(s);
    }

    private float w(String s, float size, Typeface tf) {
        p.setTypeface(tf);
        p.setTextSize(size);
        return p.measureText(s);
    }

    private void rr(float x, float y, float w, float h, float r, int fill, int stroke, float sw) {
        rf.set(x, y, x + w, y + h);
        p.setShader(null);
        if (fill != 0) {
            p.setStyle(Paint.Style.FILL);
            p.setColor(fill);
            c.drawRoundRect(rf, r, r, p);
        }
        if (stroke != 0) {
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(sw);
            p.setColor(stroke);
            c.drawRoundRect(rf, r, r, p);
        }
    }

    private void bar(float x, float y, float w, float h, float f, int col, int bg) {
        rr(x, y, w, h, h / 2, bg, 0, 0);
        rr(x, y, Math.max(h, w * f), h, h / 2, col, 0, 0);
    }

    private void ring(float cx, float cy, float r, float lw, float f, int col, int bg) {
        rf.set(cx - r, cy - r, cx + r, cy + r);
        p.setShader(null);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(lw);
        p.setStrokeCap(Paint.Cap.ROUND);
        p.setColor(bg);
        c.drawArc(rf, 0, 360, false, p);
        p.setColor(col);
        c.drawArc(rf, -90, 360f * Math.max(0.005f, f), false, p);
        p.setStrokeCap(Paint.Cap.BUTT);
    }

    /** Линия по истории (последние PcLink.histN значений). lo/hi = NaN — подобрать диапазон само. */
    private boolean line(float[] arr, float x, float y, float w, float h, int col, float lw, float lo, float hi, boolean fillBelow) {
        int n = PcLink.histN;
        if (n < 2) {
            return false;
        }
        float mn = Float.MAX_VALUE;
        float mx = -Float.MAX_VALUE;
        for (int i = arr.length - n; i < arr.length; i++) {
            float v = arr[i];
            if (!Float.isNaN(v)) {
                mn = Math.min(mn, v);
                mx = Math.max(mx, v);
            }
        }
        if (mn > mx) {
            return false;
        }
        if (Float.isNaN(lo) || Float.isNaN(hi)) {
            float span = Math.max(4f, mx - mn);
            float mid = (mx + mn) / 2f;
            lo = mid - span * 0.6f;
            hi = mid + span * 0.6f;
        }
        path.reset();
        boolean started = false;
        float lastX = x;
        for (int i = 0; i < n; i++) {
            float v = arr[arr.length - n + i];
            if (Float.isNaN(v)) {
                started = false;
                continue;
            }
            float px = x + w * (PcLink.HIST - n + i) / (float) (PcLink.HIST - 1);
            float py = y + h * (1f - Math.max(0f, Math.min(1f, (v - lo) / (hi - lo))));
            if (!started) {
                path.moveTo(px, py);
                started = true;
            } else {
                path.lineTo(px, py);
            }
            lastX = px;
        }
        p.setShader(null);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(lw);
        p.setStrokeJoin(Paint.Join.ROUND);
        p.setColor(col);
        c.drawPath(path, p);
        if (fillBelow) {
            float firstX = x + w * (PcLink.HIST - n) / (float) (PcLink.HIST - 1);
            path.lineTo(lastX, y + h);
            path.lineTo(firstX, y + h);
            path.close();
            p.setStyle(Paint.Style.FILL);
            p.setShader(new LinearGradient(0, y, 0, y + h, a(col, 0x66), a(col, 0), Shader.TileMode.CLAMP));
            c.drawPath(path, p);
            p.setShader(null);
        }
        return true;
    }

    private void card(float x, float y, float w, float h) {
        rr(x, y, w, h, 8, a(0x1A0B3C, 0x55), 0xFF4A22A0, 1.5f);
    }

    // ---------- 1. Неон-карточки ----------
    private void neon(PcLink.Data d) {
        t("КОМПЬЮТЕР", 20, 34, 22, CYAN, russo, Paint.Align.LEFT);
        t((d.host.isEmpty() ? "USB" : d.host + " · USB"), 980, 34, 16, MUTED, russo, Paint.Align.RIGHT);
        card(15, 50, 310, 210);
        card(335, 50, 310, 210);
        card(655, 50, 330, 210);
        t("ПРОЦЕССОР", 28, 76, 15, MUTED, russo, Paint.Align.LEFT);
        t(f0(d.cpuT) + "°", 28, 160, 76, tc(d.cpuT), orb, Paint.Align.LEFT);
        t(d.cpuName, 28, 185, 13, MUTED, russo, Paint.Align.LEFT);
        t(f0(d.cpuL) + "% · " + f0(d.cpuW) + " Вт · " + f2(d.cpuClk / 1000f) + " ГГц", 28, 212, 15, 0xFFEAF8FF, russo, Paint.Align.LEFT);
        bar(28, 226, 284, 9, frac(d.cpuL, 100), CYAN, 0x33FFFFFF);
        t("ВИДЕОКАРТА", 348, 76, 15, MUTED, russo, Paint.Align.LEFT);
        t(f0(d.gpuT) + "°", 348, 160, 76, tc(d.gpuT), orb, Paint.Align.LEFT);
        t(d.gpuName, 348, 185, 13, MUTED, russo, Paint.Align.LEFT);
        t(f0(d.gpuL) + "% · " + f0(d.gpuW) + " Вт · " + f0(d.gpuClk) + " МГц", 348, 212, 15, 0xFFEAF8FF, russo, Paint.Align.LEFT);
        bar(348, 226, 284, 9, frac(d.gpuL, 100), CYAN, 0x33FFFFFF);
        t("ИГРА · FPS", 668, 76, 15, MUTED, russo, Paint.Align.LEFT);
        t(f0(d.fps), 668, 160, 76, YELLOW, orb, Paint.Align.LEFT);
        t("1% low " + f0(d.fps1) + " · " + f1(d.ft) + " мс", 668, 185, 14, 0xFFEAF8FF, russo, Paint.Align.LEFT);
        t(d.game.isEmpty() ? "игра не запущена" : d.game, 668, 212, 13, MUTED, russo, Paint.Align.LEFT);
        line(PcLink.fpsHist, 668, 222, 300, 30, YELLOW, 2f, Float.NaN, Float.NaN, false);
        card(15, 272, 230, 80);
        card(255, 272, 230, 80);
        card(495, 272, 230, 80);
        card(735, 272, 250, 80);
        t("ПАМЯТЬ", 28, 296, 14, MUTED, russo, Paint.Align.LEFT);
        t(f1(d.ramU) + " / " + f0(d.ramT) + " ГБ", 28, 326, 22, 0xFFEAF8FF, russo, Paint.Align.LEFT);
        bar(28, 336, 204, 7, frac(d.ramU, d.ramT), GREEN, 0x33FFFFFF);
        t("ВИДЕОПАМЯТЬ", 268, 296, 14, MUTED, russo, Paint.Align.LEFT);
        t(f1(d.vramU) + " / " + f0(d.vramT) + " ГБ", 268, 326, 22, 0xFFEAF8FF, russo, Paint.Align.LEFT);
        bar(268, 336, 204, 7, frac(d.vramU, d.vramT), GREEN, 0x33FFFFFF);
        t("ДИСК", 508, 296, 14, MUTED, russo, Paint.Align.LEFT);
        t(f0(d.diskU) + "% · " + f0(d.diskT) + "°", 508, 326, 22, 0xFFEAF8FF, russo, Paint.Align.LEFT);
        bar(508, 336, 204, 7, frac(d.diskU, 100), YELLOW, 0x33FFFFFF);
        t("СЕТЬ", 748, 296, 14, MUTED, russo, Paint.Align.LEFT);
        t("↓" + f1(d.netD) + "  ↑" + f1(d.netU) + " Мбит", 748, 326, 20, 0xFFEAF8FF, russo, Paint.Align.LEFT);
        t("вентилятор " + f0(d.fan) + " об/мин", 748, 344, 12, MUTED, russo, Paint.Align.LEFT);
        card(15, 364, 970, 84);
        line(PcLink.cpuTHist, 28, 372, 450, 68, PINK, 2.5f, Float.NaN, Float.NaN, false);
        line(PcLink.cpuLHist, 510, 372, 460, 68, CYAN, 2.5f, 0, 40, false);
    }

    // ---------- 2. Кольца ----------
    private void rings(PcLink.Data d) {
        p.setShader(new RadialGradient(500, 230, 600, 0xFF10122A, 0xFF04050D, Shader.TileMode.CLAMP));
        p.setStyle(Paint.Style.FILL);
        c.drawRect(-300, -200, 1300, 700, p);
        p.setShader(null);
        String[] name = {"ПРОЦЕССОР", "ВИДЕОКАРТА", "ПАМЯТЬ", "ВИДЕОПАМЯТЬ"};
        String[] val = {f0(d.cpuT), f0(d.gpuT), f1(d.ramU), f1(d.vramU)};
        String[] unit = {"°C", "°C", "ГБ", "ГБ"};
        float[] fr = {frac(d.cpuT, 100), frac(d.gpuT, 100), frac(d.ramU, d.ramT), frac(d.vramU, d.vramT)};
        int[] col = {tc(d.cpuT), tc(d.gpuT), 0xFF38BDF8, 0xFFA78BFA};
        String[] cap = {"нагрузка " + f0(d.cpuL) + "%", "нагрузка " + f0(d.gpuL) + "%", "из " + f0(d.ramT) + " ГБ", "из " + f0(d.vramT) + " ГБ"};
        for (int i = 0; i < 4; i++) {
            float cx = 135 + i * 243;
            float cy = 190;
            ring(cx, cy, 95, 16, fr[i], col[i], 0x1CFFFFFF);
            t(val[i], cx, cy + 14, 54, WHITE, orb, Paint.Align.CENTER);
            t(unit[i], cx, cy + 40, 16, 0xFF9AA4C7, russo, Paint.Align.CENTER);
            t(name[i], cx, cy - 120, 16, 0xFF9AA4C7, russo, Paint.Align.CENTER);
            t(cap[i], cx, cy + 135, 15, 0xFFCFD6F5, russo, Paint.Align.CENTER);
        }
        t("FPS", 90, 420, 15, 0xFF9AA4C7, russo, Paint.Align.LEFT);
        t(f0(d.fps), 140, 424, 34, YELLOW, orb, Paint.Align.LEFT);
        t("ЧАСТОТА " + f2(d.cpuClk / 1000f) + " ГГц · " + f0(d.cpuW) + " Вт", 340, 420, 16, 0xFFCFD6F5, russo, Paint.Align.LEFT);
        t("СЕТЬ ↓" + f1(d.netD) + " ↑" + f1(d.netU), 640, 420, 16, 0xFFCFD6F5, russo, Paint.Align.LEFT);
        t("ДИСК " + f0(d.diskT) + "°", 880, 420, 16, 0xFFCFD6F5, russo, Paint.Align.LEFT);
    }

    // ---------- 3. Игровой HUD ----------
    private void hud(PcLink.Data d) {
        p.setShader(new LinearGradient(0, 0, 1000, 0, 0x2200FFA3, 0x0000FFA3, Shader.TileMode.CLAMP));
        p.setStyle(Paint.Style.FILL);
        c.drawRect(-300, -200, 1300, 700, p);
        p.setShader(null);
        t("FPS", 30, 70, 26, 0xFF00FFA3, orb, Paint.Align.LEFT);
        t(f0(d.fps), 24, 300, 220, WHITE, orb, Paint.Align.LEFT);
        t("1% LOW  " + f0(d.fps1), 34, 350, 28, 0xFF00FFA3, orb, Paint.Align.LEFT);
        t(f1(d.ft) + " мс  ·  " + (d.game.isEmpty() ? "игра не запущена" : d.game), 34, 385, 18, 0xFF8A96A8, russo, Paint.Align.LEFT);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(1f);
        p.setColor(0x18FFFFFF);
        for (int i = 0; i < 5; i++) {
            c.drawLine(520, 60 + i * 60, 980, 60 + i * 60, p);
        }
        t("ВРЕМЯ КАДРА", 520, 40, 15, 0xFF8A96A8, russo, Paint.Align.LEFT);
        line(PcLink.ftHist, 520, 60, 460, 240, 0xFF00FFA3, 2f, Float.NaN, Float.NaN, true);
        String[] l = {"ЦП", "ГП", "ОЗУ", "ВРАМ"};
        String[] v = {f0(d.cpuT) + "°  " + f0(d.cpuL) + "%", f0(d.gpuT) + "°  " + f0(d.gpuL) + "%", f1(d.ramU) + " ГБ", f1(d.vramU) + " ГБ"};
        for (int i = 0; i < 4; i++) {
            t(l[i], 520 + i * 118, 350, 14, 0xFF8A96A8, russo, Paint.Align.LEFT);
            t(v[i], 520 + i * 118, 378, 19, WHITE, russo, Paint.Align.LEFT);
        }
        t("● LIVE", 980, 30, 15, 0xFFFF3B5C, orb, Paint.Align.RIGHT);
    }

    // ---------- 4. Терминал ----------
    private void terminal(PcLink.Data d) {
        p.setShader(null);
        p.setStyle(Paint.Style.FILL);
        p.setColor(0x0600FF6A);
        for (int y = 0; y < 460; y += 4) {
            c.drawRect(0, y, 1000, y + 2, p);
        }
        int n = Float.isNaN(d.ramU) || Float.isNaN(d.ramT) || d.ramT <= 0 ? 0 : Math.round(d.ramU / d.ramT * 30);
        StringBuilder bars = new StringBuilder();
        for (int i = 0; i < 30; i++) {
            bars.append(i < n ? '#' : '.');
        }
        String host = d.host.isEmpty() ? "pc" : d.host.toLowerCase(Locale.US);
        String[] L = {
            "root@" + host + ":~# pcstats --live",
            "",
            "CPU   " + (d.cpuName.isEmpty() ? "-" : d.cpuName),
            "      temp  " + f0(d.cpuT) + " C   load " + f0(d.cpuL) + "%   " + f0(d.cpuW) + " W   " + f2(d.cpuClk / 1000f) + " GHz",
            "GPU   " + (d.gpuName.isEmpty() ? "-" : d.gpuName),
            "      temp  " + f0(d.gpuT) + " C   load " + f0(d.gpuL) + "%   " + f0(d.gpuW) + " W   vram " + f1(d.vramU) + "/" + f0(d.vramT) + " GB",
            "MEM   " + f1(d.ramU) + " / " + f0(d.ramT) + " GB   [" + bars + "]",
            "DISK  used " + f0(d.diskU) + "%   " + f0(d.diskT) + " C",
            "NET   down " + f1(d.netD) + " Mbit/s   up " + f1(d.netU) + " Mbit/s",
            "FAN   " + f0(d.fan) + " rpm",
            "GAME  " + (d.game.isEmpty() ? "-" : d.game) + "   " + f0(d.fps) + " fps   1% " + f0(d.fps1) + "   " + f1(d.ft) + " ms",
            "",
            "uptime " + (d.uptime.isEmpty() ? "-" : d.uptime) + "  $ _"
        };
        for (int i = 0; i < L.length; i++) {
            int col = i == 0 ? 0xFF7DFFB0 : L[i].startsWith("      ") ? 0xFF2BD96A : 0xFF3DFF85;
            t(L[i], 26, 50 + i * 30, 22, col, mono, Paint.Align.LEFT);
        }
        line(PcLink.fpsHist, 640, 300, 330, 110, 0xFF7DFFB0, 2f, Float.NaN, Float.NaN, false);
    }

    // ---------- 5. Минимал ----------
    private void minimal(PcLink.Data d) {
        String[] lab = {"CPU", "GPU", "RAM", "FPS"};
        String[] val = {f0(d.cpuT), f0(d.gpuT), f1(d.ramU), f0(d.fps)};
        String[] unit = {"°", "°", "ГБ", ""};
        String[] sub = {f0(d.cpuL) + "%", f0(d.gpuL) + "%", "из " + f0(d.ramT), "1% " + f0(d.fps1)};
        for (int i = 0; i < 4; i++) {
            float x = 40 + i * 240;
            t(lab[i], x, 110, 16, 0xFF666666, russo, Paint.Align.LEFT);
            float tw = t(val[i], x, 230, 100, WHITE, orb, Paint.Align.LEFT);
            t(unit[i], x + tw + 6, 160, 26, 0xFF888888, orb, Paint.Align.LEFT);
            t(sub[i], x, 270, 20, 0xFFAAAAAA, russo, Paint.Align.LEFT);
        }
        p.setStyle(Paint.Style.FILL);
        p.setColor(0xFF222222);
        c.drawRect(40, 310, 960, 311, p);
        String[] bl = {"ЧАСТОТА", "МОЩНОСТЬ", "ВИДЕОПАМЯТЬ", "ДИСК", "СЕТЬ"};
        float pw = (Float.isNaN(d.cpuW) ? 0 : d.cpuW) + (Float.isNaN(d.gpuW) ? 0 : d.gpuW);
        String[] bv = {f2(d.cpuClk / 1000f) + " ГГц", f0(pw) + " Вт", f1(d.vramU) + " ГБ", f0(d.diskT) + "°", "↓" + f1(d.netD) + " ↑" + f1(d.netU)};
        for (int i = 0; i < 5; i++) {
            t(bl[i], 40 + i * 185, 350, 13, 0xFF555555, russo, Paint.Align.LEFT);
            t(bv[i], 40 + i * 185, 385, 22, 0xFFDDDDDD, russo, Paint.Align.LEFT);
        }
        line(PcLink.cpuTHist, 40, 410, 920, 30, 0x55FFFFFF, 1.5f, Float.NaN, Float.NaN, false);
    }

    // ---------- 6. Синтвейв ----------
    private void synth(PcLink.Data d) {
        p.setStyle(Paint.Style.FILL);
        p.setShader(new LinearGradient(0, 0, 0, 460, new int[]{0xFF1B0B4A, 0xFF8A1C7C, 0xFF12042E, 0xFF12042E}, new float[]{0f, 0.55f, 0.56f, 1f}, Shader.TileMode.CLAMP));
        c.drawRect(-300, -200, 1300, 700, p);
        p.setShader(new LinearGradient(0, 60, 0, 250, 0xFFFFE14D, 0xFFFF2A9D, Shader.TileMode.CLAMP));
        rf.set(370, 120, 630, 380);
        c.drawArc(rf, 180, 180, true, p);
        p.setShader(null);
        p.setColor(0xFF8A1C7C);
        for (int i = 0; i < 6; i++) {
            c.drawRect(370, 170 + i * 14, 630, 170 + i * 14 + 3 + i, p);
        }
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(1.5f);
        p.setColor(0xAAFF2A9D);
        for (int i = -12; i <= 12; i++) {
            c.drawLine(500 + i * 18, 254, 500 + i * 120, 480, p);
        }
        for (int i = 0; i < 8; i++) {
            float y = 254 + (float) Math.pow(i / 8f, 2) * 206;
            c.drawLine(-200, y, 1200, y, p);
        }
        glow(f0(d.cpuT) + "°", 60, 150, 110, 0xFF7DF9FF, 0xFF00F0FF, Paint.Align.LEFT);
        glow(f0(d.gpuT) + "°", 640, 150, 110, 0xFFFF9CD3, 0xFFFF2A9D, Paint.Align.LEFT);
        t("CPU", 66, 185, 22, WHITE, russo, Paint.Align.LEFT);
        t("GPU", 646, 185, 22, WHITE, russo, Paint.Align.LEFT);
        t(f0(d.cpuL) + "%  " + f0(d.cpuW) + " Вт", 66, 215, 20, 0xCCFFFFFF, russo, Paint.Align.LEFT);
        t(f0(d.gpuL) + "%  " + f0(d.gpuW) + " Вт", 646, 215, 20, 0xCCFFFFFF, russo, Paint.Align.LEFT);
        glow(f0(d.fps) + " FPS", 500, 410, 60, 0xFFFFE14D, 0xFFFFE14D, Paint.Align.CENTER);
        t("ОЗУ " + f1(d.ramU) + "/" + f0(d.ramT) + "  ·  ВРАМ " + f1(d.vramU) + "/" + f0(d.vramT), 500, 445, 17, 0xCCFFFFFF, russo, Paint.Align.CENTER);
    }

    private void glow(String s, float x, float y, float size, int col, int glowCol, Paint.Align al) {
        p.setShader(null);
        p.setStyle(Paint.Style.FILL);
        p.setTypeface(orb);
        p.setTextSize(size);
        p.setColor(col);
        p.setTextAlign(al);
        p.setShadowLayer(24f, 0f, 0f, glowCol);
        c.drawText(s, x, y, p);
        p.clearShadowLayer();
    }

    // ---------- 7. Стрелочные приборы ----------
    private void gauges(PcLink.Data d) {
        p.setStyle(Paint.Style.FILL);
        p.setShader(new LinearGradient(0, 0, 0, 460, 0xFF1C1C20, 0xFF0B0B0D, Shader.TileMode.CLAMP));
        c.drawRect(-300, -200, 1300, 700, p);
        p.setShader(null);
        gauge(210, 210, 150, d.cpuT, 110, "ПРОЦЕССОР");
        gauge(790, 210, 150, d.gpuT, 110, "ВИДЕОКАРТА");
        t(f0(d.fps), 500, 200, 96, YELLOW, orb, Paint.Align.CENTER);
        t("FPS", 500, 235, 22, 0xFF99AAAA, russo, Paint.Align.CENTER);
        t("ОЗУ " + f1(d.ramU) + "/" + f0(d.ramT) + " ГБ", 500, 300, 18, 0xFFDDDDEE, russo, Paint.Align.CENTER);
        t("ВРАМ " + f1(d.vramU) + "/" + f0(d.vramT) + " ГБ", 500, 328, 18, 0xFFDDDDEE, russo, Paint.Align.CENTER);
        t("↓" + f1(d.netD) + " ↑" + f1(d.netU), 500, 356, 18, 0xFFDDDDEE, russo, Paint.Align.CENTER);
        t(f0(d.fan) + " об/мин", 500, 384, 18, 0xFFDDDDEE, russo, Paint.Align.CENTER);
    }

    private void gauge(float cx, float cy, float r, float v, float max, String label) {
        float start = 144f;
        float sweep = 252f;
        rf.set(cx - r, cy - r, cx + r, cy + r);
        p.setShader(null);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(18f);
        p.setColor(0xFF2A2A30);
        c.drawArc(rf, start, sweep, false, p);
        float[][] z = {{0f, 0.6f}, {0.6f, 0.8f}, {0.8f, 1f}};
        int[] zc = {0xCC3CFF9A, 0xCCFCEE0A, 0xCCFF2A4D};
        for (int i = 0; i < 3; i++) {
            p.setColor(zc[i]);
            c.drawArc(rf, start + sweep * z[i][0], sweep * (z[i][1] - z[i][0]), false, p);
        }
        p.setStrokeWidth(2f);
        p.setColor(0xAACFD6DD);
        for (int i = 0; i <= 10; i++) {
            double ang = Math.toRadians(start + sweep * i / 10f);
            c.drawLine(cx + (float) Math.cos(ang) * (r - 18), cy + (float) Math.sin(ang) * (r - 18),
                    cx + (float) Math.cos(ang) * (r - 30), cy + (float) Math.sin(ang) * (r - 30), p);
        }
        float f = Float.isNaN(v) ? 0f : Math.min(1f, v / max);
        double ang = Math.toRadians(start + sweep * f);
        p.setStrokeWidth(5f);
        p.setStrokeCap(Paint.Cap.ROUND);
        p.setColor(0xFFFF5A36);
        c.drawLine(cx, cy, cx + (float) Math.cos(ang) * (r - 26), cy + (float) Math.sin(ang) * (r - 26), p);
        p.setStrokeCap(Paint.Cap.BUTT);
        p.setStyle(Paint.Style.FILL);
        p.setColor(0xFFDDDDDD);
        c.drawCircle(cx, cy, 12, p);
        t(f0(v) + "°", cx, cy + 75, 40, WHITE, orb, Paint.Align.CENTER);
        t(label, cx, cy + 105, 16, 0xFF99AAAA, russo, Paint.Align.CENTER);
    }

    // ---------- 8. Графики ----------
    private void graphs(PcLink.Data d) {
        String[] title = {
            "ЦП  " + f0(d.cpuT) + "°C  " + f0(d.cpuL) + "%",
            "ЦП температура",
            "ГП  " + f0(d.gpuT) + "°C  " + f0(d.gpuL) + "%",
            "FPS  " + f0(d.fps),
            "Сеть ↓" + f1(d.netD) + " Мбит",
            "Память " + f1(d.ramU) + "/" + f0(d.ramT) + " ГБ"
        };
        float[][] arr = {PcLink.cpuLHist, PcLink.cpuTHist, PcLink.gpuTHist, PcLink.fpsHist, PcLink.netHist, PcLink.ramHist};
        int[] col = {0xFF58A6FF, 0xFFFF7B72, 0xFFD2A8FF, 0xFFE3B341, 0xFF3FB950, 0xFF79C0FF};
        for (int i = 0; i < 6; i++) {
            float x = 14 + (i % 2) * 495;
            float y = 14 + (i / 2) * 148;
            rr(x, y, 480, 138, 8, 0xFF161B22, 0xFF30363D, 1f);
            t(title[i], x + 14, y + 26, 17, 0xFFC9D1D9, russo, Paint.Align.LEFT);
            line(arr[i], x + 10, y + 38, 460, 90, col[i], 2f, Float.NaN, Float.NaN, true);
        }
    }

    // ---------- 9. Карбон ----------
    private void carbon(PcLink.Data d) {
        p.setShader(null);
        p.setStyle(Paint.Style.FILL);
        p.setColor(0x08FFFFFF);
        for (int i = 0; i < 1000; i += 8) {
            for (int j = 0; j < 460; j += 8) {
                if ((i + j) % 16 == 0) {
                    c.drawRect(i, j, i + 4, j + 4, p);
                }
            }
        }
        t("CPU LOAD", 30, 50, 16, 0xFFFF9F1A, russo, Paint.Align.LEFT);
        segs(60, frac(d.cpuL, 100));
        t("GPU LOAD", 30, 120, 16, 0xFFFF9F1A, russo, Paint.Align.LEFT);
        segs(130, frac(d.gpuL, 100));
        float w1 = t(f0(d.cpuT), 30, 300, 130, WHITE, orb, Paint.Align.LEFT);
        t("°C CPU", 30 + w1 + 14, 300, 24, 0xFFFF9F1A, orb, Paint.Align.LEFT);
        float w2 = t(f0(d.gpuT), 520, 300, 130, WHITE, orb, Paint.Align.LEFT);
        t("°C GPU", 520 + w2 + 14, 300, 24, 0xFFFF9F1A, orb, Paint.Align.LEFT);
        rr(30, 330, 940, 100, 6, 0xFF161616, 0xFFFF9F1A, 2f);
        float pw = (Float.isNaN(d.cpuW) ? 0 : d.cpuW) + (Float.isNaN(d.gpuW) ? 0 : d.gpuW);
        String[] l = {"FPS", "1% LOW", "ОЗУ", "ВРАМ", "МОЩН."};
        String[] v = {f0(d.fps), f0(d.fps1), f1(d.ramU) + " ГБ", f1(d.vramU) + " ГБ", f0(pw) + " Вт"};
        for (int i = 0; i < 5; i++) {
            t(l[i], 50 + i * 185, 360, 13, 0xFFFF9F1A, russo, Paint.Align.LEFT);
            t(v[i], 50 + i * 185, 408, 34, WHITE, orb, Paint.Align.LEFT);
        }
    }

    private void segs(float y, float f) {
        int n = 34;
        float sw = 940f / n;
        p.setShader(null);
        p.setStyle(Paint.Style.FILL);
        for (int i = 0; i < n; i++) {
            boolean on = (float) i / n < f;
            int col = (float) i / n > 0.8f ? 0xFFFF2A2A : (float) i / n > 0.6f ? 0xFFFF9F1A : 0xFFFFD500;
            p.setColor(on ? col : 0xFF2A2A2A);
            c.drawRect(30 + i * sw, y, 30 + i * sw + sw - 4, y + 26, p);
        }
    }

    // ---------- 10. Стекло ----------
    private void glass(PcLink.Data d) {
        p.setStyle(Paint.Style.FILL);
        p.setShader(new LinearGradient(0, 0, 1000, 460, new int[]{0xFF4F46E5, 0xFFA855F7, 0xFFEC4899}, new float[]{0f, 0.5f, 1f}, Shader.TileMode.CLAMP));
        c.drawRect(-300, -200, 1300, 700, p);
        p.setShader(null);
        p.setColor(0x30FFFFFF);
        c.drawCircle(150, 380, 170, p);
        c.drawCircle(880, 80, 150, p);
        gcard(24, 24, 300, 260);
        gcard(340, 24, 300, 260);
        gcard(656, 24, 320, 260);
        gblock(50, "Процессор", f0(d.cpuT) + "°", f0(d.cpuL) + "% · " + f0(d.cpuW) + " Вт · " + f1(d.cpuClk / 1000f) + " ГГц", d.cpuL);
        gblock(366, "Видеокарта", f0(d.gpuT) + "°", f0(d.gpuL) + "% · " + f0(d.gpuW) + " Вт · " + f0(d.gpuClk) + " МГц", d.gpuL);
        t("Игра", 682, 66, 20, 0xCCFFFFFF, russo, Paint.Align.LEFT);
        t(f0(d.fps), 682, 190, 100, WHITE, orb, Paint.Align.LEFT);
        t("FPS · 1% low " + f0(d.fps1), 682, 230, 17, WHITE, russo, Paint.Align.LEFT);
        line(PcLink.fpsHist, 682, 240, 270, 34, WHITE, 2.5f, Float.NaN, Float.NaN, false);
        gcard(24, 300, 470, 136);
        gcard(510, 300, 466, 136);
        t("Память", 50, 338, 17, 0xCCFFFFFF, russo, Paint.Align.LEFT);
        t(f1(d.ramU) + " / " + f0(d.ramT) + " ГБ", 50, 376, 30, WHITE, russo, Paint.Align.LEFT);
        bar(50, 396, 200, 8, frac(d.ramU, d.ramT), WHITE, 0x40FFFFFF);
        t("Видеопамять " + f1(d.vramU) + "/" + f0(d.vramT), 270, 376, 17, WHITE, russo, Paint.Align.LEFT);
        bar(270, 396, 200, 8, frac(d.vramU, d.vramT), WHITE, 0x40FFFFFF);
        t("Сеть", 536, 338, 17, 0xCCFFFFFF, russo, Paint.Align.LEFT);
        t("↓ " + f1(d.netD) + "  ↑ " + f1(d.netU), 536, 376, 28, WHITE, russo, Paint.Align.LEFT);
        t("диск " + f0(d.diskT) + "°  ·  вентилятор " + f0(d.fan), 536, 412, 17, WHITE, russo, Paint.Align.LEFT);
    }

    private void gcard(float x, float y, float w, float h) {
        rr(x, y, w, h, 26, 0x2EFFFFFF, 0x66FFFFFF, 1.5f);
    }

    private void gblock(float x, String title, String big, String line, float load) {
        t(title, x, 66, 20, 0xCCFFFFFF, russo, Paint.Align.LEFT);
        t(big, x, 190, 100, WHITE, orb, Paint.Align.LEFT);
        t(line, x, 230, 17, WHITE, russo, Paint.Align.LEFT);
        bar(x, 246, 248, 8, frac(load, 100), WHITE, 0x40FFFFFF);
    }
}
