package com.lockscreen.customplus;

import android.graphics.Bitmap;
import android.graphics.Camera;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.ColorMatrix;
import android.graphics.ColorMatrixColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.text.Layout;
import android.text.StaticLayout;
import android.text.TextPaint;
import android.text.TextUtils;
import androidx.core.view.ViewCompat;
import com.lockscreen.customplus.Design;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Random;

/* loaded from: classes3.dex */
final class Renderer {
    private float B = 1080.0f;
    private ColorMatrixColorFilter colorFilter;
    private SimpleDateFormat fDate;
    private SimpleDateFormat fH;
    private SimpleDateFormat fM;
    private boolean fmt24;
    private Locale fmtLocale;
    private int pH;
    private long pLast;
    private int pW;
    private int pkind;
    private int pn;
    private float[] pph;
    private float[] pspd;
    private float[] px;
    private float[] py;
    private boolean xfActive;
    final List<Hit> hits = new ArrayList();
    private final Paint bitmapPaint = new Paint(2);
    private final Paint clockPaint = new Paint(1);
    private final TextPaint textPaint = new TextPaint(1);
    private final Paint shapePaint = new Paint(1);
    private final Paint gradPaint = new Paint();
    private final Matrix matrix = new Matrix();
    private final Matrix xfm = new Matrix();
    private final Matrix camM = new Matrix();
    private final Camera camera = new Camera();
    private final Calendar cal = Calendar.getInstance();
    private int fmtDateIdx = -1;
    private int cmHue = Integer.MIN_VALUE;
    private int cmSat = Integer.MIN_VALUE;
    private final Random rnd = new Random(7);
    private final Map<String, StaticLayout> layouts = new HashMap();
    private final Map<String, String> layoutKeys = new HashMap();

    Renderer() {
    }

    static final class Hit {
        final String id;
        final RectF r;

        Hit(String id, RectF r) {
            this.id = id;
            this.r = r;
        }
    }

    void render(Canvas c, int W, int H, Design d, Bitmap bg, Bitmap cut, float tiltX, float tiltY, long now, String webText, String battery, boolean live) {
        float tdx;
        float tdy;
        Design design;
        Design design2;
        this.hits.clear();
        this.B = Math.min(W, H);
        prepareFormats(d);
        updateColorFilter(d);
        Date date = new Date(now);
        float maxShift = d.parallax ? W * 0.035f * (d.parallaxStrength / 100.0f) : 0.0f;
        if (!d.parallaxText) {
            tdx = 0.0f;
            tdy = 0.0f;
        } else {
            float tdx2 = W * tiltX * 0.03f;
            float tdy2 = W * tiltY * 0.03f;
            tdx = tdx2;
            tdy = tdy2;
        }
        if (d.bgMode == 0 && bg != null) {
            c.drawColor(0xFF000000);
            drawCover(c, bg, W, H, maxShift, maxShift * 0.5f * tiltX, 0.5f * maxShift * tiltY);
        } else {
            drawGradient(c, W, H, d, now);
        }
        if (d.dim > 0) {
            c.drawColor(Color.argb((d.dim * 255) / 100, 0, 0, 0));
        }
        drawParticles(c, W, H, d, now, live);
        if (d.stormy() && live) {
            long k = (now / 100) % 70;
            if (k == 0 || k == 3) {
                c.drawColor(Color.argb(110, 255, 255, 255));
            }
        }
        if (!d.clockShow || d.clockOver) {
            design = d;
        } else {
            design = d;
            drawClock(c, W, H, d, date, tdx, tdy);
        }
        if (cut == null || design.bgMode != 0) {
            design2 = design;
        } else {
            design2 = design;
            drawCover(c, cut, W, H, maxShift, maxShift * (-0.5f) * tiltX, (-0.5f) * maxShift * tiltY);
        }
        if (design2.clockShow && design2.clockOver) {
            drawClock(c, W, H, d, date, tdx, tdy);
        }
        drawElements(c, W, H, d, date, webText, battery, tdx, tdy);
    }

    private void prepareFormats(Design d) {
        Locale loc = Locale.getDefault();
        if (this.fH == null || this.fmt24 != d.use24 || !loc.equals(this.fmtLocale) || this.fmtDateIdx != d.dateFmt) {
            this.fmt24 = d.use24;
            this.fmtLocale = loc;
            this.fmtDateIdx = d.dateFmt;
            this.fH = new SimpleDateFormat(d.use24 ? "HH" : "h", loc);
            this.fM = new SimpleDateFormat("mm", loc);
            this.fDate = new SimpleDateFormat(Design.DATE_FORMATS[d.dateFmt], loc);
        }
    }

    private void updateColorFilter(Design d) {
        if (d.hue == this.cmHue && d.sat == this.cmSat) {
            return;
        }
        this.cmHue = d.hue;
        this.cmSat = d.sat;
        if (d.hue == 0 && d.sat == 100) {
            this.colorFilter = null;
        } else {
            ColorMatrix cm = new ColorMatrix();
            cm.setSaturation(d.sat / 100.0f);
            double h = Math.toRadians(d.hue);
            float cs = (float) Math.cos(h);
            float sn = (float) Math.sin(h);
            ColorMatrix hm = new ColorMatrix(new float[]{((cs * 0.787f) + 0.213f) - (sn * 0.213f), (0.715f - (cs * 0.715f)) - (sn * 0.715f), (0.072f - (cs * 0.072f)) + (sn * 0.928f), 0.0f, 0.0f, (0.213f - (cs * 0.213f)) + (0.143f * sn), (0.285f * cs) + 0.715f + (0.14f * sn), (0.072f - (cs * 0.072f)) - (0.283f * sn), 0.0f, 0.0f, (0.213f - (cs * 0.213f)) - (0.787f * sn), (0.715f - (cs * 0.715f)) + (0.715f * sn), (0.928f * cs) + 0.072f + (0.072f * sn), 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f});
            cm.postConcat(hm);
            this.colorFilter = new ColorMatrixColorFilter(cm);
        }
        this.bitmapPaint.setColorFilter(this.colorFilter);
    }

    private void begin(Canvas c, Design.Xf xf, float cx, float cy, int W) {
        c.save();
        this.xfActive = false;
        if (xf == null || xf.identity()) {
            return;
        }
        this.xfm.reset();
        this.xfm.postTranslate(-cx, -cy);
        this.xfm.postScale(xf.sx / 100.0f, xf.sy / 100.0f);
        this.xfm.postSkew(xf.skew / 100.0f, 0.0f);
        if (xf.rx != 0 || xf.ry != 0) {
            this.camera.save();
            this.camera.setLocation(0.0f, 0.0f, ((-W) * 1.6f) / 72.0f);
            this.camera.rotateX(xf.rx);
            this.camera.rotateY(xf.ry);
            this.camera.getMatrix(this.camM);
            this.camera.restore();
            this.xfm.postConcat(this.camM);
        }
        this.xfm.postRotate(xf.rot);
        this.xfm.postTranslate(cx, cy);
        c.concat(this.xfm);
        this.xfActive = true;
    }

    private void end(Canvas c) {
        c.restore();
        this.xfActive = false;
    }

    private void addHit(String id, RectF r) {
        if (this.xfActive) {
            this.xfm.mapRect(r);
        }
        this.hits.add(new Hit(id, r));
    }

    private void drawCover(Canvas c, Bitmap b, int W, int H, float margin, float dx, float dy) {
        float s = Math.max((W + (margin * 2.0f)) / b.getWidth(), (H + (margin * 2.0f)) / b.getHeight());
        float tx = ((W - (b.getWidth() * s)) / 2.0f) + dx;
        float ty = ((H - (b.getHeight() * s)) / 2.0f) + dy;
        this.matrix.reset();
        this.matrix.setScale(s, s);
        this.matrix.postTranslate(tx, ty);
        c.drawBitmap(b, this.matrix, this.bitmapPaint);
    }

    private void drawGradient(Canvas c, int W, int H, Design d, long now) {
        double ang = Math.toRadians(d.gradAnim ? (now / 80) % 360 : 60.0d);
        float len = Math.max(W, H) * 0.75f;
        float cx = W / 2.0f;
        float cy = H / 2.0f;
        float dx = ((float) Math.cos(ang)) * len;
        float dy = ((float) Math.sin(ang)) * len;
        this.gradPaint.setShader(new LinearGradient(cx - dx, cy - dy, cx + dx, cy + dy, d.grad1Col(), d.grad2Col(), Shader.TileMode.CLAMP));
        c.drawRect(0.0f, 0.0f, W, H, this.gradPaint);
    }

    private void drawParticles(Canvas c, int W, int H, Design d, long now, boolean live) {
        int i;
        int kind = d.effKind();
        if (kind == 0) {
            return;
        }
        boolean z = true;
        int n = Math.max(1, d.effCount());
        if (this.px == null || this.pkind != kind || this.pn != n || this.pW != W || this.pH != H) {
            this.px = new float[n];
            this.py = new float[n];
            this.pspd = new float[n];
            this.pph = new float[n];
            for (int i2 = 0; i2 < n; i2++) {
                this.px[i2] = this.rnd.nextFloat() * W;
                this.py[i2] = this.rnd.nextFloat() * H;
                this.pspd[i2] = this.rnd.nextFloat() + 0.5f;
                this.pph[i2] = this.rnd.nextFloat() * 6.28f;
            }
            this.pkind = kind;
            this.pn = n;
            this.pW = W;
            this.pH = H;
            this.pLast = now;
        }
        float dt = live ? Math.min(0.1f, Math.max(0.0f, (now - this.pLast) / 1000.0f)) * (d.pSpeed / 100.0f) : 0.0f;
        this.pLast = now;
        float u = (B / 1080.0f) * (d.pSize / 100.0f);
        float su = B / 1080.0f;
        float t = now / 1000.0f;
        float alpha = d.pAlpha / 100.0f;
        this.shapePaint.clearShadowLayer();
        int i3 = 0;
        while (i3 < n) {
            switch (kind) {
                case 1:
                    i = i3;
                    float[] fArr = this.py;
                    fArr[i] = fArr[i] + (((this.pspd[i] * 60.0f) + 40.0f) * su * dt);
                    float[] fArr2 = this.px;
                    fArr2[i] = fArr2[i] + (((float) Math.sin(t + this.pph[i])) * 20.0f * su * dt);
                    wrap(i, W, H, true);
                    this.shapePaint.setStyle(Paint.Style.FILL);
                    this.shapePaint.setColor(tint(d.pColor, -1, 210.0f * alpha));
                    c.drawCircle(this.px[i], this.py[i], ((this.pspd[i] * 3.0f) + 2.0f) * u, this.shapePaint);
                    break;
                case 2:
                    i = i3;
                    float[] fArr3 = this.py;
                    fArr3[i] = fArr3[i] + (((this.pspd[i] * 500.0f) + 900.0f) * su * dt);
                    float[] fArr4 = this.px;
                    fArr4[i] = fArr4[i] - ((120.0f * su) * dt);
                    wrap(i, W, H, true);
                    this.shapePaint.setStyle(Paint.Style.STROKE);
                    this.shapePaint.setStrokeWidth(u * 2.0f);
                    this.shapePaint.setColor(tint(d.pColor, -4268801, 140.0f * alpha));
                    c.drawLine(this.px[i], this.py[i], this.px[i] - (6.0f * su), this.py[i] - ((40.0f * u) * this.pspd[i]), this.shapePaint);
                    break;
                case 3:
                    i = i3;
                    float a = (((float) Math.sin((this.pspd[i] * t * 1.6f) + this.pph[i])) * 0.5f) + 0.5f;
                    this.shapePaint.setStyle(Paint.Style.FILL);
                    this.shapePaint.setColor(tint(d.pColor, -16, ((195.0f * a) + 60.0f) * alpha));
                    c.drawCircle(this.px[i], this.py[i], ((this.pspd[i] * 2.0f) + 1.0f) * u, this.shapePaint);
                    break;
                case 4:
                default:
                    i = i3;
                    float[] fArr5 = this.py;
                    fArr5[i] = fArr5[i] - ((((this.pspd[i] * 50.0f) + 30.0f) * su) * dt);
                    float[] fArr6 = this.px;
                    fArr6[i] = fArr6[i] + (((float) Math.sin(t + this.pph[i])) * 15.0f * su * dt);
                    wrap(i, W, H, false);
                    this.shapePaint.setStyle(Paint.Style.STROKE);
                    this.shapePaint.setStrokeWidth(u * 2.0f);
                    this.shapePaint.setColor(tint(d.pColor, -1, 150.0f * alpha));
                    c.drawCircle(this.px[i], this.py[i], ((this.pspd[i] * 14.0f) + 6.0f) * u, this.shapePaint);
                    break;
                case 5:
                    float[] fArr7 = this.py;
                    fArr7[i3] = fArr7[i3] + (((this.pspd[i3] * 50.0f) + 50.0f) * su * dt);
                    float[] fArr8 = this.px;
                    fArr8[i3] = fArr8[i3] + (((float) Math.sin((0.8f * t) + this.pph[i3])) * 40.0f * su * dt) + (15.0f * su * dt);
                    wrap(i3, W, H, z);
                    this.shapePaint.setStyle(Paint.Style.FILL);
                    this.shapePaint.setColor(tint(d.pColor, -18491, 220.0f * alpha));
                    c.save();
                    c.rotate((((40.0f * t) * this.pspd[i3]) + (this.pph[i3] * 57.0f)) % 360.0f, this.px[i3], this.py[i3]);
                    i = i3;
                    c.drawOval(this.px[i3] - ((u * 9.0f) * this.pspd[i3]), this.py[i3] - ((u * 5.0f) * this.pspd[i3]), this.px[i3] + (9.0f * u * this.pspd[i3]), this.py[i3] + (5.0f * u * this.pspd[i3]), this.shapePaint);
                    c.restore();
                    break;
            }
            i3 = i + 1;
            z = true;
        }
    }

    private int tint(int custom, int def, float alpha) {
        int col = custom != 0 ? custom : def;
        return Color.argb(Math.max(0, Math.min(255, Math.round(alpha))), Color.red(col), Color.green(col), Color.blue(col));
    }

    private void wrap(int i, int W, int H, boolean down) {
        if (down && this.py[i] > H + 20) {
            this.py[i] = -20.0f;
            this.px[i] = this.rnd.nextFloat() * W;
        } else if (!down && this.py[i] < -30.0f) {
            this.py[i] = H + 30;
            this.px[i] = this.rnd.nextFloat() * W;
        }
        if (this.px[i] < -30.0f) {
            this.px[i] = W + 10;
        }
        if (this.px[i] > W + 30) {
            this.px[i] = -10.0f;
        }
    }

    private void drawClock(Canvas c, int W, int H, Design d, Date date, float dx, float dy) {
        float cx = (W * d.clockX) + dx;
        float cy = (H * d.clockY) + dy;
        begin(c, d.clockXf, cx, cy, W);
        if (d.clockStyle == 2) {
            drawAnalog(c, W, cx, cy, d, date);
        } else if (d.clockStyle == 0) {
            drawStacked(c, W, cx, cy, d, date);
        } else {
            drawLine(c, W, cx, cy, d, date);
        }
        end(c);
    }

    private void prepareClockPaint(Design d, float size) {
        this.clockPaint.setTypeface(Fonts.get(d.font));
        this.clockPaint.setLetterSpacing(d.letterSpacing / 100.0f);
        this.clockPaint.setTextSize(size);
    }

    private void drawStacked(Canvas c, int W, float cx, float cy, Design d, Date date) {
        float S = B * 0.0042f * d.clockSize;
        prepareClockPaint(d, S);
        String hh = this.fH.format(date);
        String mm = this.fM.format(date);
        float wide = Math.max(this.clockPaint.measureText(hh), this.clockPaint.measureText(mm));
        if (wide > W * 0.92f) {
            S *= (W * 0.92f) / wide;
        }
        float S2 = S;
        this.clockPaint.setTextSize(S2);
        float wide2 = Math.max(this.clockPaint.measureText(hh), this.clockPaint.measureText(mm));
        this.clockPaint.setTextAlign(Paint.Align.CENTER);
        float gap = (d.lineGap / 100.0f) * S2;
        RectF block = new RectF(cx - (wide2 / 2.0f), (cy - gap) - (S2 * 0.4f), cx + (wide2 / 2.0f), cy + gap + (0.4f * S2));
        int col2 = d.twoTone ? d.clockCol2() : d.clockCol();
        glyphs(c, hh, cx, cy - gap, d.clockCol(), d, block);
        glyphs(c, mm, cx, cy + gap, col2, d, block);
        addHit("clock", new RectF(block));
    }

    private void drawLine(Canvas c, int W, float cx, float cy, Design d, Date date) {
        float S = B * 0.0028f * d.clockSize;
        prepareClockPaint(d, S);
        String a = this.fH.format(date);
        String b = this.fM.format(date);
        String sep = Design.SEPARATORS[d.separator];
        float line = this.clockPaint.measureText(a + sep + b);
        if (line > W * 0.92f) {
            S *= (W * 0.92f) / line;
        }
        float S2 = S;
        this.clockPaint.setTextSize(S2);
        this.clockPaint.setTextAlign(Paint.Align.LEFT);
        float wa = this.clockPaint.measureText(a);
        float ws = this.clockPaint.measureText(sep);
        float wb = this.clockPaint.measureText(b);
        float left = cx - (((wa + ws) + wb) / 2.0f);
        RectF block = new RectF(left, cy - (S2 * 0.55f), left + wa + ws + wb, cy + (0.55f * S2));
        int col2 = d.twoTone ? d.clockCol2() : d.clockCol();
        glyphs(c, a, left, cy, d.clockCol(), d, block);
        if (!sep.isEmpty()) {
            glyphs(c, sep, left + wa, cy, d.clockCol(), d, block);
        }
        glyphs(c, b, left + wa + ws, cy, col2, d, block);
        this.clockPaint.setTextAlign(Paint.Align.CENTER);
        addHit("clock", new RectF(block));
    }

    private void glyphs(Canvas c, String s, float x, float centerY, int color, Design d, RectF block) {
        Paint.FontMetrics fm = this.clockPaint.getFontMetrics();
        float base = centerY - ((fm.ascent + fm.descent) / 2.0f);
        float S = this.clockPaint.getTextSize();
        this.clockPaint.setShader(null);
        this.clockPaint.setStrokeJoin(Paint.Join.ROUND);
        if (d.outline) {
            this.clockPaint.setStyle(Paint.Style.STROKE);
            this.clockPaint.setStrokeWidth(Math.max(2.0f, 0.03f * S));
            this.clockPaint.clearShadowLayer();
            this.clockPaint.setColor(color);
            c.drawText(s, x, base, this.clockPaint);
            return;
        }
        if (d.stroke) {
            this.clockPaint.setStyle(Paint.Style.STROKE);
            this.clockPaint.setStrokeWidth(Math.max(1.5f, (d.strokeW * S) / 150.0f));
            this.clockPaint.clearShadowLayer();
            this.clockPaint.setColor(d.strokeColor);
            c.drawText(s, x, base, this.clockPaint);
        }
        this.clockPaint.setStyle(Paint.Style.FILL);
        if (d.shadow) {
            this.clockPaint.setShadowLayer(0.03f * S, 0.0f, 0.01f * S, -1728053248);
        } else {
            this.clockPaint.clearShadowLayer();
        }
        if (d.gradText) {
            double a = Math.toRadians(d.gradAngle);
            float r = ((float) Math.hypot(block.width(), block.height())) / 2.0f;
            float mx = block.centerX();
            float my = block.centerY();
            this.clockPaint.setShader(new LinearGradient(mx - (((float) Math.cos(a)) * r), my - (((float) Math.sin(a)) * r), mx + (((float) Math.cos(a)) * r), my + (((float) Math.sin(a)) * r), d.clockCol(), d.clockCol2(), Shader.TileMode.CLAMP));
        } else {
            this.clockPaint.setColor(color);
        }
        c.drawText(s, x, base, this.clockPaint);
        this.clockPaint.setShader(null);
    }

    private void drawAnalog(Canvas c, int W, float cx, float cy, Design d, Date date) {
        float R = B * 0.0022f * d.clockSize;
        this.cal.setTime(date);
        float hour = (this.cal.get(10) + (this.cal.get(12) / 60.0f)) * 30.0f;
        float min = this.cal.get(12) * 6.0f;
        int col2 = d.twoTone ? d.clockCol2() : d.clockCol();
        if (d.shadow) {
            this.shapePaint.setShadowLayer(R * 0.03f, 0.0f, 0.01f * R, -1728053248);
        } else {
            this.shapePaint.clearShadowLayer();
        }
        this.shapePaint.setStyle(Paint.Style.STROKE);
        this.shapePaint.setStrokeCap(Paint.Cap.ROUND);
        this.shapePaint.setColor(d.clockCol());
        this.shapePaint.setStrokeWidth(0.03f * R);
        c.drawCircle(cx, cy, R, this.shapePaint);
        int i = 0;
        while (i < 12) {
            float a = (float) Math.toRadians(i * 30);
            float len = (i % 3 == 0 ? 0.12f : 0.06f) * R;
            this.shapePaint.setStrokeWidth((i % 3 == 0 ? 0.035f : 0.02f) * R);
            c.drawLine(cx + (((float) Math.sin(a)) * (R - len)), cy - (((float) Math.cos(a)) * (R - len)), cx + (((float) Math.sin(a)) * R * 0.96f), cy - (((float) Math.cos(a)) * (0.96f * R)), this.shapePaint);
            i++;
            col2 = col2;
        }
        int col22 = col2;
        this.shapePaint.setColor(d.clockCol());
        this.shapePaint.setStrokeWidth(0.07f * R);
        hand(c, cx, cy, hour, R * 0.5f);
        this.shapePaint.setColor(col22);
        this.shapePaint.setStrokeWidth(0.045f * R);
        hand(c, cx, cy, min, R * 0.78f);
        this.shapePaint.setStyle(Paint.Style.FILL);
        this.shapePaint.setColor(col22);
        c.drawCircle(cx, cy, 0.05f * R, this.shapePaint);
        this.shapePaint.clearShadowLayer();
        this.shapePaint.setStrokeCap(Paint.Cap.BUTT);
        addHit("clock", new RectF(cx - R, cy - R, cx + R, cy + R));
    }

    private void hand(Canvas c, float cx, float cy, float deg, float len) {
        float a = (float) Math.toRadians(deg);
        c.drawLine(cx, cy, cx + (((float) Math.sin(a)) * len), cy - (((float) Math.cos(a)) * len), this.shapePaint);
    }

    private void drawElements(Canvas c, int W, int H, Design d, Date date, String webText, String battery, float dx, float dy) {
        int blockW;
        String text;
        StaticLayout sl;
        this.textPaint.setTextAlign(Paint.Align.CENTER);
        this.textPaint.setStyle(Paint.Style.FILL);
        this.textPaint.setLetterSpacing(0.0f);
        if (d.dateShow) {
            this.textPaint.setTypeface(Fonts.get(d.dateFont));
            this.textPaint.setColor(d.textCol());
            this.textPaint.setShadowLayer(B * 0.006f, 0.0f, B * 0.003f, -1728053248);
            this.textPaint.setTextSize(((B * 0.05f) * d.dateSize) / 100.0f);
            String s = this.fDate.format(date);
            if (!s.isEmpty()) {
                s = s.substring(0, 1).toUpperCase(Locale.getDefault()) + s.substring(1);
            }
            drawLabel(c, "date", s, (W * d.dateX) + dx, (H * d.dateY) + dy, d.dateXf, W);
        }
        if (d.batShow && battery != null && !battery.isEmpty()) {
            this.textPaint.setTypeface(Fonts.get(d.dateFont));
            this.textPaint.setColor(d.textCol());
            this.textPaint.setShadowLayer(B * 0.006f, 0.0f, B * 0.003f, -1728053248);
            this.textPaint.setTextSize(((B * 0.04f) * d.batSize) / 100.0f);
            drawLabel(c, "bat", battery, (W * d.batX) + dx, (H * d.batY) + dy, d.batXf, W);
        }
        int blockW2 = (int) (W * 0.8f);
        int i = 0;
        while (i < d.layers.size()) {
            Design.Layer l = d.layers.get(i);
            String text2 = l.web ? (l.url.trim().isEmpty() || webText == null) ? "" : webText : l.text.trim();
            if (text2.isEmpty()) {
                blockW = blockW2;
            } else {
                float S = ((B * 0.045f) * l.size) / 100.0f;
                int color = l.color != 0 ? l.color : d.textCol();
                String id = "tl" + i;
                String key = text2 + "|" + blockW2 + "|" + S + "|" + color + "|" + l.font.id + "|" + l.font.variation();
                StaticLayout sl2 = this.layouts.get(id);
                if (sl2 == null || !key.equals(this.layoutKeys.get(id))) {
                    blockW = blockW2;
                    text = id;
                    StaticLayout sl3 = makeLayout(text2, blockW2, S, Fonts.get(l.font), color, W);
                    this.layouts.put(text, sl3);
                    this.layoutKeys.put(text, key);
                    sl = sl3;
                } else {
                    blockW = blockW2;
                    sl = sl2;
                    text = id;
                }
                float cx = (W * l.x) + dx;
                float cy = (H * l.y) + dy;
                begin(c, l.xf, cx, cy, W);
                float left = cx - (sl.getWidth() / 2.0f);
                float top = cy - (sl.getHeight() / 2.0f);
                c.save();
                c.translate(left, top);
                sl.draw(c);
                c.restore();
                addHit(text, new RectF(left, top, sl.getWidth() + left, sl.getHeight() + top));
                end(c);
            }
            i++;
            blockW2 = blockW;
        }
        this.textPaint.clearShadowLayer();
        this.textPaint.setTypeface(Typeface.DEFAULT);
        this.textPaint.setColor(-1);
        for (int i2 = 0; i2 < d.stickers.size(); i2++) {
            Design.Sticker s2 = d.stickers.get(i2);
            float S2 = ((B * 0.14f) * s2.size) / 100.0f;
            this.textPaint.setTextSize(S2);
            float cx2 = W * s2.x;
            float cy2 = H * s2.y;
            begin(c, s2.xf, cx2, cy2, W);
            drawCentered(c, s2.s, cx2, cy2, this.textPaint);
            float half = 0.6f * S2;
            addHit("st" + i2, new RectF(cx2 - half, cy2 - half, cx2 + half, cy2 + half));
            end(c);
        }
    }

    private void drawLabel(Canvas c, String id, String text, float cx, float cy, Design.Xf xf, int W) {
        begin(c, xf, cx, cy, W);
        drawCentered(c, text, cx, cy, this.textPaint);
        float w = this.textPaint.measureText(text);
        float h = this.textPaint.getTextSize();
        addHit(id, new RectF(cx - (w / 2.0f), cy - (h * 0.6f), (w / 2.0f) + cx, (0.6f * h) + cy));
        end(c);
    }

    private StaticLayout makeLayout(String text, int width, float size, Typeface tf, int color, int W) {
        TextPaint p = new TextPaint(1);
        p.setTypeface(tf);
        p.setTextSize(size);
        p.setColor(color);
        p.setShadowLayer(W * 0.006f, 0.0f, W * 0.003f, -1728053248);
        return StaticLayout.Builder.obtain(text, 0, text.length(), p, width).setAlignment(Layout.Alignment.ALIGN_CENTER).setMaxLines(6).setEllipsize(TextUtils.TruncateAt.END).build();
    }

    private void drawCentered(Canvas c, String text, float cx, float centerY, Paint p) {
        Paint.FontMetrics fm = p.getFontMetrics();
        c.drawText(text, cx, centerY - ((fm.ascent + fm.descent) / 2.0f), p);
    }
}
