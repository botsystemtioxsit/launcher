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

        private ClockEngine() {
            super(ClockWallpaperService.this);
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
            if (this.visible && this.design.parallax && this.accel != null) {
                if (!this.sensorOn) {
                    this.sensors.registerListener(this, this.accel, 1);
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
        }

        @Override // android.hardware.SensorEventListener
        public void onAccuracyChanged(Sensor sensor, int accuracy) {
        }

        private float clamp(float v) {
            return Math.max(-1.0f, Math.min(1.0f, v));
        }

        private void reload() {
            this.needReload = false;
            this.design = Design.load(ClockWallpaperService.this);
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
            long now;
            if (this.visible) {
                if ((this.design.parallax && this.sensorOn) || this.design.animated()) {
                    now = 33;
                } else {
                    long now2 = System.currentTimeMillis();
                    now = (60000 - (now2 % 60000)) + 50;
                }
                this.handler.postDelayed(this.tick, now);
            }
        }

        /* JADX INFO: Access modifiers changed from: private */
        public void draw() {
            if (this.needReload) {
                reload();
            }
            SurfaceHolder sh = getSurfaceHolder();
            Canvas c = null;
            try {
                try {
                    c = sh.lockCanvas();
                    if (c != null) {
                        this.tiltX += (this.targetX - this.tiltX) * 0.15f;
                        this.tiltY += (this.targetY - this.tiltY) * 0.15f;
                        this.renderer.render(c, c.getWidth(), c.getHeight(), this.design, this.bg, this.cut, this.tiltX, this.tiltY, System.currentTimeMillis(), this.fetchedText, Store.batteryText(ClockWallpaperService.this), true);
                    }
                    if (c != null) {
                        sh.unlockCanvasAndPost(c);
                    }
                } catch (Exception e) {
                }
            } catch (Exception e2) {
                if (c != null) {
                    sh.unlockCanvasAndPost(c);
                }
            } catch (Throwable th) {
                if (c != null) {
                    try {
                        sh.unlockCanvasAndPost(c);
                        throw th;
                    } catch (Exception e3) {
                        throw th;
                    }
                }
                throw th;
            }
        }
    }
}
