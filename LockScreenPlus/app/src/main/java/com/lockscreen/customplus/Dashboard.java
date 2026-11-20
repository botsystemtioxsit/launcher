package com.lockscreen.customplus;

import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/**
 * Экран «статистика по USB»: когда телефон подключён кабелем, вместо обычных обоев рисуется панель
 * (часы, заряд, ток, напряжение, температура, память, график и журнал событий) в киберпанк-стиле проекта.
 * Рисуется целиком кодом, подстраивается под вертикальный и боковой экран.
 */
final class Dashboard {
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
    private final Typeface mono;
    private final Typeface label;
    private final SimpleDateFormat fHm = new SimpleDateFormat("HH:mm", Locale.getDefault());
    private final SimpleDateFormat fDate = new SimpleDateFormat("EEEE, d MMMM", Locale.getDefault());
    private final SimpleDateFormat fSec = new SimpleDateFormat("ss", Locale.getDefault());

    Dashboard() {
        Typeface d = null;
        Typeface l = null;
        try {
            d = Fonts.get("asset:Orbitron.ttf");   // цифры (латиница и цифры)
            l = Fonts.get("asset:RussoOne.ttf");   // подписи (есть кириллица)
        } catch (Throwable t) {
        }
        display = d != null ? d : Typeface.DEFAULT_BOLD;
        label = l != null ? l : Typeface.DEFAULT_BOLD;
        mono = Typeface.MONOSPACE;                  // журнал
    }

    void render(Canvas c, int W, int H, StatsHub.Snap s, long now) {
        c.drawColor(BG);
        float u = Math.min(W, H) / 100f; // «единица» — 1% короткой стороны
        drawGrid(c, W, H, u, now);
        Date date = new Date(now);
        boolean wide = W > H * 1.15f;
        float m = 4.2f * u;
        if (wide) {
            float colW = (W - 4 * m) / 3f;
            float left = m;
            float mid = m * 2 + colW;
            float right = m * 3 + colW * 2;
            drawHeader(c, left, m, colW * 2 + m, u, now, s);
            drawClockBlock(c, left, 9.5f * u, colW, H - m, u, date, s, now);
            drawTiles(c, new RectF(mid, 9.5f * u, mid + colW, 9.5f * u + (H - 9.5f * u - m) * 0.52f), u, s);
            float midTop = 9.5f * u + (H - 9.5f * u - m) * 0.52f + m * 0.6f;
            drawBars(c, new RectF(mid, midTop, mid + colW, midTop + 18f * u), u, s);
            drawSpark(c, new RectF(mid, midTop + 18f * u + m * 0.6f, mid + colW, H - m), u);
            drawLog(c, new RectF(right, 9.5f * u, W - m, H - m), u);
        } else {
            float w = W - 2 * m;
            drawHeader(c, m, m, w, u, now, s);
            float y = 9f * u;
            float clockH = 30f * u;
            drawClockBlock(c, m, y, w, y + clockH + 38f * u, u, date, s, now);
            y += clockH + 38f * u + m * 0.4f;
            float tilesH = 30f * u;
            drawTiles(c, new RectF(m, y, m + w, y + tilesH), u, s);
            y += tilesH + m * 0.5f;
            drawBars(c, new RectF(m, y, m + w, y + 18f * u), u, s);
            y += 18f * u + m * 0.5f;
            float sparkH = 13f * u;
            drawSpark(c, new RectF(m, y, m + w, y + sparkH), u);
            y += sparkH + m * 0.5f;
            drawLog(c, new RectF(m, y, m + w, H - m), u);
        }
    }

    private void drawGrid(Canvas c, int W, int H, float u, long now) {
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(Math.max(1f, u * 0.08f));
        p.setColor(0x14A259FF);
        float step = u * 8f;
        for (float x = 0; x < W; x += step) {
            c.drawLine(x, 0, x, H, p);
        }
        for (float y = 0; y < H; y += step) {
            c.drawLine(0, y, W, y, p);
        }
        // бегущая светлая полоса
        float sweep = ((now / 18) % (H + 200)) - 100;
        p.setStyle(Paint.Style.FILL);
        p.setColor(0x0A00F0FF);
        c.drawRect(0, sweep, W, sweep + u * 6f, p);
    }

    private void drawHeader(Canvas c, float x, float y, float w, float u, long now, StatsHub.Snap s) {
        boolean pulse = (now / 600) % 2 == 0;
        p.setStyle(Paint.Style.FILL);
        p.setColor(s.usbData ? (pulse ? GREEN : 0xFF1E7A4E) : (pulse ? YELLOW : 0xFF7A7410));
        c.drawCircle(x + u * 1.3f, y + u * 2f, u * 1.1f, p);
        String t = s.usbData ? "USB · ПОДКЛЮЧЕНО К КОМПЬЮТЕРУ" : "USB · ПИТАНИЕ";
        text(c, t, x + u * 3.4f, y + u * 3.1f, u * 2.9f, CYAN, label, Paint.Align.LEFT);
    }

    private void drawClockBlock(Canvas c, float x, float y, float w, float bottom, float u, Date date, StatsHub.Snap s, long now) {
        float cx = x + w / 2f;
        String hm = fHm.format(date);
        text(c, hm, cx, y + u * 13f, u * 15f, TEXT, display, Paint.Align.CENTER);
        text(c, fSec.format(date), cx, y + u * 18.5f, u * 3.6f, PINK, label, Paint.Align.CENTER);
        String d = fDate.format(date);
        text(c, d.substring(0, 1).toUpperCase(Locale.getDefault()) + d.substring(1), cx, y + u * 23.8f, u * 3.3f, MUTED, label, Paint.Align.CENTER);
        // кольцо заряда
        float r = Math.min(w * 0.34f, (bottom - y - u * 27f) * 0.38f);
        float ry = y + u * 27f + r + u * 2f;
        if (ry + r + u * 6f > bottom) {
            r = Math.max(u * 6f, (bottom - y - u * 33f) / 2f);
            ry = y + u * 27f + r + u * 2f;
        }
        int level = Math.max(0, s.level);
        int col = level > 50 ? GREEN : (level > 20 ? YELLOW : PINK);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeCap(Paint.Cap.ROUND);
        p.setStrokeWidth(u * 2.2f);
        p.setColor(0x33A259FF);
        rf.set(cx - r, ry - r, cx + r, ry + r);
        c.drawArc(rf, 135, 270, false, p);
        p.setColor(col);
        c.drawArc(rf, 135, 270f * level / 100f, false, p);
        p.setStrokeCap(Paint.Cap.BUTT);
        text(c, s.level >= 0 ? level + "%" : "—", cx, ry + u * 1.8f, u * 7.4f, TEXT, display, Paint.Align.CENTER);
        text(c, s.charging ? "ЗАРЯДКА" : "БАТАРЕЯ", cx, ry + u * 6.6f, u * 2.5f, col, label, Paint.Align.CENTER);
        String eta = s.status;
        if (s.charging && s.etaMs > 0) {
            long min = s.etaMs / 60000;
            eta = "до полной ~" + (min >= 60 ? (min / 60) + " ч " + (min % 60) + " мин" : min + " мин");
        }
        text(c, eta, cx, ry + r + u * 4.4f, u * 2.9f, MUTED, label, Paint.Align.CENTER);
    }

    private void drawTiles(Canvas c, RectF area, float u, StatsHub.Snap s) {
        float gap = u * 1.6f;
        float tw = (area.width() - gap) / 2f;
        float th = (area.height() - gap) / 2f;
        float power = (s.currentMa / 1000f) * (s.voltMv / 1000f);
        String[][] items = {
                {"ТОК", String.valueOf(s.currentMa), "мА"},
                {"НАПРЯЖЕНИЕ", StatsHub.fmt1(s.voltMv / 1000f), "В"},
                {"ТЕМПЕРАТУРА", StatsHub.fmt1(s.tempC), "°C"},
                {"МОЩНОСТЬ", StatsHub.fmt1(power), "Вт"},
        };
        int[] cols = {CYAN, YELLOW, s.tempC >= 40f ? PINK : GREEN, PINK};
        for (int i = 0; i < 4; i++) {
            float x = area.left + (i % 2) * (tw + gap);
            float y = area.top + (i / 2) * (th + gap);
            rf.set(x, y, x + tw, y + th);
            card(c, rf, cols[i]);
            text(c, items[i][0], x + u * 2f, y + u * 3.6f, u * 2.2f, MUTED, label, Paint.Align.LEFT);
            text(c, items[i][1], x + u * 2f, y + th - u * 4.4f, u * 7.2f, TEXT, display, Paint.Align.LEFT);
            text(c, items[i][2], x + u * 2f, y + th - u * 1.3f, u * 2.6f, cols[i], label, Paint.Align.LEFT);
        }
    }

    private void drawBars(Canvas c, RectF area, float u, StatsHub.Snap s) {
        float h = (area.height() - u * 1.6f) / 2f;
        bar(c, area.left, area.top, area.width(), h, u, "ОЗУ", s.ramUsed, s.ramTotal, CYAN);
        bar(c, area.left, area.top + h + u * 1.6f, area.width(), h, u, "ПАМЯТЬ", s.storUsed, s.storTotal, PINK);
    }

    private void bar(Canvas c, float x, float y, float w, float h, float u, String name, long used, long total, int col) {
        rf.set(x, y, x + w, y + h);
        card(c, rf, col);
        float frac = total > 0 ? Math.min(1f, (float) used / total) : 0f;
        text(c, name, x + u * 2f, y + h * 0.5f, u * 2.4f, MUTED, label, Paint.Align.LEFT);
        String v = gb(used) + " / " + gb(total) + " ГБ";
        text(c, v, x + w - u * 2f, y + h * 0.5f, u * 2.4f, TEXT, mono, Paint.Align.RIGHT);
        float bx = x + u * 2f;
        float bw = w - u * 4f;
        float by = y + h - u * 1.7f;
        p.setStyle(Paint.Style.FILL);
        p.setColor(0x33A259FF);
        rf.set(bx, by, bx + bw, by + u * 0.9f);
        c.drawRect(rf, p);
        p.setColor(col);
        rf.set(bx, by, bx + bw * frac, by + u * 0.9f);
        c.drawRect(rf, p);
    }

    private void drawSpark(Canvas c, RectF area, float u) {
        card(c, area, YELLOW);
        text(c, "ЗАРЯД И ТЕМПЕРАТУРА", area.left + u * 2f, area.top + u * 3.2f, u * 2.1f, MUTED, label, Paint.Align.LEFT);
        int n = Math.min(StatsHub.histN, StatsHub.HIST);
        if (n < 2) {
            text(c, "копится…", area.centerX(), area.centerY() + u, u * 2.6f, MUTED, label, Paint.Align.CENTER);
            return;
        }
        float gx = area.left + u * 2f;
        float gy = area.top + u * 5f;
        float gw = area.width() - u * 4f;
        float gh = area.height() - u * 7f;
        line(c, StatsHub.levelHist, n, gx, gy, gw, gh, 0f, 100f, GREEN, u);
        float tmin = 1e9f;
        float tmax = -1e9f;
        for (int i = StatsHub.HIST - n; i < StatsHub.HIST; i++) {
            tmin = Math.min(tmin, StatsHub.tempHist[i]);
            tmax = Math.max(tmax, StatsHub.tempHist[i]);
        }
        if (tmax - tmin < 4f) {
            float mid = (tmax + tmin) / 2f;
            tmin = mid - 2f;
            tmax = mid + 2f;
        }
        line(c, StatsHub.tempHist, n, gx, gy, gw, gh, tmin, tmax, PINK, u);
    }

    private void line(Canvas c, float[] a, int n, float gx, float gy, float gw, float gh, float lo, float hi, int col, float u) {
        path.reset();
        int start = a.length - n;
        for (int i = 0; i < n; i++) {
            float v = Math.max(lo, Math.min(hi, a[start + i]));
            float px = gx + gw * i / (n - 1f);
            float py = gy + gh - gh * (v - lo) / Math.max(0.001f, hi - lo);
            if (i == 0) {
                path.moveTo(px, py);
            } else {
                path.lineTo(px, py);
            }
        }
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(u * 0.45f);
        p.setStrokeJoin(Paint.Join.ROUND);
        p.setColor(col);
        c.drawPath(path, p);
    }

    private void drawLog(Canvas c, RectF area, float u) {
        card(c, area, GREEN);
        text(c, "ЖУРНАЛ", area.left + u * 2f, area.top + u * 3.6f, u * 2.2f, MUTED, label, Paint.Align.LEFT);
        float size = u * 2.45f;
        float lineH = size * 1.55f;
        int max = (int) ((area.height() - u * 7f) / lineH);
        List<String> lines = StatsHub.lines(Math.max(1, max));
        float y = area.top + u * 7.2f;
        for (int i = 0; i < lines.size(); i++) {
            int col = i == lines.size() - 1 ? TEXT : GREEN;
            text(c, fit(lines.get(i), area.width() - u * 4f, size), area.left + u * 2f, y, size, col, mono, Paint.Align.LEFT);
            y += lineH;
        }
    }

    private String fit(String s, float w, float size) {
        p.setTypeface(mono);
        p.setTextSize(size);
        if (p.measureText(s) <= w) {
            return s;
        }
        int n = p.breakText(s, true, w - p.measureText("…"), null);
        return s.substring(0, Math.max(0, n)) + "…";
    }

    private void card(Canvas c, RectF r, int accent) {
        float rad = Math.min(r.width(), r.height()) * 0.08f;
        p.setStyle(Paint.Style.FILL);
        p.setColor(CARD);
        c.drawRoundRect(r, rad, rad, p);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(Math.max(1.5f, Math.min(6f, Math.min(r.width(), r.height()) * 0.012f)));
        p.setColor(LINE);
        c.drawRoundRect(r, rad, rad, p);
        // яркий угол — как у кнопок Ghost Shell
        p.setColor(accent);
        float k = Math.min(r.width(), r.height()) * 0.18f;
        c.drawLine(r.left + rad, r.top, r.left + rad + k, r.top, p);
        c.drawLine(r.right - rad - k, r.bottom, r.right - rad, r.bottom, p);
    }

    private void text(Canvas c, String s, float x, float y, float size, int color, Typeface tf, Paint.Align al) {
        p.setStyle(Paint.Style.FILL);
        p.setTypeface(tf);
        p.setTextSize(size);
        p.setColor(color);
        p.setTextAlign(al);
        c.drawText(s, x, y, p);
    }

    private static String gb(long bytes) {
        return StatsHub.fmt1(bytes / 1073741824f);
    }
}
