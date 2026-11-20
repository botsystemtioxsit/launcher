/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  android.graphics.Bitmap
 *  android.graphics.Canvas
 *  android.graphics.DashPathEffect
 *  android.graphics.Paint
 *  android.graphics.Paint$Style
 *  android.graphics.Path
 *  android.graphics.Path$Direction
 *  android.graphics.PathEffect
 *  android.graphics.RectF
 *  android.view.MotionEvent
 *  android.view.View
 */
package com.lockscreen.customplus;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PathEffect;
import android.graphics.RectF;
import android.view.MotionEvent;
import android.view.View;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.Images;
import com.lockscreen.customplus.Renderer;
import com.lockscreen.customplus.Store;

final class PreviewView
extends View {
    static final float ASPECT = 2.1666667f;
    private final Paint border;
    private final Path clip;
    boolean cropTop;
    Design design;
    private String dragId;
    private float grabDx;
    private float grabDy;
    boolean interactive;
    Listener listener;
    private boolean pinching;
    private final RectF rect;
    private final Renderer renderer = new Renderer();
    private final Paint selPaint;
    String selected;
    boolean selectedCard;
    private float startAngle;
    private float startDist;
    private int startRot;
    private int startSize;

    PreviewView(Context context) {
        super(context);
        this.border = new Paint(1);
        this.selPaint = new Paint(1);
        this.clip = new Path();
        this.rect = new RectF();
        float f = context.getResources().getDisplayMetrics().density;
        this.border.setStyle(Paint.Style.STROKE);
        this.border.setStrokeWidth(f * 2.0f);
        this.selPaint.setStyle(Paint.Style.STROKE);
        this.selPaint.setStrokeWidth(2.0f * f);
        this.selPaint.setColor(-12676609);
        this.selPaint.setPathEffect((PathEffect)new DashPathEffect(new float[]{10.0f * f, 6.0f * f}, 0.0f));
    }

    private static float angle(MotionEvent motionEvent) {
        return (float)Math.toDegrees(Math.atan2(motionEvent.getY(1) - motionEvent.getY(0), motionEvent.getX(1) - motionEvent.getX(0)));
    }

    private static float clamp(float f, float f2, float f3) {
        return Math.max(f2, Math.min(f3, f));
    }

    private static float dist(MotionEvent motionEvent) {
        float f = motionEvent.getX(0);
        float f2 = motionEvent.getX(1);
        float f3 = motionEvent.getY(0);
        float f4 = motionEvent.getY(1);
        return (float)Math.hypot(f - f2, f3 - f4);
    }

    private void finish() {
        this.design.save(this.getContext());
        if (this.listener != null) {
            this.listener.onMoved();
        }
    }

    private String hit(float f, float f2) {
        float f3 = (float)this.getWidth() * 0.03f;
        for (int i = this.renderer.hits.size() - 1; i >= 0; --i) {
            Renderer.Hit hit = this.renderer.hits.get(i);
            RectF rectF = new RectF(hit.r);
            rectF.inset(-f3, -f3);
            if (!rectF.contains(f, f2)) continue;
            return hit.id;
        }
        return null;
    }

    protected void onDraw(Canvas canvas) {
        int n = this.getWidth();
        int n2 = this.getHeight();
        if (this.design != null && n != 0 && n2 != 0) {
            Images.ensure(this.getContext());
            int n3 = this.cropTop ? Math.round((float)n * 2.1666667f) : n2;
            float f = Math.min(n, n2);
            float f2 = this.cropTop ? 0.1f : 0.075f;
            f2 = f * f2;
            this.rect.set(0.0f, 0.0f, (float)n, (float)n2);
            this.clip.reset();
            this.clip.addRoundRect(this.rect, f2, f2, Path.Direction.CW);
            Design.Layer object3 = this.design.webLayer();
            Object object = "";
            if (object3 != null) {
                object = Store.prefs(this.getContext()).getString("fetchedText", "");
            }
            canvas.save();
            canvas.clipPath(this.clip);
            Renderer renderer = this.renderer;
            Design design = this.design;
            Bitmap bitmap = Images.bg;
            Bitmap bitmap2 = Images.cut;
            long l = System.currentTimeMillis();
            String string2 = Store.batteryText(this.getContext());
            boolean bl = this.interactive;
            n2 = n;
            renderer.render(canvas, n, n3, design, bitmap, bitmap2, 0.0f, 0.0f, l, (String)object, string2, bl);
            canvas.restore();
            if (this.interactive && this.selected != null) {
                for (Renderer.Hit hit : this.renderer.hits) {
                    if (!hit.id.equals(this.selected)) continue;
                    RectF rectF = new RectF(hit.r);
                    rectF.inset((float)(-n2) * 0.02f, (float)(-n2) * 0.02f);
                    canvas.drawRoundRect(rectF, (float)n2 * 0.03f, (float)n2 * 0.03f, this.selPaint);
                }
            }
            object = this.border;
            n3 = this.selectedCard ? -12676609 : -12960958;
            object.setColor(n3);
            object = this.border;
            f2 = this.getResources().getDisplayMetrics().density;
            n3 = this.selectedCard ? 3 : 2;
            object.setStrokeWidth(f2 * (float)n3);
            canvas.drawPath(this.clip, this.border);
            if (this.interactive && this.design.animated()) {
                this.postInvalidateOnAnimation();
            }
            return;
        }
    }

    public boolean onTouchEvent(MotionEvent motionEvent) {
        if (!this.interactive) {
            return super.onTouchEvent(motionEvent);
        }
        if (this.design == null) {
            return false;
        }
        float f = this.getWidth();
        float f2 = this.getHeight();
        switch (motionEvent.getActionMasked()) {
            default: {
                return true;
            }
            case 6: {
                if (this.pinching) {
                    this.finish();
                    this.pinching = false;
                    this.dragId = null;
                }
                return true;
            }
            case 5: {
                if (this.selected != null && motionEvent.getPointerCount() == 2 && this.design.xf(this.selected) != null) {
                    this.pinching = true;
                    this.startDist = Math.max(1.0f, PreviewView.dist(motionEvent));
                    this.startAngle = PreviewView.angle(motionEvent);
                    this.startSize = this.design.size(this.selected);
                    this.startRot = this.design.xf((String)this.selected).rot;
                }
                return true;
            }
            case 2: {
                if (this.pinching && motionEvent.getPointerCount() >= 2) {
                    int n;
                    this.design.setSize(this.selected, Math.round((float)this.startSize * PreviewView.dist(motionEvent) / this.startDist));
                    f2 = PreviewView.angle(motionEvent);
                    f = this.startAngle;
                    int n2 = Math.round((float)this.startRot + (f2 - f));
                    while (true) {
                        if (n2 <= 180) break;
                        n2 -= 360;
                    }
                    for (n = n2; n < -180; n += 360) {
                    }
                    this.design.xf((String)this.selected).rot = n;
                    this.invalidate();
                } else if (this.dragId != null && !this.pinching) {
                    f = PreviewView.clamp(motionEvent.getX() / f + this.grabDx, 0.04f, 0.96f);
                    f2 = PreviewView.clamp(motionEvent.getY() / f2 + this.grabDy, 0.03f, 0.97f);
                    this.design.setPos(this.dragId, f, f2);
                    this.invalidate();
                }
                return true;
            }
            case 1: 
            case 3: {
                if (this.dragId != null || this.pinching) {
                    this.finish();
                }
                this.dragId = null;
                this.pinching = false;
                return true;
            }
            case 0: 
        }
        this.pinching = false;
        this.selected = this.dragId = this.hit(motionEvent.getX(), motionEvent.getY());
        if (this.listener != null) {
            this.listener.onSelect(this.dragId);
        }
        this.invalidate();
        if (this.dragId == null) {
            return false;
        }
        float[] fArray = this.design.pos(this.dragId);
        if (fArray == null) {
            this.dragId = null;
            return false;
        }
        this.grabDx = fArray[0] - motionEvent.getX() / f;
        this.grabDy = fArray[1] - motionEvent.getY() / f2;
        if (this.getParent() != null) {
            this.getParent().requestDisallowInterceptTouchEvent(true);
        }
        return true;
    }

    static interface Listener {
        public void onMoved();

        public void onSelect(String var1);
    }
}

