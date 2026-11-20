package com.lockscreen.customplus;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.MotionEvent;
import android.view.View;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.Renderer;

/* loaded from: classes3.dex */
final class PreviewView extends View {
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
    private final Renderer renderer;
    private final Paint selPaint;
    String selected;
    boolean selectedCard;
    private float startAngle;
    private float startDist;
    private int startRot;
    private int startSize;

    interface Listener {
        void onMoved();

        void onSelect(String str);
    }

    PreviewView(Context c) {
        super(c);
        this.renderer = new Renderer();
        this.border = new Paint(1);
        this.selPaint = new Paint(1);
        this.clip = new Path();
        this.rect = new RectF();
        float d = c.getResources().getDisplayMetrics().density;
        this.border.setStyle(Paint.Style.STROKE);
        this.border.setStrokeWidth(d * 2.0f);
        this.selPaint.setStyle(Paint.Style.STROKE);
        this.selPaint.setStrokeWidth(2.0f * d);
        this.selPaint.setColor(-12676609);
        this.selPaint.setPathEffect(new DashPathEffect(new float[]{10.0f * d, 6.0f * d}, 0.0f));
    }

    @Override // android.view.View
    protected void onDraw(Canvas c) {
        Canvas canvas;
        int w;
        int w2 = getWidth();
        int h = getHeight();
        if (this.design == null || w2 == 0) {
            return;
        }
        if (h == 0) {
            return;
        }
        Images.ensure(getContext());
        int fullH = this.cropTop ? Math.round(w2 * ASPECT) : h;
        float radius = Math.min(w2, h) * (this.cropTop ? 0.1f : 0.075f);
        this.rect.set(0.0f, 0.0f, w2, h);
        this.clip.reset();
        this.clip.addRoundRect(this.rect, radius, radius, Path.Direction.CW);
        Design.Layer web = this.design.webLayer();
        String webText = web != null ? Store.prefs(getContext()).getString("fetchedText", "") : "";
        c.save();
        c.clipPath(this.clip);
        int w3 = w2;
        this.renderer.render(c, w2, fullH, this.design, Images.bg, Images.cut, 0.0f, 0.0f, System.currentTimeMillis(), webText, Store.batteryText(getContext()), this.interactive);
        c.restore();
        if (!this.interactive || this.selected == null) {
            canvas = c;
        } else {
            for (Renderer.Hit hit : this.renderer.hits) {
                if (hit.id.equals(this.selected)) {
                    RectF r = new RectF(hit.r);
                    r.inset((-w3) * 0.02f, (-w3) * 0.02f);
                    w = w3;
                    c.drawRoundRect(r, w3 * 0.03f, w3 * 0.03f, this.selPaint);
                } else {
                    w = w3;
                }
                w3 = w;
            }
            canvas = c;
        }
        this.border.setColor(this.selectedCard ? -12676609 : -12960958);
        this.border.setStrokeWidth(getResources().getDisplayMetrics().density * (this.selectedCard ? 3 : 2));
        canvas.drawPath(this.clip, this.border);
        if (!this.interactive || !this.design.animated()) {
            return;
        }
        postInvalidateOnAnimation();
    }

    @Override // android.view.View
    public boolean onTouchEvent(MotionEvent e) {
        if (!this.interactive) {
            return super.onTouchEvent(e);
        }
        if (this.design == null) {
            return false;
        }
        float w = getWidth();
        float h = getHeight();
        switch (e.getActionMasked()) {
            case 0:
                this.pinching = false;
                this.dragId = hit(e.getX(), e.getY());
                this.selected = this.dragId;
                if (this.listener != null) {
                    this.listener.onSelect(this.dragId);
                }
                invalidate();
                if (this.dragId == null) {
                    return false;
                }
                float[] p = this.design.pos(this.dragId);
                if (p == null) {
                    this.dragId = null;
                    return false;
                }
                this.grabDx = p[0] - (e.getX() / w);
                this.grabDy = p[1] - (e.getY() / h);
                if (getParent() != null) {
                    getParent().requestDisallowInterceptTouchEvent(true);
                }
                return true;
            case 1:
            case 3:
                if (this.dragId != null || this.pinching) {
                    finish();
                }
                this.dragId = null;
                this.pinching = false;
                return true;
            case 2:
                if (this.pinching && e.getPointerCount() >= 2) {
                    this.design.setSize(this.selected, Math.round((this.startSize * dist(e)) / this.startDist));
                    float delta = angle(e) - this.startAngle;
                    int rot = Math.round(this.startRot + delta);
                    while (rot > 180) {
                        rot -= 360;
                    }
                    while (rot < -180) {
                        rot += 360;
                    }
                    this.design.xf(this.selected).rot = rot;
                    invalidate();
                } else if (this.dragId != null && !this.pinching) {
                    float x = clamp((e.getX() / w) + this.grabDx, 0.04f, 0.96f);
                    float y = clamp((e.getY() / h) + this.grabDy, 0.03f, 0.97f);
                    this.design.setPos(this.dragId, x, y);
                    invalidate();
                }
                return true;
            case 4:
            default:
                return true;
            case 5:
                if (this.selected != null && e.getPointerCount() == 2 && this.design.xf(this.selected) != null) {
                    this.pinching = true;
                    this.startDist = Math.max(1.0f, dist(e));
                    this.startAngle = angle(e);
                    this.startSize = this.design.size(this.selected);
                    this.startRot = this.design.xf(this.selected).rot;
                }
                return true;
            case 6:
                if (this.pinching) {
                    finish();
                    this.pinching = false;
                    this.dragId = null;
                }
                return true;
        }
    }

    private void finish() {
        this.design.save(getContext());
        if (this.listener != null) {
            this.listener.onMoved();
        }
    }

    private static float dist(MotionEvent e) {
        float dx = e.getX(0) - e.getX(1);
        float dy = e.getY(0) - e.getY(1);
        return (float) Math.hypot(dx, dy);
    }

    private static float angle(MotionEvent e) {
        return (float) Math.toDegrees(Math.atan2(e.getY(1) - e.getY(0), e.getX(1) - e.getX(0)));
    }

    private String hit(float x, float y) {
        float slop = getWidth() * 0.03f;
        for (int i = this.renderer.hits.size() - 1; i >= 0; i--) {
            Renderer.Hit h = this.renderer.hits.get(i);
            RectF r = new RectF(h.r);
            r.inset(-slop, -slop);
            if (r.contains(x, y)) {
                return h.id;
            }
        }
        return null;
    }

    private static float clamp(float v, float lo, float hi) {
        return Math.max(lo, Math.min(hi, v));
    }
}
