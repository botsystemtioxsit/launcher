package com.lockscreen.customplus;

import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import java.util.Locale;

/** Экран «компьютер»: температура и загрузка процессора и видеокарты, память, график. Данные приходят по USB-модему. */
final class PcDash {
    private static final int BG = 0xFF070314;
    private static final int CYAN = 0xFF00F0FF;
    private static final int PINK = 0xFFFF2A9D;
    private static final int GREEN = 0xFF3CFF9A;
    private static final int YELLOW = 0xFFFCEE0A;
    private static final int TEXT = 0xFFEAF8FF;
    private static final int MUTED = 0xFFA08FE0;
    private static final int CARD = 0x2A1A0B3C;
    private static final int LINE = 0xFF4A22A0;

    private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF rf = new RectF();
    private final Path path = new Path();
    private final Typeface display;
    private final Typeface label;

    PcDash() {
        Typeface d = null;
        Typeface l = null;
        try {
            d = Fonts.get("asset:Orbitron.ttf");
            l = Fonts.get("asset:RussoOne.ttf");
        } catch (Throwable t) {
        }
        display = d != null ? d : Typeface.DEFAULT_BOLD;
        label = l != null ? l : Typeface.DEFAULT_BOLD;
    }

    void render(Canvas c, int W, int H, PcLink.Data d, long now) {
        c.drawColor(BG);
        float u = Math.min(W, H) / 100f;
        float m = 4.2f * u;
        boolean wide = W > H * 1.15f;
        // шапка
        text(c, "КОМПЬЮТЕР", m, m + 3.4f * u, 3.4f * u, CYAN, label, Paint.Align.LEFT);
        String host = d.host.isEmpty() ? "подключён по USB" : d.host + " · USB";
        text(c, host, W - m, m + 3.4f * u, 2.6f * u, MUTED, label, Paint.Align.RIGHT);
        float top = m + 7f * u;
        float bottom = H - m;
        if (wide) {
            float cw = (W - 3 * m) / 2f;
            RectF cpu = new RectF(m, top, m + cw, top + (bottom - top) * 0.58f);
            RectF gpu = new RectF(m * 2 + cw, top, m * 2 + cw * 2, cpu.bottom);
            RectF ram = new RectF(m, cpu.bottom + m * 0.6f, m + cw, bottom);
            RectF gr = new RectF(m * 2 + cw, cpu.bottom + m * 0.6f, m * 2 + cw * 2, bottom);
            block(c, cpu, u, "ПРОЦЕССОР", d.cpuName, d.cpuT, d.cpuL, d.cpuW);
            block(c, gpu, u, "ВИДЕОКАРТА", d.gpuName, d.gpuT, d.gpuL, Float.NaN);
            ramBar(c, ram, u, d);
            graph(c, gr, u);
        } else {
            float h1 = (bottom - top - 3 * m * 0.6f);
            RectF cpu = new RectF(m, top, W - m, top + h1 * 0.30f);
            RectF gpu = new RectF(m, cpu.bottom + m * 0.6f, W - m, cpu.bottom + m * 0.6f + h1 * 0.30f);
            RectF ram = new RectF(m, gpu.bottom + m * 0.6f, W - m, gpu.bottom + m * 0.6f + h1 * 0.12f);
            RectF gr = new RectF(m, ram.bottom + m * 0.6f, W - m, bottom);
            block(c, cpu, u, "ПРОЦЕССОР", d.cpuName, d.cpuT, d.cpuL, d.cpuW);
            block(c, gpu, u, "ВИДЕОКАРТА", d.gpuName, d.gpuT, d.gpuL, Float.NaN);
            ramBar(c, ram, u, d);
            graph(c, gr, u);
        }
    }

    private int tempColor(float t) {
        if (Float.isNaN(t)) {
            return MUTED;
        }
        return t >= 80 ? PINK : t >= 65 ? YELLOW : GREEN;
    }

    private void block(Canvas c, RectF r, float u, String title, String name, float temp, float load, float watts) {
        card(c, r);
        float pad = 2.6f * u;
        text(c, title, r.left + pad, r.top + pad + 2.4f * u, 2.6f * u, MUTED, label, Paint.Align.LEFT);
        if (!name.isEmpty()) {
            text(c, fit(name, r.width() - 2 * pad - 24 * u, 2.2f * u), r.right - pad, r.top + pad + 2.4f * u, 2.2f * u, MUTED, label, Paint.Align.RIGHT);
        }
        String t = Float.isNaN(temp) ? "—" : String.format(Locale.US, "%.0f", temp);
        float size = Math.min(r.height() * 0.46f, 16f * u);
        float by = r.top + pad + 3.5f * u + size;
        text(c, t, r.left + pad, by, size, tempColor(temp), display, Paint.Align.LEFT);
        float tw = measure(t, size, display);
        if (!Float.isNaN(temp)) {
            text(c, "°C", r.left + pad + tw + u, by - size * 0.45f, size * 0.38f, MUTED, display, Paint.Align.LEFT);
        } else {
            text(c, "нет датчика", r.left + pad + tw + 2 * u, by - size * 0.3f, 2.4f * u, MUTED, label, Paint.Align.LEFT);
        }
        // загрузка
        float bx = r.left + pad;
        float bw = r.width() - 2 * pad;
        float by2 = r.bottom - pad - 2.2f * u;
        String lt = Float.isNaN(load) ? "загрузка: —" : String.format(Locale.US, "загрузка %.0f%%", load);
        if (!Float.isNaN(watts)) {
            lt += String.format(Locale.US, " · %.0f Вт", watts);
        }
        text(c, lt, bx, by2 - 1.6f * u, 2.4f * u, TEXT, label, Paint.Align.LEFT);
        bar(c, bx, by2, bw, 1.8f * u, Float.isNaN(load) ? 0 : load / 100f, CYAN);
    }

    private void ramBar(Canvas c, RectF r, float u, PcLink.Data d) {
        card(c, r);
        float pad = 2.6f * u;
        String s = Float.isNaN(d.ramU) || Float.isNaN(d.ramT)
                ? "ПАМЯТЬ: —"
                : String.format(Locale.US, "ПАМЯТЬ  %.1f / %.0f ГБ", d.ramU, d.ramT);
        text(c, s, r.left + pad, r.top + pad + 2.6f * u, 2.8f * u, TEXT, label, Paint.Align.LEFT);
        float f = (Float.isNaN(d.ramU) || Float.isNaN(d.ramT) || d.ramT <= 0) ? 0 : d.ramU / d.ramT;
        bar(c, r.left + pad, r.bottom - pad - 1.8f * u, r.width() - 2 * pad, 1.8f * u, f, f > 0.85f ? PINK : GREEN);
    }

    private void graph(Canvas c, RectF r, float u) {
        card(c, r);
        float pad = 2.6f * u;
        text(c, "ПРОЦЕССОР: температура и загрузка", r.left + pad, r.top + pad + 2.4f * u, 2.4f * u, MUTED, label, Paint.Align.LEFT);
        float gx = r.left + pad;
        float gy = r.top + pad + 5f * u;
        float gw = r.width() - 2 * pad;
        float gh = r.height() - 2 * pad - 5f * u;
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(Math.max(1f, 0.2f * u));
        p.setColor(LINE);
        for (int i = 0; i <= 4; i++) {
            float y = gy + gh * i / 4f;
            c.drawLine(gx, y, gx + gw, y, p);
        }
        line(c, PcLink.cpuLHist, gx, gy, gw, gh, CYAN, u);
        line(c, PcLink.cpuTHist, gx, gy, gw, gh, PINK, u);
    }

    private void line(Canvas c, float[] a, float gx, float gy, float gw, float gh, int col, float u) {
        int n = PcLink.histN;
        if (n < 2) {
            return;
        }
        path.reset();
        boolean started = false;
        for (int i = 0; i < n; i++) {
            float v = a[a.length - n + i];
            if (Float.isNaN(v)) {
                started = false;
                continue;
            }
            float x = gx + gw * i / (float) (PcLink.HIST - 1) + gw * (PcLink.HIST - n) / (float) (PcLink.HIST - 1);
            float y = gy + gh * (1f - Math.max(0f, Math.min(100f, v)) / 100f);
            if (!started) {
                path.moveTo(x, y);
                started = true;
            } else {
                path.lineTo(x, y);
            }
        }
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(Math.max(2f, 0.5f * u));
        p.setColor(col);
        c.drawPath(path, p);
    }

    private void bar(Canvas c, float x, float y, float w, float h, float frac, int col) {
        frac = Math.max(0f, Math.min(1f, frac));
        p.setStyle(Paint.Style.FILL);
        p.setColor(0x33FFFFFF);
        rf.set(x, y, x + w, y + h);
        c.drawRoundRect(rf, h / 2, h / 2, p);
        p.setColor(col);
        rf.set(x, y, x + Math.max(h, w * frac), y + h);
        c.drawRoundRect(rf, h / 2, h / 2, p);
    }

    private void card(Canvas c, RectF r) {
        p.setStyle(Paint.Style.FILL);
        p.setColor(CARD);
        c.drawRoundRect(r, 8f, 8f, p);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(2f);
        p.setColor(LINE);
        c.drawRoundRect(r, 8f, 8f, p);
    }

    private float measure(String s, float size, Typeface tf) {
        p.setTypeface(tf);
        p.setTextSize(size);
        return p.measureText(s);
    }

    private String fit(String s, float w, float size) {
        p.setTypeface(label);
        p.setTextSize(size);
        if (w <= 0) {
            return "";
        }
        String t = s;
        while (t.length() > 3 && p.measureText(t) > w) {
            t = t.substring(0, t.length() - 2);
        }
        return t.equals(s) ? s : t + "…";
    }

    private void text(Canvas c, String s, float x, float y, float size, int color, Typeface tf, Paint.Align al) {
        p.setStyle(Paint.Style.FILL);
        p.setTypeface(tf);
        p.setTextSize(size);
        p.setColor(color);
        p.setTextAlign(al);
        c.drawText(s, x, y, p);
    }
}
