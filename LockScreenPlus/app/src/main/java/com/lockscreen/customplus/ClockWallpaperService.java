package com.lockscreen.customplus;

import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.service.wallpaper.WallpaperService;
import android.view.SurfaceHolder;
import com.lockscreen.customplus.ClockWallpaperService;
import com.lockscreen.customplus.Design;

/* loaded from: classes3.dex */
public class ClockWallpaperService extends WallpaperService {
    @Override // android.service.wallpaper.WallpaperService
    public WallpaperService.Engine onCreateEngine() {
        return new ClockEngine();
    }

    /* JADX INFO: Access modifiers changed from: private */
    class ClockEngine extends WallpaperService.Engine implements SensorEventListener, SharedPreferences.OnSharedPreferenceChangeListener {
        private Sensor accel;
        private float baseY;
        private Bitmap bg;
        private Bitmap cut;
        private Design design;
        private String fetchedText;
        private volatile boolean fetching;
        private volatile boolean fetchingWeather;
        private final Handler handler;
        private boolean haveBase;
        private long imgStamp;
        private long lastAttempt;
        private long lastWeatherAttempt;
        private boolean needReload;
        private final Renderer renderer;
        private boolean sensorOn;
        private SensorManager sensors;
        private SharedPreferences sp;
        private float targetX;
        private float targetY;
        private final Runnable tick;
        private float tiltX;
        private float tiltY;
        private boolean visible;
        // --- экран «на боку» и статистика по USB ---
        private Design landDesign;
        private boolean landOn = true;
        private boolean usbOn = true;
        private int landMode;     // 0 — обычно, 1 — телефон лежит верхом влево, 2 — верхом вправо
        private int pendingMode;
        private long pendingSince;
        private float sx;
        private float sy;
        private float sz;
        private boolean haveS;
        private Dashboard dash;
        private StatsHub.Snap snap;
        private long lastPoll;
        private boolean usbMode;

        private ClockEngine() {
            super();
            this.handler = new Handler(Looper.getMainLooper());
            this.renderer = new Renderer();
            this.design = new Design();
            this.needReload = true;
            this.imgStamp = -1L;
            this.fetchedText = "";
            this.tick = new Runnable() { // from class: com.lockscreen.customplus.ClockWallpaperService.ClockEngine.1
                @Override // java.lang.Runnable
                public void run() {
                    ClockEngine.this.draw();
                    ClockEngine.this.maybeFetch();
                    ClockEngine.this.maybeFetchWeather();
                    ClockEngine.this.scheduleNext();
                }
            };
        }

        @Override // android.service.wallpaper.WallpaperService.Engine
        public void onCreate(SurfaceHolder holder) {
            super.onCreate(holder);
            Fonts.init(ClockWallpaperService.this);
            this.sp = Store.prefs(ClockWallpaperService.this);
            this.sp.registerOnSharedPreferenceChangeListener(this);
            this.sensors = (SensorManager) ClockWallpaperService.this.getSystemService("sensor");
            this.accel = this.sensors != null ? this.sensors.getDefaultSensor(1) : null;
            this.dash = new Dashboard();
        }

        @Override // android.service.wallpaper.WallpaperService.Engine
        public void onDestroy() {
            this.visible = false;
            this.handler.removeCallbacks(this.tick);
            stopSensor();
            if (this.sp != null) {
                this.sp.unregisterOnSharedPreferenceChangeListener(this);
            }
            if (this.bg != null) {
                this.bg.recycle();
            }
            if (this.cut != null) {
                this.cut.recycle();
            }
            this.bg = null;
            this.cut = null;
            super.onDestroy();
        }

        @Override // android.service.wallpaper.WallpaperService.Engine
        public void onVisibilityChanged(boolean v) {
            this.visible = v;
            this.handler.removeCallbacks(this.tick);
            if (v) {
                if (this.needReload) {
                    reload();
                }
                updateSensor();
                this.handler.post(this.tick);
                return;
            }
            stopSensor();
        }

        @Override // android.service.wallpaper.WallpaperService.Engine
        public void onSurfaceChanged(SurfaceHolder holder, int format, int width, int height) {
            super.onSurfaceChanged(holder, format, width, height);
            draw();
        }

        @Override // android.service.wallpaper.WallpaperService.Engine
        public void onSurfaceDestroyed(SurfaceHolder holder) {
            this.visible = false;
            this.handler.removeCallbacks(this.tick);
            stopSensor();
            super.onSurfaceDestroyed(holder);
        }

        @Override // android.content.SharedPreferences.OnSharedPreferenceChangeListener
        public void onSharedPreferenceChanged(SharedPreferences s, String key) {
            this.needReload = true;
            if (this.visible) {
                this.handler.removeCallbacks(this.tick);
                this.handler.post(this.tick);
            }
        }

        private void updateSensor() {
            if (this.visible && (this.design.parallax || this.landOn) && this.accel != null) {
                if (!this.sensorOn) {
                    this.sensors.registerListener(this, this.accel, this.design.parallax ? 1 : 3);
                    this.sensorOn = true;
                    return;
                }
                return;
            }
            stopSensor();
        }

        private void stopSensor() {
            if (this.sensorOn && this.sensors != null) {
                this.sensors.unregisterListener(this);
            }
            this.sensorOn = false;
        }

        @Override // android.hardware.SensorEventListener
        public void onSensorChanged(SensorEvent e) {
            float ax = e.values[0];
            float ay = e.values[1];
            if (!this.haveBase) {
                this.baseY = ay;
                this.haveBase = true;
            }
            this.baseY += (ay - this.baseY) * 0.003f;
            this.targetX = clamp((-ax) / 6.0f);
            this.targetY = clamp((ay - this.baseY) / 6.0f);
            detectOrientation(ax, ay, e.values[2]);
        }

        private void wake() {
            if (this.visible) {
                this.handler.removeCallbacks(this.tick);
                this.handler.post(this.tick);
            }
        }

        // Телефон лёг на бок (гравитация вдоль длинной стороны экрана) — другой экран; с задержкой и запасом, чтобы не мигал
        private void detectOrientation(float ax, float ay, float az) {
            if (!this.landOn) {
                if (this.landMode != 0) {
                    this.landMode = 0;
                    wake();
                }
                return;
            }
            if (!this.haveS) {
                this.sx = ax;
                this.sy = ay;
                this.sz = az;
                this.haveS = true;
            } else {
                this.sx += (ax - this.sx) * 0.15f;
                this.sy += (ay - this.sy) * 0.15f;
                this.sz += (az - this.sz) * 0.15f;
            }
            float thr = this.landMode != 0 ? 4.5f : 6.5f;
            int cand = 0;
            if (Math.abs(this.sz) < 7.5f && Math.abs(this.sy) < 4.5f) {
                if (this.sx > thr && this.landMode != 2) {
                    cand = 1;
                } else if (this.sx < -thr && this.landMode != 1) {
                    cand = 2;
                }
            }
            long now = SystemClock.uptimeMillis();
            if (cand == this.landMode) {
                this.pendingMode = cand;
                return;
            }
            if (cand != this.pendingMode) {
                this.pendingMode = cand;
                this.pendingSince = now;
                return;
            }
            if (now - this.pendingSince >= 500) {
                this.landMode = cand;
                wake();
            }
        }

        @Override // android.hardware.SensorEventListener
        public void onAccuracyChanged(Sensor sensor, int accuracy) {
        }

        private float clamp(float v) {
            return Math.max(-1.0f, Math.min(1.0f, v));
        }

        private void reload() {
            this.needReload = false;
            this.design = Design.load(ClockWallpaperService.this, 0);
            this.landDesign = Design.load(ClockWallpaperService.this, 1);
            this.landOn = Store.landscapeOn(ClockWallpaperService.this);
            this.usbOn = Store.usbOn(ClockWallpaperService.this);
            if (!this.landOn) {
                this.landMode = 0;
            }
            this.fetchedText = this.sp.getString("fetchedText", "");
            long stamp = this.sp.getLong("imgStamp", 0L);
            if (stamp != this.imgStamp) {
                this.imgStamp = stamp;
                if (this.bg != null) {
                    this.bg.recycle();
                }
                if (this.cut != null) {
                    this.cut.recycle();
                }
                this.bg = null;
                this.cut = null;
                try {
                    if (Store.hasBackground(ClockWallpaperService.this)) {
                        this.bg = BitmapFactory.decodeFile(Store.bgFile(ClockWallpaperService.this).getPath());
                        if (Store.hasCut(ClockWallpaperService.this)) {
                            this.cut = BitmapFactory.decodeFile(Store.cutFile(ClockWallpaperService.this).getPath());
                        }
                    }
                } catch (Throwable th) {
                }
            }
            updateSensor();
        }

        /* JADX INFO: Access modifiers changed from: private */
        public void maybeFetch() {
            Design.Layer web;
            if (this.sp == null || this.fetching || (web = this.design.webLayer()) == null) {
                return;
            }
            final String url = web.url.trim();
            long now = System.currentTimeMillis();
            long every = Math.max(1, web.refreshMin) * 60000;
            if (now - this.sp.getLong("fetchedAt", 0L) < every || now - this.lastAttempt < 120000) {
                return;
            }
            this.lastAttempt = now;
            this.fetching = true;
            new Thread(new Runnable() { // from class: com.lockscreen.customplus.ClockWallpaperService$ClockEngine$$ExternalSyntheticLambda1
                @Override // java.lang.Runnable
                public final void run() {
                    ClockWallpaperService.ClockEngine.this.lambda$maybeFetch$0(url);
                }
            }).start();
        }

        /* JADX INFO: Access modifiers changed from: private */
        public /* synthetic */ void lambda$maybeFetch$0(String url) {
            try {
                String t = Store.fetchText(url);
                this.sp.edit().putString("fetchedText", t).putLong("fetchedAt", System.currentTimeMillis()).apply();
            } catch (Exception e) {
            } catch (Throwable th) {
                this.fetching = false;
                throw th;
            }
            this.fetching = false;
        }

        /* JADX INFO: Access modifiers changed from: private */
        public void maybeFetchWeather() {
            if (this.sp == null || this.fetchingWeather || !this.design.weatherAuto) {
                return;
            }
            final double lat = this.design.weatherLat;
            final double lon = this.design.weatherLon;
            if (Double.isNaN(lat) || Double.isNaN(lon)) {
                return;
            }
            long now = System.currentTimeMillis();
            if (now - this.sp.getLong("weatherAt", 0L) < 1800000 || now - this.lastWeatherAttempt < 120000) {
                return;
            }
            this.lastWeatherAttempt = now;
            this.fetchingWeather = true;
            new Thread(new Runnable() { // from class: com.lockscreen.customplus.ClockWallpaperService$ClockEngine$$ExternalSyntheticLambda0
                @Override // java.lang.Runnable
                public final void run() {
                    ClockWallpaperService.ClockEngine.this.lambda$maybeFetchWeather$1(lat, lon);
                }
            }).start();
        }

        /* JADX INFO: Access modifiers changed from: private */
        public /* synthetic */ void lambda$maybeFetchWeather$1(double lat, double lon) {
            try {
                Store.saveWeather(ClockWallpaperService.this, Store.fetchWeather(lat, lon));
            } catch (Exception e) {
            } catch (Throwable th) {
                this.fetchingWeather = false;
                throw th;
            }
            this.fetchingWeather = false;
        }

        /* JADX INFO: Access modifiers changed from: private */
        public void scheduleNext() {
            if (!this.visible) {
                return;
            }
            Design active = (this.landOn && this.landMode != 0 && this.landDesign != null) ? this.landDesign : this.design;
            long next;
            if (this.usbMode) {
                next = 250;
            } else if ((this.design.parallax && this.sensorOn && this.landMode == 0) || active.animated()) {
                next = 33;
            } else {
                next = (60000 - (System.currentTimeMillis() % 60000)) + 50;
            }
            if (this.usbOn && !this.usbMode) {
                next = Math.min(next, 3000); // следим, не подключили ли кабель
            }
            this.handler.postDelayed(this.tick, next);
        }

        /* JADX INFO: Access modifiers changed from: private */
        public void draw() {
            if (this.needReload) {
                reload();
            }
            long now = System.currentTimeMillis();
            if (this.usbOn) {
                if (this.snap == null || now - this.lastPoll >= 2000) {
                    this.lastPoll = now;
                    this.snap = StatsHub.poll(ClockWallpaperService.this);
                }
                this.usbMode = !isPreview() && this.snap != null && this.snap.usb;
            } else {
                this.usbMode = false;
            }
            SurfaceHolder sh = getSurfaceHolder();
            Canvas c = null;
            try {
                c = sh.lockCanvas();
                if (c != null) {
                    int w = c.getWidth();
                    int h = c.getHeight();
                    boolean wide = w > h;
                    // экран лежит на боку, а система держит обои вертикально — поворачиваем рисунок сами
                    boolean rotate = this.landOn && this.landMode != 0 && !wide;
                    boolean useLand = this.landOn && (rotate || wide);
                    if (rotate) {
                        c.save();
                        if (this.landMode == 1) {
                            c.translate(w, 0.0f);
                            c.rotate(90.0f);
                        } else {
                            c.translate(0.0f, h);
                            c.rotate(-90.0f);
                        }
                    }
                    int rw = rotate ? h : w;
                    int rh = rotate ? w : h;
                    if (this.usbMode && this.snap != null && this.dash != null) {
                        this.dash.render(c, rw, rh, this.snap, now);
                    } else if (useLand && this.landDesign != null) {
                        this.renderer.render(c, rw, rh, this.landDesign, this.bg, null, 0.0f, 0.0f, now, this.fetchedText, Store.batteryText(ClockWallpaperService.this), true);
                    } else {
                        this.tiltX += (this.targetX - this.tiltX) * 0.15f;
                        this.tiltY += (this.targetY - this.tiltY) * 0.15f;
                        this.renderer.render(c, w, h, this.design, this.bg, this.cut, this.tiltX, this.tiltY, now, this.fetchedText, Store.batteryText(ClockWallpaperService.this), true);
                    }
                    if (rotate) {
                        c.restore();
                    }
                }
            } catch (Exception e) {
            } finally {
                if (c != null) {
                    try {
                        sh.unlockCanvasAndPost(c);
                    } catch (Exception e2) {
                    }
                }
            }
        }
    }
}
