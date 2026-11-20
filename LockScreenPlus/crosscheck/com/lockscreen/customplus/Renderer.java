/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.graphics.Bitmap
 *  android.graphics.Camera
 *  android.graphics.Canvas
 *  android.graphics.Color
 *  android.graphics.ColorFilter
 *  android.graphics.ColorMatrix
 *  android.graphics.ColorMatrixColorFilter
 *  android.graphics.LinearGradient
 *  android.graphics.Matrix
 *  android.graphics.Paint
 *  android.graphics.Paint$Align
 *  android.graphics.Paint$Cap
 *  android.graphics.Paint$FontMetrics
 *  android.graphics.Paint$Join
 *  android.graphics.Paint$Style
 *  android.graphics.RectF
 *  android.graphics.Shader
 *  android.graphics.Shader$TileMode
 *  android.graphics.Typeface
 *  android.text.Layout$Alignment
 *  android.text.StaticLayout
 *  android.text.StaticLayout$Builder
 *  android.text.TextPaint
 *  android.text.TextUtils$TruncateAt
 */
package com.lockscreen.customplus;

import android.graphics.Bitmap;
import android.graphics.Camera;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.ColorFilter;
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
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.Fonts;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Random;

final class Renderer {
    private final Paint bitmapPaint;
    private final Calendar cal;
    private final Matrix camM;
    private final Camera camera;
    private final Paint clockPaint;
    private int cmHue;
    private int cmSat;
    private ColorMatrixColorFilter colorFilter;
    private SimpleDateFormat fDate;
    private SimpleDateFormat fH;
    private SimpleDateFormat fM;
    private boolean fmt24;
    private int fmtDateIdx = -1;
    private Locale fmtLocale;
    private final Paint gradPaint;
    final List<Hit> hits = new ArrayList<Hit>();
    private final Map<String, String> layoutKeys;
    private final Map<String, StaticLayout> layouts;
    private final Matrix matrix;
    private int pH;
    private long pLast;
    private int pW;
    private int pkind;
    private int pn;
    private float[] pph;
    private float[] pspd;
    private float[] px;
    private float[] py;
    private final Random rnd;
    private final Paint shapePaint;
    private final TextPaint textPaint;
    private boolean xfActive;
    private final Matrix xfm;

    Renderer() {
        this.bitmapPaint = new Paint(2);
        this.clockPaint = new Paint(1);
        this.textPaint = new TextPaint(1);
        this.shapePaint = new Paint(1);
        this.gradPaint = new Paint();
        this.matrix = new Matrix();
        this.xfm = new Matrix();
        this.camM = new Matrix();
        this.camera = new Camera();
        this.cal = Calendar.getInstance();
        this.cmHue = Integer.MIN_VALUE;
        this.cmSat = Integer.MIN_VALUE;
        this.rnd = new Random(7L);
        this.layouts = new HashMap<String, StaticLayout>();
        this.layoutKeys = new HashMap<String, String>();
    }

    private void addHit(String string2, RectF rectF) {
        if (this.xfActive) {
            this.xfm.mapRect(rectF);
        }
        this.hits.add(new Hit(string2, rectF));
    }

    private void begin(Canvas canvas, Design.Xf xf, float f, float f2, int n) {
        canvas.save();
        this.xfActive = false;
        if (xf != null && !xf.identity()) {
            this.xfm.reset();
            this.xfm.postTranslate(-f, -f2);
            this.xfm.postScale((float)xf.sx / 100.0f, (float)xf.sy / 100.0f);
            this.xfm.postSkew((float)xf.skew / 100.0f, 0.0f);
            if (xf.rx != 0 || xf.ry != 0) {
                this.camera.save();
                this.camera.setLocation(0.0f, 0.0f, (float)(-n) * 1.6f / 72.0f);
                this.camera.rotateX((float)xf.rx);
                this.camera.rotateY((float)xf.ry);
                this.camera.getMatrix(this.camM);
                this.camera.restore();
                this.xfm.postConcat(this.camM);
            }
            this.xfm.postRotate((float)xf.rot);
            this.xfm.postTranslate(f, f2);
            canvas.concat(this.xfm);
            this.xfActive = true;
            return;
        }
    }

    private void drawAnalog(Canvas canvas, int n, float f, float f2, Design design, Date date) {
        float f3 = (float)n * 0.0022f * (float)design.clockSize;
        this.cal.setTime(date);
        float f4 = this.cal.get(10);
        float f5 = (float)this.cal.get(12) / 60.0f;
        float f6 = this.cal.get(12);
        n = design.twoTone ? design.clockCol2() : design.clockCol();
        if (design.shadow) {
            this.shapePaint.setShadowLayer(f3 * 0.03f, 0.0f, 0.01f * f3, -1728053248);
        } else {
            this.shapePaint.clearShadowLayer();
        }
        this.shapePaint.setStyle(Paint.Style.STROKE);
        this.shapePaint.setStrokeCap(Paint.Cap.ROUND);
        this.shapePaint.setColor(design.clockCol());
        this.shapePaint.setStrokeWidth(0.03f * f3);
        canvas.drawCircle(f, f2, f3, this.shapePaint);
        for (int i = 0; i < 12; ++i) {
            float f7 = (float)Math.toRadians(i * 30);
            float f8 = i % 3 == 0 ? 0.12f : 0.06f;
            float f9 = f8 * f3;
            date = this.shapePaint;
            f8 = i % 3 == 0 ? 0.035f : 0.02f;
            date.setStrokeWidth(f8 * f3);
            canvas.drawLine(f + (float)Math.sin(f7) * (f3 - f9), f2 - (float)Math.cos(f7) * (f3 - f9), f + (float)Math.sin(f7) * (f3 * 0.96f), f2 - (float)Math.cos(f7) * (0.96f * f3), this.shapePaint);
        }
        this.shapePaint.setColor(design.clockCol());
        this.shapePaint.setStrokeWidth(0.07f * f3);
        this.hand(canvas, f, f2, (f4 + f5) * 30.0f, f3 * 0.5f);
        this.shapePaint.setColor(n);
        this.shapePaint.setStrokeWidth(0.045f * f3);
        this.hand(canvas, f, f2, f6 * 6.0f, f3 * 0.78f);
        this.shapePaint.setStyle(Paint.Style.FILL);
        this.shapePaint.setColor(n);
        canvas.drawCircle(f, f2, 0.05f * f3, this.shapePaint);
        this.shapePaint.clearShadowLayer();
        this.shapePaint.setStrokeCap(Paint.Cap.BUTT);
        this.addHit("clock", new RectF(f - f3, f2 - f3, f + f3, f2 + f3));
    }

    private void drawCentered(Canvas canvas, String string2, float f, float f2, Paint paint) {
        Paint.FontMetrics fontMetrics = paint.getFontMetrics();
        canvas.drawText(string2, f, f2 - (fontMetrics.ascent + fontMetrics.descent) / 2.0f, paint);
    }

    private void drawClock(Canvas canvas, int n, int n2, Design design, Date date, float f, float f2) {
        f = (float)n * design.clockX + f;
        f2 = (float)n2 * design.clockY + f2;
        this.begin(canvas, design.clockXf, f, f2, n);
        if (design.clockStyle == 2) {
            this.drawAnalog(canvas, n, f, f2, design, date);
        } else if (design.clockStyle == 0) {
            this.drawStacked(canvas, n, f, f2, design, date);
        } else {
            this.drawLine(canvas, n, f, f2, design, date);
        }
        this.end(canvas);
    }

    private void drawCover(Canvas canvas, Bitmap bitmap, int n, int n2, float f, float f2, float f3) {
        float f4 = Math.max(((float)n + f * 2.0f) / (float)bitmap.getWidth(), ((float)n2 + f * 2.0f) / (float)bitmap.getHeight());
        float f5 = ((float)n - (float)bitmap.getWidth() * f4) / 2.0f;
        f = ((float)n2 - (float)bitmap.getHeight() * f4) / 2.0f;
        this.matrix.reset();
        this.matrix.setScale(f4, f4);
        this.matrix.postTranslate(f5 + f2, f + f3);
        canvas.drawBitmap(bitmap, this.matrix, this.bitmapPaint);
    }

    private void drawElements(Canvas canvas, int n, int n2, Design design, Date object, String string2, String string3, float f, float f2) {
        int n3;
        Object object2;
        float f3;
        this.textPaint.setTextAlign(Paint.Align.CENTER);
        this.textPaint.setStyle(Paint.Style.FILL);
        this.textPaint.setLetterSpacing(0.0f);
        if (design.dateShow) {
            f3 = (float)n * 0.05f * (float)design.dateSize / 100.0f;
            this.textPaint.setTypeface(Fonts.get(design.dateFont));
            this.textPaint.setColor(design.textCol());
            this.textPaint.setShadowLayer((float)n * 0.006f, 0.0f, (float)n * 0.003f, -1728053248);
            this.textPaint.setTextSize(f3);
            object = object2 = this.fDate.format((Date)object);
            if (!((String)object2).isEmpty()) {
                object = ((String)object2).substring(0, 1).toUpperCase(Locale.getDefault()) + ((String)object2).substring(1);
            }
            this.drawLabel(canvas, "date", (String)object, (float)n * design.dateX + f, (float)n2 * design.dateY + f2, design.dateXf, n);
        }
        if (design.batShow && string3 != null && !string3.isEmpty()) {
            this.textPaint.setTypeface(Fonts.get(design.dateFont));
            this.textPaint.setColor(design.textCol());
            this.textPaint.setShadowLayer((float)n * 0.006f, 0.0f, (float)n * 0.003f, -1728053248);
            this.textPaint.setTextSize((float)n * 0.04f * (float)design.batSize / 100.0f);
            this.drawLabel(canvas, "bat", string3, (float)n * design.batX + f, (float)n2 * design.batY + f2, design.batXf, n);
        }
        int n4 = (int)((float)n * 0.8f);
        for (n3 = 0; n3 < design.layers.size(); ++n3) {
            object2 = design.layers.get(n3);
            object = ((Design.Layer)object2).web ? (!((Design.Layer)object2).url.trim().isEmpty() && string2 != null ? string2 : "") : ((Design.Layer)object2).text.trim();
            if (((String)object).isEmpty()) continue;
            f3 = (float)n * 0.045f * (float)((Design.Layer)object2).size / 100.0f;
            int n5 = ((Design.Layer)object2).color != 0 ? ((Design.Layer)object2).color : design.textCol();
            String string4 = "tl" + n3;
            String string5 = (String)object + "|" + n4 + "|" + f3 + "|" + n5 + "|" + ((Design.Layer)object2).font.id + "|" + ((Design.Layer)object2).font.variation();
            string3 = this.layouts.get(string4);
            if (string3 != null && string5.equals(this.layoutKeys.get(string4))) {
                object = string3;
            } else {
                string3 = Fonts.get(((Design.Layer)object2).font);
                object = this.makeLayout((String)object, n4, f3, (Typeface)string3, n5, n);
                this.layouts.put(string4, (StaticLayout)object);
                this.layoutKeys.put(string4, string5);
            }
            f3 = (float)n * ((Design.Layer)object2).x + f;
            float f4 = (float)n2 * ((Design.Layer)object2).y + f2;
            this.begin(canvas, ((Design.Layer)object2).xf, f3, f4, n);
            canvas.save();
            canvas.translate(f3 -= (float)object.getWidth() / 2.0f, f4 -= (float)object.getHeight() / 2.0f);
            object.draw(canvas);
            canvas.restore();
            this.addHit(string4, new RectF(f3, f4, (float)object.getWidth() + f3, (float)object.getHeight() + f4));
            this.end(canvas);
        }
        this.textPaint.clearShadowLayer();
        this.textPaint.setTypeface(Typeface.DEFAULT);
        this.textPaint.setColor(-1);
        for (n3 = 0; n3 < design.stickers.size(); ++n3) {
            object = design.stickers.get(n3);
            f3 = (float)n * 0.14f * (float)((Design.Sticker)object).size / 100.0f;
            this.textPaint.setTextSize(f3);
            f2 = (float)n * ((Design.Sticker)object).x;
            f = (float)n2 * ((Design.Sticker)object).y;
            this.begin(canvas, ((Design.Sticker)object).xf, f2, f, n);
            this.drawCentered(canvas, ((Design.Sticker)object).s, f2, f, (Paint)this.textPaint);
            f3 = 0.6f * f3;
            this.addHit("st" + n3, new RectF(f2 - f3, f - f3, f2 + f3, f + f3));
            this.end(canvas);
        }
    }

    private void drawGradient(Canvas canvas, int n, int n2, Design design, long l) {
        double d = design.gradAnim ? (double)(l / 80L % 360L) : 60.0;
        d = Math.toRadians(d);
        float f = (float)Math.max(n, n2) * 0.75f;
        float f2 = (float)n / 2.0f;
        float f3 = (float)n2 / 2.0f;
        float f4 = (float)Math.cos(d) * f;
        f = (float)Math.sin(d) * f;
        this.gradPaint.setShader((Shader)new LinearGradient(f2 - f4, f3 - f, f2 + f4, f3 + f, design.grad1Col(), design.grad2Col(), Shader.TileMode.CLAMP));
        canvas.drawRect(0.0f, 0.0f, (float)n, (float)n2, this.gradPaint);
    }

    private void drawLabel(Canvas canvas, String string2, String string3, float f, float f2, Design.Xf xf, int n) {
        this.begin(canvas, xf, f, f2, n);
        this.drawCentered(canvas, string3, f, f2, (Paint)this.textPaint);
        float f3 = this.textPaint.measureText(string3);
        float f4 = this.textPaint.getTextSize();
        this.addHit(string2, new RectF(f - f3 / 2.0f, f2 - f4 * 0.6f, f3 / 2.0f + f, 0.6f * f4 + f2));
        this.end(canvas);
    }

    private void drawLine(Canvas canvas, int n, float f, float f2, Design design, Date date) {
        float f3 = (float)n * 0.0028f * (float)design.clockSize;
        this.prepareClockPaint(design, f3);
        String string2 = this.fH.format(date);
        String string3 = this.fM.format(date);
        String string4 = Design.SEPARATORS[design.separator];
        float f4 = this.clockPaint.measureText(string2 + string4 + string3);
        float f5 = f3;
        if (f4 > (float)n * 0.92f) {
            f5 = f3 * ((float)n * 0.92f / f4);
        }
        this.clockPaint.setTextSize(f5);
        this.clockPaint.setTextAlign(Paint.Align.LEFT);
        float f6 = this.clockPaint.measureText(string2);
        f4 = this.clockPaint.measureText(string4);
        f3 = this.clockPaint.measureText(string3);
        date = new RectF(f -= (f6 + f4 + f3) / 2.0f, f2 - f5 * 0.55f, f + f6 + f4 + f3, f2 + 0.55f * f5);
        n = design.twoTone ? design.clockCol2() : design.clockCol();
        this.glyphs(canvas, string2, f, f2, design.clockCol(), design, (RectF)date);
        if (!string4.isEmpty()) {
            this.glyphs(canvas, string4, f + f6, f2, design.clockCol(), design, (RectF)date);
        }
        this.glyphs(canvas, string3, f + f6 + f4, f2, n, design, (RectF)date);
        this.clockPaint.setTextAlign(Paint.Align.CENTER);
        this.addHit("clock", new RectF((RectF)date));
    }

    private void drawParticles(Canvas canvas, int n, int n2, Design design, long l, boolean bl) {
        int n3;
        int n4 = design.effKind();
        if (n4 == 0) {
            return;
        }
        int n5 = Math.max(1, design.effCount());
        if (this.px == null || this.pkind != n4 || this.pn != n5 || this.pW != n || this.pH != n2) {
            this.px = new float[n5];
            this.py = new float[n5];
            this.pspd = new float[n5];
            this.pph = new float[n5];
            for (n3 = 0; n3 < n5; ++n3) {
                this.px[n3] = this.rnd.nextFloat() * (float)n;
                this.py[n3] = this.rnd.nextFloat() * (float)n2;
                this.pspd[n3] = this.rnd.nextFloat() + 0.5f;
                this.pph[n3] = this.rnd.nextFloat() * 6.28f;
            }
            this.pkind = n4;
            this.pn = n5;
            this.pW = n;
            this.pH = n2;
            this.pLast = l;
        }
        float f = 0.0f;
        if (bl) {
            f = Math.min(0.1f, Math.max(0.0f, (float)(l - this.pLast) / 1000.0f)) * ((float)design.pSpeed / 100.0f);
        }
        this.pLast = l;
        float f2 = (float)n / 1080.0f * ((float)design.pSize / 100.0f);
        float f3 = (float)n / 1080.0f;
        float f4 = (float)l / 1000.0f;
        float f5 = (float)design.pAlpha / 100.0f;
        this.shapePaint.clearShadowLayer();
        block7: for (n3 = 0; n3 < n5; ++n3) {
            switch (n4) {
                default: {
                    float[] fArray = this.py;
                    fArray[n3] = fArray[n3] - (this.pspd[n3] * 50.0f + 30.0f) * f3 * f;
                    fArray = this.px;
                    fArray[n3] = fArray[n3] + (float)Math.sin(f4 + this.pph[n3]) * 15.0f * f3 * f;
                    this.wrap(n3, n, n2, false);
                    this.shapePaint.setStyle(Paint.Style.STROKE);
                    this.shapePaint.setStrokeWidth(f2 * 2.0f);
                    this.shapePaint.setColor(this.tint(design.pColor, -1, 150.0f * f5));
                    canvas.drawCircle(this.px[n3], this.py[n3], (this.pspd[n3] * 14.0f + 6.0f) * f2, this.shapePaint);
                    continue block7;
                }
                case 5: {
                    float[] fArray = this.py;
                    fArray[n3] = fArray[n3] + (this.pspd[n3] * 50.0f + 50.0f) * f3 * f;
                    fArray = this.px;
                    fArray[n3] = fArray[n3] + ((float)Math.sin(0.8f * f4 + this.pph[n3]) * 40.0f * f3 * f + 15.0f * f3 * f);
                    this.wrap(n3, n, n2, true);
                    this.shapePaint.setStyle(Paint.Style.FILL);
                    this.shapePaint.setColor(this.tint(design.pColor, -18491, 220.0f * f5));
                    canvas.save();
                    canvas.rotate((40.0f * f4 * this.pspd[n3] + this.pph[n3] * 57.0f) % 360.0f, this.px[n3], this.py[n3]);
                    canvas.drawOval(this.px[n3] - f2 * 9.0f * this.pspd[n3], this.py[n3] - f2 * 5.0f * this.pspd[n3], this.px[n3] + 9.0f * f2 * this.pspd[n3], this.py[n3] + 5.0f * f2 * this.pspd[n3], this.shapePaint);
                    canvas.restore();
                    continue block7;
                }
                case 3: {
                    float f6 = (float)Math.sin(this.pspd[n3] * f4 * 1.6f + this.pph[n3]);
                    this.shapePaint.setStyle(Paint.Style.FILL);
                    this.shapePaint.setColor(this.tint(design.pColor, -16, (195.0f * (f6 * 0.5f + 0.5f) + 60.0f) * f5));
                    canvas.drawCircle(this.px[n3], this.py[n3], (this.pspd[n3] * 2.0f + 1.0f) * f2, this.shapePaint);
                    continue block7;
                }
                case 2: {
                    float[] fArray = this.py;
                    fArray[n3] = fArray[n3] + (this.pspd[n3] * 500.0f + 900.0f) * f3 * f;
                    fArray = this.px;
                    fArray[n3] = fArray[n3] - 120.0f * f3 * f;
                    this.wrap(n3, n, n2, true);
                    this.shapePaint.setStyle(Paint.Style.STROKE);
                    this.shapePaint.setStrokeWidth(f2 * 2.0f);
                    this.shapePaint.setColor(this.tint(design.pColor, -4268801, 140.0f * f5));
                    canvas.drawLine(this.px[n3], this.py[n3], this.px[n3] - 6.0f * f3, this.py[n3] - 40.0f * f2 * this.pspd[n3], this.shapePaint);
                    continue block7;
                }
                case 1: {
                    float[] fArray = this.py;
                    fArray[n3] = fArray[n3] + (this.pspd[n3] * 60.0f + 40.0f) * f3 * f;
                    fArray = this.px;
                    fArray[n3] = fArray[n3] + (float)Math.sin(f4 + this.pph[n3]) * 20.0f * f3 * f;
                    this.wrap(n3, n, n2, true);
                    this.shapePaint.setStyle(Paint.Style.FILL);
                    this.shapePaint.setColor(this.tint(design.pColor, -1, 210.0f * f5));
                    canvas.drawCircle(this.px[n3], this.py[n3], (this.pspd[n3] * 3.0f + 2.0f) * f2, this.shapePaint);
                }
            }
        }
    }

    private void drawStacked(Canvas canvas, int n, float f, float f2, Design design, Date date) {
        float f3 = (float)n * 0.0042f * (float)design.clockSize;
        this.prepareClockPaint(design, f3);
        String string2 = this.fH.format(date);
        String string3 = this.fM.format(date);
        float f4 = Math.max(this.clockPaint.measureText(string2), this.clockPaint.measureText(string3));
        float f5 = f3;
        if (f4 > (float)n * 0.92f) {
            f5 = f3 * ((float)n * 0.92f / f4);
        }
        this.clockPaint.setTextSize(f5);
        f3 = Math.max(this.clockPaint.measureText(string2), this.clockPaint.measureText(string3));
        this.clockPaint.setTextAlign(Paint.Align.CENTER);
        f4 = (float)design.lineGap / 100.0f * f5;
        date = new RectF(f - f3 / 2.0f, f2 - f4 - f5 * 0.4f, f + f3 / 2.0f, f2 + f4 + 0.4f * f5);
        n = design.twoTone ? design.clockCol2() : design.clockCol();
        this.glyphs(canvas, string2, f, f2 - f4, design.clockCol(), design, (RectF)date);
        this.glyphs(canvas, string3, f, f2 + f4, n, design, (RectF)date);
        this.addHit("clock", new RectF((RectF)date));
    }

    private void end(Canvas canvas) {
        canvas.restore();
        this.xfActive = false;
    }

    private void glyphs(Canvas canvas, String string2, float f, float f2, int n, Design design, RectF rectF) {
        Paint.FontMetrics fontMetrics = this.clockPaint.getFontMetrics();
        f2 -= (fontMetrics.ascent + fontMetrics.descent) / 2.0f;
        float f3 = this.clockPaint.getTextSize();
        this.clockPaint.setShader(null);
        this.clockPaint.setStrokeJoin(Paint.Join.ROUND);
        if (design.outline) {
            this.clockPaint.setStyle(Paint.Style.STROKE);
            this.clockPaint.setStrokeWidth(Math.max(2.0f, 0.03f * f3));
            this.clockPaint.clearShadowLayer();
            this.clockPaint.setColor(n);
            canvas.drawText(string2, f, f2, this.clockPaint);
            return;
        }
        if (design.stroke) {
            this.clockPaint.setStyle(Paint.Style.STROKE);
            this.clockPaint.setStrokeWidth(Math.max(1.5f, (float)design.strokeW * f3 / 150.0f));
            this.clockPaint.clearShadowLayer();
            this.clockPaint.setColor(design.strokeColor);
            canvas.drawText(string2, f, f2, this.clockPaint);
        }
        this.clockPaint.setStyle(Paint.Style.FILL);
        if (design.shadow) {
            this.clockPaint.setShadowLayer(0.03f * f3, 0.0f, 0.01f * f3, -1728053248);
        } else {
            this.clockPaint.clearShadowLayer();
        }
        if (design.gradText) {
            double d = Math.toRadians(design.gradAngle);
            float f4 = (float)Math.hypot(rectF.width(), rectF.height()) / 2.0f;
            float f5 = rectF.centerX();
            f3 = rectF.centerY();
            this.clockPaint.setShader((Shader)new LinearGradient(f5 - (float)Math.cos(d) * f4, f3 - (float)Math.sin(d) * f4, f5 + (float)Math.cos(d) * f4, f3 + (float)Math.sin(d) * f4, design.clockCol(), design.clockCol2(), Shader.TileMode.CLAMP));
        } else {
            this.clockPaint.setColor(n);
        }
        canvas.drawText(string2, f, f2, this.clockPaint);
        this.clockPaint.setShader(null);
    }

    private void hand(Canvas canvas, float f, float f2, float f3, float f4) {
        f3 = (float)Math.toRadians(f3);
        canvas.drawLine(f, f2, f + (float)Math.sin(f3) * f4, f2 - (float)Math.cos(f3) * f4, this.shapePaint);
    }

    private StaticLayout makeLayout(String string2, int n, float f, Typeface typeface, int n2, int n3) {
        TextPaint textPaint = new TextPaint(1);
        textPaint.setTypeface(typeface);
        textPaint.setTextSize(f);
        textPaint.setColor(n2);
        textPaint.setShadowLayer((float)n3 * 0.006f, 0.0f, (float)n3 * 0.003f, -1728053248);
        return StaticLayout.Builder.obtain((CharSequence)string2, (int)0, (int)string2.length(), (TextPaint)textPaint, (int)n).setAlignment(Layout.Alignment.ALIGN_CENTER).setMaxLines(6).setEllipsize(TextUtils.TruncateAt.END).build();
    }

    private void prepareClockPaint(Design design, float f) {
        this.clockPaint.setTypeface(Fonts.get(design.font));
        this.clockPaint.setLetterSpacing((float)design.letterSpacing / 100.0f);
        this.clockPaint.setTextSize(f);
    }

    private void prepareFormats(Design design) {
        Locale locale = Locale.getDefault();
        if (this.fH == null || this.fmt24 != design.use24 || !locale.equals(this.fmtLocale) || this.fmtDateIdx != design.dateFmt) {
            this.fmt24 = design.use24;
            this.fmtLocale = locale;
            this.fmtDateIdx = design.dateFmt;
            String string2 = design.use24 ? "HH" : "h";
            this.fH = new SimpleDateFormat(string2, locale);
            this.fM = new SimpleDateFormat("mm", locale);
            this.fDate = new SimpleDateFormat(Design.DATE_FORMATS[design.dateFmt], locale);
        }
    }

    private int tint(int n, int n2, float f) {
        if (n == 0) {
            n = n2;
        }
        return Color.argb((int)Math.max(0, Math.min(255, Math.round(f))), (int)Color.red((int)n), (int)Color.green((int)n), (int)Color.blue((int)n));
    }

    private void updateColorFilter(Design design) {
        if (design.hue == this.cmHue && design.sat == this.cmSat) {
            return;
        }
        this.cmHue = design.hue;
        this.cmSat = design.sat;
        if (design.hue == 0 && design.sat == 100) {
            this.colorFilter = null;
        } else {
            ColorMatrix colorMatrix = new ColorMatrix();
            colorMatrix.setSaturation((float)design.sat / 100.0f);
            double d = Math.toRadians(design.hue);
            float f = (float)Math.cos(d);
            float f2 = (float)Math.sin(d);
            colorMatrix.postConcat(new ColorMatrix(new float[]{f * 0.787f + 0.213f - f2 * 0.213f, 0.715f - f * 0.715f - f2 * 0.715f, 0.072f - f * 0.072f + f2 * 0.928f, 0.0f, 0.0f, 0.213f - f * 0.213f + 0.143f * f2, 0.285f * f + 0.715f + 0.14f * f2, 0.072f - f * 0.072f - 0.283f * f2, 0.0f, 0.0f, 0.213f - f * 0.213f - 0.787f * f2, 0.715f - f * 0.715f + 0.715f * f2, 0.928f * f + 0.072f + 0.072f * f2, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f}));
            this.colorFilter = new ColorMatrixColorFilter(colorMatrix);
        }
        this.bitmapPaint.setColorFilter((ColorFilter)this.colorFilter);
    }

    private void wrap(int n, int n2, int n3, boolean bl) {
        if (bl && this.py[n] > (float)(n3 + 20)) {
            this.py[n] = -20.0f;
            this.px[n] = this.rnd.nextFloat() * (float)n2;
        } else if (!bl && this.py[n] < -30.0f) {
            this.py[n] = n3 + 30;
            this.px[n] = this.rnd.nextFloat() * (float)n2;
        }
        if (this.px[n] < -30.0f) {
            this.px[n] = n2 + 10;
        }
        if (this.px[n] > (float)(n2 + 30)) {
            this.px[n] = -10.0f;
        }
    }

    void render(Canvas canvas, int n, int n2, Design design, Bitmap bitmap, Bitmap bitmap2, float f, float f2, long l, String string2, String string3, boolean bl) {
        float f3;
        float f4;
        this.hits.clear();
        this.prepareFormats(design);
        this.updateColorFilter(design);
        Date date = new Date(l);
        float f5 = design.parallax ? (float)n * 0.035f * ((float)design.parallaxStrength / 100.0f) : 0.0f;
        if (design.parallaxText) {
            f4 = n;
            f3 = n;
            f4 = f4 * f * 0.03f;
            f3 = f3 * f2 * 0.03f;
        } else {
            f4 = 0.0f;
            f3 = 0.0f;
        }
        if (design.bgMode == 0 && bitmap != null) {
            canvas.drawColor(-16777216);
            this.drawCover(canvas, bitmap, n, n2, f5, f5 * 0.5f * f, 0.5f * f5 * f2);
        } else {
            this.drawGradient(canvas, n, n2, design, l);
        }
        if (design.dim > 0) {
            canvas.drawColor(Color.argb((int)(design.dim * 255 / 100), (int)0, (int)0, (int)0));
        }
        this.drawParticles(canvas, n, n2, design, l, bl);
        if (design.stormy() && bl && ((l = l / 100L % 70L) == 0L || l == 3L)) {
            canvas.drawColor(Color.argb((int)110, (int)255, (int)255, (int)255));
        }
        if (design.clockShow && !design.clockOver) {
            this.drawClock(canvas, n, n2, design, date, f4, f3);
        }
        if (bitmap2 != null && design.bgMode == 0) {
            this.drawCover(canvas, bitmap2, n, n2, f5, f5 * -0.5f * f, -0.5f * f5 * f2);
        }
        if (design.clockShow && design.clockOver) {
            this.drawClock(canvas, n, n2, design, date, f4, f3);
        }
        this.drawElements(canvas, n, n2, design, date, string2, string3, f4, f3);
    }

    static final class Hit {
        final String id;
        final RectF r;

        Hit(String string2, RectF rectF) {
            this.id = string2;
            this.r = rectF;
        }
    }
}

